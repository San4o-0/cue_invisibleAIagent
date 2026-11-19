namespace LiveTranscriber;

public sealed class Settings
{
    private const string FallbackPrompt = "Знайди останнє питання у фрагменті розмови і дай на нього коротку, зрозумілу відповідь українською, яку можна сказати вголос.";

    public required string? EnvironmentApiKey { get; init; }
    public required Uri BaseUrl { get; init; }
    public required string ChatModel { get; init; }
    public required string TranscribeModel { get; init; }
    public required string? Language { get; init; }
    public required string SystemPrompt { get; init; }
    public required string TranscribeTerms { get; init; }
    public required string DataFolder { get; init; }

    public static Settings Load()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var language = Read("TRANSCRIBE_LANGUAGE", "uk");
        var promptPath = Path.Combine(AppContext.BaseDirectory, "prompt.txt");
        var termsPath = Path.Combine(AppContext.BaseDirectory, "terms.txt");
        var terms = File.Exists(termsPath)
            ? File.ReadAllLines(termsPath).Select(line => line.Trim()).Where(line => line.Length > 0)
            : [];

        return new Settings
        {
            EnvironmentApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            BaseUrl = new Uri(Read("OPENAI_BASE_URL", "https://api.openai.com/v1").TrimEnd('/') + "/"),
            ChatModel = Read("OPENAI_MODEL", "gpt-4o-mini"),
            TranscribeModel = Read("OPENAI_TRANSCRIBE_MODEL", "gpt-4o-transcribe"),
            Language = language == "auto" ? null : language,
            SystemPrompt = File.Exists(promptPath) ? File.ReadAllText(promptPath) : FallbackPrompt,
            TranscribeTerms = string.Join(", ", terms),
            DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LiveTranscriber")
        };
    }

    private static string Read(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
