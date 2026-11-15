using Microsoft.AspNetCore.Identity;

namespace Respawn.Accounts.Infrastructure.Persistence;

/// <summary>An account. ASP.NET Core Identity owns the password hash, the failed-attempt count and the
/// lockout; the e-mail is lower-cased, and empty for an account created from a provider that sends
/// none.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";

    public bool IsAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A signed-in browser. Only the SHA-256 of the cookie's token is stored.</summary>
public sealed class Session
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>An ID token already exchanged for a session, so none is accepted twice. Kept until the
/// token is no longer accepted: its expiry plus the validation leeway.</summary>
public sealed class ExchangedToken
{
    public string TokenHash { get; set; } = "";

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>An instance setting an administrator changes at run time.</summary>
public sealed class Setting
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
