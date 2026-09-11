using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CodexBeacon;

public sealed class SystemSnapshot
{
    public DateTime CollectedAt { get; set; }
    public string OverallState { get; set; } = "Unknown";
    public string OverallMessage { get; set; } = "正在等待首次检测";
    public List<ComponentStatus> Components { get; set; } = [];
    public List<ProcessRecord> Processes { get; set; } = [];
    public List<ProxyHop> ProxyChain { get; set; } = [];
    public List<TailnetDevice> TailscaleDevices { get; set; } = [];
    public List<NodeRuntime> NodeVersions { get; set; } = [];
    public List<ProxyHop> CandidateEndpoints { get; set; } = [];
    public List<ExternalConnection> ExternalConnections { get; set; } = [];
    public List<PublicEgress> PublicEgress { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class ExternalConnection
{
    public string ProcessName { get; set; } = "";
    public int Pid { get; set; }
    public string Role { get; set; } = "";
    public string RemoteAddress { get; set; } = "";
    public int RemotePort { get; set; }
    public string State { get; set; } = "";
    public string RemoteEndpoint => $"{RemoteAddress}:{RemotePort}";
}

public sealed class PublicEgress
{
    public string Address { get; set; } = "";
    public string Route { get; set; } = "系统默认网络路径";
    public string Evidence { get; set; } = "";
    public string CheckedAt { get; set; } = "";
}

public sealed class NodeRuntime
{
    public string Version { get; set; } = "";
    public bool IsCurrent { get; set; }
    public string DisplayName => IsCurrent ? $"{Version} · 当前使用" : Version;
}

public sealed class ComponentStatus
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public string Detail { get; set; } = "";
    public string InstalledVersion { get; set; } = "—";
    public string LatestVersion { get; set; } = "—";
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public string AccountState { get; set; } = "NotApplicable";
    public bool CanManageService { get; set; }
    [JsonIgnore]
    public bool CanInstall { get; set; }
    [JsonIgnore]
    public bool PrerequisitesReady { get; set; } = true;
    [JsonIgnore]
    public bool CanStart => IsInstalled && CanManageService && !IsRunning;
    [JsonIgnore]
    public bool CanStop => IsInstalled && CanManageService && IsRunning;
    [JsonIgnore]
    public bool CanRestart => CanStop;
    [JsonIgnore]
    public bool CanUpgrade => IsInstalled && PrerequisitesReady
        && Version.TryParse(InstalledVersion, out var installed)
        && Version.TryParse(LatestVersion, out var latest) && latest > installed;
    [JsonIgnore]
    public bool CanLogin => IsInstalled && AccountState == "SignedOut";
    [JsonIgnore]
    public string ReadinessLabel => !IsInstalled ? "下一步：安装"
        : AccountState == "SignedOut" ? "下一步：登录账号"
        : AccountState == "Unknown" ? "登录状态待确认"
        : !PrerequisitesReady ? "请先完成运行环境配置"
        : !IsRunning && CanManageService ? "下一步：启动服务"
        : CanUpgrade ? "有可用更新"
        : "已就绪";
    [JsonIgnore]
    public Uri? RepositoryUri => Id switch
    {
        "desktop" => new("https://github.com/Wangnov/Codex-App-Manager"),
        "codex" => new("https://github.com/openai/codex"),
        "opencodex" => new("https://github.com/lidge-jun/opencodex"),
        "relay" => new("https://github.com/gronxb/codex-relay"),
        "nvm" => new("https://github.com/coreybutler/nvm-windows"),
        "node" => new("https://github.com/nodejs/node"),
        "npm" => new("https://github.com/npm/cli"),
        "tailscale" => new("https://github.com/tailscale/tailscale"),
        _ => null
    };
    [JsonIgnore]
    public string RepositoryLabel => Id == "desktop" ? "版本管理参考" : "GitHub";
    public string StatusLabel => State switch
    {
        "Healthy" => "运行正常",
        "Stopped" => "已停止",
        "Warning" => "需要关注",
        "Unavailable" => "未安装",
        _ => "状态未知"
    };
    public string VersionSummary => LatestVersion is not "—" && InstalledVersion != LatestVersion
        ? $"{InstalledVersion}  →  {LatestVersion}"
        : InstalledVersion;
    [JsonIgnore]
    public SolidColorBrush StatusBackground => State switch
    {
        "Healthy" => Brush("#E5F6EF"),
        "Warning" => Brush("#FFF4CE"),
        "Stopped" => Brush("#EEF1F4"),
        _ => Brush("#FFF0EE")
    };
    [JsonIgnore]
    public SolidColorBrush StatusForeground => State switch
    {
        "Healthy" => Brush("#087F5B"),
        "Warning" => Brush("#805A00"),
        "Stopped" => Brush("#536171"),
        _ => Brush("#B42318")
    };

    private static SolidColorBrush Brush(string hex)
    {
        var value = hex.TrimStart('#');
        return new(Color.FromArgb(255, Convert.ToByte(value[..2], 16), Convert.ToByte(value[2..4], 16), Convert.ToByte(value[4..6], 16)));
    }
}

public sealed class ProcessRecord
{
    public int Pid { get; set; }
    public int ParentPid { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "Codex 相关进程";
    public string Version { get; set; } = "—";
    public string Architecture { get; set; } = "—";
    public string StartedAt { get; set; } = "—";
    public string ExecutablePath { get; set; } = "—";
    public string CommandLine { get; set; } = "—";
}

public sealed class ProxyHop
{
    public int Order { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public string Evidence { get; set; } = "";
    public string OrderLabel => Order.ToString("00");
    public string StateLabel => State == "Healthy" ? "可达" : State == "Warning" ? "待确认" : "不可达";
}

public sealed class TailnetDevice
{
    public string Name { get; set; } = "";
    public string DnsName { get; set; } = "";
    public string OS { get; set; } = "";
    public string Addresses { get; set; } = "";
    public bool Online { get; set; }
    public bool IsSelf { get; set; }
    public string LastSeen { get; set; } = "";
    public string OnlineLabel => Online ? "在线" : "离线";
    public string DeviceLabel => IsSelf ? $"{Name} · 本机" : Name;
}

public sealed class ActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Details { get; set; } = "";
}

public sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 15;
    public string RelayTaskName { get; set; } = "Codex Relay";
    public string ProxyTaskName { get; set; } = "opencodex-proxy";
}
