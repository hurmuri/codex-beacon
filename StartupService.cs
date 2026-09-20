using Microsoft.Win32;

namespace CodexBeacon;

internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexBeacon";

    public static bool IsBackgroundLaunch => Environment.GetCommandLineArgs()
        .Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase));

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value
                && value.Equals(BuildCommand(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the Windows startup registry key.");
        if (enabled)
            key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string BuildCommand() => $"\"{ResolveExecutablePath()}\" --background";

    private static string ResolveExecutablePath()
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index].Equals("--launcher", StringComparison.OrdinalIgnoreCase)
                && File.Exists(arguments[index + 1]))
                return Path.GetFullPath(arguments[index + 1]);
        }

        return Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the Codex Beacon executable path.");
    }
}
