using Respawn.Accounts;

namespace Respawn.Accounts.Ports;

public interface IUnitOfWork
{
    /// <summary>Runs the work in one transaction, committed when it returns and rolled back when it
    /// throws. The work may run again from the start when the connection is lost on the way: it must
    /// only touch the database.</summary>
    /// <param name="serialiseRegistrations">One registration at a time until the transaction ends, so
    /// two simultaneous first registrations cannot both pass the check and become administrator.</param>
    /// <param name="work">What the transaction holds.</param>
    /// <param name="ct">Cancels the request.</param>
    Task<T> RunAsync<T>(bool serialiseRegistrations, Func<Task<T>> work, CancellationToken ct);
}

public interface IUserDirectory
{
    Task<bool> AnyAsync(CancellationToken ct);

    Task<Account?> FindByEmailAsync(string email, CancellationToken ct);

    Task<Account?> FindByExternalIdentityAsync(string issuer, string subject, CancellationToken ct);

    /// <summary>Creates the account; without a password it only signs in through a provider.</summary>
    /// <exception cref="AccountException">The e-mail is already used, or the password is refused.</exception>
    Task<Account> CreateAsync(string? email, string displayName, string? password, bool isAdmin, CancellationToken ct);

    /// <summary>The account for these credentials, or null. A locked account is refused like a wrong
    /// password, and no faster.</summary>
    Task<Account?> AuthenticateAsync(string email, string password, CancellationToken ct);

    Task LinkExternalIdentityAsync(Guid accountId, string issuer, string subject, CancellationToken ct);
}

public interface IGroupDirectory
{
    Task<Group?> FindDefaultAsync(CancellationToken ct);

    /// <summary>Creates the default group, owned by this account. The instance has one at most.</summary>
    Task<Group> CreateDefaultAsync(string name, Guid ownerId, CancellationToken ct);

    Task AddMemberAsync(Guid groupId, Guid accountId, CancellationToken ct);
}

public interface IInstanceSettings
{
    Task<bool> GetFlagAsync(string key, bool fallback, CancellationToken ct);
}

public interface ISessionStore
{
    /// <summary>Opens a session and returns its token, which is only ever stored hashed.</summary>
    Task<string> OpenAsync(Guid accountId, DateTimeOffset expiresAt, CancellationToken ct);

    Task<Account?> FindAccountAsync(string token, CancellationToken ct);

    Task CloseAsync(string token, CancellationToken ct);
}

public interface IExchangedTokens
{
    /// <summary>Records an ID token as used. False when it was already exchanged for a session.</summary>
    Task<bool> ExchangeOnceAsync(string idToken, DateTimeOffset expiresAt, CancellationToken ct);
}

public interface IExternalTokenValidator
{
    /// <exception cref="AccountException"><see cref="AccountError.InvalidToken"/> when the token is not
    /// one this provider issued for this app, now; <see cref="AccountError.ProviderUnavailable"/> when
    /// the provider's keys could not be fetched, so nothing can be said about the token.</exception>
    Task<ExternalIdentity> ValidateAsync(OidcProvider provider, string idToken, string nonce, CancellationToken ct);
}
