param(
    [switch]$IncludeLatest,
    [string]$SettingsPath = '',
    [ValidateSet('en-US','zh-CN')][string]$Language = 'en-US'
)

$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding

function Convert-UiText([string]$Text) {
    if ($Language -ne 'en-US' -or [string]::IsNullOrEmpty($Text)) { return $Text }
    $exact = @{
        '尚未查询远端版本' = 'Remote version not checked'; '远端版本清单暂时不可达' = 'The remote version manifest is temporarily unavailable'
        'Node.js 版本管理器' = 'Node.js version manager'; '安装前置条件' = 'Installation prerequisite'
        'Codex 桌面客户端' = 'Codex desktop app'; 'OpenAI 官方 Windows 应用' = 'Official OpenAI Windows app'
        'Codex 桌面应用' = 'Codex desktop app'; 'Codex 工具运行时' = 'Codex tool runtime'; 'Codex CLI / 子进程' = 'Codex CLI / child process'
        'Codex 客户端' = 'Codex clients'; '本机进程与当前配置' = 'Local processes and active configuration'
        '当前 Provider' = 'Active provider'; 'config.toml 的 model_provider' = 'model_provider in config.toml'
        '默认 Provider' = 'Default provider'; 'OpenAI 官方服务' = 'Official OpenAI service'
        'config.toml 未选择自定义 model_provider' = 'No custom model_provider is selected in config.toml'
        'Provider 端点' = 'Provider endpoint'; '当前 provider 的 base_url' = 'base_url of the active provider'
        '当前 Provider 上游' = 'Active provider upstream'; 'Codex 服务连接' = 'Codex service connection'
        '外网上游' = 'External upstream'; '当前 Provider 的已建立 TCP 连接（远端服务 IP）' = 'Established TCP connections from the active provider (remote service IPs)'
        '空闲时按需连接' = 'Connects on demand when active'; '采样时没有可归属的已建立连接' = 'No attributable established connection was observed during this sample'
        '备用端点' = 'Fallback endpoint'; '已配置，未证明参与当前路径' = 'Configured, but not proven to participate in the active path'
        '系统默认网络路径' = 'System default network path'; 'api64.ipify.org 从外部观察' = 'Observed externally by api64.ipify.org'
        '当前在线' = 'Online now'; '未知' = 'Unknown'; 'Windows 网络服务' = 'Windows network service'
        '未检测到 Codex 桌面客户端或 Codex CLI' = 'Codex desktop app and Codex CLI were not detected'
        'Codex 已安装，但当前 Provider 的本地端点未就绪' = 'Codex is installed, but the local endpoint for the active provider is not ready'
        'Codex 客户端管理已就绪；扩展模块为可选项' = 'Codex client management is ready; extension modules are optional'
        '为保护凭据，不展示进程参数' = 'Process arguments are hidden to protect credentials'
    }
    if ($exact.ContainsKey($Text)) { return $exact[$Text] }
    $Text = $Text -replace '正在运行', 'Running'
    $Text = $Text -replace '尚未查询远端版本', 'Remote version not checked'
    $Text = $Text -replace '远端版本清单暂时不可达', 'The remote version manifest is temporarily unavailable'
    $Text = $Text -replace '^同步 Microsoft Store 产品 (.+) · 清单 (.+)$', 'Synced Microsoft Store product $1 · manifest $2'
    $Text = $Text -replace '^(\d+) 个已安装的 Node\.js 版本 · ', '$1 installed Node.js version(s) · '
    $Text = $Text -replace '^未在 PATH 中检测到 nvm\.exe$', 'nvm.exe was not found on PATH'
    $Text = $Text -replace '^满足 Codex Relay 的最低要求 ≥ 22\.14\.0 · ', 'Meets the Codex Relay minimum requirement ≥ 22.14.0 · '
    $Text = $Text -replace '^版本过低；请先升级 Node\.js 至 22\.14\.0 或更高版本$', 'Version is too old; upgrade Node.js to 22.14.0 or newer'
    $Text = $Text -replace '^未在 PATH 中检测到 node\.exe$', 'node.exe was not found on PATH'
    $Text = $Text -replace '^包管理器可用 · ', 'Package manager available · '
    $Text = $Text -replace '^未在 PATH 中检测到 npm\.cmd，无法安装或升级组件$', 'npm.cmd was not found on PATH; components cannot be installed or upgraded'
    $Text = $Text -replace '^未检测到 OpenAI\.Codex 应用包$', 'The OpenAI.Codex app package was not detected'
    $Text = $Text -replace '^已安装，Codex 账号尚未登录$', 'Installed; the Codex account is signed out'
    $Text = $Text -replace '^正在运行$', 'Running'
    $Text = $Text -replace '^已安装，当前未运行$', 'Installed; currently stopped'
    $Text = $Text -replace '^未检测到全局 npm 包$', 'The global npm package was not detected'
    $Text = $Text -replace '^已安装，但尚未登录账号$', 'Installed, but signed out'
    $Text = $Text -replace '^已安装并已登录账号$', 'Installed and signed in'
    $Text = $Text -replace '^已安装；登录状态无法确认$', 'Installed; sign-in status could not be confirmed'
    $Text = $Text -replace '^监听端口 (.+) · 计划任务 (.+)$', 'Listening on port(s) $1 · scheduled task $2'
    $Text = $Text -replace '^包已安装，但计划任务尚未配置$', 'Package installed; scheduled task not configured'
    $Text = $Text -replace '^未检测到监听端口 · 计划任务 (.+)$', 'No listening port detected · scheduled task $1'
    $Text = $Text -replace '^127\.0\.0\.1:8787 正在监听 · 计划任务 (.+)$', '127.0.0.1:8787 is listening · scheduled task $1'
    $Text = $Text -replace '^包已安装；点击启动可使用官方后台模式$', 'Package installed; select Start to use the official background mode'
    $Text = $Text -replace '^端口 8787 未监听 · 计划任务 (.+)$', 'Port 8787 is not listening · scheduled task $1'
    $Text = $Text -replace '^未安装 Tailscale$', 'Tailscale is not installed'
    $Text = $Text -replace '^已安装，但尚未登录 Tailnet$', 'Installed, but signed out of the Tailnet'
    $Text = $Text -replace '^未读取到 Tailnet 状态$', 'Tailnet status could not be read'
    $Text = $Text -replace '^监听进程 PID (\d+)$', 'Listening process PID $1'
    return $Text
}

