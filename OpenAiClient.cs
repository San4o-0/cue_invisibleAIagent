using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LiveTranscriber;

public sealed class OpenAiClient : IDisposable
{
    private readonly HttpClient http;
    private readonly Settings settings;
    private volatile string apiKey;

    public OpenAiClient(Settings settings, string apiKey)
    {
        this.settings = settings;
        this.apiKey = apiKey;
        http = new HttpClient { BaseAddress = settings.BaseUrl, Timeout = TimeSpan.FromSeconds(60) };
    }

    public string ApiKey
    {
        get => apiKey;
        set => apiKey = value.Trim();
    }

    public bool HasApiKey => apiKey.Length > 0;

    public async Task<string> TranscribeAsync(byte[] wav, string prompt, string? language, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        content.Add(file, "file", "segment.wav");
        content.Add(new StringContent(settings.TranscribeModel), "model");
        content.Add(new StringContent("text"), "response_format");

        if (language is not null)
        {
            content.Add(new StringContent(language), "language");
        }

        if (prompt.Length > 0)
        {
            content.Add(new StringContent(prompt), "prompt");
        }

        using var request = Request(HttpMethod.Post, "audio/transcriptions", apiKey, content);
        using var response = await http.SendAsync(request, cancellationToken);
        return (await ReadAsync(response, cancellationToken)).Trim();
    }

    public async Task<string> AskAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = settings.ChatModel,
            messages = messages.Select(message => new { role = message.Role, content = message.Content })
        };

        using var request = Request(HttpMethod.Post, "chat/completions", apiKey, JsonContent.Create(body));
        using var response = await http.SendAsync(request, cancellationToken);
        using var json = JsonDocument.Parse(await ReadAsync(response, cancellationToken));

        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    public async Task<HttpStatusCode> CheckKeyAsync(string key, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, "models", key.Trim(), null);
        using var response = await http.SendAsync(request, cancellationToken);
        return response.StatusCode;
    }

    public void Dispose() => http.Dispose();

    private static HttpRequestMessage Request(HttpMethod method, string path, string key, HttpContent? content)
    {
        if (key.Length == 0)
        {
            throw new InvalidOperationException("Немає ключа OpenAI. Додай його в налаштуваннях");
        }

        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        var (code, message) = Error(body);

        throw new HttpRequestException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "OpenAI не прийняв ключ. Перевір його в налаштуваннях",
            HttpStatusCode.TooManyRequests when code == "insufficient_quota" => "На рахунку OpenAI закінчились кошти. Поповни баланс на platform.openai.com",
            HttpStatusCode.TooManyRequests => "Забагато запитів до OpenAI. Зачекай кілька секунд",
            _ => $"OpenAI {(int)response.StatusCode}: {message}"
        });
    }

    private static (string? Code, string Message) Error(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var error = json.RootElement.GetProperty("error");
            var code = error.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return (code, error.GetProperty("message").GetString() ?? body);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, body);
        }
    }
}
