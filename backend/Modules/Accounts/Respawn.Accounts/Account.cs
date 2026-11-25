namespace Respawn.Accounts;

/// <summary>An account as the use cases see it. There is no e-mail for an account created from a
/// provider that sends none; the settings, for an account that never chose any.</summary>
public sealed record Account(Guid Id, string? Email, string DisplayName, bool IsAdmin, InterfaceSettings? Settings);

/// <summary>The languages the interface is written in.</summary>
public enum Language
{
    French,
    English,
}

/// <summary>The colours of the interface. <see cref="System"/> follows the device's preference.</summary>
public enum Theme
{
    System,
    Dark,
    Light,
}

/// <summary>What the labels of the interface look like.</summary>
public enum Character
{
    Pixel,
    Plain,
}

/// <summary>How the interface looks to an account, kept on it so that it follows its owner from one
/// browser to the next. The three are chosen together: an account has all of them or none, and with none
/// the browser's own choices apply.</summary>
public sealed record InterfaceSettings(Language Language, Theme Theme, Character Character);

/// <summary>Someone a provider has proven, known by its issuer and subject, never by its e-mail.</summary>
public sealed record ExternalIdentity(
    string Issuer,
    string Subject,
    string? Email,
    bool EmailVerified,
    string? DisplayName);

/// <summary>An OpenID Connect provider the instance signs in with. <c>Name</c> is its short name in the
/// addresses of the API, <c>Label</c> what the sign-in page shows. Public values only: the instance is a
/// public client of the provider, with no client secret. The issuer is kept without its trailing slash.
/// <c>CreatesAccounts</c>: someone the provider proves, and who has no account yet, gets a member account
/// even while registration is closed; the provider's own policy then says who may come in.</summary>
public sealed record OidcProvider(string Name, string Label, string Issuer, string ClientId, bool CreatesAccounts = false);

/// <summary>A provider of the instance as one account sees it: <c>Linked</c> when an identity it proves
/// already signs in to that account.</summary>
public sealed record AccountProvider(OidcProvider Provider, bool Linked);

/// <summary>What the sign-in page may offer.</summary>
public sealed record SignInOptions(
    bool LocalSignIn,
    bool RegistrationOpen,
    bool FirstAccount,
    int PasswordMinLength,
    IReadOnlyList<OidcProvider> Providers);

/// <summary>A sign-in that succeeded: the account, and the session token for the browser.</summary>
public sealed record SignedIn(Account Account, string SessionToken);
