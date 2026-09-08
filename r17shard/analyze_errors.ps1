$logFiles = @(
    "E:\il2cpp\r77shard\r77shard1_shard1_errors.log",
    "E:\il2cpp\r77shard\r77shard2_shard2_errors.log",
    "E:\il2cpp\r77shard\r77shard3_shard3_errors.log"
)

$patterns = @(
    'incompatible pointer types',
    'no member named',
    'unknown type name',
    'no viable conversion',
    'no matching function',
    'undeclared identifier',
    'incompatible type',
    'assigning to',
    'static_cast from',
    'reinterpret_cast from',
    'reinterpret_cast to',
    'invalid operands',
    'no viable overloaded',
    'redefinition',
    'cannot convert',
    'cannot cast',
    'cannot initialize',
    'base class',
    'inheritance',
    'derived'
)

$counts = @{}
$examples = @{}

foreach ($logFile in $logFiles) {
    if (-not (Test-Path $logFile)) { continue }
    Get-Content $logFile | ForEach-Object {
        $line = $_
        foreach ($pat in $patterns) {
            if ($line -match $pat) {
                if (-not $counts.ContainsKey($pat)) {
                    $counts[$pat] = 0
                    $examples[$pat] = @()
                }
                $counts[$pat]++
                if ($examples[$pat].Count -lt 3) {
                    $examples[$pat] += $line
                }
            }
        }
    }
}

$counts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host "=== $($_.Key) ($($_.Count)) ==="
    $examples[$_.Key] | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
}
