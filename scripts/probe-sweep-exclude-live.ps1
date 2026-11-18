<#
.SYNOPSIS
    Run three excluded cases through the FULL byte-exact difftest path with the
    exclusion temporarily neutralised, to find out whether their exclusion is
    still earned.

.DESCRIPTION
    sweep-exclude.txt makes difftest.ps1 early-return SKIP. The only reliable way
    to tell whether an entry is still justified is to remove it and run the real
    comparison: a case that now passes byte-exactly is a stale exclusion that has
    been hiding a green case (and, on the next regression, would hide a RED one
    just as quietly).

    This script:
      1. reads sweep-exclude.txt,
      2. writes a sibling copy with the test entries commented out,
      3. runs difftest.ps1 for each test case with that copy in place,
      4. restores the original file in a finally block,
      5. prints one verdict per case.

    The original file is restored even if difftest fails, and the byte content is
    verified equal afterwards.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/probe-sweep-exclude-live.ps1
#>
param(
    [string[]]$Cases = @("probe-grp-lazy-rs", "p-stacktrace", "p-adb-loud"),
    [string]$Compiler = "mingw"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$excludePath = Join-Path $Root "scripts/sweep-exclude.txt"

$original = [System.IO.File]::ReadAllBytes($excludePath)
$originalHash = (Get-FileHash -LiteralPath $excludePath -Algorithm SHA256).Hash

# Build the neutralised copy: comment out exactly the test cases' lines.
$text = [System.IO.File]::ReadAllText($excludePath)
$lines = $text -split "`r`n|`n"
$out = New-Object System.Collections.Generic.List[string]
foreach ($line in $lines) {
    $t = $line.Trim()
    $first = if ($t -ne "" -and -not $t.StartsWith("#")) { ($t -split '\s+')[0] } else { "" }
    if ($first -ne "" -and ($Cases -contains $first)) {
        $out.Add("# NEUTRALISED FOR AUDIT: $line")
    } else {
        $out.Add($line)
    }
}
$verdicts = @()
try {
    [System.IO.File]::WriteAllText($excludePath, ($out -join "`r`n"))

    foreach ($case in $Cases) {
        $log = Join-Path $Root ".opencode/tmp/sweepaudit/$case.diff.log"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "difftest.ps1") `
            -Case $case -Compiler $Compiler *> $log
        $code = $LASTEXITCODE
        $tail = (Get-Content -LiteralPath $log -ErrorAction SilentlyContinue | Select-Object -Last 12) -join "`n"
        $verdict = switch ($code) {
            0 { "GREEN-UNEXPECTED  (the exclusion is stale: this case passes byte-exactly)" }
            default { "RED-AS-EXPECTED    (the exclusion is still earned)" }
        }
        $verdicts += [pscustomobject]@{ Case = $case; Exit = $code; Verdict = $verdict; Log = $log; Tail = $tail }
    }
}
finally {
    [System.IO.File]::WriteAllBytes($excludePath, $original)
}

$restoredHash = (Get-FileHash -LiteralPath $excludePath -Algorithm SHA256).Hash
if ($restoredHash -ne $originalHash) {
    throw "sweep-exclude.txt was NOT restored (hash mismatch: $originalHash -> $restoredHash)"
}

"=== live sweep-exclude audit ($($verdicts.Count) cases) ==="
foreach ($v in $verdicts) {
    "exit={0,-4} {1}" -f $v.Exit, $v.Case
    "       $($v.Verdict)"
}
""
"sweep-exclude.txt restored (sha256 $($originalHash.Substring(0,16))...)"

$stale = @($verdicts | Where-Object { $_.Exit -eq 0 })
if ($stale.Count -gt 0) {
    ""
    "STALE EXCLUSIONS: $($stale.Count)"
    foreach ($s in $stale) { "  - $($s.Case) (log: $($s.Log))" }
    exit 1
}
exit 0
