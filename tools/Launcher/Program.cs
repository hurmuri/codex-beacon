using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace CodexBeaconLauncher;

static class Program
{
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

            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon", "app-portable");
            var targetExe = Path.Combine(appDir, "CodexBeacon.exe");
            var stampFile = Path.Combine(appDir, "version.stamp");
            var currentStamp = ResolveStamp(assembly);

            if (!File.Exists(targetExe) || !File.Exists(stampFile) || File.ReadAllText(stampFile) != currentStamp)
            {
                if (Directory.Exists(appDir))
                {
                    try { Directory.Delete(appDir, true); } catch { }
                }
                Directory.CreateDirectory(appDir);
                using var stream = assembly.GetManifestResourceStream(resourceName)!;
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

    // Hands the application the path of the single-file launcher that produced this
    // run. The application cannot infer it - it executes from the extracted
    // app-portable copy - but the updater needs it to replace the install package
    // in place. The launcher has already exited by the time the app runs, so that
    // file is never locked and can be overwritten without elevation.
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

    // The extraction stamp must change whenever the embedded payload is rebuilt,
    // so a fresh launcher always re-expands its app-portable copy. The assembly
    // informational version is set by build/AutoVersion.targets; we fall back to
    // the numeric assembly version when that attribute is absent.
    static string ResolveStamp(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "dev";
    }
}
