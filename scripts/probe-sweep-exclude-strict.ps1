# TDD probe for the sweep-exclude strictness fix (P2-1 / R3).
#
# Exercises the two properties the audit called out:
#   A) an entry in sweep-exclude.txt with NO matching tests/cases/<name>/ directory
#      must be a hard error (today it is silently ignored -- a typo'd case name
#      would silence nothing but also never be noticed);
#   B) a run in which every case was SKIPped must NOT report success
#      (today `exit $(if ($failures -gt 0) {1} else {0})` answers 0 because
#      $failures stays 0 and $passed is never consulted).
#
# A is checked against a temp copy of the list so the repo file is untouched.
# B is checked by inspecting the computed exit expression's inputs, since running
# a real all-SKIP pool would need a whole-pool sweep.
#
# Exit 0 = both properties hold. Exit 1 = at least one is broken (this is the RED
# state before the fix).

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $Root "scripts/sweep-exclude.ps1")

$failures = 0

# ---- A: non-existent entry must be rejected ---------------------------------
$tmpList = Join-Path ([System.IO.Path]::GetTempPath()) ("sweep-exclude-probe-" + [guid]::NewGuid().ToString("N") + ".txt")
try {
    @(
        "# probe list",
        "comslot                       # real case, must be accepted",
        "zz-no-such-case-9f3a1c        # fake case, must be rejected"
    ) | Set-Content -LiteralPath $tmpList -Encoding UTF8

    $missing = @()
    $threw = $false
    try {
        # The validator is what the fix adds; its absence is itself the failure.
        if (-not (Get-Command Assert-SweepExcludeEntriesExist -ErrorAction SilentlyContinue)) {
            Write-Host "A FAIL: Assert-SweepExcludeEntriesExist is not defined"
            $failures++
        } else {
            Assert-SweepExcludeEntriesExist -Path $tmpList -CasesRoot (Join-Path $Root "tests/cases")
            Write-Host "A FAIL: a non-existent case name did not raise"
            $failures++
        }
    } catch {
        $threw = $true
        if ($_.Exception.Message -match 'zz-no-such-case-9f3a1c') {
            Write-Host "A OK: rejected the non-existent entry -> $($_.Exception.Message)"
        } else {
            Write-Host "A FAIL: threw for the wrong reason -> $($_.Exception.Message)"
            $failures++
        }
    }
    if (-not $threw -and (Get-Command Assert-SweepExcludeEntriesExist -ErrorAction SilentlyContinue)) {
        # fall through: already counted above
    }

    # and a list where every entry is real must be accepted
    @(
        "# probe list",
        "comslot                       # real case"
    ) | Set-Content -LiteralPath $tmpList -Encoding UTF8
    if (Get-Command Assert-SweepExcludeEntriesExist -ErrorAction SilentlyContinue) {
        try {
            Assert-SweepExcludeEntriesExist -Path $tmpList -CasesRoot (Join-Path $Root "tests/cases")
            Write-Host "A OK: all-real list accepted"
        } catch {
            Write-Host "A FAIL: all-real list was rejected -> $($_.Exception.Message)"
            $failures++
        }
    }
} finally {
    if (Test-Path -LiteralPath $tmpList) { Remove-Item -LiteralPath $tmpList -Force }
}

# ---- B: an all-SKIP run must not exit 0 -------------------------------------
$runTests = Get-Content -LiteralPath (Join-Path $Root "scripts/run-tests.ps1") -Raw
if ($runTests -match 'skipped\s*-gt\s*0[^\n]*\n?[^\n]*exit') {
    Write-Host "B OK: run-tests.ps1 treats an all-SKIP run as a failure"
} elseif ($runTests -match '\$passed\s*-eq\s*0') {
    Write-Host "B OK: run-tests.ps1 consults the passed count before exiting"
} else {
    Write-Host "B FAIL: run-tests.ps1 exits 0 when every case was skipped"
    $failures++
}

if ($failures -gt 0) {
    Write-Host "SWEEP-EXCLUDE-STRICT: FAIL ($failures)"
    exit 1
}
Write-Host "SWEEP-EXCLUDE-STRICT: PASS"
exit 0