function Invoke-VersionCommand([string]$Command, [string[]]$Arguments) {
    try {
        $text = (& $Command @Arguments 2>$null | Select-Object -First 1).ToString().Trim()
        if ($LASTEXITCODE -eq 0 -and $text) { return ($text -replace '^[^0-9]*', '') }
    } catch {}
    return '—'
}

function Get-NpmGlobalPackageVersion([string]$PackageName) {
    try {
        $json = & $script:NpmExecutable list -g $PackageName --depth=0 --json 2>$null | Out-String | ConvertFrom-Json
        $entry = $json.dependencies.PSObject.Properties[$PackageName]
        if ($entry -and $entry.Value.version) { return [string]$entry.Value.version }
    } catch {}
    return '—'
}

function Get-NpmLatest([string]$PackageName) {
    if (-not $IncludeLatest) { return '—' }
    try {
        $value = (& $script:NpmExecutable view $PackageName version --json 2>$null | Out-String).Trim().Trim('"')
        if ($LASTEXITCODE -eq 0 -and $value -match '^\d+\.\d+') { return $value }
    } catch {}
    return '—'
}

function Get-GitHubLatest([string]$Repository) {
    if (-not $IncludeLatest) { return '—' }
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers @{ 'User-Agent' = 'Codex-Beacon' } -TimeoutSec 8 -UseBasicParsing
        if ($release.tag_name) { return ([string]$release.tag_name).TrimStart('v') }
    } catch {}
    return '—'
}

