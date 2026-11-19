using Respawn.Accounts;

namespace Respawn.Accounts.Ports;

/// <summary>The account use cases. Every refusal is an <see cref="AccountException"/>.</summary>
public interface IAccountService
{
    Task<SignInOptions> GetSignInOptionsAsync(CancellationToken ct);

    Task<SignedIn> RegisterAsync(string email, string displayName, string password, CancellationToken ct);

    Task<SignedIn> LoginAsync(string email, string password, CancellationToken ct);

    /// <summary>Signs in with an ID token of a configured provider. With <paramref name="sessionToken"/>
    /// naming a current session, a provider that is not known yet is linked to that account instead of
    /// creating one.</summary>
    Task<SignedIn> SignInWithProviderAsync(
        string providerName, string idToken, string nonce, string? sessionToken, CancellationToken ct);

    Task<Account?> FindSignedInAsync(string? sessionToken, CancellationToken ct);

    /// <summary>Keeps the settings of the interface on the account, in place of the ones it had; null
    /// when the account no longer exists.</summary>
    Task<Account?> SaveSettingsAsync(Guid accountId, InterfaceSettings settings, CancellationToken ct);

    Task LogoutAsync(string? sessionToken, CancellationToken ct);
}
