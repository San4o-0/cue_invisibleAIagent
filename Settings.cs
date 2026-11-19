namespace LiveTranscriber;

public sealed class Settings
{
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
        var terms = ReadText("terms.txt").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

        return new Settings
        {
            EnvironmentApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            BaseUrl = new Uri(Read("OPENAI_BASE_URL", "https://api.openai.com/v1").TrimEnd('/') + "/"),
            ChatModel = Read("OPENAI_MODEL", "gpt-4o-mini"),
            TranscribeModel = Read("OPENAI_TRANSCRIBE_MODEL", "gpt-4o-transcribe"),
            Language = language == "auto" ? null : language,
            SystemPrompt = ReadText("prompt.txt").Trim(),
            TranscribeTerms = string.Join(", ", terms),
            DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LiveTranscriber")
        };
    }

    private static string Read(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string ReadText(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);

        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        using var stream = typeof(Settings).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"У програмі немає вбудованого файлу {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
