using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace CodexBeacon;

public partial class App : Application
{
    private const string StartupMutexName = @"Local\CodexBeacon.StartupReplacement";
    private Window? _window;

    public App()
    {
        Localization.Initialize();
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            ReplaceExistingInstance();
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    private static void ReplaceExistingInstance()
    {
        using var startupMutex = new Mutex(false, StartupMutexName);
        var ownsMutex = false;
        try
        {
            try { ownsMutex = startupMutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) throw new TimeoutException("Timed out waiting to replace the previous Codex Beacon instance.");

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
        finally
        {
            if (ownsMutex) startupMutex.ReleaseMutex();
        }
    }

    internal void ReloadMainWindow(Window previousWindow)
    {
        _window = new MainWindow();
        _window.Activate();
        previousWindow.Close();
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "crash.log"), $"{DateTimeOffset.Now:O}\n{exception}");
        }
        catch { }
    }
}
