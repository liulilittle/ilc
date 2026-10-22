# loud-gate.ps1 -- assert that every LOUD-contract case still THROWS the
# documented exception instead of silently returning a wrong value.
#
# Background: scripts/sweep-exclude.txt removes the LOUD cases from the
# byte-exact sweep (a PASS for them is impossible -- they are supposed to fail).
# Without this gate, "excluded" would also mean "unprotected": someone could
# turn a LOUD rejection into a silent wrong value and the sweep would stay green.
#
# What it does: reads sweep-exclude.txt, takes every line whose note contains
# "LOUD contract", runs scripts/difftest.ps1 -Case <name>, and requires BOTH
#   1. difftest exits non-zero (the case must NOT pass), and
#   2. the captured output/log mentions the expected exception evidence:
#      "Unhandled managed exception"  (runtime-time rejection)
#      "not transpiled"               (transpile-time rejection)
#      "culture-aware collation"      (the string-key message)
#      "no cell ABI"                  (the Zip-result message)
#
# Exit code: 0 = every LOUD case threw as documented; 1 = at least one did not
# (or the exclude list / difftest is missing).
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/loud-gate.ps1
param(
    [string]$ExcludeList = "",
    [string]$CaseFilter = "",
    [int]$TimeoutSec = 900
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if ($ExcludeList -eq "") { $ExcludeList = Join-Path $PSScriptRoot "sweep-exclude.txt" }
$diffTest = Join-Path $PSScriptRoot "difftest.ps1"

if (-not (Test-Path -LiteralPath $ExcludeList)) {
    Write-Output "loud-gate: FAIL -- exclude list not found: $ExcludeList"
    exit 1
}
if (-not (Test-Path -LiteralPath $diffTest)) {
    Write-Output "loud-gate: FAIL -- difftest.ps1 not found: $diffTest"
    exit 1
}

$loudCases = New-Object System.Collections.Generic.List[string]
foreach ($raw in [System.IO.File]::ReadAllLines($ExcludeList)) {
    $line = $raw.Trim()
    if ($line.Length -eq 0) { continue }
    if ($line.StartsWith("#")) { continue }
    if ($line -notmatch "LOUD contract") { continue }
    $name = ($line -split "#")[0].Trim()
    if ($name.Length -eq 0) { continue }
    if ($CaseFilter -ne "" -and $name -ne $CaseFilter) { continue }
    $loudCases.Add($name)
}

if ($loudCases.Count -eq 0) {
    Write-Output "loud-gate: FAIL -- no 'LOUD contract' entries found in $ExcludeList"
    exit 1
}

# Evidence that the case rejected the shape instead of returning a value.
$evidence = @(
    "Unhandled managed exception",
    "not transpiled",
    "culture-aware collation",
    "no cell ABI"
)

$failures = New-Object System.Collections.Generic.List[string]
$logDir = Join-Path $Root ".opencode/tmp/loud-gate"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

Write-Output "loud-gate: checking $($loudCases.Count) LOUD contract case(s)"

foreach ($case in $loudCases) {
    $log = Join-Path $logDir "$case.log"
    $rc = 1
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $diffTest -Case $case *> $log
        $rc = $LASTEXITCODE
    }
    catch {
        # difftest throws for a transpile failure; that is the expected path.
        $_ | Out-String | Add-Content -Path $log -Encoding UTF8
        $rc = 1
    }

    $text = ""
    if (Test-Path -LiteralPath $log) { $text = [System.IO.File]::ReadAllText($log) }

    # The harness also persists the raw runtime/transpile evidence; fold it in.
    $evDir = Join-Path $Root ".opencode/tmp/diffharness"
    foreach ($suffix in @("$case.mingw.runtime.err.bin", "$case.mingw.diffreport.txt")) {
        $p = Join-Path $evDir $suffix
        if (Test-Path -LiteralPath $p) {
            $bytes = [System.IO.File]::ReadAllBytes($p)
            $text += [System.Text.Encoding]::UTF8.GetString($bytes)
        }
    }
    $logFiles = Get-ChildItem -Path (Join-Path $Root ".opencode/logs") -Filter "$case*" -ErrorAction SilentlyContinue
    foreach ($lf in $logFiles) {
        try { $text += [System.IO.File]::ReadAllText($lf.FullName) } catch { }
    }

    $found = @()
    foreach ($e in $evidence) { if ($text.Contains($e)) { $found += $e } }

    if ($rc -eq 0) {
        $msg = "$case : FAIL -- difftest PASSED (exit 0); the LOUD contract was lost (silent wrong value?)"
        Write-Output "loud-gate: $msg"
        $failures.Add($msg)
    }
    elseif ($found.Count -eq 0) {
        $msg = "$case : FAIL -- non-zero exit but NO documented exception evidence ($($evidence -join ' | '))"
        Write-Output "loud-gate: $msg"
        $failures.Add($msg)
    }
    else {
        Write-Output "loud-gate: $case : OK -- exit=$rc, evidence: $($found -join ', ')"
    }
}

if ($failures.Count -gt 0) {
    Write-Output "loud-gate: FAIL ($($failures.Count) of $($loudCases.Count))"
    exit 1
}

Write-Output "loud-gate: PASS ($($loudCases.Count) LOUD contract case(s) still reject)"
exit 0
