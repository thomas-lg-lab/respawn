using Respawn.Games.Ports;

namespace Respawn.Games.Services;

/// <summary>An owner's own access to a store, set and taken away. The connector of the store says what
/// can be one; the store itself is only asked by the sync.</summary>
public sealed class CredentialService(
    ILinkedAccountStore linkedAccounts,
    ICredentialStore credentials,
    IEnumerable<ILibrarySource> sources,
    TimeProvider clock) : ICredentialService
{
    public async Task SetAsync(Guid ownerId, Guid linkedAccountId, string credential, CancellationToken ct)
    {
        var account = await linkedAccounts.OwnedAsync(ownerId, linkedAccountId, ct);
        var given = credential.Trim();
        if (sources.FirstOrDefault(source => source.Source == account.Source) is not { } source || !source.IsCredential(given))
        {
            throw new GamesException(GamesError.InvalidCredential);
        }

        bool kept;
        try
        {
            kept = await credentials.SetAsync(account.Id, given, clock.GetUtcNow(), ct);
        }
        catch (CredentialException)
        {
            throw new GamesException(GamesError.CredentialsUnavailable);
        }

        if (!kept)
        {
            // Unlinked meanwhile.
            throw new GamesException(GamesError.UnknownLinkedAccount);
        }
    }

    public async Task RemoveAsync(Guid ownerId, Guid linkedAccountId, CancellationToken ct)
    {
        var account = await linkedAccounts.OwnedAsync(ownerId, linkedAccountId, ct);
        if (!await credentials.RemoveAsync(account.Id, ct))
        {
            throw new GamesException(GamesError.UnknownLinkedAccount);
        }
    }
}