function Get-TaskState([string]$Name) {
    $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if ($task) { return [string]$task.State }
    return 'NotInstalled'
}

function Test-Port([int]$Port) {
    return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function New-Component($Id, $Name, $Kind, $State, $Detail, $Installed, $Latest, $Manage, $Running = $false, $Account = 'NotApplicable') {
    [ordered]@{
        Id = $Id; Name = $Name; Kind = $Kind; State = $State; Detail = $Detail
        InstalledVersion = $Installed; LatestVersion = $Latest
        IsInstalled = ($Installed -ne '—'); IsRunning = [bool]$Running
        AccountState = [string]$Account; CanManageService = [bool]$Manage
    }
}

$settings = @{ RelayTaskName = 'Codex Relay'; ProxyTaskName = 'opencodex-proxy' }
if ($SettingsPath -and (Test-Path -LiteralPath $SettingsPath)) {
    try {
        $saved = Get-Content -Raw -LiteralPath $SettingsPath -Encoding UTF8 | ConvertFrom-Json
        if ($saved.RelayTaskName) { $settings.RelayTaskName = $saved.RelayTaskName }
        if ($saved.ProxyTaskName) { $settings.ProxyTaskName = $saved.ProxyTaskName }
    } catch {}
}

$nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
$nvmCommand = Get-Command nvm.exe -ErrorAction SilentlyContinue
$nvmVersion = if ($nvmCommand) { Invoke-VersionCommand $nvmCommand.Source @('version') } else { '—' }
$nodeVersions = @()
if ($nvmCommand) {
    try {
        foreach ($line in (& $nvmCommand.Source list 2>$null)) {
            if ($line -match '^\s*(\*)?\s*(\d+\.\d+\.\d+)') {
                $nodeVersions += [ordered]@{ Version = $matches[2]; IsCurrent = ($matches[1] -eq '*') }
            }
        }
    } catch {}
}
$script:NpmExecutable = if ($nodeCommand) { Join-Path (Split-Path $nodeCommand.Source -Parent) 'npm.cmd' } else { '' }
$npmCommand = if ($script:NpmExecutable -and (Test-Path -LiteralPath $script:NpmExecutable)) { Get-Item -LiteralPath $script:NpmExecutable } else { $null }
$nodeVersion = '—'
if ($nodeCommand) {
    try {
        $nodeVersionText = (& $nodeCommand.Source --version 2>$null | Select-Object -First 1)
        if ($nodeVersionText) { $nodeVersion = $nodeVersionText.ToString().Trim() -replace '^[^0-9]*','' }
    } catch {}
}
$npmVersion = '—'
if ($npmCommand) {
    try {
        $npmPackageJson = Join-Path (Split-Path $nodeCommand.Source -Parent) 'node_modules\npm\package.json'
        if (Test-Path -LiteralPath $npmPackageJson) {
            $npmVersion = [string](Get-Content -Raw -LiteralPath $npmPackageJson -Encoding UTF8 | ConvertFrom-Json).version
        } else {
            $npmVersionText = (& $script:NpmExecutable --version 2>$null | Select-Object -First 1)
            if ($npmVersionText) { $npmVersion = $npmVersionText.ToString().Trim() }
        }
    } catch {}
}
$nodeOk = $false
if ($nodeVersion -ne '—') {
    try { $nodeOk = [version]($nodeVersion -replace '-.*$', '') -ge [version]'22.14.0' } catch {}
}
$npmOk = $npmVersion -ne '—'

$codexVersion = if ($npmOk) { Get-NpmGlobalPackageVersion '@openai/codex' } else { '—' }
$codexAccount = 'NotApplicable'
if ($codexVersion -ne '—') {
    try {
        $loginStatus = (& cmd.exe /d /c 'codex login status 2>&1' | Out-String).Trim()
        $codexAccount = if ($loginStatus -match '(?i)logged in using') { 'SignedIn' } elseif ($loginStatus -match '(?i)not logged in') { 'SignedOut' } else { 'Unknown' }
    } catch { $codexAccount = 'Unknown' }
}
$openCodexVersion = if ($npmOk) { Get-NpmGlobalPackageVersion '@bitkyc08/opencodex' } else { '—' }
$relayPackage = Join-Path $env:USERPROFILE '.codex-relay\app\node_modules\codex-relay\package.json'
$relayVersion = '—'
if (Test-Path -LiteralPath $relayPackage) {
    try { $relayVersion = [string](Get-Content -Raw -LiteralPath $relayPackage -Encoding UTF8 | ConvertFrom-Json).version } catch {}
}

$relayTaskState = Get-TaskState $settings.RelayTaskName
$proxyTaskState = Get-TaskState $settings.ProxyTaskName
$relayListening = Test-Port 8787
$proxyPorts = @(@(10100) | Where-Object { Test-Port $_ })

$tailVersion = Invoke-VersionCommand 'tailscale.exe' @('version')
$tailService = Get-Service -Name 'Tailscale' -ErrorAction SilentlyContinue
$tailJson = $null
try { $tailJson = (& tailscale.exe status --json 2>$null | Out-String | ConvertFrom-Json) } catch {}
$tailAccount = if ($tailVersion -eq '—') { 'NotApplicable' } elseif ($tailJson -and $tailJson.BackendState -eq 'Running') { 'SignedIn' } elseif ($tailJson -and $tailJson.BackendState -in @('NeedsLogin','NoState')) { 'SignedOut' } else { 'Unknown' }
$desktopPackage = Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue | Select-Object -First 1
$desktopVersion = if ($desktopPackage) { [string]$desktopPackage.Version } else { '—' }
$desktopRunning = [bool](Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ExecutablePath -match '(?i)WindowsApps[\\/]OpenAI\.Codex_[^\\/]+' -and $_.Name -eq 'ChatGPT.exe'
} | Select-Object -First 1)
$desktopLatest = '—'
$desktopVersionEvidence = '尚未查询远端版本'
if ($IncludeLatest) {
    try {
        $codexManifest = Invoke-RestMethod -Uri 'https://codexapp.agentsmirror.com/latest/manifest' -TimeoutSec 10 -UseBasicParsing
        if ($codexManifest.sources.windows.version) {
            $desktopLatest = [string]$codexManifest.sources.windows.version
            $desktopVersionEvidence = "同步 Microsoft Store 产品 $($codexManifest.sources.windows.productId) · 清单 $($codexManifest.generatedAt)"
        }
    } catch { $desktopVersionEvidence = '远端版本清单暂时不可达' }
}

