param(
    [Parameter(Mandatory = $true)][string]$Path,
    [switch]$Increment
)

# Reads the stored marketing version (Major.Minor.Patch) and echoes it on stdout
# so MSBuild can apply it to assembly metadata.
#
#   -Increment   bump the patch component, write it back, echo the new value.
#                Used once per real application build (build/AutoVersion.targets).
#   (omitted)    echo the current value without touching the file. Used by the
#                launcher so its stamp matches the payload it embeds instead of
#                inflating the shared version counter.

$ErrorActionPreference = 'Stop'

$fallback = '0.3.0'
$current = if (Test-Path -LiteralPath $Path) { (Get-Content -Raw -LiteralPath $Path).Trim() } else { $fallback }
if (-not $current) { $current = $fallback }

$parts = @($current.Split('.'))
while ($parts.Count -lt 3) { $parts += '0' }

$major = 0; $minor = 0; $patch = 0
if (-not [int]::TryParse($parts[0], [ref]$major)) { $major = 0 }
if (-not [int]::TryParse($parts[1], [ref]$minor)) { $minor = 0 }
if (-not [int]::TryParse($parts[2], [ref]$patch)) { $patch = 0 }

$resolved = "$major.$minor.$patch"

if ($Increment) {
    $resolved = "$major.$minor.$($patch + 1)"
    $directory = Split-Path -Path $Path -Parent
    if ($directory -and -not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $resolved)
}

Write-Output $resolved
