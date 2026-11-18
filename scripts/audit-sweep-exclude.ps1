<#
.SYNOPSIS
    Audit scripts/sweep-exclude.txt: report, for every excluded case, whether the
    exclusion is still EARNED or has silently become obsolete.

.DESCRIPTION
    sweep-exclude.txt makes the byte-exact sweep skip a case entirely
    (difftest.ps1 early-returns SKIP). The mechanism is sound while the entry is
    true; the failure mode is an entry that outlives its reason -- the case may
    have turned green (or the case directory may be gone) and nobody notices,
    because a SKIP is never a failure.

    This audit answers one question per entry: is the exclusion still justified?

    Categories:
      MISSING      the case directory does not exist -- the name drifted or the
                   case was renamed/removed. MUST be fixed in the list.
      LOUD         the entry's note marks a LOUD contract, and loud-contracts.txt
                   lists the same case. Verified by loud-gate.ps1, not by the
                   byte-exact sweep. Earned by construction.
      RED-ANCHOR   the entry's note marks an intentional red anchor (a registered
                   scope gap). Earned while the gap is open -- this audit cannot
                   decide that, so it reports the entry and its reason.
      UNVERIFIED   no loud/RED-anchor marker: the exclusion rests on a case-specific
                   reason that needs a byte-exact run WITHOUT the exclusion to
                   confirm. Reported as needing a manual call, not as OK.

    Exit code: 0 when every entry is categorised without a MISSING case,
    1 when any entry is MISSING (a list that points at nothing is always wrong).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts/audit-sweep-exclude.ps1
#>
param(
    [string]$Root = "",
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }

$excludePath = Join-Path $Root "scripts/sweep-exclude.txt"
$loudPath    = Join-Path $Root "scripts/loud-contracts.txt"

if (-not (Test-Path $excludePath)) { throw "missing $excludePath" }

# The SAME parser the three consumers use (sweep-exclude.ps1 contract):
# trim, drop empty and '^#' lines, first token of -split '\s+'.
function Read-EntryList([string]$path) {
    $rows = @()
    foreach ($line in (Get-Content -LiteralPath $path)) {
        $t = $line.Trim()
        if ($t -eq "" -or $t.StartsWith("#")) { continue }
        $tokens = $t -split '\s+'
        $rows += [pscustomobject]@{
            Name  = $tokens[0]
            Note  = ($tokens | Select-Object -Skip 1) -join ' '
            Line  = $line
        }
    }
    return $rows
}

$excluded = Read-EntryList $excludePath
$loud     = @{}
if (Test-Path $loudPath) {
    foreach ($e in (Read-EntryList $loudPath)) { $loud[$e.Name] = $true }
}

$results = @()
foreach ($e in $excluded) {
    $dir = Join-Path $Root "tests/cases/$($e.Name)"
    $note = $e.Note
    if (-not (Test-Path $dir)) {
        $cat = "MISSING"
        $why = "tests/cases/$($e.Name) does not exist"
    }
    elseif ($note -match 'LOUD contract') {
        if ($loud.ContainsKey($e.Name)) {
            $cat = "LOUD"
            $why = "loud-contracts.txt lists it; loud-gate.ps1 verifies the rejection"
        } else {
            $cat = "LOUD-UNREGISTERED"
            $why = "note claims a LOUD contract but loud-contracts.txt does not list it"
        }
    }
    elseif ($note -match 'RED|SCOPE GAP|W7 whitelist|RUNTIME-CONTRACT') {
        $cat = "RED-ANCHOR"
        $why = $note
    }
    else {
        $cat = "UNVERIFIED"
        $why = "no loud/RED marker: needs a byte-exact run with the entry removed"
    }
    $results += [pscustomobject]@{
        Name = $e.Name
        Category = $cat
        Why = $why
    }
}

if (-not $Quiet) {
    "=== sweep-exclude audit ($($results.Count) entries) ==="
    foreach ($r in $results) {
        "{0,-20} {1}" -f $r.Category, $r.Name
        "                     $($r.Why)"
    }
    ""
    "=== summary ==="
    $results | Group-Object Category | Sort-Object Name | ForEach-Object {
        "{0,-20} {1}" -f $_.Name, $_.Count
    }
}

$bad = @($results | Where-Object { $_.Category -eq "MISSING" -or $_.Category -eq "LOUD-UNREGISTERED" })
if ($bad.Count -gt 0) {
    ""
    "FAIL: $($bad.Count) entry/entries are wrong regardless of the sweep:"
    foreach ($b in $bad) { "  - $($b.Name): $($b.Why)" }
    exit 1
}
exit 0
