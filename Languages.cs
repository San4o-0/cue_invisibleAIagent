namespace LiveTranscriber;

public sealed record LanguageOption(string Code, string Label, string EnglishName);

public static class Languages
{
    public const string Auto = "auto";
    public const string SameAsConversation = "same";

    public static readonly LanguageOption[] All =
    [
        new("uk", "Українська", "Ukrainian"),
        new("en", "English", "English"),
        new("pl", "Polski", "Polish"),
        new("de", "Deutsch", "German"),
        new("es", "Español", "Spanish"),
        new("fr", "Français", "French"),
        new("ru", "Русский", "Russian")
    ];

    public static LanguageOption? Find(string? code) => All.FirstOrDefault(language => language.Code == code);
}
