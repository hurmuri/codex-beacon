using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace CodexBeacon;

public sealed partial class MainWindow : Window
{
    private const string EgressWebsite = "https://ip.net.coffee/gpt/";

    private readonly SystemService _systemService = new();
    private readonly UpdateService _updateService = new();
    private readonly DispatcherQueueTimer _timer;
    private bool _refreshBusy;
    private bool _actionBusy;
    private bool _loaded;
    private bool _initializingLanguage = true;
    private bool _initializingUpdate = true;
    private bool _updateBusy;
    private UpdateCheckResult? _updateCheck;
    private string? _pendingUpdatePath;
    private CancellationTokenSource? _actionCancellation;
    private AppSettings _settings;

    public ObservableCollection<ComponentStatus> CoreComponents { get; } = [];
    public ObservableCollection<ComponentStatus> InstallableComponents { get; } = [];
    public ObservableCollection<ProcessRecord> Processes { get; } = [];
    public ObservableCollection<TailnetDevice> TailnetDevices { get; } = [];
    public ObservableCollection<NodeRuntime> NodeVersions { get; } = [];
    public ObservableCollection<PublicEgress> PublicEgressRoutes { get; } = [];
    public ObservableCollection<ModelProviderItem> ModelProviders { get; } = [];
    public ObservableCollection<ProviderProfile> ProviderProfiles { get; } = [];
    public ObservableCollection<NetworkProbe> NetworkProbes { get; } = [];
    public ObservableCollection<OpenCodexModel> OpenCodexModels { get; } = [];
    public ObservableCollection<ComponentStatus> OpenCodexComponents { get; } = [];

    /// <summary>Nav tag to restore after the language-switch window reload.</summary>
    internal static string? PendingNavTag;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _systemService.LoadSettings();
        RefreshSecondsBox.Value = _settings.RefreshSeconds;
        RelayTaskNameBox.Text = _settings.RelayTaskName;
        ProxyTaskNameBox.Text = _settings.ProxyTaskName;
        NodeMirrorBox.Text = _settings.NodeMirror;
        NpmRegistryBox.Text = _settings.NpmRegistry;
        NetworkModePicker.SelectedValue = _settings.NetworkMode;
        if (NetworkModePicker.SelectedIndex < 0) NetworkModePicker.SelectedValue = "system";
        CustomHttpProxyBox.Text = _settings.CustomHttpProxy;
        Replace(ProviderProfiles, _systemService.LoadProviders());
        UpdateProviderEmptyState();
        LanguagePicker.SelectedValue = _settings.Language;
        if (LanguagePicker.SelectedIndex < 0) LanguagePicker.SelectedValue = Localization.SystemLanguage;
        _initializingLanguage = false;

