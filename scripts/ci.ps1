$ErrorActionPreference = "Stop"
# Resolve the repo root from the script location, avoiding relative-path failures
# when CWD is undefined in -File mode
$Root = Split-Path -Parent $PSScriptRoot

# One-shot full CI:
#   1. Build the transpiler (dotnet build src/il2cppc)
#   2. Build fixtures (dotnet build tests/fixtures, inputs for unit and e2e tests, self-contained on a clean checkout)
#   3. Unit tests (dotnet test tests/unit)
#   3.5 Runtime C++ tests (test_object / test_gc)
#   3.6 LOUD contract gate (cases that must REJECT)
#   3.7 Declaration symmetry (runtime symbol defined but not declared in its header)
#   3.8 GREEN contract gate (cases that must stay byte-exact)
#   4. End-to-end MinGW (6 cases, byte-for-byte diff of net48 reference vs transpiled stdout)
#   5. End-to-end MSVC
#   6. End-to-end WSL (GCC 7.5 baseline)
# Any step fails -> non-zero exit; all pass -> print "=== CI PASS ===".
# Note: run-tests.ps1 uses Split-Path -Parent $PSScriptRoot as its path base,
#       and must be invoked with pwsh 7 (Windows PowerShell 5.1 has no $PSScriptRoot
#       compatibility issue, but we standardize on pwsh).
# Note: tools/audit/run-audit.ps1 (30-60s, needs the net48 mscorlib reflection
#       channel) is a MANUAL/NIGHTLY step, not part of this chain. Its
#       verify_report.py is the working-tree copy (run-audit resolves $Tools from
#       $Root), so the P0-B evidence assertions run whenever the audit is run.

Write-Host "=== 1. build transpiler ==="
dotnet build "$Root/src/il2cppc/il2cppc.csproj"
if ($LASTEXITCODE -ne 0) { throw "transpiler build failed" }

Write-Host "=== 2. build fixtures ==="
pwsh -NoProfile -File "$Root/scripts/build-fixtures.ps1"
if ($LASTEXITCODE -ne 0) { throw "fixtures build failed" }

Write-Host "=== 3. unit tests ==="
dotnet test "$Root/tests/unit/unit.csproj"
if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }

Write-Host "=== 3.5 runtime C++ tests (test_object / test_gc) ==="
cmake -S "$Root/tests/runtime" -B "$Root/build/tests_runtime" -G "MinGW Makefiles" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "runtime tests cmake configure failed" }
cmake --build "$Root/build/tests_runtime" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "runtime tests cmake build failed" }
& "$Root/build/tests_runtime/test_object.exe"
if ($LASTEXITCODE -ne 0) { throw "test_object failed" }
& "$Root/build/tests_runtime/test_gc.exe"
if ($LASTEXITCODE -ne 0) { throw "test_gc failed" }
Write-Host "runtime C++ tests passed (test_object + test_gc)"

# W11 F1: LOUD contract gate. Placed AFTER the transpiler build (gate ->
# difftest -> il2cppc.dll needs the transpiler) and BEFORE any e2e step.
# Fixed -Compiler mingw: never reuse the fake msvc channel. Because this CI is
# serial (every step throws on failure), a gate failure blocks the downstream
# chain e2e mingw(:42)/e2e msvc(:46)/manifest(:56)/e2e wsl(:60)/capi(:64-68) --
# fail-fast is the intended semantics (a lost contract must stop the pipeline).
# The gate's controlled contract set (scripts/loud-contracts.txt) is always
# non-empty, so exit 1 (contract lost) and exit 2 (INCONCLUSIVE/no active
# contract) both block here.
Write-Host "=== 3.6 LOUD contract gate (mingw) ==="
powershell -NoProfile -ExecutionPolicy Bypass -File "$Root/scripts/loud-gate.ps1" -Compiler mingw
if ($LASTEXITCODE -ne 0) { throw "LOUD contract gate failed (exit $LASTEXITCODE): see .opencode/tmp/loud-gate/*.log" }

# W11 P0-E / P0-5: the evidence verifiers were never in CI. They existed and
# were run by hand only, so a regression in any of them could not block a merge:
#   * check_decl_symmetry.py -- a runtime symbol DEFINED but not DECLARED in its
#     header compiles the runtime fine and only fails when a USER program calls
#     it (P0-D: il2cpp_linq_grouping_first_dec). <1s, so it belongs in CI.
#   * verify_report.py -- the five P0-B evidence assertions (no-site rows,
#     symbol existence, coverage, join-key uniqueness, sig<->metadata, publicapi
#     channel). It needs a classified.json corpus, so CI runs the audit chain
#     into a SCRATCH dir first (3.75) and verifies a SCRATCH render (--doc), so
#     the tracked document is never written.
#   * green-gate.ps1 -- the mirror of loud-gate: cases that must stay byte-exact
#     GREEN (a LOUD anchor and a green anchor fail in opposite directions, so
#     one gate cannot protect both). It also runs verify_report.py itself (R2).
# run-audit.ps1 is the NIGHTLY entry point (30-60s, needs the net48 mscorlib
# reflection channel): scripts/run-nightly-audit.ps1. CI runs the same chain
# inline at 3.75 because it is only ~10s on this machine and the evidence gate
# is worth blocking on.
Write-Host "=== 3.7 declaration symmetry (runtime header vs definition) ==="
& python "$Root/tools/audit/check_decl_symmetry.py" $Root
if ($LASTEXITCODE -ne 0) { throw "declaration symmetry failed (exit $LASTEXITCODE): a runtime symbol is defined but not declared in its header" }

