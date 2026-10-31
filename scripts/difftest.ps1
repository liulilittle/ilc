param(
    [Parameter(Mandatory)][string]$Case,
    [ValidateSet("mingw","msvc","clang")][string]$Compiler = "mingw",
    [string]$ReportDir = "",
    [int]$TimeoutMs = 120000,
    [string]$AppArgs = ""
)
# AppArgs: optional process command-line arguments passed IDENTICALLY to the
# net48 oracle exe and the il2cpp_gen_exe runtime exe (e.g. a
# GetCommandLineArgs probe - p-nb-env -ArgA argB -x=42). Default empty keeps
# the existing no-args behavior for every prior caller.
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
    param([string]$ExePath, [int]$TimeoutMs = 120000, [string]$AppArgs = "")
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ExePath
    if ($AppArgs) { $psi.Arguments = $AppArgs }
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

# --- CONCURRENCY-FIX (2026-10-30): same-case parallel runs used to corrupt each other ---
# Proven root cause (two independent seats hit it; reproduced 1/4 green with 4 concurrent
# `-Case probe-aggrs`): the harness had three per-run resources under SHARED names --
#   (1) <case>/out_stage_diff (shared staging, deleted+recreated per run),
#   (2) <case>/gen_diff_<stamp> and <case>/build_diff_<compiler>_<stamp> (stamp = SECOND
#       resolution, so two runs starting in the same second share one directory), and
#   (3) .opencode/logs/<case>_diff.<stamp>.*.log (same second -> cmd /c cannot open the
#       shared `>` redirect target: "The process cannot access the file because it is being
#       used by another process", which is the failure the seats reported as a harness crash).
# Fix = two independent layers, so correctness does not depend on either one alone:
#   (a) every per-run path carries a unique run tag (timestamp + PID + GUID prefix) and is
#       removed in a finally block (the pre-fix harness also leaked gen_diff_*/build_diff_*
#       directories forever);
#   (b) a per-case cross-process named Mutex serializes same-case runs. It is REQUIRED even
#       with unique staging because `dotnet build` writes the case's shared `obj/`
#       intermediate directory, and because cases that do relative file IO use the caller's
#       CWD (difftest deliberately does NOT chdir: p-nb2-dir prints Directory.GetCurrentDirectory,
#       so a per-run CWD would make its output non-deterministic). The mutex name is the same
#       one run-tests.ps1 uses, so difftest and run-tests also exclude each other on a case.
# Timeout semantics mirror run-tests.ps1: a live holder that outlasts the cap logs a warning
# and the run proceeds unlocked (never a silent deadlock); the cap is env-tunable.
function Enter-CaseLock {
    param([string]$CaseName, [int]$TimeoutMs = 0)
    if ($TimeoutMs -le 0) {
        $TimeoutMs = 900000
        if ($env:IL2CPP_DIFFTEST_LOCK_MS -match '^\d+$' -and [int]$env:IL2CPP_DIFFTEST_LOCK_MS -gt 0) {
            $TimeoutMs = [int]$env:IL2CPP_DIFFTEST_LOCK_MS
        }
    }
    foreach ($ns in @('Global', 'Local')) {
        $m = $null
        try {
            $m = [System.Threading.Mutex]::new($false, "$ns\il2cpp_runtests_$CaseName")
            $acquired = $false
            try { $acquired = $m.WaitOne($TimeoutMs) }
            catch [System.Threading.AbandonedMutexException] { $acquired = $true }
            if (-not $acquired) {
                Write-Warning "difftest case lock timeout after ${TimeoutMs}ms for $CaseName ($ns): proceeding WITHOUT the lock"
                try { $m.Dispose() } catch { }
                return [pscustomobject]@{ Mutex = $null; Acquired = $false; Namespace = $ns; TimedOut = $true }
            }
            return [pscustomobject]@{ Mutex = $m; Acquired = $true; Namespace = $ns; TimedOut = $false }
        } catch {
            if ($m) { try { $m.Dispose() } catch { } }
        }
    }
    Write-Warning "difftest case lock unavailable for $CaseName; proceeding unlocked"
    return [pscustomobject]@{ Mutex = $null; Acquired = $false; Namespace = $null; TimedOut = $false }
}
function Exit-CaseLock {
    param($Lock)
    if ($Lock -and $Lock.Acquired -and $Lock.Mutex) {
        try { $Lock.Mutex.ReleaseMutex() } catch { Write-Warning "difftest case lock release failed: $_" }
        try { $Lock.Mutex.Dispose() } catch { }
    }
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

# --- NON BYTE-EXACT CASE SKIP (R1) -------------------------------------------------
# scripts/sweep-exclude.txt is the repo's single source of truth for case directories that
# are NOT byte-exact difftest cases: W7 whitelist cases, RUNTIME-CONTRACT probes (net48
# deliberately diverges, e.g. it returns localized text where the runtime must THROW), LOUD
# contract cases and incomplete WIP assets. sweep-cases.ps1 already filtered the full-pool
# sweep with it, but difftest.ps1 did not, so a direct `difftest.ps1 -Case <excluded>` was
# a PERMANENT ok=False that hides real regressions.
# The list is read through scripts/sweep-exclude.ps1 -- the SAME parser sweep-cases.ps1 uses
# (Trim / drop empty and '^#' / first token of -split '\s+'), so the three consumers can
# never drift. A SKIP is never silent: the reason is the entry's trailing '# ...' comment,
# printed here and written to the report files; exit code stays 0 because SKIP != failure.
# (loud-gate.ps1 deliberately relies on excluded cases still exiting non-zero; it filters
# its own 'LOUD contract' subset out of this list and is unaffected by this early return.)
# LOUD contract rows are the exception: their non-zero exit + documented exception text IS
# the assertion, and scripts/loud-gate.ps1 drives difftest.ps1 on exactly those rows
# (`if ($line -notmatch "LOUD contract") { continue }`, loud-gate.ps1:47) treating a zero
# exit as "the LOUD contract was lost". They must therefore keep running the normal path
# here; loud-gate is their verifier, the byte-exact suite is not.
. (Join-Path $PSScriptRoot "sweep-exclude.ps1")
$excludePath = Join-Path $PSScriptRoot "sweep-exclude.txt"
$excludeEntry = Find-SweepExcludeEntry -Case $Case -Path $excludePath
if ($excludeEntry -and $excludeEntry.Line -notmatch 'LOUD contract') {
    $skipReason = if ($excludeEntry.Reason) { $excludeEntry.Reason } else { "(no reason recorded in $excludePath)" }
    $jsonPath = "$ReportDir/$Case.$Compiler.diffreport.json"
    $txtPath  = "$ReportDir/$Case.$Compiler.diffreport.txt"
    ([ordered]@{
        case = $Case; compiler = $Compiler; ok = "SKIP"; skipped = $true
        reason = $skipReason; excludeList = $excludePath; excludeLine = $excludeEntry.Line
        note = "listed as non byte-exact in sweep-exclude.txt: oracle-vs-runtime comparison not run"
    } | ConvertTo-Json -Depth 4) | Set-Content -Path $jsonPath -Encoding UTF8
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("case=$Case compiler=$Compiler ok=SKIP")
    $lines.Add("skip reason=$skipReason")
    $lines.Add("skip source=$excludePath")
    $lines.Add("skip note=listed as non byte-exact in sweep-exclude.txt; oracle-vs-runtime comparison not run")
    $lines | Set-Content -Path $txtPath -Encoding UTF8
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $lines | ForEach-Object { Write-Host $_ }
    [Console]::OutputEncoding = $origOutputEncoding
    exit 0
}
# Unique log name per round: a same-named log, when a previous invocation aborted and its
# cmake/cmd child processes still hold the handle, makes the next redirect fail outright
# (cmd cannot open > target -> a non-exit-code error, NativeCommandError). The run tag adds
# PID + a GUID prefix to the timestamp: the second-resolution stamp alone collided for two
# runs started in the same second (proven: 4 concurrent same-case runs all redirected into
# one `<case>_diff.<stamp>.net48.log` and 3 of 4 died on the shared `>` target).
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runTag = "$stamp-$PID-$([guid]::NewGuid().ToString('N').Substring(0,8))"
# Every per-run scratch path is unique per invocation (no cross-run sharing, no leftovers).
$oracleDir = "$caseDir/out_stage_diff_$runTag"
$gen       = "$caseDir/gen_diff_$runTag"
$buildDir  = "$caseDir/build_diff_${Compiler}_$runTag"
$logDir    = "$Root/.opencode/logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$caseLock = Enter-CaseLock $Case
$exitCode = 1
# NOTE: the body below is intentionally left at the original indentation so the fix stays a
# reviewable diff; everything between `try {` and `finally {` runs under the case lock.
try {

$net48Log = "$logDir/${Case}_diff.$runTag.net48.log"
New-Item -ItemType Directory -Force -Path $oracleDir | Out-Null
cmd /c "dotnet build `"$csproj`" -c Release -o `"$oracleDir`" > `"$net48Log`" 2>&1"
if ($LASTEXITCODE -ne 0) { throw "net48 build failed (see $net48Log)" }
# Decide the transpile input BEFORE deploying native libs: a case-owned native dll (mylib/ etc.) must not be mistaken
# for the managed assembly (run-tests L255-259); net48 produces only an exe here.
$oracleExe = "$oracleDir/$Case.exe"
if (-not (Test-Path $oracleExe)) { throw "oracle exe missing: $oracleExe" }
$transpileInput = if (Test-Path "$oracleDir/$Case.dll") { "$oracleDir/$Case.dll" } else { $oracleExe }
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
        $libBuildLog = "$logDir/${Case}_diff.$runTag.$libName.dll.log"
        cmd /c "g++ -shared -o `"$oracleDir/$libName.dll`" `"$($libSrc.FullName)`" > `"$libBuildLog`" 2>&1"
        if ($LASTEXITCODE -ne 0) { throw "build $libName.dll for oracle $Case failed (see $libBuildLog)" }
    }
}

