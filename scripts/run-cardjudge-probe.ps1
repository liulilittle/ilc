$ErrorActionPreference = 'Continue'
#------------------------------------------------------------------------------
# W11 P0-8: THREE-STATE acceptance judge (IMPLEMENTED / PARTIAL / NOT-VERIFIED)
#
# WHY THIS REPLACES THE V1/V2/V3 COMPARISON
# -----------------------------------------
# The old probe compared three judges and printed two [RED] lines. That proved
# the BAD judges are bad, but it left the ACCEPTANCE VERDICT ambiguous: a reader
# still had to decide which judge to trust. This script emits ONE verdict per
# card, in three states, from the GOOD judge (real file + consumption chain):
#
#   IMPLEMENTED   -- the real artifact exists AND the consumption chain is wired
#   PARTIAL       -- some evidence exists but the chain is broken (or vice versa)
#   NOT-VERIFIED  -- no real artifact / no chain evidence at all
#
# V1 (filename-table) is DELETED as a judge: it keys on the card's DECLARED
# filename, which is not the shipped name (T304 declares `il2cpp_invoker.h`,
# HEAD ships `invoker.h`), so it reports a false negative on an implemented card.
#
# M5.5 is a VERIFICATION milestone, not an implementation one: it additionally
# requires the T609 clipping spec + run-reflect-matrix.ps1 + L0/L1/L2 dual-run
# green evidence. Missing any of those keeps it PARTIAL even when every
# implementation artifact exists (the old V2 called that "passed").
#
# Exit code: 0 = every card judged (no inconclusive state); 1 = at least one
# card is NOT-VERIFIED or the evidence is self-contradictory.
#------------------------------------------------------------------------------
$root = 'F:\il2cpp'

# --- evidence inputs (real current-HEAD files) -------------------------------
$realInvokerH    = Join-Path $root 'src/runtime/include/internal/invoker.h'
$realInvokerCpp  = Join-Path $root 'src/runtime/src/invoker.cpp'
$realInvokerTest = Join-Path $root 'tests/runtime/test_invoker.cpp'
$prog            = Join-Path $root 'src/il2cppc/Program.cs'
$cg              = Join-Path $root 'src/il2cppc/Emit/CodeGenerator.cs'
$cli             = Join-Path $root 'src/il2cppc/CliOptions.cs'
$t609spec        = Join-Path $root 'docs/architecture/裁剪分级规范.md'
$matrix          = Join-Path $root 'scripts/run-reflect-matrix.ps1'

function T([string]$p) { return (Test-Path -LiteralPath $p) }
function Has([string]$p, [string]$pattern) {
    if (-not (Test-Path -LiteralPath $p)) { return $false }
    return [bool](Select-String -LiteralPath $p -Pattern $pattern -Quiet)
}

$ev = [ordered]@{
    realInvokerH    = T $realInvokerH
    realInvokerCpp  = T $realInvokerCpp
    realInvokerTest = T $realInvokerTest
    invokerInclude  = Has $realInvokerCpp '#include "invoker.h"'
    progConsumes    = Has $prog 'FilterReflectionManifest|ReflectionListProvided'
    cgConsumes      = Has $cg 'FilterReflectionManifest|ReflectionListProvided|\.Kept'
    cliParses       = Has $cli 'reflection-list|reflection-granularity'
    t609spec        = T $t609spec
    reflectMatrix   = T $matrix
    dualRunGreen    = $false   # no L0/L1/L2 dual-run green evidence with mtime > HEAD ci
}
Write-Host "[EVIDENCE] $((($ev.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '))"

# --- three-state judge ------------------------------------------------------
function Judge-Implementation {
    param([hashtable]$E)
    $artifact = $E.realInvokerH -and $E.realInvokerCpp -and $E.realInvokerTest
    $chain    = $E.invokerInclude -and $E.progConsumes -and $E.cgConsumes -and $E.cliParses
    if ($artifact -and $chain) { return 'IMPLEMENTED' }
    if ($artifact -or $chain)  { return 'PARTIAL' }
    return 'NOT-VERIFIED'
}

function Judge-M55 {
    param([hashtable]$E)
    $impl = Judge-Implementation $E
    if ($impl -ne 'IMPLEMENTED') { return 'NOT-VERIFIED' }
    $verification = $E.t609spec -and $E.reflectMatrix -and $E.dualRunGreen
    if ($verification) { return 'IMPLEMENTED' }
    # Implementation is complete but the VERIFICATION milestone is not: that is
    # exactly PARTIAL (the old V2 called it "passed").
    return 'PARTIAL'
}

$t304 = Judge-Implementation $ev
$m55  = Judge-M55 $ev
Write-Host "[VERDICT] T304=$t304"
$missing = @('t609spec','reflectMatrix','dualRunGreen') | Where-Object { -not $ev[$_] }
Write-Host "[VERDICT] M5.5=$m55 (requires T609 spec + reflect matrix + L0/L1/L2 dual-run green; missing: $($missing -join ', '))"

# --- mutation (Art.16): sever the consumption chain -> T304 must drop -------
$origProg = $ev.progConsumes
$ev.progConsumes = $false
$mut = Judge-Implementation $ev
Write-Host "[MUT] severed Program.cs consumption -> T304=$mut (expected PARTIAL, was $t304)"
if ($mut -eq 'PARTIAL') { Write-Host "[MUT] RED OK (the judge caught the severed chain)" }
else { Write-Host "[MUT] FAIL: the judge stayed $mut after breaking a key predicate = fake judge" }
$ev.progConsumes = $origProg
$restored = Judge-Implementation $ev
Write-Host "[MUT] restore -> T304=$restored (expected $t304)"
if ($restored -eq $t304) { Write-Host "[MUT] GREEN OK (restore recovers)" }
else { Write-Host "[MUT] FAIL: restore did not recover" }

Write-Host "=== END W11 P0-8 three-state judge ==="
if ($t304 -eq 'NOT-VERIFIED' -or $m55 -eq 'NOT-VERIFIED') { exit 1 }
exit 0
