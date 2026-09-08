param([int]$Shard = 1)
$ErrorActionPreference = 'Continue'
$tree = 'E:/il2cpp/out/mscorlib_gen_r78'
$inc1 = "$tree/include/internal"
$inc2 = 'E:/il2cpp/src/runtime/include/internal'
$outDir = 'E:/il2cpp/r78shard'
$files = Get-ChildItem "$tree/src/*.cpp" | Sort-Object Name | Select-Object -ExpandProperty FullName
$n = $files.Count
$per = [Math]::Ceiling($n / 3)
$start = ($Shard - 1) * $per
$end = [Math]::Min($start + $per - 1, $n - 1)
$list = $files[$start..$end]
$total = $list.Count
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$log = Join-Path $outDir "r78shard${Shard}_shard${Shard}.log"
$csv = Join-Path $outDir "r78shard${Shard}_shard${Shard}.csv"
if (Test-Path $log) { Remove-Item $log -Force }
if (Test-Path $csv) { Remove-Item $csv -Force }
$results = $list | ForEach-Object -Parallel {
  $f = $_
  $o = & g++ -fsyntax-only -std=c++17 -fexceptions -fpermissive -ferror-limit=0 -I $using:inc1 -I $using:inc2 $f 2>&1 | Out-String
  $e = $LASTEXITCODE
  [pscustomobject]@{ File = $f; Exit = $e; Out = $o }
} -ThrottleLimit 8
$fail = 0
foreach ($r in $results) {
  $first = ''
  if ($r.Out -match '(?m)^.*error:.*$') { $first = $Matches[0].Trim() }
  Add-Content -LiteralPath $csv -Value "$($r.File)`t$($r.Exit)`t$first"
  Add-Content -LiteralPath $log -Value "=== $($r.File) exit=$($r.Exit)`n$($r.Out)"
  if ($r.Exit -ne 0) { $fail++ }
}
$elapsed = $sw.Elapsed.TotalSeconds
$ok = $total - $fail
$summary = @"
r78 shard$Shard Results
====================
Total: $total
SuccessEmpty: $ok
Fail: $fail
ElapsedSeconds: $elapsed
ThrottleLimit: 8
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
"@
Set-Content -LiteralPath (Join-Path $outDir "r78shard${Shard}_shard${Shard}_summary.txt") -Value $summary
$results | Where-Object { $_.Exit -ne 0 } | ForEach-Object { $_.Out } | Set-Content -LiteralPath (Join-Path $outDir "r78shard${Shard}_shard${Shard}_errors.log")
$results | Where-Object { $_.Exit -ne 0 } | ForEach-Object { $_.Out } | Select-String 'error:' | Group-Object { ($_ -replace '^.*error:\s*','' -replace '\s+.*$','').Substring(0, [Math]::Min(60, ($_ -replace '^.*error:\s*','').Length)) } | Sort-Object Count -Descending | Select-Object -First 30 Count,Name | Out-String | Set-Content -LiteralPath (Join-Path $outDir "r78shard${Shard}_shard${Shard}_clusters.txt")
Set-Content -LiteralPath (Join-Path $outDir "DONE_r78shard${Shard}.flag") -Value "DONE shard$Shard total=$total success=$ok fail=$fail elapsed=$elapsed $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "DONE shard$Shard total=$total fail=$fail elapsed=$elapsed"