        AppVersionText.Text = _updateService.CurrentVersion;
        AppPackageText.Text = Localization.Get(_updateService.IsSelfContained ? "AppPackagePortable" : "AppPackageSlim");
        ProjectVersionText.Text = AppVersionText.Text;
        ProjectPackageText.Text = AppPackageText.Text;
        AutoUpdateToggle.IsOn = _settings.AutoCheckUpdates;
        _initializingUpdate = false;
        SetUpdateStatus(Localization.Get("UpdateIdle"));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1320, 860));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 800;
            presenter.PreferredMinimumHeight = 560;
        }
        if (PendingNavTag is string pendingTag)
        {
            PendingNavTag = null;
            NavigateTo(pendingTag);
        }

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        _timer.Tick += async (_, _) =>
        {
            if (_actionBusy) return;
            await RefreshAsync(false);
        };
        Activated += MainWindow_Activated;
        Closed += (_, _) => { _timer.Stop(); _actionCancellation?.Cancel(); };
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync(true);
        _timer.Start();
        if (_settings.AutoCheckUpdates) _ = CheckForUpdatesAsync(true);
    }

    // ------------------------------------------------------------------ status

    private void SetStatus(string message) => StatusText.Text = message;

    private void UpdateBusyUi()
    {
        BusyRing.IsActive = _actionBusy || _refreshBusy;
        RefreshButton.IsEnabled = !_actionBusy && !_refreshBusy;
        CancelButton.Visibility = _actionBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Or(string value) => string.IsNullOrWhiteSpace(value) ? Palette.Dash : value;

    // --------------------------------------------------------------- refreshing

    private async Task RefreshAsync(bool includeLatest)
    {
        if (_refreshBusy || _actionBusy) return;
        _refreshBusy = true;
        UpdateBusyUi();
        SetStatus(Localization.Get("RefreshingStatus"));
        try
        {
            var previousVersions = CoreComponents.Concat(InstallableComponents)
                .GroupBy(component => component.Id)
                .ToDictionary(group => group.Key, group => (group.First().LatestVersion, group.First().EvidenceKey, group.First().EvidenceArgs));

            // 步骤 1：秒级本地快照采集，UI 瞬间点亮呈现
            var snapshot = await _systemService.CollectAsync(includeLatest: false);
            if (!includeLatest) RestoreVersionEvidence(snapshot, previousVersions);

            ApplySnapshot(snapshot);
            SetStatus(snapshot.Error ?? Localization.Get("CheckComplete"));

            // 步骤 2：异步非阻塞并发任务，各自独立刷新
            var proxyAddress = _settings.NetworkMode == "custom" ? _settings.CustomHttpProxy : null;

            // 任务 2.1：网络连通性探测
            _ = Task.Run(async () =>
            {
                try
                {
                    var probes = await SystemService.ProbeNetworkAsync(proxyAddress).ConfigureAwait(false);
                    DispatcherQueue.TryEnqueue(() => Replace(NetworkProbes, probes));
                }
                catch { }
            });

            // 任务 2.2：公网出口检测
            _ = Task.Run(async () =>
            {
                try
                {
                    var egress = await SystemService.QueryPublicEgressAsync(proxyAddress).ConfigureAwait(false);
                    if (egress.Count > 0)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            Replace(PublicEgressRoutes, egress);
                            EgressEmptyText.Visibility = Visibility.Collapsed;
                            OpenEgressWebsiteButton.IsEnabled = true;
                        });
                    }
                }
                catch { }
            });

            // 任务 2.3：远端最新版本并发检测
            _ = Task.Run(async () =>
            {
                try
                {
                    var latestMap = await SystemService.QueryLatestVersionsAsync(proxyAddress).ConfigureAwait(false);
                    if (latestMap.Count > 0)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            foreach (var (compKey, latestVer) in latestMap)
                            {
                                var match = CoreComponents.Concat(InstallableComponents)
                                    .FirstOrDefault(x => x.Id.Equals(compKey, StringComparison.OrdinalIgnoreCase));
                                if (match is not null) match.LatestVersion = latestVer;
                            }
                            UpdateSpecializedComponentCards();
                            
                        });
                    }
                }
                catch { }
            });

            // 任务 2.4：OpenCodex 模型拉取
            if (snapshot.Components.Any(x => x.Id == "opencodex" && x.IsRunning))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var models = await SystemService.FetchOpenCodexModelsAsync().ConfigureAwait(false);
                        if (models.Count > 0)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                Replace(OpenCodexModels, models);
                                if (OpenCodexModelPicker.SelectedItem is null && OpenCodexModels.Count > 0)
                                    OpenCodexModelPicker.SelectedIndex = 0;
                            });
                        }
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            SetStatus(Localization.Format("RefreshFailed", ex.Message));
        }
        finally
        {
            _refreshBusy = false;
            UpdateBusyUi();
        }
    }

    private static void RestoreVersionEvidence(
        SystemSnapshot snapshot,
        Dictionary<string, (string Latest, string EvidenceKey, string EvidenceArgs)> previous)
    {
        foreach (var component in snapshot.Components)
        {
            if (!string.IsNullOrEmpty(component.LatestVersion)) continue;
            if (!previous.TryGetValue(component.Id, out var carried)) continue;
            component.LatestVersion = carried.Latest;
            if (string.IsNullOrEmpty(component.EvidenceKey)) component.EvidenceKey = carried.EvidenceKey;
            if (string.IsNullOrEmpty(component.EvidenceArgs)) component.EvidenceArgs = carried.EvidenceArgs;
        }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        var node = snapshot.Components.FirstOrDefault(x => x.Id == "node");
        var npm = snapshot.Components.FirstOrDefault(x => x.Id == "npm");
        var nvm = snapshot.Components.FirstOrDefault(x => x.Id == "nvm");
        var appInstallerReady = snapshot.Components.FirstOrDefault(x => x.Id == "appinstaller")?.State == "Healthy";
        var wingetReady = snapshot.Components.FirstOrDefault(x => x.Id == "winget")?.State == "Healthy";
        var storeReady = snapshot.Components.FirstOrDefault(x => x.Id == "msstore")?.State == "Healthy";
        var nodeReady = node?.State == "Healthy";
        var npmReady = npm?.State == "Healthy";
        StoreDependencyText.Text = storeReady ? Localization.Get("StatusHealthy") : Localization.Get("StatusUnavailable");
        NpmDependencyText.Text = npmReady ? $"{Localization.Get("StatusHealthy")} · {npm?.InstalledVersionLabel}" : Localization.Get("StatusUnavailable");

        foreach (var component in snapshot.Components)
        {
            component.PrerequisitesReady = component.Id switch
            {
                "desktop" => storeReady,
                "codex" or "opencodex" or "relay" => nodeReady && npmReady,
                "winget" => appInstallerReady,
                "msstore" or "nvm" => wingetReady,
                "node" => nvm?.State == "Healthy",
                "npm" => nodeReady,
                _ => true
            };
            component.CanInstall = !component.IsInstalled && component.PrerequisitesReady;
        }
        Replace(InstallableComponents, snapshot.Components.Where(x => x.Id is "appinstaller" or "winget" or "msstore" or "nvm"));

        Replace(CoreComponents, snapshot.Components.Where(x => x.Id is "desktop" or "codex" or "opencodex" or "tailscale" or "relay"));
        Replace(OpenCodexComponents, snapshot.Components.Where(x => x.Id == "opencodex"));
        Replace(Processes, snapshot.Processes.OrderBy(x => x.RoleKey).ThenBy(x => x.Pid));
        Replace(TailnetDevices, snapshot.TailscaleDevices.OrderByDescending(x => x.IsSelf).ThenByDescending(x => x.Online).ThenBy(x => x.Name));
        Replace(NodeVersions, snapshot.NodeVersions.OrderByDescending(x => x.IsCurrent)
            .ThenByDescending(x => Version.TryParse(x.Version, out var version) ? version : new Version()));
        Replace(ModelProviders, snapshot.Providers);
        if (snapshot.NetworkProbes.Count > 0) Replace(NetworkProbes, snapshot.NetworkProbes);
        Replace(OpenCodexModels, snapshot.OpenCodexModels);
        OpenCodexIntegrationText.Text = string.IsNullOrWhiteSpace(snapshot.OpenCodexIntegration)
            ? Localization.Get("OpenCodexIntegrationNone")
            : snapshot.OpenCodexIntegration == "current"
                ? Localization.Get("OpenCodexIntegrationCurrent")
                : Localization.Format("OpenCodexIntegrationState", snapshot.OpenCodexIntegration);
        if (snapshot.PublicEgress.Count > 0) Replace(PublicEgressRoutes, snapshot.PublicEgress);

        OverallMessageText.Text = Localization.Get(snapshot.OverallKey);
        PulseTimestampText.Text = snapshot.CollectedAt == default
            ? Localization.Get("SampleFailed")
            : Localization.Format("SampleAt", snapshot.CollectedAt.ToLocalTime());
        RefreshAgeText.Text = snapshot.CollectedAt == default
            ? Localization.Get("DataUnavailable")
            : Localization.Format("UpdatedAt", snapshot.CollectedAt.ToLocalTime());

        ApplyRuntime(node, NodeStatusText, NodePathText, NodeNoteText);
        ApplyRuntime(npm, NpmStatusText, NpmPathText, NpmNoteText);
        ApplyRuntime(nvm, NvmStatusText, NvmPathText, NvmNoteText);
        NvmMirrorText.Text = Or(snapshot.NodeMirror);
        UpdateSpecializedComponentCards();

        // 更新当前主路由提示
        var activeProvider = snapshot.Providers.FirstOrDefault(x => x.IsActive);
        if (activeProvider is not null)
        {
            ActiveProviderBannerText.Text = activeProvider.Id == "opencodex"
                ? Localization.Get("ProviderBannerOpencodex")
                : $"{activeProvider.Name} ({activeProvider.BaseUrl})";
        }
        else
        {
            ActiveProviderBannerText.Text = Localization.Get("ProviderBannerOpenAI");
        }

        if (NodeVersionPicker.SelectedItem is null && NodeVersions.Count > 0) NodeVersionPicker.SelectedIndex = 0;

        var online = snapshot.TailscaleDevices.Count(x => x.Online);
        var self = snapshot.TailscaleDevices.FirstOrDefault(x => x.IsSelf);
        TailnetSummaryText.Text = self is null
            ? Localization.Get("TailscaleDisconnected")
            : Localization.Format("TailnetConnected", self.Name);
        TailnetDetailText.Text = self is null
            ? Localization.Get("TailscaleSignInHint")
            : Localization.Format("TailnetOnlineCount", online, self.Addresses);

        EgressEmptyText.Visibility = PublicEgressRoutes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenEgressWebsiteButton.IsEnabled = PublicEgressRoutes.Any(route => route.Resolved);
    }

    private static void ApplyRuntime(ComponentStatus? component, TextBlock status, TextBlock path, TextBlock note)
    {
        if (component is null)
        {
            status.Text = Palette.Dash;
            path.Text = Palette.Dash;
            note.Text = Palette.Dash;
            return;
        }
        status.Text = $"{component.StatusLabel} · {component.InstalledVersionLabel}";
        path.Text = Or(component.Path);
        note.Text = component.Detail;
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items) collection.Add(item);
    }

    private static void UpdateVersionBadge(ComponentStatus component, Border badgeBorder, TextBlock badgeText)
    {
        if (string.IsNullOrWhiteSpace(component.LatestVersion) || component.LatestVersion == Palette.Dash)
        {
            badgeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        badgeBorder.Visibility = Visibility.Visible;
        if (!component.IsInstalled)
        {
            badgeBorder.Background = Palette.NeutralBgBrush;
            badgeText.Foreground = Palette.NeutralFgBrush;
            badgeText.Text = Localization.Get("StatusUnavailable");
        }
        else if (component.CanUpgrade)
        {
            badgeBorder.Background = Palette.InfoBgBrush;
            badgeText.Foreground = Palette.InfoFgBrush;
            badgeText.Text = Localization.Get("VersionUpdateAvailable");
        }
        else
        {
            badgeBorder.Background = Palette.HealthyBgBrush;
            badgeText.Foreground = Palette.HealthyFgBrush;
            badgeText.Text = Localization.Get("VersionUpToDate");
        }
    }

    private void UpdateSpecializedComponentCards()
    {
        var desktop = CoreComponents.Concat(InstallableComponents).FirstOrDefault(x => x.Id == "desktop");
        if (desktop is not null)
        {
            DesktopStatusBadgeText.Text = desktop.StatusLabel;
            DesktopStatusBadgeBorder.Background = desktop.StatusBackground;
            DesktopStatusBadgeText.Foreground = desktop.StatusForeground;
            DesktopCurrentVersionText.Text = desktop.InstalledVersionLabel;
            DesktopLatestVersionText.Text = desktop.LatestVersionLabel;
            UpdateVersionBadge(desktop, DesktopUpdateBadgeBorder, DesktopUpdateBadgeText);
            DesktopPathText.Text = string.IsNullOrWhiteSpace(desktop.Path) ? Localization.Get("DesktopStorePathFallback") : desktop.Path;
            DesktopStartButton.IsEnabled = desktop.CanStart;
            DesktopStopButton.IsEnabled = desktop.CanStop;
            DesktopRestartButton.IsEnabled = desktop.CanRestart;
            if (!desktop.IsInstalled)
            {
                DesktopInstallButton.Content = Localization.Get("ActionInstallStore");
                DesktopInstallButton.Tag = "install";
                DesktopInstallButton.IsEnabled = true;
            }
            else if (desktop.CanUpgrade)
            {
                DesktopInstallButton.Content = Localization.Get("ActionUpgradeStore");
                DesktopInstallButton.Tag = "upgrade";
                DesktopInstallButton.IsEnabled = true;
            }
            else
            {
                DesktopInstallButton.Content = Localization.Get("ActionOpenStore");
                DesktopInstallButton.Tag = "store";
                DesktopInstallButton.IsEnabled = true;
            }
        }

        var codex = CoreComponents.Concat(InstallableComponents).FirstOrDefault(x => x.Id == "codex");
        if (codex is not null)
        {
            CodexStatusBadgeText.Text = codex.StatusLabel;
            CodexStatusBadgeBorder.Background = codex.StatusBackground;
            CodexStatusBadgeText.Foreground = codex.StatusForeground;
            CodexCurrentVersionText.Text = codex.InstalledVersionLabel;
            CodexLatestVersionText.Text = codex.LatestVersionLabel;
            UpdateVersionBadge(codex, CodexUpdateBadgeBorder, CodexUpdateBadgeText);
            CodexPathText.Text = string.IsNullOrWhiteSpace(codex.Path) ? Localization.Get("CodexPathFallback") : codex.Path;
            CodexLoginButton.IsEnabled = codex.IsInstalled;
            CodexUpgradeButton.IsEnabled = codex.CanUpgrade;
        }

        var openCodex = CoreComponents.Concat(InstallableComponents).FirstOrDefault(x => x.Id == "opencodex");
        if (openCodex is not null)
        {
            OpenCodexStatusBadgeText.Text = openCodex.StatusLabel;
            OpenCodexVersionText.Text = openCodex.VersionSummary;
            OpenCodexDetailText.Text = openCodex.Detail;
            OpenCodexInstallButton.IsEnabled = openCodex.CanInstall;
            OpenCodexUpgradeButton.IsEnabled = openCodex.CanUpgrade;
            OpenCodexStartButton.IsEnabled = openCodex.CanStart;
            OpenCodexStopButton.IsEnabled = openCodex.CanStop;
            OpenCodexIntegrateButton.IsEnabled = openCodex.IsInstalled;
        }

        var tailscale = CoreComponents.Concat(InstallableComponents).FirstOrDefault(x => x.Id == "tailscale");
        if (tailscale is not null)
        {
            TailscaleStatusBadgeText.Text = tailscale.StatusLabel;
            TailscaleVersionText.Text = tailscale.VersionSummary;
            TailscaleUpgradeButton.IsEnabled = tailscale.CanUpgrade;
            TailscaleStartButton.IsEnabled = tailscale.CanStart;
            TailscaleStopButton.IsEnabled = tailscale.CanStop;
            TailscaleRestartButton.IsEnabled = tailscale.CanRestart;
        }

        var relay = CoreComponents.Concat(InstallableComponents).FirstOrDefault(x => x.Id == "relay");
        if (relay is not null)
        {
            RelayStatusBadgeText.Text = relay.StatusLabel;
            RelayVersionText.Text = relay.VersionSummary;
            RelayInstallButton.IsEnabled = relay.CanInstall;
            RelayUpgradeButton.IsEnabled = relay.CanUpgrade;
            RelayStartButton.IsEnabled = relay.CanStart;
            RelayStopButton.IsEnabled = relay.CanStop;
        }
    }

    private async void DesktopAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        await ExecuteActionAsync("desktop", action, null, Localization.Format("RunningAction", ActionName(action), Localization.Get("DesktopAppName")));
    }

    private async void CodexAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        await ExecuteActionAsync("codex", action, null, Localization.Format("RunningAction", ActionName(action), "Codex CLI"));
    }

    // ----------------------------------------------------------------- actions

    /// <summary>
    /// Runs one component action. Status output is streamed so that long
    /// downloads never look like a frozen window, and the run can be cancelled.
    /// </summary>
    private async Task<bool> ExecuteActionAsync(string component, string action, string? version, string busyMessage)
    {
        if (_actionBusy)
        {
            SetStatus(Localization.Get("ActionAlreadyRunning"));
            return false;
        }

        _actionBusy = true;
        _actionCancellation = new CancellationTokenSource();
        UpdateBusyUi();
        SetStatus(busyMessage);
        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.IsIndeterminate = true;

        ActionResult? result = null;
        try
        {
            var progress = new Progress<string>(SetStatus);
            var operationProgress = new Progress<OperationProgress>(ApplyOperationProgress);
            result = await _systemService.InvokeActionAsync(component, action, version, progress, _actionCancellation.Token, operationProgress);
        }
        catch (Exception ex)
        {
            SetStatus(Localization.Format("RefreshFailed", ex.Message));
        }
        finally
        {
            _actionBusy = false;
            _actionCancellation?.Dispose();
            _actionCancellation = null;
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Visibility = Visibility.Collapsed;
            UpdateBusyUi();
        }

        if (result is null) return false;
        SetStatus(result.DisplayText);
        if (!result.Success) return false;

        await Task.Delay(600);
        await RefreshAsync(true);
        return true;
    }

    private void ApplyOperationProgress(OperationProgress progress)
    {
        OperationProgressBar.Visibility = Visibility.Visible;
        OperationProgressBar.IsIndeterminate = progress.Percent is null;
        if (progress.Percent is double percent)
        {
            OperationProgressBar.Minimum = 0;
            OperationProgressBar.Maximum = 100;
            OperationProgressBar.Value = Math.Clamp(percent, 0, 100);
        }

        var parts = new List<string> { progress.Stage };
        if (progress.TotalBytes is long total && total > 0)
            parts.Add($"{FormatBytes(progress.BytesReceived)} / {FormatBytes(total)}");
        else if (progress.BytesReceived > 0) parts.Add(FormatBytes(progress.BytesReceived));
        if (progress.BytesPerSecond is double speed && speed > 0) parts.Add($"{FormatBytes((long)speed)}/s");
        parts.Add($"{progress.ElapsedSeconds:0.0}s");
        if (progress.EtaSeconds is double eta && eta >= 0) parts.Add($"ETA {eta:0}s");
        if (!string.IsNullOrWhiteSpace(progress.Source)) parts.Add(progress.Source);
        if (!string.IsNullOrWhiteSpace(progress.Message)) parts.Add(progress.Message);
        SetStatus(string.Join(" · ", parts));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private void NavigateDependencies_Click(object sender, RoutedEventArgs e) => NavigateTo("installations");

    private void CancelRunning_Click(object sender, RoutedEventArgs e)
    {
        _actionCancellation?.Cancel();
        SetStatus(Localization.Get("Cancelling"));
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag) ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        DashboardPage.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        ProcessesPage.Visibility = tag == "processes" ? Visibility.Visible : Visibility.Collapsed;
        NetworkPage.Visibility = tag == "network" ? Visibility.Visible : Visibility.Collapsed;
        ProvidersPage.Visibility = tag == "providers" ? Visibility.Visible : Visibility.Collapsed;
        OpenCodexPage.Visibility = tag == "opencodex" ? Visibility.Visible : Visibility.Collapsed;
        TailscalePage.Visibility = tag == "tailscale" ? Visibility.Visible : Visibility.Collapsed;
        InstallationsPage.Visibility = tag == "installations" ? Visibility.Visible : Visibility.Collapsed;
        RelayPage.Visibility = tag == "relay" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ServiceAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action, DataContext: ComponentStatus component }) return;
        if (action == "install" && !component.CanInstall)
        {
            SetStatus(Localization.Get("PrerequisiteHint"));
            return;
        }
        await ExecuteActionAsync(component.Id, action, null, Localization.Format("RunningAction", ActionName(action), component.Name));
    }

    private async void PackageAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action, DataContext: ComponentStatus component }) return;
        if (action == "install" ? !component.CanInstall : !component.CanUpgrade)
        {
            SetStatus(Localization.Get("PrerequisiteHint"));
            return;
        }
        await ExecuteActionAsync(component.Id, action, null, Localization.Format("RunningAction", ActionName(action), component.Name));
    }

    private async void KillCodexProcesses_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmAsync(
            Localization.Get("KillCodexProcessesTitle"),
            Localization.Get("KillCodexProcessesBody"),
            Localization.Get("KillCodexProcessesButton"));
        if (confirmed) await ExecuteActionAsync("desktop", "kill", null, Localization.Get("KillCodexBusy"));
    }

    private async void RestartDesktopApp_Click(object sender, RoutedEventArgs e)
        => await ExecuteActionAsync("desktop", "restart", null, Localization.Get("RestartDesktopBusy"));

    private async void TailscaleServiceAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action }) return;
        await ExecuteActionAsync("tailscale", action, null, Localization.Format("RunningAction", ActionName(action), "Tailscale"));
    }

    private async void SwitchProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string providerId }) return;
        await ExecuteActionAsync("provider", "use", providerId, Localization.Format("SwitchingProvider", providerId));
    }

    private async void InstallNode_Click(object sender, RoutedEventArgs e)
    {
        var version = NewNodeVersionBox.Text.Trim().TrimStart('v');
        if (!Version.TryParse(version, out _) || version.Count(c => c == '.') != 2)
        {
            SetStatus(Localization.Get("FullNodeVersionHint"));
            NewNodeVersionBox.Focus(FocusState.Programmatic);
            return;
        }
        await ExecuteActionAsync("nvm", "install-node", version, Localization.Format("InstallingNode", version));
    }

    private async void SwitchNode_Click(object sender, RoutedEventArgs e)
    {
        if (NodeVersionPicker.SelectedItem is not NodeRuntime runtime)
        {
            SetStatus(Localization.Get("SelectNodeVersion"));
            return;
        }
        await ExecuteActionAsync("nvm", "use-node", runtime.Version, Localization.Format("SwitchingNode", runtime.Version));
    }

    private void OpenEgressCheck_Click(object sender, RoutedEventArgs e)
        => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(EgressWebsite));

    private async void CheckNetwork_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(Localization.Get("NetworkTesting"));
        var proxy = NetworkModePicker.SelectedValue as string == "custom" ? CustomHttpProxyBox.Text.Trim() : null;
        try
        {
            var probes = await SystemService.ProbeNetworkAsync(proxy);
            Replace(NetworkProbes, probes);
            var egress = await SystemService.QueryPublicEgressAsync(proxy);
            if (egress.Count > 0)
            {
                Replace(PublicEgressRoutes, egress);
                EgressEmptyText.Visibility = Visibility.Collapsed;
                OpenEgressWebsiteButton.IsEnabled = true;
            }
            SetStatus(Localization.Get("NetworkTestDone"));
        }
        catch (Exception ex)
        {
            SetStatus(Localization.Format("NetworkTestFailed", ex.Message));
        }
    }

    private async void AddProvider_Click(object sender, RoutedEventArgs e)
    {
        var provider = await ShowProviderDialogAsync(null);
        if (provider is null) return;
        ProviderProfiles.Add(provider);
        _systemService.SaveProviders(ProviderProfiles);
        UpdateProviderEmptyState();
        SetStatus(Localization.Get("ProviderSaved"));
    }

    private async void EditProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var existing = ProviderProfiles.FirstOrDefault(item => item.Id == id);
        if (existing is null) return;
        var updated = await ShowProviderDialogAsync(existing);
        if (updated is null) return;
        var index = ProviderProfiles.IndexOf(existing);
        ProviderProfiles[index] = updated;
        _systemService.SaveProviders(ProviderProfiles);
        SetStatus(Localization.Get("ProviderSaved"));
    }

    private async void DeleteProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var existing = ProviderProfiles.FirstOrDefault(item => item.Id == id);
        if (existing is null) return;
        if (!await ConfirmAsync(Localization.Get("DeleteProviderTitle"),
                Localization.Format("DeleteProviderBody", existing.Name), Localization.Get("Delete"))) return;
        ProviderProfiles.Remove(existing);
        _systemService.SaveProviders(ProviderProfiles);
        UpdateProviderEmptyState();
        SetStatus(Localization.Get("ProviderDeleted"));
    }

    private void UpdateProviderEmptyState()
        => ProviderEmptyState.Visibility = ProviderProfiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void TestProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var provider = ProviderProfiles.FirstOrDefault(item => item.Id == id);
        if (provider is null) return;
        SetStatus(Localization.Format("ProviderTesting", provider.Name, provider.TestModel));
        var result = await SystemService.TestProviderAsync(provider, provider.TestModel);
        SetStatus(result.DisplayText);
    }

    private async void TestSystemProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var provider = ModelProviders.FirstOrDefault(item => item.Id == id);
        if (provider is null) return;
        SetStatus($"正在测试 {provider.Name} (gpt-5.4)…");
        try
        {
            var profile = new ProviderProfile
            {
                Id = provider.Id,
                Name = provider.Name,
                BaseUrl = provider.BaseUrl,
                WireApi = provider.WireApi,
                TestModel = "gpt-5.4"
            };
            var matched = ProviderProfiles.FirstOrDefault(x => x.Id == id || x.BaseUrl == provider.BaseUrl);
            if (matched is not null) profile.ApiKey = matched.ApiKey;
            var result = await SystemService.TestProviderAsync(profile, "gpt-5.4");
            SetStatus(result.DisplayText);
        }
        catch (Exception ex)
        {
            SetStatus(Localization.Format("ProviderTestFailed", ex.Message));
        }
    }

    private async void CheckComponentUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string compId }) return;
        SetStatus(Localization.Format("CheckingComponentUpdate", compId));
        try
        {
            var proxyAddress = _settings.NetworkMode == "custom" ? _settings.CustomHttpProxy : null;
            var latestMap = await SystemService.QueryLatestVersionsAsync(proxyAddress);
            if (latestMap.TryGetValue(compId, out var latestVer))
            {
                var match = CoreComponents.Concat(InstallableComponents)
                    .FirstOrDefault(x => x.Id.Equals(compId, StringComparison.OrdinalIgnoreCase));
                if (match is not null) match.LatestVersion = latestVer;
                UpdateSpecializedComponentCards();
                SetStatus(Localization.Format("ComponentUpdateResult", compId, latestVer));
            }
            else
            {
                SetStatus(Localization.Format("ComponentUpdateUnknown", compId));
            }
        }
        catch (Exception ex)
        {
            SetStatus(Localization.Format("ComponentUpdateFailed", ex.Message));
        }
    }

    private async void UseStoredProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (!await ExecuteActionAsync("provider", "use-stored", id, Localization.Format("SwitchingProvider", id))) return;
        foreach (var item in ProviderProfiles) item.IsDefault = item.Id == id;
        _systemService.SaveProviders(ProviderProfiles);
        Replace(ProviderProfiles, _systemService.LoadProviders());
    }

    private async Task<ProviderProfile?> ShowProviderDialogAsync(ProviderProfile? existing)
    {
        var idBox = new TextBox { Header = "ID", Text = existing?.Id ?? "", PlaceholderText = "my-provider" };
        var nameBox = new TextBox { Header = Localization.Get("Name"), Text = existing?.Name ?? "" };
        var baseUrlBox = new TextBox { Header = "Base URL", Text = existing?.BaseUrl ?? "https://api.openai.com/v1" };
        var keyBox = new PasswordBox
        {
            Header = "API key",
            PasswordRevealMode = PasswordRevealMode.Peek,
            PlaceholderText = existing?.HasApiKey == true ? Localization.Get("ProviderKeepExistingKey") : "sk-…"
        };
        var wirePicker = new ComboBox { Header = "Wire API", Width = 260, SelectedValuePath = "Tag" };
        wirePicker.Items.Add(new ComboBoxItem { Content = "Responses API", Tag = "responses" });
        wirePicker.Items.Add(new ComboBoxItem { Content = "Chat Completions", Tag = "chat" });
        wirePicker.SelectedValue = existing?.WireApi == "chat" ? "chat" : "responses";
        var modelPicker = new ComboBox { Header = Localization.Get("TestModel"), Width = 260 };
        modelPicker.Items.Add("gpt-5.4");
        modelPicker.Items.Add("gpt-5.5");
        modelPicker.SelectedItem = existing?.TestModel == "gpt-5.5" ? "gpt-5.5" : "gpt-5.4";
        var fields = new StackPanel { Spacing = 10, Width = 520 };
        fields.Children.Add(idBox);
        fields.Children.Add(nameBox);
        fields.Children.Add(baseUrlBox);
        fields.Children.Add(keyBox);
        fields.Children.Add(wirePicker);
        fields.Children.Add(modelPicker);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = existing is null ? Localization.Get("AddProvider") : Localization.Get("EditProvider"),
            Content = fields,
            PrimaryButtonText = Localization.Get("Save"),
            CloseButtonText = Localization.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var id = idBox.Text.Trim();
        var baseUrl = baseUrlBox.Text.Trim();
        if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9_-]*$")
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || (existing is null && ProviderProfiles.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
        {
            SetStatus(Localization.Get("ProviderInvalid"));
            return null;
        }

        return new ProviderProfile
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(nameBox.Text) ? id : nameBox.Text.Trim(),
            BaseUrl = baseUrl.TrimEnd('/'),
            ApiKey = string.IsNullOrWhiteSpace(keyBox.Password) ? existing?.ApiKey ?? "" : keyBox.Password,
            WireApi = wirePicker.SelectedValue as string ?? "responses",
            TestModel = modelPicker.SelectedItem as string ?? "gpt-5.4",
            IsDefault = existing?.IsDefault == true
        };
    }

    private async void OpenCodexAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        var model = action == "test-model" && OpenCodexModelPicker.SelectedItem is OpenCodexModel selected
            ? $"{selected.Provider}\u001F{selected.Id}" : null;
        await ExecuteActionAsync("opencodex", action, model, Localization.Format("RunningAction", ActionName(action), "OpenCodex"));
    }

    private async void RelayAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        await ExecuteActionAsync("relay", action, null, Localization.Format("RunningAction", ActionName(action), "Codex Relay"));
    }

    private async Task<bool> ConfirmAsync(string title, string body, string confirmText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = body,
            PrimaryButtonText = confirmText,
            CloseButtonText = Localization.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ---------------------------------------------------------------- settings

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        PersistSettings(LanguagePicker.SelectedValue as string);
        var registry = NpmRegistryBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(registry) && Uri.TryCreate(registry, UriKind.Absolute, out _))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c npm config set registry {registry}")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    p?.WaitForExit(3000);
                }
                catch { }
            });
        }
        SetStatus(Localization.Get("SettingsSaved"));
    }

    private AppSettings PersistSettings(string? language, string? skippedVersion = null)
    {
        var seconds = double.IsNaN(RefreshSecondsBox.Value) ? 15 : (int)RefreshSecondsBox.Value;
        var defaults = new AppSettings();
        var next = new AppSettings
        {
            RefreshSeconds = Math.Clamp(seconds, 5, 300),
            RelayTaskName = string.IsNullOrWhiteSpace(RelayTaskNameBox.Text) ? "Codex Relay" : RelayTaskNameBox.Text.Trim(),
            ProxyTaskName = string.IsNullOrWhiteSpace(ProxyTaskNameBox.Text) ? "opencodex-proxy" : ProxyTaskNameBox.Text.Trim(),
            NodeMirror = NormalizeHttpEndpoint(NodeMirrorBox.Text, defaults.NodeMirror),
            NpmRegistry = NormalizeHttpEndpoint(NpmRegistryBox.Text, defaults.NpmRegistry),
            NetworkMode = NetworkModePicker.SelectedValue as string ?? "system",
            CustomHttpProxy = NormalizeHttpEndpoint(CustomHttpProxyBox.Text, ""),
            Language = language is "en-US" or "zh-CN" ? language : Localization.SystemLanguage,
            AutoCheckUpdates = AutoUpdateToggle.IsOn,
            SkippedVersion = skippedVersion ?? _settings.SkippedVersion
        };
        if (next.NetworkMode == "custom" && string.IsNullOrWhiteSpace(next.CustomHttpProxy)) next.NetworkMode = "system";
        _systemService.ApplyNetworkSettings(_settings, next);
        _settings = next;
        _systemService.SaveSettings(_settings);
        _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        return _settings;
    }

    private static string NormalizeHttpEndpoint(string value, string fallback)
    {
        var trimmed = value.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo)
            ? trimmed.TrimEnd('/')
            : fallback;
    }

    private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLanguage || LanguagePicker.SelectedValue is not string language) return;
        if (language == _settings.Language) return;

        PendingNavTag = (Nav.SelectedItem as Microsoft.UI.Xaml.Controls.NavigationViewItem)?.Tag as string;
        PersistSettings(language);
        Localization.ApplyLanguage(_settings.Language);
        ((App)Application.Current).ReloadMainWindow(this);
    }

    // ------------------------------------------------------------------ updates

    private void SetUpdateStatus(string message) => UpdateStatusText.Text = message;

    /// <summary>
    /// Queries the official release feed. Silent runs (startup, toggling the
    /// preference on) still surface a banner, but stay quiet about "up to date"
    /// and about failures so the window is not noisy on every launch.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_updateBusy || _refreshBusy || _actionBusy) return;

        // A finished download is a stronger state than a check result: keep the
        // "restart to apply" affordance instead of re-deriving it.
        if (_pendingUpdatePath is not null && _updateCheck is not null)
        {
            ShowUpdateAvailable(_updateCheck);
            return;
        }

        _updateBusy = true;
        CheckUpdateButton.IsEnabled = false;
        SetUpdateStatus(Localization.Get("UpdateChecking"));

        try
        {
            var check = await _updateService.CheckAsync();
            _updateCheck = check;

            if (!check.Succeeded)
            {
                SetUpdateStatus(Localization.Format(
                    "UpdateCheckFailed", Or(check.ErrorDetail ?? string.Empty)));
                ClearUpdateSurface(keepNotes: false);
                return;
            }

            if (!check.HasUpdate)
            {
                // Report the running version, not the remote tag: a local build can
                // legitimately be ahead of the newest published release.
                SetUpdateStatus(Localization.Format("UpdateUpToDate", check.CurrentVersion));
                ClearUpdateSurface(keepNotes: false);
                return;
            }

            ShowUpdateAvailable(check);
        }
        catch (Exception ex)
        {
            SetUpdateStatus(Localization.Format("UpdateCheckFailed", ex.Message));
        }
        finally
        {
            _updateBusy = false;
            CheckUpdateButton.IsEnabled = true;

            // ShowUpdateAvailable renders the download row while this check still
            // holds the busy flag, so it wrote IsEnabled = false and nothing ever
            // lifted it again - the settings-page download button stayed dead for
            // the rest of the session. Re-apply the state now that the check has
            // released the flag. A check cannot overlap a download (both guard on
            // the same flags), so this cannot re-enable a button mid-download.
            if (DownloadUpdateButton.Visibility == Visibility.Visible)
            {
                DownloadUpdateButton.IsEnabled = true;
            }
        }
    }

    /// <summary>Renders the "a newer release exists" state across banner and panel.</summary>
    private void ShowUpdateAvailable(UpdateCheckResult check)
    {
        var skipped = !string.IsNullOrEmpty(_settings.SkippedVersion)
            && check.LatestVersion == _settings.SkippedVersion;

        SkipVersionButton.Visibility = skipped ? Visibility.Collapsed : Visibility.Visible;
        ShowReleaseNotes(check);

        if (check.AssetMissing)
        {
            DownloadUpdateButton.Visibility = Visibility.Collapsed;
            SetUpdateStatus(Localization.Get("UpdateAssetMissing"));
        }
        else if (!_updateService.CanApplyInPlace)
        {
            DownloadUpdateButton.Visibility = Visibility.Collapsed;
            SetUpdateStatus(Localization.Get("UpdateNoLauncherHint"));
        }
        else
        {
            DownloadUpdateButton.Visibility = Visibility.Visible;
            DownloadUpdateButton.IsEnabled = !_updateBusy;
            DownloadUpdateButton.Content = _pendingUpdatePath is null
                ? Localization.GetXUid("DownloadUpdateButton", "Content")
                : Localization.Get("UpdateApplyButton");
            SetUpdateStatus(Localization.Format(
                "UpdateAvailableMessage", check.CurrentVersion, check.LatestVersion));
        }

        if (skipped)
        {
            UpdateInfoBar.IsOpen = false;
            return;
        }

        UpdateInfoBar.Title = Localization.Format("UpdateAvailableTitle", check.LatestVersion);
        UpdateInfoBar.Message = Localization.Format(
            "UpdateAvailableMessage", check.CurrentVersion, check.LatestVersion);
        UpdateInfoBarActionButton.Content = _updateService.CanApplyInPlace && !check.AssetMissing
            ? Localization.GetXUid("DownloadUpdateButton", "Content")
            : Localization.GetXUid("OpenReleaseButton", "Content");
        UpdateInfoBar.IsOpen = true;
    }

    /// <summary>Hides every transient update row, optionally keeping the notes.</summary>
    private void ClearUpdateSurface(bool keepNotes)
    {
        UpdateInfoBar.IsOpen = false;
        DownloadUpdateButton.Visibility = Visibility.Collapsed;
        SkipVersionButton.Visibility = Visibility.Collapsed;
        UpdateProgressBar.Visibility = Visibility.Collapsed;
        UpdateProgressText.Visibility = Visibility.Collapsed;
        UpdateProgressBar.IsIndeterminate = false;
        if (keepNotes) return;
        UpdateNotesHeadingText.Visibility = Visibility.Collapsed;
        UpdateNotesScroll.Visibility = Visibility.Collapsed;
        UpdatePublishedText.Visibility = Visibility.Collapsed;
    }

    private void ShowReleaseNotes(UpdateCheckResult check)
    {
        var notes = (check.ReleaseNotes ?? string.Empty).Trim();
        if (notes.Length > 4000) notes = notes[..4000];

        if (string.IsNullOrWhiteSpace(notes))
        {
            UpdateNotesHeadingText.Visibility = Visibility.Collapsed;
            UpdateNotesScroll.Visibility = Visibility.Collapsed;
        }
        else
        {
            UpdateNotesText.Text = notes;
            UpdateNotesHeadingText.Visibility = Visibility.Visible;
            UpdateNotesScroll.Visibility = Visibility.Visible;
        }

        var published = FormatPublished(check.PublishedAt);
        UpdatePublishedText.Text = published;
        UpdatePublishedText.Visibility = string.IsNullOrEmpty(published)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string FormatPublished(string raw)
        => DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? Localization.Format("UpdatePublishedAt", value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : string.Empty;

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        => await CheckForUpdatesAsync(false);

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdatePath is not null)
        {
            ApplyPendingUpdate();
            return;
        }

        await StartDownloadAsync();
    }

    private async Task StartDownloadAsync()
    {
        if (_updateCheck is not { HasUpdate: true, AssetMissing: false }) return;
        if (_updateBusy || _actionBusy || _refreshBusy) return;
        if (!_updateService.CanApplyInPlace)
        {
            SetUpdateStatus(Localization.Get("UpdateNoLauncherHint"));
            return;
        }

        _updateBusy = true;
        _actionBusy = true;
        _actionCancellation = new CancellationTokenSource();
        UpdateBusyUi();
        CheckUpdateButton.IsEnabled = false;
        DownloadUpdateButton.IsEnabled = false;
        SetUpdateStatus(Localization.Get("UpdateDownloading"));
        SetStatus(Localization.Get("UpdateDownloading"));
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.IsIndeterminate = true;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressText.Text = Palette.Dash;
        UpdateProgressText.Visibility = Visibility.Visible;

        var progress = new Progress<UpdateProgress>(OnDownloadProgress);
        string? downloaded = null;
        try
        {
            downloaded = await _updateService.DownloadAsync(_updateCheck, progress, _actionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetUpdateStatus(Localization.Get("ActionCanceled"));
        }
        catch (Exception ex)
        {
            SetUpdateStatus(Localization.Format("UpdateDownloadFailed", ex.Message));
        }
        finally
        {
            _updateBusy = false;
            _actionBusy = false;
            _actionCancellation?.Dispose();
            _actionCancellation = null;
            UpdateBusyUi();
            CheckUpdateButton.IsEnabled = true;
            DownloadUpdateButton.IsEnabled = true;
            UpdateProgressBar.IsIndeterminate = false;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateProgressText.Visibility = Visibility.Collapsed;
        }

        if (downloaded is null) return;

        _pendingUpdatePath = downloaded;
        SetUpdateStatus(Localization.Get("UpdateReady"));
        DownloadUpdateButton.Content = Localization.Get("UpdateApplyButton");
        SkipVersionButton.Visibility = Visibility.Collapsed;

        var confirmed = await ConfirmAsync(
            Localization.Get("UpdateRestartTitle"),
            Localization.Format("UpdateRestartBody", _updateCheck.LatestVersion),
            Localization.Get("UpdateRestartButton"));
        if (confirmed) ApplyPendingUpdate();
    }

    private void OnDownloadProgress(UpdateProgress progress)
    {
        UpdateProgressBar.IsIndeterminate = progress.Indeterminate;
        if (!progress.Indeterminate) UpdateProgressBar.Value = progress.Fraction;

        var text = Localization.Format(
            "UpdateDownloadProgress",
            FormatBytes(progress.Received),
            FormatBytes(progress.Total),
            FormatBytes((long)progress.BytesPerSecond));
        UpdateProgressText.Text = text;
        SetStatus(text);
    }

    /// <summary>
    /// Hands the swap to a detached helper and closes. The helper waits for this
    /// process to exit, replaces the single-file launcher, and starts it again;
    /// the refreshed launcher then re-expands its payload.
    /// </summary>
    private void ApplyPendingUpdate()
    {
        if (_pendingUpdatePath is null) return;
        SetUpdateStatus(Localization.Get("UpdateApplying"));
        SetStatus(Localization.Get("UpdateApplying"));
        try
        {
            _updateService.ApplyAndRestart(_pendingUpdatePath);
        }
        catch (Exception ex)
        {
            SetUpdateStatus(Localization.Format("UpdateApplyFailed", ex.Message));
            return;
        }

        Close();
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e) => OpenReleasePage();

    private void OpenReleasePage()
    {
        var url = _updateCheck is { } check && !string.IsNullOrWhiteSpace(check.ReleaseUrl)
            ? check.ReleaseUrl
            : _updateService.ReleasesPageUrl;
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private void SkipVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_updateCheck is null) return;
        PersistSettings(LanguagePicker.SelectedValue as string, _updateCheck.LatestVersion);
        UpdateInfoBar.IsOpen = false;
        SkipVersionButton.Visibility = Visibility.Collapsed;
        SetUpdateStatus(Localization.Format("UpdateSkipped", _updateCheck.LatestVersion));
    }

    private void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializingUpdate) return;
        PersistSettings(LanguagePicker.SelectedValue as string);
        if (AutoUpdateToggle.IsOn) _ = CheckForUpdatesAsync(true);
    }

    private async void UpdateInfoBarAction_Click(object sender, RoutedEventArgs e)
    {
        UpdateInfoBar.IsOpen = false;
        NavigateTo("settings");
        if (_updateCheck is null) return;

        if (_updateService.CanApplyInPlace && !_updateCheck.AssetMissing) await StartDownloadAsync();
        else OpenReleasePage();
    }

    private void NavigateTo(string tag)
    {
        foreach (var item in Nav.MenuItems)
        {
            if (item is not NavigationViewItem { Tag: string value } || value != tag) continue;
            Nav.SelectedItem = item;
            return;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return Palette.Dash;
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private static string ActionName(string action) => action switch
    {
        "start" => Localization.Get("ActionStartName"),
        "stop" => Localization.Get("ActionStopName"),
        "restart" => Localization.Get("ActionRestartName"),
        "install" => Localization.Get("ActionInstallName"),
        "upgrade" => Localization.Get("ActionUpgradeName"),
        "login" => Localization.Get("ActionLoginName"),
        "store" => Localization.Get("ActionStoreName"),
        _ => Localization.Get("ActionProcessName")
    };
}
