namespace Respawn.Accounts;

/// <summary>The words of the settings of the interface, in one place: the API speaks them, the database
/// stores them (a row reads as what the account chose) and its checks list them.</summary>
public static class SettingWords
{
    public const string French = "fr";
    public const string English = "en";
    public const string System = "system";
    public const string Dark = "dark";
    public const string Light = "light";
    public const string Pixel = "pixel";
    public const string Plain = "plain";
    public const string Blocks = "blocks";
    public const string Column = "column";

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

    public static string Of(HomeLayout home) => home switch
    {
        HomeLayout.Blocks => Blocks,
        HomeLayout.Column => Column,
        _ => throw new ArgumentOutOfRangeException(nameof(home), home, null),
    };

    public static Language ToLanguage(string word) => word switch
    {
        French => Language.French,
        English => Language.English,
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "not a language"),
    };

    public static Theme ToTheme(string word) => word switch
    {
        System => Theme.System,
        Dark => Theme.Dark,
        Light => Theme.Light,
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "not a theme"),
    };

    public static Character ToCharacter(string word) => word switch
    {
        Pixel => Character.Pixel,
        Plain => Character.Plain,
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "not a character"),
    };

    public static HomeLayout ToHomeLayout(string word) => word switch
    {
        Blocks => HomeLayout.Blocks,
        Column => HomeLayout.Column,
        _ => throw new ArgumentOutOfRangeException(nameof(word), word, "not a home layout"),
    };
}
