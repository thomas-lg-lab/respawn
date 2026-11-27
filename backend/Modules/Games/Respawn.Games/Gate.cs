namespace Respawn.Games;

/// <summary>What is on its way for the linked accounts, in this process, and when each thing was last
/// tried: a linked account is worked on by one at a time, and what was tried is not tried again at once.
/// Each use has a gate of its own, one for the whole app, which runs as one replica; a restart forgets
/// it.</summary>
/// <typeparam name="TTry">What a try is remembered by: the linked account, or the account and why.</typeparam>
public abstract class Gate<TTry>
    where TTry : notnull
{
    private readonly HashSet<Guid> _running = [];
    private readonly Dictionary<TTry, DateTimeOffset> _tried = [];

    /// <summary>Takes the account; false when it is taken already.</summary>
    public bool TryEnter(Guid linkedAccountId)
    {
        lock (_running)
        {
            return _running.Add(linkedAccountId);
        }
    }

    public void Leave(Guid linkedAccountId)
    {
        lock (_running)
        {
            _running.Remove(linkedAccountId);
        }
    }

    /// <summary>Whether something is on its way for that account in this process.</summary>
    public bool IsRunning(Guid linkedAccountId)
    {
        lock (_running)
        {
            return _running.Contains(linkedAccountId);
        }
    }

    /// <summary>When that was last tried, since the app started; null when it was not, or was forgotten.</summary>
    public DateTimeOffset? TriedAt(TTry tried)
    {
        lock (_running)
        {
            return _tried.TryGetValue(tried, out var at) ? at : null;
        }
    }

    public void Tried(TTry tried, DateTimeOffset at)
    {
        lock (_running)
        {
            _tried[tried] = at;
        }
    }

    /// <summary>Forgets when that was last tried: something kept says it from now on.</summary>
    public void Forget(TTry tried)
    {
        lock (_running)
        {
            _tried.Remove(tried);
        }
    }
}
