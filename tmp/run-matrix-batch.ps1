param(
    [int]$Start = 0,
    [int]$Count = 25,
    [string]$Log = "tmp/matrix_scan.log"
)
$cases = Get-Content "tmp/matrix_cases.txt"
$end = [Math]::Min($Start + $Count, $cases.Count)
$pass = 0; $fail = 0; $failed = @()
for ($i = $Start; $i -lt $end; $i++) {
    $c = $cases[$i]
    & "scripts/run-tests.ps1" -Case $c *> $null
    if ($LASTEXITCODE -eq 0) { $pass++; Add-Content -Path $Log -Value "OK $i $c" -Encoding utf8 }
    else { $fail++; $failed += $c; Add-Content -Path $Log -Value "FAIL $i $c" -Encoding utf8 }
}
Write-Output "BATCH[$Start..$($end-1)]: PASS=$pass FAIL=$fail"
if ($failed.Count -gt 0) { $failed | ForEach-Object { "FAILED: $_" } }
