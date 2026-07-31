param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [switch]$Run
)

$ErrorActionPreference = "Stop"

function Remove-PathIfExists {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force -Recurse
    }
}

function Find-Iscc {
    $paths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($p in $paths) {
        if (Test-Path -LiteralPath $p) {
            return $p
        }
    }

    # fallback — через registry (самый надёжный способ)
    $reg = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*" `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like "*Inno Setup*" } |
        Select-Object -First 1

    if ($reg.InstallLocation) {
        $exe = Join-Path $reg.InstallLocation "ISCC.exe"
        if (Test-Path $exe) { return $exe }
    }

    throw "ISCC.exe not found"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$appProject = Join-Path $repoRoot "src\AdbControl.App\AdbControl.App.csproj"
$issFile = Join-Path $repoRoot "build\installer\inno\AdbControl.iss"

$publishDir = Join-Path $repoRoot "artifacts\publish\installer\$Runtime"
$installerOutDir = Join-Path $repoRoot "artifacts\installer\$Runtime"

Remove-PathIfExists $publishDir
Remove-PathIfExists $installerOutDir

Write-Host "Publishing app to $publishDir"
dotnet publish $appProject `
  -c $Configuration `
  -r $Runtime `
  --self-contained true `
  -o $publishDir `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugSymbols=false `
  -p:DebugType=None `
  -p:Version=$Version `
  -p:SatelliteResourceLanguages=ru `
  -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$iscc = Find-Iscc

Write-Host "Building installer with Inno Setup"
& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" $issFile
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup build failed."
}

$setupExe = Get-ChildItem -Path $installerOutDir -Filter "AdbControl-Setup.exe" -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $setupExe) {
    throw "Installer EXE was not produced."
}

Write-Host ""
Write-Host "Installer ready:"
Write-Host $setupExe.FullName
Write-Host ""
Write-Host "Send only this file to the user."

if ($Run) {
    Start-Process -FilePath $setupExe.FullName
}
