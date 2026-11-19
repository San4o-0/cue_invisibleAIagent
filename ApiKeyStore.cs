using System.Security.Cryptography;
using System.Text;

namespace LiveTranscriber;

public static class ApiKeyStore
{
    private const string FileName = "openai-key.bin";
    private static readonly byte[] Entropy = "Cue.OpenAI.ApiKey"u8.ToArray();

    public static string? Load(string folder)
    {
        var path = Path.Combine(folder, FileName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var key = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)).Trim();
            return key.Length > 0 ? key : null;
        }
        catch (Exception exception) when (exception is CryptographicException or IOException)
        {
            return null;
        }
    }

    public static void Save(string folder, string key)
    {
        Directory.CreateDirectory(folder);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(folder, FileName), encrypted);
    }

    public static void Delete(string folder)
    {
        var path = Path.Combine(folder, FileName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string Mask(string key) => key.Length <= 10 ? "••••" : $"{key[..3]}…{key[^4..]}";
}
