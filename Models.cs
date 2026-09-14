using System.Text.Json.Serialization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CodexBeacon;

/// <summary>
/// Shared status palette. Colour supplements explicit text, never replaces it.
/// </summary>
internal static class Palette
{
    public const string Dash = "—";

    private static readonly Color HealthyBackground = Color.FromArgb(255, 229, 246, 239);
    private static readonly Color HealthyForeground = Color.FromArgb(255, 8, 127, 91);
    private static readonly Color WarningBackground = Color.FromArgb(255, 255, 244, 206);
    private static readonly Color WarningForeground = Color.FromArgb(255, 128, 90, 0);
    private static readonly Color NeutralBackground = Color.FromArgb(255, 238, 241, 244);
    private static readonly Color NeutralForeground = Color.FromArgb(255, 83, 97, 113);
    private static readonly Color DangerBackground = Color.FromArgb(255, 255, 240, 238);
    private static readonly Color DangerForeground = Color.FromArgb(255, 180, 35, 24);
    private static readonly Color InfoBackground = Color.FromArgb(255, 224, 242, 254);
    private static readonly Color InfoForeground = Color.FromArgb(255, 3, 105, 161);

    public static SolidColorBrush HealthyBgBrush => new(HealthyBackground);
    public static SolidColorBrush HealthyFgBrush => new(HealthyForeground);
    public static SolidColorBrush WarningBgBrush => new(WarningBackground);
    public static SolidColorBrush WarningFgBrush => new(WarningForeground);
    public static SolidColorBrush NeutralBgBrush => new(NeutralBackground);
    public static SolidColorBrush NeutralFgBrush => new(NeutralForeground);
    public static SolidColorBrush DangerBgBrush => new(DangerBackground);
    public static SolidColorBrush DangerFgBrush => new(DangerForeground);
    public static SolidColorBrush InfoBgBrush => new(InfoBackground);
    public static SolidColorBrush InfoFgBrush => new(InfoForeground);

    public static SolidColorBrush Background(string state) => state switch
    {
        "Healthy" => new SolidColorBrush(HealthyBackground),
        "Warning" => new SolidColorBrush(WarningBackground),
        "Stopped" => new SolidColorBrush(NeutralBackground),
        _ => new SolidColorBrush(DangerBackground)
    };

    public static SolidColorBrush Foreground(string state) => state switch
    {
        "Healthy" => new SolidColorBrush(HealthyForeground),
        "Warning" => new SolidColorBrush(WarningForeground),
        "Stopped" => new SolidColorBrush(NeutralForeground),
        _ => new SolidColorBrush(DangerForeground)
    };
}

public sealed class SystemSnapshot
{
    public DateTime CollectedAt { get; set; }
    public string OverallState { get; set; } = "Unknown";
    public string OverallKey { get; set; } = "";
    public List<ComponentStatus> Components { get; set; } = [];
    public List<ProcessRecord> Processes { get; set; } = [];
    public List<TailnetDevice> TailscaleDevices { get; set; } = [];
    public List<NodeRuntime> NodeVersions { get; set; } = [];
    public ProxyStatus Proxy { get; set; } = new();
    public List<PublicEgress> PublicEgress { get; set; } = [];
    public List<ModelProviderItem> Providers { get; set; } = [];
    public List<NetworkProbe> NetworkProbes { get; set; } = [];
    public List<OpenCodexModel> OpenCodexModels { get; set; } = [];
    public List<OpenCodexProvider> OpenCodexProviders { get; set; } = [];
    public string NodeMirror { get; set; } = "";
    public string NpmRegistry { get; set; } = "";
    public string OpenCodexIntegration { get; set; } = "";
    public string NodeMinimumVersion { get; set; } = "22.14.0";
    public string CodexStoreProductId { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class NetworkProbe
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public string Detail { get; set; } = "";
    public long LatencyMs { get; set; } = -1;

    [JsonIgnore] public string LatencyLabel => LatencyMs < 0 ? Palette.Dash : $"{LatencyMs} ms";
    [JsonIgnore] public string StatusLabel => State switch
    {
        "Connected" => Localization.Get("NetworkConnected"),
        "Disconnected" => Localization.Get("NetworkDisconnected"),
        "Unreachable" => Localization.Get("NetworkUnreachable"),
        _ => Localization.Get("StatusUnknown")
    };
    [JsonIgnore] public SolidColorBrush StatusBackground => Palette.Background(State == "Connected" ? "Healthy" : State == "Unknown" ? "Stopped" : "Warning");
    [JsonIgnore] public SolidColorBrush StatusForeground => Palette.Foreground(State == "Connected" ? "Healthy" : State == "Unknown" ? "Stopped" : "Warning");
}

public sealed class OpenCodexModel
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Provider) ? Id : $"{Id} · {Provider}";
}

