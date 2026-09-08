$ErrorActionPreference="Stop"
$buildDir="E:\il2cpp\build"
$outDir="E:\il2cpp\r17shard"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$exes=Get-ChildItem "$buildDir\test_*.exe" | Sort-Object Name
$total=$exes.Count
Write-Host "TOTAL=$total"
$results=$exes | ForEach-Object -Parallel {
  $exe=$_
  $name=$exe.BaseName
  $psi=[System.Diagnostics.ProcessStartInfo]::new()
  $psi.FileName=$exe.FullName
  $psi.RedirectStandardOutput=$true
  $psi.RedirectStandardError=$true
  $psi.UseShellExecute=$false
  $p=[System.Diagnostics.Process]::new()
  $p.StartInfo=$psi
  $null=$p.Start()
  $out=$p.StandardOutput.ReadToEnd()
  $err=$p.StandardError.ReadToEnd()
  $p.WaitForExit()
  $code=$p.ExitCode
  $isFail = ($out -like "*FAIL*") -or ($err -like "*FAIL*")
  if($code -eq 0 -and -not $isFail){$status="PASS"} else {$status="FAIL"}
  [pscustomobject]@{Name=$name;Status=$status;Code=$code}
} -ThrottleLimit 8
$passed=($results | Where-Object Status -eq "PASS").Count
$failed=($results | Where-Object Status -eq "FAIL").Count
$passRate=[math]::Round(($passed/$total)*100,1)
Write-Host "=== PASS RATE: $passed/$total ($passRate%) ==="
if($failed -gt 0){
  Write-Host "=== FAILURE LIST ==="
  $results | Where-Object Status -eq "FAIL" | ForEach-Object { Write-Host "FAIL $($_.Name) code=$($_.Code)" }
} else {
  Write-Host "=== NO FAILURES ==="
}
$summary=@("passed=$passed failed=$failed total=$total passRate=$passRate%","---FAILURES---")
$failLines=$results | Where-Object Status -eq "FAIL" | ForEach-Object { "$($_.Name)`tcode=$($_.Code)" }
$allLines=$results | ForEach-Object { "$($_.Status)`t$($_.Name)`tcode=$($_.Code)" }
$outContent=$summary + $failLines + @("---ALL---") + $allLines
Set-Content -Path "$outDir\test_parallel_summary.txt" -Value ($outContent -join "`n") -Encoding utf8
Write-Host "results written to $outDir\test_parallel_summary.txt"
