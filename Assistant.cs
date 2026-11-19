using System.Threading.Channels;

namespace LiveTranscriber;

public sealed class Assistant : IDisposable
{
    private readonly Settings settings;
    private readonly OpenAiClient openAi;
    private readonly Transcript transcript;
    private readonly LoopbackRecorder recorder;
    private readonly Channel<byte[]> segments = Channel.CreateUnbounded<byte[]>();
    private readonly List<ChatMessage> history = [];
    private readonly CancellationTokenSource cancellation = new();
    private DateTime lastAsk = DateTime.MinValue;

    public Assistant(Settings settings, string apiKey)
    {
        this.settings = settings;
        openAi = new OpenAiClient(settings, apiKey);
        transcript = new Transcript(Path.Combine(settings.DataFolder, "transcripts", $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt"));
        recorder = new LoopbackRecorder(segment => segments.Writer.TryWrite(segment));
    }

    public event Action<string>? TranscriptAdded;
    public event Action<string>? ErrorOccurred;

    public string Format => recorder.Format;

    public TimeSpan ContextWindow { get; set; } = TimeSpan.FromSeconds(90);

    public string ApiKey
    {
        get => openAi.ApiKey;
        set => openAi.ApiKey = value;
    }

    public bool HasApiKey => openAi.HasApiKey;

    public Task<System.Net.HttpStatusCode> CheckKeyAsync(string key) => openAi.CheckKeyAsync(key, cancellation.Token);

    public bool Paused
    {
        get => recorder.Paused;
        set => recorder.Paused = value;
    }

    public void Start()
    {
        recorder.Start();
        _ = Task.Run(TranscribeLoopAsync);
    }

    public async Task<string?> AskAsync(string context, string? question)
    {
        var now = DateTime.Now;
        var windowStart = now - ContextWindow;
        var fragment = transcript.Since(lastAsk > windowStart ? lastAsk : windowStart);

        if (fragment.Length == 0 && string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        var request = BuildRequest(fragment, question);
        var messages = new List<ChatMessage> { new("system", settings.SystemPrompt) };

        if (!string.IsNullOrWhiteSpace(context))
        {
            messages.Add(new ChatMessage("system", $"Додатковий контекст від користувача:{Environment.NewLine}{context.Trim()}"));
        }

        messages.AddRange(history);
        messages.Add(new ChatMessage("user", request));

        var answer = await openAi.AskAsync(messages, cancellation.Token);

        history.Add(new ChatMessage("user", request));
        history.Add(new ChatMessage("assistant", answer));
        lastAsk = now;

        return answer;
    }

    public void ClearTranscript() => transcript.Clear();

    public void ClearConversation()
    {
        history.Clear();
        lastAsk = DateTime.MinValue;
    }

    public void Dispose()
    {
        cancellation.Cancel();
        recorder.Dispose();
        openAi.Dispose();
        transcript.Dispose();
        cancellation.Dispose();
    }

    private static string BuildRequest(string fragment, string? question)
    {
        var parts = new List<string>();

        if (fragment.Length > 0)
        {
            parts.Add($"Фрагмент розмови:{Environment.NewLine}{fragment}");
        }

        if (!string.IsNullOrWhiteSpace(question))
        {
            parts.Add($"Питання користувача: {question.Trim()}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private string TranscriptionPrompt()
    {
        var parts = new List<string>();
        var previous = transcript.Tail(150);

        if (settings.TranscribeTerms.Length > 0)
        {
            parts.Add($"Розмова українською про програмування. У мовленні трапляються англійські терміни: {settings.TranscribeTerms}.");
        }

        if (previous.Length > 0)
        {
            parts.Add($"Попередня фраза: {previous}");
        }

        return string.Join(" ", parts);
    }

    private async Task TranscribeLoopAsync()
    {
        try
        {
            await foreach (var segment in segments.Reader.ReadAllAsync(cancellation.Token))
            {
                if (!openAi.HasApiKey)
                {
                    continue;
                }

                try
                {
                    var prompt = TranscriptionPrompt();
                    var text = await openAi.TranscribeAsync(segment, prompt, cancellation.Token);
                    text = Hallucinations.Clean(text, prompt, LoopbackRecorder.Duration(segment));

                    if (text.Length > 0)
                    {
                        TranscriptAdded?.Invoke(transcript.Add(text));
                    }
                }
                catch (Exception exception) when (!cancellation.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(exception.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
