param(
    [Parameter(Mandatory=$true)][ValidateSet('nvm','desktop','codex','opencodex','relay','tailscale','all')][string]$Component,
    [Parameter(Mandatory=$true)][ValidateSet('install','upgrade','login','start','stop','restart','kill','install-node','use-node')][string]$Action,
    [string]$Version = '',
    [string]$SettingsPath = '',
    [ValidateSet('en-US','zh-CN')][string]$Language = 'en-US'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
$settings = @{ RelayTaskName = 'Codex Relay'; ProxyTaskName = 'opencodex-proxy' }
if ($SettingsPath -and (Test-Path -LiteralPath $SettingsPath)) {
    try {
        $saved = Get-Content -Raw -LiteralPath $SettingsPath -Encoding UTF8 | ConvertFrom-Json
        if ($saved.RelayTaskName) { $settings.RelayTaskName = $saved.RelayTaskName }
        if ($saved.ProxyTaskName) { $settings.ProxyTaskName = $saved.ProxyTaskName }
    } catch {}
}

function Convert-ActionText([string]$Text) {
    if ($Language -ne 'en-US' -or [string]::IsNullOrEmpty($Text)) { return $Text }
    $exact = @{
        '缺少 Node.js 或 npm。请先安装 Node.js 22.14.0 或更高版本。' = 'Node.js or npm is missing. Install Node.js 22.14.0 or newer first.'
        'Codex Relay 尚未安装。请先完成安装。' = 'Codex Relay is not installed. Complete installation first.'
        '未检测到 winget，无法安装或升级 NVM for Windows。' = 'winget was not detected, so NVM for Windows cannot be installed or upgraded.'
        'NVM for Windows 安装状态已更新' = 'NVM for Windows installation status updated'
        '未检测到 NVM for Windows。请先安装 NVM 并重新打开 Codex Beacon。' = 'NVM for Windows was not detected. Install NVM and reopen Codex Beacon.'
        '请输入完整 Node.js 版本号，例如 24.15.0。' = 'Enter a complete Node.js version, for example 24.15.0.'
        'NVM 不支持此操作。' = 'NVM does not support this action.'
        '已打开 Codex 登录流程' = 'Codex sign-in opened'
        '请在 Codex 桌面客户端中完成登录，完成后返回并刷新。' = 'Complete sign-in in the Codex desktop app, then return and refresh.'
        '已打开 Codex 的 Microsoft Store 产品页' = 'Opened the Codex Microsoft Store product page'
        '产品 ID 9PLM9XGG6VKS；安装与升级由官方应用分发通道完成。' = 'Product ID 9PLM9XGG6VKS. Installation and upgrades use the official app distribution channel.'
        'Codex 桌面客户端已启动' = 'Codex desktop app started'
        'Codex 桌面客户端已关闭' = 'Codex desktop app closed'
        'Codex 桌面客户端已重新启动' = 'Codex desktop app restarted'
        '已打开 Tailscale 登录流程' = 'Tailscale sign-in opened'
        '请在新窗口中完成浏览器授权，完成后返回并刷新。' = 'Complete browser authorization in the new window, then return and refresh.'
        '未检测到 winget，无法安装或升级 Tailscale。' = 'winget was not detected, so Tailscale cannot be installed or upgraded.'
        'Tailscale 安装状态已更新' = 'Tailscale installation status updated'
        'Tailscale 服务已启动' = 'Tailscale service started'; 'Tailscale 服务已停止' = 'Tailscale service stopped'; 'Tailscale 服务已重新启动' = 'Tailscale service restarted'
        'Tailscale 不支持此操作。' = 'Tailscale does not support this action.'
        'Codex CLI 尚未安装。请先完成安装。' = 'Codex CLI is not installed. Complete installation first.'
        '请在新窗口中完成登录，完成后返回并刷新。' = 'Complete sign-in in the new window, then return and refresh.'
        '批量安装不受支持，请逐项执行。' = 'Bulk installation is not supported. Install components individually.'
        '安装完成。需要使用该扩展时，再点击“启动”完成服务配置。' = 'Installation complete. Select Start when you want to configure and use this extension.'
        '安装完成。点击“启动”会使用 Relay 官方后台模式运行。' = 'Installation complete. Select Start to use the official Relay background mode.'
        'Codex 可选服务已重新启动' = 'Optional Codex services restarted'
        'Codex 可选服务已终止' = 'Optional Codex services terminated'
        'Codex CLI 不是常驻服务；请在进程页管理正在运行的代理与 Relay。' = 'Codex CLI is not a persistent service. Manage active proxies and Relay from the Processes page.'
        'Codex Relay 已启动' = 'Codex Relay started'; 'Codex Relay 已停止' = 'Codex Relay stopped'; 'Codex Relay 已重新启动' = 'Codex Relay restarted'
        'OpenCodex 尚未安装。请先完成安装。' = 'OpenCodex is not installed. Complete installation first.'
        '操作未完成' = 'Action did not complete'
        '所有 Codex / ChatGPT 进程已关闭' = 'All Codex and ChatGPT processes terminated'
        'ChatGPT 桌面客户端已重新启动' = 'ChatGPT desktop app restarted'
        '已清理旧进程并重新拉起应用。' = 'Cleaned up stale processes and restarted the app.'
    }
    if ($exact.ContainsKey($Text)) { return $exact[$Text] }
    $Text = $Text -replace '^已终止 (\d+) 个相关进程。$', 'Terminated $1 related process(es).' 
    $Text = $Text -replace '^Node\.js (.+) 版本过低；需要 22\.14\.0 或更高版本。$', 'Node.js $1 is too old; version 22.14.0 or newer is required.'
    $Text = $Text -replace '^未找到计划任务 \[(.+)\]。请先完成对应服务安装。$', 'Scheduled task [$1] was not found. Install the corresponding service first.'
    $Text = $Text -replace '^Node\.js (.+) 已安装$', 'Node.js $1 installed'
    $Text = $Text -replace '^已切换到 Node\.js (.+)$', 'Switched to Node.js $1'
    $Text = $Text -replace '^(.+) 已安装为最新版$', '$1 installed at the latest version'
    $Text = $Text -replace '^终止了 (\d+) 个明确匹配的服务进程；已启动：(.+)。$', 'Terminated $1 explicitly matched service process(es); started: $2.'
    $Text = $Text -replace '^终止了 (\d+) 个明确匹配的服务进程；Codex 桌面应用和本管理器未受影响。$', 'Terminated $1 explicitly matched service process(es). The Codex desktop app and Codex Beacon were not affected.'
    $Text = $Text -replace '^不支持的操作：(.+)$', 'Unsupported action: $1'
    $Text = $Text -replace '^OpenCodex 服务已启动$', 'OpenCodex service started'
    $Text = $Text -replace '^OpenCodex 服务已停止$', 'OpenCodex service stopped'
    $Text = $Text -replace '^OpenCodex 服务已重新启动$', 'OpenCodex service restarted'
    $Text = $Text -replace '^(.+) 已启动$', '$1 started'
    $Text = $Text -replace '^(.+) 已停止$', '$1 stopped'
    $Text = $Text -replace '^(.+) 已重新启动$', '$1 restarted'
    return ($Text -replace '已隐藏','redacted' -replace 'OpenCodex 计划任务','OpenCodex scheduled task' -replace 'Relay 计划任务','Relay scheduled task' -replace 'OpenCodex 服务','OpenCodex service' -replace 'Relay 后台模式','Relay background mode' -replace '、',', ')
}

function Result([bool]$Success, [string]$Message, [string]$Details = '') {
    [ordered]@{ Success = $Success; Message = (Convert-ActionText $Message); Details = (Convert-ActionText $Details) } | ConvertTo-Json -Compress
}

function Assert-Prerequisites {
    $node = Get-Command node.exe -ErrorAction SilentlyContinue
    $script:NodeExecutable = if ($node) { $node.Source } else { '' }
    $script:NpmExecutable = if ($node) { Join-Path (Split-Path $node.Source -Parent) 'npm.cmd' } else { '' }
    if (-not $node -or -not (Test-Path -LiteralPath $script:NpmExecutable)) { throw '缺少 Node.js 或 npm。请先安装 Node.js 22.14.0 或更高版本。' }
    $versionText = (& node.exe --version).Trim() -replace '^v','' -replace '-.*$',''
    if ([version]$versionText -lt [version]'22.14.0') { throw "Node.js $versionText 版本过低；需要 22.14.0 或更高版本。" }
}

function Invoke-NpmInstall([string]$PackageName, [bool]$RelayLocal) {
    Assert-Prerequisites
    if ($RelayLocal) {
        $relayApp = Join-Path $env:USERPROFILE '.codex-relay\app'
        New-Item -ItemType Directory -Path $relayApp -Force | Out-Null
        $output = & $script:NpmExecutable install --prefix $relayApp "$PackageName@latest" --save-exact 2>&1 | Out-String
    } else {
        $output = & $script:NpmExecutable install -g "$PackageName@latest" 2>&1 | Out-String
    }
    if ($LASTEXITCODE -ne 0) { throw $output.Trim() }
    return ($output.Trim() -replace '(?i)(token|authorization|api[_-]?key)\s*[=:]\s*\S+','$1=<已隐藏>')
}

function Get-RelayCli {
    $path = Join-Path $env:USERPROFILE '.codex-relay\app\node_modules\codex-relay\dist\cli.js'
    if (-not (Test-Path -LiteralPath $path)) { throw 'Codex Relay 尚未安装。请先完成安装。' }
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

function Set-Task([string]$Name, [string]$Mode) {
    $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if (-not $task) { throw "未找到计划任务 [${Name}]。请先完成对应服务安装。" }
    if ($Mode -eq 'start') { Start-ScheduledTask -TaskName $Name }
    else { Stop-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue }
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

try {
    if ($Component -eq 'nvm') {
        $nvm = Get-Command nvm.exe -ErrorAction SilentlyContinue
        if ($Action -in @('install','upgrade')) {
            $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
            if (-not $winget) { throw '未检测到 winget，无法安装或升级 NVM for Windows。' }
            $verb = if ($Action -eq 'install') { 'install' } else { 'upgrade' }
            $output = & $winget.Source $verb --id CoreyButler.NVMforWindows --exact --accept-package-agreements --accept-source-agreements 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw $output.Trim() }
            Result $true 'NVM for Windows 安装状态已更新' $output.Trim()
            exit 0
        }
        if (-not $nvm) { throw '未检测到 NVM for Windows。请先安装 NVM 并重新打开 Codex Beacon。' }
        if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '请输入完整 Node.js 版本号，例如 24.15.0。' }
        if ($Action -eq 'install-node') {
            $output = & $nvm.Source install $Version 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw $output.Trim() }
            Result $true "Node.js $Version 已安装" $output.Trim()
        } elseif ($Action -eq 'use-node') {
            $output = & $nvm.Source use $Version 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw $output.Trim() }
            Result $true "已切换到 Node.js $Version" $output.Trim()
        } else { throw 'NVM 不支持此操作。' }
        exit 0
    }
    if ($Component -eq 'desktop') {
        if ($Action -eq 'login') {
            Start-Process 'shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App'
            Result $true '已打开 Codex 登录流程' '请在 Codex 桌面客户端中完成登录，完成后返回并刷新。'
        } elseif ($Action -in @('install','upgrade')) {
            Start-Process 'ms-windows-store://pdp/?ProductId=9PLM9XGG6VKS'
            Result $true '已打开 Codex 的 Microsoft Store 产品页' '产品 ID 9PLM9XGG6VKS；安装与升级由官方应用分发通道完成。'
        } elseif ($Action -eq 'start') {
            Start-Process 'shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App'
            Result $true 'Codex 桌面客户端已启动'
        } elseif ($Action -in @('stop','kill')) {
            $count = Stop-CodexDesktop
            Result $true '所有 Codex / ChatGPT 进程已关闭' "已终止 $count 个相关进程。"
        } elseif ($Action -eq 'restart') {
            $count = Stop-CodexDesktop
            Start-Sleep -Milliseconds 800
            Start-Process 'shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App'
            Result $true 'ChatGPT 桌面客户端已重新启动' "已清理旧进程并重新拉起应用。"
        }
        exit 0
    }
    if ($Component -eq 'tailscale') {
        if ($Action -eq 'login') {
            Start-Process powershell.exe -ArgumentList '-NoExit','-Command','tailscale login'
            Result $true '已打开 Tailscale 登录流程' '请在新窗口中完成浏览器授权，完成后返回并刷新。'
        } elseif ($Action -in @('install','upgrade')) {
            $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
            if (-not $winget) { throw '未检测到 winget，无法安装或升级 Tailscale。' }
            $verb = if ($Action -eq 'install') { 'install' } else { 'upgrade' }
            $output = & $winget.Source $verb --id Tailscale.Tailscale --exact --accept-package-agreements --accept-source-agreements 2>&1 | Out-String
            if ($LASTEXITCODE -ne 0) { throw $output.Trim() }
            Result $true 'Tailscale 安装状态已更新' $output.Trim()
        } elseif ($Action -eq 'start') { Start-Service -Name Tailscale; Result $true 'Tailscale 服务已启动' }
        elseif ($Action -eq 'stop') { Stop-Service -Name Tailscale; Result $true 'Tailscale 服务已停止' }
        elseif ($Action -eq 'restart') { Restart-Service -Name Tailscale; Result $true 'Tailscale 服务已重新启动' }
        else { throw 'Tailscale 不支持此操作。' }
        exit 0
    }
    if ($Component -eq 'codex' -and $Action -eq 'login') {
        if (-not (Get-Command codex -ErrorAction SilentlyContinue)) { throw 'Codex CLI 尚未安装。请先完成安装。' }
        Start-Process powershell.exe -ArgumentList '-NoExit','-Command','codex login'
        Result $true '已打开 Codex 登录流程' '请在新窗口中完成登录，完成后返回并刷新。'
        exit 0
    }
    if ($Action -in @('install','upgrade')) {
        $package = switch ($Component) { 'codex' { '@openai/codex' }; 'opencodex' { '@bitkyc08/opencodex' }; 'relay' { 'codex-relay' }; default { throw '批量安装不受支持，请逐项执行。' } }
        $details = Invoke-NpmInstall $package ($Component -eq 'relay')
        $next = if ($Component -eq 'opencodex') { '安装完成。需要使用该扩展时，再点击“启动”完成服务配置。' } elseif ($Component -eq 'relay') { '安装完成。点击“启动”会使用 Relay 官方后台模式运行。' } else { '' }
        Result $true "$package 已安装为最新版" "$details`n$next"
        exit 0
    }

    if ($Component -eq 'all') {
        $relayTask = Get-ScheduledTask -TaskName $settings.RelayTaskName -ErrorAction SilentlyContinue
        $proxyTask = Get-ScheduledTask -TaskName $settings.ProxyTaskName -ErrorAction SilentlyContinue
        if ($relayTask) { Stop-ScheduledTask -TaskName $settings.RelayTaskName -ErrorAction SilentlyContinue }
        elseif (Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.codex-relay\app\node_modules\codex-relay\dist\cli.js')) { try { Invoke-Relay @('stop') | Out-Null } catch {} }
        if ($proxyTask) { Stop-ScheduledTask -TaskName $settings.ProxyTaskName -ErrorAction SilentlyContinue }
        elseif (Get-Command opencodex -ErrorAction SilentlyContinue) { try { & opencodex service stop 2>$null | Out-Null } catch {} }
        $count = Stop-ServiceProcesses
        if ($Action -eq 'restart') {
            $started = @()
            if ($proxyTask) { Start-ScheduledTask -TaskName $settings.ProxyTaskName; $started += 'OpenCodex 计划任务' }
            elseif (Get-Command opencodex -ErrorAction SilentlyContinue) { & opencodex service start 2>$null | Out-Null; if ($LASTEXITCODE -eq 0) { $started += 'OpenCodex 服务' } }
            if ($relayTask) { Start-ScheduledTask -TaskName $settings.RelayTaskName; $started += 'Relay 计划任务' }
            elseif (Test-Path -LiteralPath (Join-Path $env:USERPROFILE '.codex-relay\app\node_modules\codex-relay\dist\cli.js')) { Invoke-Relay @('--bg') | Out-Null; $started += 'Relay 后台模式' }
            Result $true 'Codex 可选服务已重新启动' "终止了 $count 个明确匹配的服务进程；已启动：$($started -join '、')。"
        } else {
            Result $true 'Codex 可选服务已终止' "终止了 $count 个明确匹配的服务进程；Codex 桌面应用和本管理器未受影响。"
        }
        exit 0
    }

    if ($Component -eq 'codex') { throw 'Codex CLI 不是常驻服务；请在进程页管理正在运行的代理与 Relay。' }
    $taskName = if ($Component -eq 'relay') { $settings.RelayTaskName } else { $settings.ProxyTaskName }
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($Component -eq 'relay' -and -not $task) {
        if ($Action -eq 'start') { $details = Invoke-Relay @('--bg'); Result $true 'Codex Relay 已启动' $details }
        elseif ($Action -eq 'stop') { $details = Invoke-Relay @('stop'); Result $true 'Codex Relay 已停止' $details }
        elseif ($Action -eq 'restart') { Invoke-Relay @('stop') | Out-Null; Start-Sleep -Milliseconds 500; $details = Invoke-Relay @('--bg'); Result $true 'Codex Relay 已重新启动' $details }
        else { throw "不支持的操作：$Action" }
        exit 0
    }
    if ($Component -eq 'opencodex' -and -not $task) {
        $opencodex = Get-Command opencodex -ErrorAction SilentlyContinue
        if (-not $opencodex) { throw 'OpenCodex 尚未安装。请先完成安装。' }
        $subcommand = if ($Action -eq 'restart') { 'restart' } else { $Action }
        $details = & $opencodex.Source service $subcommand 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw $details.Trim() }
        Result $true "OpenCodex 服务已$(@{start='启动';stop='停止';restart='重新启动'}[$Action])" $details.Trim()
        exit 0
    }
    if ($Action -eq 'start') { Set-Task $taskName 'start'; Result $true "$taskName 已启动" }
    elseif ($Action -eq 'stop') { Set-Task $taskName 'stop'; Stop-ServiceProcesses $Component | Out-Null; Result $true "$taskName 已停止" }
    elseif ($Action -eq 'restart') { Set-Task $taskName 'stop'; Stop-ServiceProcesses $Component | Out-Null; Start-Sleep -Milliseconds 800; Set-Task $taskName 'start'; Result $true "$taskName 已重新启动" }
    else { throw "不支持的操作：$Action" }
}
catch {
    $safeMessage = $_.Exception.Message -replace '(?i)(token|authorization|api[_-]?key)\s*[=:]\s*\S+','$1=<已隐藏>'
    Result $false '操作未完成' ($safeMessage.Substring(0, [Math]::Min(1200, $safeMessage.Length)))
    exit 1
}
