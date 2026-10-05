param(
    [ValidateSet("msvc","mingw","wsl","clang")][string]$Compiler = "mingw",
    [string]$Case = "",
    [switch]$ContinueOnError,
    [string]$ResultsFile = ""
)
$ErrorActionPreference = "Stop"
# 以脚本位置为基准解析仓库根，避免 -File 模式下 CWD 不确定导致相对路径失效
$Root = Split-Path -Parent $PSScriptRoot

# E:\il2cpp\xxx -> /mnt/e/il2cpp/xxx（先 Resolve-Path 到绝对路径再转换，相对路径 Substring(0,1) 会取错盘符）
function ConvertTo-WslPath([string]$winPath) {
    $drive = $winPath.Substring(0,1).ToLower()
    $rest = $winPath.Substring(3).Replace("\","/")
    return "/mnt/$drive/$rest"
}

# 原生进程捕获：stdout/stderr 分别以 UTF-8 解码（不用 2>&1 合并——合并顺序依赖 CRT
# 缓冲/调度，连 net48 oracle 自身都会在两次运行间翻转，是 console3/consolestream
# 流序 diff FAIL 的 harness 伪影）。返回 {Out, Err, Code}，调用方按流逐字节比对：
# 内容语义与原合并比对等价，且顺序确定。异步读避免大输出时的管道死锁。
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
    # stdin 立即 EOF：部分用例（consoleio）显式测试"EOF stdin 下 ReadLine=null"语义，
    # 且可避免在非交互宿主（计划任务 SYSTEM）下 ReadLine 阻塞挂死整个 worker。
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

# D4 (P-D4): 把用例的 SHARED 原生库 .dll 部署到转译后 exe 旁（清单外 resolve 在
# 运行期经 LoadLibrary 加载——Windows 首先搜索 exe 所在目录）。MinGW/clang 把
# .dll 落在库目标自身的二进制子目录（buildDir/<mod>/）；MSVC 还多一层 config
# 子目录。静态库（mylib.a/.lib）与 INTERFACE 垫片（kernel32）无 .dll，天然跳过。
function Deploy-NativeLibs([string]$buildDir) {
    $exeDir = (Resolve-Path $buildDir).Path
    $dlls = Get-ChildItem $buildDir -Recurse -Filter *.dll -ErrorAction SilentlyContinue
    foreach ($d in $dlls) {
        if ($d.DirectoryName -eq $exeDir) { continue }
        try { Copy-Item $d.FullName "$buildDir/" -Force -ErrorAction Stop }
        catch { Write-Warning "deploy native lib dll failed (non-fatal): $_" }
    }
}

# 捕获文本按行拆分（统一 CRLF/LF、去掉末尾空行）——与原 & 捕获的按行数组语义一致。
function Split-CaptureLines([string]$text) {
    if ($null -eq $text) { return @() }
    $lines = @($text -split "`r?`n")
    if ($lines.Count -gt 0 -and $lines[-1] -eq '') { $lines = $lines[0..($lines.Count-2)] }
    return @($lines)
}

$failures = 0
# --- UTF-8 捕获管线（console3 unicode 编码修复，2026-08-11）---
# pwsh 用 [Console]::OutputEncoding 解码原生命令输出。net48 oracle 的
# Console.OutputEncoding 默认取活动控制台输出代码页（本机 936/GBK），wide 字符
# 按 GBK 输出；转译运行时则固定输出 UTF-8。两者经同一个代码页解码时必有一方乱码
# （console3 的 wide 行 diff FAIL 根因）。把控制台输出代码页钉到 UTF-8 (65001)
# 后两侧一致：net48 oracle 跟随控制台 CP（SetConsoleOutputCP 传播到子进程）也输出
# UTF-8，运行时本即 UTF-8，pwsh 按 UTF-8 解码——同一组 code point，与主机代码页
# 无关。ASCII 输出在两种代码页下字节恒等，其余用例不受影响。
$origOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# --- RT-fix (2026-09-13): 同 case 跨通道并发 + 残留进程文件锁的健壮化 ---
# 实证根因: tests/cases/<case>/out 为多通道共享; 残留的 <case>.exe 进程锁住 PE
# 镜像, 下一次 dotnet build 复制 obj->out 时 MSB3026 重试 10 次后 MSB3027 失败
# (consoleio PID 30376 实锤; autogen044 同型重试后成功)。另: gen/ 与 out/ 跨通道
# 并发读写, 以及 sweep-cases 超时 taskkill /T 竞态遗留孤儿进程。三道防线:
#   (1) Enter-CaseLock: 每 case 跨进程命名 Mutex, 串行化同 case 的共享目录读写;
#   (2) Stop-ResidualCaseProcess: 持锁后按"精确 exe 路径"回收残留镜像进程
#       (只杀 ExecutablePath 完全等于 out/staging 内该 case exe 的进程);
#   (3) staging 参考输出目录: 构建/参考运行不写共享 out, 结束后尽力发布回 out
#       (发布失败仅警告, 不影响用例判定)。
function Enter-CaseLock {
    param([string]$CaseName, [int]$TimeoutMs = 0)
    # R1: WaitOne 的返回值必须检查。RTFIX 基线 `$null = $m.WaitOne(...)` 在超时
    # (返回 $false) 后仍把未持有的 mutex 当锁返回; 之后 ReleaseMutex 抛
    # ApplicationException 又被 catch{} 吞掉 —— 结果=静默无锁放行且无任何记录。
    # R1: 超时=有活持有者; 执行路径与基线等价(仍无锁继续, 用例照跑保兼容),
    # 但显式告警留痕(审计)并丢弃未持有的 mutex; 释放只针对真正持有的锁,
    # 释放失败=真异常必须告警(不再吞)。
    # 等待上限可经 IL2CPP_RUNTESTS_LOCK_MS 覆写(默认 480000ms 与基线一致;
    # 供超时分支自测与 CI 调优)。
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
            catch [System.Threading.AbandonedMutexException] { $acquired = $true } # 前任持有者已死=已获得
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
    # R1: 只释放真正持有的锁; 释放失败必须告警(基线 catch{} 静默吞掉)。
    if ($Lock -and $Lock.Acquired -and $Lock.Mutex) {
        try { $Lock.Mutex.ReleaseMutex() } catch { Write-Warning "case lock release failed: $_" }
        try { $Lock.Mutex.Dispose() } catch { }
    }
}
function Stop-ResidualCaseProcess {
    param([string]$ExePath)
    # 调用处用 "/" 拼路径, WMI 返回 "\"; 不归一化则字符串比较永不相等 (自测实证)。
    $full = [System.IO.Path]::GetFullPath($ExePath)
    if (-not (Test-Path -LiteralPath $full)) { return }
    $exeName = Split-Path $full -Leaf
    try {
        $procs = Get-CimInstance Win32_Process -Filter "Name = '$exeName'" -ErrorAction Stop |
            Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $full) }
        foreach ($procInfo in @($procs)) {
            try {
                Stop-Process -Id $procInfo.ProcessId -Force -ErrorAction Stop
                # TerminateProcess 异步: 镜像文件锁可能仍短暂存在, 等其真正退出再构建。
                try { $null = [System.Diagnostics.Process]::GetProcessById($procInfo.ProcessId).WaitForExit(5000) } catch { }
                Write-Host "reaped residual $exeName PID $($procInfo.ProcessId)"
            } catch { Write-Warning "reap failed for PID $($procInfo.ProcessId): $_" }
        }
    } catch {
        Write-Warning "residual scan failed for ${exeName}: $_"
    }
}
# 结果表：每用例一条记录 {Case, Status(PASS/FAIL), Stage(失败阶段或错误信息)}
$results = [System.Collections.Generic.List[object]]::new()
# 用例 = tests/cases 下每个独立目录（tests/cases/<name>/<name>.cs + <name>.csproj）。
# wf1probe 二期：同目录变体工程可被精确点名 —— `-Case <variant>` 先按目录名匹配；
# 目录未命中时再精确匹配 tests/cases/*/<variant>.csproj（恰好一处；零处沿用原报错，
# 多处报歧义）。命中变体后：记录/日志/锁/产物名一律用变体工程名（与既有单例同格式，
# 可进 check-final 聚合）；目录级共享路径（gen/build/out/staging）沿用所在目录并在
# 每次运行前重建（与主例同）；变体与主例不得并发（锁名不同，门只跑目录池，变体仅
# 手工单例串行调用）。
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
    # 变体点名时记录名用工程名（目录名仅定位共享路径）；主例/全池时两者一致。
    # 所有落盘工作目录一律按所在目录键（$dirName），与主例共享并在每次运行前重建；
    # 变体工程名（$name）仅用于记录 Case、日志名、锁名（锁按目录取，与主例互斥串行）、
    # 产物 exe 名与隔离工作区。如此转译器的 caseRoot（程序集上两级）恒为真实 case
    # 目录，P/Invoke 垫片（kernel32/…）相对路径恒可解。
    $name = if ($projName) { $projName } else { $caseDir.Name }
    $dirName = $caseDir.Name
    Write-Host "=== case: $name ($Compiler) ==="
    # RT-fix: 同 case 跨通道串行化（共享 out/gen 的读写互斥）; 持锁期间回收残留进程。
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

    # 1. 编译为 net48 程序集（fixtures 模式：net48 Exe → 产物是 <name>.exe，非 dll）
    $csproj = "$Root/tests/cases/$($caseDir.Name)/$name.csproj"
    if (-not (Test-Path $csproj)) { throw "missing $csproj" }
    # RT-fix: 参考输出构建进本通道私有 staging（不写共享 out，免疫残留进程文件锁）;
    # 持 case 锁后先按精确路径回收锁住 out/staging 镜像的残留进程。
    $outDir = "$Root/tests/cases/$dirName/out"
    $staging = "$Root/tests/cases/$dirName/out_stage_$Compiler"
    Stop-ResidualCaseProcess "$outDir/$name.exe"
    Stop-ResidualCaseProcess "$staging/$name.exe"
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    # dotnet 输出落盘取证（曾因 Out-Null 吞掉零填充 project.assets.json 的首错文本）；
    # cmd /c 传退出码，日志含 stdout+stderr。
    $dotnetLog = "$Root/.opencode/logs/$($name)_$Compiler.dotnet.log"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dotnetLog) | Out-Null
    cmd /c "dotnet build `"$csproj`" -c Release -o `"$staging`" > `"$dotnetLog`" 2>&1"
    if ($LASTEXITCODE -ne 0) { throw "build $name failed" }

    # 3/1.5 转译输入判定必须在本步骤（原生库构建之前）完成：net8+ 用例的产物是
    # 原生 apphost exe + 托管 dll（转译读 dll）；net48 只产 exe（转译读 exe）。
    # 原生库（如 extdll 与用例同名）随后会进入 staging 生成 <name>.dll，若此时
    # 才判定会把原生 dll 误当托管程序集（"PE image does not have metadata"）。
    $transpileInput = if (Test-Path "$staging/$name.dll") { "$staging/$name.dll" } else { "$staging/$name.exe" }

    # 1.5 M4/D4 P/Invoke: 用例自带原生库目录（mylib/ 与 SHARED 库如 extdll/）→
    # 构建 DLL 供 net48 参考运行动态加载（net48 是 Windows 进程 → 始终 g++ -shared）。
    # 库名取自目录的 CMakeLists project()（无则用目录名）；无 .cpp 的目录（系统
    # DLL 的 INTERFACE 垫片 kernel32/user32/...）自然跳过。
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

    # 2. 参考运行：.NET Framework 4.8 直接执行 net48 exe（.NET 8 host 不能跑 net48 程序集）
    $exp = Invoke-NativeRun "$staging/$name.exe" -WorkingDirectory $caseCwd
    if ($exp.Code -ne 0) { throw "reference run $name failed" }

    # 2.5 RT-fix: 尽力发布到共享 out（兼容消费方/诊断）; 被外部进程锁住时仅警告, 不影响判定。
    try {
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        Copy-Item "$staging/*" $outDir -Recurse -Force -ErrorAction Stop
    } catch { Write-Warning "publish to out failed (non-fatal): $_" }

    # 3. 转译：输入用转译输入（net8 dll / net48 exe，见前判定）
    $gen = "$Root/tests/cases/$dirName/gen"
    Remove-Item $gen -Recurse -Force -ErrorAction SilentlyContinue
    $transpilerDll = "$Root/src/il2cppc/bin/Debug/net8.0/il2cppc.dll"
    if (Test-Path $transpilerDll) {
        dotnet exec $transpilerDll $transpileInput $gen | Out-Null
    } else {
        dotnet run --project "$Root/src/il2cppc" -- $transpileInput $gen | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "transpile $name failed" }

    # 4. 三编译器构建 + 运行
    $buildDir = "$Root/tests/cases/$dirName/build_$Compiler"
    if ($Compiler -eq "wsl") {
        # WSL cmake 3.10.2 无 -S/-B（3.13 引入）→ cd 进 build 目录 + 绝对源路径；
        # -d Ubuntu1804 显式锁定 GCC 7.5 兼容基线发行版
        $wslGen = ConvertTo-WslPath (Resolve-Path $gen)
        New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
        $wslBuild = ConvertTo-WslPath (Resolve-Path $buildDir)
        wsl -d Ubuntu1804 bash -c "cd `"$wslBuild`" && rm -rf * && cmake -G 'Unix Makefiles' -DCMAKE_BUILD_TYPE=Release `"$wslGen`" && cmake --build ."
        if ($LASTEXITCODE -ne 0) { throw "wsl cmake build $name failed" }
        # WSL 运行：一次运行、shell 重定向分流到临时文件（再按流读取比对，与
        # Invoke-NativeRun 同一语义——避免 2>&1 合并顺序伪影）。
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
        $sccache = "E:\il2cpp\.opencode\tmp\tools\sccache\sccache-v0.17.0-x86_64-pc-windows-msvc\sccache.exe"
        $ninja = "D:\android\sdk\cmake\3.22.1\bin\ninja.exe"
        $useSccache = (Test-Path $sccache) -and (Test-Path $ninja)
        if ($useSccache) {
            $env:SCCACHE_DIR = "E:\il2cpp\.opencode\tmp\sccache"
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
            cmake -S $gen -B $buildDir -G "Visual Studio 17 2022" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "cmake configure $name failed" }
            cmake --build $buildDir --config Release | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
            Deploy-NativeLibs $buildDir
            $actual = Invoke-NativeRun "$buildDir/Release/il2cpp_gen_exe.exe" -WorkingDirectory $caseCwd
        }
    } elseif ($Compiler -eq "clang") {
        # Clang 23 (windows-gnu) + MSYS2 MinGW-w64 sysroot: 该 LLVM 不做 GCC 探测,
        # winpthread 由工具链文件注入; ccache 由工具链 find_program 自动挂接。
        # build_clang 按编译器隔离, 但首次接入/工具链变更后的 cache 不可复用 -> 清。
        $env:PATH = "D:\android\sdk\cmake\3.22.1\bin;D:\Program Files\mingw\msys2\mingw64\bin;" + $env:PATH
        Remove-Item $buildDir -Recurse -Force -ErrorAction SilentlyContinue
        # P-F-FIX D1: MinGW/clang 分支的原生 stderr（编译器 warning，如 gcc16
        # -Warray-bounds formatext.cpp:436）在 PS5.1 + EAP=Stop 下经 |Out-Null 管道
        # 变 NativeCommandError 中止一个实际 exit=0 的成功构建。stderr 2>nul 分流
        # （构建成败只看 $LASTEXITCODE），与 run-tests 其它分支同口径。
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

    # 4.5 Archive gate（NDK/Android 通道；IL2CPP_ARCHIVE_GATE=1 时启用）：
    # 运行时归档成员数=源清单 + 关键符号哨兵；失败按用例 FAIL 处理（throw 进 catch）。
    if ($env:IL2CPP_ARCHIVE_GATE -eq '1') {
        $gateArchive = if ($env:IL2CPP_ARCHIVE_PATH) { $env:IL2CPP_ARCHIVE_PATH } else { Join-Path $buildDir 'libil2cpp_runtime.a' }
        $gateSources = if ($env:IL2CPP_ARCHIVE_SOURCES) { $env:IL2CPP_ARCHIVE_SOURCES } else { Join-Path $Root '.opencode/tmp/ndk/files.txt' }
        & "$Root/scripts/archive-gate.ps1" -Archive $gateArchive -SourceList $gateSources
        if ($LASTEXITCODE -ne 0) { throw "archive gate failed: $gateArchive" }
    }
    # 5. 逐字节 diff：stdout/stderr 分列比对（UTF-8 解码，按行拆分后 LF join 归一化换行）
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
        # -ContinueOnError：任一阶段失败（含转译）→ 记录到结果表并继续下一个用例；默认行为不变（立即中止）
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
