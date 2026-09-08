 $ErrorActionPreference="Stop"
$buildDir="E:\il2cpp\build"
$outDir="E:\il2cpp\r17shard"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$exes=Get-ChildItem "$buildDir\test_*.exe" | Sort-Object Name
$total=$exes.Count
$passed=0
$failed=0
$results=[System.Collections.Generic.List[string]]::new()
foreach($exe in $exes){
  $name=$exe.BaseName
  Write-Host "RUN $name"
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
  if($status -eq "PASS"){ $passed++ } else { $failed++ }
  $results.Add("$status`t$name`tcode=$code")
  $lines=@("case=$name","status=$status","code=$code","---stdout---",$out,"---stderr---",$err)
  Set-Content -Path "$outDir\test_${name}.txt" -Value ($lines -join "`n") -Encoding utf8
  Write-Host "$status $name code=$code"
}
$summary=@("passed=$passed failed=$failed total=$total") + $results
Set-Content -Path "$outDir\test_summary.txt" -Value ($summary -join "`n") -Encoding utf8
Write-Host "=== results: $passed/$total passed ==="
