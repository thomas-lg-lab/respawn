namespace Respawn.Accounts;

/// <summary>An account as the use cases see it. The e-mail is empty for an account created from a
/// provider that sends none.</summary>
public sealed record Account(Guid Id, string? Email, string DisplayName, bool IsAdmin);

/// <summary>A sharing scope: what an account makes visible, it shares with a group, never with the
/// instance as a whole. The default group holds every account of the instance.</summary>
public sealed record Group(Guid Id, string Name, Guid OwnerId, bool IsDefault);

/// <summary>A sign-in through a provider, known by its issuer and subject, never by its e-mail.</summary>
public sealed record ExternalIdentity(
    string Issuer,
    string Subject,
    string? Email,
    bool EmailVerified,
    string? DisplayName,
    DateTimeOffset ExpiresAt);

/// <summary>An OpenID Connect provider the front may sign in with. Public values only: no client
/// secret. The issuer is kept without its trailing slash.</summary>
public sealed record OidcProvider(string Name, string Issuer, string ClientId);

/// <summary>What the sign-in page may offer.</summary>
public sealed record SignInOptions(bool LocalSignIn, bool RegistrationOpen, IReadOnlyList<OidcProvider> Providers);

/// <summary>A sign-in that succeeded: the account, and the session token for the browser.</summary>
public sealed record SignedIn(Account Account, string SessionToken);
