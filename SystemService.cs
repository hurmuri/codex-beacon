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
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexBeacon", "settings.json");

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
            "-SettingsPath", _settingsPath
        };
        if (includeLatest) arguments.Add("-IncludeLatest");
        var result = await RunAsync("powershell.exe", arguments, cancellationToken);
        if (result.ExitCode != 0)
            return new() { OverallState = "Warning", OverallMessage = "检测未完成", Error = SafeError(result.Error) };

        try
        {
            return JsonSerializer.Deserialize<SystemSnapshot>(result.Output, JsonOptions)
                ?? new() { OverallState = "Warning", OverallMessage = "检测没有返回数据" };
        }
        catch (Exception ex)
        {
            return new() { OverallState = "Warning", OverallMessage = "无法解析检测结果", Error = SafeError(ex.Message) };
        }
    }

    public async Task<ActionResult> InvokeActionAsync(string component, string action, string? version = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(_scriptDirectory, "Invoke-CodexAction.ps1"),
            "-Component", component, "-Action", action, "-SettingsPath", _settingsPath
        };
        if (!string.IsNullOrWhiteSpace(version)) { arguments.Add("-Version"); arguments.Add(version); }
        var result = await RunAsync("powershell.exe", arguments, cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<ActionResult>(result.Output, JsonOptions)
                ?? new() { Success = false, Message = "操作没有返回结果", Details = SafeError(result.Error) };
        }
        catch
        {
            return new() { Success = false, Message = "操作执行失败", Details = SafeError(result.Error + " " + result.Output) };
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
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"无法启动 {fileName}");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static string SafeError(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "没有更多诊断信息。";
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.Contains("token", StringComparison.OrdinalIgnoreCase)
                        && !line.Contains("authorization", StringComparison.OrdinalIgnoreCase)
                        && !line.Contains("api_key", StringComparison.OrdinalIgnoreCase));
        return string.Join(" ", lines).Trim();
    }
}
