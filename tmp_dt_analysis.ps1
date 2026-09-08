$dt1 = Select-String -Path "E:\il2cpp\r77shard\r77shard1_shard1_errors.log" -Pattern "DateTime|datetime"
$dt2 = Select-String -Path "E:\il2cpp\r77shard\r77shard2_shard2_errors.log" -Pattern "DateTime|datetime"
$dt3 = Select-String -Path "E:\il2cpp\r77shard\r77shard3_shard3_errors.log" -Pattern "DateTime|datetime"

$allErrors = @()
$allErrors += $dt1 | ForEach-Object { $_.Line }
$allErrors += $dt2 | ForEach-Object { $_.Line }
$allErrors += $dt3 | ForEach-Object { $_.Line }

$totalCount = $allErrors.Count

$categoryPatterns = @{
    "no_viable_conversion" = @()
    "no_viable_overloaded" = @()
    "no_match_for_call" = @()
    "cannot_cast" = @()
    "cannot_take_address" = @()
    "other" = @()
}

foreach ($err in $allErrors) {
    if ($err -like "*no viable conversion*") { $categoryPatterns["no_viable_conversion"] += $err }
    elseif ($err -like "*no viable overloaded*") { $categoryPatterns["no_viable_overloaded"] += $err }
    elseif ($err -like "*no matching function for call*") { $categoryPatterns["no_match_for_call"] += $err }
    elseif ($err -like "*cannot cast*") { $categoryPatterns["cannot_cast"] += $err }
    elseif ($err -like "*cannot take the address*") { $categoryPatterns["cannot_take_address"] += $err }
    else { $categoryPatterns["other"] += $err }
}

Write-Output "TOTAL_DATETIME=$totalCount"
foreach ($k in $categoryPatterns.Keys) {
    Write-Output "$k : $($categoryPatterns[$k].Count)"
}

Write-Output "`n===CONVERSION_PATTERNS==="
$conversionPatterns = @{}
foreach ($err in $categoryPatterns["no_viable_conversion"]) {
    if ($err -match "no viable conversion from '([^']+)' to '([^']+)'") {
        $key = "$($matches[1]) -> $($matches[2])"
        if (-not $conversionPatterns.ContainsKey($key)) { $conversionPatterns[$key] = 0 }
        $conversionPatterns[$key]++
    }
}
$conversionPatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Output "$($_.Key): $($_.Value)"
}

Write-Output "`n===OVERLOADED_PATTERNS==="
$overloadedPatterns = @{}
foreach ($err in $categoryPatterns["no_viable_overloaded"]) {
    if ($err -match "no viable overloaded\s+'=\s*(\w+)'") {
        $target = $matches[1]
        if (-not $overloadedPatterns.ContainsKey($target)) { $overloadedPatterns[$target] = 0 }
        $overloadedPatterns[$target]++
    }
}
$overloadedPatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 15 | ForEach-Object {
    Write-Output "$($_.Key): $($_.Value)"
}

Write-Output "`n===FILE_PATTERNS==="
$filePatterns = @{}
foreach ($err in $allErrors) {
    if ($err -match "^([^:]+):") {
        $fname = $matches[1]
        if (-not $filePatterns.ContainsKey($fname)) { $filePatterns[$fname] = 0 }
        $filePatterns[$fname]++
    }
}
$filePatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Output "$($_.Key): $($_.Value)"
}
