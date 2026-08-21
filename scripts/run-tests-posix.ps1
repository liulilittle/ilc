param(
    [string]$TestDir = "",
    [string]$Filter = "",
    [int]$TimeoutSec = 120,
    [string]$ResultsFile = ""
)
$ErrorActionPreference = "Stop"
# 以脚本位置为基准解析仓库根，避免 -File 模式下 CWD 不确定导致相对路径失效
$Root = Split-Path -Parent $PSScriptRoot

# 原生进程捕获：stdout/stderr 分别以 UTF-8 解码（与 run-tests.ps1 同一语义）。
# 异步读避免大输出时的管道死锁；超时后 kill 进程树并标记 TIMEOUT。
function Invoke-NativeRun {
    param([string]$ExePath, [int]$TimeoutSec)
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
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        try { $p.Kill($true) } catch { }
        $p.WaitForExit()
        return [pscustomobject]@{
            Out  = $outTask.GetAwaiter().GetResult()
            Err  = $errTask.GetAwaiter().GetResult()
            Code = -1
            TimedOut = $true
        }
    }
    return [pscustomobject]@{
        Out  = $outTask.GetAwaiter().GetResult()
        Err  = $errTask.GetAwaiter().GetResult()
        Code = $p.ExitCode
        TimedOut = $false
    }
}

if (-not $TestDir) {
    $TestDir = "$Root/build/runtime_posix"
}
if (-not (Test-Path $TestDir)) {
    throw "TestDir '$TestDir' does not exist. Build tests/runtime first (cmake -S tests/runtime -B $TestDir -G 'Unix Makefiles' && cmake --build $TestDir)."
}

# 收集测试 exe：tests/runtime 的 CMakeLists GLOB 生成 test_*.cpp -> test_*.exe（POSIX 下无 .exe 后缀）
$exes = @(Get-ChildItem $TestDir -File | Where-Object {
    $_.Name -like "test_*" -and $_.Extension -notin @(".o", ".obj", ".a", ".lib", ".pdb", ".ilk", ".dll", ".so", ".dylib")
})
if ($exes.Count -eq 0) {
    throw "No test_* executables found under $TestDir"
}
if ($Filter) {
    $exes = @($exes | Where-Object Name -like "*$Filter*")
    if ($exes.Count -eq 0) { throw "No test_* executable matches filter '$Filter'" }
}

$failures = 0
$results = [System.Collections.Generic.List[object]]::new()
$total = $exes.Count
$i = 0
foreach ($exe in $exes) {
    $i++
    $name = $exe.Name
    Write-Host "[$i/$total] $name"
    $r = Invoke-NativeRun $exe.FullName $TimeoutSec
    if ($r.TimedOut) {
        $failures++
        $results.Add([pscustomobject]@{ Case = $name; Status = "TIMEOUT"; Stage = "exceeded ${TimeoutSec}s" })
        Write-Host "  TIMEOUT (${TimeoutSec}s)"
    } elseif ($r.Code -ne 0) {
        $failures++
        $results.Add([pscustomobject]@{ Case = $name; Status = "FAIL"; Stage = "exit $($r.Code)" })
        Write-Host "  FAIL (exit $($r.Code))"
        if ($r.Out) { Write-Host "  stdout: $($r.Out.Trim())" }
        if ($r.Err) { Write-Host "  stderr: $($r.Err.Trim())" }
    } else {
        $results.Add([pscustomobject]@{ Case = $name; Status = "PASS"; Stage = "" })
        Write-Host "  PASS"
    }
}
$passed = $total - $failures
Write-Host "=== results: $passed/$total passed ==="
Write-Host "PASS: $passed  FAIL: $failures  TOTAL: $total"
if ($failures -gt 0) {
    Write-Host "Failed cases:"
    $results | Where-Object Status -ne "PASS" | ForEach-Object { Write-Host "  - $($_.Case): $($_.Stage)" }
}
if ($ResultsFile) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("testdir=$TestDir")
    $lines.Add("passed=$passed failed=$failures total=$total")
    foreach ($r in $results) { $lines.Add("$($r.Status)`t$($r.Case)`t$($r.Stage)") }
    Set-Content -Path $ResultsFile -Value $lines -Encoding utf8
    Write-Host "results written to $ResultsFile"
}
exit $(if ($failures -gt 0) { 1 } else { 0 })