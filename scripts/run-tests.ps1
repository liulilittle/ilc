param(
    [ValidateSet("msvc","mingw","wsl","clang")][string]$Compiler = "mingw",
    [string]$Case = "",
    [switch]$ContinueOnError,
    [string]$ResultsFile = "",
    [string]$Arch = ""
)
$ErrorActionPreference = "Stop"
# Resolve the repo root relative to this script location; avoids relative-path breakage when CWD is undefined under -File mode
$Root = Split-Path -Parent $PSScriptRoot

# F:\il2cpp\xxx -> /mnt/f/il2cpp/xxx (Resolve-Path to an absolute path first; on a relative path Substring(0,1) picks the wrong drive)
function ConvertTo-WslPath([string]$winPath) {
    $drive = $winPath.Substring(0,1).ToLower()
    $rest = $winPath.Substring(3).Replace("\","/")
    return "/mnt/$drive/$rest"
}

# Native process capture: decode stdout/stderr separately as UTF-8 (no 2>&1 merge -- merge order depends on CRT
# buffering/scheduling and even flips between two runs of the net48 oracle itself; that artifact is the root of
# console3/consolestream stream-order diff FAILs). Returns {Out, Err, Code}; the caller compares per stream:
# semantics equal the old merged compare, with a deterministic order. Async read avoids pipe deadlock on large output.
function Invoke-NativeRun {
    param([string]$ExePath, [int]$TimeoutMs = 120000, [string]$WorkingDirectory = $Root)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ExePath
    # H-3: native children (net48 oracle) resolve RELATIVE temp paths against
    # their working directory; pin it explicitly so an unset/inherited
    # CurrentDirectory (e.g. C:\Windows\System32 under a WMI-launched worker)
    # cannot make the oracle fail ("reference run <case> failed").
    # H-2: callers pass a per-case scratch dir so fixed-name relative temp files
    # (filechain_tmp.txt, fcopy_*, sp_tmp.bin, ...) never collide across cases
    # or across concurrent runner generations. Oracle and runtime share the SAME
    # scratch dir per case so CWD-printing cases (envdeep/environment2) stay
    # byte-identical between the two sides.
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
# stdin EOF immediately: some cases (consoleio) explicitly test the "ReadLine=null under EOF stdin" semantics,
# and it prevents ReadLine from blocking and hanging the whole worker under a non-interactive host (scheduled-task SYSTEM).
    $psi.RedirectStandardInput = $true
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $p = [System.Diagnostics.Process]::new()
    $p.StartInfo = $psi
    $null = $p.Start()
    $p.StandardInput.Close()
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutMs)) {
        try { $p.Kill($true) } catch { }
        throw "native run timeout (${TimeoutMs}ms): $ExePath"
    }
    return [pscustomobject]@{
        Out  = $outTask.GetAwaiter().GetResult()
        Err  = $errTask.GetAwaiter().GetResult()
        Code = $p.ExitCode
    }
}

# D4 (P-D4): deploy the case SHARED native library .dll next to the transpiled exe (off-manifest resolve
# loads it at runtime via LoadLibrary -- Windows searches the exe own directory first). MinGW/clang put
# the .dll under the library target own binary subdirectory (buildDir/<mod>/); MSVC adds another config
# subdirectory. Static libraries (mylib.a/.lib) and INTERFACE shims (kernel32) have no .dll and are skipped naturally.
function Deploy-NativeLibs([string]$buildDir) {
    $exeDir = (Resolve-Path $buildDir).Path
    $dlls = Get-ChildItem $buildDir -Recurse -Filter *.dll -ErrorAction SilentlyContinue
    foreach ($d in $dlls) {
        if ($d.DirectoryName -eq $exeDir) { continue }
        try { Copy-Item $d.FullName "$buildDir/" -Force -ErrorAction Stop }
        catch { Write-Warning "deploy native lib dll failed (non-fatal): $_" }
    }
}

