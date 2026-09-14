using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace CodexBeaconLauncher;

static class Program
{
#if LAUNCHER_SLIM
    private const string VariantName = "slim";
    private const string AppDirName = "app-slim";
#else
    private const string VariantName = "portable";
    private const string AppDirName = "app-portable";
#endif

    [STAThread]
    static void Main()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("payload.zip"));
            if (resourceName == null)
            {
                // Fallback to local sibling directory if available
                var localExe = Path.Combine(AppContext.BaseDirectory, "CodexBeacon.exe");
                if (File.Exists(localExe)) { Start(localExe); return; }
                return;
            }

            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon", AppDirName);
            var targetExe = Path.Combine(appDir, "CodexBeacon.exe");
            var stampFile = Path.Combine(appDir, "version.stamp");

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            var currentStamp = ResolveStamp(assembly, stream);

            if (!File.Exists(targetExe) || !File.Exists(stampFile) || File.ReadAllText(stampFile) != currentStamp)
            {
                if (Directory.Exists(appDir))
                {
                    try { Directory.Delete(appDir, true); } catch { }
                }
                Directory.CreateDirectory(appDir);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(appDir, true);
                File.WriteAllText(stampFile, currentStamp);
            }

            Start(targetExe);
        }
        catch (Exception ex)
        {
            try
            {
                var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon", "launcher.log");
                File.WriteAllText(log, ex.ToString());
            }
            catch { }
        }
    }

    static void Start(string executablePath)
    {
        var launcherPath = Environment.ProcessPath;
        var arguments = string.IsNullOrWhiteSpace(launcherPath) ? string.Empty : $"--launcher \"{launcherPath}\"";
        Process.Start(new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            Arguments = arguments
        });
    }

    static string ResolveStamp(Assembly assembly, Stream stream)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = !string.IsNullOrWhiteSpace(informational)
            ? (informational.IndexOf('+') >= 0 ? informational[..informational.IndexOf('+')] : informational)
            : assembly.GetName().Version?.ToString() ?? "dev";

        string hashPrefix;
        try
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            hashPrefix = Convert.ToHexString(hash)[..12];
            stream.Position = 0; // Rewind for extraction
        }
        catch
        {
            hashPrefix = stream.Length.ToString("X");
        }

        return $"{version}-{VariantName}-{hashPrefix}";
    }
}
