# green-gate.ps1 -- assert that every GREEN-contract case still passes byte-exact.
#
# Mirror image of scripts/loud-gate.ps1:
#   loud-gate.ps1  : every case MUST exit non-zero (a documented rejection)
#   green-gate.ps1 : every case MUST exit zero with ok=True (byte-exact)
#
# Why a separate gate instead of a `-Expect pass` mode on loud-gate:
#   * the two gates read DIFFERENT files (loud-contracts.txt / green-contracts.txt),
#     so a row can never be filed in the wrong list and silently invert its own
#     assertion;
#   * the failure messages are opposite ("the LOUD contract was lost" vs "the
#     GREEN anchor regressed"), and a shared mode would have to branch on every
#     message;
#   * loud-gate.ps1 is already wired into ci.ps1 and covered by its own contract;
#     adding a mode would put that working gate at risk for no gain.
#
# What it does: reads scripts/green-contracts.txt, runs scripts/difftest.ps1
# -Case <name>, and requires
#   1. difftest exits 0, AND
#   2. the report JSON says ok=true (a SKIP is NOT a pass: a green anchor that
#      stopped being executed is a lost anchor, not a green one).
#
# Exit code: 0 = every green case passed; 1 = at least one did not (or the
# contract list / difftest is missing); 2 = INCONCLUSIVE (no active green
# contract cases -- the controlled set must be non-empty).
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/green-gate.ps1
#   ci.ps1 invokes it with -Compiler mingw (never the fake msvc channel).
#
# W11 P0-5 R2: this is an EVIDENCE gate, not only a contract gate. Before the
# per-case loop it runs the declaration-symmetry check (a runtime symbol defined
# but not declared in any header is invisible to the byte-exact diff -- the
# P0-D shape). A green anchor can be byte-exact while the runtime surface is
# broken, so the two checks are independent and both must pass.
param(
    [string]$ContractList = "",
    [string]$CaseFilter = "",
    [string]$Compiler = "mingw",
    [string]$ReportDir = "",
    [int]$TimeoutSec = 900,
    [switch]$SkipEvidence
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if ($ContractList -eq "") { $ContractList = Join-Path $PSScriptRoot "green-contracts.txt" }
if ($ReportDir -eq "") { $ReportDir = Join-Path $Root ".opencode/tmp/green-gate" }
$diffTest = Join-Path $PSScriptRoot "difftest.ps1"

if (-not (Test-Path -LiteralPath $ContractList)) {
    Write-Output "green-gate: FAIL -- contract list not found: $ContractList"
    exit 1
}
if (-not (Test-Path -LiteralPath $diffTest)) {
    Write-Output "green-gate: FAIL -- difftest.ps1 not found: $diffTest"
    exit 1
}
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null

# --- R2: evidence check (declaration symmetry) -------------------------------
# Runs BEFORE the contract loop so a broken runtime surface blocks even when
# every green anchor happens to pass. `-SkipEvidence` exists for targeted
# debugging of the contract loop only; ci.ps1 never passes it.
if (-not $SkipEvidence) {
    $sym = Join-Path $Root "tools/audit/check_decl_symmetry.py"
    if (-not (Test-Path -LiteralPath $sym)) {
        Write-Output "green-gate: FAIL -- evidence tool not found: $sym"
        exit 1
    }
    Write-Output "green-gate: evidence -- declaration symmetry"
    & python $sym $Root
    $symRc = $LASTEXITCODE
    if ($symRc -ne 0) {
        Write-Output "green-gate: FAIL -- declaration symmetry (exit $symRc): a runtime symbol is defined but not declared in any header"
        exit 1
    }
    Write-Output "green-gate: evidence OK -- declaration symmetry"
}

# Parse exactly like loud-gate.ps1 / sweep-exclude.ps1: Trim, drop empty and
# '^#' lines, take the first token of -split '\s+'.
$greenCases = New-Object System.Collections.Generic.List[string]
$seen = New-Object System.Collections.Generic.HashSet[string]
foreach ($raw in [System.IO.File]::ReadAllLines($ContractList)) {
    $line = $raw.Trim()
    if ($line.Length -eq 0) { continue }
    if ($line.StartsWith("#")) { continue }
    $name = ($line -split "#")[0].Trim()
    if ($name.Length -eq 0) { continue }
    if ($CaseFilter -ne "" -and $name -ne $CaseFilter) { continue }
    if ($seen.Add($name)) { $greenCases.Add($name) }
}

if ($greenCases.Count -eq 0) {
    # An EMPTY active set is INCONCLUSIVE, never a vacuous PASS (same rule as
    # loud-gate.ps1:67-79): a lost contract file must block, not silently pass.
    Write-Output "green-gate: INCONCLUSIVE -- no active GREEN contract cases in '$ContractList'. A non-empty controlled set is required for this gate to protect anything."
    exit 2
}

$failures = New-Object System.Collections.Generic.List[string]
$logDir = Join-Path $Root ".opencode/tmp/green-gate"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

Write-Output "green-gate: checking $($greenCases.Count) GREEN contract case(s)"

foreach ($case in $greenCases) {
    $log = Join-Path $logDir "$case.log"
    $rc = 1
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $diffTest -Case $case -Compiler $Compiler -ReportDir $ReportDir *> $log
        $rc = $LASTEXITCODE
    }
    catch {
        $_ | Out-String | Add-Content -Path $log -Encoding UTF8
        $rc = 1
    }

    # The JSON is the authority: `ok` must be the BOOLEAN true. A SKIP report
    # carries the STRING "SKIP", which is not a pass (the anchor did not run).
    $json = Join-Path $ReportDir "$case.$Compiler.diffreport.json"
    $okValue = $null
    $produced = $null
    if (Test-Path -LiteralPath $json) {
        try {
            $j = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
            $okValue = $j.ok
            $produced = $j.producedBytes
        } catch {
            $okValue = $null
        }
    }

    if ($rc -ne 0) {
        $msg = "$case : FAIL -- difftest exited $rc (the GREEN anchor regressed; see $log)"
        Write-Output "green-gate: $msg"
        $failures.Add($msg)
    }
    elseif ($okValue -ne $true) {
        $msg = "$case : FAIL -- difftest exited 0 but the report ok=$okValue (expected boolean true; a SKIP is not a pass)"
        Write-Output "green-gate: $msg"
        $failures.Add($msg)
    }
    else {
        Write-Output "green-gate: $case : OK -- ok=True, producedBytes=$produced"
    }
}

if ($failures.Count -gt 0) {
    Write-Output "green-gate: FAIL ($($failures.Count) of $($greenCases.Count))"
    exit 1
}

Write-Output "green-gate: PASS ($($greenCases.Count) GREEN contract case(s) still byte-exact)"
exit 0
