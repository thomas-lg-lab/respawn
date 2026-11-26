using System.Runtime.InteropServices;
using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>The covers: a game's, asked of the sources of its store entries and, on an instance that keeps
/// images, kept and served from there; and where the stores say they are, asked on a schedule of its own
/// and kept beside each store entry.</summary>
public sealed class CoverService(
    ICatalogue catalogue,
    ILinkedAccountStore linkedAccounts,
    IEnumerable<ICoverSource> sources,
    CoverGate gate,
    IImageStore images,
    ImageGate imageGate,
    TimeProvider clock) : ICoverService
{
    public async Task<CoverAnswer> GetCoverAsync(Guid gameId, CancellationToken ct)
    {
        var entries = await catalogue.FindCoverEntriesAsync(gameId, ct)
            ?? throw new GamesException(GamesError.UnknownGame);
        if (!images.Keeps)
        {
            var (cover, _, settled) = await FetchAsync(entries, ct);
            return new CoverAnswer(cover is null ? null : Passed(cover, null), settled);
        }

        // One fetch per game at a time: who waited finds the image the first one kept.
        await using var turn = await imageGate.EnterAsync(gameId, ct);
        var now = clock.GetUtcNow();
        var kept = await catalogue.FindImageAsync(gameId, ImageKinds.Cover, ct);
        if (kept is not null && IsCurrent(kept, entries, now) && await OpenAsync(kept, ct) is { } current)
        {
            return new CoverAnswer(current, true);
        }

        Fetched fetched;
        try
        {
            fetched = await FetchAsync(entries, ct);
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
            // Kept only once the store said where its cover is: what was found before may not be the
            // right image.
            if (fetched.From is { Asked: true } from
                && ImageName.Of(ImageKinds.Cover, from.Source, from.ExternalId, found) is { } name
                && await KeepAsync(gameId, new KeptImage(name, now), found, ct))
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
                await catalogue.CheckedImageAsync(gameId, ImageKinds.Cover, now, ct);
            }

            return new CoverAnswer(only, fetched.Settled);
        }

        return new CoverAnswer(null, fetched.Settled);
    }

    /// <summary>Whether the kept image still stands: its source was asked for it after its store last
    /// said where the cover is, and less than <see cref="CoverSchedule.AskAgainAfter"/> ago. A store
    /// entry of its source must still be the game's.</summary>
    private static bool IsCurrent(KeptImage kept, IReadOnlyList<CoverEntry> entries, DateTimeOffset now) =>
        kept.CheckedAt > now - CoverSchedule.AskAgainAfter
        && entries.Any(entry => entry.Source == kept.Name.Source && entry.AskedAt is { } asked && kept.CheckedAt >= asked);

    /// <summary>Writes the image and makes it the one the game shows, in its file's turn, then removes the
    /// file it showed before once no game shows it, in that file's turn: a file another game is being
    /// given is never removed under it. False when the image could not be written.</summary>
    private async Task<bool> KeepAsync(Guid gameId, KeptImage image, Cover cover, CancellationToken ct)
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
                replaced = await catalogue.KeepImageAsync(gameId, image, ct);
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

    /// <summary>What the sources of a game's store entries give.</summary>
    /// <param name="Cover">The first cover found; null when none was.</param>
    /// <param name="From">The entry it was found for.</param>
    /// <param name="Settled">Whether the answer stands on the stores' word (<see cref="CoverAnswer.Settled"/>).</param>
    private sealed record Fetched(Cover? Cover, CoverEntry? From, bool Settled);

    /// <summary>The cover of the first store entry whose source has one.</summary>
    /// <exception cref="SourceException">None did, and a source could not be read.</exception>
    private async Task<Fetched> FetchAsync(IReadOnlyList<CoverEntry> entries, CancellationToken ct)
    {
        // The same entry first every time, so that a game owned in two stores keeps one cover.
        SourceException? trouble = null;
        var settled = true;
        foreach (var entry in entries.OrderBy(entry => entry.Source, StringComparer.Ordinal).ThenBy(entry => entry.StoreEntryId))
        {
            if (sources.FirstOrDefault(source => source.Source == entry.Source) is not { } source)
            {
                continue;
            }

            try
            {
                if (await source.ReadCoverAsync(entry.ExternalId, entry.CoverPath, ct) is { } cover)
                {
                    return new Fetched(cover, entry, entry.Asked);
                }

                settled &= entry.Asked;
            }
            catch (SourceException ex)
            {
                // Another entry may still give one.
                trouble ??= ex;
            }
        }

        // A source that did not answer is not a game without a cover.
        return trouble is null ? new Fetched(null, null, settled) : throw trouble;
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
