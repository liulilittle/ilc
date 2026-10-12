param(
    [Parameter(Mandatory)][string]$Case,
    [ValidateSet("mingw","msvc","clang")][string]$Compiler = "mingw",
    [string]$ReportDir = "",
    [int]$TimeoutMs = 120000
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $ReportDir) { $ReportDir = "$Root/.opencode/tmp/diffharness" }
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null

# Differential comparison harness (A3): net48 oracle vs our runtime, **byte-for-byte** compare of stdout/stderr/exit code.
# Reused points (isomorphic with run-tests.ps1): net48 Exe reference run + il2cppc transpile + cmake channel build + il2cpp_gen_exe.
# Key differences (fixing run-tests harness defects):
#   1. All native invocations redirect to log files via cmd /c -- under $ErrorActionPreference='Stop',
#      PS 5.1 turns native stderr (e.g. transpiler warnings) into NativeCommandError terminating exceptions,
#      silently aborting the case at the transpile stage (demonstrated in this pilot). After cmd /c
#      redirection, stderr only lands in the log and the exit code is the sole failure signal.
#   2. Output is captured as **raw bytes** (no UTF-8 text decoding), avoiding decode normalization masking byte-level differences.
#   3. Console output code page pinned to UTF-8(65001): the net48 oracle follows the console CP output, while
#      the runtime is fixed to UTF-8; without pinning, non-ASCII records (e.g. enc.utf8.getstring) produce
#      pure encoding artifacts (same method as run-tests L60-69).

$origOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Invoke-RawRun {
    param([string]$ExePath, [int]$TimeoutMs = 120000)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ExePath
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::new()
    $p.StartInfo = $psi
    $null = $p.Start()
    $p.StandardInput.Close()
    $outMs = New-Object System.IO.MemoryStream
    $errMs = New-Object System.IO.MemoryStream
    $tOut = $p.StandardOutput.BaseStream.CopyToAsync($outMs)
    $tErr = $p.StandardError.BaseStream.CopyToAsync($errMs)
    if (-not $p.WaitForExit($TimeoutMs)) {
        try { $p.Kill($true) } catch { }
        throw "diff run timeout (${TimeoutMs}ms): $ExePath"
    }
    $tOut.Wait(); $tErr.Wait()
    return [pscustomobject]@{ Out = $outMs.ToArray(); Err = $errMs.ToArray(); Code = $p.ExitCode }
}

function Get-Sha256Bytes([byte[]]$b) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($b))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-FirstDiff([byte[]]$a, [byte[]]$b) {
    $n = [Math]::Min($a.Length, $b.Length)
    for ($i = 0; $i -lt $n; $i++) { if ($a[$i] -ne $b[$i]) { return $i } }
    if ($a.Length -ne $b.Length) { return $n }
    return -1
}

function Get-Records([byte[]]$b) {
    $txt = [System.Text.Encoding]::UTF8.GetString($b)
    $map = @{}
    foreach ($line in ($txt -split "`r?`n")) {
        if ($line -match '^\[([^\]]+)\]\s*(.*)$') { $map[$Matches[1]] = $Matches[2] }
    }
    return $map
}

function Resolve-ToolDir {
    param([string[]]$Candidates, [string]$Exe)
    foreach ($c in $Candidates) { if ($c -and (Test-Path (Join-Path $c $Exe))) { return $c } }
    return $null
}
# Toolchain PATH injection: run-tests relies on the caller's environment having cmake/MinGW set up;
# this harness carries its own detection so a single machine (without a CI shell) can reproduce in
# one command, avoiding environment noise like "cmake not in PATH" polluting the diff conclusion.
$cmakeDir = Resolve-ToolDir @('D:\android\sdk\cmake\3.22.1\bin','C:\Program Files\CMake\bin','C:\msys64\mingw64\bin') 'cmake.exe'
$mingwDir = Resolve-ToolDir @('D:\Program Files\mingw\msys2\mingw64\bin','C:\msys64\mingw64\bin','C:\mingw64\bin','D:\mingw64\bin') 'g++.exe'
foreach ($d in @($cmakeDir, $mingwDir)) { if ($d -and ($env:PATH -notlike "*$d*")) { $env:PATH = "$d;$env:PATH" } }

$caseDir  = "$Root/tests/cases/$Case"
$csproj   = "$caseDir/$Case.csproj"
if (-not (Test-Path $csproj)) { throw "missing $csproj" }
# Unique log name per round: a same-named log, when a previous invocation aborted and its
# cmake/cmd child processes still hold the handle, makes the next redirect fail outright
# (cmd cannot open > target -> a non-exit-code error, NativeCommandError).
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$oracleDir = "$caseDir/out_stage_diff"
$logDir    = "$Root/.opencode/logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Remove-Item $oracleDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $oracleDir | Out-Null

