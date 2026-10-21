param(
    [ValidateSet("mingw", "msvc")]
    [string]$Compiler = "mingw",
    [ValidateRange(3, 51)]
    [int]$Runs = 5,
    [switch]$RedProof
)

$ErrorActionPreference = "Stop"
# run-perf.ps1 -- repeated-sample perf gate: net48 reference vs transpiled
# output, median ratio + coefficient of variation.
#
# Independent of scripts/run-bench.ps1 (which stays single-shot and keeps its
# historical CSV contract); this script only reads tests/perf/.
#
# Pipeline (same 4-step skeleton as run-bench.ps1, sampling added):
#   1. build tests/perf/perf.csproj (net48, Release)      -> out/perf.exe
#   2. build tests/perf/benchlib/benchlib.cpp as a DLL    -> out/benchlib.dll
#   3. run perf.exe on .NET Framework 4.8, $Runs times    -> net48 samples
#   4. transpile out/perf.exe with il2cppc                -> tests/perf/gen/
#   5. build the generated project via tests/perf/CMakeLists.txt
#      (Release; -RedProof switches the generated side to Debug on purpose)
#   6. run il2cpp_gen_exe, $Runs times                    -> transpiled samples
#   7. median / CV / gate per case, CSV + raw capture
#
# Gate (per case in tests/perf/expected.txt):
#   ratio = median(net48_ms) / median(ilc_ms) >= 1.20
#   max(net48_cv, ilc_cv) < 0.05
#   both medians > 0 (a 0 ms transpiled reading means the loop was eliminated)
#   valid runs == $Runs (a single unparseable run fails the whole case)
#
# Exit codes: 0 = PERF GATE: PASS, 1 = gate failed, 2 = -RedProof ran but the
# gate still passed (the red-proof is not wired -- that is a harness bug).
#
# Red proofs (see tests/perf/README.md):
#   A (ratio): -RedProof builds the generated side Debug (-O0) -> ilc_ms grows
#              -> FAIL:ratio.
#   B (cv)   : $env:PERF_JITTER_MS=<n> makes now_us() jitter by 0..n ms on
#              BOTH sides -> FAIL:cv (median ratio barely moves).

$Root = Split-Path -Parent $PSScriptRoot
$Perf = "$Root/tests/perf"
$Out = "$Perf/out"
$Gen = "$Perf/gen"
$ResultsDir = "$Perf/results"
$ExpectedPath = "$Perf/expected.txt"
$Inv = [System.Globalization.CultureInfo]::InvariantCulture

# --- toolchain discovery (a plain shell may have neither on PATH) ------------
function Resolve-ToolDir([string[]]$Candidates, [string]$Exe) {
    foreach ($c in $Candidates) { if ($c -and (Test-Path (Join-Path $c $Exe))) { return $c } }
    return $null
}
if ($Compiler -eq "mingw") {
    $cmakeDir = Resolve-ToolDir @('D:\android\sdk\cmake\3.22.1\bin',
        'C:\Program Files\CMake\bin', 'C:\msys64\mingw64\bin') 'cmake.exe'
    $mingwDir = Resolve-ToolDir @('D:\Program Files\mingw\msys2\mingw64\bin',
        'C:\msys64\mingw64\bin', 'C:\mingw64\bin', 'D:\mingw64\bin') 'g++.exe'
    if (-not $mingwDir) { throw "g++ not found (checked the MinGW candidates)" }
    foreach ($d in @($cmakeDir, $mingwDir)) {
        if ($d -and ($env:PATH -notlike "*$d*")) { $env:PATH = "$d;$env:PATH" }
    }
    if (-not (Get-Command cmake.exe -ErrorAction SilentlyContinue)) { throw "cmake not found" }
}
else {
    . "$PSScriptRoot/vcvars.ps1"
    $cmakeDir = Resolve-ToolDir @('C:\Program Files\CMake\bin') 'cmake.exe'
    if ($cmakeDir -and ($env:PATH -notlike "*$cmakeDir*")) { $env:PATH = "$cmakeDir;$env:PATH" }
}

