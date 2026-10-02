<#
.SYNOPSIS
  Fetch Valve's steam_api64.dll (from the matching Steamworks.NET release on GitHub) into lib\steam. It's needed for
  the Steam build and for testing Steam features; the plain GitHub build runs without it. Checks Valve's signature.
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tag = "2025.164.1"
$tmp = Join-Path $env:TEMP "steamworks-dl"
New-Item -ItemType Directory -Force $tmp | Out-Null
$zip = Join-Path $tmp "Steamworks.NET-Standalone_$tag.zip"
Invoke-WebRequest "https://github.com/rlabrecque/Steamworks.NET/releases/download/$tag/Steamworks.NET-Standalone_$tag.zip" -OutFile $zip
Expand-Archive $zip -DestinationPath (Join-Path $tmp "x") -Force
$dll = Join-Path $tmp "x\Windows-x64\steam_api64.dll"
$sig = Get-AuthenticodeSignature $dll
if ($sig.Status -ne "Valid" -or $sig.SignerCertificate.Subject -notlike "CN=Valve Corp.*") { throw "steam_api64.dll isn't signed by Valve; not using it." }
Copy-Item $dll (Join-Path $root "lib\steam\steam_api64.dll") -Force
Write-Host "steam_api64.dll is in lib\steam (signed by Valve)."
