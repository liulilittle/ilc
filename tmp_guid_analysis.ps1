$guidErrors1 = Select-String -Path "E:\il2cpp\r77shard\r77shard1_shard1_errors.log" -Pattern "Guid|GUID"
$guidErrors2 = Select-String -Path "E:\il2cpp\r77shard\r77shard2_shard2_errors.log" -Pattern "Guid|GUID"
$guidErrors3 = Select-String -Path "E:\il2cpp\r77shard\r77shard3_shard3_errors.log" -Pattern "Guid|GUID"

$allErrors = @()
$allErrors += $guidErrors1 | ForEach-Object { $_.Line }
$allErrors += $guidErrors2 | ForEach-Object { $_.Line }
$allErrors += $guidErrors3 | ForEach-Object { $_.Line }

$totalCount = $allErrors.Count

$categoryPatterns = @{
    "no_match_for_call" = @()
    "no_viable_overloaded" = @()
    "no_viable_conversion" = @()
    "cannot_cast" = @()
    "cannot_take_address" = @()
    "other" = @()
}

foreach ($err in $allErrors) {
    if ($err -like "*no matching function for call*") { $categoryPatterns["no_match_for_call"] += $err }
    elseif ($err -like "*no viable overloaded*") { $categoryPatterns["no_viable_overloaded"] += $err }
    elseif ($err -like "*no viable conversion*") { $categoryPatterns["no_viable_conversion"] += $err }
    elseif ($err -like "*cannot cast*") { $categoryPatterns["cannot_cast"] += $err }
    elseif ($err -like "*cannot take the address*") { $categoryPatterns["cannot_take_address"] += $err }
    else { $categoryPatterns["other"] += $err }
}

Write-Output "TOTAL_GUID=$totalCount"
foreach ($k in $categoryPatterns.Keys) {
    Write-Output "$k : $($categoryPatterns[$k].Count)"
}

Write-Output "`n===NO_MATCH_FOR_CALL==="
$categoryPatterns["no_match_for_call"] | Select-Object -First 20 | ForEach-Object { Write-Output $_ }

Write-Output "`n===CANNOT_CAST==="
$categoryPatterns["cannot_cast"] | Select-Object -First 10 | ForEach-Object { Write-Output $_ }

Write-Output "`n===CANNOT_TAKE_ADDRESS==="
$categoryPatterns["cannot_take_address"] | Select-Object -First 10 | ForEach-Object { Write-Output $_ }