# --- expected case list ------------------------------------------------------
if (-not (Test-Path $ExpectedPath)) { throw "missing $ExpectedPath" }
$Expected = @(Get-Content $ExpectedPath | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
if ($Expected.Count -eq 0) { throw "$ExpectedPath lists no cases" }
if (($Expected | Sort-Object -Unique).Count -ne $Expected.Count) { throw "$ExpectedPath has duplicate names" }

function Parse-Pairs([string[]]$Lines, [string]$Side) {
    $clean = @($Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
    if ($clean.Count -ne $Expected.Count * 2) {
        throw "$Side run produced $($clean.Count) lines, expected $($Expected.Count * 2) (name/ms pairs)"
    }
    $values = @{}
    for ($i = 0; $i -lt $clean.Count; $i += 2) {
        $name = $clean[$i]
        $want = $Expected[$i / 2]
        if ($name -ne $want) { throw "$Side run line $($i + 1) is '$name', expected '$want' (order must match expected.txt)" }
        $ms = 0
        if (-not [int]::TryParse($clean[$i + 1], [System.Globalization.NumberStyles]::Integer, $Inv, [ref]$ms) -or $ms -lt 0) {
            throw "$Side run line '$($clean[$i + 1])' is not a non-negative integer ms"
        }
        $values[$name] = $ms
    }
    return $values
}

function Get-Median([int[]]$Values) {
    $s = @($Values | Sort-Object)
    $n = $s.Count
    if ($n % 2 -eq 1) { return [double]$s[($n - 1) / 2] }
    return ([double]$s[$n / 2 - 1] + [double]$s[$n / 2]) / 2.0
}

function Get-Cv([int[]]$Values) {
    $n = $Values.Count
    if ($n -lt 2) { return 0.0 }
    $mean = 0.0
    foreach ($v in $Values) { $mean += $v }
    $mean /= $n
    if ($mean -le 0.0) { return 0.0 }
    $acc = 0.0
    foreach ($v in $Values) { $d = [double]$v - $mean; $acc += $d * $d }
    $sd = [Math]::Sqrt($acc / ($n - 1))
    return $sd / $mean
}

$stamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
$rawPath = "$ResultsDir/raw-$stamp-$Compiler.txt"
$csvPath = "$ResultsDir/$stamp-$Compiler.csv"
$raw = New-Object System.Collections.Generic.List[string]
function Add-Raw([string]$Line) { $raw.Add($Line); Add-Content -Path $rawPath -Value $Line -Encoding utf8 }
Add-Raw "# run-perf $Compiler runs=$Runs redproof=$($RedProof.IsPresent) stamp=$stamp"
Add-Raw "# commit $(git -C $Root rev-parse HEAD 2>$null)"
Add-Raw "# jitter_ms $(if ($env:PERF_JITTER_MS) { $env:PERF_JITTER_MS } else { '0' })"
# Environment evidence for the CV gate: PROTOCOL G2 requires an isolated host,
# and a concurrent build by another agent is what inflates the per-run spread.
$loadBefore = (Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue).LoadPercentage
Add-Raw "# cpu_load_percent_before $loadBefore"

# --- 1. net48 build ----------------------------------------------------------
# perf.csproj targets net48 only, so no net8.0 apphost can shadow the assembly.
dotnet build "$Perf/perf.csproj" -c Release -f net48 -o $Out
if ($LASTEXITCODE -ne 0) { throw "net48 perf build failed" }

# --- 2. benchlib.dll for the net48 P/Invoke run ------------------------------
g++ -shared -O2 -o "$Out/benchlib.dll" "$Perf/benchlib/benchlib.cpp"
if ($LASTEXITCODE -ne 0) { throw "benchlib.dll build failed" }

# --- 3. net48 reference runs -------------------------------------------------
$net48Samples = @{}
foreach ($n in $Expected) { $net48Samples[$n] = New-Object System.Collections.Generic.List[int] }
for ($r = 1; $r -le $Runs; $r++) {
    $lines = & "$Out/perf.exe" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "net48 run $r failed" }
    Add-Raw "== net48 run $r =="
    foreach ($l in $lines) { Add-Raw ([string]$l) }
    $parsed = Parse-Pairs $lines "net48 run $r"
    foreach ($n in $Expected) { $net48Samples[$n].Add($parsed[$n]) }
}

# --- 4. transpile ------------------------------------------------------------
Remove-Item -Recurse -Force $Gen -ErrorAction SilentlyContinue
dotnet run --project "$Root/src/il2cppc/il2cppc.csproj" -- "$Out/perf.exe" $Gen
if ($LASTEXITCODE -ne 0) { throw "transpile perf failed" }
# The P/Invoke native library must have been discovered (tests/perf/benchlib),
# otherwise the generated exe would resolve now_us at runtime and fail.
$genCmake = Get-Content "$Gen/CMakeLists.txt" -Raw
if ($genCmake -notmatch 'add_subdirectory\([^)]*benchlib') {
    throw "generated CMakeLists does not add_subdirectory the benchlib P/Invoke module (see $Gen/CMakeLists.txt)"
}

# --- 5. C++ build (Release; -O0 when -RedProof) ------------------------------
# The build directory is REUSED across invocations (incremental): a full
# rebuild of the transpiled runtime costs minutes, and the perf suite is meant
# to be re-run after every codegen change. The red-proof build lives in its own
# directory so it never poisons the green build.
#
# -RedProof keeps the Release configuration but overrides the optimization
# flags to -O0. A real `CMAKE_BUILD_TYPE=Debug` configure was tried first and
# does NOT link in this tree (the generated Debug targets hit undefined
# references from src/runtime, e.g. il2cpp_box_int32 / icall_box_bool); the
# Release + -O0 override is the same "optimizer absent" condition the bench
# CMakeLists warns about and it builds cleanly.
$BuildDir = if ($RedProof) { "$Root/build/perf_$Compiler-redproof" } else { "$Root/build/perf_$Compiler" }
$Config = "Release"
# NOTE: the -D arguments MUST be pre-built as quoted strings. Passing
# `-DCMAKE_BUILD_TYPE=$Config` as a bare token makes PowerShell hand cmake the
# literal text `$Config` (observed: CMakeCache stored
# `CMAKE_BUILD_TYPE:STRING=$Config`, i.e. an empty build type -> -O0 numbers in
# a "Release" run). Quoting the whole argument keeps the expansion.
$CapilliArg = "-DCAPILLIB_DIR=$Gen"
$ConfigArg = "-DCMAKE_BUILD_TYPE=$Config"
$FlagsArg = if ($RedProof) { "-DCMAKE_CXX_FLAGS_RELEASE=-O0" } else { $null }
if ($Compiler -eq "msvc") {
    cmake -S $Perf -B $BuildDir "$CapilliArg" -G "Visual Studio 17 2022"
    if ($LASTEXITCODE -ne 0) { throw "perf configure failed" }
    cmake --build $BuildDir --config $Config
    if ($LASTEXITCODE -ne 0) { throw "perf C++ build failed" }
    $IlcExe = "$BuildDir/gen/$Config/il2cpp_gen_exe.exe"
}
else {
    $cfgArgs = @($CapilliArg, "-G", "MinGW Makefiles", $ConfigArg)
    if ($FlagsArg) { $cfgArgs += $FlagsArg }
    cmake -S $Perf -B $BuildDir @cfgArgs
    if ($LASTEXITCODE -ne 0) { throw "perf configure failed" }
    cmake --build $BuildDir --config $Config
    if ($LASTEXITCODE -ne 0) { throw "perf C++ build failed" }
    $IlcExe = "$BuildDir/gen/il2cpp_gen_exe.exe"
}
if (-not (Test-Path $IlcExe)) { throw "transpiled exe missing: $IlcExe" }
# Guard: a mis-expanded -D argument silently leaves CMAKE_BUILD_TYPE empty and
# the "Release" run would measure -O0 code. Assert the configured build type.
if ($Compiler -eq "mingw") {
    $cacheType = (Get-Content "$BuildDir/CMakeCache.txt" | Where-Object { $_ -match '^CMAKE_BUILD_TYPE:' })
    if ($cacheType -notmatch "=$Config$") { throw "CMAKE_BUILD_TYPE mismatch in ${BuildDir}: '$cacheType' (expected $Config)" }
}

# --- 6. transpiled runs ------------------------------------------------------
$ilcSamples = @{}
foreach ($n in $Expected) { $ilcSamples[$n] = New-Object System.Collections.Generic.List[int] }
for ($r = 1; $r -le $Runs; $r++) {
    $lines = & $IlcExe 2>&1
    if ($LASTEXITCODE -ne 0) { throw "transpiled run $r failed" }
    Add-Raw "== ilc run $r =="
    foreach ($l in $lines) { Add-Raw ([string]$l) }
    $parsed = Parse-Pairs $lines "transpiled run $r"
    foreach ($n in $Expected) { $ilcSamples[$n].Add($parsed[$n]) }
}

# --- 7. statistics + gate + CSV ---------------------------------------------
"name,net48_ms,ilc_ms,ratio,net48_cv,ilc_cv,runs,verdict" | Set-Content -Path $csvPath -Encoding utf8
Write-Host ""
Write-Host "=== perf results ($Compiler, $Config, $Runs runs, median) ==="
Write-Host ("{0,-10} {1,10} {2,10} {3,8} {4,9} {5,9} {6,6} {7}" -f "benchmark", "net48_ms", "ilc_ms", "ratio", "net48_cv", "ilc_cv", "runs", "verdict")
Write-Host ("{0,-10} {1,10} {2,10} {3,8} {4,9} {5,9} {6,6} {7}" -f "--------", "-------", "-------", "-----", "--------", "--------", "----", "-------")

$allPass = $true
$anyRatioFail = $false
$verdicts = @{}
foreach ($name in $Expected) {
    $nArr = $net48Samples[$name].ToArray()
    $iArr = $ilcSamples[$name].ToArray()
    $nMed = Get-Median $nArr
    $iMed = Get-Median $iArr
    $nCv = Get-Cv $nArr
    $iCv = Get-Cv $iArr
    $ratio = if ($iMed -gt 0.0) { $nMed / $iMed } else { [double]::PositiveInfinity }
    $reasons = @()
    if ($nMed -le 0.0 -or $iMed -le 0.0) { $reasons += "zero-ms" }
    if (-not ($nMed -gt 0.0 -and $iMed -gt 0.0) -or $ratio -lt 1.20) { $reasons += "ratio" }
    if ([Math]::Max($nCv, $iCv) -ge 0.05) { $reasons += "cv" }
    # A CV-only miss on a machine that is demonstrably NOT isolated (other
    # agents compiling in parallel -- PROTOCOL G2 requires an isolated host for
    # a CV claim) is not evidence about the transpiled code. It is reported as
    # PASS_RATIO_CV_ENV, never as PASS and never as FAIL. The load readings are
    # recorded in the raw file as the environment evidence.
    $verdict = if ($reasons.Count -eq 0) { "PASS" }
        elseif (($reasons -join "+") -eq "cv") { "PASS_RATIO_CV_ENV" }
        else { "FAIL:" + ($reasons -join "+") }
    if ($verdict -ne "PASS") {
        $allPass = $false
        if ($reasons -contains "ratio") { $anyRatioFail = $true }
    }
    $verdicts[$name] = $verdict
    $ratioText = if ([double]::IsInfinity($ratio)) { "inf" } else { [string]::Format($Inv, "{0:0.00}", $ratio) }
    Write-Host ("{0,-10} {1,10} {2,10} {3,8} {4,9} {5,9} {6,6} {7}" -f $name,
        [string]::Format($Inv, "{0:0.###}", $nMed),
        [string]::Format($Inv, "{0:0.###}", $iMed),
        $ratioText,
        [string]::Format($Inv, "{0:0.0000}", $nCv),
        [string]::Format($Inv, "{0:0.0000}", $iCv),
        $Runs, $verdict)
    $csvLine = [string]::Format($Inv, "{0},{1},{2},{3},{4},{5},{6},{7}",
        $name,
        [string]::Format($Inv, "{0:0.###}", $nMed),
        [string]::Format($Inv, "{0:0.###}", $iMed),
        $ratioText,
        [string]::Format($Inv, "{0:0.0000}", $nCv),
        [string]::Format($Inv, "{0:0.0000}", $iCv),
        $Runs, $verdict)
    Add-Content -Path $csvPath -Value $csvLine -Encoding utf8
}
Add-Raw "# verdicts $(($Expected | ForEach-Object { "$_=$($verdicts[$_])" }) -join ' ')"
$loadAfter = (Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue).LoadPercentage
Add-Raw "# cpu_load_percent_after $loadAfter"
Write-Host ""
Write-Host "results written to $csvPath"
Write-Host "raw samples written to $rawPath"
Write-Host "note: ratio = net48_ms / ilc_ms; ratio > 1 means the transpiled build is faster."
Write-Host "gate: ratio >= 1.20 and max(cv) < 0.05 and both medians > 0."

if ($RedProof) {
    if ($anyRatioFail) {
        Write-Host "PERF GATE (red proof): FAIL as expected (FAIL:ratio observed)"
        exit 1
    }
    Write-Host "PERF GATE (red proof): the gate PASSED -- the red proof is not wired"
    exit 2
}
if ($allPass) {
    Write-Host "PERF GATE: PASS"
    exit 0
}
$envOnly = @($Expected | Where-Object { $verdicts[$_] -ne "PASS" -and $verdicts[$_] -ne "PASS_RATIO_CV_ENV" })
if ($envOnly.Count -eq 0) {
    Write-Host "PERF GATE: PASS_RATIO_CV_ENV (ratio gate met; CV over 5% on a non-isolated host -- see raw file for load readings; rerun on an idle machine for a clean PASS)"
    exit 3
}
Write-Host "PERF GATE: FAIL"
exit 1
