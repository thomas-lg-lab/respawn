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
                SettingWords.Of(settings.Language), SettingWords.Of(settings.Theme), SettingWords.Of(settings.Character))
            : null);
}

/// <summary>The settings of the interface.</summary>
/// <param name="Language">fr or en.</param>
/// <param name="Theme">system (the device's preference), dark or light.</param>
/// <param name="Character">pixel or plain: what the labels look like.</param>
public sealed record SettingsResponse(string Language, string Theme, string Character);

/// <summary>The settings of the interface, the three together: they replace the ones the account had.</summary>
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

    /// <summary>The settings, once the request is valid.</summary>
    public InterfaceSettings ToSettings() => new(
        SettingWords.ToLanguage(Language), SettingWords.ToTheme(Theme), SettingWords.ToCharacter(Character));
}

/// <summary>The words of the settings on the wire.</summary>
internal static class SettingWords
{
    public const string French = "fr";
    public const string English = "en";
    public const string System = "system";
    public const string Dark = "dark";
    public const string Light = "light";
    public const string Pixel = "pixel";
    public const string Plain = "plain";

    public static string Of(Language language) => language switch
    {
        Language.French => French,
        Language.English => English,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    public static string Of(Theme theme) => theme switch
    {
        Theme.System => System,
        Theme.Dark => Dark,
        Theme.Light => Light,
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    public static string Of(Character character) => character switch
    {
        Character.Pixel => Pixel,
        Character.Plain => Plain,
        _ => throw new ArgumentOutOfRangeException(nameof(character), character, null),
    };

    public static Language ToLanguage(string word) => word switch
    {
        French => Language.French,
        English => Language.English,
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, null),
    };

    public static Theme ToTheme(string word) => word switch
    {
        System => Theme.System,
        Dark => Theme.Dark,
        Light => Theme.Light,
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, null),
    };

    public static Character ToCharacter(string word) => word switch
    {
        Pixel => Character.Pixel,
        Plain => Character.Plain,
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, null),
    };
}

public sealed record ProviderResponse(string Name, string Issuer, string ClientId);

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

public sealed class OidcRequest
{
    [Required, StringLength(64)]
    public string Provider { get; init; } = "";

    [Required, StringLength(16384)]
    public string IdToken { get; init; } = "";

    [Required, StringLength(256, MinimumLength = 8)]
    public string Nonce { get; init; } = "";
}
