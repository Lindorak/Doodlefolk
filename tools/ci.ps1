<#
.SYNOPSIS
  Doodlefolk's checks, on your own PC: build, unit tests, Studio page tests, and the self-test (the real app,
  sped up and hidden, playing through every feature with its own throwaway data; your cast is never touched).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\ci.ps1          # everything (about 3 minutes)
  powershell -ExecutionPolicy Bypass -File tools\ci.ps1 -Quick   # skip the self-test (under a minute)
#>
param([switch]$Quick)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $env:TEMP "Doodlefolk-ci"
$results = [System.Collections.Generic.List[object]]::new()
$sw = [Diagnostics.Stopwatch]::StartNew()

function Step($name, [scriptblock]$body) {
    Write-Host "`n=== $name ===" -ForegroundColor Cyan
    $t = [Diagnostics.Stopwatch]::StartNew()
    try { & $body; $ok = $true; $why = "" }
    catch { $ok = $false; $why = $_.Exception.Message }
    $results.Add([pscustomobject]@{ Step = $name; Result = $(if ($ok) { "PASS" } else { "FAIL" }); Seconds = [int]$t.Elapsed.TotalSeconds; Why = $why })
    if ($ok) { Write-Host "PASS $name" -ForegroundColor Green } else { Write-Host "FAIL $name : $why" -ForegroundColor Red }
}

Push-Location $root
try {
    Step "Build" {
        dotnet build Doodlefolk.csproj -c Release -nologo -v q -o $out 2>&1 | Tee-Object -Variable log | Out-Null
        if ($LASTEXITCODE -ne 0) { $log | Select-String " error " | Select-Object -First 10 | ForEach-Object { Write-Host $_ }; throw "build failed" }
        $warn = @($log | Select-String "warning CS" | ForEach-Object { $_.ToString() } | Sort-Object -Unique)
        Write-Host "$($warn.Count) compiler warnings"
    }
    Step "Unit tests" {
        dotnet test tests\Doodlefolk.Tests -nologo -v q 2>&1 | Tee-Object -Variable log | Out-Null
        $log | Select-String "Passed!|Failed!|\[FAIL\]" | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }
    }
    Step "Studio page" {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Write-Host "Node.js isn't installed: skipping the page checks."; return }
        foreach ($f in "studio.js") { node --check (Join-Path "Studio\web" $f); if ($LASTEXITCODE -ne 0) { throw "$f has a syntax error" } }
        node tests\studio.test.mjs
        if ($LASTEXITCODE -ne 0) { throw "Studio tests failed" }
    }
    if (-not $Quick) {
        Step "Self-test" {
            $data = Join-Path $env:TEMP "Doodlefolk-selftest-ci"
            $p = Start-Process (Join-Path $out "Doodlefolk.exe") -ArgumentList "--selftest", "--data", "`"$data`"" -PassThru
            if (-not $p.WaitForExit(600000)) { $p.Kill(); throw "the self-test didn't finish in 10 minutes" }
            $report = Join-Path $data "selftest-report.txt"
            # Keep the report and log beside the code (ci-output\, not committed) for a look afterwards.
            $keep = Join-Path $root "ci-output"; New-Item -ItemType Directory -Force $keep | Out-Null
            Copy-Item $report, (Join-Path $data "events.log") $keep -ErrorAction SilentlyContinue
            if (Test-Path $report) { Get-Content $report | ForEach-Object { if ($_ -like "FAIL*") { Write-Host $_ -ForegroundColor Red } else { Write-Host $_ } } }
            if ($p.ExitCode -eq 2) { throw "the self-test's data folder already had other files in it" }
            if ($p.ExitCode -ne 0) { throw "self-test exit code $($p.ExitCode)" }
        }
    }
}
finally { Pop-Location }

Write-Host "`n=== Summary ($([int]$sw.Elapsed.TotalSeconds)s) ===" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host
$failed = @($results | Where-Object Result -eq "FAIL").Count
# Keep a record of every run.
"$(Get-Date -Format s)  $(if ($failed) { "FAILED" } else { "passed" })  $(git -C $root rev-parse --short HEAD 2>$null)  $(($results | ForEach-Object { "$($_.Step)=$($_.Result)" }) -join ' ')" |
    Add-Content (Join-Path $root "tools\ci-history.log")
if ($failed) { Write-Host "$failed STEP(S) FAILED" -ForegroundColor Red; exit 1 }
Write-Host "ALL CHECKS PASSED" -ForegroundColor Green
exit 0
