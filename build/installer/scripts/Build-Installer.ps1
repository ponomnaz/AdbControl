param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [switch]$SelfContained = $true,
    [switch]$OpenMsi,
    [switch]$IncludeBootstrapper
)

$ErrorActionPreference = "Stop"

function Remove-PathIfExists {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force -Recurse
    }
}

function Build-Msi {
    param(
        [string]$InstallerProject,
        [string]$Configuration,
        [string]$Version,
        [string]$PublishDir,
        [string]$OutputPath
    )

    dotnet build $InstallerProject `
      -c $Configuration `
      -p:InstallerPlatform=x64 `
      -p:InstallerVersion=$Version `
      -p:PublishDir="$PublishDir\" `
      -p:OutputPath="$OutputPath\"

    if ($LASTEXITCODE -ne 0) {
        throw "WiX build failed."
    }

    $msi = Get-ChildItem -Path $OutputPath -Filter *.msi -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $msi) {
        throw "MSI file was not produced."
    }

    return $msi
}

function Get-MsiProductCode {
    param([string]$MsiPath)

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($MsiPath, 0)
    $view = $database.OpenView("SELECT `Value` FROM `Property` WHERE `Property`='ProductCode'")
    $view.Execute()
    $record = $view.Fetch()
    if (-not $record) {
        throw "ProductCode was not found in MSI."
    }
    $productCode = $record.StringData(1).Trim()

    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()

    return $productCode
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$appProject = Join-Path $repoRoot "src\AdbControl.App\AdbControl.App.csproj"
$installerProject = Join-Path $repoRoot "build\installer\wix\AdbControl.Installer.wixproj"
$setupHostProject = Join-Path $repoRoot "build\installer\setuphost\AdbControl.SetupHost.csproj"
$uninstallHostProject = Join-Path $repoRoot "build\installer\uninstallhost\AdbControl.UninstallHost.csproj"

$artifactsRoot = Join-Path $repoRoot "artifacts\installer\$Runtime"
$publishDir = Join-Path $repoRoot "artifacts\publish\installer\$Runtime"
$msiOutDir = Join-Path $artifactsRoot "msi"
$setupHostOutDir = Join-Path $artifactsRoot "setuphost"
$bundleOutDir = Join-Path $artifactsRoot "bundle"
$stableSetupExePath = Join-Path $artifactsRoot "AdbControl.Setup.exe"
$legacyStableMsiPath = Join-Path $artifactsRoot "AdbControl.Setup.msi"

Remove-PathIfExists $publishDir
Remove-PathIfExists $msiOutDir
Remove-PathIfExists $setupHostOutDir
Remove-PathIfExists $stableSetupExePath
Remove-PathIfExists $legacyStableMsiPath

if (-not (Test-Path -LiteralPath $artifactsRoot)) {
    New-Item -ItemType Directory -Path $artifactsRoot | Out-Null
}

Write-Host "Publishing app to $publishDir"
dotnet publish $appProject `
  -c $Configuration `
  -r $Runtime `
  --self-contained:$SelfContained `
  -o $publishDir `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugSymbols=false `
  -p:DebugType=None `
  -p:SatelliteResourceLanguages=ru `
  -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Host "Building provisional MSI"
$msi = Build-Msi `
    -InstallerProject $installerProject `
    -Configuration $Configuration `
    -Version $Version `
    -PublishDir $publishDir `
    -OutputPath $msiOutDir

$productCode = Get-MsiProductCode -MsiPath $msi.FullName

Write-Host "Publishing uninstall helper to $publishDir"
dotnet publish $uninstallHostProject `
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
  -p:ProductCode=$productCode
if ($LASTEXITCODE -ne 0) {
    throw "Uninstall helper publish failed."
}

Remove-PathIfExists $msiOutDir
New-Item -ItemType Directory -Path $msiOutDir | Out-Null

Write-Host "Building final MSI"
$msi = Build-Msi `
    -InstallerProject $installerProject `
    -Configuration $Configuration `
    -Version $Version `
    -PublishDir $publishDir `
    -OutputPath $msiOutDir

Write-Host "Building custom setup host"
dotnet publish $setupHostProject `
  -c $Configuration `
  -r $Runtime `
  --self-contained true `
  -o $setupHostOutDir `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugSymbols=false `
  -p:DebugType=None `
  -p:Version=$Version `
  -p:MsiPayloadPath="$($msi.FullName)"
if ($LASTEXITCODE -ne 0) {
    throw "Custom setup host build failed."
}

$setupExe = Get-ChildItem -Path $setupHostOutDir -Filter AdbControl.Setup.exe -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $setupExe) {
    throw "Custom setup host EXE was not produced."
}

Copy-Item -LiteralPath $setupExe.FullName -Destination $stableSetupExePath
$setupExe = Get-Item -LiteralPath $stableSetupExePath

if (-not $IncludeBootstrapper) {
    Remove-PathIfExists $msiOutDir
    Remove-PathIfExists $setupHostOutDir
}
else {
    $bundleProject = Join-Path $repoRoot "build\installer\wix\AdbControl.Bundle.wixproj"

    Write-Host ""
    Write-Host "Building optional bootstrapper EXE"
    dotnet build $bundleProject `
      -c $Configuration `
      -p:BundleVersion=$Version `
      -p:MsiDir="$($msi.Directory.FullName)\\" `
      -p:OutputPath="$bundleOutDir\"
    if ($LASTEXITCODE -ne 0) {
        throw "Bundle build failed."
    }
}

Write-Host ""
Write-Host "Installer ready:"
Write-Host $setupExe.FullName
Write-Host ""
Write-Host "Send only this file to the user."

if ($OpenMsi) {
    Start-Process -FilePath $setupExe.FullName
}
