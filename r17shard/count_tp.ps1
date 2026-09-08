$errors = Get-Content 'E:\il2cpp\r77shard\r77shard3_shard3_errors.log'

$unknownCount = 0
$totalLines = 0

foreach ($line in $errors) {
    if ($line -match 'IThreadPoolWorkItem') {
        $totalLines++
        if ($line -match 'unknown type name') {
            $unknownCount++
        }
    }
}

Write-Host "IThreadPoolWorkItem total lines: $totalLines"
Write-Host "IThreadPoolWorkItem unknown type errors: $unknownCount"
