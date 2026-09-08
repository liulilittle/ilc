$logFiles = @(
    "E:\il2cpp\r77shard\r77shard1_shard1_errors.log",
    "E:\il2cpp\r77shard\r77shard2_shard2_errors.log",
    "E:\il2cpp\r77shard\r77shard3_shard3_errors.log"
)

$patterns = @(
    'redefinition',
    'no member named',
    'unknown type name',
    'undeclared identifier',
    'incompatible pointer types',
    'incompatible pointer to integer',
    'incompatible type',
    'assigning to',
    'no viable conversion',
    'no viable overloaded',
    'no matching function',
    'cannot convert',
    'cannot cast',
    'cannot initialize',
    'static_cast from',
    'reinterpret_cast from',
    'reinterpret_cast to',
    'invalid operands',
    'array type',
    'base class',
    'inheritance',
    'derived',
    'which are not related',
    'ambiguous',
    'too few arguments',
    'too many arguments'
)

$counts = @{}
$totalErrors = 0

foreach ($logFile in $logFiles) {
    if (-not (Test-Path $logFile)) { Write-Host "MISSING: $logFile"; continue }
    Write-Host "Processing: $logFile"
    
    Get-Content $logFile | ForEach-Object {
        if ($_ -match 'error:\s*(.*)') {
            $totalErrors++
            $msg = $Matches[1]
            $categorized = $false
            foreach ($pat in $patterns) {
                if ($msg -like "*$pat*") {
                    if (-not $counts.ContainsKey($pat)) { $counts[$pat] = 0 }
                    $counts[$pat]++
                    $categorized = $true
                    break
                }
            }
            if (-not $categorized) {
                if (-not $counts.ContainsKey('other')) { $counts['other'] = 0 }
                $counts['other']++
            }
        }
    }
}

Write-Host ""
Write-Host "=== TOTAL ERRORS: $totalErrors ==="
Write-Host ""
Write-Host "=== ERROR PATTERN COUNTS ==="
$counts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host "$($_.Key): $($_.Count)"
}
