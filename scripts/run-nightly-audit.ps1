# run-nightly-audit.ps1 -- the NIGHTLY entry point for the mscorlib icall audit.
#
# WHY THIS FILE EXISTS (W11 P0-5 R3)
# ----------------------------------
# tools/audit/run-audit.ps1 is the full audit chain (reflection channel ->
# metadata channel -> cross-check -> call graph -> wiring probe -> runtime
# symbol index -> wiring surface -> wiring sites -> classification -> render ->
# verify -> five-class gate). It needs the net48 mscorlib reflection channel
# (Windows PowerShell 5.1 + Assembly.LoadFrom) and takes ~10-60s, so it is NOT
# part of the per-commit CI chain. This script is its scheduled entry point.
#
# CI (scripts/ci.ps1) runs the SAME chain inline at step 3.75 into a scratch
# directory, because on this machine it is only ~10s and the evidence gate is
# worth blocking a merge on. This nightly script exists for the case where the
# chain is too slow for CI (a cold machine, a slow disk) or where the audit
# should also PUBLISH the tracked document.
#
# DIFFERENCE FROM CI: this script publishes. run-audit.ps1's render step writes
# docs/audit/icall-mscorlib-current.md (the tracked document) -- that is the
# intended publishing path, and render_report.py refuses to write it unless the
# provenance is a real commit. CI deliberately does NOT publish (it renders to
# scratch and verifies that), so a CI run can never dirty the work tree.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-nightly-audit.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-nightly-audit.ps1 -OutDir F:\tmp\audit
#
# Exit code: 0 = the whole chain passed; non-zero = a stage failed (the stage
# name is printed by run-audit.ps1's Step() banner).
param(
    [string]$OutDir = "",
    [string]$MscorlibPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll',
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if ($OutDir -eq "") { $OutDir = Join-Path $Root ".opencode/tmp/nightly-audit" }

Write-Host "=== nightly audit: full chain (publishes docs/audit/icall-mscorlib-current.md) ==="
Write-Host "  OutDir : $OutDir"
Write-Host "  mscorlib: $MscorlibPath"

$args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Root 'tools/audit/run-audit.ps1'),
          '-OutDir', $OutDir, '-MscorlibPath', $MscorlibPath)
if ($SkipBuild) { $args += '-SkipBuild' }
& powershell @args
if ($LASTEXITCODE -ne 0) {
    Write-Host "nightly audit: FAIL (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# The chain already ran verify_report.py and the five-class gate; re-assert the
# two CI-only evidence checks so a nightly run covers the same surface as CI.
Write-Host "=== nightly audit: declaration symmetry ==="
& python (Join-Path $Root 'tools/audit/check_decl_symmetry.py') $Root
if ($LASTEXITCODE -ne 0) {
    Write-Host "nightly audit: FAIL -- declaration symmetry (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

Write-Host "=== nightly audit: PASS ==="
exit 0
