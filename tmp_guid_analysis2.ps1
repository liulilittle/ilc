$guidErrors1 = Select-String -Path "E:\il2cpp\r77shard\r77shard1_shard1_errors.log" -Pattern "Guid|GUID"
$guidErrors2 = Select-String -Path "E:\il2cpp\r77shard\r77shard2_shard2_errors.log" -Pattern "Guid|GUID"
$guidErrors3 = Select-String -Path "E:\il2cpp\r77shard\r77shard3_shard3_errors.log" -Pattern "Guid|GUID"

$allErrors = @()
$allErrors += $guidErrors1 | ForEach-Object { $_.Line }
$allErrors += $guidErrors2 | ForEach-Object { $_.Line }
$allErrors += $guidErrors3 | ForEach-Object { $_.Line }

$totalCount = $allErrors.Count

$functionPatterns = @{}
$conversionPatterns = @{}

foreach ($err in $allErrors) {
    if ($err -match "no matching function for call to '([^']+)'") {
        $func = $matches[1]
        if (-not $functionPatterns.ContainsKey($func)) {
            $functionPatterns[$func] = 0
        }
        $functionPatterns[$func]++
    }
    if ($err -match "no viable conversion from '([^']+)' to '([^']+)'") {
        $key = "$($matches[1]) -> $($matches[2])"
        if (-not $conversionPatterns.ContainsKey($key)) {
            $conversionPatterns[$key] = 0
        }
        $conversionPatterns[$key]++
    }
}

Write-Output "TOTAL_GUID=$totalCount"
Write-Output "`n===TOP_FUNCTION_PATTERNS==="
$functionPatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 25 | ForEach-Object {
    Write-Output "$($_.Key): $($_.Value)"
}

Write-Output "`n===TOP_CONVERSION_PATTERNS==="
$conversionPatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 15 | ForEach-Object {
    Write-Output "$($_.Key): $($_.Value)"
}
