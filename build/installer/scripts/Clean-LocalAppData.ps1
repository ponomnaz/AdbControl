param(
    [switch]$Force
)

$target = Join-Path $env:LOCALAPPDATA "AdbControl"

if (-not (Test-Path -LiteralPath $target)) {
    Write-Host "Local app data folder not found: $target"
    exit 0
}

if (-not $Force) {
    $answer = Read-Host "Delete local application data at '$target'? [y/N]"
    if ($answer -notin @("y", "Y", "yes", "YES", "д", "Д", "да", "ДА")) {
        Write-Host "Cleanup cancelled."
        exit 0
    }
}

Remove-Item -LiteralPath $target -Recurse -Force
Write-Host "Local application data removed: $target"