$components = @()
$components += New-Component 'nvm' 'NVM for Windows' 'Node.js 版本管理器' $(if ($nvmCommand) { 'Healthy' } else { 'Unavailable' }) $(if ($nvmCommand) { "$($nodeVersions.Count) 个已安装的 Node.js 版本 · $($nvmCommand.Source)" } else { '未在 PATH 中检测到 nvm.exe' }) $nvmVersion $(Get-GitHubLatest 'coreybutler/nvm-windows') $false ([bool]$nvmCommand)
$components += New-Component 'node' 'Node.js' '安装前置条件' $(if ($nodeOk) { 'Healthy' } elseif ($nodeCommand) { 'Warning' } else { 'Unavailable' }) $(if ($nodeOk) { "满足 Codex Relay 的最低要求 ≥ 22.14.0 · $($nodeCommand.Source)" } elseif ($nodeCommand) { '版本过低；请先升级 Node.js 至 22.14.0 或更高版本' } else { '未在 PATH 中检测到 node.exe' }) $nodeVersion '—' $false ([bool]$nodeOk)
$components += New-Component 'npm' 'npm' '安装前置条件' $(if ($npmOk) { 'Healthy' } else { 'Unavailable' }) $(if ($npmOk) { "包管理器可用 · $script:NpmExecutable" } else { '未在 PATH 中检测到 npm.cmd，无法安装或升级组件' }) $npmVersion '—' $false ([bool]$npmOk)
$desktopAccount = if ($desktopPackage -and $codexAccount -in @('SignedIn','SignedOut')) { $codexAccount } else { 'Unknown' }
$components += New-Component 'desktop' 'Codex 桌面客户端' 'OpenAI 官方 Windows 应用' $(if (-not $desktopPackage) { 'Unavailable' } elseif ($desktopAccount -eq 'SignedOut') { 'Warning' } elseif ($desktopRunning) { 'Healthy' } else { 'Stopped' }) "$(if (-not $desktopPackage) { '未检测到 OpenAI.Codex 应用包' } elseif ($desktopAccount -eq 'SignedOut') { '已安装，Codex 账号尚未登录' } elseif ($desktopRunning) { '正在运行' } else { '已安装，当前未运行' }) · $desktopVersionEvidence" $desktopVersion $desktopLatest $true $desktopRunning $desktopAccount
$components += New-Component 'codex' 'Codex CLI' 'npm · @openai/codex' $(if ($codexVersion -eq '—') { 'Unavailable' } elseif ($codexAccount -eq 'SignedOut') { 'Warning' } else { 'Healthy' }) $(if ($codexVersion -eq '—') { '未检测到全局 npm 包' } elseif ($codexAccount -eq 'SignedOut') { '已安装，但尚未登录账号' } elseif ($codexAccount -eq 'SignedIn') { '已安装并已登录账号' } else { '已安装；登录状态无法确认' }) $codexVersion $(Get-NpmLatest '@openai/codex') $false ($codexVersion -ne '—') $codexAccount
$components += New-Component 'opencodex' 'OpenCodex Proxy' 'npm · @bitkyc08/opencodex' $(if ($proxyPorts.Count -gt 0) { 'Healthy' } elseif ($proxyTaskState -eq 'Running') { 'Warning' } elseif ($openCodexVersion -eq '—') { 'Unavailable' } else { 'Stopped' }) $(if ($proxyPorts.Count -gt 0) { "监听端口 $($proxyPorts -join ', ') · 计划任务 $proxyTaskState" } elseif ($proxyTaskState -eq 'NotInstalled' -and $openCodexVersion -ne '—') { '包已安装，但计划任务尚未配置' } else { "未检测到监听端口 · 计划任务 $proxyTaskState" }) $openCodexVersion $(Get-NpmLatest '@bitkyc08/opencodex') $true ($proxyPorts.Count -gt 0)
$components += New-Component 'relay' 'Codex Relay' 'npm · codex-relay' $(if ($relayListening) { 'Healthy' } elseif ($relayTaskState -eq 'Running') { 'Warning' } elseif ($relayVersion -eq '—') { 'Unavailable' } else { 'Stopped' }) $(if ($relayListening) { "127.0.0.1:8787 正在监听 · 计划任务 $relayTaskState" } elseif ($relayTaskState -eq 'NotInstalled' -and $relayVersion -ne '—') { '包已安装；点击启动可使用官方后台模式' } else { "端口 8787 未监听 · 计划任务 $relayTaskState" }) $relayVersion $(Get-NpmLatest 'codex-relay') $true $relayListening
$components += New-Component 'tailscale' 'Tailscale' 'Windows 网络服务' $(if ($tailVersion -eq '—') { 'Unavailable' } elseif ($tailAccount -eq 'SignedOut') { 'Warning' } elseif ($tailJson -and $tailJson.Self.Online) { 'Healthy' } else { 'Stopped' }) $(if ($tailVersion -eq '—') { '未安装 Tailscale' } elseif ($tailAccount -eq 'SignedOut') { '已安装，但尚未登录 Tailnet' } elseif ($tailJson) { "$($tailJson.Self.HostName) · $($tailJson.Self.TailscaleIPs -join ', ')" } else { '未读取到 Tailnet 状态' }) $tailVersion $(Get-GitHubLatest 'tailscale/tailscale') $true ($tailService.Status -eq 'Running') $tailAccount

