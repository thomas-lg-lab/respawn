using Microsoft.EntityFrameworkCore;
using Respawn.Accounts;
using Respawn.Accounts.Ports;
using Respawn.Accounts.Infrastructure.Identity;
using Respawn.Accounts.Infrastructure.Oidc;

namespace Respawn.Accounts.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(AccountsDbContext db) : IUnitOfWork
{
    /// <summary>The advisory lock every registration takes; any number, as long as it is always this one.</summary>
    private const long RegistrationLock = 7305;

    public Task<T> RunAsync<T>(bool serialiseRegistrations, Func<Task<T>> work, CancellationToken ct) =>
        // The context retries on a lost connection (AccountsDbContext.Configure), which EF only allows for a
        // transaction when the whole of it can be run again: hence the delegate.
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // nothing of an attempt that failed half-way
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (serialiseRegistrations)
            {
                // Released by PostgreSQL itself when the transaction ends, committed or not.
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({RegistrationLock})", ct);
            }

            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        });
}

internal sealed class EfGroupDirectory(AccountsDbContext db, TimeProvider clock) : IGroupDirectory
{
    public async Task EnsureEveryoneInDefaultGroupAsync(string defaultGroupName, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        var now = clock.GetUtcNow();

        // Two statements that change nothing when there is nothing to do, and that the database keeps
        // right under concurrency: the unique index on the default group and the key of a membership
        // make a second writer wait for the first, then skip. The same choice of owner as the
        // migration that created groups: the first administrator, else the oldest account.
        // Memberships are written in the order of the accounts' ids: two repairs at once then take
        // their rows in the same order, and neither can hold one the other waits for.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO accounts.groups (id, name, owner_id, is_default, created_at)
            SELECT {id}, {defaultGroupName}, u.id, TRUE, {now}
            FROM accounts.users u
            WHERE NOT EXISTS (SELECT 1 FROM accounts.groups WHERE is_default)
            ORDER BY u.is_admin DESC, u.created_at, u.id
            LIMIT 1
            ON CONFLICT DO NOTHING
            """,
            ct);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO accounts.group_memberships (group_id, user_id, joined_at)
            SELECT g.id, u.id, {now}
            FROM accounts.groups g CROSS JOIN accounts.users u
            WHERE g.is_default
              AND NOT EXISTS (SELECT 1 FROM accounts.group_memberships m WHERE m.group_id = g.id AND m.user_id = u.id)
            ORDER BY u.id
            ON CONFLICT DO NOTHING
            """,
            ct);
    }
}

internal sealed class EfInstanceSettings(AccountsDbContext db) : IInstanceSettings
{
    public async Task<bool> GetFlagAsync(string key, bool fallback, CancellationToken ct)
    {
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting is null ? fallback : setting.Value == "true";
    }
}

internal sealed class EfSessionStore(AccountsDbContext db, TimeProvider clock) : ISessionStore
{
    public async Task<string> OpenAsync(Guid accountId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var (token, hash) = Tokens.New();
        var now = clock.GetUtcNow();
        db.Sessions.Add(new Session
        {
            Id = Guid.CreateVersion7(),
            UserId = accountId,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(s => s.ExpiresAt < now).ExecuteDeleteAsync(ct);
        return token;
    }

    public async Task<Account?> FindAccountAsync(string token, CancellationToken ct)
    {
        var hash = Tokens.Digest(token);
        var now = clock.GetUtcNow();
        return await (
            from session in db.Sessions
            where session.TokenHash == hash && session.ExpiresAt > now
            join user in db.Users on session.UserId equals user.Id
            select new Account(user.Id, user.Email, user.DisplayName, user.IsAdmin)).FirstOrDefaultAsync(ct);
    }

    public async Task CloseAsync(string token, CancellationToken ct)
    {
        var hash = Tokens.Digest(token);
        await db.Sessions.Where(s => s.TokenHash == hash).ExecuteDeleteAsync(ct);
    }
}

internal sealed class EfExchangedTokens(AccountsDbContext db, OidcValidationSettings settings, TimeProvider clock) : IExchangedTokens
{
    public async Task<bool> ExchangeOnceAsync(string idToken, DateTimeOffset expiresAt, CancellationToken ct)
    {
        // A token is still accepted for the leeway after it expires, so its record is kept that long too.
        var forgotten = clock.GetUtcNow() - settings.Leeway;
        await db.ExchangedTokens.Where(t => t.ExpiresAt < forgotten).ExecuteDeleteAsync(ct);

        var hash = Tokens.Digest(idToken);
        if (await db.ExchangedTokens.AnyAsync(t => t.TokenHash == hash, ct))
        {
            return false;
        }

        db.ExchangedTokens.Add(new ExchangedToken { TokenHash = hash, ExpiresAt = expiresAt });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
