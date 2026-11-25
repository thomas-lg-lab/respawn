using Microsoft.AspNetCore.Identity;
using Respawn.Accounts;

namespace Respawn.Accounts.Infrastructure.Persistence;

/// <summary>An account. ASP.NET Core Identity owns the password hash, the failed-attempt count and the
/// lockout; the e-mail is lower-cased, and null for an account created from a provider that sends
/// none.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";

    public bool IsAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // The settings of the interface: the three together, or none (a check of the table says so).

    public Language? InterfaceLanguage { get; set; }

    public Theme? InterfaceTheme { get; set; }

    public Character? InterfaceCharacter { get; set; }

    /// <summary>The account as the use cases see it.</summary>
    public Account ToAccount() => new(
        Id,
        Email,
        DisplayName,
        IsAdmin,
        InterfaceLanguage is { } language && InterfaceTheme is { } theme && InterfaceCharacter is { } character
            ? new InterfaceSettings(language, theme, character)
            : null);
}

/// <summary>A sharing scope. The default group holds every account; the database allows one only.</summary>
public sealed class UserGroup
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public Guid OwnerId { get; set; }

    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An account in a group, once at most.</summary>
public sealed class GroupMembership
{
    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
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

/// <summary>An instance setting an administrator changes at run time.</summary>
public sealed class Setting
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
