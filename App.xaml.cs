using Microsoft.UI.Xaml;

namespace CodexBeacon;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
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
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
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
