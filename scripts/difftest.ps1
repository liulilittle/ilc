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

# 差分对照 harness（A3）：net48 oracle vs 我们的运行时，**逐字节**比较 stdout/stderr/退出码。
# 复用点（与 run-tests.ps1 同构）：net48 Exe 参考运行 + il2cppc 转译 + cmake 通道构建 + il2cpp_gen_exe。
# 关键差异（run-tests 的 harness 缺陷修正）：
#   1. 所有原生调用经 cmd /c 重定向到日志文件 —— PS 5.1 在 $ErrorActionPreference='Stop' 下会把
#      原生命令 stderr（如转译器 warning）转成 NativeCommandError 终止异常，导致用例在转译阶段
#      被静默中止（本 pilot 实证）。经 cmd /c 重定向后 stderr 只落日志，退出码是唯一失败信号。
#   2. 输出按**原始字节**采集（不经 UTF-8 文本解码），避免解码归一化掩盖字节级差异。
#   3. 控制台输出代码页钉到 UTF-8(65001)：net48 oracle 跟随控制台 CP 输出，运行时固定 UTF-8；
#      不钉则非 ASCII 记录（如 enc.utf8.getstring）产生纯编码伪影（与 run-tests L60-69 同法）。

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
# 工具链 PATH 注入：run-tests 依赖调用方环境已配好 cmake/MinGW；本 harness 自带探测，
# 使单机（无 CI 外壳）也能一键复现，避免 "cmake 不在 PATH" 之类的环境噪声污染差分结论。
$cmakeDir = Resolve-ToolDir @('D:\android\sdk\cmake\3.22.1\bin','C:\Program Files\CMake\bin','C:\msys64\mingw64\bin') 'cmake.exe'
$mingwDir = Resolve-ToolDir @('D:\Program Files\mingw\msys2\mingw64\bin','C:\msys64\mingw64\bin','C:\mingw64\bin','D:\mingw64\bin') 'g++.exe'
foreach ($d in @($cmakeDir, $mingwDir)) { if ($d -and ($env:PATH -notlike "*$d*")) { $env:PATH = "$d;$env:PATH" } }

$caseDir  = "$Root/tests/cases/$Case"
$csproj   = "$caseDir/$Case.csproj"
if (-not (Test-Path $csproj)) { throw "missing $csproj" }
# 唯一化本轮日志名：同名日志在"上一次调用异常退出、其 cmake/cmd 子进程仍持有句柄"时
# 会让下一次重定向直接失败（cmd 无法打开 > 目标 → 非退出码类错误，NativeCommandError）。
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$oracleDir = "$caseDir/out_stage_diff"
$logDir    = "$Root/.opencode/logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Remove-Item $oracleDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $oracleDir | Out-Null

$net48Log = "$logDir/${Case}_diff.$stamp.net48.log"
cmd /c "dotnet build `"$csproj`" -c Release -o `"$oracleDir`" > `"$net48Log`" 2>&1"
if ($LASTEXITCODE -ne 0) { throw "net48 build failed (see $net48Log)" }
$oracleExe = "$oracleDir/$Case.exe"
if (-not (Test-Path $oracleExe)) { throw "oracle exe missing: $oracleExe" }
$exp = Invoke-RawRun $oracleExe $TimeoutMs

$transpileLog = "$logDir/${Case}_diff.$stamp.transpile.log"
$input = if (Test-Path "$oracleDir/$Case.dll") { "$oracleDir/$Case.dll" } else { $oracleExe }
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

# 原始输出留证：运行时中止/崩溃类结论必须能回看原始 stdout/stderr（字节级）。
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
