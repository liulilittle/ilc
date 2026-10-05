param(
    [ValidateSet("mingw", "msvc")]
    [string]$Compiler = "mingw"
)

$ErrorActionPreference = "Stop"
# run-bench-array.ps1 -- array/string hot-path benchmarks: net48 reference vs
# transpiled output.
#
# Pipeline:
#   1. build tests/bench-array/bench_array.csproj AND bench_array_core.csproj
#      (net48, Release)                                      -> out/*.exe
#   2. build tests/bench-array/benchlib/benchlib.cpp as a DLL -> out/benchlib.dll
#      (P/Invoke clock for the net48 runs; the transpiled run links the same
#       source as a static lib through the generated project)
#   3. run bench_array.exe (FULL program, 6 benchmarks) AND bench_array_core.exe
#      (the transpilable subset: arr_sum / arr_write / str_len) on .NET
#      Framework 4.8 -> net48 reference
#   4. transpile out/bench_array.exe with il2cppc -> gen_array/
#      EXPECTED TO FAIL (documented wiring gaps): String.Concat, get_Chars and
#      Array.Copy each crash the transpiler (Stack empty / callvirt on
#      non-instance). The failure point is captured and reported (NOT fatal).
#   5. transpile out/bench_array_core.exe -> gen_array_core/; build + run it
#      -> transpiled numbers for the core benchmarks
#   6. pair the "name" / "<ms>" lines, print the comparison tables and write
#      tests/bench-array/results/<date>-array-<compiler>.csv plus a failures
#      file with the full-program failure point.
#
# RATIO METHODOLOGY: the ratio for the three core benchmarks uses the CORE
# program's net48 numbers (same binary both sides -- apples to apples). The
# FULL program's net48 numbers form the 6-benchmark reference table (its
# arr_sum measures ~2x slower than the core program's arr_sum for byte-
# identical IL -- a deterministic net48 JIT artifact, see README-array.md).
#
# Exit code: 0 = net48 runs parsed AND the core transpiled pipeline completed
# AND the full program's transpiled pipeline completed (since batch 159 the
# FULL program transpiles/builds/runs -- arr_copy / str_chars / str_concat
# are measured from it); nonzero = any mandatory step failed.
$Root = Split-Path -Parent $PSScriptRoot
$Bench = "$Root/tests/bench-array"
$Out = "$Bench/out"
$GenFull = "$Bench/gen_array"
$GenCore = "$Bench/gen_array_core"
$ResultsDir = "$Bench/results"
$ExpectedFull = @("arr_sum", "arr_write", "arr_copy", "str_chars", "str_len", "str_concat")
$ExpectedCore = @("arr_sum", "arr_write", "str_len")
$Il2Cppc = "$Root/src/il2cppc/il2cppc.csproj"
$Il2CppcDll = "$Root/src/il2cppc/bin/Release/net8.0/il2cppc.dll"

# --- transpiler driver -------------------------------------------------------
# Prefer the source build (dotnet run --project); fall back to the last-known
# good prebuilt dll when the source tree is mid-edit (broken build). Records
# which one was used so the report can attribute the numbers.
$TranspilerUsed = ""
function Invoke-Transpiler([string]$Exe, [string]$GenDir) {
    $srcOut = & dotnet run --project $Il2Cppc -- "$Exe" $GenDir 2>&1
    if ($LASTEXITCODE -eq 0) {
        $script:TranspilerUsed = "source build (dotnet run --project il2cppc)"
        return $srcOut
    }
    if ($srcOut -match "il2cppc error:") {
        # The transpiler BUILT fine but rejected the input -- a documented
        # wiring gap (unwired BCL calls crash the transpiler). Do not fall
        # back: the prebuilt dll would reject the same input identically.
        throw "transpiler rejected $Exe : $($srcOut | Select-String 'il2cppc error' | Select-Object -First 1)"
    }
    if (Test-Path $Il2CppcDll) {
        $dllOut = & dotnet $Il2CppcDll $Exe $GenDir 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:TranspilerUsed = "prebuilt $Il2CppcDll (source build broken: $($srcOut | Select-Object -Last 1))"
            return $dllOut
        }
    }
    throw "transpile failed for $Exe (source build and prebuilt dll both failed)`n$srcOut"
}

