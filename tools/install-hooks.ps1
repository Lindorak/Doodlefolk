<#
.SYNOPSIS
  Run StickFight's checks automatically before every "git push" (the quick ones: build, unit tests, Studio page;
  about a minute). A failing check stops the push. Run again with -Full to include the 2-minute self-test, or
  -Remove to take the hook away. To push anyway just once: git push --no-verify
#>
param([switch]$Full, [switch]$Remove)

$root = Split-Path -Parent $PSScriptRoot
$hook = Join-Path $root ".git\hooks\pre-push"
if ($Remove) { Remove-Item $hook -ErrorAction SilentlyContinue; Write-Host "Pre-push checks removed."; exit 0 }
$args = if ($Full) { "" } else { " -Quick" }
@"
#!/bin/sh
# Installed by tools/install-hooks.ps1: run StickFight's checks before pushing.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ci.ps1$args
"@ -replace "`r`n", "`n" | Set-Content -NoNewline -Encoding ascii $hook
Write-Host "Pre-push checks installed ($(if ($Full) { 'full, with the self-test' } else { 'quick' }))."
