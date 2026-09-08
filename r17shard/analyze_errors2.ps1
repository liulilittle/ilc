$logFiles = @(
    "E:\il2cpp\r77shard\r77shard1_shard1_errors.log",
    "E:\il2cpp\r77shard\r77shard2_shard2_errors.log",
    "E:\il2cpp\r77shard\r77shard3_shard3_errors.log"
)

$patterns = @(
    'no matching function',
    'cannot initialize',
    'assigning to',
    'cannot cast',
    'no viable overloaded',
    'no member named',
    'no viable conversion',
    'undeclared identifier',
    'incompatible type',
    'unknown type name',
    'reinterpret_cast from',
    'incompatible pointer types',
    'static_cast from',
    'cannot convert',
    'invalid operands',
    'redefinition',
    'base class',
    'inheritance',
    'derived',
    'reinterpret_cast to',
    'incompatible pointer to integer'
)

$counts = @{}
$examples = @{}

foreach ($logFile in $logFiles) {
    if (-not (Test-Path $logFile)) { Write-Host "MISSING: $logFile"; continue }
    Write-Host "Processing: $logFile"
    Get-Content $logFile | ForEach-Object {
        $line = $_
        foreach ($pat in $patterns) {
            if ($line -match [regex]::Escape($pat)) {
                if (-not $counts.ContainsKey($pat)) {
                    $counts[$pat] = 0
                    $examples[$pat] = @()
                }
                $counts[$pat]++
                if ($examples[$pat].Count -lt 5) {
                    $examples[$pat] += $line
                }
            }
        }
    }
}

Write-Host ""
Write-Host "=== ERROR PATTERN COUNTS ==="
$counts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host "$($_.Key): $($_.Count)"
}

Write-Host ""
Write-Host "=== DETAILED EXAMPLES ==="
$counts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host ""
    Write-Host "=== $($_.Key) ($($_.Count)) ==="
    $examples[$_.Key] | ForEach-Object { Write-Host "  $_" }
}