# Split captured text into lines (normalize CRLF/LF, drop the trailing empty line) -- same per-line array semantics as the old & capture.
function Split-CaptureLines([string]$text) {
    if ($null -eq $text) { return @() }
    $lines = @($text -split "`r?`n")
    if ($lines.Count -gt 0 -and $lines[-1] -eq '') { $lines = $lines[0..($lines.Count-2)] }
    return @($lines)
}

$failures = 0
# --- UTF-8 capture pipeline (console3 unicode encoding fix, 2026-08-11) ---
# pwsh decodes native command output with [Console]::OutputEncoding. The net48 oracle Console.OutputEncoding
# defaults to the active console output code page (936/GBK on this host), so wide chars are emitted as GBK;
# the transpiled runtime always emits UTF-8. Decoding both through one code page garbles one side
# (the root cause of the console3 wide-line diff FAIL). Pinning the console output code page to UTF-8 (65001)
# aligns both sides: the net48 oracle follows the console CP (SetConsoleOutputCP propagates to children) and
# also emits UTF-8, the runtime is UTF-8 already, and pwsh decodes as UTF-8 -- the same code points, independent of
# the host code page. ASCII output is byte-identical under either code page, so other cases are unaffected.
$origOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# --- RT-fix (2026-09-13): robustness against same-case cross-channel concurrency + residual-process file locks ---
# Proven root cause: tests/cases/<case>/out is shared across channels; a residual <case>.exe process locks the PE
# image, so the next dotnet build copying obj->out fails MSB3027 after MSB3026 retries 10 times
# (confirmed with consoleio PID 30376; autogen044 succeeds after the same-shape retry). Also: gen/ and out/ are
# read+written concurrently across channels, and a sweep-cases timeout taskkill /T race leaves orphan processes. Three defenses:
#   (1) Enter-CaseLock: a per-case cross-process named Mutex serializes shared-directory reads/writes for the same case;
#   (2) Stop-ResidualCaseProcess: after taking the lock, reclaim residual image processes by exact exe path
#       (kill only processes whose ExecutablePath exactly equals this case exe under out/staging);
#   (3) staging reference-output directory: build/reference-run does not write the shared out, and best-effort publishes back to out after
#       (a publish failure only warns and does not affect the case verdict).
function Enter-CaseLock {
    param([string]$CaseName, [int]$TimeoutMs = 0)
    # R1: the WaitOne return value must be checked. The RTFIX baseline treated an unheld mutex as the lock after
    # a timeout (returning $false); then ReleaseMutex threw ApplicationException which catch{} swallowed -- the result
    # was a silent unlocked pass with no record. R1: a timeout means a live holder; the execution path equals the
    # baseline (still continues unlocked, cases still run for compatibility), but it logs an explicit warning (audit)
    # and drops the unheld mutex; release only targets a truly held lock, and a release failure is a real exception
    # that must warn (no longer swallowed). The wait cap can be overridden via IL2CPP_RUNTESTS_LOCK_MS
    # (default 480000ms, same as baseline, for timeout-branch self-test and CI tuning).
    # for the timeout-branch self-test and CI tuning).
    if ($TimeoutMs -le 0) {
        $TimeoutMs = 480000
        if ($env:IL2CPP_RUNTESTS_LOCK_MS -match '^\d+$' -and [int]$env:IL2CPP_RUNTESTS_LOCK_MS -gt 0) {
            $TimeoutMs = [int]$env:IL2CPP_RUNTESTS_LOCK_MS
        }
    }
    foreach ($ns in @('Global', 'Local')) {
        $m = $null
        try {
            $m = [System.Threading.Mutex]::new($false, "$ns\il2cpp_runtests_$CaseName")
            $acquired = $false
            try { $acquired = $m.WaitOne($TimeoutMs) }
            catch [System.Threading.AbandonedMutexException] { $acquired = $true } # previous holder died = acquired
            if (-not $acquired) {
                Write-Warning "case lock timeout after ${TimeoutMs}ms for $CaseName ($ns): proceeding WITHOUT the lock"
                try { $m.Dispose() } catch { }
                return [pscustomobject]@{ Mutex = $null; Acquired = $false; Namespace = $ns; TimedOut = $true }
            }
            return [pscustomobject]@{ Mutex = $m; Acquired = $true; Namespace = $ns; TimedOut = $false }
        } catch {
            if ($m) { try { $m.Dispose() } catch { } }
        }
    }
    Write-Warning "case lock unavailable for $CaseName; proceeding unlocked"
    return [pscustomobject]@{ Mutex = $null; Acquired = $false; Namespace = $null; TimedOut = $false }
}
function Exit-CaseLock {
    param($Lock)
    # R1: release only a truly held lock; a release failure must warn (baseline catch{} swallowed silently).
    if ($Lock -and $Lock.Acquired -and $Lock.Mutex) {
        try { $Lock.Mutex.ReleaseMutex() } catch { Write-Warning "case lock release failed: $_" }
        try { $Lock.Mutex.Dispose() } catch { }
    }
}
function Stop-ResidualCaseProcess {
    param([string]$ExePath)
    # Callers join paths with "/", WMI returns "\"; without normalization the string compare never matches (self-test proven).
    $full = [System.IO.Path]::GetFullPath($ExePath)
    if (-not (Test-Path -LiteralPath $full)) { return }
    $exeName = Split-Path $full -Leaf
    try {
        $procs = Get-CimInstance Win32_Process -Filter "Name = '$exeName'" -ErrorAction Stop |
            Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $full) }
        foreach ($procInfo in @($procs)) {
            try {
                Stop-Process -Id $procInfo.ProcessId -Force -ErrorAction Stop
                # TerminateProcess is async: the image file lock may linger briefly, so wait for a real exit before building.
                try { $null = [System.Diagnostics.Process]::GetProcessById($procInfo.ProcessId).WaitForExit(5000) } catch { }
                Write-Host "reaped residual $exeName PID $($procInfo.ProcessId)"
            } catch { Write-Warning "reap failed for PID $($procInfo.ProcessId): $_" }
        }
    } catch {
        Write-Warning "residual scan failed for ${exeName}: $_"
    }
}
# Result table: one record per case {Case, Status(PASS/FAIL), Stage(failing stage or error message)}
$results = [System.Collections.Generic.List[object]]::new()
# A case = each standalone directory under tests/cases (tests/cases/<name>/<name>.cs + <name>.csproj).
# wf1probe phase 2: a same-directory variant project can be addressed by name -- -Case <variant> matches the directory name first;
# if the directory does not match, it then exactly matches tests/cases/*/<variant>.csproj (exactly one; zero keeps the original error,
# multiple reports ambiguity). After a variant matches: names for records/logs/locks/artifacts all use the variant project name (same format as existing singletons,
# eligible for check-final aggregation); directory-level shared paths (gen/build/out/staging) follow the containing directory and are
# rebuilt before each run (same as the main case); a variant and the main case must not run concurrently (different lock names, the gate only runs the directory pool,
# and a variant is invoked only by manual serial singletons).
$caseDirs = Get-ChildItem "$Root/tests/cases" -Directory | Sort-Object Name
$projName = ""
if ($Case) {
    $match = $caseDirs | Where-Object Name -eq $Case
    if (-not $match) {
        $variantFiles = @(Get-ChildItem "$Root/tests/cases/*/$Case.csproj" -ErrorAction SilentlyContinue)
        if ($variantFiles.Count -eq 0 -or $null -eq $variantFiles[0]) { throw "case '$Case' not found under tests/cases" }
        if ($variantFiles.Count -gt 1) { throw "case '$Case' is ambiguous (multiple variant projects)" }
        $match = $variantFiles[0].Directory
        $projName = $Case
    }
    $caseDirs = @($match)
}
$total = $caseDirs.Count