$net48Log = "$logDir/${Case}_diff.$stamp.net48.log"
cmd /c "dotnet build `"$csproj`" -c Release -o `"$oracleDir`" > `"$net48Log`" 2>&1"
if ($LASTEXITCODE -ne 0) { throw "net48 build failed (see $net48Log)" }
# P/Invoke cases: build the case-own native libs (mylib/ etc.) into the oracle output dir with the
# SAME x64 flavor as the oracle exe (net48 oracle must load mylib.dll at the same architecture).
# The runtime channel compiles the native lib STATIC into il2cpp_gen_exe (CMake), so only the
# oracle side needs the DLL; without it the net48 oracle fails LoadLibrary/DllNotFoundException.
$nativeLibDirs = @(Get-ChildItem "$caseDir" -Directory -ErrorAction SilentlyContinue |
    Where-Object { (Test-Path (Join-Path $_.FullName 'CMakeLists.txt')) -and $_.Name -notlike 'gen*' -and $_.Name -notlike 'build*' })
foreach ($libDir in $nativeLibDirs) {
    $cmakeTxt = Get-Content (Join-Path $libDir.FullName 'CMakeLists.txt') -Raw -ErrorAction SilentlyContinue
    $libName = ''
    if ($cmakeTxt -and $cmakeTxt -match 'project\(\s*([A-Za-z0-9_]+)') { $libName = $Matches[1] }
    if (-not $libName) { $libName = $libDir.Name }
    $libSrc = Get-ChildItem $libDir.FullName -Filter *.cpp | Select-Object -First 1
    if ($libSrc) {
        $libBuildLog = "$logDir/${Case}_diff.$stamp.$libName.dll.log"
        cmd /c "g++ -shared -o `"$oracleDir/$libName.dll`" `"$($libSrc.FullName)`" > `"$libBuildLog`" 2>&1"
        if ($LASTEXITCODE -ne 0) { throw "build $libName.dll for oracle $Case failed (see $libBuildLog)" }
    }
}
$oracleExe = "$oracleDir/$Case.exe"
if (-not (Test-Path $oracleExe)) { throw "oracle exe missing: $oracleExe" }
# Decide the transpile input BEFORE deploying native libs: a case-owned native dll (mylib/ etc.) must not be mistaken
# for the managed assembly (run-tests L255-259); net48 produces only an exe here.
$transpileInput = if (Test-Path "$oracleDir/$Case.dll") { "$oracleDir/$Case.dll" } else { $oracleExe }

# 2.5 Oracle native-lib deployment: a case-owned native library directory (mylib/ and SHARED libs) is consumed by the
# net48 oracle via LoadLibrary, so its DLL must sit next to the oracle exe (the transpiled runtime links it statically
# and needs nothing extra). Built with g++ -shared (net48 oracle runs on Windows); the oracle exe architecture is pinned
# by the case csproj PlatformTarget so exe and DLL stay aligned. Mirrors run-tests L261-277.
$nativeLibDirs = @(Get-ChildItem $caseDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'CMakeLists.txt') })
foreach ($libDir in $nativeLibDirs) {
    $cmakeTxt = Get-Content (Join-Path $libDir.FullName 'CMakeLists.txt') -Raw -ErrorAction SilentlyContinue
    $libName = ''
    if ($cmakeTxt -and $cmakeTxt -match 'project\(\s*([A-Za-z0-9_]+)') { $libName = $Matches[1] }
    if (-not $libName) { $libName = $libDir.Name }
    $libSrc = Get-ChildItem $libDir.FullName -Filter *.cpp | Select-Object -First 1
    if ($libSrc) {
        $libLog = "$logDir/${Case}_diff.$stamp.libbuild.log"
        cmd /c "g++ -shared -o `"$oracleDir/$libName.dll`" `"$($libSrc.FullName)`" > `"$libLog`" 2>&1"
        if ($LASTEXITCODE -ne 0) { throw "build $libName.dll for oracle failed (see $libLog)" }
    }
}

$exp = Invoke-RawRun $oracleExe $TimeoutMs

