param(
    [switch]$IncludeLatest,
    [string]$SettingsPath = ''
)

# ---------------------------------------------------------------------------
# Codex Beacon status collector
#
# Contract: this script must never emit user-facing prose. Every human-readable
# sentence is produced by the WinUI layer from resources.resw, driven by the
# semantic keys collected here. That keeps localization in exactly one place.
#
#   DetailKey  - a resource key
#   DetailArgs - argument values joined by U+001F. An argument prefixed with
#                '@' is itself a resource key and is resolved before formatting.
#
# Remote/expensive lookups run only when -IncludeLatest is supplied.
# ---------------------------------------------------------------------------

$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding

$script:Separator = [char]0x1F
# The collector emits plain ASCII only. "No value" is an empty string and the
# presentation layer renders it as an em dash, so the payload survives any
# console code page without mangling.
$script:Unknown = ''
$NodeMinimumVersion = '22.14.0'
$CodexStoreProductId = '9PLM9XGG6VKS'
$DefaultNodeMirror = 'https://mirrors.aliyun.com/nodejs-release'
$DefaultNpmRegistry = 'https://registry.npmjs.org'

function Join-Args([object[]]$Values) {
    if (-not $Values) { return '' }
    $flat = @()
    foreach ($value in $Values) { $flat += [string]$value }
    return ($flat -join $script:Separator)
}

function Get-CountryFlag([string]$Code) {
    if (-not $Code -or $Code.Length -ne 2) { return '' }
    $upper = $Code.ToUpper()
    $flags = @{
        'HK' = [char]::ConvertFromUtf32(0x1F1ED) + [char]::ConvertFromUtf32(0x1F1F0)
        'US' = [char]::ConvertFromUtf32(0x1F1FA) + [char]::ConvertFromUtf32(0x1F1F8)
        'CN' = [char]::ConvertFromUtf32(0x1F1E8) + [char]::ConvertFromUtf32(0x1F1F3)
        'JP' = [char]::ConvertFromUtf32(0x1F1EF) + [char]::ConvertFromUtf32(0x1F1F5)
        'SG' = [char]::ConvertFromUtf32(0x1F1F8) + [char]::ConvertFromUtf32(0x1F1EC)
        'TW' = [char]::ConvertFromUtf32(0x1F1F9) + [char]::ConvertFromUtf32(0x1F1FC)
        'GB' = [char]::ConvertFromUtf32(0x1F1EC) + [char]::ConvertFromUtf32(0x1F1E7)
        'DE' = [char]::ConvertFromUtf32(0x1F1E9) + [char]::ConvertFromUtf32(0x1F1EA)
        'FR' = [char]::ConvertFromUtf32(0x1F1EB) + [char]::ConvertFromUtf32(0x1F1F7)
        'CA' = [char]::ConvertFromUtf32(0x1F1E8) + [char]::ConvertFromUtf32(0x1F1E6)
        'AU' = [char]::ConvertFromUtf32(0x1F1E6) + [char]::ConvertFromUtf32(0x1F1FA)
        'KR' = [char]::ConvertFromUtf32(0x1F1F0) + [char]::ConvertFromUtf32(0x1F1F7)
        'NL' = [char]::ConvertFromUtf32(0x1F1F3) + [char]::ConvertFromUtf32(0x1F1F1)
    }
    if ($flags.ContainsKey($upper)) { return $flags[$upper] }
    try {
        $chars = $upper.ToCharArray()
        return [char]::ConvertFromUtf32(0x1F1E6 + ([int]$chars[0] - [int][char]'A')) +
               [char]::ConvertFromUtf32(0x1F1E6 + ([int]$chars[1] - [int][char]'A'))
    } catch { return '' }
}

function Invoke-VersionCommand([string]$Command, [string[]]$Arguments) {
    try {
        $text = (& $Command @Arguments 2>$null | Select-Object -First 1).ToString().Trim()
        if ($text) { return ($text -replace '^[^0-9]*', '') }
    } catch { }
    return $null
}

function Get-PackageVersion([string]$NpmPath, [string]$PackageName) {
    if (-not $NpmPath -or -not (Test-Path -LiteralPath $NpmPath)) { return $null }
    # Fast path: read package.json directly without spawning a Node.js process
    $roots = @((Split-Path $NpmPath -Parent))
    if ($env:APPDATA) { $roots += (Join-Path $env:APPDATA 'npm') }
    foreach ($root in $roots) {
        $pkgJson = Join-Path $root "node_modules\$PackageName\package.json"
        if (Test-Path -LiteralPath $pkgJson) {
            try {
                $ver = (Get-Content -Raw -LiteralPath $pkgJson -ErrorAction SilentlyContinue | ConvertFrom-Json).version
                if ($ver) { return [string]$ver }
            } catch { }
        }
    }
    try {
        $json = & $NpmPath list -g $PackageName --depth=0 --json 2>$null | Out-String | ConvertFrom-Json
        $entry = $json.dependencies.PSObject.Properties[$PackageName]
        if ($entry -and $entry.Value.version) { return [string]$entry.Value.version }
    } catch { }
    return $null
}

function Get-RegistryVersion([string]$PackageName) {
    if (-not $IncludeLatest) { return $null }
    if ($script:NpmExecutables.Count -eq 0) { return $null }
    try {
        $value = (& $script:NpmExecutables[0] view $PackageName version --json 2>$null | Out-String).Trim().Trim('"')
        if ($value -match '^\d+\.\d+') { return $value }
    } catch { }
    return $null
}

function Get-TaskStateKey([string]$Name) {
    $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if (-not $task) { return 'TaskStateNotInstalled' }
    switch ([string]$task.State) {
        'Running' { return 'TaskStateRunning' }
        'Ready' { return 'TaskStateReady' }
        'Disabled' { return 'TaskStateDisabled' }
        default { return 'TaskStateUnknown' }
    }
}

