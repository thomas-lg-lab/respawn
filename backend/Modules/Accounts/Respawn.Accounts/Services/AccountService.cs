using System.Text.RegularExpressions;
using Respawn.Accounts;
using Respawn.Accounts.Ports;

namespace Respawn.Accounts.Services;

/// <summary>Account rules: who may register, how a sign-in becomes a session. Thin on purpose: hashing,
/// lockout and token validation belong to the adapters and their libraries.</summary>
public sealed partial class AccountService(
    IUnitOfWork unitOfWork,
    IUserDirectory users,
    IInstanceSettings settings,
    ISessionStore sessions,
    IExchangedTokens exchangedTokens,
    IExternalTokenValidator tokens,
    AccountOptions options,
    TimeProvider clock) : IAccountService
{
    private const int DisplayNameMaxLength = 100;

    public async Task<SignInOptions> GetSignInOptionsAsync(CancellationToken ct) =>
        new(
            await settings.GetFlagAsync(InstanceFlags.LocalSignIn, true, ct),
            await RegistrationAllowedAsync(ct),
            options.OidcProviders);

    public async Task<SignedIn> RegisterAsync(string email, string displayName, string password, CancellationToken ct)
    {
        var address = NormaliseEmail(email);
        if (!EmailShape().IsMatch(address))
        {
            throw new AccountException(AccountError.InvalidEmail);
        }

        if (password.Length < options.PasswordMinLength)
        {
            throw new AccountException(AccountError.WeakPassword);
        }

        return await unitOfWork.RunAsync(
            serialiseRegistrations: true,
            async () =>
            {
                if (!await settings.GetFlagAsync(InstanceFlags.LocalSignIn, true, ct) || !await RegistrationAllowedAsync(ct))
                {
                    throw new AccountException(AccountError.RegistrationClosed);
                }

                if (await users.FindByEmailAsync(address, ct) is not null)
                {
                    throw new AccountException(AccountError.AccountExists);
                }

                var account = await users.CreateAsync(
                    address, displayName.Trim(), password, await IsFirstAccountAsync(ct), ct);
                return await OpenSessionAsync(account, ct);
            },
            ct);
    }

    public async Task<SignedIn> LoginAsync(string email, string password, CancellationToken ct)
    {
        var signedIn = await unitOfWork.RunAsync(
            serialiseRegistrations: false,
            async () =>
            {
                if (!await settings.GetFlagAsync(InstanceFlags.LocalSignIn, true, ct))
                {
                    throw new AccountException(AccountError.LocalSignInDisabled);
                }

                // A refusal returns instead of throwing, so that the failed-attempt count is committed.
                var account = await users.AuthenticateAsync(NormaliseEmail(email), password, ct);
                return account is null ? null : await OpenSessionAsync(account, ct);
            },
            ct);
        return signedIn ?? throw new AccountException(AccountError.InvalidCredentials);
    }

    public async Task<SignedIn> SignInWithProviderAsync(
        string providerName, string idToken, string nonce, string? sessionToken, CancellationToken ct)
    {
        var provider = options.OidcProviders.FirstOrDefault(p => p.Name == providerName)
            ?? throw new AccountException(AccountError.UnknownProvider);
        var identity = await tokens.ValidateAsync(provider, idToken, nonce, ct);

        return await unitOfWork.RunAsync(
            serialiseRegistrations: true,
            async () =>
            {
                if (!await exchangedTokens.ExchangeOnceAsync(idToken, identity.ExpiresAt, ct))
                {
                    throw new AccountException(AccountError.InvalidToken);
                }

                var account = await users.FindByExternalIdentityAsync(identity.Issuer, identity.Subject, ct);
                if (account is null)
                {
                    // An account already signed in links the provider to itself.
                    account = await FindSignedInAsync(sessionToken, ct) ?? await CreateFromProviderAsync(identity, ct);
                    await users.LinkExternalIdentityAsync(account.Id, identity.Issuer, identity.Subject, ct);
                }

                return await OpenSessionAsync(account, ct);
            },
            ct);
    }

    public async Task<Account?> FindSignedInAsync(string? sessionToken, CancellationToken ct) =>
        string.IsNullOrEmpty(sessionToken) ? null : await sessions.FindAccountAsync(sessionToken, ct);

    public async Task LogoutAsync(string? sessionToken, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(sessionToken))
        {
            await sessions.CloseAsync(sessionToken, ct);
        }
    }

    private async Task<Account> CreateFromProviderAsync(ExternalIdentity identity, CancellationToken ct)
    {
        if (!await RegistrationAllowedAsync(ct))
        {
            throw new AccountException(AccountError.RegistrationClosed);
        }

        if (identity.Email is not null && await users.FindByEmailAsync(identity.Email, ct) is not null)
        {
            // Never attached by e-mail alone: the owner signs in first, then links the provider.
            throw new AccountException(AccountError.LinkProviderFirst);
        }

        var name = identity.DisplayName ?? identity.Email ?? "Player";
        if (name.Length > DisplayNameMaxLength)
        {
            name = name[..DisplayNameMaxLength];
        }

        return await users.CreateAsync(identity.Email, name, password: null, await IsFirstAccountAsync(ct), ct);
    }

    private async Task<SignedIn> OpenSessionAsync(Account account, CancellationToken ct)
    {
        var token = await sessions.OpenAsync(account.Id, clock.GetUtcNow().AddDays(options.SessionDays), ct);
        return new SignedIn(account, token);
    }

    /// <summary>The first account is always allowed and becomes the administrator; after it,
    /// registration is closed until an administrator opens it.</summary>
    private async Task<bool> RegistrationAllowedAsync(CancellationToken ct) =>
        await IsFirstAccountAsync(ct) || await settings.GetFlagAsync(InstanceFlags.RegistrationOpen, false, ct);

    private async Task<bool> IsFirstAccountAsync(CancellationToken ct) => !await users.AnyAsync(ct);

    private static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailShape();
}
