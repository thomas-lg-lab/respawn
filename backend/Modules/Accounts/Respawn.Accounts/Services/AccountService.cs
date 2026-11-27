using System.Text.RegularExpressions;
using Respawn.Accounts;
using Respawn.Accounts.Ports;

namespace Respawn.Accounts.Services;

/// <summary>Account rules: who may register, how a sign-in becomes a session. Thin on purpose: hashing,
/// lockout and the OpenID Connect exchange belong to the adapters and their libraries.</summary>
public sealed partial class AccountService(
    IUnitOfWork unitOfWork,
    IUserDirectory users,
    IGroupDirectory groups,
    IInstanceSettings settings,
    ISessionStore sessions,
    AccountOptions options,
    TimeProvider clock) : IAccountService
{
    private const int DisplayNameMaxLength = 100;

    /// <summary>The longest e-mail address there is: 64 characters, an @, 255 more.</summary>
    private const int EmailMaxLength = 320;

    private const string DefaultGroupName = "Everyone";

    public async Task<SignInOptions> GetSignInOptionsAsync(CancellationToken ct)
    {
        var firstAccount = await IsFirstAccountAsync(ct);
        return new(
            await settings.GetFlagAsync(InstanceFlags.LocalSignIn, true, ct),
            await RegistrationAllowedAsync(firstAccount, ct),
            firstAccount,
            options.PasswordMinLength,
            options.OidcProviders);
    }

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

                var account = await CreateAccountAsync(address, displayName.Trim(), password, ct);
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

    public Task<SignedIn> SignInWithProviderAsync(OidcProvider provider, ExternalIdentity identity, CancellationToken ct) =>
        unitOfWork.RunAsync(
            serialiseRegistrations: true,
            async () =>
            {
                var account = await users.FindByExternalIdentityAsync(identity.Issuer, identity.Subject, ct);
                if (account is null)
                {
                    account = await CreateFromProviderAsync(provider, identity, ct);
                    await users.LinkExternalIdentityAsync(account.Id, identity.Issuer, identity.Subject, ct);
                }

                return await OpenSessionAsync(account, ct);
            },
            ct);

    public Task LinkProviderAsync(Guid accountId, ExternalIdentity identity, CancellationToken ct) =>
        unitOfWork.RunAsync(
            serialiseRegistrations: true,
            async () =>
            {
                var linked = await users.FindByExternalIdentityAsync(identity.Issuer, identity.Subject, ct);
                if (linked is null)
                {
                    await users.LinkExternalIdentityAsync(accountId, identity.Issuer, identity.Subject, ct);
                }
                else if (linked.Id != accountId)
                {
                    // One identity, one account: moving it would take a way in away from its owner.
                    throw new AccountException(AccountError.ProviderLinkedElsewhere);
                }

                return true;
            },
            ct);

    public async Task<IReadOnlyList<AccountProvider>> GetProvidersAsync(Guid accountId, CancellationToken ct)
    {
        if (options.OidcProviders.Count == 0)
        {
            return [];
        }

        // An identity is kept under the configured issuer (see ExternalIdentity), so the two compare as written.
        var linked = await users.LinkedIssuersAsync(accountId, ct);
        return [.. options.OidcProviders.Select(provider => new AccountProvider(provider, linked.Contains(provider.Issuer)))];
    }

    public async Task<Account?> FindSignedInAsync(string? sessionToken, CancellationToken ct) =>
        string.IsNullOrEmpty(sessionToken) ? null : await sessions.FindAccountAsync(sessionToken, ct);

    public Task<Account?> SaveSettingsAsync(Guid accountId, InterfaceSettings settings, CancellationToken ct) =>
        users.SaveSettingsAsync(accountId, settings, ct);

    public Task SetRegistrationOpenAsync(Account actor, bool open, CancellationToken ct) =>
        // The rule is here, whatever asks: a route today, something else tomorrow.
        actor.IsAdmin
            ? settings.SetFlagAsync(InstanceFlags.RegistrationOpen, open, ct)
            : throw new AccountException(AccountError.Forbidden);

    public async Task LogoutAsync(string? sessionToken, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(sessionToken))
        {
            await sessions.CloseAsync(sessionToken, ct);
        }
    }

    private async Task<Account> CreateFromProviderAsync(OidcProvider provider, ExternalIdentity identity, CancellationToken ct)
    {
        // A provider that creates accounts lets in whomever its own policy lets in, registration closed or not.
        if (!provider.CreatesAccounts && !await RegistrationAllowedAsync(ct))
        {
            throw new AccountException(AccountError.RegistrationClosed);
        }

        // An address the provider did not verify is not the account's: anyone could claim another's. Nor
        // is one that is not an address, whatever the provider says of it: the account goes without.
        var email = identity is { EmailVerified: true, Email: { } claimed }
            && NormaliseEmail(claimed) is { Length: <= EmailMaxLength } address
            && EmailShape().IsMatch(address)
                ? address
                : null;
        if (email is not null && await users.FindByEmailAsync(email, ct) is not null)
        {
            // Never attached by e-mail alone: the owner signs in first, then links the provider.
            throw new AccountException(AccountError.LinkProviderFirst);
        }

        var name = identity.DisplayName ?? email ?? "Player";
        if (name.Length > DisplayNameMaxLength)
        {
            name = name[..DisplayNameMaxLength];
        }

        return await CreateAccountAsync(email, name, password: null, ct);
    }

    /// <summary>Every account is created here, inside the caller's transaction: it is in the default
    /// group as soon as it exists, whether or not a sign-in follows.</summary>
    private async Task<Account> CreateAccountAsync(string? email, string displayName, string? password, CancellationToken ct)
    {
        var account = await users.CreateAsync(email, displayName, password, await IsFirstAccountAsync(ct), ct);
        await groups.EnsureEveryoneInDefaultGroupAsync(DefaultGroupName, ct);
        return account;
    }

    /// <summary>Every sign-in ends here, the one of a new account included, inside its transaction.</summary>
    private async Task<SignedIn> OpenSessionAsync(Account account, CancellationToken ct)
    {
        // Every account is in the default group. The rule is restored here as well as when an account
        // is created: an account may have been created by something that knows no groups (an instance
        // of the previous version during an update), and the group itself may be missing.
        await groups.EnsureEveryoneInDefaultGroupAsync(DefaultGroupName, ct);
        var token = await sessions.OpenAsync(account.Id, clock.GetUtcNow().AddDays(options.SessionDays), ct);
        return new SignedIn(account, token);
    }

    /// <summary>The first account is always allowed and becomes the administrator; after it,
    /// registration is closed until an administrator opens it.</summary>
    private async Task<bool> RegistrationAllowedAsync(CancellationToken ct) =>
        await RegistrationAllowedAsync(await IsFirstAccountAsync(ct), ct);

    /// <summary>The first account can always be created; after it, only where the instance says so.</summary>
    private async Task<bool> RegistrationAllowedAsync(bool firstAccount, CancellationToken ct) =>
        firstAccount || await settings.GetFlagAsync(InstanceFlags.RegistrationOpen, false, ct);

    private async Task<bool> IsFirstAccountAsync(CancellationToken ct) => !await users.AnyAsync(ct);

    private static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailShape();
}
