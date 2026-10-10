param(
    # 4-channel generalization draft: mingw | msvc | clang.
    # Per-compiler, isolated: results glob shard*_<compiler>.txt, own pause flag,
    # own process match (-Compiler <channel>), restart via the matching supervisor.
    [ValidateSet("mingw", "msvc", "clang")][string]$Compiler = "mingw",
    [string]$Root = "F:\il2cpp",
    [string]$ResultsDir = "",
    [int]$CompleteThreshold = 563,
    [int]$Workers = 2,
    [int]$BuildParallel = 1,
    [int]$ShardCount = 32,
    [string]$PauseFlag = "",
    [string]$SupervisorScript = "",
    [switch]$DryRun
)
$ErrorActionPreference = "Continue"
if (-not $ResultsDir) { $ResultsDir = Join-Path $Root ".opencode\results" }
if (-not $PauseFlag) { $PauseFlag = Join-Path $Root ".opencode\tmp\sweep-pause_$Compiler.flag" }
if (-not $SupervisorScript) { $SupervisorScript = Join-Path $Root "scripts\sweep-supervisor.ps1" }
if (Test-Path -LiteralPath $PauseFlag) { Write-Host "paused ($Compiler): $PauseFlag"; exit 0 }
$results = Get-ChildItem -LiteralPath $ResultsDir -Filter "shard*_$Compiler.txt" -File -ErrorAction SilentlyContinue
$count = 0
foreach ($f in $results) { $count += @(Get-Content -LiteralPath $f.FullName | Where-Object { $_ -match '^(PASS|FAIL)\s' }).Count }
if ($count -ge $CompleteThreshold) { Write-Host "sweep complete ($count records)"; exit 0 }
$runners = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -match 'sweep-(runner|supervisor)' -and $_.CommandLine -match ('-Compiler\s+' + [regex]::Escape($Compiler) + '(\s|$)')
})
if ($runners.Count -gt 0) { Write-Host "sweep running ($($runners.Count) procs, $count records)"; exit 0 }
$cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$SupervisorScript`" -Compiler $Compiler -Workers $Workers -ShardCount $ShardCount -BuildParallel $BuildParallel"
if ($DryRun) { Write-Host "restarting ($count records so far) -- DRY-RUN would start: $cmd"; exit 0 }
Write-Host "restarting ($count records so far)"
Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $SupervisorScript, "-Compiler", $Compiler, "-Workers", $Workers, "-ShardCount", $ShardCount, "-BuildParallel", $BuildParallel -WorkingDirectory $Root
