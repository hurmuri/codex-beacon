param(
    [Parameter(Mandatory=$true)][ValidateSet('appinstaller','winget','msstore','nvm','desktop','codex','opencodex','relay','tailscale','all','provider')][string]$Component,
    [Parameter(Mandatory=$true)][ValidateSet('install','upgrade','login','start','stop','restart','kill','install-node','use-node','use','use-stored','integrate','models','test-model')][string]$Action,
    [string]$Version = '',
    [string]$SettingsPath = ''
)

# ---------------------------------------------------------------------------
# Codex Beacon action runner
#
# Stdout protocol
#   * Any line that does not start with the marker is progress output produced
#     by a native tool (nvm/npm/winget downloads and installs). The UI streams
#     it so long operations never look frozen.
#   * The last marker line carries the ActionResult as JSON:
#       { Success, MessageKey, MessageArgs, HintKey, HintArgs, Details }
#     Localization lives in resources.resw, never here.
#
# Exit code 0 means "a result was produced". Failure is expressed by
# Success=false inside the payload.
# ---------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding

$script:Marker = '##RESULT##'
$script:ProgressMarker = '##PROGRESS##'
$script:Separator = [char]0x1F
$script:ActionStarted = Get-Date

$settings = @{
    RelayTaskName = 'Codex Relay'
    ProxyTaskName = 'opencodex-proxy'
    NodeMirror    = 'https://cdn.npmmirror.com/binaries/node'
    NpmRegistry   = 'https://registry.npmjs.org/'
    NetworkMode   = 'system'
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
if ($settings.NetworkMode -eq 'custom' -and [string]$settings.CustomHttpProxy -match '^https?://') {
    $env:HTTP_PROXY = [string]$settings.CustomHttpProxy
    $env:HTTPS_PROXY = [string]$settings.CustomHttpProxy
}

function Convert-Args([object[]]$Values) {
    if (-not $Values) { return '' }
    $flat = @()
    foreach ($value in $Values) { $flat += [string]$value }
    return ($flat -join $script:Separator)
}

function Protect-Output([string]$Text) {
    if (-not $Text) { return '' }
    return ($Text.Trim() -replace '(?i)(token|authorization|api[_-]?key|bearer)\s*[=:]\s*\S+', '$1=<redacted>')
}

# Writes a progress line straight to stdout. [Console] is used instead of
# Write-Output/Write-Host because those are captured by the calling function's
# return value (or the information stream) and would never reach the UI live.
function Write-Progress-Line([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return }
    [Console]::Out.WriteLine($Text)
    [Console]::Out.Flush()
}

function Write-Progress-Event {
    param(
        [string]$Stage, [string]$Source = '', [string]$Message = '',
        [Nullable[double]]$Percent = $null, [long]$BytesReceived = 0,
        [Nullable[long]]$TotalBytes = $null
    )
    $elapsed = [Math]::Max(0.1, ((Get-Date) - $script:ActionStarted).TotalSeconds)
    $speed = if ($BytesReceived -gt 0) { [double]$BytesReceived / $elapsed } else { $null }
    $eta = if ($speed -and $TotalBytes -and $TotalBytes -gt $BytesReceived) { [double]($TotalBytes - $BytesReceived) / $speed } else { $null }
    $payload = [ordered]@{
        Stage = $Stage; BytesReceived = $BytesReceived; TotalBytes = $TotalBytes; Percent = $Percent
        BytesPerSecond = $speed; EtaSeconds = $eta
        ElapsedSeconds = [Math]::Round($elapsed, 1)
        Source = $Source; Message = (Protect-Output $Message)
    }
    [Console]::Out.WriteLine($script:ProgressMarker + ($payload | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
}

function Convert-DownloadSize([double]$Value, [string]$Unit) {
    switch ($Unit.ToUpperInvariant()) {
        'KB' { return [long]($Value * 1KB) }
        'MB' { return [long]($Value * 1MB) }
        'GB' { return [long]($Value * 1GB) }
        default { return [long]$Value }
    }
}

function Invoke-TrackedCommand {
    param([string]$File, [string[]]$Arguments, [string]$Stage, [string]$Source)
    $lines = New-Object System.Collections.Generic.List[string]
    & $File @Arguments 2>&1 | ForEach-Object {
        $line = [string]$_
        [void]$lines.Add($line)
        $percent = $null
        $received = 0L
        $total = $null
        if ($line -match '(\d+(?:\.\d+)?)\s*(KB|MB|GB)\s*/\s*(\d+(?:\.\d+)?)\s*(KB|MB|GB)') {
            $received = Convert-DownloadSize ([double]$matches[1]) $matches[2]
            $total = Convert-DownloadSize ([double]$matches[3]) $matches[4]
            if ($total -gt 0) { $percent = [Math]::Min(100, ($received * 100.0 / $total)) }
        } elseif ($line -match '(\d{1,3})(?:\.\d+)?\s*%') {
            $percent = [double]$matches[1]
        }
        Write-Progress-Event -Stage $Stage -Source $Source -Percent $percent -BytesReceived $received -TotalBytes $total
    }
    return [pscustomobject]@{ ExitCode=$LASTEXITCODE; Output=($lines -join "`r`n") }
}

function Write-Result {
    param(
        [bool]$Success,
        [string]$MessageKey,
        [object[]]$MessageArgs,
        [string]$HintKey = '',
        [object[]]$HintArgs,
        [string]$Details = ''
    )
    $payload = [ordered]@{
        Success     = $Success
        MessageKey  = $MessageKey
        MessageArgs = (Convert-Args $MessageArgs)
        HintKey     = $HintKey
        HintArgs    = (Convert-Args $HintArgs)
        Details     = (Protect-Output $Details)
    }
    Write-Output ($script:Marker + ($payload | ConvertTo-Json -Compress))
    exit 0
}

function Assert-Prerequisites {
    $node = Get-Command node.exe -ErrorAction SilentlyContinue
    $script:NodeExecutable = if ($node) { $node.Source } else { '' }
    $script:NpmExecutable = if ($node) { Join-Path (Split-Path $node.Source -Parent) 'npm.cmd' } else { '' }
    if (-not $node -or -not (Test-Path -LiteralPath $script:NpmExecutable)) {
        throw 'Node.js or npm is missing. Install Node.js 22.14.0 or newer first.'
    }
    $versionText = ((& node.exe --version) -replace '^v', '' -replace '-.*$', '').Trim()
    if ([version]::TryParse($versionText, [ref]$null) -and [version]$versionText -lt [version]'22.14.0') {
        throw "Node.js $versionText is too old; 22.14.0 or newer is required."
    }
}

function Invoke-NpmInstall([string]$PackageName, [bool]$RelayLocal) {
    Assert-Prerequisites
    $registryArgs = @()
    if ($settings.NpmRegistry) { $registryArgs = @('--registry', [string]$settings.NpmRegistry) }
    Write-Progress-Event -Stage 'Downloading' -Source ([string]$settings.NpmRegistry) -Message $PackageName
    if ($RelayLocal) {
        $relayApp = Join-Path $env:USERPROFILE '.codex-relay\app'
        New-Item -ItemType Directory -Path $relayApp -Force | Out-Null
        Write-Progress-Line "npm install $PackageName@latest --prefix $relayApp"
        $run = Invoke-TrackedCommand $script:NpmExecutable (@('install','--prefix',$relayApp,"$PackageName@latest",'--save-exact') + $registryArgs) 'Downloading' ([string]$settings.NpmRegistry)
    } else {
        Write-Progress-Line "npm install -g $PackageName@latest"
        $run = Invoke-TrackedCommand $script:NpmExecutable (@('install','-g',"$PackageName@latest") + $registryArgs) 'Downloading' ([string]$settings.NpmRegistry)
    }
    $output = $run.Output
    if ($run.ExitCode -ne 0) { throw $output.Trim() }
    Write-Progress-Event -Stage 'Completed' -Source ([string]$settings.NpmRegistry) -Message $PackageName -Percent 100
    return $output.Trim()
}

function Get-RelayCli {
    $path = Join-Path $env:USERPROFILE '.codex-relay\app\node_modules\codex-relay\dist\cli.js'
    if (-not (Test-Path -LiteralPath $path)) { throw 'Codex Relay is not installed.' }
    return $path
}

function Invoke-Relay([string[]]$Arguments) {
    Assert-Prerequisites
    $relayHome = Join-Path $env:USERPROFILE '.codex-relay'
    New-Item -ItemType Directory -Path $relayHome -Force | Out-Null
    Push-Location $relayHome
    try {
        $output = & $script:NodeExecutable (Get-RelayCli) @Arguments 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw $output.Trim() }
        return $output.Trim()
    } finally { Pop-Location }
}

# nvm-windows downloads from nodejs.org by default, which is unreachable on
# many networks. Try the configured mirror first and fall back to the official
# host only when the mirror fails, so this never hangs on a dead endpoint.
function Invoke-Nvm {
    param([string]$NvmExecutable, [string[]]$Arguments)
    $attempts = @()
    if ($settings.NodeMirror) { $attempts += $settings.NodeMirror }
    $attempts += ''
    $lastOutput = ''
    $index = 0
    foreach ($mirror in $attempts) {
        $index++
        if ($mirror) {
            $env:NVM_NODEJS_ORG_MIRROR = $mirror
            Write-Progress-Line "Using Node.js mirror: $mirror"
        } else {
            Remove-Item Env:\NVM_NODEJS_ORG_MIRROR -ErrorAction SilentlyContinue
            Write-Progress-Line 'Falling back to the official Node.js distribution host.'
        }
        $source = if ($mirror) { $mirror } else { 'https://nodejs.org/dist/' }
        $run = Invoke-TrackedCommand $NvmExecutable $Arguments 'Downloading' $source
        $lastOutput = $run.Output.Trim()
        if ($run.ExitCode -eq 0) { return $lastOutput }
        Write-Progress-Line "nvm exited with code $($run.ExitCode)."
        if ($index -lt $attempts.Count) { Write-Progress-Line 'Mirror attempt failed, retrying...' }
    }
    throw $lastOutput
}

function Stop-CodexDesktop {
    $count = 0
    foreach ($p in (Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        ($_.ExecutablePath -match '(?i)WindowsApps[\\/]OpenAI\.Codex_[^\\/]+') -or ($_.Name -in @('ChatGPT.exe','codex.exe','codex-code-mode-host.exe'))
    })) {
        if ($p.ExecutablePath -match 'CodexBeacon|CodexServiceManager') { continue }
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
        $count++
    }
    return $count
}

function Stop-ServiceProcesses([string]$Target = 'all') {
    $patterns = @('codex-relay-service\.cmd','codex-relay-service-launcher\.vbs','node_modules[\\/]codex-relay[\\/]dist[\\/]cli\.js','@bitkyc08[\\/]opencodex','opencodex-proxy')
    $count = 0
    foreach ($p in (Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)) {
        $identity = "$($p.Name) $($p.ExecutablePath) $($p.CommandLine)"
        if ($identity -match 'CodexBeacon|CodexServiceManager|OpenAI\.Codex') { continue }
        if ($Target -eq 'relay' -and $identity -notmatch 'codex-relay') { continue }
        if ($Target -eq 'opencodex' -and $identity -notmatch 'opencodex') { continue }
        if ($patterns | Where-Object { $identity -match $_ }) {
            Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
            $count++
        }
    }
    return $count
}

function Set-Task([string]$Name, [string]$Mode) {
    $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if (-not $task) { throw "TASK_MISSING::$Name" }
    if ($Mode -eq 'start') { Start-ScheduledTask -TaskName $Name }
    else { Stop-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue }
}

try {
    # --- provider switching ------------------------------------------------
    if ($Component -eq 'provider' -and $Action -eq 'use-stored') {
        $providersPath = Join-Path $env:USERPROFILE '.CodexBeacon\providers.json'
        if (-not (Test-Path -LiteralPath $providersPath)) { throw 'PROVIDER_STORE_MISSING' }
        $providerId = $Version.Trim()
        $savedProviders = @(Get-Content -Raw -LiteralPath $providersPath -Encoding UTF8 | ConvertFrom-Json)
        $savedProvider = @($savedProviders | Where-Object { $_.Id -eq $providerId } | Select-Object -First 1)
        if ($savedProvider.Count -eq 0) { throw 'PROVIDER_NOT_FOUND' }
        $opencodex = Get-Command opencodex -ErrorAction SilentlyContinue
        if (-not $opencodex) { throw 'PROXY_MISSING' }
        $provider = $savedProvider[0]
        $adapter = if ([string]$provider.WireApi -eq 'chat') { 'openai-chat' } else { 'openai-responses' }
        Write-Progress-Event -Stage 'Configuring' -Source 'OpenCodex' -Message $providerId
        $addOutput = & $opencodex.Source provider add $providerId --adapter $adapter --base-url ([string]$provider.BaseUrl) --default-model ([string]$provider.TestModel) --set-default --force --json 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw $addOutput.Trim() }
        if ([string]$provider.ApiKey) {
            try {
                $accountJson = & $opencodex.Source account list $providerId --json 2>$null | Out-String | ConvertFrom-Json
                foreach ($account in @($accountJson.accounts | Where-Object { $_.label -eq 'Codex Beacon' })) {
                    & $opencodex.Source account remove $providerId ([string]$account.id) --yes --json 2>$null | Out-Null
                }
            } catch { }
            $keyOutput = ([string]$provider.ApiKey) | & $opencodex.Source account add-key $providerId --label 'Codex Beacon' --json 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) {
                $safeKeyOutput = $keyOutput.Replace([string]$provider.ApiKey, '<redacted>')
                throw (Protect-Output $safeKeyOutput.Trim())
            }
        }
        $integrationOutput = & $opencodex.Source integration native codex on --json 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw $integrationOutput.Trim() }
        Write-Progress-Event -Stage 'Completed' -Source 'OpenCodex' -Message $providerId -Percent 100
        Write-Result -Success $true -MessageKey 'ActionProviderSwitched' -MessageArgs @($providerId) -Details 'Provider configured through OpenCodex.'
    }

    if ($Component -eq 'provider' -and $Action -eq 'use') {
        $configPath = Join-Path $env:USERPROFILE '.codex\config.toml'
        if (-not (Test-Path -LiteralPath $configPath)) { throw 'CONFIG_MISSING' }
        $providerToSet = $Version.Trim()
        # Local proxy providers are only reachable through openai_base_url, so that
        # key has to exist in the file, not merely be rewritten when present.
        $isLocalProxy = $providerToSet -eq 'opencodex'
        $proxyBaseUrl = 'http://127.0.0.1:10100/v1'
        $keepsModelProvider = $providerToSet -notin @('custom', 'default', '')

        $lines = @(Get-Content -LiteralPath $configPath -Encoding UTF8)
        $newLines = @()
        $modelProviderIndex = -1
        $sawModelProvider = $false
        $sawBaseUrl = $false
        foreach ($line in $lines) {
            $trimmed = $line.Trim()
            if ($trimmed -match '^model_provider\s*=') {
                $sawModelProvider = $true
                if ($keepsModelProvider) {
                    $modelProviderIndex = $newLines.Count
                    $newLines += ('model_provider = "' + $providerToSet + '"')
                }
                continue
            }
            # openai_base_url is managed by whichever provider is selected, so
            # drop stale local-proxy injections when switching away from them.
            if ($trimmed -match '^openai_base_url\s*=') {
                $sawBaseUrl = $true
                if ($isLocalProxy) { $newLines += ('openai_base_url = "' + $proxyBaseUrl + '"') }
                continue
            }
            $newLines += $line
        }

        if (-not $sawModelProvider -and $keepsModelProvider) {
            $newLines = @('model_provider = "' + $providerToSet + '"') + $newLines
            $modelProviderIndex = 0
        }

        if ($isLocalProxy -and -not $sawBaseUrl) {
            # Insert directly below model_provider, but never past the first
            # section header - a top-level key placed inside a table is invalid.
            $limit = $newLines.Count
            for ($i = 0; $i -lt $newLines.Count; $i++) {
                if ($newLines[$i].Trim().StartsWith('[')) { $limit = $i; break }
            }
            $insertAt = 0
            if ($modelProviderIndex -ge 0) { $insertAt = [Math]::Min($modelProviderIndex + 1, $limit) }
            $rebuilt = @()
            if ($insertAt -gt 0) { $rebuilt += $newLines[0..($insertAt - 1)] }
            $rebuilt += ('openai_base_url = "' + $proxyBaseUrl + '"')
            if ($insertAt -lt $newLines.Count) { $rebuilt += $newLines[$insertAt..($newLines.Count - 1)] }
            $newLines = $rebuilt
        }

        [System.IO.File]::WriteAllText($configPath, ($newLines -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
        Write-Result -Success $true -MessageKey 'ActionProviderSwitched' -MessageArgs @($providerToSet) -Details 'config.toml updated'
    }

    # --- App Installer / WinGet / Microsoft Store -------------------------
    if ($Component -in @('appinstaller','winget')) {
        Start-Process 'ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1'
        Write-Result -Success $true -MessageKey 'ActionAppInstallerStoreOpened' -Details 'Microsoft Store App Installer page opened.'
    }
    if ($Component -eq 'msstore') {
        $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
        if (-not $winget) { throw 'WINGET_MISSING_STORE' }
        Write-Progress-Event -Stage 'Repairing' -Source 'winget / msstore' -Message 'Updating package sources'
        $output = & $winget.Source source update --name msstore --disable-interactivity 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw $output.Trim() }
        Write-Progress-Event -Stage 'Completed' -Source 'winget / msstore' -Message 'Source available' -Percent 100
        Write-Result -Success $true -MessageKey 'ActionStoreSourceReady' -Details $output
    }

    # --- nvm ---------------------------------------------------------------
    if ($Component -eq 'nvm') {
        $nvm = Get-Command nvm.exe -ErrorAction SilentlyContinue
        if ($Action -in @('install', 'upgrade')) {
            $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
            if (-not $winget) { throw 'WINGET_MISSING_NVM' }
            $verb = if ($Action -eq 'install') { 'install' } else { 'upgrade' }
            $run = Invoke-TrackedCommand $winget.Source @($verb,'--id','CoreyButler.NVMforWindows','--exact','--accept-package-agreements','--accept-source-agreements','--disable-interactivity') 'Downloading' 'winget'
            $output = $run.Output
            if ($run.ExitCode -ne 0) { throw $output.Trim() }
            Write-Result -Success $true -MessageKey 'ActionNvmInstalled' -Details $output
        }
        if (-not $nvm) { throw 'NVM_MISSING' }
        if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'NVM_VERSION_INVALID' }
        if ($Action -eq 'install-node') {
            Write-Progress-Event -Stage 'Downloading' -Source ([string]$settings.NodeMirror) -Message ("Node.js " + $Version)
            $output = Invoke-Nvm -NvmExecutable $nvm.Source -Arguments @('install', $Version)
            Write-Progress-Event -Stage 'Completed' -Source ([string]$settings.NodeMirror) -Message ("Node.js " + $Version) -Percent 100
            Write-Result -Success $true -MessageKey 'ActionNvmNodeInstalled' -MessageArgs @($Version) -Details $output
        } elseif ($Action -eq 'use-node') {
            $output = Invoke-Nvm -NvmExecutable $nvm.Source -Arguments @('use', $Version)
            Write-Result -Success $true -MessageKey 'ActionNvmNodeSwitched' -MessageArgs @($Version) -Details $output
        } else { throw 'NVM_UNSUPPORTED' }
    }

    if ($Component -eq 'opencodex' -and $Action -in @('integrate','models','test-model')) {
        $opencodex = Get-Command opencodex -ErrorAction SilentlyContinue
        if (-not $opencodex) { throw 'PROXY_MISSING' }
        if ($Action -eq 'integrate') {
            $details = & $opencodex.Source integration native codex on --json 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw $details.Trim() }
            Write-Result -Success $true -MessageKey 'ActionOpenCodexIntegrated' -Details $details
        }
        if ($Action -eq 'models') {
            $details = & $opencodex.Source models list --json 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw $details.Trim() }
            Write-Result -Success $true -MessageKey 'ActionOpenCodexModelsRefreshed' -Details 'Model catalog refreshed.'
        }
        $parts = $Version -split ([string][char]0x1F), 2
        $providerName = if ($parts.Count -gt 1) { $parts[0] } else { '' }
        if (-not $providerName) { throw 'OPENCODEX_MODEL_REQUIRED' }
        $details = & $opencodex.Source provider test $providerName --json 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw $details.Trim() }
        Write-Result -Success $true -MessageKey 'ProviderTestSucceeded' -MessageArgs @($Version) -Details 'OpenCodex provider test succeeded.'
    }

    # --- desktop client ----------------------------------------------------
    if ($Component -eq 'desktop') {
        $appUserModelId = 'shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App'
        if ($Action -eq 'login' -or $Action -eq 'start') {
            Start-Process $appUserModelId
            if ($Action -eq 'login') {
                Write-Result -Success $true -MessageKey 'ActionDesktopLoginOpened' -HintKey 'ActionDesktopLoginHint'
            }
            Write-Result -Success $true -MessageKey 'ActionDesktopStarted'
        } elseif ($Action -in @('install', 'upgrade')) {
            $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
            if (-not $winget) { throw 'WINGET_MISSING_DESKTOP' }
            $verb = if ($Action -eq 'install') { 'install' } else { 'upgrade' }
            $run = Invoke-TrackedCommand $winget.Source @($verb,'9PLM9XGG6VKS','--source','msstore','--accept-package-agreements','--accept-source-agreements','--disable-interactivity') 'Downloading' 'Microsoft Store / winget'
            $output = $run.Output
            if ($run.ExitCode -ne 0) { throw $output.Trim() }
            Write-Progress-Event -Stage 'Completed' -Source 'Microsoft Store / winget' -Message 'ChatGPT' -Percent 100
            Write-Result -Success $true -MessageKey 'ActionPackageInstalled' -MessageArgs @('ChatGPT') -Details $output
        } elseif ($Action -in @('stop', 'kill')) {
            $count = Stop-CodexDesktop
            Write-Result -Success $true -MessageKey 'ActionDesktopStopped' -MessageArgs @($count)
        } elseif ($Action -eq 'restart') {
            $count = Stop-CodexDesktop
            Start-Sleep -Milliseconds 800
            Start-Process $appUserModelId
            Write-Result -Success $true -MessageKey 'ActionDesktopRestarted' -MessageArgs @($count)
        }
        throw 'DESKTOP_UNSUPPORTED'
    }

    # --- tailscale ---------------------------------------------------------
    if ($Component -eq 'tailscale') {
        if ($Action -eq 'login') {
            Start-Process powershell.exe -ArgumentList '-NoExit', '-Command', 'tailscale login'
            Write-Result -Success $true -MessageKey 'ActionTailscaleLoginOpened' -HintKey 'ActionTailscaleLoginHint'
        } elseif ($Action -in @('install', 'upgrade')) {
            $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
            if (-not $winget) { throw 'WINGET_MISSING_TAILSCALE' }
            $verb = if ($Action -eq 'install') { 'install' } else { 'upgrade' }
            $run = Invoke-TrackedCommand $winget.Source @($verb,'--id','Tailscale.Tailscale','--exact','--accept-package-agreements','--accept-source-agreements','--disable-interactivity') 'Downloading' 'winget'
            $output = $run.Output
            if ($run.ExitCode -ne 0) { throw $output.Trim() }
            Write-Result -Success $true -MessageKey 'ActionTailscaleInstalled' -Details $output
        } elseif ($Action -eq 'start') { Start-Service -Name Tailscale; Write-Result -Success $true -MessageKey 'ActionTailscaleStarted' }
        elseif ($Action -eq 'stop') { Stop-Service -Name Tailscale; Write-Result -Success $true -MessageKey 'ActionTailscaleStopped' }
        elseif ($Action -eq 'restart') { Restart-Service -Name Tailscale; Write-Result -Success $true -MessageKey 'ActionTailscaleRestarted' }
        else { throw 'TAILSCALE_UNSUPPORTED' }
    }

    # --- codex CLI ---------------------------------------------------------
    if ($Component -eq 'codex' -and $Action -eq 'login') {
        if (-not (Get-Command codex -ErrorAction SilentlyContinue)) { throw 'CODEX_MISSING' }
        Start-Process powershell.exe -ArgumentList '-NoExit', '-Command', 'codex login'
        Write-Result -Success $true -MessageKey 'ActionCodexLoginOpened' -HintKey 'ActionCodexLoginHint'
    }
    # The CLI is not a long-running service, but install/upgrade still flow into
    # the npm package branch below, so only the service verbs are rejected here.
    if ($Component -eq 'codex' -and $Action -in @('start', 'stop', 'restart', 'kill')) {
        throw 'CODEX_NOT_A_SERVICE'
    }

    # --- npm packages ------------------------------------------------------
    if ($Action -in @('install', 'upgrade')) {
        $package = switch ($Component) {
            'codex' { '@openai/codex' }
            'opencodex' { '@bitkyc08/opencodex' }
            'relay' { 'codex-relay' }
            default { throw 'BULK_INSTALL_UNSUPPORTED' }
        }
        $details = Invoke-NpmInstall $package ($Component -eq 'relay')
        $hintKey = switch ($Component) {
            'opencodex' { 'ActionHintProxyStart' }
            'relay' { 'ActionHintRelayStart' }
            default { '' }
        }
        Write-Result -Success $true -MessageKey 'ActionPackageInstalled' -MessageArgs @($package) -HintKey $hintKey -Details $details
    }

    # --- bulk recovery -----------------------------------------------------
    if ($Component -eq 'all') {
        $relayTask = Get-ScheduledTask -TaskName $settings.RelayTaskName -ErrorAction SilentlyContinue
        $proxyTask = Get-ScheduledTask -TaskName $settings.ProxyTaskName -ErrorAction SilentlyContinue
        if ($relayTask) { Stop-ScheduledTask -TaskName $settings.RelayTaskName -ErrorAction SilentlyContinue }
        elseif (Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.codex-relay\app\node_modules\codex-relay\dist\cli.js')) { try { Invoke-Relay @('stop') | Out-Null } catch { } }
        if ($proxyTask) { Stop-ScheduledTask -TaskName $settings.ProxyTaskName -ErrorAction SilentlyContinue }
        elseif (Get-Command opencodex -ErrorAction SilentlyContinue) { try { & opencodex service stop 2>$null | Out-Null } catch { } }
        $count = Stop-ServiceProcesses
        if ($Action -eq 'restart') {
            $started = @()
            if ($proxyTask) { Start-ScheduledTask -TaskName $settings.ProxyTaskName; $started += 'OpenCodex' }
            elseif (Get-Command opencodex -ErrorAction SilentlyContinue) { & opencodex service start 2>$null | Out-Null; if ($LASTEXITCODE -eq 0) { $started += 'OpenCodex' } }
            if ($relayTask) { Start-ScheduledTask -TaskName $settings.RelayTaskName; $started += 'Relay' }
            elseif (Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.codex-relay\app\node_modules\codex-relay\dist\cli.js')) { Invoke-Relay @('--bg') | Out-Null; $started += 'Relay' }
            $startedLabel = if ($started.Count -gt 0) { $started -join ', ' } else { 'none' }
            Write-Result -Success $true -MessageKey 'ActionServicesRestarted' -MessageArgs @($count, $startedLabel)
        }
        Write-Result -Success $true -MessageKey 'ActionServicesTerminated' -MessageArgs @($count)
    }

    # --- opencodex / relay service control ---------------------------------
    $taskName = if ($Component -eq 'relay') { $settings.RelayTaskName } else { $settings.ProxyTaskName }
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue

    if ($Component -eq 'relay' -and -not $task) {
        if ($Action -eq 'start') { $details = Invoke-Relay @('--bg'); Write-Result -Success $true -MessageKey 'ActionRelayStarted' -Details $details }
        elseif ($Action -eq 'stop') { $details = Invoke-Relay @('stop'); Write-Result -Success $true -MessageKey 'ActionRelayStopped' -Details $details }
        elseif ($Action -eq 'restart') { Invoke-Relay @('stop') | Out-Null; Start-Sleep -Milliseconds 500; $details = Invoke-Relay @('--bg'); Write-Result -Success $true -MessageKey 'ActionRelayRestarted' -Details $details }
        else { throw 'RELAY_UNSUPPORTED' }
    }

    if ($Component -eq 'opencodex' -and -not $task) {
        $opencodex = Get-Command opencodex -ErrorAction SilentlyContinue
        if (-not $opencodex) { throw 'PROXY_MISSING' }
        $subcommand = if ($Action -eq 'restart') { 'restart' } else { $Action }
        $details = & $opencodex.Source service $subcommand 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw $details.Trim() }
        $messageKey = switch ($Action) { 'start' { 'ActionProxyStarted' } 'stop' { 'ActionProxyStopped' } default { 'ActionProxyRestarted' } }
        Write-Result -Success $true -MessageKey $messageKey -Details $details
    }

    if ($Action -eq 'start') { Set-Task $taskName 'start'; Write-Result -Success $true -MessageKey 'ActionTaskStarted' -MessageArgs @($taskName) }
    elseif ($Action -eq 'stop') { Set-Task $taskName 'stop'; Stop-ServiceProcesses $Component | Out-Null; Write-Result -Success $true -MessageKey 'ActionTaskStopped' -MessageArgs @($taskName) }
    elseif ($Action -eq 'restart') {
        Set-Task $taskName 'stop'
        Stop-ServiceProcesses $Component | Out-Null
        Start-Sleep -Milliseconds 800
        Set-Task $taskName 'start'
        Write-Result -Success $true -MessageKey 'ActionTaskRestarted' -MessageArgs @($taskName)
    } else { throw 'ACTION_UNSUPPORTED' }
}
catch {
    $reason = $_.Exception.Message
    # Known sentinels map to localized sentences; anything else is raw tool
    # output and is only shown in the details pane.
    switch -Regex ($reason) {
        '^TASK_MISSING::(.+)$' { Write-Result -Success $false -MessageKey 'ActionTaskMissing' -MessageArgs @($matches[1]) -Details $reason }
        '^Node\.js or npm is missing' { Write-Result -Success $false -MessageKey 'ActionNeedRuntime' -Details $reason }
        '^Node\.js (.+) is too old' { Write-Result -Success $false -MessageKey 'ActionRuntimeTooOld' -MessageArgs @($matches[1]) -Details $reason }
        '^Codex Relay is not installed' { Write-Result -Success $false -MessageKey 'ActionRelayMissing' -Details $reason }
        '^NVM_MISSING$' { Write-Result -Success $false -MessageKey 'ActionNvmMissing' -Details $reason }
        '^NVM_VERSION_INVALID$' { Write-Result -Success $false -MessageKey 'ActionNvmVersionInvalid' -Details $reason }
        '^NVM_UNSUPPORTED$' { Write-Result -Success $false -MessageKey 'ActionNvmUnsupported' -Details $reason }
        '^WINGET_MISSING_NVM$' { Write-Result -Success $false -MessageKey 'ActionWingetMissingNvm' -Details $reason }
        '^WINGET_MISSING_TAILSCALE$' { Write-Result -Success $false -MessageKey 'ActionWingetMissingTailscale' -Details $reason }
        '^CODEX_MISSING$' { Write-Result -Success $false -MessageKey 'ActionCodexMissing' -Details $reason }
        '^CODEX_NOT_A_SERVICE$' { Write-Result -Success $false -MessageKey 'ActionCodexNotAService' -Details $reason }
        '^PROXY_MISSING$' { Write-Result -Success $false -MessageKey 'ActionProxyMissing' -Details $reason }
        '^BULK_INSTALL_UNSUPPORTED$' { Write-Result -Success $false -MessageKey 'ActionBulkInstallUnsupported' -Details $reason }
        '^CONFIG_MISSING$' { Write-Result -Success $false -MessageKey 'ActionConfigMissing' -Details $reason }
        '^RELAY_UNSUPPORTED$' { Write-Result -Success $false -MessageKey 'ActionUnsupported' -Details $reason }
        '^TAILSCALE_UNSUPPORTED$' { Write-Result -Success $false -MessageKey 'ActionUnsupported' -Details $reason }
        '^DESKTOP_UNSUPPORTED$' { Write-Result -Success $false -MessageKey 'ActionUnsupported' -Details $reason }
        '^ACTION_UNSUPPORTED$' { Write-Result -Success $false -MessageKey 'ActionUnsupported' -Details $reason }
        'denied|administrator|privilege|elevation|EPERM|EACCES' {
            Write-Result -Success $false -MessageKey 'ActionNeedsAdmin' -Details $reason
        }
        default { Write-Result -Success $false -MessageKey 'ActionFailed' -Details $reason }
    }
}