# --- in-flight workaround ----------------------------------------------------
# The transpiler now emits il2cpp::il2cpp_string_intern_literal for ldstr, but
# the runtime's aggregate header (il2cpp_runtime.h) does not include
# il2cpp_string_intern.h yet -- the generated code would not compile. Patch
# the GENERATED header (our own gen dir, NOT src/) when needed. This
# workaround is removed automatically once the runtime header lands.
function Patch-GeneratedHeader([string]$GenDir) {
    $genH = "$GenDir/include/internal/generated.h"
    if (-not (Test-Path $genH)) { return }
    $text = Get-Content $genH -Raw
    $usesIntern = @(Get-ChildItem "$GenDir/src" -Filter *.cpp -ErrorAction SilentlyContinue |
        Select-String -Pattern "il2cpp_string_intern_literal").Count -gt 0
    if ($usesIntern -and $text -match 'include "il2cpp_runtime.h"' -and $text -notmatch "il2cpp_string_intern.h") {
        $text = $text.Replace('#include "il2cpp_runtime.h"',
            '#include "il2cpp_runtime.h"' + "`n" + '#include "il2cpp_string_intern.h"')
        Set-Content $genH -Value $text -Encoding utf8
        Write-Host "note: patched $genH (added il2cpp_string_intern.h for the in-flight ldstr-interning work)"
    }
}

# --- 1. net48 builds ---------------------------------------------------------
dotnet build "$Bench/bench_array.csproj" -c Release -o $Out
if ($LASTEXITCODE -ne 0) { throw "net48 bench_array build failed" }
dotnet build "$Bench/bench_array_core.csproj" -c Release -o $Out
if ($LASTEXITCODE -ne 0) { throw "net48 bench_array_core build failed" }

# --- 2. benchlib.dll for the net48 P/Invoke runs -----------------------------
g++ -shared -O2 -o "$Out/benchlib.dll" "$Bench/benchlib/benchlib.cpp"
if ($LASTEXITCODE -ne 0) { throw "benchlib.dll build failed" }

# --- 3. net48 reference runs -------------------------------------------------
$net48FullRaw = & "$Out/bench_array.exe" 2>&1
if ($LASTEXITCODE -ne 0) { throw "net48 bench_array run failed" }
$net48CoreRaw = & "$Out/bench_array_core.exe" 2>&1
if ($LASTEXITCODE -ne 0) { throw "net48 bench_array_core run failed" }

# --- 4. transpile the FULL program ------------------------------------------
Remove-Item -Recurse -Force $GenFull -ErrorAction SilentlyContinue
$fullFailures = @()
try {
    $null = Invoke-Transpiler "$Out/bench_array.exe" $GenFull
    Patch-GeneratedHeader $GenFull
    $BuildFull = "$Root/build/bench_array_$Compiler"
    Remove-Item -Recurse -Force $BuildFull -ErrorAction SilentlyContinue
    if ($Compiler -eq "msvc") { . "$PSScriptRoot/vcvars.ps1" }
    $cfgArgs = @("-S", $Bench, "-B", $BuildFull, "-DCAPILLIB_DIR=$GenFull")
    if ($Compiler -eq "msvc") {
        $cfgArgs += @("-G", "Visual Studio 17 2022")
    } else {
        $cfgArgs += @("-G", "MinGW Makefiles", "-DCMAKE_BUILD_TYPE=Release")
    }
    $fullCfg = & cmake @cfgArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $fullFailures += "CMAKE CONFIGURE FAILED:"
        $fullFailures += @($fullCfg)
    } else {
        $fullBuild = & cmake --build $BuildFull --config Release 2>&1
        if ($LASTEXITCODE -ne 0) {
            $fullFailures += "C++ BUILD FAILED (expected: unwired BCL calls):"
            $fullFailures += @($fullBuild)
        } else {
            if ($Compiler -eq "msvc") {
                $IlcFullExe = "$BuildFull/gen/Release/il2cpp_gen_exe.exe"
            } else {
                $IlcFullExe = "$BuildFull/gen/il2cpp_gen_exe.exe"
            }
            $ilcFullRaw = & $IlcFullExe 2>&1
            if ($LASTEXITCODE -ne 0) {
                $fullFailures += "TRANSPILED FULL RUN FAILED:"
                $fullFailures += @($ilcFullRaw)
            }
        }
    }
} catch {
    $fullFailures += "TRANSPILE FAILED:"
    $fullFailures += @($_)
}

