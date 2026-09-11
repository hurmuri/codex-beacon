using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexBeacon;

public sealed class SystemService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _scriptDirectory = Path.Combine(AppContext.BaseDirectory, "Scripts");
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon", "settings.json");

    private readonly string _settingsPath = SettingsPath;

    public AppSettings LoadSettings()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public async Task<SystemSnapshot> CollectAsync(bool includeLatest, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(_scriptDirectory, "Collect-CodexStatus.ps1"),
            "-SettingsPath", _settingsPath,
            "-Language", EffectiveLanguage()
        };
        if (includeLatest) arguments.Add("-IncludeLatest");
        var result = await RunAsync("powershell.exe", arguments, cancellationToken);
        if (result.ExitCode != 0)
            return new() { OverallState = "Warning", OverallMessage = Localization.Get("CollectionIncomplete"), Error = SafeError(result.Error) };

        try
        {
            return JsonSerializer.Deserialize<SystemSnapshot>(result.Output, JsonOptions)
                ?? new() { OverallState = "Warning", OverallMessage = Localization.Get("CollectionNoData") };
        }
        catch (Exception ex)
        {
            return new() { OverallState = "Warning", OverallMessage = Localization.Get("CollectionParseFailed"), Error = SafeError(ex.Message) };
        }
    }

    public async Task<ActionResult> InvokeActionAsync(string component, string action, string? version = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(_scriptDirectory, "Invoke-CodexAction.ps1"),
            "-Component", component, "-Action", action, "-SettingsPath", _settingsPath,
            "-Language", EffectiveLanguage()
        };
        if (!string.IsNullOrWhiteSpace(version)) { arguments.Add("-Version"); arguments.Add(version); }
        var result = await RunAsync("powershell.exe", arguments, cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<ActionResult>(result.Output, JsonOptions)
                ?? new() { Success = false, Message = Localization.Get("ActionNoResult"), Details = SafeError(result.Error) };
        }
        catch
        {
            return new() { Success = false, Message = Localization.Get("ActionFailed"), Details = SafeError(result.Error + " " + result.Output) };
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException(Localization.Format("UnableToStart", fileName));
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static string SafeError(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Localization.Get("NoMoreDiagnostics");
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.Contains("token", StringComparison.OrdinalIgnoreCase)
                        && !line.Contains("authorization", StringComparison.OrdinalIgnoreCase)
                        && !line.Contains("api_key", StringComparison.OrdinalIgnoreCase));
        return string.Join(" ", lines).Trim();
    }

    private static string EffectiveLanguage() => Localization.CurrentLanguage == Localization.SystemLanguage
        ? (System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US")
        : Localization.CurrentLanguage;
}
