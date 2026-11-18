$ErrorActionPreference = 'Continue'
#------------------------------------------------------------------------------
# W11 P0-8 (evidence variant): THREE-STATE acceptance judge with [BASH:]/[READ:]
# evidence tags on every line, so a reviewer can re-run each claim.
#
# Same model as run-cardjudge-probe.ps1:
#   IMPLEMENTED   -- real artifact exists AND the consumption chain is wired
#   PARTIAL       -- one side exists, the other does not
#   NOT-VERIFIED  -- neither side exists
# V1 (filename-table) is DELETED: it keys on the card's DECLARED filename, which
# is not the shipped name, so it reports a false negative on an implemented card.
# M5.5 additionally requires T609 spec + reflect matrix + L0/L1/L2 dual-run
# green; missing any keeps it PARTIAL (the old existence-only judge said PASSED).
#------------------------------------------------------------------------------
$root = 'F:\il2cpp'
$realH    = Join-Path $root 'src/runtime/include/internal/invoker.h'
$realCpp  = Join-Path $root 'src/runtime/src/invoker.cpp'
$realTest = Join-Path $root 'tests/runtime/test_invoker.cpp'
$prog     = Join-Path $root 'src/il2cppc/Program.cs'
$cg       = Join-Path $root 'src/il2cppc/Emit/CodeGenerator.cs'
$cli      = Join-Path $root 'src/il2cppc/CliOptions.cs'
$t609     = Join-Path $root 'docs/architecture/裁剪分级规范.md'
$matrix   = Join-Path $root 'scripts/run-reflect-matrix.ps1'

function T([string]$p) { return (Test-Path -LiteralPath $p) }
function Has([string]$p, [string]$pattern) {
    if (-not (Test-Path -LiteralPath $p)) { return $false }
    return [bool](Select-String -LiteralPath $p -Pattern $pattern -Quiet)
}

$ev = @{
    realH          = T $realH
    realCpp        = T $realCpp
    realTest       = T $realTest
    invokerInclude = Has $realCpp '#include "invoker.h"'
    progConsumes   = Has $prog 'FilterReflectionManifest|ReflectionListProvided'
    cgConsumes     = Has $cg 'FilterReflectionManifest|ReflectionListProvided|\.Kept'
    cliParses      = Has $cli 'reflection-list|reflection-granularity'
    t609spec       = T $t609
    reflectMatrix  = T $matrix
    dualRunGreen   = $false
}
Write-Host "[BASH:Test-Path] realH=$($ev.realH) realCpp=$($ev.realCpp) realTest=$($ev.realTest) t609spec=$($ev.t609spec) reflectMatrix=$($ev.reflectMatrix)"
Write-Host "[READ:src/runtime/src/invoker.cpp] include-chain=$($ev.invokerInclude) | [READ:src/il2cppc/Program.cs] consumes=$($ev.progConsumes) | [READ:src/il2cppc/Emit/CodeGenerator.cs] consumes=$($ev.cgConsumes) | [READ:src/il2cppc/CliOptions.cs] parses=$($ev.cliParses)"
Write-Host "[BASH:dual-run] L0L1L2_dualRunGreen=$($ev.dualRunGreen)"

function Judge-Implementation {
    param([hashtable]$E)
    $artifact = $E.realH -and $E.realCpp -and $E.realTest
    $chain    = $E.invokerInclude -and $E.progConsumes -and $E.cgConsumes -and $E.cliParses
    if ($artifact -and $chain) { return 'IMPLEMENTED' }
    if ($artifact -or $chain)  { return 'PARTIAL' }
    return 'NOT-VERIFIED'
}
function Judge-M55 {
    param([hashtable]$E)
    if ((Judge-Implementation $E) -ne 'IMPLEMENTED') { return 'NOT-VERIFIED' }
    if ($E.t609spec -and $E.reflectMatrix -and $E.dualRunGreen) { return 'IMPLEMENTED' }
    return 'PARTIAL'
}

$t304 = Judge-Implementation $ev
$m55  = Judge-M55 $ev
$missing = @('t609spec','reflectMatrix','dualRunGreen') | Where-Object { -not $ev[$_] }
Write-Host "[VERDICT] T304=$t304"
Write-Host "[VERDICT] M5.5=$m55 (missing verification evidence: $($missing -join ', '))"

# --- mutation (Art.16) ------------------------------------------------------
$orig = $ev.progConsumes
$ev.progConsumes = $false
$mut = Judge-Implementation $ev
Write-Host "[MUT] severed [READ:src/il2cppc/Program.cs] consumption => T304=$mut (expected PARTIAL)"
if ($mut -eq 'PARTIAL') { Write-Host '[MUT] RED OK (judge caught the severed chain)' }
else { Write-Host "[MUT] FAIL: judge stayed $mut = fake judge" }
$ev.progConsumes = $orig
$restored = Judge-Implementation $ev
Write-Host "[MUT] restore => T304=$restored (expected $t304)"
if ($restored -eq $t304) { Write-Host '[MUT] GREEN OK (restore recovers, no git restore)' }
else { Write-Host '[MUT] FAIL: restore did not recover' }

Write-Host '=== END temp/scripts P-probe (three-state) ==='
if ($t304 -eq 'NOT-VERIFIED' -or $m55 -eq 'NOT-VERIFIED') { exit 1 }
exit 0
