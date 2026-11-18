using Microsoft.EntityFrameworkCore;
using Respawn.Games.Infrastructure.Persistence;
using Respawn.Games.Ports;

namespace Respawn.Games.Infrastructure.Credentials;

/// <summary>An owner's own access to a store, sealed on its linked account's row: unlinking the account
/// deletes it with the row.</summary>
internal sealed class EfCredentialStore(GamesDbContext db, CredentialCipher cipher) : ICredentialStore
{
    public async Task<bool> SetAsync(Guid linkedAccountId, string credential, DateTimeOffset at, CancellationToken ct)
    {
        var kept = cipher.Seal(linkedAccountId, credential);
        return await db.LinkedAccounts
            .Where(a => a.Id == linkedAccountId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Credential, kept).SetProperty(a => a.CredentialSetAt, at), ct) == 1;
    }

    public async Task<bool> RemoveAsync(Guid linkedAccountId, CancellationToken ct) =>
        await db.LinkedAccounts
            .Where(a => a.Id == linkedAccountId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Credential, (byte[]?)null).SetProperty(a => a.CredentialSetAt, (DateTimeOffset?)null), ct) == 1;

    public async Task<string?> FindAsync(Guid linkedAccountId, CancellationToken ct) =>
        await db.LinkedAccounts.AsNoTracking()
            .Where(a => a.Id == linkedAccountId)
            .Select(a => a.Credential)
            .FirstOrDefaultAsync(ct) is { } kept
            ? cipher.Open(linkedAccountId, kept)
            : null;
}