# Include explicit Codex processes and descendants of the Codex desktop process.
$allProcesses = @(Get-CimInstance Win32_Process)
$byId = @{}
foreach ($p in $allProcesses) { $byId[[int]$p.ProcessId] = $p }
function Test-CodexProcess($Process) {
    if (-not $Process) { return $false }
    $identity = "$($Process.Name) $($Process.ExecutablePath) $($Process.CommandLine)"
    if ($identity -match 'CodexBeacon|CodexServiceManager|Collect-CodexStatus') { return $false }
    if ($identity -match '(?i)OpenAI\.Codex|@openai[\\/]codex|@bitkyc08[\\/]opencodex|codex-relay|codex\.exe|codex-cli|node_repl') { return $true }
    $parent = $byId[[int]$Process.ParentProcessId]
    if ($parent) {
        $parentIdentity = "$($parent.Name) $($parent.ExecutablePath) $($parent.CommandLine)"
        return $parentIdentity -match '(?i)OpenAI\.Codex|@openai[\\/]codex|@bitkyc08[\\/]opencodex|codex-relay'
    }
    return $false
}

$processes = @()
foreach ($p in $allProcesses) {
    if (-not (Test-CodexProcess $p)) { continue }
    $identity = "$($p.Name) $($p.ExecutablePath) $($p.CommandLine)"
    $role = if ($identity -match '(?i)codex-relay') { 'Codex Relay' } elseif ($identity -match '(?i)opencodex|10100|51863') { 'OpenCodex Proxy' } elseif ($identity -match '(?i)OpenAI\.Codex') { 'Codex 桌面应用' } elseif ($identity -match '(?i)node_repl') { 'Codex 工具运行时' } else { 'Codex CLI / 子进程' }
    $version = if ($role -eq 'Codex Relay') { $relayVersion } elseif ($role -eq 'OpenCodex Proxy') { $openCodexVersion } elseif ($role -like 'Codex CLI*') { $codexVersion } else { '—' }
    if ($p.ExecutablePath -and (Test-Path -LiteralPath $p.ExecutablePath)) {
        try {
            $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($p.ExecutablePath).FileVersion
            if ($version -eq '—' -and $fileVersion) { $version = $fileVersion }
        } catch {}
    }
    $started = '—'
    try { $started = (Get-Process -Id $p.ProcessId).StartTime.ToString('MM-dd HH:mm:ss') } catch {}
    $processes += [ordered]@{
        Pid = [int]$p.ProcessId; ParentPid = [int]$p.ParentProcessId; Name = [string]$p.Name; Role = $role
        Version = $version; Architecture = $(if ($p.ExecutablePath -match 'Program Files \(x86\)') { 'x86' } else { 'x64' })
        StartedAt = $started; ExecutablePath = $(if ($p.ExecutablePath) { [string]$p.ExecutablePath } else { '—' })
        CommandLine = '为保护凭据，不展示进程参数'
    }
}

