<#
.SYNOPSIS
  Build Doodlefolk for Steam and (optionally) upload it with steamcmd. See docs\STEAM.md.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\steam-build.ps1 -AppId 1234560 -DepotId 1234561
#>
param(
    [Parameter(Mandatory)][string]$AppId,
    [Parameter(Mandatory)][string]$DepotId,
    [string]$Description = "",
    [switch]$Upload
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$steam = Join-Path $root "steam"
$content = Join-Path $steam "content"
$dll = Join-Path $root "lib\steam\steam_api64.dll"
if (-not (Test-Path $dll)) { throw "lib\steam\steam_api64.dll is missing. Run tools\get-steam-dll.ps1 first." }

Remove-Item $content -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $content | Out-Null
Write-Host "Publishing the Steam build..."
dotnet publish (Join-Path $root "Doodlefolk.csproj") -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o $content -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Remove-Item (Join-Path $content "*.pdb") -ErrorAction SilentlyContinue
if (-not (Test-Path (Join-Path $content "steam_api64.dll"))) { throw "steam_api64.dll didn't make it into the build" }

$version = (Get-Item (Join-Path $content "Doodlefolk.exe")).VersionInfo.ProductVersion -replace '\+.*', ''
if (-not $Description) { $Description = "Doodlefolk $version" }
@"
"AppBuild"
{
    "AppID" "$AppId"
    "Desc" "$Description"
    "ContentRoot" "content"
    "BuildOutput" "output"
    "Depots"
    {
        "$DepotId" "depot_build.vdf"
    }
}
"@ | Set-Content -Encoding ascii (Join-Path $steam "app_build.vdf")
@"
"DepotBuild"
{
    "DepotID" "$DepotId"
    "FileMapping"
    {
        "LocalPath" "*"
        "DepotPath" "."
        "Recursive" "1"
    }
    "FileExclusion" "*.pdb"
}
"@ | Set-Content -Encoding ascii (Join-Path $steam "depot_build.vdf")
Write-Host "Steam build $version is in $content; scripts in $steam."

if ($Upload) {
    $cmd = Get-Command steamcmd -ErrorAction SilentlyContinue
    if (-not $cmd) { throw "steamcmd isn't installed (https://developer.valvesoftware.com/wiki/SteamCMD)." }
    $user = Read-Host "Steamworks account name"
    & $cmd.Source +login $user +run_app_build (Join-Path $steam "app_build.vdf") +quit
    Write-Host "Uploaded. Set the build live on the partner site (SteamPipe → Builds)."
}
