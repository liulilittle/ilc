$ErrorActionPreference = 'Continue'
Set-Location E:\il2cpp
$status = 0
foreach ($tag in 'net48','nc21','nc31','net5','net6','net7','net8','net9','net10') {
  $dir = "tmp/icallwide"
  cmake -S "$dir/gen_$tag" -B "$dir/build_$tag" -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release 2>&1 | Out-Null
  $b = cmake --build "$dir/build_$tag" -j 8 2>&1
  if ($LASTEXITCODE -ne 0) {
    Write-Output "${tag}: BUILD FAILED"
    $b | Select-Object -Last 8 | ForEach-Object { Write-Output "    $_" }
    $status = 1
    continue
  }
  $out = & "$dir/build_$tag/il2cpp_gen_exe.exe" 2>&1
  $rc = $LASTEXITCODE
  $fails = @($out | Where-Object { $_ -match '^FAIL' })
  $last = $out[-1]
  Write-Output "${tag}: EXIT=$rc FAILS=$($fails.Count) LAST=[$last]"
  if ($rc -ne 0 -or $fails.Count -gt 0) {
    $fails | Select-Object -First 12 | ForEach-Object { Write-Output "    $_" }
    $status = 1
  }
}
exit $status
