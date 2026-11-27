using Respawn.Accounts;

namespace Respawn.Accounts.Ports;

/// <summary>The account use cases. Every refusal is an <see cref="AccountException"/>.</summary>
public interface IAccountService
{
    Task<SignInOptions> GetSignInOptionsAsync(CancellationToken ct);

    Task<SignedIn> RegisterAsync(string email, string displayName, string password, CancellationToken ct);

    Task<SignedIn> LoginAsync(string email, string password, CancellationToken ct);

    /// <summary>Signs in as someone a provider has proven: the account their identity is linked to, or a
    /// new one where registration allows it or the provider creates accounts. Proving them is the caller's
    /// work (the OpenID Connect exchange is an inbound adapter's, done by a library).</summary>
    Task<SignedIn> SignInWithProviderAsync(OidcProvider provider, ExternalIdentity identity, CancellationToken ct);

    /// <summary>Links an identity a provider has proven to an account, which then signs in with that
    /// provider too. Linking it again to the same account changes nothing.</summary>
    /// <exception cref="AccountException"><see cref="AccountError.ProviderLinkedElsewhere"/>.</exception>
    Task LinkProviderAsync(Guid accountId, ExternalIdentity identity, CancellationToken ct);

    /// <summary>The instance's providers, in the order of the settings, each saying whether the account is
    /// linked to it.</summary>
    Task<IReadOnlyList<AccountProvider>> GetProvidersAsync(Guid accountId, CancellationToken ct);

    Task<Account?> FindSignedInAsync(string? sessionToken, CancellationToken ct);

    /// <summary>Keeps the settings of the interface on the account, in place of the ones it had; null
    /// when the account no longer exists.</summary>
    Task<Account?> SaveSettingsAsync(Guid accountId, InterfaceSettings settings, CancellationToken ct);

    /// <summary>Opens registration to anyone, or closes it, as <paramref name="actor"/>, the signed-in
    /// account. Closed, the providers that create accounts still do.</summary>
    /// <exception cref="AccountException"><see cref="AccountError.Forbidden"/>: not an administrator.</exception>
    Task SetRegistrationOpenAsync(Account actor, bool open, CancellationToken ct);

    Task LogoutAsync(string? sessionToken, CancellationToken ct);
}
