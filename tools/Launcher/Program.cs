using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace CodexBeaconLauncher;

static class Program
{
    private const string StartupMutexName = @"Local\CodexBeacon.StartupReplacement";
#if LAUNCHER_SLIM
    private const string VariantName = "slim";
    private const string AppDirName = "app-slim";
#else
    private const string VariantName = "portable";
    private const string AppDirName = "app-portable";
#endif

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            WriteLauncherLog("INFO", $"Launcher started ({VariantName}).");
            using var startupMutex = new Mutex(false, StartupMutexName);
            var ownsMutex = false;
            try
            {
                try { ownsMutex = startupMutex.WaitOne(TimeSpan.FromSeconds(30)); }
                catch (AbandonedMutexException) { ownsMutex = true; }
                if (!ownsMutex) throw new TimeoutException("Timed out waiting to replace the previous Codex Beacon instance.");

                StopExistingInstances();
                LaunchPayload(args.Contains("--background", StringComparer.OrdinalIgnoreCase));
            }
            finally
            {
                if (ownsMutex) startupMutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            WriteLauncherLog("ERROR", ex.ToString());
        }
    }

    static void WriteLauncherLog(string level, string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon");
            Directory.CreateDirectory(directory);
            var log = Path.Combine(directory, "app.log");
            var safe = message.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in safe.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                File.AppendAllText(log, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] [Launcher] {line}{Environment.NewLine}");
        }
        catch { }
    }

    static void LaunchPayload(bool background)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("payload.zip"));
        if (resourceName == null)
        {
            // Fallback to local sibling directory if available
            var localExe = Path.Combine(AppContext.BaseDirectory, "CodexBeacon.exe");
            if (File.Exists(localExe)) { Start(localExe, background); return; }
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

        Start(targetExe, background);
    }

    static void StopExistingInstances()
    {
        var currentId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("CodexBeacon"))
        {
            using (process)
            {
                if (process.Id == currentId) continue;
                try
                {
                    if (process.HasExited) continue;
                    if (process.CloseMainWindow() && process.WaitForExit(1500)) continue;
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000))
                        throw new TimeoutException($"Codex Beacon process {process.Id} did not exit.");
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception)
                {
                    try { if (process.HasExited) continue; } catch (InvalidOperationException) { continue; }
                    throw;
                }
            }
        }
    }

    static void Start(string executablePath, bool background)
    {
        var launcherPath = Environment.ProcessPath;
        var arguments = string.IsNullOrWhiteSpace(launcherPath) ? string.Empty : $"--launcher \"{launcherPath}\"";
        if (background) arguments = $"{arguments} --background".Trim();
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
