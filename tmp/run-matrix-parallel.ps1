param(
    [int[]]$Indices,
    [int]$Throttle = 4,
    [string]$Log = "tmp/matrix_scan.log"
)
$cases = Get-Content "tmp/matrix_cases.txt"
$results = $Indices | ForEach-Object -Parallel {
    $i = $_
    $allCases = $using:cases
    $c = $allCases[$i]
    & "E:\il2cpp\scripts\run-tests.ps1" -Case $c *> $null
    $code = $LASTEXITCODE
    $tag = if ($code -eq 0) { "OK" } else { "FAIL" }
    Set-Content -Path "E:\il2cpp\tmp\matrix_job_$i.txt" -Value "$tag $i $c" -Encoding utf8
    [pscustomobject]@{ Index = $i; Case = $c; Code = $code }
} -ThrottleLimit $Throttle -AsJob | Receive-Job -Wait
$pass = 0; $fail = 0; $failed = @()
foreach ($r in $results) {
    if ($r.Code -eq 0) { $pass++ } else { $fail++; $failed += $r.Case }
    Add-Content -Path $Log -Value "$(if ($r.Code -eq 0) { 'OK' } else { 'FAIL' }) $($r.Index) $($r.Case)" -Encoding utf8
}
Write-Output "PARALLEL[$($Indices -join ',')]: PASS=$pass FAIL=$fail"
if ($failed.Count -gt 0) { $failed | ForEach-Object { "FAILED: $_" } }