$codexConfig = Join-Path $env:USERPROFILE '.codex\config.toml'
$configLines = if (Test-Path -LiteralPath $codexConfig) { @(Get-Content -LiteralPath $codexConfig -Encoding UTF8) } else { @() }
$selectedProvider = ''
foreach ($line in $configLines) { if ($line -match '^\s*model_provider\s*=\s*["'']([^"'']+)["'']') { $selectedProvider = $matches[1]; break } }
$activeUrl = ''
$activeSection = $false
foreach ($line in $configLines) {
    if ($line -match '^\s*\[model_providers\.([^\]]+)\]') { $activeSection = $matches[1] -eq $selectedProvider; continue }
    if ($activeSection -and $line -match '^\s*base_url\s*=\s*["'']([^"'']+)["'']') { $activeUrl = $matches[1]; break }
}
$proxyHops = @([ordered]@{ Order = 1; Name = 'Codex 客户端'; Address = 'OpenAI.Codex / Codex CLI'; State = 'Healthy'; Evidence = '本机进程与当前配置' })
if ($selectedProvider) {
    $proxyHops += [ordered]@{ Order = 2; Name = '当前 Provider'; Address = $selectedProvider; State = 'Healthy'; Evidence = 'config.toml 的 model_provider' }
} else {
    $proxyHops += [ordered]@{ Order = 2; Name = '默认 Provider'; Address = 'OpenAI 官方服务'; State = 'Healthy'; Evidence = 'config.toml 未选择自定义 model_provider' }
}
$activeOwnerPid = 0
if ($activeUrl) {
    $uri = $null; try { $uri = [uri]$activeUrl } catch {}
    $listener = if ($uri -and $uri.IsLoopback) { Get-NetTCPConnection -LocalPort $uri.Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 } else { $null }
    $owner = if ($listener) { Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)" -ErrorAction SilentlyContinue } else { $null }
    if ($owner) { $activeOwnerPid = [int]$owner.ProcessId }
    $proxyHops += [ordered]@{ Order = $proxyHops.Count + 1; Name = $(if ($owner) { [IO.Path]::GetFileNameWithoutExtension($owner.Name) } else { 'Provider 端点' }); Address = $activeUrl; State = $(if ($listener -or ($uri -and -not $uri.IsLoopback)) { 'Healthy' } else { 'Warning' }); Evidence = $(if ($owner) { "监听进程 PID $($owner.ProcessId)" } else { '当前 provider 的 base_url' }) }
}

