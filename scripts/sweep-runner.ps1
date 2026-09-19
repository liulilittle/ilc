param(
    # 4-channel generalization draft: mingw | msvc | clang.
    [int]$Worker = 0,
    [int]$Workers = 8,
    [int]$ShardCount = 32,
    [ValidateSet("mingw", "msvc", "clang")][string]$Compiler = "mingw",
    [int]$BuildParallel = 4,
    [string]$Root = "E:\il2cpp",
    [string]$PauseFlag = ""
)
$ErrorActionPreference = "Continue"
if (-not $PauseFlag) { $PauseFlag = Join-Path $Root ".opencode\tmp\sweep-pause_$Compiler.flag" }
for ($i = $Worker; $i -lt $ShardCount; $i += $Workers) {
    if (Test-Path -LiteralPath $PauseFlag) { Write-Host "paused ($Compiler) before shard $i"; break }
    $list = Join-Path $Root (".opencode\tmp\shards\shard{0:D2}.txt" -f $i)
    $out = Join-Path $Root (".opencode\results\shard{0:D2}_{1}.txt" -f $i, $Compiler)
    & (Join-Path $Root "scripts\sweep-cases.ps1") -List $list -Out $out -Compiler $Compiler -BuildParallel $BuildParallel
}
