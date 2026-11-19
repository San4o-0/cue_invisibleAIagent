namespace LiveTranscriber;

public sealed class Transcript : IDisposable
{
    private readonly List<(DateTime Time, string Text)> lines = [];
    private readonly Lock gate = new();
    private readonly StreamWriter file;

    public Transcript(string path)
    {
        FilePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        file = new StreamWriter(FilePath, append: true) { AutoFlush = true };
    }

    public string FilePath { get; }

    public string Add(string text)
    {
        lock (gate)
        {
            var now = DateTime.Now;
            var line = $"[{now:HH:mm:ss}] {text}";

            lines.Add((now, text));
            file.WriteLine(line);
            return line;
        }
    }

    public string Since(DateTime from)
    {
        lock (gate)
        {
            return string.Join(" ", lines.Where(line => line.Time >= from).Select(line => line.Text));
        }
    }

    public string Tail(int maxLength)
    {
        lock (gate)
        {
            if (lines.Count == 0)
            {
                return string.Empty;
            }

            var last = lines[^1].Text;
            return last.Length <= maxLength ? last : last[^maxLength..];
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            lines.Clear();
        }
    }

    public void Dispose() => file.Dispose();
}
