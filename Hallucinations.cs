using System.Text.RegularExpressions;

namespace LiveTranscriber;

public static partial class Hallucinations
{
    private const int MaxCharsPerSecond = 40;
    private const int ExtraChars = 25;
    private const int MinEchoWords = 3;

    private static readonly string[] Phrases =
    [
        "дякую за перегляд", "дякуємо за перегляд", "спасибо за просмотр", "благодарю за просмотр",
        "thank you for watching", "thanks for watching",
        "підписуйтесь на канал", "підписуйтеся на канал", "підписуйтесь на наш канал", "підписуйтеся на наш канал",
        "подписывайтесь на канал", "подписывайтесь на наш канал", "подпишитесь на канал",
        "ставте лайки", "ставьте лайки", "please subscribe", "like and subscribe",
        "до зустрічі у наступному відео", "до зустрічі в наступному відео", "до встречи в следующем видео",
        "продолжение следует", "субтитри створювала", "субтитри зроблені", "субтитри підготував",
        "субтитры сделал", "субтитры создавал", "редактор субтитров", "amara org", "dimatorzok"
    ];

    public static string Clean(string text, string prompt, TimeSpan duration)
    {
        if (text.Length > ExtraChars + duration.TotalSeconds * MaxCharsPerSecond)
        {
            return string.Empty;
        }

        var words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var promptWords = Normalize(prompt).Split(' ').ToHashSet();

        if (words.Length >= MinEchoWords && words.All(promptWords.Contains))
        {
            return string.Empty;
        }

        var kept = SentenceEnd().Split(text.Trim())
            .Where(sentence => sentence.Any(char.IsLetterOrDigit) && !IsHallucination(sentence));

        return string.Join(" ", kept).Trim();
    }

    private static bool IsHallucination(string sentence) => Phrases.Any(Normalize(sentence).Contains);

    private static string Normalize(string text) =>
        string.Join(" ", NonWord().Split(text.ToLowerInvariant()).Where(word => word.Length > 0));

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
