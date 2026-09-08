$logFile = "E:\il2cpp\r77shard\r77shard3_shard3_errors.log"

$unknowns = Get-Content $logFile | Where-Object { $_ -like '*unknown type name*IThreadPoolWorkItem*' }
$totals = Get-Content $logFile | Where-Object { $_ -like '*IThreadPoolWorkItem*' }

Write-Host "IThreadPoolWorkItem unknown type errors: $($unknowns.Count)"
Write-Host "IThreadPoolWorkItem total lines: $($totals.Count)"

Write-Host ""
Write-Host "=== Sample unknown errors ==="
$unknowns | Select-Object -First 20 | ForEach-Object { Write-Host $_ }

Write-Host ""
Write-Host "=== Affected files ==="
$fileNames = @()
foreach ($line in $unknowns) {
    if ($line -match '^([^:]+):(\d+):\d+: error:') {
        $f = $Matches[1]
        if ($fileNames -notcontains $f) {
            $fileNames += $f
        }
    }
}
$fileNames | ForEach-Object { Write-Host $_ }

Write-Host ""
Write-Host "=== All unique IThreadPoolWorkItem types referenced ==="
$typeNames = @{}
foreach ($line in $totals) {
    if ($line -match '(System_Threading_IThreadPoolWorkItem[A-Za-z0-9_]*)') {
        $tn = $Matches[1]
        if (-not $typeNames.ContainsKey($tn)) {
            $typeNames[$tn] = 0
        }
        $typeNames[$tn]++
    }
}
$typeNames.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host "$($_.Key): $($_.Count)"
}