foreach ($caseDir in $caseDirs) {
    # When a variant is named, the record uses the project name (the directory name only locates shared paths); for the main case/full pool the two are identical.
    # Every on-disk working directory is keyed by the containing directory ($dirName), shared with the main case and rebuilt before each run;
    # the variant project name ($name) is used only for the record Case, log names, lock names (locks are taken per directory, mutually serialized with the main case),
    # the artifact exe name, and the isolated workspace. This keeps the transpiler caseRoot (two levels up from the assembly) always the real case
    # directory, so P/Invoke shim (kernel32/...) relative paths always resolve.
    $name = if ($projName) { $projName } else { $caseDir.Name }
    $dirName = $caseDir.Name
    Write-Host "=== case: $name ($Compiler) ==="
    # RT-fix: serialize the same case across channels (mutual exclusion on shared out/gen reads+writes); reclaim residual processes while holding the lock.
    $caseLock = Enter-CaseLock $dirName
    try {
    try {

    # H-2: per-case isolated working directory, created UNDER the case lock so
    # two runners of the same case cannot race its creation. Case IO uses
    # RELATIVE temp names (filechain_tmp.txt, fcopy_*.txt, sp_tmp.bin, ...); two
    # runners (e.g. mingw and msvc sweeps) would otherwise clobber each other's
    # temp files in the shared repo-root CWD. The net48 oracle and the transpiled
    # runtime share this scratch dir, so the byte-for-byte diff is unaffected.
    # H-2b: a few cases reference REPO-ROOT-RELATIVE paths, not just bare temp
    # names (processcase: Process.Start("tests\\cases\\processcase\\exit42.exe"),
    # whose oracle HANGS when the path does not resolve). Expose the repo's
    # top-level `tests` directory inside the scratch CWD via a junction so those
    # paths keep resolving exactly as they did under the old repo-root CWD.
    $caseCwd = "$Root\.opencode\tmp\casecwd\${name}_$Compiler"
    Remove-Item -LiteralPath $caseCwd -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $caseCwd | Out-Null
    $testsLink = Join-Path $caseCwd "tests"
    try {
        if (-not (Test-Path -LiteralPath $testsLink)) {
            New-Item -ItemType Junction -Path $testsLink -Target "$Root\tests" -ErrorAction Stop | Out-Null
        }
    } catch {
        # Non-fatal: only cases that use repo-root-relative paths need it; if the
        # junction cannot be created, fall back to the shared repo-root CWD for
        # THIS case so its relative paths still resolve (loses H-2 isolation but
        # keeps correctness).
        Write-Warning "casecwd junction failed for ${name}: $_ (using repo root CWD)"
        $caseCwd = $Root
    }

    # 1. Compile to a net48 assembly (fixtures mode: net48 Exe -> artifact is <name>.exe, not a dll)
    $csproj = "$Root/tests/cases/$($caseDir.Name)/$name.csproj"
    if (-not (Test-Path $csproj)) { throw "missing $csproj" }
    # RT-fix: the reference output builds into this channel private staging (does not write the shared out, immune to residual-process file locks);
    # after taking the case lock, first reclaim residual processes by exact path that lock the out/staging image.
    $outDir = "$Root/tests/cases/$dirName/out"
    $staging = "$Root/tests/cases/$dirName/out_stage_$Compiler"
    Stop-ResidualCaseProcess "$outDir/$name.exe"
    Stop-ResidualCaseProcess "$staging/$name.exe"
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    # Persist dotnet output as evidence (Out-Null once swallowed the first-error text of a zero-filled project.assets.json);
    # cmd /c carries the exit code, and the log holds stdout+stderr.
    $dotnetLog = "$Root/.opencode/logs/$($name)_$Compiler.dotnet.log"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dotnetLog) | Out-Null
    cmd /c "dotnet build `"$csproj`" -c Release -o `"$staging`" > `"$dotnetLog`" 2>&1"
    if ($LASTEXITCODE -ne 0) { throw "build $name failed" }

    # 3/1.5 The transpile-input decision must be made in this step (before building native libs): a net8+ case artifact is
    # a native apphost exe + a managed dll (the transpiler reads the dll); net48 produces only an exe (the transpiler reads the exe).
    # A native library (e.g. extdll with the same name as the case) subsequently enters staging as <name>.dll; deciding
    # now would mistake the native dll for a managed assembly ("PE image does not have metadata").
    $transpileInput = if (Test-Path "$staging/$name.dll") { "$staging/$name.dll" } else { "$staging/$name.exe" }

    # 1.5 M4/D4 P/Invoke: a case own native library directories (mylib/ and SHARED libs such as extdll/) ->
    # build a DLL for dynamic loading by the net48 reference run (net48 is a Windows process -> always g++ -shared).
    # The library name comes from the directory CMakeLists project() (fallback: the directory name); directories with no .cpp (the system
    # DLL INTERFACE shims kernel32/user32/...) are skipped naturally.
    $nativeLibDirs = @(Get-ChildItem "$Root/tests/cases/$dirName" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'CMakeLists.txt') })
    foreach ($libDir in $nativeLibDirs) {
        $cmakeTxt = Get-Content (Join-Path $libDir.FullName 'CMakeLists.txt') -Raw -ErrorAction SilentlyContinue
        $libName = ''
        if ($cmakeTxt -and $cmakeTxt -match 'project\(\s*([A-Za-z0-9_]+)') { $libName = $Matches[1] }
        if (-not $libName) { $libName = $libDir.Name }
        $libSrc = Get-ChildItem $libDir.FullName -Filter *.cpp | Select-Object -First 1
        if ($libSrc) {
            g++ -shared -o "$staging/$libName.dll" $libSrc.FullName
            if ($LASTEXITCODE -ne 0) { throw "build $libName.dll for $name failed" }
        }
    }

    # 2. Reference run: .NET Framework 4.8 executes the net48 exe directly (a .NET 8 host cannot run a net48 assembly)
    $exp = Invoke-NativeRun "$staging/$name.exe" -WorkingDirectory $caseCwd
    if ($exp.Code -ne 0) { throw "reference run $name failed" }

    # 2.5 RT-fix: best-effort publish to the shared out (compatibility/diagnostics); if locked by an external process, only warn and do not affect the verdict.
    try {
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        Copy-Item "$staging/*" $outDir -Recurse -Force -ErrorAction Stop
    } catch { Write-Warning "publish to out failed (non-fatal): $_" }

    # 3. Transpile: input is the transpile input (net8 dll / net48 exe, see the earlier decision)
    $gen = "$Root/tests/cases/$dirName/gen"
    Remove-Item $gen -Recurse -Force -ErrorAction SilentlyContinue
    # G4-x86: pass the arch parameter through to the transpiler (--arch) so the generated layout + G1 entry interlock match
    # the cmake -A platform (by design the interlock rejects a default host-width transpile + -A Win32 build).
    $tArch = switch ($Arch) {
        "Win32" { "x86" }
        "x64"   { "x64" }
        "ARM64" { "arm64" }
        "arm32" { "arm32" }
        default { "" }
    }
    $tArgs = @($transpileInput, $gen)
    if ($tArch -ne "") { $tArgs = @("--arch", $tArch) + $tArgs }
    $transpilerDll = "$Root/src/il2cppc/bin/Debug/net8.0/il2cppc.dll"
    if (Test-Path $transpilerDll) {
        dotnet exec $transpilerDll @tArgs | Out-Null
    } else {
        dotnet run --project "$Root/src/il2cppc" -- @tArgs | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "transpile $name failed" }

    # 4. Build + run for the three compilers
    $buildDir = "$Root/tests/cases/$dirName/build_$Compiler"
    if ($Compiler -eq "wsl") {
        # WSL cmake 3.10.2 has no -S/-B (introduced in 3.13) -> cd into the build dir + absolute source path;
        # -d Ubuntu1804 explicitly pins the GCC 7.5 compatibility baseline distro
        $wslGen = ConvertTo-WslPath (Resolve-Path $gen)
        New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
        $wslBuild = ConvertTo-WslPath (Resolve-Path $buildDir)
        wsl -d Ubuntu1804 bash -c "cd `"$wslBuild`" && rm -rf * && cmake -G 'Unix Makefiles' -DCMAKE_BUILD_TYPE=Release `"$wslGen`" && cmake --build ."
        if ($LASTEXITCODE -ne 0) { throw "wsl cmake build $name failed" }
        # WSL run: one run, shell redirects split to temp files (then read per stream for comparison, the same
        # semantics as Invoke-NativeRun -- avoids 2>&1 merge-order artifacts).
        $wslTmpOut = "/tmp/il2cpp_${name}_out.txt"
        $wslTmpErr = "/tmp/il2cpp_${name}_err.txt"
        wsl -d Ubuntu1804 bash -c "`"$wslBuild/il2cpp_gen_exe`" > `"$wslTmpOut`" 2> `"$wslTmpErr`""
        if ($LASTEXITCODE -ne 0) { throw "wsl run $name failed" }
        $actual = [pscustomobject]@{
            Out  = ((wsl -d Ubuntu1804 bash -c "cat `"$wslTmpOut`"") -join "`n")
            Err  = ((wsl -d Ubuntu1804 bash -c "cat `"$wslTmpErr`"") -join "`n")
            Code = 0
        }
    } elseif ($Compiler -eq "msvc") {
        . "$Root/scripts/vcvars.ps1"
        # sccache + Ninja fast path. All sccache probes are bounded (.NET Process +
        # taskkill tree) and tolerate an already-running server; generator mismatch
        # is cleaned in both branches so a direct run-tests invocation cannot fail.
        function Invoke-BoundedCmd([string]$exe, [string]$argLine, [int]$timeoutMs) {
            $psi = [System.Diagnostics.ProcessStartInfo]::new()
            $psi.FileName = "cmd.exe"
            $psi.Arguments = "/d /c `"`"$exe`" $argLine 1>nul 2>nul`""
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $p = [System.Diagnostics.Process]::new()
            $p.StartInfo = $psi
            $null = $p.Start()
            if ($p.WaitForExit($timeoutMs)) { return $p.ExitCode }
            & taskkill.exe /PID $p.Id /T /F *> $null
            return $null
        }
        function Clear-IfGeneratorMismatch([string]$dir, [string]$want) {
            $cache = Join-Path $dir "CMakeCache.txt"
            if (Test-Path $cache) {
                $ct = Get-Content $cache -Raw
                if ($ct -notmatch ("CMAKE_GENERATOR:INTERNAL=" + [regex]::Escape($want))) {
                    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
        }
        $sccache = "F:\il2cpp\.opencode\tmp\tools\sccache\sccache-v0.17.0-x86_64-pc-windows-msvc\sccache.exe"
        $ninja = "D:\android\sdk\cmake\3.22.1\bin\ninja.exe"
        # Ninja has no -A platform concept (the VS generator does); an explicit
        # -Arch forces the VS generator path so -A reaches the toolchain.
        $useSccache = (Test-Path $sccache) -and (Test-Path $ninja) -and ($Arch -eq "")
        if ($useSccache) {
            $env:SCCACHE_DIR = "F:\il2cpp\.opencode\tmp\sccache"
            $env:SCCACHE_IGNORE_SERVER_IO_ERROR = "1"
            $rc = Invoke-BoundedCmd $sccache "--start-server" 10000
            if ($null -eq $rc -or $rc -ne 0) {
                # start-server is not idempotent: "Address in use" also means a usable
                # server already runs. Bounded connect decides; a foreign listener that
                # accepts TCP but never answers is killed by the timeout above.
                $rc2 = Invoke-BoundedCmd $sccache "--show-stats" 5000
                $useSccache = ($null -ne $rc2 -and $rc2 -eq 0)
            }
        }
        if ($useSccache) {
            Clear-IfGeneratorMismatch $buildDir "Ninja"
            cmake -S $gen -B $buildDir -G Ninja -DCMAKE_BUILD_TYPE=Release "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_CXX_COMPILER_LAUNCHER=$sccache" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "cmake configure $name failed" }
            cmake --build $buildDir --parallel 2 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
            Deploy-NativeLibs $buildDir
            $actual = Invoke-NativeRun "$buildDir/il2cpp_gen_exe.exe" -WorkingDirectory $caseCwd
        } else {
            Clear-IfGeneratorMismatch $buildDir "Visual Studio 17 2022"
            if ($Arch -ne "") {
                cmake -S $gen -B $buildDir -G "Visual Studio 17 2022" -A $Arch | Out-Null
            } else {
                cmake -S $gen -B $buildDir -G "Visual Studio 17 2022" | Out-Null
            }
            if ($LASTEXITCODE -ne 0) { throw "cmake configure $name failed" }
            cmake --build $buildDir --config Release | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
            Deploy-NativeLibs $buildDir
            $actual = Invoke-NativeRun "$buildDir/Release/il2cpp_gen_exe.exe" -WorkingDirectory $caseCwd
        }
    } elseif ($Compiler -eq "clang") {
        # Clang 23 (windows-gnu) + MSYS2 MinGW-w64 sysroot: this LLVM does no GCC probing,
        # winpthread is injected by the toolchain file; ccache is auto-hooked via the toolchain find_program.
        # build_clang is isolated per compiler, but the cache is not reusable after first setup/toolchain change -> clear it.
        $env:PATH = "D:\android\sdk\cmake\3.22.1\bin;D:\Program Files\mingw\msys2\mingw64\bin;" + $env:PATH
        Remove-Item $buildDir -Recurse -Force -ErrorAction SilentlyContinue
        # P-F-FIX D1: native stderr in the MinGW/clang branch (compiler warnings, e.g. gcc16
        # -Warray-bounds formatext.cpp:436) under PS5.1 + EAP=Stop via the |Out-Null pipe becomes
        # a NativeCommandError and aborts a build that actually exits 0. Route stderr 2>nul
        # (build success is judged solely by $LASTEXITCODE), the same policy as other run-tests branches.
        cmd /c "cmake -S `"$gen`" -B `"$buildDir`" -G `"MinGW Makefiles`" -DCMAKE_BUILD_TYPE=Release `"-DCMAKE_TOOLCHAIN_FILE=$Root/cmake/clang-mingw-w64.cmake`" 2>nul" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake configure $name failed" }
        cmd /c "cmake --build `"$buildDir`" --config Release 2>nul" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
        Deploy-NativeLibs $buildDir
        $actual = Invoke-NativeRun "$buildDir/il2cpp_gen_exe.exe" -WorkingDirectory $caseCwd
    } else {
        cmd /c "cmake -S `"$gen`" -B `"$buildDir`" -G `"MinGW Makefiles`" -DCMAKE_BUILD_TYPE=Release 2>nul" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake configure $name failed" }
# P2-HARDEN #4: g++ strict-aliasing warnings (interlocked.cpp etc.) go to
        # STDERR; under PS 5.1 + $ErrorActionPreference=Stop they surface as
        # NativeCommandError and abort the whole run (manual cmake was fine).
        # Route the build stderr to nul (cmd /c carries the exit code through
        # $LASTEXITCODE; Out-Null alone still lets the host turn stderr into an
        # error record).
        cmd /c "cmake --build `"$buildDir`" --config Release 2>nul" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
        Deploy-NativeLibs $buildDir
        $actual = Invoke-NativeRun "$buildDir/il2cpp_gen_exe.exe" -WorkingDirectory $caseCwd
    }

    # 4.5 Archive gate (NDK/Android channel; enabled when IL2CPP_ARCHIVE_GATE=1):
    # the runtime archive member count = source manifest + key-symbol sentinels; a failure is treated as a case FAIL (throw into catch).
    if ($env:IL2CPP_ARCHIVE_GATE -eq '1') {
        $gateArchive = if ($env:IL2CPP_ARCHIVE_PATH) { $env:IL2CPP_ARCHIVE_PATH } else { Join-Path $buildDir 'libil2cpp_runtime.a' }
        $gateSources = if ($env:IL2CPP_ARCHIVE_SOURCES) { $env:IL2CPP_ARCHIVE_SOURCES } else { Join-Path $Root '.opencode/tmp/ndk/files.txt' }
        & "$Root/scripts/archive-gate.ps1" -Archive $gateArchive -SourceList $gateSources
        if ($LASTEXITCODE -ne 0) { throw "archive gate failed: $gateArchive" }
    }
    # 5. Byte-for-byte diff: compare stdout/stderr separately (UTF-8 decode, split into lines then LF-join to normalize newlines,
    $eOut = ((Split-CaptureLines $exp.Out)  -join "`n")
    $eErr = ((Split-CaptureLines $exp.Err)  -join "`n")
    $aOut = ((Split-CaptureLines $actual.Out) -join "`n")
    $aErr = ((Split-CaptureLines $actual.Err) -join "`n")
    if ($eOut -cne $aOut -or $eErr -cne $aErr) {
        $failures++
        $results.Add([pscustomobject]@{ Case = $name; Status = "FAIL"; Stage = "diff" })
        Write-Host "FAIL: $name"
        Write-Host "--- expected stdout ---"; Split-CaptureLines $exp.Out
        Write-Host "--- expected stderr ---"; Split-CaptureLines $exp.Err
        Write-Host "--- actual stdout ---"; Split-CaptureLines $actual.Out
        Write-Host "--- actual stderr ---"; Split-CaptureLines $actual.Err
    } else {
        $results.Add([pscustomobject]@{ Case = $name; Status = "PASS"; Stage = "" })
        Write-Host "PASS: $name"
    }
    } catch {
        # -ContinueOnError: any stage failure (including transpile) -> record in the result table and continue to the next case; default behavior unchanged (abort immediately)
        if (-not $ContinueOnError) { throw }
        $failures++
        $results.Add([pscustomobject]@{ Case = $name; Status = "FAIL"; Stage = $_.Exception.Message })
        Write-Host "FAIL: $name ($($_.Exception.Message))"
    }
    } finally { Exit-CaseLock $caseLock }
}
$passed = $total - $failures
Write-Host "=== results: $passed/$total passed ($Compiler) ==="
Write-Host "PASS: $passed  FAIL: $failures  TOTAL: $total"
if ($failures -gt 0) {
    Write-Host "Failed cases:"
    $results | Where-Object Status -eq "FAIL" | ForEach-Object { Write-Host "  - $($_.Case): $($_.Stage)" }
}
if ($ResultsFile) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("compiler=$Compiler")
    $lines.Add("passed=$passed failed=$failures total=$total")
    foreach ($r in $results) { $lines.Add("$($r.Status)`t$($r.Case)`t$($r.Stage)") }
    Set-Content -Path $ResultsFile -Value $lines -Encoding utf8
    Write-Host "results written to $ResultsFile"
}
[Console]::OutputEncoding = $origOutputEncoding
exit $(if ($failures -gt 0) { 1 } else { 0 })
