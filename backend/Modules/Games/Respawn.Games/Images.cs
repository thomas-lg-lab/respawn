using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Respawn.Games;

// The images an instance keeps of its games, when it is given a data directory: fetched from a source
// the first time one is asked for, written once, and served from there to every client afterwards.
// A file is named by what it is (its kind), where it came from (its source) and a key of that source,
// never by the game: the database says which image each game shows.

/// <summary>The kinds of image a game has.</summary>
public static class ImageKinds
{
    /// <summary>The portrait image of a game, as a library shows it.</summary>
    public const string Cover = "cover";
}

/// <summary>Where a game keeps an image of a kind: its own, the one it shows when nobody chose, and one
/// per source asked for by name, for the owners who chose that source's. Two slots may show one file.</summary>
public static class ImageSlots
{
    /// <summary>The game's own image: the first source's that has one, in the order they are looked for.</summary>
    public const string Own = "";

    /// <summary>The slot of the image of one source, asked for by name.</summary>
    public static string Of(string source) => source;
}

/// <summary>Where a kept image is: its kind, its source, and its key in that source, with its media
/// type. Two images that differ have different names; a refreshed image that changed gets a new
/// one.</summary>
public sealed partial record ImageName(string Kind, string Source, string Key, string ContentType)
{
    /// <summary>What names the image and no other, for a browser to ask whether it changed.</summary>
    public string Tag => $"{Source}-{Key}";

    /// <summary>The name of an image a source gave for a game: its id in the source and the start of
    /// the image's SHA-256. The same bytes keep the same name, whatever address they came from; other
    /// bytes, a new one. Null when the id cannot be part of a file's name.</summary>
    public static ImageName? Of(string kind, string source, string externalId, Cover cover)
    {
        var key = $"{externalId}-{Convert.ToHexStringLower(SHA256.HashData(cover.Bytes.Span))[..16]}";
        return IsPart(kind) && IsPart(source) && IsPart(key) ? new ImageName(kind, source, key, cover.ContentType) : null;
    }

    /// <summary>Whether this can be one part of a file's name: letters, digits, dashes and
    /// underscores, and not too long. Nothing else goes in a path.</summary>
    public static bool IsPart(string part) => PartShape.IsMatch(part);

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,100}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PartShape { get; }
}

/// <summary>The image a game shows for a kind, as the database keeps it.</summary>
/// <param name="Name">Where it is.</param>
/// <param name="CheckedAt">When its source was last asked for it.</param>
public sealed record KeptImage(ImageName Name, DateTimeOffset CheckedAt);

/// <summary>Turns in this process. One fetch of a game's images at a time: two screens asking for the
/// same cover at once cost its source one request, the second finding the first one's file. And one
/// change of an image's file at a time, whichever games show it: a file is made one of a game's and
/// removed once no game shows it, never both at once.</summary>
public sealed class ImageGate
{
    private readonly Dictionary<object, (SemaphoreSlim Turn, int Waiting)> _turns = [];

    /// <summary>Waits for the game's turn; disposing of what is returned ends it.</summary>
    public Task<IAsyncDisposable> EnterAsync(Guid gameId, CancellationToken ct) => EnterAsync((object)gameId, ct);

    /// <summary>Waits for the image's turn, whichever game asks; disposing of what is returned ends it.
    /// Taken while a game's turn is held, never the other way round.</summary>
    public Task<IAsyncDisposable> EnterAsync(ImageName image, CancellationToken ct) => EnterAsync((object)image, ct);

    private async Task<IAsyncDisposable> EnterAsync(object key, CancellationToken ct)
    {
        SemaphoreSlim turn;
        lock (_turns)
        {
            var (held, waiting) = _turns.TryGetValue(key, out var known) ? known : (new SemaphoreSlim(1, 1), 0);
            _turns[key] = (held, waiting + 1);
            turn = held;
        }

        try
        {
            await turn.WaitAsync(ct);
        }
        catch
        {
            Leave(key);
            throw;
        }

        return new Turn(this, key, turn);
    }

    private void Leave(object key)
    {
        lock (_turns)
        {
            var (turn, waiting) = _turns[key];
            if (waiting == 1)
            {
                _turns.Remove(key);
                turn.Dispose();
            }
            else
            {
                _turns[key] = (turn, waiting - 1);
            }
        }
    }

    private sealed class Turn(ImageGate gate, object key, SemaphoreSlim turn) : IAsyncDisposable
    {
        private int _ended;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                turn.Release();
                gate.Leave(key);
            }

            return ValueTask.CompletedTask;
        }
    }
}