function Test-Port([int]$Port) {
    return [bool](Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Test-WebEndpoint([string]$Id, [string]$Name, [string]$Address, [string]$ProxyAddress = '') {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $state = 'Unreachable'
    $detail = ''
    try {
        $request = [Net.HttpWebRequest]::Create($Address)
        $request.Method = 'HEAD'
        $request.Timeout = 8000
        $request.AllowAutoRedirect = $true
        $request.UserAgent = 'Codex-Beacon'
        if ($ProxyAddress) { $request.Proxy = New-Object Net.WebProxy($ProxyAddress, $true) }
        $response = $request.GetResponse()
        $state = 'Connected'
        $detail = [string][int]$response.StatusCode
        $response.Close()
    } catch [Net.WebException] {
        if ($_.Exception.Response) {
            $state = 'Connected'
            $detail = [string][int]$_.Exception.Response.StatusCode
            $_.Exception.Response.Close()
        } elseif ($_.Exception.Status -in @('NameResolutionFailure','ConnectFailure','ProxyNameResolutionFailure')) {
            $state = 'Disconnected'
            $detail = [string]$_.Exception.Status
        } else { $detail = [string]$_.Exception.Status }
    } catch { $detail = $_.Exception.GetType().Name }
    $watch.Stop()
    return [ordered]@{ Id=$Id; Name=$Name; Address=$Address; State=$state; Detail=$detail; LatencyMs=[long]$watch.ElapsedMilliseconds }
}

function New-Component {
    param(
        [string]$Id, [string]$NameKey, [string]$KindKey, [string]$State,
        [string]$DetailKey, [object[]]$DetailArgs,
        [string]$Installed = $script:Unknown, [string]$Latest = $script:Unknown,
        [bool]$Manage = $false, [bool]$Running = $false,
        [string]$Account = 'NotApplicable', [string]$EvidenceKey = '', [object[]]$EvidenceArgs,
        [bool]$PrereqReady = $true, [string]$Path = ''
    )
    [ordered]@{
        Id               = $Id
        NameKey          = $NameKey
        KindKey          = $KindKey
        State            = $State
        DetailKey        = $DetailKey
        DetailArgs       = (Join-Args $DetailArgs)
        EvidenceKey      = $EvidenceKey
        EvidenceArgs     = (Join-Args $EvidenceArgs)
        Path             = $Path
        InstalledVersion = $Installed
        LatestVersion    = $Latest
        IsInstalled      = ($Installed -ne $script:Unknown)
        IsRunning        = [bool]$Running
        AccountState     = [string]$Account
        CanManageService = [bool]$Manage
        PrerequisitesReady = [bool]$PrereqReady
        RequiresAdmin    = ($Id -eq 'tailscale')
    }
}

# --- settings -------------------------------------------------------------

$settings = @{
    RelayTaskName = 'Codex Relay'
    ProxyTaskName = 'opencodex-proxy'
    NodeMirror    = $DefaultNodeMirror
    NpmRegistry   = $DefaultNpmRegistry
    NetworkMode   = 'auto'
    CustomHttpProxy = ''
}
if ($SettingsPath -and (Test-Path -LiteralPath $SettingsPath)) {
    try {
        $saved = Get-Content -Raw -LiteralPath $SettingsPath -Encoding UTF8 | ConvertFrom-Json
        foreach ($name in @('RelayTaskName', 'ProxyTaskName', 'NodeMirror', 'NpmRegistry', 'NetworkMode', 'CustomHttpProxy')) {
            if ($saved.$name) { $settings[$name] = $saved.$name }
        }
    } catch { }
}

# --- App Installer / WinGet / Store --------------------------------------

$appInstaller = Get-AppxPackage -Name 'Microsoft.DesktopAppInstaller' -ErrorAction SilentlyContinue | Select-Object -First 1
$wingetCommand = Get-Command winget.exe -ErrorAction SilentlyContinue
$wingetVersion = if ($wingetCommand) { Invoke-VersionCommand $wingetCommand.Source @('--version') } else { $null }
$msstoreAvailable = $false
if ($wingetCommand) {
    try {
        $sourceOutput = & $wingetCommand.Source source list --name msstore --disable-interactivity 2>$null | Out-String
        $msstoreAvailable = ($LASTEXITCODE -eq 0 -and $sourceOutput -match '(?i)msstore')
    } catch { }
}

# --- nvm / node / npm -----------------------------------------------------

$nvmCommand = Get-Command nvm.exe -ErrorAction SilentlyContinue
$nvmVersion = if ($nvmCommand) { Invoke-VersionCommand $nvmCommand.Source @('version') } else { $null }

$nvmRoot = $env:NVM_HOME
$nvmLink = $env:NVM_SYMLINK
if ($nvmCommand) {
    $nvmSettingsFile = Join-Path (Split-Path $nvmCommand.Source -Parent) 'settings.txt'
    if (Test-Path -LiteralPath $nvmSettingsFile) {
        foreach ($line in (Get-Content -LiteralPath $nvmSettingsFile)) {
            if ($line -match '^\s*root\s*:\s*(.+)$') { $nvmRoot = $matches[1].Trim() }
            if ($line -match '^\s*path\s*:\s*(.+)$') { $nvmLink = $matches[1].Trim() }
        }
    }
}

$nodeVersions = @()
if ($nvmCommand) {
    try {
        foreach ($line in (& $nvmCommand.Source list 2>$null)) {
            if ($line -match '^\s*(\*)?\s*v?(\d+\.\d+\.\d+)') {
                $nodeVersions += [ordered]@{ Version = $matches[2]; IsCurrent = ($matches[1] -eq '*'); IsInstalled = $true; Lts = '' }
            }
        }
    } catch { }
}

# nvm-windows marks the active version through the symlink, which is more
# reliable than the (sometimes missing) asterisk in `nvm list`.
$activeNodeVersion = $null
if ($nvmLink -and (Test-Path -LiteralPath $nvmLink)) {
    try {
        $target = (Get-Item -LiteralPath $nvmLink -Force).Target
        if ($target) {
            $leaf = Split-Path -Leaf ([string]($target | Select-Object -First 1))
            if ($leaf -match '^v?(\d+\.\d+\.\d+)') { $activeNodeVersion = $matches[1] }
        }
    } catch { }
}
if ($nodeVersions.Count -gt 0) {
    if (-not $activeNodeVersion) {
        $activeNodeVersion = ($nodeVersions | Where-Object { $_.IsCurrent } | Select-Object -First 1).Version
    }
    foreach ($entry in $nodeVersions) {
        $entry.IsCurrent = ($activeNodeVersion -and $entry.Version -eq $activeNodeVersion)
    }
}

$nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
$nodeVersion = $null
if ($nodeCommand) {
    $raw = Invoke-VersionCommand $nodeCommand.Source @('--version')
    if ($raw -match '^(\d+\.\d+\.\d+)') { $nodeVersion = $matches[1] }
}
$nodeOk = $false
if ($nodeVersion) { $nodeOk = ([version]$nodeVersion -ge [version]$NodeMinimumVersion) }

# npm is resolved next to every node runtime we know about, because the runtime
# first on PATH is not necessarily the one that owns the global packages.
$nodeRoots = @()
if ($nodeCommand) { $nodeRoots += (Split-Path $nodeCommand.Source -Parent) }
if ($nvmLink) { $nodeRoots += $nvmLink }
if ($nvmRoot) { $nodeRoots += @(Get-ChildItem -LiteralPath $nvmRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }) }
$nodeRoots = @($nodeRoots | Where-Object { $_ } | Select-Object -Unique)

$script:NpmExecutables = @()
foreach ($root in $nodeRoots) {
    $candidate = Join-Path $root 'npm.cmd'
    if (Test-Path -LiteralPath $candidate) { $script:NpmExecutables += $candidate }
}
$script:NpmExecutables = @($script:NpmExecutables | Select-Object -Unique)

$npmExecutable = if ($script:NpmExecutables.Count -gt 0) { $script:NpmExecutables[0] } else { '' }
$npmVersion = if ($npmExecutable) { Invoke-VersionCommand $npmExecutable @('--version') } else { $null }
$npmOk = [bool]$npmVersion
$npmRegistry = [string]$settings.NpmRegistry
if ($npmExecutable) {
    try {
        $configuredRegistry = (& $npmExecutable config get registry 2>$null | Select-Object -First 1).ToString().Trim()
        if ($configuredRegistry -match '^https?://') { $npmRegistry = $configuredRegistry }
    } catch { }
}

# --- codex desktop app ----------------------------------------------------

$desktopPackage = Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue | Select-Object -First 1
$desktopVersion = if ($desktopPackage) { [string]$desktopPackage.Version } else { $null }
$desktopProcesses = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -eq 'ChatGPT.exe' -and $_.ExecutablePath -match '(?i)OpenAI\.Codex'
})
$desktopRunning = ($desktopProcesses.Count -gt 0)

