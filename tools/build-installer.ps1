# Builds the Windows installer: artifacts\MFG-Manager-<version>.msi
#   powershell -File tools\build-installer.ps1
# Publishes the app without debug files (they contain the build computer's folder paths), then packs it with WiX.
# The Linux version has its own script: tools/build-linux.sh

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"

# Stops .NET and Avalonia from reporting anything home during the build.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:AVALONIA_TELEMETRY_OPTOUT = "1"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish "$root\src\MfgManager.App" -c Release -r win-x64 --self-contained false -o $publish -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The drawing libraries come with large debug files of their own (over 100 MB). Nobody running the app needs them.
Get-ChildItem $publish -Filter *.pdb -Recurse | Remove-Item -Force

# Windows only asks for admin rights if this setting is inside the program. Without them, the Mods folder and some game
# folders can't be changed, and nothing else would notice. So stop here instead of packing it.
$exe = Join-Path $publish "MfgManager.exe"
if ([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($exe)) -notmatch 'requestedExecutionLevel\s+level="requireAdministrator"') {
    throw "$exe doesn't ask for admin rights, so it wasn't packed."
}
# The same for the setting that stops an environment variable from loading other code into the app as admin.
if ((Get-Content (Join-Path $publish "MfgManager.runtimeconfig.json") -Raw) -notmatch '"System\.StartupHookProvider\.IsSupported":\s*false') {
    throw "The published app still allows startup hooks, so it wasn't packed."
}

$version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$msiName = "MFG-Manager-$($version -replace '\.0$', '').msi"

dotnet build "$root\installer\MfgManager.Installer.wixproj" -c Release -o $artifacts "-p:PublishDir=$publish"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# installer\Strings.wxl makes WiX put the .msi in a language folder (artifacts\en-US); move it up next to the others.
$cultureFolder = Join-Path $artifacts "en-US"
if (Test-Path $cultureFolder) {
    Get-ChildItem $cultureFolder | Move-Item -Destination $artifacts -Force
    Remove-Item $cultureFolder
}

$msi = Join-Path $artifacts $msiName
if (-not (Test-Path $msi)) { throw "Expected $msi but it isn't there" }
"Built $msi"
