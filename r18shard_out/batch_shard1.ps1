param(
    [int]$Skip = 0,
    [int]$Take = 514,
    [string]$Suffix = "part1"
)
$ErrorActionPreference = "Continue"
$shardFile = "E:\il2cpp\r18shard_out\shard1.txt"
$outDir = "E:\il2cpp\r18shard_out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$filesAll = Get-Content -LiteralPath $shardFile | Where-Object { $_.Trim() -ne "" }
$files = $filesAll | Select-Object -Skip $Skip -First $Take
$total = $files.Count
Write-Host "Batch $Suffix Skip $Skip Take $Take actual $total"

$start = Get-Date

$results = $files | ForEach-Object -Parallel {
    $file = $_.Trim()
    if (-not $file) { return }
    $fSlash = $file -replace "\\","/"
    $out = & g++ -fsyntax-only -std=c++17 -fexceptions -fpermissive -ferror-limit=0 -I E:/il2cpp/out/mscorlib_gen_r18/include/internal -I E:/il2cpp/src/runtime/include/internal "$fSlash" 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($null -eq $out) { $out = "" }
    $out = $out.Trim()
    [PSCustomObject]@{
        File = $file
        FSlash = $fSlash
        ExitCode = $code
        Output = $out
    }
} -ThrottleLimit 8

$end = Get-Date
$elapsed = ($end - $start).TotalSeconds
Write-Host "Batch $Suffix done elapsed ${elapsed}s"

$csvTmp = Join-Path $outDir "shard1_${Suffix}.csv"
$logTmp = Join-Path $outDir "shard1_${Suffix}.log"

$results | Select-Object File, FSlash, ExitCode, @{N='Output';E={$_.Output}}, @{N='HasOutput';E={$_.Output -ne ""}} | Export-Csv -LiteralPath $csvTmp -NoTypeInformation -Encoding UTF8

$logLines = @()
$logLines += "# Batch $Suffix Skip $Skip Take $Take elapsed ${elapsed}s"
foreach ($r in $results) {
    $logLines += "=== $($r.File) Exit:$($r.ExitCode) ==="
    if ($r.Output -ne "") { $logLines += $r.Output } else { $logLines += "(ok)" }
    $logLines += ""
}
Set-Content -LiteralPath $logTmp -Value ($logLines -join "`r`n") -Encoding UTF8

Write-Host "Written $csvTmp and $logTmp"
