$ErrorActionPreference = "Stop"
# Resolve the repo root from the script location, avoiding relative-path failures
# when CWD is undefined in -File mode
$Root = Split-Path -Parent $PSScriptRoot

# One-shot full CI:
#   1. Build the transpiler (dotnet build src/il2cppc)
#   2. Build fixtures (dotnet build tests/fixtures, inputs for unit and e2e tests, self-contained on a clean checkout)
#   3. Unit tests (dotnet test tests/unit)
#   4. End-to-end MinGW (6 cases, byte-for-byte diff of net48 reference vs transpiled stdout)
#   5. End-to-end MSVC
#   6. End-to-end WSL (GCC 7.5 baseline)
# Any step fails -> non-zero exit; all pass -> print "=== CI PASS ===".
# Note: run-tests.ps1 uses Split-Path -Parent $PSScriptRoot as its path base,
#       and must be invoked with pwsh 7 (Windows PowerShell 5.1 has no $PSScriptRoot
#       compatibility issue, but we standardize on pwsh).

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
