param(
    [ValidateSet('all','portable','slim')][string]$Target = 'all'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path .
$artifactsDir = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
$launcherProj = Join-Path $repoRoot 'tools\Launcher\Launcher.csproj'
$payloadZip = Join-Path $repoRoot 'tools\Launcher\payload.zip'

# Clean up old packages and temporary folders
Get-ChildItem -Path $artifactsDir -Force | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

@('single-file-test', 'verify-portable', 'verify-runtime-dependent', 'win-x64') | ForEach-Object {
    $oldPub = Join-Path $repoRoot "publish\$_"
    if (Test-Path -LiteralPath $oldPub) {
        Remove-Item -LiteralPath $oldPub -Recurse -Force -ErrorAction SilentlyContinue
    }
}

@('payload.zip', 'out', 'out-portable', 'out-slim') | ForEach-Object {
    $oldLauncherItem = Join-Path $repoRoot "tools\Launcher\$_"
    if (Test-Path -LiteralPath $oldLauncherItem) {
        Remove-Item -LiteralPath $oldLauncherItem -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$version = if (Test-Path (Join-Path $repoRoot 'build\version.txt')) {
    (Get-Content (Join-Path $repoRoot 'build\version.txt') -Raw).Trim()
} else { '0.3.1' }
if (-not $version) { $version = '0.3.1' }

if ($Target -in @('all', 'portable')) {
    Write-Host "Publishing Portable App ($version)..." -ForegroundColor Cyan
    $portableOut = Join-Path $repoRoot 'publish\portable-win-x64'
    dotnet publish "$repoRoot\CodexBeacon.csproj" -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:AutoVersionIncrement=false -o $portableOut
    
    Write-Host "Creating portable payload archive..." -ForegroundColor Cyan
    Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path "$portableOut\*" -DestinationPath $payloadZip -Force

    Write-Host "Compiling Single-file Portable Executable..." -ForegroundColor Cyan
    $launcherOut = Join-Path $repoRoot 'tools\Launcher\out-portable'
    if (Test-Path (Join-Path $repoRoot "tools/Launcher/bin")) { [System.IO.Directory]::Delete((Join-Path $repoRoot "tools/Launcher/bin"), $true) }; if (Test-Path (Join-Path $repoRoot "tools/Launcher/obj")) { [System.IO.Directory]::Delete((Join-Path $repoRoot "tools/Launcher/obj"), $true) }; dotnet publish $launcherProj -c Release -r win-x64 -p:LauncherVariant=portable -p:DefineConstants="LAUNCHER_PORTABLE" -o $launcherOut
    $versionedExe = Join-Path (Join-Path $repoRoot 'publish') "CodexBeacon-$version.exe"
    $defaultExe   = Join-Path (Join-Path $repoRoot 'publish') "CodexBeacon.exe"
    Copy-Item "$launcherOut\Launcher.exe" $versionedExe -Force
    Copy-Item "$launcherOut\Launcher.exe" $defaultExe -Force
    Copy-Item "$launcherOut\Launcher.exe" "$artifactsDir\CodexBeacon-$version.exe" -Force
    Remove-Item $launcherOut -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Portable Single-file EXE built: $versionedExe" -ForegroundColor Green
}

if ($Target -in @('all', 'slim')) {
    Write-Host "Publishing Slim (Runtime-dependent) App ($version)..." -ForegroundColor Cyan
    $slimOut = Join-Path $repoRoot 'publish\runtime-dependent-win-x64'
    dotnet publish "$repoRoot\CodexBeacon.csproj" -c Release -r win-x64 --self-contained false -p:WindowsAppSDKSelfContained=true -p:AutoVersionIncrement=false -o $slimOut
    
    Write-Host "Creating slim payload archive..." -ForegroundColor Cyan
    Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path "$slimOut\*" -DestinationPath $payloadZip -Force

    Write-Host "Compiling Single-file Slim Executable..." -ForegroundColor Cyan
    $launcherOut = Join-Path $repoRoot 'tools\Launcher\out-slim'
    if (Test-Path (Join-Path $repoRoot "tools/Launcher/bin")) { [System.IO.Directory]::Delete((Join-Path $repoRoot "tools/Launcher/bin"), $true) }; if (Test-Path (Join-Path $repoRoot "tools/Launcher/obj")) { [System.IO.Directory]::Delete((Join-Path $repoRoot "tools/Launcher/obj"), $true) }; dotnet publish $launcherProj -c Release -r win-x64 -p:LauncherVariant=slim -p:DefineConstants="LAUNCHER_SLIM" -o $launcherOut
    $slimVersionedExe = Join-Path (Join-Path $repoRoot 'publish') "CodexBeacon-$version-slim.exe"
    Copy-Item "$launcherOut\Launcher.exe" $slimVersionedExe -Force
    Copy-Item "$launcherOut\Launcher.exe" "$artifactsDir\CodexBeacon-$version-slim.exe" -Force
    Remove-Item $launcherOut -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Slim Single-file EXE built: $slimVersionedExe" -ForegroundColor Green
}

Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue

Write-Host "Generating SHA-256 Checksums..." -ForegroundColor Cyan
Get-FileHash "$artifactsDir\*" -Algorithm SHA256 |
    Where-Object { $_.Path -notlike '*.txt' } |
    ForEach-Object { "$($_.Hash.ToLowerInvariant()) *$(Split-Path $_.Path -Leaf)" } |
    Set-Content -Path (Join-Path $artifactsDir 'SHA256SUMS.txt') -Encoding utf8

Write-Host "All Packages generated in $artifactsDir" -ForegroundColor Green
