using System.ComponentModel.DataAnnotations;
using Respawn.Accounts;

namespace Respawn.Accounts.Api;

// The bodies of the public API, version 1. Property names are written in snake_case on the wire.

/// <summary>An account.</summary>
/// <param name="Settings">The settings of the interface kept on the account; null while it never chose any,
/// and the browser's own choices apply.</param>
public sealed record UserResponse(Guid Id, string? Email, string DisplayName, bool IsAdmin, SettingsResponse? Settings)
{
    public static UserResponse From(Account account) => new(
        account.Id,
        account.Email,
        account.DisplayName,
        account.IsAdmin,
        account.Settings is { } settings
            ? new SettingsResponse(
                SettingWords.Of(settings.Language),
                SettingWords.Of(settings.Theme),
                SettingWords.Of(settings.Character),
                SettingWords.Of(settings.Home))
            : null);
}

/// <summary>The settings of the interface.</summary>
/// <param name="Language">fr or en.</param>
/// <param name="Theme">system (the device's preference), dark or light.</param>
/// <param name="Character">pixel or plain: what the labels look like.</param>
/// <param name="Home">blocks or column: how the home places its blocks on a wide screen.</param>
public sealed record SettingsResponse(string Language, string Theme, string Character, string Home);

/// <summary>The settings of the interface, together: they replace the ones the account had. The home's
/// layout came after the other three and may be left out, so that a client that does not know it keeps
/// working: the account then keeps the one it had.</summary>
public sealed class SettingsRequest
{
    /// <summary>fr or en.</summary>
    [Required, AllowedValues(SettingWords.French, SettingWords.English)]
    public string Language { get; init; } = "";

    /// <summary>system (the device's preference), dark or light.</summary>
    [Required, AllowedValues(SettingWords.System, SettingWords.Dark, SettingWords.Light)]
    public string Theme { get; init; } = "";

    /// <summary>pixel or plain: what the labels look like.</summary>
    [Required, AllowedValues(SettingWords.Pixel, SettingWords.Plain)]
    public string Character { get; init; } = "";

    /// <summary>blocks or column: how the home places its blocks on a wide screen. Left out, the account
    /// keeps the one it had, blocks for an account that had none.</summary>
    [AllowedValues(SettingWords.Blocks, SettingWords.Column, null)]
    public string? Home { get; init; }

    /// <summary>The settings, once the request is valid, with <paramref name="kept"/> for a home layout
    /// left out.</summary>
    public InterfaceSettings ToSettings(HomeLayout kept) => new(
        SettingWords.ToLanguage(Language),
        SettingWords.ToTheme(Theme),
        SettingWords.ToCharacter(Character),
        Home is { } home ? SettingWords.ToHomeLayout(home) : kept);
}

/// <summary>A provider the sign-in page may offer: <c>Name</c> goes in the address that starts a sign-in
/// through it (<c>/api/v1/auth/oidc/{name}</c>), <c>Label</c> is what the page shows.</summary>
public sealed record ProviderResponse(string Name, string Label);

/// <summary>A provider of the instance as the signed-in account sees it: <c>Linked</c> when the account
/// already signs in with it, otherwise it may be linked (<c>/api/v1/auth/oidc/{name}/link</c>).</summary>
public sealed record AccountProviderResponse(string Name, string Label, bool Linked);

/// <summary>What the sign-in page may offer. <c>FirstAccount</c>: no account exists yet, so the one created
/// now becomes the administrator. <c>PasswordMinLength</c>: the one rule a new local password must meet.</summary>
public sealed record ConfigResponse(
    bool LocalSignIn,
    bool RegistrationOpen,
    bool FirstAccount,
    int PasswordMinLength,
    IReadOnlyList<ProviderResponse> OidcProviders);

public sealed class RegisterRequest
{
    [Required, StringLength(320)]
    public string Email { get; init; } = "";

    [Required, StringLength(100)]
    public string DisplayName { get; init; } = "";

    [Required, StringLength(256)]
    public string Password { get; init; } = "";
}

public sealed class LoginRequest
{
    [Required, StringLength(320)]
    public string Email { get; init; } = "";

    [Required, StringLength(256)]
    public string Password { get; init; } = "";
}