# --- 5. transpile + build + run the CORE program -----------------------------
Remove-Item -Recurse -Force $GenCore -ErrorAction SilentlyContinue
$null = Invoke-Transpiler "$Out/bench_array_core.exe" $GenCore
Patch-GeneratedHeader $GenCore
$BuildCore = "$Root/build/bench_array_core_$Compiler"
Remove-Item -Recurse -Force $BuildCore -ErrorAction SilentlyContinue
if ($Compiler -eq "msvc") { . "$PSScriptRoot/vcvars.ps1" }
$cfgArgs = @("-S", $Bench, "-B", $BuildCore, "-DCAPILLIB_DIR=$GenCore")
if ($Compiler -eq "msvc") {
    $cfgArgs += @("-G", "Visual Studio 17 2022")
} else {
    $cfgArgs += @("-G", "MinGW Makefiles", "-DCMAKE_BUILD_TYPE=Release")
}
cmake @cfgArgs
if ($LASTEXITCODE -ne 0) { throw "bench-array core configure failed" }
cmake --build $BuildCore --config Release
if ($LASTEXITCODE -ne 0) { throw "bench-array core C++ build failed" }
if ($Compiler -eq "msvc") {
    $IlcCoreExe = "$BuildCore/gen/Release/il2cpp_gen_exe.exe"
} else {
    $IlcCoreExe = "$BuildCore/gen/il2cpp_gen_exe.exe"
}
$ilcCoreRaw = & $IlcCoreExe 2>&1
if ($LASTEXITCODE -ne 0) { throw "transpiled core run failed" }