$externalConnections = @()
$relevantPids = @($processes | ForEach-Object { $_.Pid })
if ($activeOwnerPid) { $relevantPids += $activeOwnerPid }
foreach ($connection in (Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue)) {
    if ($connection.OwningProcess -notin $relevantPids) { continue }
    if ($connection.RemoteAddress -match '^(127\.|::1$|0\.0\.0\.0$)') { continue }
    $proc = $byId[[int]$connection.OwningProcess]
    $role = if ($connection.OwningProcess -eq $activeOwnerPid) { '当前 Provider 上游' } else { 'Codex 服务连接' }
    $externalConnections += [ordered]@{ ProcessName = [string]$proc.Name; Pid = [int]$connection.OwningProcess; Role = $role; RemoteAddress = [string]$connection.RemoteAddress; RemotePort = [int]$connection.RemotePort; State = [string]$connection.State }
}
$externalConnections = @($externalConnections | Sort-Object Pid,RemoteAddress,RemotePort -Unique)
$providerConnections = @($externalConnections | Where-Object Role -eq '当前 Provider 上游')
if ($providerConnections.Count -gt 0) {
    $addresses = @($providerConnections | Select-Object -ExpandProperty RemoteAddress -Unique)
    $proxyHops += [ordered]@{ Order = $proxyHops.Count + 1; Name = '外网上游'; Address = ($addresses -join ', '); State = 'Healthy'; Evidence = '当前 Provider 的已建立 TCP 连接（远端服务 IP）' }
} else {
    $proxyHops += [ordered]@{ Order = $proxyHops.Count + 1; Name = '外网上游'; Address = '空闲时按需连接'; State = 'Warning'; Evidence = '采样时没有可归属的已建立连接' }
}

$candidateEndpoints = @()
$seenEndpoints = @{}
if ($activeUrl) { $seenEndpoints[$activeUrl] = $true }
$sectionName = ''
foreach ($line in $configLines) {
    if ($line -match '^\s*\[model_providers\.([^\]]+)\]') { $sectionName = $matches[1]; continue }
    if ($line -match '^\s*(?:base_url|openai_base_url|experimental_realtime_ws_base_url)\s*=\s*["'']([^"'']+)["'']') {
        $url = $matches[1]; if ($seenEndpoints[$url]) { continue }; $seenEndpoints[$url] = $true
        $candidateEndpoints += [ordered]@{ Order = $candidateEndpoints.Count + 1; Name = $(if ($sectionName) { $sectionName } else { '备用端点' }); Address = $url; State = 'Warning'; Evidence = '已配置，未证明参与当前路径' }
    }
}

