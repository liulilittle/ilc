param(
    [ValidateSet("mingw", "msvc")]
    [string]$Compiler = "mingw"
)

$ErrorActionPreference = "Stop"
# run-bench.ps1 -- one-click benchmark: net48 reference vs transpiled output.
#
# Pipeline:
#   1. build tests/bench/bench.csproj (net48, Release)        -> out/bench.exe
#   2. build tests/bench/benchlib/benchlib.cpp as a DLL       -> out/benchlib.dll
#      (P/Invoke clock for the net48 run; the transpiled run links the same
#       source as a static lib through the generated project)
#   3. run bench.exe on .NET Framework 4.8                    -> net48 reference
#   4. transpile out/bench.exe with il2cppc                   -> gen/
#   5. build the generated project via tests/bench/CMakeLists.txt
#      (-DCAPILLIB_DIR, Release optimization -- see CMakeLists.txt)
#   6. run il2cpp_gen_exe                                     -> transpiled output
#   7. pair the "name" / "<ms>" lines of both runs, print the comparison
#      table (net48_ms / ilc_ms / ratio) and write
#      tests/bench/results/<date>.csv
#
# Exit code: 0 = both runs completed and parsed; nonzero = any step failed.
$Root = Split-Path -Parent $PSScriptRoot
$Bench = "$Root/tests/bench"
$Out = "$Bench/out"
$Gen = "$Bench/gen"
$ResultsDir = "$Bench/results"
$Expected = @("fib", "loop", "string", "array", "object", "sort", "hash")

# --- 1. net48 build ----------------------------------------------------------
# batch 159: the csproj multi-targets (net48;net8.0 -- batch 152b net8
# baseline), so an unqualified `-o $Out` drops BOTH frameworks into one dir
# and the net8.0 apphost bench.exe shadows the net48 assembly. Build the
# net48 framework explicitly and run the net48 output.
dotnet build "$Bench/bench.csproj" -c Release -f net48 -o $Out
if ($LASTEXITCODE -ne 0) { throw "net48 bench build failed" }

# --- 2. benchlib.dll for the net48 P/Invoke run ------------------------------
g++ -shared -O2 -o "$Out/benchlib.dll" "$Bench/benchlib/benchlib.cpp"
if ($LASTEXITCODE -ne 0) { throw "benchlib.dll build failed" }

# --- 3. net48 reference run --------------------------------------------------
$net48Raw = & "$Out/bench.exe" 2>&1
if ($LASTEXITCODE -ne 0) { throw "net48 reference run failed" }

# --- 4. transpile ------------------------------------------------------------
Remove-Item -Recurse -Force $Gen -ErrorAction SilentlyContinue
dotnet run --project "$Root/src/il2cppc/il2cppc.csproj" -- "$Out/bench.exe" $Gen
if ($LASTEXITCODE -ne 0) { throw "transpile bench failed" }

# --- 5. C++ build (Release) --------------------------------------------------
$BuildDir = "$Root/build/bench_$Compiler"
Remove-Item -Recurse -Force $BuildDir -ErrorAction SilentlyContinue
if ($Compiler -eq "msvc") { . "$PSScriptRoot/vcvars.ps1" }
if ($Compiler -eq "msvc") {
    cmake -S "$Bench" -B $BuildDir -DCAPILLIB_DIR="$Gen" -G "Visual Studio 17 2022"
    if ($LASTEXITCODE -ne 0) { throw "bench configure failed" }
    cmake --build $BuildDir --config Release
    if ($LASTEXITCODE -ne 0) { throw "bench C++ build failed" }
    # the generated project is added as the 'gen' subdirectory, so the
    # transpiled executable lives under build/bench_<c>/gen/Release/.
    $IlcExe = "$BuildDir/gen/Release/il2cpp_gen_exe.exe"
}
else {
    cmake -S "$Bench" -B $BuildDir -DCAPILLIB_DIR="$Gen" -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
    if ($LASTEXITCODE -ne 0) { throw "bench configure failed" }
    cmake --build $BuildDir --config Release
    if ($LASTEXITCODE -ne 0) { throw "bench C++ build failed" }
    # the generated project is added as the 'gen' subdirectory, so the
    # transpiled executable lives under build/bench_<c>/gen/.
    $IlcExe = "$BuildDir/gen/il2cpp_gen_exe.exe"
}

# --- 6. transpiled run -------------------------------------------------------
$ilcRaw = & $IlcExe 2>&1
if ($LASTEXITCODE -ne 0) { throw "transpiled bench run failed" }

# --- 7. parse + compare + CSV ------------------------------------------------
function Parse-Pairs([string[]]$Lines, [string]$Side) {
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
$net48 = Parse-Pairs $net48Raw "net48"
$ilc = Parse-Pairs $ilcRaw "transpiled"

$stamp = Get-Date -Format "yyyy-MM-dd_HHmm"
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
# compiler in the name: same-minute runs for different toolchains must not
# overwrite each other (run-bench.ps1 -Compiler mingw|msvc)
$csvPath = "$ResultsDir/$stamp-$Compiler.csv"
"name,net48_ms,ilc_ms,ratio" | Set-Content -Path $csvPath -Encoding utf8

Write-Host ""
Write-Host "=== bench results ($Compiler, Release) ==="
Write-Host ("{0,-10} {1,10} {2,10} {3,12}" -f "benchmark", "net48_ms", "ilc_ms", "ratio")
Write-Host ("{0,-10} {1,10} {2,10} {3,12}" -f "--------", "-------", "-------", "net48/ilc")
$bad = 0
foreach ($name in $Expected) {
    $n = $net48[$name]
    $i = $ilc[$name]
    $ratio = if ($i -gt 0) { "{0:N2}" -f ($n / $i) } else { "inf" }
    if ($i -eq 0 -and $n -gt 0) { $bad++ }
    Write-Host ("{0,-10} {1,10} {2,10} {3,12}" -f $name, $n, $i, $ratio)
    Add-Content -Path $csvPath -Value ("{0},{1},{2},{3}" -f $name, $n, $i, $ratio) -Encoding utf8
}
Write-Host ""
Write-Host "results written to $csvPath"
Write-Host "note: ratio > 1 means the transpiled build is faster than net48."
if ($bad -gt 0) {
    Write-Host "WARNING: $bad benchmark(s) report 0 ms on the transpiled side (loop eliminated? check the generated code)."
    exit 1
}
exit 0
