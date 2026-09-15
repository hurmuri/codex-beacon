using System.Text;
using System.Text.RegularExpressions;

namespace CodexBeacon;

internal static partial class AppLog
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int MaxDisplayCharacters = 1_500_000;
    private static readonly object Gate = new();

    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon");
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "app.log");

    public static event Action<string>? LineAdded;

    public static void Info(string source, string message) => Write("INFO", source, message);
    public static void Warning(string source, string message) => Write("WARN", source, message);
    public static void Error(string source, string message) => Write("ERROR", source, message);

    public static void Write(string level, string source, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        foreach (var rawLine in message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var line = $"{timestamp} [{level}] [{Sanitize(source)}] {Sanitize(rawLine)}";
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    RotateIfNeeded();
                    File.AppendAllText(FilePath, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
            LineAdded?.Invoke(line);
        }
    }

    public static string ReadText()
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return string.Empty;
                var text = File.ReadAllText(FilePath, Encoding.UTF8);
                return text.Length <= MaxDisplayCharacters ? text : text[^MaxDisplayCharacters..];
            }
        }
        catch (Exception ex)
        {
            return $"Unable to read {FilePath}: {ex.Message}";
        }
    }

    public static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var safe = KeyValueSecretRegex().Replace(value, "$1=<redacted>");
        safe = BearerRegex().Replace(safe, "$1<redacted>");
        return SecretTokenRegex().Replace(safe, "<redacted>");
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < MaxLogBytes) return;
        var archivedPath = FilePath + ".1";
        File.Move(FilePath, archivedPath, true);
    }

    [GeneratedRegex(@"(?i)\b(token|authorization|api[_ -]?key|password)\s*[=:]\s*\S+")]
    private static partial Regex KeyValueSecretRegex();

    [GeneratedRegex(@"(?i)\b(bearer\s+)\S+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"\b(?:sk|sess|ocx_session)_[A-Za-z0-9_-]{12,}\b|\bsk-[A-Za-z0-9_-]{12,}\b")]
    private static partial Regex SecretTokenRegex();
}
