$nvErrors1 = Select-String -Path "E:\il2cpp\r77shard\r77shard1_shard1_errors.log" -Pattern "no viable"
$nvErrors2 = Select-String -Path "E:\il2cpp\r77shard\r77shard2_shard2_errors.log" -Pattern "no viable"
$nvErrors3 = Select-String -Path "E:\il2cpp\r77shard\r77shard3_shard3_errors.log" -Pattern "no viable"

$allErrors = @()
$allErrors += $nvErrors1 | ForEach-Object { $_.Line }
$allErrors += $nvErrors2 | ForEach-Object { $_.Line }
$allErrors += $nvErrors3 | ForEach-Object { $_.Line }

$totalCount = $allErrors.Count

$overloaded = @()
$conversion = @()
foreach ($err in $allErrors) {
    if ($err -like "*no viable overloaded*") { $overloaded += $err }
    elseif ($err -like "*no viable conversion from*") { $conversion += $err }
}

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

$filePatterns = @{}
foreach ($err in $allErrors) {
    if ($err -match "^(E:\\il2cpp\\out\\mscorlib_gen_r77\\src\\[^\\]+\.cpp):") {
        $fname = $matches[1].Replace("E:\il2cpp\out\mscorlib_gen_r77\src\", "")
        if (-not $filePatterns.ContainsKey($fname)) {
            $filePatterns[$fname] = 0
        }
        $filePatterns[$fname]++
    }
}

$overloadedPatterns = @{}
foreach ($err in $overloaded) {
    if ($err -match "no viable overloaded '=(\w+)'") {
        $target = $matches[1]
        if (-not $overloadedPatterns.ContainsKey($target)) {
            $overloadedPatterns[$target] = 0
        }
        $overloadedPatterns[$target]++
    }
}

Write-Output "TOTAL_NV=$totalCount"
Write-Output "OVERLOADED=$($overloaded.Count)"
Write-Output "CONVERSION=$($conversion.Count)"

Write-Output "---TOP_CONVERSION---"
$typePatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 25 | ForEach-Object {
    Write-Output "$($_.Key)|$($_.Value)"
}

Write-Output "---TOP_FILES---"
$filePatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 25 | ForEach-Object {
    Write-Output "$($_.Key)|$($_.Value)"
}

Write-Output "---OVERLOADED_TARGETS---"
$overloadedPatterns.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 15 | ForEach-Object {
    Write-Output "$($_.Key)|$($_.Value)"
}
