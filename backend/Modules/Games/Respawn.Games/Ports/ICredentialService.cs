namespace Respawn.Games.Ports;

/// <summary>The use cases of an owner's own access to a store: given on one of their linked accounts,
/// it is what the sync reads that account with from then on. Every refusal is a
/// <see cref="GamesException"/>.</summary>
public interface ICredentialService
{
    /// <summary>Gives a linked account its owner's own access, in place of the one it had. Nothing is
    /// asked of the store here: the next sync says whether it takes it.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>: no such linked
    /// account, or not this owner's. <see cref="GamesError.InvalidCredential"/>: not in the shape of the
    /// store's keys, or the store takes none. <see cref="GamesError.CredentialsUnavailable"/>: the
    /// instance cannot keep it.</exception>
    Task SetAsync(Guid ownerId, Guid linkedAccountId, string credential, CancellationToken ct);

    /// <summary>Takes a linked account's own access away: it is read with the instance's again. Removing
    /// what is not there changes nothing.</summary>
    /// <exception cref="GamesException"><see cref="GamesError.UnknownLinkedAccount"/>.</exception>
    Task RemoveAsync(Guid ownerId, Guid linkedAccountId, CancellationToken ct);
}
