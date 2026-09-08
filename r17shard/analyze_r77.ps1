$logFiles = @(
    "E:\il2cpp\r77shard\r77shard1_shard1_errors.log",
    "E:\il2cpp\r77shard\r77shard2_shard2_errors.log",
    "E:\il2cpp\r77shard\r77shard3_shard3_errors.log"
)

$errorPatterns = @(
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
    'which are not related'
)

$counts = @{}
$examples = @{}
$totalErrors = 0

foreach ($logFile in $logFiles) {
    if (-not (Test-Path $logFile)) { Write-Host "MISSING: $logFile"; continue }
    Write-Host "Processing: $logFile"
    
    $lines = Get-Content $logFile
    $i = 0
    while ($i -lt $lines.Count) {
        $line = $lines[$i]
        if ($line -match '1 error generated') {
            $totalErrors++
            # Find the error line (look backwards for the "error:" line)
            $j = $i - 1
            while ($j -ge 0 -and $lines[$j] -notmatch 'error:') { $j-- }
            if ($j -ge 0) {
                $errorLine = $lines[$j]
                # Extract the error message
                if ($errorLine -match 'error:\s*(.*)') {
                    $msg = $Matches[1]
                    # Categorize
                    $categorized = $false
                    foreach ($pat in $errorPatterns) {
                        if ($msg -match [regex]::Escape($pat)) {
                            if (-not $counts.ContainsKey($pat)) {
                                $counts[$pat] = 0
                                $examples[$pat] = @()
                            }
                            $counts[$pat]++
                            if ($examples[$pat].Count -lt 5) {
                                $examples[$pat] += $errorLine
                            }
                            $categorized = $true
                            break
                        }
                    }
                    if (-not $categorized) {
                        if (-not $counts.ContainsKey('other')) {
                            $counts['other'] = 0
                            $examples['other'] = @()
                        }
                        $counts['other']++
                        if ($examples['other'].Count -lt 5) {
                            $examples['other'] += $errorLine
                        }
                    }
                }
            }
        }
        $i++
    }
}

Write-Host ""
Write-Host "=== TOTAL ERRORS: $totalErrors ==="
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
