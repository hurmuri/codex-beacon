# Codex Beacon Windows Installer
# Run via: irm https://hurmuri.github.io/codex-beacon/install.ps1 | iex

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host ""
Write-Host "  =======================================================" -ForegroundColor Cyan
Write-Host "    Codex Beacon · All-in-One Control Center for Codex   " -ForegroundColor White
Write-Host "  =======================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check Windows Version (Requires Windows 10 1809+ / Windows 11)
$osVersion = [System.Environment]::OSVersion.Version
if ($osVersion.Major -lt 10 -or ($osVersion.Major -eq 10 -and $osVersion.Build -lt 17763)) {
    Write-Error "Codex Beacon requires Windows 10 1809 (Build 17763) or Windows 11."
    exit 1
}

# 2. Determine target install directory in %LOCALAPPDATA%
$installDir = Join-Path $env:LOCALAPPDATA "CodexBeacon"
$targetExe = Join-Path $installDir "CodexBeacon-portable.exe"

if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

Write-Host "==> Fetching latest release metadata from GitHub..." -ForegroundColor Yellow
$repo = "hurmuri/codex-beacon"
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"

try {
    $release = Invoke-RestMethod -Uri $apiUrl -UseBasicParsing
    $tag = $release.tag_name
    Write-Host "==> Latest version identified: $tag" -ForegroundColor Green
    
    $asset = $release.assets | Where-Object { $_.name -eq 'CodexBeacon-portable.exe' } | Select-Object -First 1
    if ($asset) {
        $downloadUrl = $asset.browser_download_url
    } else {
        $downloadUrl = "https://github.com/$repo/releases/latest/download/CodexBeacon-portable.exe"
    }
} catch {
    Write-Host "==> Direct release lookup fallback..." -ForegroundColor Yellow
    $downloadUrl = "https://github.com/$repo/releases/latest/download/CodexBeacon-portable.exe"
}

Write-Host "==> Downloading CodexBeacon-portable.exe..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $downloadUrl -OutFile $targetExe -UseBasicParsing

if (Test-Path $targetExe) {
    Write-Host "==> Download complete! Installed to: $targetExe" -ForegroundColor Green
    
    # 3. Create Desktop Shortcut
    try {
        $wshShell = New-Object -ComObject WScript.Shell
        $desktopPath = [System.Environment]::GetFolderPath('Desktop')
        $shortcutPath = Join-Path $desktopPath "Codex Beacon.lnk"
        $shortcut = $wshShell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $targetExe
        $shortcut.WorkingDirectory = $installDir
        $shortcut.Description = "Codex Beacon - All-in-One Control Center for Codex"
        $shortcut.Save()
        Write-Host "==> Created desktop shortcut: $shortcutPath" -ForegroundColor Green
    } catch {
        # Optional shortcut creation failure ignored
    }

    Write-Host ""
    Write-Host "  Launch command: & '$targetExe'" -ForegroundColor Cyan
    Write-Host "  Starting Codex Beacon now..." -ForegroundColor Green
    Write-Host ""
    Start-Process -FilePath $targetExe
} else {
    Write-Error "Failed to install Codex Beacon."
    exit 1
}