$publicEgress = @()
if ($IncludeLatest) {
    try {
        $ipResult = Invoke-RestMethod -Uri 'https://api64.ipify.org?format=json' -TimeoutSec 6 -UseBasicParsing
        if ($ipResult.ip) { $publicEgress += [ordered]@{ Address = [string]$ipResult.ip; Route = '系统默认网络路径'; Evidence = 'api64.ipify.org 从外部观察'; CheckedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') } }
    } catch {}
}

$tailDevices = @()
if ($tailJson) {
    $nodes = @($tailJson.Self) + @($tailJson.Peer.PSObject.Properties.Value)
    foreach ($device in $nodes) {
        if (-not $device) { continue }
        $tailDevices += [ordered]@{
            Name = [string]$device.HostName; DnsName = [string]$device.DNSName; OS = [string]$device.OS
            Addresses = [string]($device.TailscaleIPs -join ', '); Online = [bool]$device.Online
            IsSelf = ($device.ID -eq $tailJson.Self.ID); LastSeen = $(if ($device.Online) { '当前在线' } elseif ($device.LastSeen) { ([datetime]$device.LastSeen).ToLocalTime().ToString('yyyy-MM-dd HH:mm') } else { '未知' })
        }
    }
}

$coreInstalled = [bool]$desktopPackage -or $codexVersion -ne '—'
$routeNeedsAttention = $activeUrl -and $activeUrl -match '^https?://(?:localhost|127\.0\.0\.1|\[::1\])' -and -not $activeOwnerPid
$snapshot = [ordered]@{
    CollectedAt = (Get-Date).ToString('o')
    OverallState = $(if (-not $coreInstalled) { 'Stopped' } elseif ($routeNeedsAttention) { 'Warning' } else { 'Healthy' })
    OverallMessage = $(if (-not $coreInstalled) { '未检测到 Codex 桌面客户端或 Codex CLI' } elseif ($routeNeedsAttention) { 'Codex 已安装，但当前 Provider 的本地端点未就绪' } else { 'Codex 客户端管理已就绪；扩展模块为可选项' })
    Components = $components; Processes = $processes; ProxyChain = $proxyHops; CandidateEndpoints = $candidateEndpoints; ExternalConnections = $externalConnections; PublicEgress = $publicEgress; TailscaleDevices = $tailDevices; NodeVersions = $nodeVersions; Error = $null
}
if ($Language -eq 'en-US') {
    $snapshot.OverallMessage = Convert-UiText $snapshot.OverallMessage
    foreach ($item in $snapshot.Components) { $item.Name = Convert-UiText $item.Name; $item.Kind = Convert-UiText $item.Kind; $item.Detail = Convert-UiText $item.Detail }
    foreach ($item in $snapshot.Processes) { $item.Role = Convert-UiText $item.Role; $item.CommandLine = Convert-UiText $item.CommandLine }
    foreach ($item in $snapshot.ProxyChain) { $item.Name = Convert-UiText $item.Name; $item.Address = Convert-UiText $item.Address; $item.Evidence = Convert-UiText $item.Evidence }
    foreach ($item in $snapshot.CandidateEndpoints) { $item.Name = Convert-UiText $item.Name; $item.Evidence = Convert-UiText $item.Evidence }
    foreach ($item in $snapshot.ExternalConnections) { $item.Role = Convert-UiText $item.Role }
    foreach ($item in $snapshot.PublicEgress) { $item.Route = Convert-UiText $item.Route; $item.Evidence = Convert-UiText $item.Evidence }
    foreach ($item in $snapshot.TailscaleDevices) { $item.LastSeen = Convert-UiText $item.LastSeen }
}
$snapshot | ConvertTo-Json -Depth 7 -Compress