# --- codex CLI ------------------------------------------------------------

# The CLI ships through several independent channels. Collect them all so the
# card can state where the resolved version actually came from.
$codexCandidates = @()
foreach ($name in @('codex.exe', 'codex.cmd')) {
    $c = Get-Command $name -ErrorAction SilentlyContinue
    if ($c) { $codexCandidates += $c.Source }
}
if ($nvmLink) {
    foreach ($name in @('codex.cmd', 'codex.exe', 'codex')) {
        $candidate = Join-Path $nvmLink $name
        if (Test-Path -LiteralPath $candidate) { $codexCandidates += $candidate }
    }
}
$codexCandidates = @($codexCandidates | Select-Object -Unique)

$codexPathVersion = $null
$codexPathExecutable = $null
foreach ($candidate in $codexCandidates) {
    $raw = Invoke-VersionCommand $candidate @('--version')
    if ($raw -match '^(\d+\.\d+\.\d+)') {
        $codexPathVersion = $matches[1]
        $codexPathExecutable = $candidate
        break
    }
}

$codexNpmVersion = $null
$codexNpmOwner = $null
$codexNpmExecutable = $null
foreach ($npm in $script:NpmExecutables) {
    $found = Get-PackageVersion $npm '@openai/codex'
    if ($found) {
        $codexNpmVersion = $found
        $codexNpmOwner = $npm
        $npmDirectory = Split-Path $npm -Parent
        $codexNpmExecutable = $codexCandidates | Where-Object { (Split-Path $_ -Parent) -eq $npmDirectory } | Select-Object -First 1
        break
    }
}

$codexBundledVersion = $null
$codexBundledExecutable = $null
$bundledRoot = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
if (Test-Path -LiteralPath $bundledRoot) {
    foreach ($directory in (Get-ChildItem -LiteralPath $bundledRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending)) {
        $candidate = Join-Path $directory.FullName 'codex.exe'
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($candidate)
        $found = $null
        if ($info.FileVersion -match '(\d+\.\d+\.\d+)') { $found = $matches[1] }
        if (-not $found) { $found = Invoke-VersionCommand $candidate @('--version') }
        if ($found -match '(\d+\.\d+\.\d+)') {
            $codexBundledVersion = $matches[1]
            $codexBundledExecutable = $candidate
            break
        }
    }
}

$codexVersion = $null
$codexSourceKey = 'DetailCodexMissing'
$codexSourceArgs = @()
$codexResolvedPath = ''
if ($codexNpmVersion) {
    $codexVersion = $codexNpmVersion
    $codexSourceKey = 'DetailCodexNpm'
    $codexSourceArgs = @($codexNpmVersion)
    $codexResolvedPath = if ($codexNpmExecutable) { $codexNpmExecutable } else { $codexNpmOwner }
} elseif ($codexPathVersion) {
    $codexVersion = $codexPathVersion
    $codexSourceKey = 'DetailCodexOnPath'
    $codexSourceArgs = @($codexPathVersion)
    $codexResolvedPath = $codexPathExecutable
} elseif ($codexBundledVersion) {
    $codexVersion = $codexBundledVersion
    $codexSourceKey = 'DetailCodexBundled'
    $codexSourceArgs = @($codexBundledVersion)
    $codexResolvedPath = $codexBundledExecutable
}

