using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace CodexBeacon;

public sealed partial class MainWindow : Window
{
    private readonly SystemService _systemService = new();
    private readonly DispatcherQueueTimer _timer;
    private bool _busy;
    private bool _loaded;
    private AppSettings _settings;

    public ObservableCollection<ComponentStatus> Components { get; } = [];
    public ObservableCollection<ComponentStatus> InstallableComponents { get; } = [];
    public ObservableCollection<ProcessRecord> Processes { get; } = [];
    public ObservableCollection<ProxyHop> ProxyHops { get; } = [];
    public ObservableCollection<TailnetDevice> TailnetDevices { get; } = [];
    public ObservableCollection<NodeRuntime> NodeVersions { get; } = [];
    public ObservableCollection<ProxyHop> CandidateEndpoints { get; } = [];
    public ObservableCollection<ExternalConnection> ExternalConnections { get; } = [];
    public ObservableCollection<PublicEgress> PublicEgressRoutes { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        _settings = _systemService.LoadSettings();
        RefreshSecondsBox.Value = _settings.RefreshSeconds;
        RelayTaskNameBox.Text = _settings.RelayTaskName;
        ProxyTaskNameBox.Text = _settings.ProxyTaskName;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1320, 860));

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        _timer.Tick += async (_, _) => await RefreshAsync(false);
        Activated += MainWindow_Activated;
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync(true);
        _timer.Start();
    }

    private async Task RefreshAsync(bool includeLatest)
    {
        if (_busy) return;
        SetBusy(true, includeLatest ? "正在检测本机组件并查询最新版本…" : "正在刷新运行状态…");
        try
        {
            var previousLatest = Components.Concat(InstallableComponents)
                .GroupBy(x => x.Id).ToDictionary(group => group.Key, group => group.First().LatestVersion);
            var snapshot = await _systemService.CollectAsync(includeLatest);
            if (!includeLatest)
            {
                foreach (var component in snapshot.Components)
                    if (component.LatestVersion == "—" && previousLatest.TryGetValue(component.Id, out var latest))
                        component.LatestVersion = latest;
            }
            ApplySnapshot(snapshot);
            StatusText.Text = snapshot.Error is null ? "检测完成" : snapshot.Error;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"刷新失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        var node = snapshot.Components.FirstOrDefault(x => x.Id == "node");
        var npm = snapshot.Components.FirstOrDefault(x => x.Id == "npm");
        var nvm = snapshot.Components.FirstOrDefault(x => x.Id == "nvm");
        var prerequisitesReady = node?.State == "Healthy" && npm?.State == "Healthy";
        InstallableComponents.Clear();
        foreach (var component in snapshot.Components.Where(x => x.Id is "desktop" or "codex" or "opencodex" or "relay" or "nvm" or "tailscale"))
        {
            component.PrerequisitesReady = component.Id is "desktop" or "nvm" or "tailscale" || prerequisitesReady;
            component.CanInstall = !component.IsInstalled && (component.Id is "desktop" or "nvm" or "tailscale" || prerequisitesReady);
            InstallableComponents.Add(component);
        }
        var tailscale = snapshot.Components.FirstOrDefault(x => x.Id == "tailscale");
        if (tailscale is not null) tailscale.CanInstall = !tailscale.IsInstalled;
        Replace(Components, snapshot.Components.Where(x => x.Id is "desktop" or "codex" or "opencodex" or "relay" or "tailscale"));
        Replace(Processes, snapshot.Processes.OrderBy(x => x.Role).ThenBy(x => x.Pid));
        Replace(ProxyHops, snapshot.ProxyChain.OrderBy(x => x.Order));
        Replace(TailnetDevices, snapshot.TailscaleDevices.OrderByDescending(x => x.IsSelf).ThenByDescending(x => x.Online).ThenBy(x => x.Name));
        Replace(NodeVersions, snapshot.NodeVersions.OrderByDescending(x => x.IsCurrent).ThenByDescending(x => Version.TryParse(x.Version, out var version) ? version : new Version()));
        Replace(CandidateEndpoints, snapshot.CandidateEndpoints);
        Replace(ExternalConnections, snapshot.ExternalConnections);
        if (snapshot.PublicEgress.Count > 0) Replace(PublicEgressRoutes, snapshot.PublicEgress);

        OverallMessageText.Text = snapshot.OverallMessage;
        PulseTimestampText.Text = snapshot.CollectedAt == default ? "采样失败" : $"采样 {snapshot.CollectedAt.ToLocalTime():HH:mm:ss}";
        RefreshAgeText.Text = snapshot.CollectedAt == default ? "数据不可用" : $"更新于 {snapshot.CollectedAt.ToLocalTime():HH:mm:ss}";
        NodeRequirementText.Text = node is null ? "未返回 Node.js 检测结果" : $"{node.StatusLabel} · {node.InstalledVersion}\n{node.Detail}";
        NpmRequirementText.Text = npm is null ? "未返回 npm 检测结果" : $"{npm.StatusLabel} · {npm.InstalledVersion}\n{npm.Detail}";
        NvmRequirementText.Text = nvm is null ? "未返回 NVM 检测结果" : $"{nvm.StatusLabel} · {nvm.InstalledVersion}\n{nvm.Detail}";
        if (NodeVersionPicker.SelectedItem is null && NodeVersions.Count > 0) NodeVersionPicker.SelectedIndex = 0;

        var online = snapshot.TailscaleDevices.Count(x => x.Online);
        var self = snapshot.TailscaleDevices.FirstOrDefault(x => x.IsSelf);
        TailnetSummaryText.Text = self is null ? "未连接 Tailscale" : $"{self.Name} 已连接 Tailnet";
        TailnetDetailText.Text = self is null ? "请确认 Tailscale 已安装并登录" : $"{online} 台设备在线 · 本机 {self.Addresses}";
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items) collection.Add(item);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        RefreshButton.IsEnabled = !busy;
        if (message is not null) StatusText.Text = message;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag) ShowPage(tag);
    }

    private void OpenSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            ShowPage(tag);
            foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
                if (Equals(item.Tag, tag)) Nav.SelectedItem = item;
        }
    }

    private void ShowPage(string tag)
    {
        DashboardPage.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        ProcessesPage.Visibility = tag == "processes" ? Visibility.Visible : Visibility.Collapsed;
        NetworkPage.Visibility = tag == "network" ? Visibility.Visible : Visibility.Collapsed;
        InstallationsPage.Visibility = tag == "installations" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ServiceAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action, DataContext: ComponentStatus component }) return;
        await RunActionAsync(component.Id, action, $"正在{ActionName(action)} {component.Name}…");
    }

    private async void PackageAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action, DataContext: ComponentStatus component }) return;
        if (action == "install" ? !component.CanInstall : !component.CanUpgrade)
        {
            StatusText.Text = "请先安装 Node.js 22.14.0 或更高版本，并确认 npm 可用。";
            return;
        }
        await RunActionAsync(component.Id, action, $"正在{ActionName(action)} {component.Name}…");
    }

    private async void DangerAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action }) return;
        var restarting = action == "restart";
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = restarting ? "重新启动所有 Codex 服务？" : "终止所有 Codex 服务？",
            Content = restarting
                ? "将停止 OpenCodex Proxy 与 Codex Relay 的计划任务及其明确匹配的服务进程，然后重新启动计划任务。Codex 桌面客户端和 Codex Beacon 不会关闭。"
                : "将停止 OpenCodex Proxy 与 Codex Relay 的计划任务及其明确匹配的服务进程。Codex 桌面客户端、Codex Beacon 和无关 Node 进程不会关闭。",
            PrimaryButtonText = restarting ? "全部重启" : "终止服务",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await RunActionAsync("all", action == "kill" ? "kill" : "restart", restarting ? "正在重新启动服务…" : "正在终止服务…");
    }

    private async Task RunActionAsync(string component, string action, string busyMessage)
    {
        if (_busy) return;
        SetBusy(true, busyMessage);
        try
        {
            var result = await _systemService.InvokeActionAsync(component, action);
            StatusText.Text = result.Success ? result.Message : $"{result.Message}：{result.Details}";
            await Task.Delay(700);
            await RefreshAfterActionAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void InstallNode_Click(object sender, RoutedEventArgs e)
    {
        var version = NewNodeVersionBox.Text.Trim().TrimStart('v');
        if (!Version.TryParse(version, out _) || version.Count(c => c == '.') != 2)
        {
            StatusText.Text = "请输入完整 Node.js 版本号，例如 24.15.0。";
            NewNodeVersionBox.Focus(FocusState.Programmatic);
            return;
        }
        await RunVersionActionAsync("install-node", version, $"正在通过 NVM 安装 Node.js {version}…");
    }

    private async void SwitchNode_Click(object sender, RoutedEventArgs e)
    {
        if (NodeVersionPicker.SelectedItem is not NodeRuntime runtime)
        {
            StatusText.Text = "请先选择一个已安装的 Node.js 版本。";
            return;
        }
        await RunVersionActionAsync("use-node", runtime.Version, $"正在切换到 Node.js {runtime.Version}…");
    }

    private async Task RunVersionActionAsync(string action, string version, string busyMessage)
    {
        if (_busy) return;
        SetBusy(true, busyMessage);
        try
        {
            var result = await _systemService.InvokeActionAsync("nvm", action, version);
            StatusText.Text = result.Success ? result.Message : $"{result.Message}：{result.Details}";
            await Task.Delay(700);
            await RefreshAfterActionAsync();
        }
        finally { SetBusy(false); }
    }

    private async Task RefreshAfterActionAsync()
    {
        var snapshot = await _systemService.CollectAsync(true);
        ApplySnapshot(snapshot);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var seconds = double.IsNaN(RefreshSecondsBox.Value) ? 15 : (int)RefreshSecondsBox.Value;
        _settings = new AppSettings
        {
            RefreshSeconds = Math.Clamp(seconds, 5, 300),
            RelayTaskName = string.IsNullOrWhiteSpace(RelayTaskNameBox.Text) ? "Codex Relay" : RelayTaskNameBox.Text.Trim(),
            ProxyTaskName = string.IsNullOrWhiteSpace(ProxyTaskNameBox.Text) ? "opencodex-proxy" : ProxyTaskNameBox.Text.Trim()
        };
        _systemService.SaveSettings(_settings);
        _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        StatusText.Text = "设置已保存，将在下次刷新时生效。";
    }

    private static string ActionName(string action) => action switch
    {
        "start" => "启动",
        "stop" => "停止",
        "restart" => "重启",
        "install" => "安装或修复",
        "upgrade" => "升级",
        "login" => "登录",
        _ => "处理"
    };
}
