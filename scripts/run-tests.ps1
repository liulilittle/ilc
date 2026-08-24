param(
    [ValidateSet("msvc","mingw","wsl")][string]$Compiler = "mingw",
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
    param([string]$ExePath)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ExePath
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $p = [System.Diagnostics.Process]::new()
    $p.StartInfo = $psi
    $null = $p.Start()
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    $p.WaitForExit()
    return [pscustomobject]@{
        Out  = $outTask.GetAwaiter().GetResult()
        Err  = $errTask.GetAwaiter().GetResult()
        Code = $p.ExitCode
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
# 结果表：每用例一条记录 {Case, Status(PASS/FAIL), Stage(失败阶段或错误信息)}
$results = [System.Collections.Generic.List[object]]::new()
# 用例 = tests/cases 下每个独立目录（tests/cases/<name>/<name>.cs + <name>.csproj）
$caseDirs = Get-ChildItem "$Root/tests/cases" -Directory | Sort-Object Name
if ($Case) {
    $match = $caseDirs | Where-Object Name -eq $Case
    if (-not $match) { throw "case '$Case' not found under tests/cases" }
    $caseDirs = @($match)
}
$total = $caseDirs.Count

foreach ($caseDir in $caseDirs) {
    $name = $caseDir.Name
    Write-Host "=== case: $name ($Compiler) ==="
    try {

    # 1. 编译为 net48 程序集（fixtures 模式：net48 Exe → 产物是 <name>.exe，非 dll）
    $csproj = "$Root/tests/cases/$name/$name.csproj"
    if (-not (Test-Path $csproj)) { throw "missing $csproj" }
    dotnet build $csproj -c Release -o "$Root/tests/cases/$name/out" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "build $name failed" }

    # 1.5 M4 P/Invoke: 用例自带原生库（mylib/ 目录）→ 构建 DLL 供 net48 参考运行动态加载。
    # net48 参考运行是 Windows 进程 → 始终构建 mylib.dll（Windows 侧 g++）。
    if (Test-Path "$Root/tests/cases/$name/mylib") {
        $mylibSrc = Get-ChildItem "$Root/tests/cases/$name/mylib" -Filter *.cpp | Select-Object -First 1
        if ($mylibSrc) {
            g++ -shared -o "$Root/tests/cases/$name/out/mylib.dll" $mylibSrc.FullName
            if ($LASTEXITCODE -ne 0) { throw "build mylib.dll for $name failed" }
        }
    }

    # 2. 参考运行：.NET Framework 4.8 直接执行 net48 exe（.NET 8 host 不能跑 net48 程序集）
    $exp = Invoke-NativeRun "$Root/tests/cases/$name/out/$name.exe"
    if ($exp.Code -ne 0) { throw "reference run $name failed" }

    # 3. 转译：输入用 net48 exe PE（PEReader 不区分扩展名，与 fixtures 的 calc.dll 同 PE）
    # P25f (dhprobe)：net8+ 用例的产物是 原生 apphost exe + 托管 dll —— 转译必须读
    # 托管的 dll（apphost 的 exe 没有托管元数据，PEReader.GetMetadataBlock 失败）；
    # net48 用例只产 exe（无 dll），保持原有 exe 路径。判定：out/<name>.dll 存在。
    $transpileInput = if (Test-Path "$Root/tests/cases/$name/out/$name.dll") { "$Root/tests/cases/$name/out/$name.dll" } else { "$Root/tests/cases/$name/out/$name.exe" }
    $gen = "$Root/tests/cases/$name/gen"
    Remove-Item $gen -Recurse -Force -ErrorAction SilentlyContinue
    dotnet run --project "$Root/src/il2cppc" -- $transpileInput $gen | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "transpile $name failed" }

    # 4. 三编译器构建 + 运行
    $buildDir = "$Root/tests/cases/$name/build_$Compiler"
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
        cmake -S $gen -B $buildDir -G "Visual Studio 17 2022" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake configure $name failed" }
        cmake --build $buildDir --config Release | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
        $actual = Invoke-NativeRun "$buildDir/Release/il2cpp_gen_exe.exe"
    } else {
        cmake -S $gen -B $buildDir -G "MinGW Makefiles" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake configure $name failed" }
        cmake --build $buildDir --config Release | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
        $actual = Invoke-NativeRun "$buildDir/il2cpp_gen_exe.exe"
    }

    # 5. 逐字节 diff：stdout/stderr 分列比对（UTF-8 解码，按行拆分后 LF join 归一化换行）
    $eOut = ((Split-CaptureLines $exp.Out)  -join "`n")
    $eErr = ((Split-CaptureLines $exp.Err)  -join "`n")
    $aOut = ((Split-CaptureLines $actual.Out) -join "`n")
    $aErr = ((Split-CaptureLines $actual.Err) -join "`n")
    if ($eOut -ne $aOut -or $eErr -ne $aErr) {
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
