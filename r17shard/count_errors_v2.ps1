$logFile = "E:\il2cpp\r77shard\r77shard3_shard3_errors.log"

$lines = Get-Content $logFile

$fileErrors = @{}
$typeNames = @{}

foreach ($line in $lines) {
    if ($line -like "*IThreadPoolWorkItem*") {
        # Extract file name
        if ($line -match '^([^:]+):(\d+):\d+: error:') {
            $f = $Matches[1]
            if (-not $fileErrors.ContainsKey($f)) {
                $fileErrors[$f] = 0
            }
            $fileErrors[$f]++
        }
        # Extract type name
        if ($line -match '(System_Threading_IThreadPoolWorkItem[A-Za-z0-9_]*)') {
            $tn = $Matches[1]
            if (-not $typeNames.ContainsKey($tn)) {
                $typeNames[$tn] = 0
            }
            $typeNames[$tn]++
        }
    }
}

Write-Host "IThreadPoolWorkItem affected files: $($fileErrors.Count)"
Write-Host ""
Write-Host "=== Per-file error counts ==="
$fileErrors.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host "$($_.Key): $($_.Count)"
}

Write-Host ""
Write-Host "=== IThreadPoolWorkItem type variants ==="
$typeNames.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host "$($_.Key): $($_.Count)"
}
