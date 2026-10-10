param(
    # 4-channel generalization draft: mingw | msvc | clang (live behavior unchanged for mingw)
    [ValidateSet("mingw", "msvc", "clang")][string]$Compiler = "mingw",
    [int]$Workers = 6,
    [int]$ShardCount = 32,
    [int]$BuildParallel = 3,
    [string]$Root = "F:\il2cpp",
    [string]$RunnerScript = "",
    [string]$PauseFlag = "",
    [switch]$DryRun
)
$ErrorActionPreference = "Continue"
if (-not $RunnerScript) { $RunnerScript = Join-Path $Root "scripts\sweep-runner.ps1" }
if (-not $PauseFlag) { $PauseFlag = Join-Path $Root ".opencode\tmp\sweep-pause_$Compiler.flag" }
if (Test-Path -LiteralPath $PauseFlag) { Write-Host "paused ($Compiler): $PauseFlag"; exit 0 }
# Crash recovery: a hard reset can leave building_<case>.flag behind; the
# interrupted case's build_<Compiler> may hold half-written obj/ar (fake
# undefined-reference errors), so purge it and the flag before restarting.
$tmpDir = Join-Path $Root ".opencode\tmp"
$recoveryLog = Join-Path $Root ".opencode\logs\recovery.log"
foreach ($m in Get-ChildItem (Join-Path $tmpDir "building_*.flag") -ErrorAction SilentlyContinue) {
    $case = $m.BaseName -replace '^building_', ''
    if ($DryRun) { Write-Host "DRY-RUN would purge build_$Compiler+obj+out for crash-interrupted case $case"; continue }
    $bd = Join-Path $Root "tests\cases\$case\build_$Compiler"
    if (Test-Path -LiteralPath $bd) { Remove-Item -Recurse -Force $bd -ErrorAction SilentlyContinue }
    # dotnet stages can be cut too: project.assets.json can be zero-filled
    # ('0x00' invalid start) and fails the build instantly -> clear obj/out.
    foreach ($sub in @("obj", "out")) {
        $sd = Join-Path $Root "tests\cases\$case\$sub"
        if (Test-Path -LiteralPath $sd) { Remove-Item -Recurse -Force $sd -ErrorAction SilentlyContinue }
    }
    Remove-Item -LiteralPath $m.FullName -Force -ErrorAction SilentlyContinue
    Add-Content -LiteralPath $recoveryLog -Value ("{0} purged build_$Compiler+obj+out for crash-interrupted case $case" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
}
if ($DryRun) {
    Write-Host "DRY-RUN supervisor ($Compiler) Workers=$Workers ShardCount=$ShardCount BuildParallel=$BuildParallel"
    for ($w = 0; $w -lt $Workers; $w++) {
        $log = Join-Path $Root ".opencode\logs\worker$w.log"
        $err = Join-Path $Root ".opencode\logs\worker$w.err"
        Write-Host ("DRY-RUN would start: powershell.exe -NoProfile -ExecutionPolicy Bypass -File {0} -Worker {1} -Workers {2} -ShardCount {3} -Compiler {4} -BuildParallel {5} (logs: {6}, {7})" -f $RunnerScript, $w, $Workers, $ShardCount, $Compiler, $BuildParallel, $log, $err)
    }
    exit 0
}
$procs = @()
for ($w = 0; $w -lt $Workers; $w++) {
    $log = Join-Path $Root ".opencode\logs\worker$w.log"
    $err = Join-Path $Root ".opencode\logs\worker$w.err"
    $p = Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $RunnerScript, "-Worker", $w, "-Workers", $Workers, "-ShardCount", $ShardCount, "-Compiler", $Compiler, "-BuildParallel", $BuildParallel -WorkingDirectory $Root -RedirectStandardOutput $log -RedirectStandardError $err -PassThru
    $procs += $p
}
$procs | Wait-Process
