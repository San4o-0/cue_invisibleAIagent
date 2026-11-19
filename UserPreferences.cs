using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace LiveTranscriber;

public sealed class UserPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public Key HotkeyKey { get; set; } = Key.Space;
    public int ContextSeconds { get; set; } = 90;
    public bool HideFromCapture { get; set; } = true;
    public int Transparency { get; set; } = 20;
    public bool AlwaysOnTop { get; set; }
    public bool AnswersOnly { get; set; }
    public double CompactWidth { get; set; } = 560;
    public double CompactHeight { get; set; } = 380;

    public static UserPreferences Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path), JsonOptions) ?? new UserPreferences()
                : new UserPreferences();
        }
        catch (JsonException)
        {
            return new UserPreferences();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
