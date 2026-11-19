namespace LiveTranscriber;

public sealed record ContextFile(string Name, string Text);

public sealed class ContextCollection
{
    public List<ContextFile> Files { get; } = [];
    public List<string> TooLarge { get; } = [];
    public List<string> Unreadable { get; } = [];
    public int Length { get; set; }
    public bool LimitReached { get; set; }
}

public static class ContextCollector
{
    public const long MaxFileSize = 512 * 1024;
    public const long MaxFolderFileSize = 200 * 1024;

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "packages", "dist", "build", "out", "target", "publish",
        "Debug", "Release", "TestResults", "coverage", "__pycache__", "venv", "env", "vendor", "Pods"
    };

    private static readonly HashSet<string> SkippedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "packages.lock.json", "poetry.lock",
        "Pipfile.lock", "Cargo.lock", "composer.lock", "Gemfile.lock", "go.sum"
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".slnx", ".props", ".targets", ".xaml", ".axaml", ".razor", ".cshtml",
        ".fs", ".vb", ".py", ".ipynb", ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".vue", ".svelte",
        ".java", ".kt", ".kts", ".gradle", ".scala", ".groovy", ".c", ".h", ".cpp", ".cc", ".hpp", ".cxx",
        ".m", ".mm", ".swift", ".go", ".rs", ".php", ".rb", ".dart", ".lua", ".r", ".pl", ".asm",
        ".sql", ".prisma", ".graphql", ".proto", ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".json", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf", ".config", ".properties",
        ".md", ".txt", ".rst", ".sh", ".bat", ".cmd", ".ps1", ".psm1", ".dockerfile", ".tf", ".cmake"
    };

    private static readonly HashSet<string> CodeFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dockerfile", "Makefile", "CMakeLists.txt", "Gemfile", "Procfile"
    };

    public static ContextCollection Collect(IEnumerable<string> paths, int budget)
    {
        var result = new ContextCollection();

        foreach (var path in paths)
        {
            if (result.LimitReached)
            {
                break;
            }

            if (File.Exists(path))
            {
                Add(path, Path.GetFileName(path), MaxFileSize, budget, result);
            }
            else if (Directory.Exists(path))
            {
                var folder = Path.TrimEndingDirectorySeparator(path);
                AddFolder(folder, Path.GetDirectoryName(folder) ?? folder, budget, result);
            }
        }

        return result;
    }

    private static void AddFolder(string folder, string root, int budget, ContextCollection result)
    {
        string[] files;
        string[] folders;

        try
        {
            files = Directory.GetFiles(folder);
            folders = Directory.GetDirectories(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result.Unreadable.Add(Relative(root, folder));
            return;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.Where(IsCode))
        {
            if (result.LimitReached)
            {
                return;
            }

            Add(file, Relative(root, file), MaxFolderFileSize, budget, result);
        }

        foreach (var child in folders)
        {
            var name = Path.GetFileName(child);

            if (result.LimitReached)
            {
                return;
            }

            if (!name.StartsWith('.') && !SkippedFolders.Contains(name))
            {
                AddFolder(child, root, budget, result);
            }
        }
    }

    private static void Add(string path, string name, long maxSize, int budget, ContextCollection result)
    {
        try
        {
            if (new FileInfo(path).Length > maxSize)
            {
                result.TooLarge.Add(name);
                return;
            }

            var text = File.ReadAllText(path);

            if (result.Length + text.Length > budget)
            {
                result.LimitReached = true;
                return;
            }

            result.Files.Add(new ContextFile(name, text));
            result.Length += text.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result.Unreadable.Add(name);
        }
    }

    private static bool IsCode(string path)
    {
        var name = Path.GetFileName(path);

        if (SkippedFiles.Contains(name) || name.Contains(".min.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CodeFileNames.Contains(name) || CodeExtensions.Contains(Path.GetExtension(path));
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
