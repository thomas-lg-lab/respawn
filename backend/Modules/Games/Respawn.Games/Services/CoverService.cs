using System.Runtime.InteropServices;
using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The covers: a game's, asked of the sources of its store entries first, then of the game
/// database when none has one, and, on an instance that keeps images, kept and served from there; and
/// where the stores say they are, asked on a schedule of its own and kept beside each store entry.</summary>
public sealed class CoverService(
    ICatalogue catalogue,
    ILinkedAccountStore linkedAccounts,
    IEnumerable<ICoverSource> sources,
    IGameDataSource gameData,
    CoverGate gate,
    IImageStore images,
    ImageGate imageGate,
    TimeProvider clock) : ICoverService
{
    public async Task<CoverAnswer> GetCoverAsync(Guid gameId, string? source, CancellationToken ct)
    {
        var entries = await catalogue.FindCoverEntriesAsync(gameId, ct)
            ?? throw new GamesException(GamesError.UnknownGame);

        // A source's own cover, for who chose it; none from it, or a source that does not answer with
        // nothing of it kept, and the game's own is the answer. That one is never settled: it is in
        // the place of the source's, which a browser is to ask for again once the source has it.
        if (source is not null && (source == gameData.Source || sources.Any(store => store.Source == source)))
        {
            return await ChosenCoverAsync(gameId, ImageSlots.Of(source), entries, ct) is { Cover: not null } chosen
                ? chosen
                : (await GetCoverAsync(gameId, ImageSlots.Own, entries, ct)) with { Settled = false };
        }

        return await GetCoverAsync(gameId, ImageSlots.Own, entries, ct);
    }

    /// <summary>The cover of one source's slot; null when that source could not be read and nothing of
    /// it is kept.</summary>
    private async Task<CoverAnswer?> ChosenCoverAsync(Guid gameId, string slot, IReadOnlyList<CoverEntry> entries, CancellationToken ct)
    {
        try
        {
            return await GetCoverAsync(gameId, slot, entries, ct);
        }
        catch (SourceException)
        {
            return null;
        }
    }

    /// <summary>The cover of a slot: the game's own, or one source's.</summary>
    private async Task<CoverAnswer> GetCoverAsync(Guid gameId, string slot, IReadOnlyList<CoverEntry> entries, CancellationToken ct)
    {
        if (!images.Keeps)
        {
            var (fetchedFrom, said) = Asked(slot, entries, await catalogue.FindGameDataCoverAsync(gameId, ct), keptFromStore: false);
            var passed = await FetchAsync(fetchedFrom, said, ct);
            return new CoverAnswer(passed.Cover is { } cover ? Passed(cover, null) : null, passed.Settled);
        }

        // One fetch per game at a time: who waited finds the image the first one kept.
        await using var turn = await imageGate.EnterAsync(gameId, ct);
        var now = clock.GetUtcNow();
        var kept = await catalogue.FindImageAsync(gameId, ImageKinds.Cover, slot, ct);
        var keptFromStore = kept is not null && kept.Name.Source != gameData.Source;

        // A store's kept image stands on the stores' word alone: what the game database says is asked
        // only for its own image, or for a fetch.
        if (kept is not null && keptFromStore && IsStoreImageCurrent(kept, entries, now) && await OpenAsync(kept, ct) is { } fromStore)
        {
            return new CoverAnswer(fromStore, true);
        }

        // The game database's own slot does not wait for the stores: their covers are not its.
        var data = await catalogue.FindGameDataCoverAsync(gameId, ct);
        if (kept is not null
            && !keptFromStore
            && IsGameDataImageCurrent(kept, slot == ImageSlots.Own ? entries : [], data, now)
            && await OpenAsync(kept, ct) is { } current)
        {
            return new CoverAnswer(current, true);
        }

        Fetched fetched;
        try
        {
            var (from, said) = Asked(slot, entries, data, keptFromStore);
            fetched = await FetchAsync(from, said, ct);
        }
        catch (SourceException) when (kept is not null)
        {
            // A source that does not answer leaves the kept image as it was: still the game's cover,
            // for now.
            if (await OpenAsync(kept, ct) is { } stale)
            {
                return new CoverAnswer(stale, false);
            }

            throw;
        }

        if (fetched.Cover is { } found)
        {
            // Kept only once its source's word stands: a store that said where its cover is, or the game
            // database's cover once the stores said they have none. What was found before may not be
            // the image the game will show.
            if (fetched.Keep
                && ImageName.Of(ImageKinds.Cover, fetched.Source!, fetched.Key!, found) is { } name
                && await KeepAsync(gameId, slot, new KeptImage(name, now), found, ct))
            {
                return new CoverAnswer(Passed(found, name.Tag), true);
            }

            return new CoverAnswer(Passed(found, null), fetched.Settled);
        }

        // No source has one any more: a game whose store page is gone keeps the image it had, the kept
        // file being its only copy now.
        if (kept is not null && await OpenAsync(kept, ct) is { } only)
        {
            if (fetched.Settled)
            {
                await catalogue.CheckedImageAsync(gameId, ImageKinds.Cover, slot, now, ct);
            }

            return new CoverAnswer(only, fetched.Settled);
        }

        return new CoverAnswer(null, fetched.Settled);
    }

    /// <summary>What a slot's cover is asked of: for the game's own, every store then the game database,
    /// except that a store's image the store no longer has stays the game's (it is the art made for a
    /// library, which the game database's never replaces); for a store's, that store's entries alone;
    /// for the game database's, it alone.</summary>
    private (IReadOnlyList<CoverEntry> Entries, GameDataCover Data) Asked(
        string slot, IReadOnlyList<CoverEntry> entries, GameDataCover data, bool keptFromStore) =>
        slot == ImageSlots.Own ? (entries, keptFromStore ? data with { ImageId = null } : data)
        : slot == ImageSlots.Of(gameData.Source) ? ([], data)
        : ([.. entries.Where(entry => ImageSlots.Of(entry.Source) == slot)], new GameDataCover(null, Asked: true));

    /// <summary>Whether a store's kept image still stands: it was asked for less than
    /// <see cref="CoverSchedule.AskAgainAfter"/> ago, after its store last said where the cover is, a
    /// store entry of its source still being the game's.</summary>
    private static bool IsStoreImageCurrent(KeptImage kept, IReadOnlyList<CoverEntry> entries, DateTimeOffset now) =>
        kept.CheckedAt > now - CoverSchedule.AskAgainAfter
        && entries.Any(entry => entry.Source == kept.Name.Source && entry.AskedAt is { } asked && kept.CheckedAt >= asked);

    /// <summary>Whether the game database's kept image still stands: it was asked for less than
    /// <see cref="CoverSchedule.AskAgainAfter"/> ago; no store with covers said anything since, which
    /// may be a cover of its own, the one a game shows first; and it is still the cover the database
    /// gives the game, or the database said nothing since it was last looked for. A database that now
    /// gives none, or one its host does not have, leaves it standing once looked for, as a store's page
    /// that is gone does.</summary>
    private bool IsGameDataImageCurrent(KeptImage kept, IReadOnlyList<CoverEntry> entries, GameDataCover data, DateTimeOffset now) =>
        kept.CheckedAt > now - CoverSchedule.AskAgainAfter
        && WithCovers(entries).All(entry => entry.AskedAt is { } asked && kept.CheckedAt >= asked)
        && ((data.ImageId is { } image && kept.Name.Key.StartsWith(image + "-", StringComparison.Ordinal))
            || !(data.SaidAt > kept.CheckedAt));

    /// <summary>The entries of the stores that give covers, in the order they are asked: the same entry
    /// first every time, so that a game owned in two stores keeps one cover.</summary>
    private IEnumerable<CoverEntry> WithCovers(IReadOnlyList<CoverEntry> entries) =>
        entries
            .Where(entry => sources.Any(source => source.Source == entry.Source))
            .OrderBy(entry => entry.Source, StringComparer.Ordinal)
            .ThenBy(entry => entry.StoreEntryId);

    /// <summary>Writes the image and makes it the one the game shows, in its file's turn, then removes the
    /// file it showed before once no game shows it, in that file's turn: a file another game is being
    /// given is never removed under it. False when the image could not be written.</summary>
    private async Task<bool> KeepAsync(Guid gameId, string slot, KeptImage image, Cover cover, CancellationToken ct)
    {
        ImageName? replaced;
        await using (await imageGate.EnterAsync(image.Name, ct))
        {
            if (!await images.WriteAsync(image.Name, cover.Bytes, ct))
            {
                return false;
            }

            try
            {
                replaced = await catalogue.KeepImageAsync(gameId, slot, image, ct);
            }
            catch
            {
                // A request cut short or a database that failed leaves a file no game may show: it goes
                // now, or it would stay for good.
                await RemoveUnshownQuietlyAsync(image.Name);
                throw;
            }
        }

        if (replaced is not null)
        {
            // Not cut short by the request: the old file would stay for good.
            await using (await imageGate.EnterAsync(replaced, CancellationToken.None))
            {
                await RemoveUnshownAsync(replaced);
            }
        }

        return true;
    }

    /// <summary>Removes an image's file once no game shows it; asked in the file's turn.</summary>
    private async Task RemoveUnshownAsync(ImageName image)
    {
        if (!await catalogue.IsImageShownAsync(image, CancellationToken.None))
        {
            images.Delete(image);
        }
    }

    /// <summary>The same, after a failure: one that cannot be made now leaves the file, and the first
    /// failure is the one said.</summary>
    private async Task RemoveUnshownQuietlyAsync(ImageName image)
    {
        try
        {
            await RemoveUnshownAsync(image);
        }
        catch (Exception)
        {
        }
    }

    private async Task<CoverImage?> OpenAsync(KeptImage kept, CancellationToken ct) =>
        await images.OpenAsync(kept.Name, ct) is { } file ? new CoverImage(kept.Name.ContentType, file, kept.Name.Tag) : null;

    /// <summary>An image a source just gave, sent from memory: the source's own array when the image
    /// is the whole of it, which it is, with no second copy.</summary>
    private static CoverImage Passed(Cover cover, string? tag)
    {
        var bytes = MemoryMarshal.TryGetArray(cover.Bytes, out var held) && held.Array is { } array
            ? new MemoryStream(array, held.Offset, held.Count, writable: false)
            : new MemoryStream(cover.Bytes.ToArray(), writable: false);
        return new CoverImage(cover.ContentType, bytes, tag);
    }

    /// <summary>What the sources of a game's cover give.</summary>
    /// <param name="Cover">The first cover found; null when none was.</param>
    /// <param name="Source">Who gave it: a store, or the game database.</param>
    /// <param name="Key">What names it in that source: the game's id in the store, or the database's
    /// image id.</param>
    /// <param name="Keep">Whether it may be kept: its source's word stands.</param>
    /// <param name="Settled">Whether the answer stands on the sources' word (<see cref="CoverAnswer.Settled"/>).</param>
    private sealed record Fetched(Cover? Cover, string? Source, string? Key, bool Keep, bool Settled);

    /// <summary>The cover of the first store entry whose source has one; when none has, the one the game
    /// database gives the game. The stores' come first: their art is made for a library.</summary>
    /// <exception cref="SourceException">None did, and a source could not be read.</exception>
    private async Task<Fetched> FetchAsync(IReadOnlyList<CoverEntry> entries, GameDataCover data, CancellationToken ct)
    {
        SourceException? trouble = null;
        var settled = true;
        foreach (var entry in WithCovers(entries))
        {
            var source = sources.First(source => source.Source == entry.Source);

            try
            {
                if (await source.ReadCoverAsync(entry.ExternalId, entry.CoverPath, ct) is { } cover)
                {
                    return new Fetched(cover, entry.Source, entry.ExternalId, entry.Asked, entry.Asked);
                }

                settled &= entry.Asked;
            }
            catch (SourceException ex)
            {
                // Another entry may still give one.
                trouble ??= ex;
            }
        }

        // No store gave one: the game database's. Kept only when the stores said they have none; while
        // a store was not asked yet, or did not answer, it may still have one, and the database's is
        // passed on for now.
        if (data.ImageId is { } image)
        {
            try
            {
                if (await gameData.ReadCoverAsync(image, ct) is { } cover)
                {
                    var stands = trouble is null && settled;
                    return new Fetched(cover, gameData.Source, image, stands, stands);
                }
            }
            catch (SourceException ex)
            {
                trouble ??= ex;
            }
        }

        // A game the database was not asked about yet may get a cover from it; an instance with no
        // access to it never asks.
        settled &= data.Asked || !gameData.HasAccess;

        // A source that did not answer is not a game without a cover.
        return trouble is null ? new Fetched(null, null, null, false, settled) : throw trouble;
    }

    public async Task<IReadOnlyList<Guid>> GetDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = (await catalogue.ListAccountsWithCoverPathsDueAsync(now - CoverSchedule.AskAgainAfter, ct)).ToHashSet();
        if (due.Count == 0)
        {
            return [];
        }

        var readable = sources.Select(source => source.Source).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. (await linkedAccounts.ListAllAsync(ct))
                .Where(account => due.Contains(account.Id)
                    && readable.Contains(account.Source)
                    && !(gate.TriedAt(account.Id) > now - CoverSchedule.RetryAfter))
                .OrderBy(account => account.LinkedAt)
                .ThenBy(account => account.Id)
                .Select(account => account.Id),
        ];
    }

    public async Task<CoverPathsRead?> ReadCoverPathsAsync(Guid linkedAccountId, CancellationToken ct)
    {
        if (await linkedAccounts.FindByIdAsync(linkedAccountId, ct) is not { } account
            || sources.FirstOrDefault(source => source.Source == account.Source) is not { } source
            || !gate.TryEnter(account.Id))
        {
            return null;
        }

        var learnt = 0;
        try
        {
            var now = clock.GetUtcNow();
            if (gate.TriedAt(account.Id) > now - CoverSchedule.RetryAfter)
            {
                return null;
            }

            // Whatever comes of it: a store that fails, or leaves games out, is not asked again at
            // every turn of the schedule.
            gate.Tried(account.Id, now);
            var due = await catalogue.ListCoverPathsDueAsync(account.Id, now - CoverSchedule.AskAgainAfter, ct);
            foreach (var some in due.Chunk(Math.Max(1, source.PathsPerRequest)))
            {
                var said = await source.ReadCoverPathsAsync([.. some.Select(entry => entry.ExternalId)], ct);
                if (said.Count == 0)
                {
                    // An answer gone empty is not a batch of games without a cover: nothing is kept
                    // of it, and it is said.
                    throw new SourceException(SourceError.Unreadable, "the store's answer names none of the games it was asked about");
                }

                // Kept batch by batch: a store that stops answering half-way does not cost what it
                // said. A game it says it has no cover of is kept as such; so is one that an answer
                // naming others leaves out, which the store then does not know.
                await catalogue.SetCoverPathsAsync(
                    [
                        .. some.Select(entry => new CoverPath(
                            entry.StoreEntryId,
                            said.GetValueOrDefault(entry.ExternalId) is { Length: > 0 and <= Limits.CoverPath } path ? path : null)),
                    ],
                    now,
                    ct);
                learnt += some.Length;
            }

            return new CoverPathsRead(learnt);
        }
        catch (SourceException ex)
        {
            return new CoverPathsRead(learnt, ex.Error, ex.Message is { Length: > Limits.SyncDetail } detail ? detail[..Limits.SyncDetail] : ex.Message);
        }
        finally
        {
            gate.Leave(account.Id);
        }
    }
}
