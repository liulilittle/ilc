# sweep-exclude.ps1 -- shared reader for scripts/sweep-exclude.txt.
#
# sweep-exclude.txt is the single source of truth for case directories under tests/cases/
# that are NOT byte-exact difftest cases (W7 whitelist, RUNTIME-CONTRACT probes, LOUD
# contract cases, incomplete WIP assets). sweep-cases.ps1 already filtered the full-pool
# sweep with it; difftest.ps1 and run-tests.ps1 did not, so invoking them directly on such
# a case produced a permanent false FAIL that hides real regressions.
#
# Parsing is IDENTICAL to the original sweep-cases.ps1:39-46 block (Trim, drop empty and
# '^#' lines, first token of -split '\s+'), so every consumer sees exactly the same set.
# This function lives in a separate file so sweep-cases.ps1, difftest.ps1 and run-tests.ps1
# share one implementation instead of three hand-copied parsers.
#
# Line format: "<case>" or "<case>  # <reason>"; the text after '#' is the audit reason.

$script:SweepExcludePath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "sweep-exclude.txt"

function Get-SweepExcludeEntries {
    param([string]$Path = $script:SweepExcludePath)
    $entries = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path -LiteralPath $Path)) { return $entries }
    foreach ($raw in (Get-Content -LiteralPath $Path)) {
        $line = $raw.Trim()
        if (-not $line) { continue }
        if ($line -match '^#') { continue }
        $name = ($line -split '\s+')[0]
        if (-not $name) { continue }
        $reason = ""
        $hash = $line.IndexOf('#')
        if ($hash -ge 0) { $reason = $line.Substring($hash + 1).Trim() }
        $entries.Add([pscustomobject]@{ Case = $name; Reason = $reason; Line = $line })
    }
    return $entries
}

function Get-SweepExcludedCaseNames {
    param([string]$Path = $script:SweepExcludePath)
    return @((Get-SweepExcludeEntries -Path $Path) | ForEach-Object { $_.Case })
}

function Find-SweepExcludeEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Case,
        [string]$Path = $script:SweepExcludePath
    )
    foreach ($e in (Get-SweepExcludeEntries -Path $Path)) {
        if ($e.Case -ceq $Case) { return $e }
    }
    return $null
}