$codexAccount = 'NotApplicable'
if ($codexVersion) {
    # Probe the same channels the version came from, including the copy bundled
    # with the desktop app when that is the only one present. The CLI reports
    # sign-in state on stderr, so go through cmd.exe: this script runs with
    # ErrorActionPreference=SilentlyContinue, which would otherwise swallow it.
    $loginCandidates = @()
    if ($codexResolvedPath -and (Test-Path -LiteralPath $codexResolvedPath) -and $codexResolvedPath -notmatch '(?i)npm(?:\.cmd)?$') {
        # The executable that supplied the displayed version/path is authoritative.
        # Only fall back to other copies when that executable cannot be resolved.
        $loginCandidates += $codexResolvedPath
    }
    else {
        $loginCandidates += $codexCandidates
        if ($codexBundledExecutable) { $loginCandidates += $codexBundledExecutable }
    }
    $sawSignedOut = $false
    foreach ($candidate in @($loginCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique)) {
        $status = (cmd.exe /d /c "`"$candidate`" login status 2>&1" | Out-String).Trim()
        if ($status -match '(?i)not logged in') { $sawSignedOut = $true; continue }
        if ($status -match '(?i)logged in') { $codexAccount = 'SignedIn'; break }
        $codexAccount = 'Unknown'
    }
    if ($codexAccount -ne 'SignedIn' -and $sawSignedOut) { $codexAccount = 'SignedOut' }
}

# --- optional npm modules -------------------------------------------------

$openCodexVersion = $null
foreach ($npm in $script:NpmExecutables) {
    $found = Get-PackageVersion $npm '@bitkyc08/opencodex'
    if ($found) { $openCodexVersion = $found; break }
}
$proxyPorts = @()
foreach ($port in @(10100, 51863)) { if (Test-Port $port) { $proxyPorts += $port } }
$proxyTaskKey = Get-TaskStateKey $settings.ProxyTaskName
$openCodexModels = @()
$openCodexProviders = @()
$openCodexIntegration = ''
if ($openCodexVersion) {
    $openCodexCommand = Get-Command opencodex -ErrorAction SilentlyContinue
    if ($openCodexCommand) {
        try {
            $modelJson = & $openCodexCommand.Source models list --json 2>$null | Out-String | ConvertFrom-Json
            foreach ($entry in @($modelJson.models)) {
                if ($entry.model) { $openCodexModels += [ordered]@{ Id=[string]$entry.model; Provider=[string]$entry.provider } }
            }
        } catch { }
        try {
            $providerJson = & $openCodexCommand.Source provider list --json 2>$null | Out-String | ConvertFrom-Json
            foreach ($entry in @($providerJson.configured)) {
                $openCodexProviders += [ordered]@{ Id=[string]$entry.name; Name=[string]$entry.name; BaseUrl=[string]$entry.baseUrl; Enabled=$true }
            }
        } catch { }
        try {
            $integrationJson = & $openCodexCommand.Source integration native --json 2>$null | Out-String | ConvertFrom-Json
            $codexIntegration = @($integrationJson.clients | Where-Object { $_.clientId -eq 'codex' } | Select-Object -First 1)
            if ($codexIntegration.Count -gt 0) { $openCodexIntegration = [string]$codexIntegration[0].state }
        } catch { }
    }
}

$relayVersion = $null
$relayPackageJson = Join-Path $env:USERPROFILE '.codex-relay\app\node_modules\codex-relay\package.json'
if (Test-Path -LiteralPath $relayPackageJson) {
    try { $relayVersion = [string]((Get-Content -Raw -LiteralPath $relayPackageJson | ConvertFrom-Json).version) } catch { }
}
if (-not $relayVersion) {
    foreach ($npm in $script:NpmExecutables) {
        $found = Get-PackageVersion $npm 'codex-relay'
        if ($found) { $relayVersion = $found; break }
    }
}
$relayListening = Test-Port 8787
$relayTaskKey = Get-TaskStateKey $settings.RelayTaskName

# --- tailscale ------------------------------------------------------------

$tailService = Get-Service -Name 'Tailscale' -ErrorAction SilentlyContinue
$tailCommand = Get-Command tailscale.exe -ErrorAction SilentlyContinue
$tailVersion = if ($tailCommand) {
    $raw = Invoke-VersionCommand $tailCommand.Source @('version')
    if ($raw -match '^([0-9]+\.[0-9.]+)') { $matches[1] } else { $null }
} else { $null }
$tailJson = $null
$tailAccount = 'NotApplicable'
if ($tailCommand) {
    try {
        $tailJson = & $tailCommand.Source status --json 2>$null | Out-String | ConvertFrom-Json
        if ($tailJson.BackendState -eq 'Running') { $tailAccount = 'SignedIn' }
        elseif ($tailJson.BackendState -in @('NeedsLogin', 'Stopped')) { $tailAccount = 'SignedOut' }
        else { $tailAccount = 'Unknown' }
    } catch { }
}

# --- remote version lookups ----------------------------------------------

$desktopLatest = $null
$desktopEvidenceKey = 'VersionEvidenceUnknown'
$desktopEvidenceArgs = @()
$desktopAppVersion = $null
$desktopBackendVersion = $null
if ($IncludeLatest) {
    try {
        $manifest = Invoke-RestMethod -Uri 'https://codexapp.agentsmirror.com/latest/manifest' -TimeoutSec 10 -UseBasicParsing
        if ($manifest.sources.windows.version) {
            $desktopLatest = [string]$manifest.sources.windows.version
            $desktopAppVersion = if ($manifest.sources.windows.appVersion) { [string]$manifest.sources.windows.appVersion } else { $null }
            $desktopBackendVersion = if ($manifest.sources.windows.backendVersion) { [string]$manifest.sources.windows.backendVersion } else { $null }
            $desktopEvidenceKey = 'VersionEvidenceMirror'
            $desktopEvidenceArgs = @($CodexStoreProductId, [string]$manifest.generatedAt)
        }
    } catch { $desktopEvidenceKey = 'VersionEvidenceUnreachable' }
}

# --- components -----------------------------------------------------------

$components = @()

$components += New-Component -Id 'appinstaller' -NameKey 'CompAppInstallerName' -KindKey 'CompAppInstallerKind' `
    -State $(if ($appInstaller) { 'Healthy' } else { 'Unavailable' }) `
    -DetailKey $(if ($appInstaller) { 'DetailAppInstallerReady' } else { 'DetailAppInstallerMissing' }) -DetailArgs @() `
    -Installed $(if ($appInstaller) { [string]$appInstaller.Version } else { $script:Unknown }) -Manage $false -Running $false

$components += New-Component -Id 'winget' -NameKey 'CompWingetName' -KindKey 'CompWingetKind' `
    -State $(if ($wingetCommand) { 'Healthy' } else { 'Unavailable' }) `
    -DetailKey $(if ($wingetCommand) { 'DetailWingetReady' } else { 'DetailWingetMissing' }) -DetailArgs @() `
    -Path $(if ($wingetCommand) { $wingetCommand.Source } else { '' }) `
    -Installed $(if ($wingetVersion) { $wingetVersion } else { $script:Unknown }) -Manage $false -Running $false

$components += New-Component -Id 'msstore' -NameKey 'CompStoreName' -KindKey 'CompStoreKind' `
    -State $(if ($msstoreAvailable) { 'Healthy' } else { 'Unavailable' }) `
    -DetailKey $(if ($msstoreAvailable) { 'DetailStoreReady' } else { 'DetailStoreMissing' }) -DetailArgs @() `
    -Installed $(if ($msstoreAvailable) { 'available' } else { $script:Unknown }) -Manage $false -Running $false

$components += New-Component -Id 'nvm' -NameKey 'CompNvmName' -KindKey 'CompNvmKind' `
    -State $(if ($nvmCommand) { 'Healthy' } else { 'Unavailable' }) `
    -DetailKey $(if ($nvmCommand) { 'DetailNvmReady' } else { 'DetailNvmMissing' }) `
    -DetailArgs $(if ($nvmCommand) { @($nodeVersions.Count) } else { @() }) `
    -Path $(if ($nvmCommand) { $nvmCommand.Source } else { '' }) `
    -Installed $(if ($nvmVersion) { $nvmVersion } else { $script:Unknown }) `
    -Manage $false -Running $false

$components += New-Component -Id 'node' -NameKey 'CompNodeName' -KindKey 'CompNodeKind' `
    -State $(if ($nodeOk) { 'Healthy' } elseif ($nodeCommand) { 'Warning' } else { 'Unavailable' }) `
    -DetailKey $(if ($nodeOk) { 'DetailNodeReady' } elseif ($nodeCommand) { 'DetailNodeOld' } else { 'DetailNodeMissing' }) `
    -DetailArgs $(if ($nodeCommand) { @($NodeMinimumVersion) } else { @() }) `
    -Path $(if ($nodeCommand) { $nodeCommand.Source } else { '' }) `
    -Installed $(if ($nodeVersion) { $nodeVersion } else { $script:Unknown }) `
    -Manage $false -Running $false -PrereqReady $nodeOk

$components += New-Component -Id 'npm' -NameKey 'CompNpmName' -KindKey 'CompNpmKind' `
    -State $(if ($npmOk) { 'Healthy' } else { 'Unavailable' }) `
    -DetailKey $(if ($npmOk) { 'DetailNpmReady' } else { 'DetailNpmMissing' }) `
    -DetailArgs @() `
    -Path $npmExecutable `
    -Installed $(if ($npmVersion) { $npmVersion } else { $script:Unknown }) `
    -Manage $false -Running $false -PrereqReady $npmOk

$desktopAccount = if ($desktopPackage) { 'Unknown' } else { 'NotApplicable' }
$desktopState = if (-not $desktopPackage) { 'Unavailable' }
    elseif ($desktopRunning) { 'Healthy' } else { 'Stopped' }
$desktopDetailKey = if (-not $desktopPackage) { 'DetailDesktopMissing' }
    elseif ($desktopRunning) { 'DetailDesktopRunning' } else { 'DetailDesktopStopped' }
$components += New-Component -Id 'desktop' -NameKey 'CompDesktopName' -KindKey 'CompDesktopKind' `
    -State $desktopState -DetailKey $desktopDetailKey -DetailArgs @() `
    -Installed $(if ($desktopVersion) { $desktopVersion } else { $script:Unknown }) `
    -Latest $(if ($desktopLatest) { $desktopLatest } else { $script:Unknown }) `
    -Manage $true -Running $desktopRunning -Account $desktopAccount `
    -EvidenceKey $desktopEvidenceKey -EvidenceArgs $desktopEvidenceArgs

$codexState = if (-not $codexVersion) { 'Unavailable' } elseif ($codexAccount -eq 'SignedOut') { 'Warning' } else { 'Healthy' }
$components += New-Component -Id 'codex' -NameKey 'CompCodexName' -KindKey 'CompCodexKind' `
    -State $codexState -DetailKey $codexSourceKey -DetailArgs $codexSourceArgs -Path $codexResolvedPath `
    -Installed $(if ($codexVersion) { $codexVersion } else { $script:Unknown }) `
    -Latest $(if ($codexNpmVersion) { $v = Get-RegistryVersion '@openai/codex'; if ($v) { $v } else { $script:Unknown } } else { $script:Unknown }) `
    -Manage $false -Running ([bool]$codexVersion) -Account $codexAccount -PrereqReady ($nodeOk -and $npmOk)

$proxyState = if ($proxyPorts.Count -gt 0) { 'Healthy' }
    elseif (-not $openCodexVersion) { 'Unavailable' }
    elseif ($proxyTaskKey -eq 'TaskStateRunning') { 'Warning' }
    else { 'Stopped' }
$proxyDetailKey = if ($proxyPorts.Count -gt 0) { 'DetailProxyListening' }
    elseif (-not $openCodexVersion) { 'DetailProxyMissing' }
    elseif ($proxyTaskKey -eq 'TaskStateNotInstalled') { 'DetailProxyNoTask' }
    else { 'DetailProxyStopped' }
$proxyDetailArgs = if ($proxyPorts.Count -gt 0) { @(($proxyPorts -join ', '), ('@' + $proxyTaskKey)) } else { @(('@' + $proxyTaskKey)) }
$components += New-Component -Id 'opencodex' -NameKey 'CompProxyName' -KindKey 'CompProxyKind' `
    -State $proxyState -DetailKey $proxyDetailKey -DetailArgs $proxyDetailArgs `
    -Installed $(if ($openCodexVersion) { $openCodexVersion } else { $script:Unknown }) `
    -Latest $(if ($openCodexVersion) { $v = Get-RegistryVersion '@bitkyc08/opencodex'; if ($v) { $v } else { $script:Unknown } } else { $script:Unknown }) `
    -Manage $true -Running ($proxyPorts.Count -gt 0) -PrereqReady ($nodeOk -and $npmOk)

$relayState = if ($relayListening) { 'Healthy' }
    elseif (-not $relayVersion) { 'Unavailable' }
    elseif ($relayTaskKey -eq 'TaskStateRunning') { 'Warning' }
    else { 'Stopped' }
$relayDetailKey = if ($relayListening) { 'DetailRelayListening' }
    elseif (-not $relayVersion) { 'DetailRelayMissing' }
    elseif ($relayTaskKey -eq 'TaskStateNotInstalled') { 'DetailRelayNoTask' }
    else { 'DetailRelayStopped' }
$relayDetailArgs = @(('@' + $relayTaskKey))
$components += New-Component -Id 'relay' -NameKey 'CompRelayName' -KindKey 'CompRelayKind' `
    -State $relayState -DetailKey $relayDetailKey -DetailArgs $relayDetailArgs `
    -Installed $(if ($relayVersion) { $relayVersion } else { $script:Unknown }) `
    -Latest $(if ($relayVersion) { $v = Get-RegistryVersion 'codex-relay'; if ($v) { $v } else { $script:Unknown } } else { $script:Unknown }) `
    -Manage $true -Running $relayListening -PrereqReady ($nodeOk -and $npmOk)

$tailState = if (-not $tailVersion) { 'Unavailable' }
    elseif ($tailAccount -eq 'SignedOut') { 'Warning' }
    elseif ($tailJson -and $tailJson.Self.Online) { 'Healthy' }
    else { 'Stopped' }
$tailDetailKey = if (-not $tailVersion) { 'DetailTailscaleMissing' }
    elseif ($tailAccount -eq 'SignedOut') { 'DetailTailscaleSignedOut' }
    elseif ($tailJson) { 'DetailTailscaleReady' }
    else { 'DetailTailscaleNoState' }
$tailDetailArgs = if ($tailJson -and $tailVersion -and $tailAccount -ne 'SignedOut') {
    @([string]$tailJson.Self.HostName, [string]($tailJson.Self.TailscaleIPs -join ', '))
} else { @() }
$components += New-Component -Id 'tailscale' -NameKey 'CompTailscaleName' -KindKey 'CompTailscaleKind' `
    -State $tailState -DetailKey $tailDetailKey -DetailArgs $tailDetailArgs `
    -Installed $(if ($tailVersion) { $tailVersion } else { $script:Unknown }) `
    -Latest $(if ($IncludeLatest) {
        try {
            $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/tailscale/tailscale/releases/latest' -Headers @{ 'User-Agent' = 'Codex-Beacon' } -TimeoutSec 8 -UseBasicParsing
            if ($release.tag_name) { ([string]$release.tag_name).TrimStart('v') } else { $script:Unknown }
        } catch { $script:Unknown }
    } else { $script:Unknown }) `
    -Manage $true -Running ($tailService.Status -eq 'Running') -Account $tailAccount

# --- processes ------------------------------------------------------------

function Get-ProcessRole([string]$Path, [string]$Name) {
    if ($Name -eq 'codex-code-mode-host.exe') { return @{ Key = 'RoleSupportHost'; Args = @() } }
    if ($Path -match '(?i)WindowsApps[\\/]OpenAI\.Codex_') { return @{ Key = 'RoleDesktopApp'; Args = @() } }
    if ($Path -match '(?i)\.vscode[\\/]extensions[\\/]') { return @{ Key = 'RoleCliVsCode'; Args = @() } }
    if ($Path -match '(?i)\.codex-relay[\\/]') { return @{ Key = 'RoleCliRelay'; Args = @() } }
    if ($Path -match '(?i)AppData[\\/]Local[\\/]OpenAI[\\/]Codex[\\/]bin[\\/]') { return @{ Key = 'RoleCliAppBundled'; Args = @() } }
    if ($Name -eq 'codex.exe') { return @{ Key = 'RoleCliOther'; Args = @() } }
    return @{ Key = 'RoleUnknown'; Args = @() }
}

$processes = @()
foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'ChatGPT.exe' or Name = 'codex.exe' or Name = 'codex-code-mode-host.exe'" -ErrorAction SilentlyContinue)) {
    if (-not $p) { continue }
    $isDesktop = ($p.Name -eq 'ChatGPT.exe') -and ($p.ExecutablePath -match '(?i)OpenAI\.Codex')
    $isCli = ($p.Name -eq 'codex.exe')
    $isHost = ($p.Name -eq 'codex-code-mode-host.exe')
    if (-not ($isDesktop -or $isCli -or $isHost)) { continue }

    # Read the version from the executable itself. Several independent installs
    # can be running at once and they do not share a version.
    $version = $null
    if ($p.ExecutablePath) {
        try {
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo([string]$p.ExecutablePath)
            if ($info.FileVersion -match '(\d+\.\d+\.\d+)') { $version = $matches[1] }
            elseif ($info.ProductVersion -match '(\d+\.\d+\.\d+)') { $version = $matches[1] }
        } catch { }
    }
    if (-not $version -and $isDesktop) { $version = $desktopVersion }
    if (-not $version -and ($isCli -or $isHost)) {
        if ($codexPathVersion) { $version = $codexPathVersion }
        elseif ($codexBundledVersion) { $version = $codexBundledVersion }
    }

    $startedAt = $null
    try { $startedAt = (Get-Process -Id $p.ProcessId).StartTime.ToString('MM-dd HH:mm:ss') } catch { }

    $role = Get-ProcessRole ([string]$p.ExecutablePath) ([string]$p.Name)
    $processes += [ordered]@{
        Pid            = [int]$p.ProcessId
        ParentPid      = [int]$p.ParentProcessId
        Name           = [string]$p.Name
        RoleKey        = $role.Key
        RoleArgs       = (Join-Args $role.Args)
        Version        = $(if ($version) { $version } else { $script:Unknown })
        Architecture   = $(if ($p.ExecutablePath -match 'Program Files \(x86\)') { 'x86' } else { 'x64' })
        StartedAt      = $(if ($startedAt) { $startedAt } else { $script:Unknown })
        ExecutablePath = $(if ($p.ExecutablePath) { [string]$p.ExecutablePath } else { $script:Unknown })
    }
}

# --- providers ------------------------------------------------------------

$codexConfig = Join-Path $env:USERPROFILE '.codex\config.toml'
$configLines = if (Test-Path -LiteralPath $codexConfig) { @(Get-Content -LiteralPath $codexConfig -Encoding UTF8) } else { @() }
$selectedProvider = ''
$openaiBaseUrl = ''
foreach ($line in $configLines) {
    $trimmed = $line.Trim()
    if ($trimmed.StartsWith('model_provider') -and $trimmed.Contains('=')) { $selectedProvider = $trimmed.Split('=', 2)[1].Trim().Trim('"').Trim("'") }
    if ($trimmed.StartsWith('openai_base_url') -and $trimmed.Contains('=')) { $openaiBaseUrl = $trimmed.Split('=', 2)[1].Trim().Trim('"').Trim("'") }
}

$providersList = @()
$currentProviderId = ''
$currentProviderName = ''
$currentProviderUrl = ''
$currentProviderWire = ''
foreach ($line in ($configLines + @('[__end__]'))) {
    $trimmed = $line.Trim()
    if ($trimmed.StartsWith('[model_providers.')) {
        if ($currentProviderId) {
            $providersList += [ordered]@{
                Id      = $currentProviderId
                Name    = $(if ($currentProviderName) { $currentProviderName } else { $currentProviderId })
                BaseUrl = $(if ($currentProviderUrl) { $currentProviderUrl } else { 'https://api.openai.com/v1' })
                WireApi = $(if ($currentProviderWire) { $currentProviderWire } else { 'responses' })
                IsActive = [bool]($selectedProvider -eq $currentProviderId)
            }
        }
        $currentProviderId = $trimmed.Substring('[model_providers.'.Length).TrimEnd(']').Trim()
        $currentProviderName = ''; $currentProviderUrl = ''; $currentProviderWire = ''
        continue
    }
    if ($trimmed.StartsWith('[')) {
        if ($currentProviderId) {
            $providersList += [ordered]@{
                Id      = $currentProviderId
                Name    = $(if ($currentProviderName) { $currentProviderName } else { $currentProviderId })
                BaseUrl = $(if ($currentProviderUrl) { $currentProviderUrl } else { 'https://api.openai.com/v1' })
                WireApi = $(if ($currentProviderWire) { $currentProviderWire } else { 'responses' })
                IsActive = [bool]($selectedProvider -eq $currentProviderId)
            }
        }
        $currentProviderId = ''
        continue
    }
    if ($currentProviderId) {
        if ($trimmed -match '^name\s*=') { $currentProviderName = $trimmed.Split('=', 2)[1].Trim().Trim('"').Trim("'") }
        elseif ($trimmed -match '^base_url\s*=') { $currentProviderUrl = $trimmed.Split('=', 2)[1].Trim().Trim('"').Trim("'") }
        elseif ($trimmed -match '^wire_api\s*=') { $currentProviderWire = $trimmed.Split('=', 2)[1].Trim().Trim('"').Trim("'") }
    }
}

# config.toml may omit model_provider. In that case Codex uses its built-in
# OpenAI provider, unless openai_base_url was rewritten to a local proxy — which
# is exactly what OpenCodex does on install. Resolve that before marking a row
# active, otherwise the UI claims OpenAI is serving requests that never leave
# the machine.
$localProxyPattern = '(?i)^https?://(?:localhost|127\.0\.0\.1|\[::1\]):(?:10100|51863)'
$effectiveProviderId = $selectedProvider
if (-not $effectiveProviderId) {
    if ($openaiBaseUrl -and $openaiBaseUrl -match $localProxyPattern) { $effectiveProviderId = 'opencodex' }
    else { $effectiveProviderId = 'custom' }
}

if (-not ($providersList | Where-Object { $_.Id -eq 'custom' })) {
    $providersList = @([ordered]@{
        Id = 'custom'; Name = 'OpenAI'; BaseUrl = 'https://api.openai.com/v1'
        WireApi = 'responses'; IsActive = $false
    }) + $providersList
}
if (-not ($providersList | Where-Object { $_.Id -eq 'opencodex' })) {
    $providersList += [ordered]@{
        Id       = 'opencodex'
        Name     = 'OpenCodex'
        BaseUrl  = 'http://127.0.0.1:10100/v1'
        WireApi  = 'responses'
        IsActive = [bool]($effectiveProviderId -eq 'opencodex')
    }
}
foreach ($provider in $providersList) { $provider.IsActive = ($provider.Id -eq $effectiveProviderId) }

# --- public egress --------------------------------------------------------

# Two requests only: resolve the address OpenAI sees, then enrich it through
# ip.net.coffee, which is the source the project standardises on.
$publicEgress = @()
if ($IncludeLatest) {
    $egressAddress = $null
    $egressSourceKey = 'EgressSourceGeneric'
    try {
        $trace = [string](curl.exe -sSL -m 6 https://chatgpt.com/cdn-cgi/trace 2>&1)
        $match = [regex]::Match($trace, 'ip=([0-9a-fA-F.:]+)')
        if ($match.Success) { $egressAddress = $match.Groups[1].Value.Trim(); $egressSourceKey = 'EgressSourceChatGpt' }
    } catch { }
    if (-not $egressAddress) {
        $egressSourceKey = 'EgressSourceIpEcho'
        # Prefer IPv4: every enrichment source downstream reports better data
        # for it, and dual-stack machines otherwise get an opaque IPv6 address.
        foreach ($endpoint in @('https://ipv4.icanhazip.com', 'https://api.ip.sb/ip', 'https://api.ipify.org', 'https://ipwho.is/')) {
            try {
                $raw = [string](curl.exe -sSL -m 5 $endpoint 2>&1).Trim()
                if (-not $raw) { continue }
                if ($raw.StartsWith('{')) {
                    try { $raw = [string](($raw | ConvertFrom-Json).ip) } catch { continue }
                }
                $parsed = $null
                if (-not [System.Net.IPAddress]::TryParse($raw, [ref]$parsed)) { continue }
                if ($parsed.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) { $egressAddress = $raw; break }
                if (-not $egressAddress) { $egressAddress = $raw }
            } catch { }
        }
    }
    if ($egressAddress) {
        $risk = $null
        try {
            $risk = Invoke-RestMethod -Uri ("https://ip.net.coffee/api/iprisk/" + $egressAddress) -TimeoutSec 8 -UseBasicParsing
        } catch { }
        $countryCode = if ($risk -and $risk.countryCode) { [string]$risk.countryCode } else { '' }
        $publicEgress += [ordered]@{
            Address      = $egressAddress
            SourceKey    = $egressSourceKey
            CheckedAt    = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
            Country      = if ($risk) { [string]$risk.country } else { '' }
            CountryCode  = $countryCode
            FlagEmoji    = (Get-CountryFlag $countryCode)
            Region       = if ($risk) { [string]$risk.region } else { '' }
            City         = if ($risk) { [string]$risk.city } else { '' }
            Isp          = if ($risk) { [string]$risk.asOrganization } else { '' }
            AsNumber     = if ($risk -and $risk.asn) { "AS$($risk.asn)" } else { '' }
            IsHosting    = if ($risk) { [bool]$risk.is_datacenter } else { $false }
            HostingName  = if ($risk) { [string]$risk.datacenter_name } else { '' }
            IsProxy      = if ($risk) { [bool]($risk.is_proxy -or $risk.is_vpn) } else { $false }
            TrustScore   = if ($risk -and $null -ne $risk.trust_score) { [int]$risk.trust_score } else { -1 }
            Resolved     = [bool]$risk
        }
    }
}

# --- tailnet --------------------------------------------------------------

$tailDevices = @()
if ($tailJson) {
    $nodes = @($tailJson.Self) + @($tailJson.Peer.PSObject.Properties.Value)
    foreach ($device in $nodes) {
        if (-not $device) { continue }
        $tailDevices += [ordered]@{
            Name      = [string]$device.HostName
            DnsName   = [string]$device.DNSName
            OS        = [string]$device.OS
            Addresses = [string]($device.TailscaleIPs -join ', ')
            Online    = [bool]$device.Online
            IsSelf    = ($device.ID -eq $tailJson.Self.ID)
            LastSeen  = $(if ($device.Online) { '@LastSeenNow' }
                          elseif ($device.LastSeen) { ([datetime]$device.LastSeen).ToLocalTime().ToString('yyyy-MM-dd HH:mm') }
                          else { '@LastSeenUnknown' })
        }
    }
}

# --- snapshot -------------------------------------------------------------

# Report configured intent and observable Windows proxy evidence separately.
$internetSettings = $null
try { $internetSettings = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue } catch { }
$systemProxyEnabled = [bool]($internetSettings -and [int]$internetSettings.ProxyEnable -eq 1)
$systemProxyAddress = if ($systemProxyEnabled) { [string]$internetSettings.ProxyServer } else { '' }
$autoConfigUrl = if ($internetSettings) { [string]$internetSettings.AutoConfigURL } else { '' }
$autoDetectEnabled = [bool]($internetSettings -and [int]$internetSettings.AutoDetect -eq 1)
$tunAdapterName = ''
try {
    $tun = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object {
        $_.Status -eq 'Up' -and (($_.Name + ' ' + $_.InterfaceDescription) -match '(?i)\b(tun|wintun|tap|clash|mihomo|sing-box)\b')
    } | Select-Object -First 1
    if ($tun) { $tunAdapterName = [string]$tun.Name }
} catch { }
$environmentProxy = [string]([Environment]::GetEnvironmentVariable('HTTPS_PROXY', 'User'))
if (-not $environmentProxy) { $environmentProxy = [string]([Environment]::GetEnvironmentVariable('HTTP_PROXY', 'User')) }
$effectiveProxyMode = if ([string]$settings.NetworkMode -eq 'custom' -and [string]$settings.CustomHttpProxy) { 'custom' }
    elseif ([string]$settings.NetworkMode -eq 'tun' -and $tunAdapterName) { 'tun' }
    elseif ($systemProxyEnabled) { 'system' }
    elseif ($autoConfigUrl -or $autoDetectEnabled) { 'auto' }
    elseif ($tunAdapterName) { 'tun' }
    elseif ($environmentProxy) { 'environment' }
    else { 'direct' }

$networkProbes = @()
if ($IncludeLatest) {
    $probeProxy = if ($settings.NetworkMode -eq 'custom') { [string]$settings.CustomHttpProxy } else { '' }
    $networkProbes += Test-WebEndpoint 'chatgpt' 'ChatGPT client' 'https://chatgpt.com/' $probeProxy
    $networkProbes += Test-WebEndpoint 'codex' 'Codex CLI' 'https://api.openai.com/v1/models' $probeProxy
}

$coreInstalled = [bool]$desktopPackage -or [bool]$codexVersion
$overallKey = if (-not $coreInstalled) { 'OverallNotInstalled' } else { 'OverallReady' }

$snapshot = [ordered]@{
    CollectedAt   = (Get-Date).ToString('o')
    OverallState  = $(if (-not $coreInstalled) { 'Stopped' } else { 'Healthy' })
    OverallKey    = $overallKey
    Components    = $components
    Processes     = $processes
    TailscaleDevices = $tailDevices
    NodeVersions  = $nodeVersions
    Proxy         = [ordered]@{
        ConfiguredMode = [string]$settings.NetworkMode
        EffectiveMode = $effectiveProxyMode
        SystemProxyEnabled = $systemProxyEnabled
        SystemProxyAddress = $systemProxyAddress
        AutoDetectEnabled = $autoDetectEnabled
        AutoConfigUrl = $autoConfigUrl
        TunAdapterName = $tunAdapterName
        EnvironmentProxy = $environmentProxy
    }
    PublicEgress  = $publicEgress
    Providers     = $providersList
    NetworkProbes = $networkProbes
    OpenCodexModels = $openCodexModels
    OpenCodexProviders = $openCodexProviders
    OpenCodexIntegration = $openCodexIntegration
    NodeMirror    = [string]$settings.NodeMirror
    NpmRegistry   = $npmRegistry
    NodeMinimumVersion = $NodeMinimumVersion
    CodexStoreProductId = $CodexStoreProductId
    Error         = $null
}

$snapshot | ConvertTo-Json -Depth 8 -Compress
