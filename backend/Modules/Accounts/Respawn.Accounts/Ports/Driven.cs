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

    /// <summary>Creates the account; without a password it only signs in through a provider. It is in
    /// no group yet: the caller restores the rule of <see cref="IGroupDirectory"/> in the same
    /// transaction.</summary>
    /// <exception cref="AccountException">The e-mail is already used, or the password is refused.</exception>
    Task<Account> CreateAsync(string? email, string displayName, string? password, bool isAdmin, CancellationToken ct);

    /// <summary>The account for these credentials, or null. A locked account is refused like a wrong
    /// password, and no faster.</summary>
    Task<Account?> AuthenticateAsync(string email, string password, CancellationToken ct);

    Task LinkExternalIdentityAsync(Guid accountId, string issuer, string subject, CancellationToken ct);
}

/// <summary>Groups are sharing scopes: what an account makes visible, it shares with a group, never with
/// the instance as a whole.</summary>
public interface IGroupDirectory
{
    /// <summary>Makes the rule true again, whatever broke it: the instance has one default group, and
    /// every account is a member of it. A default group that is missing while accounts exist is created
    /// under this name and owned by the first administrator. Changes nothing when the rule already
    /// holds, and may be asked by several requests at once.</summary>
    Task EnsureEveryoneInDefaultGroupAsync(string defaultGroupName, CancellationToken ct);
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
