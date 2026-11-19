using Respawn.Accounts;

namespace Respawn.Accounts.Infrastructure.Persistence;

/// <summary>The words the settings are stored as, the ones the API uses: a row reads as what the account
/// chose. The checks of the users table list the same words.</summary>
internal static class SettingWords
{
    public static string Of(Language language) => language switch
    {
        Language.French => "fr",
        Language.English => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
    };

    public static string Of(Theme theme) => theme switch
    {
        Theme.System => "system",
        Theme.Dark => "dark",
        Theme.Light => "light",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    public static string Of(Character character) => character switch
    {
        Character.Pixel => "pixel",
        Character.Plain => "plain",
        _ => throw new ArgumentOutOfRangeException(nameof(character), character, null),
    };

    public static Language ToLanguage(string word) => word switch
    {
        "fr" => Language.French,
        "en" => Language.English,
        _ => throw new InvalidOperationException($"unknown language in the database: {word}"),
    };

    public static Theme ToTheme(string word) => word switch
    {
        "system" => Theme.System,
        "dark" => Theme.Dark,
        "light" => Theme.Light,
        _ => throw new InvalidOperationException($"unknown theme in the database: {word}"),
    };

    public static Character ToCharacter(string word) => word switch
    {
        "pixel" => Character.Pixel,
        "plain" => Character.Plain,
        _ => throw new InvalidOperationException($"unknown character in the database: {word}"),
    };
}