# 3.72: the F4-a 32-slot reference-field limit is a DOCUMENTED LIMITATION, and
# this harness is what keeps the documentation honest. It asserts BOTH
# directions, which is why it cannot be folded into a single-sided check:
#   A1/A2  the boundary is EXACT (31 reference fields transpile, 32 are rejected)
#   A3/A4  base-class fields COUNT toward the slot budget (a derived type with
#          few own fields can still exceed slot 31 through its base)
#   B      a normal CultureInfo program does NOT trip the guard (mscorlib types
#          are never laid out by TypeEmitter; if that regresses, the documented
#          "translated assemblies only" scope is wrong)
# A weakened guard fails A2/A4; an over-applied guard fails A1/A3/B. It needs a
# built transpiler (src/il2cppc/bin/Debug/net8.0/il2cppc.dll) and the net48
# reference assemblies, both of which CI has already produced by this point.
Write-Host "=== 3.72 F4-a 32-slot reference-field limit (docs/w11-f4-bitmap-limit.md) ==="
& python "$Root/tools/audit/check_f4a_limit.py" --repo $Root
if ($LASTEXITCODE -ne 0) { throw "F4-a limit harness failed (exit $LASTEXITCODE): the documented 32-slot reference-field limit no longer matches the transpiler" }

# 3.75: produce the audit corpus in a SCRATCH dir, render to a SCRATCH file, and
# verify the evidence chain. Nothing here writes a tracked file:
#   run-audit.ps1 -OutDir <scratch>   (its render step writes the tracked doc --
#                                      see the note below)
#   render_report.py --out <scratch>  (explicit output, tracked-doc guard bypassed)
#   verify_report.py --doc <scratch>  (verify the scratch render)
# NOTE: run-audit.ps1's own render step DOES write the tracked document (it is
# the publishing path). CI therefore re-renders to scratch and verifies THAT;
# the tracked doc is left untouched by CI.
Write-Host "=== 3.75 audit evidence chain (scratch corpus + scratch render + verify) ==="
$auditDir = Join-Path $Root ".opencode/tmp/ci-audit"
powershell -NoProfile -ExecutionPolicy Bypass -File "$Root/tools/audit/run-audit.ps1" -OutDir $auditDir
if ($LASTEXITCODE -ne 0) { throw "audit chain failed (exit $LASTEXITCODE): see $auditDir" }
$auditDoc = Join-Path $auditDir "render/icall-mscorlib-current.md"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $auditDoc) | Out-Null
$srcRev = (& git -C $Root rev-list -1 HEAD -- src 2>$null | Select-Object -First 1)
if (-not $srcRev) { $srcRev = 'unknown' }
& python "$Root/tools/audit/render_report.py" $auditDir $Root $srcRev --out $auditDoc
if ($LASTEXITCODE -ne 0) { throw "scratch render failed (exit $LASTEXITCODE)" }
& python "$Root/tools/audit/verify_report.py" $auditDir $Root --doc $auditDoc
if ($LASTEXITCODE -ne 0) { throw "audit evidence verification failed (exit $LASTEXITCODE): see $auditDir" }

Write-Host "=== 3.8 GREEN contract gate (mingw) ==="
powershell -NoProfile -ExecutionPolicy Bypass -File "$Root/scripts/green-gate.ps1" -Compiler mingw
if ($LASTEXITCODE -ne 0) { throw "GREEN contract gate failed (exit $LASTEXITCODE): see .opencode/tmp/green-gate/*.log" }

Write-Host "=== 4. e2e mingw ==="
pwsh -NoProfile -File "$Root/scripts/run-tests.ps1" -Compiler mingw
if ($LASTEXITCODE -ne 0) { throw "e2e mingw failed" }

Write-Host "=== 5. e2e msvc ==="
pwsh -NoProfile -File "$Root/scripts/run-tests.ps1" -Compiler msvc
if ($LASTEXITCODE -ne 0) { throw "e2e msvc failed" }

# nb10: the Windows application manifest. Unit tests only inspect the generated
# CMakeLists, which cannot catch a toolchain that overrides the .rc -- the MSVC
# linker synthesizes its own RT_MANIFEST id 1 (CVT1100 + LNK1123, or silently
# winning on the Ninja path, leaving a requireAdministrator build running
# asInvoker). This gate builds BOTH toolchains and reads the resource back,
# asserting its SHA-256 equals the generated app.manifest.
Write-Host "=== 5.5 windows application manifest (mingw + msvc) ==="
pwsh -NoProfile -File "$Root/scripts/test-manifest-e2e.ps1" -Compiler all
if ($LASTEXITCODE -ne 0) { throw "manifest e2e failed" }

Write-Host "=== 6. e2e wsl ==="
pwsh -NoProfile -File "$Root/scripts/run-tests.ps1" -Compiler wsl
if ($LASTEXITCODE -ne 0) { throw "e2e wsl failed" }

Write-Host "=== 7. C API (mingw/msvc/wsl) ==="
pwsh -NoProfile -File "$Root/scripts/run-capi.ps1" -Compiler mingw
if ($LASTEXITCODE -ne 0) { throw "capi mingw failed" }
pwsh -NoProfile -File "$Root/scripts/run-capi.ps1" -Compiler msvc
if ($LASTEXITCODE -ne 0) { throw "capi msvc failed" }
pwsh -NoProfile -File "$Root/scripts/run-capi.ps1" -Compiler wsl
if ($LASTEXITCODE -ne 0) { throw "capi wsl failed" }

Write-Host "=== CI PASS ==="
exit 0
