$nvErrors1 = Select-String -Path "E:\il2cpp\r77shard\r77shard1_shard1_errors.log" -Pattern "no viable"
$nvErrors2 = Select-String -Path "E:\il2cpp\r77shard\r77shard2_shard2_errors.log" -Pattern "no viable"
$nvErrors3 = Select-String -Path "E:\il2cpp\r77shard\r77shard3_shard3_errors.log" -Pattern "no viable"

$allErrors = @()
$allErrors += $nvErrors1 | ForEach-Object { $_.Line }
$allErrors += $nvErrors2 | ForEach-Object { $_.Line }
$allErrors += $nvErrors3 | ForEach-Object { $_.Line }

$totalCount = $allErrors.Count
Write-Output "Total no viable errors: $totalCount"
Write-Output "Shard 1: $($nvErrors1.Count)"
Write-Output "Shard 2: $($nvErrors2.Count)"
Write-Output "Shard 3: $($nvErrors3.Count)"

$overloaded = @()
$conversion = @()
foreach ($err in $allErrors) {
    if ($err -like "*no viable overloaded*") { $overloaded += $err }
    elseif ($err -like "*no viable conversion from*") { $conversion += $err }
}

Write-Output "no viable overloaded '=' : $($overloaded.Count)"
Write-Output "no viable conversion from : $($conversion.Count)"

$typePatterns = @{}
foreach ($err in $conversion) {
    if ($err -match "no viable conversion from '([^']+)' to '([^']+)'") {
        $fromType = $matches[1]
        $toType = $matches[2]
        $key = "$fromType -> $toType"
        if (-not $typePatterns.ContainsKey($key)) {
            $typePatterns[$key] = 0
        }
        $typePatterns[$key]++
    }
}

Write-Output "`nTop 20 conversion type mismatches:"
$typePatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Output "$($_.Key): $($_.Value)"
}

$filePatterns = @{}
foreach ($err in $allErrors) {
    if ($err -match "^([^:]+):") {
        $fname = $matches[1]
        if (-not $filePatterns.ContainsKey($fname)) {
            $filePatterns[$fname] = 0
        }
        $filePatterns[$fname]++
    }
}

Write-Output "`nTop 20 files with most nv errors:"
$filePatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Output "$($_.Key): $($_.Value)"
}