$exp = Invoke-RawRun $oracleExe $TimeoutMs $AppArgs

$transpileLog = "$logDir/${Case}_diff.$runTag.transpile.log"
$input = $transpileInput
$transpilerDll = "$Root/src/il2cppc/bin/Debug/net8.0/il2cppc.dll"
$transpileCmd = if (Test-Path $transpilerDll) { "dotnet exec `"$transpilerDll`"" } else { "dotnet run --project `"$Root/src/il2cppc`" --" }
cmd /c "$transpileCmd `"$input`" `"$gen`" > `"$transpileLog`" 2>&1"
if ($LASTEXITCODE -ne 0) { throw "transpile failed/L3-loud-reject (see $transpileLog)" }

$buildLog = "$logDir/${Case}_diff.$runTag.build.log"
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
# P/Invoke cases: the gen CMake builds the case-own native lib as a SHARED dll under <build>/<lib>/; deploy it next to
# the runtime exe (mirrors run-tests Deploy-NativeLibs) so the out-of-manifest DllImport resolves at runtime.
$rtExeDir = (Resolve-Path $buildDir).Path
Get-ChildItem $buildDir -Recurse -Filter *.dll -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.DirectoryName -eq $rtExeDir) { return }
    try { Copy-Item $_.FullName "$buildDir/" -Force -ErrorAction Stop }
    catch { Write-Warning "deploy native lib dll failed (non-fatal): $_" }
}
$act = Invoke-RawRun $rtExe $TimeoutMs $AppArgs

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
$exitCode = if ($ok) { 0 } else { 1 }

} finally {
    # Always reclaim this run's private scratch (unique tag -> cannot hit a peer's directory).
    # The pre-fix harness never cleaned these, so every run leaked gen_diff_*/build_diff_*/
    # out_stage_diff under the case directory. Best-effort: a still-running child must not turn
    # cleanup into a second failure.
    foreach ($p in @($oracleDir, $gen, $buildDir)) {
        if ($p) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Exit-CaseLock $caseLock
}
exit $exitCode
