$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "RevitWallChecker.csproj"
$buildOutput = Join-Path $projectRoot "bin\Release\net8.0-windows"

Write-Host "Building Revit Wall Checker..."
dotnet build $projectFile -c Release

$dll = Join-Path $buildOutput "RevitWallChecker.dll"
if (-not (Test-Path $dll)) {
    throw "Build completed but RevitWallChecker.dll was not found at $dll"
}

Unblock-File -Path $dll -ErrorAction SilentlyContinue
Get-ChildItem $buildOutput -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

$addinDirectory = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2025"
New-Item -ItemType Directory -Force -Path $addinDirectory | Out-Null

$installDirectory = Join-Path $env:APPDATA "RevitWallChecker"
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null

Copy-Item -Path (Join-Path $buildOutput "*") -Destination $installDirectory -Recurse -Force

$installedDll = Join-Path $installDirectory "RevitWallChecker.dll"
$addinFile = Join-Path $addinDirectory "RevitWallChecker.addin"

$addinXml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Revit Wall Checker</Name>
    <Assembly>$installedDll</Assembly>
    <AddInId>6F4A6D2D-7E76-4E75-9F54-1A1F4D1A5A21</AddInId>
    <FullClassName>RevitWallChecker.App</FullClassName>
    <VendorId>RevitWallChecker</VendorId>
    <VendorDescription>Wall-only BIM quality checking tool.</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -Path $addinFile -Value $addinXml -Encoding UTF8

Write-Host ""
Write-Host "Installation completed."
Write-Host "Revit add-in: $addinFile"
Write-Host "Assembly:      $installedDll"
Write-Host ""
Write-Host "Restart Revit 2025 and open the Add-Ins tab."
