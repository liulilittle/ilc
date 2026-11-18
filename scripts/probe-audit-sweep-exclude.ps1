<#
.SYNOPSIS
    Self-test for audit-sweep-exclude.ps1: proves the audit's two fail conditions
    actually fire. A static auditor that never reports anything is worse than none,
    because it looks like assurance.

.DESCRIPTION
    Runs the audit against three synthetic roots:
      A. an entry whose name has no tests/cases/ directory  -> must exit 1
      B. an entry claiming a LOUD contract that loud-contracts.txt omits -> must exit 1
      C. a well-formed list (RED-anchor + registered LOUD) -> must exit 0

    Each root is a throwaway tree with only the files the audit reads.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/probe-audit-sweep-exclude.ps1
#>
param([string]$Root = "")

$ErrorActionPreference = "Stop"
if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }
$audit = Join-Path $PSScriptRoot "audit-sweep-exclude.ps1"
if (-not (Test-Path $audit)) { throw "missing $audit" }

$base = Join-Path $Root ".opencode/tmp/sweepaudit-selftest"
if (Test-Path $base) { Remove-Item -Recurse -Force $base }
New-Item -ItemType Directory -Force -Path $base | Out-Null

function New-Root([string]$name, [string]$excludeBody, [string]$loudBody, [string[]]$caseDirs) {
    $r = Join-Path $base $name
    New-Item -ItemType Directory -Force -Path (Join-Path $r "scripts") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $r "tests/cases") | Out-Null
    Set-Content -LiteralPath (Join-Path $r "scripts/sweep-exclude.txt") -Value $excludeBody -Encoding UTF8
    if ($null -ne $loudBody) {
        Set-Content -LiteralPath (Join-Path $r "scripts/loud-contracts.txt") -Value $loudBody -Encoding UTF8
    }
    foreach ($d in $caseDirs) {
        New-Item -ItemType Directory -Force -Path (Join-Path $r "tests/cases/$d") | Out-Null
    }
    return $r
}

$fails = @()

# A: name with no case directory -> MISSING -> exit 1
$rA = New-Root "a-missing" @"
# header
ghost-case   # nothing here
"@ $null @()
& powershell -NoProfile -ExecutionPolicy Bypass -File $audit -Root $rA -Quiet | Out-Null
if ($LASTEXITCODE -ne 1) { $fails += "A: expected exit 1 (missing case directory), got $LASTEXITCODE" }
else { "A OK: a name with no case directory is rejected" }

# B: LOUD claim not registered in loud-contracts.txt -> exit 1
$rB = New-Root "b-loud-unregistered" @"
real-case   # LOUD contract: claims a rejection nobody verifies
"@ @"
# empty
"@ @("real-case")
& powershell -NoProfile -ExecutionPolicy Bypass -File $audit -Root $rB -Quiet | Out-Null
if ($LASTEXITCODE -ne 1) { $fails += "B: expected exit 1 (LOUD claim not registered), got $LASTEXITCODE" }
else { "B OK: an unregistered LOUD claim is rejected" }

# C: well-formed -> exit 0
$rC = New-Root "c-healthy" @"
red-case    # W11 SCOPE GAP red anchor: intentional
loud-case   # LOUD contract: must reject
"@ @"
loud-case
"@ @("red-case", "loud-case")
& powershell -NoProfile -ExecutionPolicy Bypass -File $audit -Root $rC -Quiet | Out-Null
if ($LASTEXITCODE -ne 0) { $fails += "C: expected exit 0 (well-formed list), got $LASTEXITCODE" }
else { "C OK: a well-formed list is accepted" }

Remove-Item -Recurse -Force $base -ErrorAction SilentlyContinue

if ($fails.Count -gt 0) {
    ""
    "AUDIT-SWEEP-EXCLUDE SELFTEST: FAIL"
    foreach ($f in $fails) { "  - $f" }
    exit 1
}
"AUDIT-SWEEP-EXCLUDE SELFTEST: PASS"
exit 0
