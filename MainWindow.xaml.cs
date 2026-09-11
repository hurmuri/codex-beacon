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
    private bool _initializingLanguage = true;
    private AppSettings _settings;

    public ObservableCollection<ComponentStatus> Components { get; } = [];
    public ObservableCollection<ComponentStatus> CoreComponents { get; } = [];
    public ObservableCollection<FlowStep> ModelFlowSteps { get; } = [];
    public ObservableCollection<FlowStep> NetworkFlowSteps { get; } = [];
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
        LanguagePicker.SelectedValue = _settings.Language;
        if (LanguagePicker.SelectedIndex < 0) LanguagePicker.SelectedValue = Localization.SystemLanguage;
        _initializingLanguage = false;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1320, 860));

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        _timer.Tick += async (_, _) => await RefreshAsync(false);
        Activated += MainWindow_Activated;
        Closed += (_, _) => _timer.Stop();
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
        SetBusy(true, Localization.Get(includeLatest ? "CheckingLatest" : "RefreshingStatus"));
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
            StatusText.Text = snapshot.Error is null ? Localization.Get("CheckComplete") : snapshot.Error;
        }
        catch (Exception ex)
        {
            StatusText.Text = Localization.Format("RefreshFailed", ex.Message);
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
        Replace(CoreComponents, snapshot.Components.Where(x => x.Id is "desktop" or "codex" or "opencodex" or "relay"));
        Replace(ModelFlowSteps, snapshot.ModelFlow);
        Replace(NetworkFlowSteps, snapshot.NetworkFlow);
        Replace(Processes, snapshot.Processes.OrderBy(x => x.Role).ThenBy(x => x.Pid));
        Replace(ProxyHops, snapshot.ProxyChain.OrderBy(x => x.Order));
        Replace(TailnetDevices, snapshot.TailscaleDevices.OrderByDescending(x => x.IsSelf).ThenByDescending(x => x.Online).ThenBy(x => x.Name));
        Replace(NodeVersions, snapshot.NodeVersions.OrderByDescending(x => x.IsCurrent).ThenByDescending(x => Version.TryParse(x.Version, out var version) ? version : new Version()));
        Replace(CandidateEndpoints, snapshot.CandidateEndpoints);
        Replace(ExternalConnections, snapshot.ExternalConnections);
        if (snapshot.PublicEgress.Count > 0) Replace(PublicEgressRoutes, snapshot.PublicEgress);

        OverallMessageText.Text = snapshot.OverallMessage;
        PulseTimestampText.Text = snapshot.CollectedAt == default ? Localization.Get("SampleFailed") : Localization.Format("SampleAt", snapshot.CollectedAt.ToLocalTime());
        RefreshAgeText.Text = snapshot.CollectedAt == default ? Localization.Get("DataUnavailable") : Localization.Format("UpdatedAt", snapshot.CollectedAt.ToLocalTime());
        NodeRequirementText.Text = node is null ? Localization.Get("MissingNodeResult") : $"{node.StatusLabel} · {node.InstalledVersion}\n{node.Detail}";
        NpmRequirementText.Text = npm is null ? Localization.Get("MissingNpmResult") : $"{npm.StatusLabel} · {npm.InstalledVersion}\n{npm.Detail}";
        NvmRequirementText.Text = nvm is null ? Localization.Get("MissingNvmResult") : $"{nvm.StatusLabel} · {nvm.InstalledVersion}\n{nvm.Detail}";
        if (NodeVersionPicker.SelectedItem is null && NodeVersions.Count > 0) NodeVersionPicker.SelectedIndex = 0;

        var online = snapshot.TailscaleDevices.Count(x => x.Online);
        var self = snapshot.TailscaleDevices.FirstOrDefault(x => x.IsSelf);
        TailnetSummaryText.Text = self is null ? Localization.Get("TailscaleDisconnected") : Localization.Format("TailnetConnected", self.Name);
        TailnetDetailText.Text = self is null ? Localization.Get("TailscaleSignInHint") : Localization.Format("TailnetOnlineCount", online, self.Addresses);
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
        TailscalePage.Visibility = tag == "tailscale" ? Visibility.Visible : Visibility.Collapsed;
        InstallationsPage.Visibility = tag == "installations" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ServiceAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action, DataContext: ComponentStatus component }) return;
        await RunActionAsync(component.Id, action, Localization.Format("RunningAction", ActionName(action), component.Name));
    }

    private async void PackageAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action, DataContext: ComponentStatus component }) return;
        if (action == "install" ? !component.CanInstall : !component.CanUpgrade)
        {
            StatusText.Text = Localization.Get("PrerequisiteHint");
            return;
        }
        await RunActionAsync(component.Id, action, Localization.Format("RunningAction", ActionName(action), component.Name));
    }

    private async void DangerAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action }) return;
        var restarting = action == "restart";
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Localization.Get(restarting ? "RestartServicesTitle" : "TerminateServicesTitle"),
            Content = Localization.Get(restarting ? "RestartServicesBody" : "TerminateServicesBody"),
            PrimaryButtonText = Localization.Get(restarting ? "RestartAllButton" : "TerminateServicesButton"),
            CloseButtonText = Localization.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await RunActionAsync("all", action == "kill" ? "kill" : "restart", Localization.Get(restarting ? "RestartingServices" : "TerminatingServices"));
    }

    private async void KillCodexProcesses_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Localization.Get("KillCodexProcessesTitle"),
            Content = Localization.Get("KillCodexProcessesBody"),
            PrimaryButtonText = Localization.Get("KillCodexProcessesButton"),
            CloseButtonText = Localization.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await RunActionAsync("desktop", "kill", Localization.Get("KillCodexBusy"));
        }
    }

    private async void RestartDesktopApp_Click(object sender, RoutedEventArgs e)
    {
        await RunActionAsync("desktop", "restart", Localization.Get("RestartDesktopBusy"));
    }

    private async void TailscaleServiceAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action }) return;
        await RunActionAsync("tailscale", action, Localization.Format("RunningAction", ActionName(action), "Tailscale"));
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
            StatusText.Text = Localization.Get("FullNodeVersionHint");
            NewNodeVersionBox.Focus(FocusState.Programmatic);
            return;
        }
        await RunVersionActionAsync("install-node", version, Localization.Format("InstallingNode", version));
    }

    private async void SwitchNode_Click(object sender, RoutedEventArgs e)
    {
        if (NodeVersionPicker.SelectedItem is not NodeRuntime runtime)
        {
            StatusText.Text = Localization.Get("SelectNodeVersion");
            return;
        }
        await RunVersionActionAsync("use-node", runtime.Version, Localization.Format("SwitchingNode", runtime.Version));
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
        var language = LanguagePicker.SelectedValue as string ?? Localization.SystemLanguage;
        _settings = new AppSettings
        {
            RefreshSeconds = Math.Clamp(seconds, 5, 300),
            RelayTaskName = string.IsNullOrWhiteSpace(RelayTaskNameBox.Text) ? "Codex Relay" : RelayTaskNameBox.Text.Trim(),
            ProxyTaskName = string.IsNullOrWhiteSpace(ProxyTaskNameBox.Text) ? "opencodex-proxy" : ProxyTaskNameBox.Text.Trim(),
            Language = language
        };
        _systemService.SaveSettings(_settings);
        _timer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        StatusText.Text = Localization.Get("SettingsSaved");
    }

    private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLanguage || LanguagePicker.SelectedValue is not string language || language == _settings.Language) return;
        var seconds = double.IsNaN(RefreshSecondsBox.Value) ? 15 : (int)RefreshSecondsBox.Value;
        _settings = new AppSettings
        {
            RefreshSeconds = Math.Clamp(seconds, 5, 300),
            RelayTaskName = string.IsNullOrWhiteSpace(RelayTaskNameBox.Text) ? "Codex Relay" : RelayTaskNameBox.Text.Trim(),
            ProxyTaskName = string.IsNullOrWhiteSpace(ProxyTaskNameBox.Text) ? "opencodex-proxy" : ProxyTaskNameBox.Text.Trim(),
            Language = language
        };
        _systemService.SaveSettings(_settings);
        Localization.ApplyLanguage(language);
        ((App)Application.Current).ReloadMainWindow(this);
    }

    private static string ActionName(string action) => action switch
    {
        "start" => Localization.Get("ActionStartName"),
        "stop" => Localization.Get("ActionStopName"),
        "restart" => Localization.Get("ActionRestartName"),
        "install" => Localization.Get("ActionInstallName"),
        "upgrade" => Localization.Get("ActionUpgradeName"),
        "login" => Localization.Get("ActionLoginName"),
        _ => Localization.Get("ActionProcessName")
    };

}