$transpileLog = "$logDir/${Case}_diff.$stamp.transpile.log"
$input = $transpileInput
$gen = "$caseDir/gen_diff_$stamp"
Remove-Item $gen -Recurse -Force -ErrorAction SilentlyContinue
$transpilerDll = "$Root/src/il2cppc/bin/Debug/net8.0/il2cppc.dll"
$transpileCmd = if (Test-Path $transpilerDll) { "dotnet exec `"$transpilerDll`"" } else { "dotnet run --project `"$Root/src/il2cppc`" --" }
cmd /c "$transpileCmd `"$input`" `"$gen`" > `"$transpileLog`" 2>&1"
if ($LASTEXITCODE -ne 0) { throw "transpile failed/L3-loud-reject (see $transpileLog)" }

$buildDir = "$caseDir/build_diff_${Compiler}_$stamp"
$buildLog = "$logDir/${Case}_diff.$stamp.build.log"
$cfgRc = 1; $bldRc = 1
for ($attempt = 1; $attempt -le 2; $attempt++) {
    Remove-Item $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    if ($Compiler -eq "clang") {
        $env:PATH = "D:\android\sdk\cmake\3.22.1\bin;D:\Program Files\mingw\msys2\mingw64\bin;" + $env:PATH
        cmd /c "cmake -S `"$gen`" -B `"$buildDir`" -G `"MinGW Makefiles`" -DCMAKE_BUILD_TYPE=Release -DCMAKE_TOOLCHAIN_FILE=`"$Root/cmake/clang-mingw-w64.cmake`" > `"$buildLog`" 2>&1"
    } else {
        cmd /c "cmake -S `"$gen`" -B `"$buildDir`" -G `"MinGW Makefiles`" -DCMAKE_BUILD_TYPE=Release > `"$buildLog`" 2>&1"
    }
    $cfgRc = $LASTEXITCODE
    if ($cfgRc -ne 0) { continue }
    cmd /c "cmake --build `"$buildDir`" --config Release --parallel >> `"$buildLog`" 2>&1"
    $bldRc = $LASTEXITCODE
    if ($bldRc -eq 0) { break }
}
if ($cfgRc -ne 0) { throw "cmake configure failed (see $buildLog)" }
if ($bldRc -ne 0) { throw "cmake build failed (see $buildLog)" }
$rtExe = "$buildDir/il2cpp_gen_exe.exe"
if (-not (Test-Path $rtExe)) { throw "runtime exe missing: $rtExe" }
$act = Invoke-RawRun $rtExe $TimeoutMs

# Keep raw output as evidence: abort/crash conclusions must be traceable to the original stdout/stderr (byte-level).
[System.IO.File]::WriteAllBytes("$ReportDir/$Case.$Compiler.oracle.out.bin", $exp.Out)
[System.IO.File]::WriteAllBytes("$ReportDir/$Case.$Compiler.oracle.err.bin", $exp.Err)
[System.IO.File]::WriteAllBytes("$ReportDir/$Case.$Compiler.runtime.out.bin", $act.Out)
[System.IO.File]::WriteAllBytes("$ReportDir/$Case.$Compiler.runtime.err.bin", $act.Err)

$dOut = Get-FirstDiff $exp.Out $act.Out
$dErr = Get-FirstDiff $exp.Err $act.Err
$expRec = Get-Records $exp.Out
$actRec = Get-Records $act.Out
$diffIds = @()
$allIds = @($expRec.Keys) + @($actRec.Keys) | Sort-Object -Unique
foreach ($id in $allIds) {
    $ev = if ($expRec.ContainsKey($id)) { $expRec[$id] } else { '<missing>' }
    $av = if ($actRec.ContainsKey($id)) { $actRec[$id] } else { '<missing>' }
    if (-not [string]::Equals($ev, $av, [System.StringComparison]::Ordinal)) { $diffIds += [pscustomobject]@{ Id = $id; Expected = $ev; Actual = $av } }
}
$ok = ($dOut -lt 0) -and ($dErr -lt 0) -and ($exp.Code -eq $act.Code) -and ($diffIds.Count -eq 0)

$report = [ordered]@{
    case = $Case; compiler = $Compiler; ok = $ok
    oracle = [ordered]@{ exe = $oracleExe; exitCode = $exp.Code; outBytes = $exp.Out.Length; outSha256 = (Get-Sha256Bytes $exp.Out); errBytes = $exp.Err.Length; errSha256 = (Get-Sha256Bytes $exp.Err) }
    runtime = [ordered]@{ exe = $rtExe; exitCode = $act.Code; outBytes = $act.Out.Length; outSha256 = (Get-Sha256Bytes $act.Out); errBytes = $act.Err.Length; errSha256 = (Get-Sha256Bytes $act.Err) }
    firstDiff = [ordered]@{ stdout = $dOut; stderr = $dErr }
    recordCount = [ordered]@{ oracle = $expRec.Count; runtime = $actRec.Count }
    differingRecords = $diffIds
}
$jsonPath = "$ReportDir/$Case.$Compiler.diffreport.json"
$txtPath  = "$ReportDir/$Case.$Compiler.diffreport.txt"
($report | ConvertTo-Json -Depth 6) | Set-Content -Path $jsonPath -Encoding UTF8
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("case=$Case compiler=$Compiler ok=$ok")
$lines.Add("oracle  code=$($exp.Code) out=$($exp.Out.Length)B sha=$($report.oracle.outSha256)")
$lines.Add("runtime code=$($act.Code) out=$($act.Out.Length)B sha=$($report.runtime.outSha256)")
$lines.Add("firstDiff stdout=$dOut stderr=$dErr records(oracle=$($expRec.Count) runtime=$($actRec.Count)) diff=$($diffIds.Count)")
foreach ($d in $diffIds) { $lines.Add("DIFF [$($d.Id)] expected=$($d.Expected) actual=$($d.Actual)") }
$lines | Set-Content -Path $txtPath -Encoding UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$lines | ForEach-Object { Write-Host $_ }
exit $(if ($ok) { 0 } else { 1 })