public sealed class OpenCodexProvider
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public bool Enabled { get; set; }
}

public sealed class ComponentStatus
{
    public string Id { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string KindKey { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public string DetailKey { get; set; } = "";
    public string DetailArgs { get; set; } = "";
    public string EvidenceKey { get; set; } = "";
    public string EvidenceArgs { get; set; } = "";
    public string Path { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public string AccountState { get; set; } = "NotApplicable";
    public bool CanManageService { get; set; }
    public bool PrerequisitesReady { get; set; } = true;
    public bool RequiresAdmin { get; set; }

    // Resolved by the view layer after collection.
    [JsonIgnore] public bool CanInstall { get; set; }

    [JsonIgnore] public string Name => Localization.Get(NameKey);
    [JsonIgnore] public string Kind => Localization.Get(KindKey);
    [JsonIgnore] public string Detail => Localization.Compose(DetailKey, DetailArgs);
    [JsonIgnore] public string Evidence => Localization.Compose(EvidenceKey, EvidenceArgs);
    [JsonIgnore] public Visibility EvidenceVisibility =>
        string.IsNullOrEmpty(EvidenceKey) ? Visibility.Collapsed : Visibility.Visible;
    [JsonIgnore] public Visibility AdminBadgeVisibility => RequiresAdmin ? Visibility.Visible : Visibility.Collapsed;
    [JsonIgnore] public Visibility SettingsVisibility => !PrerequisitesReady ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public string InstalledVersionLabel => string.IsNullOrEmpty(InstalledVersion) ? Palette.Dash : InstalledVersion;
    [JsonIgnore] public string LatestVersionLabel => string.IsNullOrEmpty(LatestVersion) ? Palette.Dash : LatestVersion;
    [JsonIgnore] public string AccountStatusLabel => AccountState switch
    {
        "SignedIn" => Localization.Get("AccountSignedIn"),
        "SignedOut" => Localization.Get("AccountSignedOut"),
        "Unknown" => Localization.Get("AccountUnknown"),
        _ => Palette.Dash
    };

    [JsonIgnore]
    public string VersionSummary
    {
        get
        {
            var installed = InstalledVersionLabel;
            if (string.IsNullOrEmpty(LatestVersion) || LatestVersion == InstalledVersion) return installed;
            return $"{installed}  →  {LatestVersion}";
        }
    }

    [JsonIgnore]
    public bool CanStart => IsInstalled && CanManageService && !IsRunning;
    [JsonIgnore]
    public bool CanStop => IsInstalled && CanManageService && IsRunning;
    [JsonIgnore]
    public bool CanRestart => CanStop;
    [JsonIgnore]
    public bool CanLogin => IsInstalled && AccountState == "SignedOut";
    [JsonIgnore]
    public bool CanUpgrade => IsInstalled && PrerequisitesReady
        && !string.IsNullOrWhiteSpace(LatestVersion)
        && LatestVersion != InstalledVersion
        && (!Version.TryParse(InstalledVersion, out var installed) || !Version.TryParse(LatestVersion, out var latest) || latest > installed);

    [JsonIgnore]
    public bool ShowsVersion => Id is "desktop" or "codex";

    [JsonIgnore]
    public string ReadinessLabel => IsRunning ? Localization.Get("StatusRunning")
        : !IsInstalled ? Localization.Get("ReadinessInstall")
        : !PrerequisitesReady ? Localization.Get("ReadinessPrerequisites")
        : AccountState == "SignedOut" ? Localization.Get("ReadinessLogin")
        : AccountState == "Unknown" && Id == "desktop" ? Localization.Get("ReadinessUnknownAccount")
        : !IsRunning && CanManageService ? Localization.Get("ReadinessStart")
        : CanUpgrade ? Localization.Get("ReadinessUpgrade")
        : Localization.Get("ReadinessReady");

    [JsonIgnore]
    public string StatusLabel => State switch
    {
        "Healthy" => Localization.Get("StatusHealthy"),
        "Stopped" => Localization.Get("StatusStopped"),
        "Warning" => Localization.Get("StatusWarning"),
        "Unavailable" => Localization.Get("StatusUnavailable"),
        _ => Localization.Get("StatusUnknown")
    };

    [JsonIgnore] public SolidColorBrush StatusBackground => Palette.Background(State);
    [JsonIgnore] public SolidColorBrush StatusForeground => Palette.Foreground(State);

    [JsonIgnore]
    public Uri? RepositoryUri => Id switch
    {
        "desktop" => new Uri("https://github.com/Wangnov/Codex-App-Manager"),
        "codex" => new Uri("https://github.com/openai/codex"),
        "opencodex" => new Uri("https://github.com/lidge-jun/opencodex"),
        "relay" => new Uri("https://github.com/gronxb/codex-relay"),
        "nvm" => new Uri("https://github.com/coreybutler/nvm-windows"),
        "appinstaller" or "winget" => new Uri("https://github.com/microsoft/winget-cli"),
        "msstore" => new Uri("https://apps.microsoft.com/detail/9NBLGGH4NNS1"),
        "node" => new Uri("https://github.com/nodejs/node"),
        "npm" => new Uri("https://github.com/npm/cli"),
        "tailscale" => new Uri("https://github.com/tailscale/tailscale"),
        _ => null
    };

    [JsonIgnore]
    public string RepositoryLabel => Id switch
    {
        "desktop" => Localization.Get("VersionManagementReference"),
        "msstore" => "Microsoft Store",
        _ => "GitHub"
    };
}

public sealed class ProcessRecord
{
    public int Pid { get; set; }
    public int ParentPid { get; set; }
    public string Name { get; set; } = "";
    public string RoleKey { get; set; } = "";
    public string RoleArgs { get; set; } = "";
    public string Version { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string ExecutablePath { get; set; } = "";

    [JsonIgnore] public string Role => Localization.Compose(RoleKey, RoleArgs);
    [JsonIgnore] public string VersionLabel => string.IsNullOrEmpty(Version) ? Palette.Dash : Version;
    [JsonIgnore] public string StartedAtLabel => string.IsNullOrEmpty(StartedAt) ? Palette.Dash : StartedAt;
    [JsonIgnore] public string PathLabel => string.IsNullOrEmpty(ExecutablePath) ? Palette.Dash : ExecutablePath;
}

public sealed class ModelProviderItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string WireApi { get; set; } = "";
    public bool IsActive { get; set; }
    public string TestModel { get; set; } = "gpt-5.4";

    [JsonIgnore] public bool CanSwitch => !IsActive;
    [JsonIgnore] public string SwitchButtonText => Localization.Get(IsActive ? "ProviderActiveBtn" : "ProviderSwitchBtn");
    [JsonIgnore] public string StatusLabel => Localization.Get(IsActive ? "ProviderActive" : "ProviderInactive");
    [JsonIgnore] public SolidColorBrush StatusBackground => Palette.Background(IsActive ? "Healthy" : "Stopped");
    [JsonIgnore] public SolidColorBrush StatusForeground => Palette.Foreground(IsActive ? "Healthy" : "Stopped");
}

/// <summary>
/// User-managed provider configuration. ApiKey is serialized only by ProviderStore;
/// it must never be copied into diagnostics, command-line arguments, or status output.
/// </summary>
public sealed class ProviderProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string WireApi { get; set; } = "responses";
    public string ApiKey { get; set; } = "";
    public string TestModel { get; set; } = "gpt-5.4";
    public bool IsDefault { get; set; }

    [JsonIgnore] public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
    [JsonIgnore] public string KeyStatus => Localization.Get(HasApiKey ? "ProviderKeyConfigured" : "ProviderKeyMissing");
}

public sealed class OperationProgress
{
    public string Stage { get; set; } = "";
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public double? Percent { get; set; }
    public double? BytesPerSecond { get; set; }
    public double? EtaSeconds { get; set; }
    public double ElapsedSeconds { get; set; }
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class PublicEgress
{
    public string Address { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string CheckedAt { get; set; } = "";
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string FlagEmoji { get; set; } = "";
    public string Region { get; set; } = "";
    public string City { get; set; } = "";
    public string Isp { get; set; } = "";
    public string AsNumber { get; set; } = "";
    public string HostingName { get; set; } = "";
    public bool IsHosting { get; set; }
    public bool IsProxy { get; set; }
    public int TrustScore { get; set; } = -1;
    public bool Resolved { get; set; }

    [JsonIgnore]
    public string LocationSummary
    {
        get
        {
            var place = string.IsNullOrEmpty(City) ? Country : $"{Country} · {City}";
            return string.IsNullOrWhiteSpace(place) ? Localization.Get("EgressLocationUnknown") : $"{FlagEmoji} {place}".Trim();
        }
    }

    [JsonIgnore]
    public string RegionLabel => string.IsNullOrEmpty(Region) ? Palette.Dash : Region;

    [JsonIgnore]
    public string IspLabel => string.IsNullOrEmpty(Isp) ? Palette.Dash : Isp;

    [JsonIgnore]
    public string AsNumberLabel => string.IsNullOrEmpty(AsNumber) ? Palette.Dash : AsNumber;

    [JsonIgnore]
    public string LineTypeLabel => Localization.Get(IsHosting ? "LineTypeIDC" : "LineTypeResidential");

    [JsonIgnore]
    public Visibility LineTypeVisibility => IsHosting ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public string SourceLabel => Localization.Get(string.IsNullOrEmpty(SourceKey) ? "EgressSourceIpEcho" : SourceKey);

    [JsonIgnore]
    public string TrustLabel => TrustScore < 0 ? Palette.Dash : $"{TrustScore}/100";

    [JsonIgnore]
    public bool CanOpenWebsite => Resolved;
}

public sealed class NodeRuntime
{
    public string Version { get; set; } = "";
    public bool IsCurrent { get; set; }
    public bool IsInstalled { get; set; } = true;
    public string Lts { get; set; } = "";
    public string DisplayName
    {
        get
        {
            var parts = new List<string> { Version };
            if (IsCurrent) parts.Add(Localization.Get("CurrentInUse"));
            else if (IsInstalled) parts.Add(Localization.Get("NodeInstalled"));
            if (!string.IsNullOrWhiteSpace(Lts)) parts.Add($"LTS {Lts}");
            return string.Join(" · ", parts);
        }
    }
}

public sealed class ProxyStatus
{
    public string ConfiguredMode { get; set; } = "system";
    public string EffectiveMode { get; set; } = "direct";
    public bool SystemProxyEnabled { get; set; }
    public string SystemProxyAddress { get; set; } = "";
    public bool AutoDetectEnabled { get; set; }
    public string AutoConfigUrl { get; set; } = "";
    public string TunAdapterName { get; set; } = "";
    public string EnvironmentProxy { get; set; } = "";
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

    [JsonIgnore]
    public string LastSeenLabel => LastSeen.StartsWith('@') ? Localization.Get(LastSeen[1..])
        : string.IsNullOrEmpty(LastSeen) ? Palette.Dash : LastSeen;

    [JsonIgnore] public string OnlineLabel => Localization.Get(Online ? "Online" : "Offline");
    [JsonIgnore] public string DeviceLabel => IsSelf ? $"{Name} · {Localization.Get("ThisDevice")}" : Name;
    [JsonIgnore] public string DnsLabel => string.IsNullOrEmpty(DnsName) ? Palette.Dash : DnsName;
    [JsonIgnore] public string OsLabel => string.IsNullOrEmpty(OS) ? Palette.Dash : OS;
    [JsonIgnore] public string AddressLabel => string.IsNullOrEmpty(Addresses) ? Palette.Dash : Addresses;
}

public sealed class ActionResult
{
    public bool Success { get; set; }
    public string MessageKey { get; set; } = "";
    public string MessageArgs { get; set; } = "";
    public string HintKey { get; set; } = "";
    public string HintArgs { get; set; } = "";
    public string Details { get; set; } = "";

    [JsonIgnore] public string Message => Localization.Compose(MessageKey, MessageArgs);
    [JsonIgnore] public string Hint => Localization.Compose(HintKey, HintArgs);
    [JsonIgnore] public string Summary => string.IsNullOrWhiteSpace(Hint) ? Message : $"{Message} {Hint}";

    [JsonIgnore]
    public string DisplayText => Success
        ? Summary
        : string.IsNullOrWhiteSpace(Details) ? Message : Localization.Format("MessageWithDetails", Message, Details);
}

public sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 15;
    public string RelayTaskName { get; set; } = "Codex Relay";
    public string ProxyTaskName { get; set; } = "opencodex-proxy";
    public string Language { get; set; } = Localization.SystemLanguage;
    public string NodeMirror { get; set; } = "https://mirrors.aliyun.com/nodejs-release";
    public string NpmRegistry { get; set; } = "https://registry.npmjs.org";
    public string NetworkMode { get; set; } = "auto";
    public string CustomHttpProxy { get; set; } = "";
    public bool ManagesUserProxyEnvironment { get; set; }
    public string? PreviousHttpProxy { get; set; }
    public string? PreviousHttpsProxy { get; set; }

    /// <summary>Query GitHub for a newer release shortly after startup.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Release the user asked not to be prompted about again.</summary>
    public string SkippedVersion { get; set; } = "";
}