# --- 6. parse + compare + CSV ------------------------------------------------
function Parse-Pairs([string[]]$Lines, [string]$Side, [string[]]$Expected) {
    $clean = @($Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
    if ($clean.Count -ne $Expected.Count * 2) {
        throw "$Side run produced $($clean.Count) lines, expected $($Expected.Count * 2) (name/ms pairs)"
    }
    $map = @{}
    for ($i = 0; $i -lt $clean.Count; $i += 2) {
        $name = $clean[$i]
        if ($name -notin $Expected) { throw "$Side run has unexpected benchmark name '$name'" }
        $ms = 0
        if (-not [int]::TryParse($clean[$i + 1], [ref]$ms) -or $ms -lt 0) {
            throw "$Side run line '$($clean[$i + 1])' is not a non-negative integer ms"
        }
        $map[$name] = $ms
    }
    return $map
}
$net48Full = Parse-Pairs $net48FullRaw "net48 full" $ExpectedFull
$net48Core = Parse-Pairs $net48CoreRaw "net48 core" $ExpectedCore
$ilcCore = Parse-Pairs $ilcCoreRaw "transpiled core" $ExpectedCore
# batch 159: the FULL bench_array program now transpiles+builds+runs on the
# ILC side (the documented wiring gaps -- String.Concat / get_Chars /
# Array.Copy -- were closed by later batches). When the full build
# succeeded, parse its ILC numbers too so arr_copy / str_chars / str_concat
# get real ratios instead of FAIL.
$ilcFull = $null
if ($fullFailures.Count -eq 0 -and $IlcFullExe -and (Test-Path $IlcFullExe)) {
    $ilcFull = Parse-Pairs $ilcFullRaw "transpiled full" $ExpectedFull
}

$stamp = Get-Date -Format "yyyy-MM-dd_HHmm"
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
$csvPath = "$ResultsDir/$stamp-array-$Compiler.csv"
"name,net48_full_ms,net48_core_ms,ilc_ms,ratio,source" | Set-Content -Path $csvPath -Encoding utf8

Write-Host ""
Write-Host "=== bench-array reference (net48, 6 benchmarks, FULL program) ==="
Write-Host ("{0,-10} {1,10}" -f "benchmark", "net48_ms")
foreach ($name in $ExpectedFull) {
    Write-Host ("{0,-10} {1,10}" -f $name, $net48Full[$name])
}
Write-Host ""
Write-Host "=== bench-array ratio table ($Compiler, Release; core program: same binary both sides) ==="
Write-Host ("{0,-10} {1,10} {2,10} {3,12}" -f "benchmark", "net48_ms", "ilc_ms", "ratio")
Write-Host ("{0,-10} {1,10} {2,10} {3,12}" -f "--------", "-------", "-------", "net48/ilc")
$bad = 0
foreach ($name in $ExpectedFull) {
    $nFull = $net48Full[$name]
    if ($name -in $ExpectedCore) {
        $n = $net48Core[$name]
        $i = $ilcCore[$name]
        $src = "core"
        $ratio = if ($i -gt 0) { "{0:N2}" -f ($n / $i) } else { "inf" }
        if ($i -eq 0 -and $n -gt 0) { $bad++ }
        Write-Host ("{0,-10} {1,10} {2,10} {3,12}" -f $name, $n, $i, $ratio)
    } elseif ($ilcFull -ne $null) {
        # batch 159: non-core benchmarks measured from the FULL program
        # (same binary both sides -- net48 full vs ILC full).
        $n = $net48Full[$name]
        $i = $ilcFull[$name]
        $src = "full"
        $ratio = if ($i -gt 0) { "{0:N2}" -f ($n / $i) } else { "inf" }
        if ($i -eq 0 -and $n -gt 0) { $bad++ }
        Write-Host ("{0,-10} {1,10} {2,10} {3,12}" -f $name, $n, $i, $ratio)
    } else {
        $n = ""
        $i = "FAIL"
        $src = "FAIL"
        $ratio = "-"
        Write-Host ("{0,-10} {1,10} {2,10} {3,12}" -f $name, "-", $i, $ratio)
    }
    Add-Content -Path $csvPath -Value ("{0},{1},{2},{3},{4},{5}" -f $name, $nFull, $n, $i, $ratio, $src) -Encoding utf8
}
Write-Host ""
Write-Host "transpiler: $TranspilerUsed"
Write-Host "results written to $csvPath"
Write-Host "note: ratio > 1 means the transpiled build is faster than net48."
if ($fullFailures.Count -gt 0) {
    $failPath = "$ResultsDir/$stamp-array-$Compiler-failures.txt"
    $fullFailures | Set-Content -Path $failPath -Encoding utf8
    Write-Host ""
    Write-Host "WARNING: the FULL bench_array program did not transpile/build/run on the ILC side."
    Write-Host "failure point (also in $failPath):"
    $fullFailures | ForEach-Object {
        if ($_ -match "error|Exception|undefined reference|LNK2019|LNK2001|callvirt|Stack empty") { Write-Host "  $_" }
    }
}
if ($bad -gt 0) {
    Write-Host "WARNING: $bad benchmark(s) report 0 ms on the transpiled side (loop eliminated? check the generated code)."
    exit 1
}
exit 0
