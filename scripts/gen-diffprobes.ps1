param(
    [string]$A6Dir = "",
    [string]$Domain = "",
    [string]$Priority = "",
    [string]$Out = "",
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $A6Dir) { $A6Dir = "$Root/final-report" }
if (-not $Out)   { $Out = "$Root/.opencode/tmp/diffharness/manifest.tsv" }

# Differential probe manifest generator (A3): input = A6 apiscan/entries.tsv (=MethodMap mapping list) INTERSECT
# an actual `case "Name"` label scan INTERSECT the P0 order of triage-top20.tsv; output manifest.tsv:
# domain owner name wired tfm priority class vectors source rank.
#   * wired   = the name appears in some MethodMap's `case "..."` label (including .ctor / op_* / get_*).
#   * domain  = the source MethodMap file (the file column of entries.tsv, stripping the MethodMap. prefix and .cs suffix).
#   * priority= `P0#<rank>` when it hits triage-top20, otherwise `PMAP` (`PMAP-pending-review` when A6 is missing).
#   * tfm     = net48 default; vectors = built-in shape hints; class = pure (batch authors change to io/effectful as needed).
# When A6 is missing, degrade to source=methodmap and mark priority as the pending-review literal (do not silently pass it off).
# Note: this script contains Chinese comments and must be written as UTF-8+BOM (otherwise PS 5.1 parses it as GBK and errors -- F66).

function Get-MethodMapWired {
    $map = @{}
    Get-ChildItem "$Root/src/il2cppc/Emit" -Filter 'MethodMap*.cs' | Where-Object { $_.Name -notmatch 'Test' } | ForEach-Object {
        $text = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
        foreach ($m in [regex]::Matches($text, 'case\s+"([A-Za-z0-9_.]+)"')) {
            $n = $m.Groups[1].Value
            if (-not $map.ContainsKey($n)) { $map[$n] = New-Object System.Collections.Generic.List[string] }
            $dom = $_.BaseName -replace '^MethodMap\.', ''
            if (-not $map[$n].Contains($dom)) { $map[$n].Add($dom) }
        }
    }
    return $map
}

function Get-VectorHint([string]$name, [string]$domain) {
    if ($name -eq '.ctor') { return 'ctor,instance-apis' }
    if ($name -match '^(ToInt|ToUInt|ToSingle|ToDouble|ToInt64|ToUInt64|ToInt16|ToUInt16|ToBoolean|ToChar|GetBytes|SingleToInt32Bits|Int32BitsToSingle)$') { return 'normal,boundary,nalign,error' }
    if ($name -match '^(Parse|TryParse)$') { return 'normal,boundary,format,error' }
    if ($name -match '^(ToString|ToBase64String)$') { return 'normal,empty,pad,format,error' }
    if ($name -match '^FromBase64String$') { return 'normal,whitespace,pad,error' }
    if ($name -match '^(IsDigit|IsLetter|IsLetterOrDigit|IsWhiteSpace|IsUpper|IsLower|IsPunctuation|IsControl|IsSeparator|IsSymbol|IsSurrogate|IsHighSurrogate|IsLowSurrogate|ToUpper|ToLower|ToUpperInvariant|ToLowerInvariant|GetNumericValue)$') { return 'ascii,latin,ar-indic,cjk,surrogate,edge' }
    if ($name -match '^(Abs|Max|Min|Sign|Sqrt|Pow|Round|Floor|Ceiling|Truncate|DivRem|BigMul|IEEERemainder|Log|Log10|Exp|Sin|Cos|Tan|Atan2)$') { return 'normal,boundary,nan,inf,negzero' }
    if ($name -match '^(BlockCopy|GetByte|SetByte|ByteLength)$') { return 'normal,overlap,empty,error' }
    if ($name -match '^(GetString|GetChars|GetBytes|get_UTF8|get_ASCII|get_Unicode|get_BigEndianUnicode)$') { return 'ascii,latin,cjk,bom,empty' }
    if ($domain -eq 'IO' -or $domain -eq 'StreamIO' -or $domain -eq 'BinaryIO') { return 'normal,eof,error,dispose' }
    return 'normal,boundary,error'
}

$wired = Get-MethodMapWired
$rank = @{}
$rankFile = Join-Path $A6Dir 'apiscan/triage-top20.tsv'
if (Test-Path $rankFile) {
    $rl = [System.IO.File]::ReadAllLines($rankFile, [System.Text.Encoding]::UTF8)
    for ($i = 1; $i -lt $rl.Count; $i++) {
        if (-not $rl[$i].Trim()) { continue }
        $c = $rl[$i] -split "`t"
        if ($c.Count -ge 3) { $rank["$($c[1])|$($c[2])"] = $c[0] }
    }
}

$rows = New-Object System.Collections.Generic.List[object]
$entries = Join-Path $A6Dir 'apiscan/entries.tsv'
$haveA6 = Test-Path $entries
$source = if ($haveA6) { 'apiscan' } else { 'methodmap' }
$seen = @{}

if ($haveA6) {
    $lines = [System.IO.File]::ReadAllLines($entries, [System.Text.Encoding]::UTF8)
    $header = $lines[0] -split "`t"
    $idx = @{}
    for ($i = 0; $i -lt $header.Count; $i++) {
        $h = $header[$i].ToLowerInvariant()
        if ($h -eq 'owner') { $idx['owner'] = $i }
        elseif ($h -eq 'method') { $idx['name'] = $i }
        elseif ($h -eq 'file') { $idx['file'] = $i }
    }
    for ($r = 1; $r -lt $lines.Count; $r++) {
        if (-not $lines[$r].Trim()) { continue }
        $c = $lines[$r] -split "`t"
        $name = if ($idx.ContainsKey('name') -and $idx['name'] -lt $c.Count) { $c[$idx['name']] } else { '' }
        $owner = if ($idx.ContainsKey('owner') -and $idx['owner'] -lt $c.Count) { $c[$idx['owner']] } else { '' }
        $file = if ($idx.ContainsKey('file') -and $idx['file'] -lt $c.Count) { $c[$idx['file']] } else { '' }
        if (-not $name) { continue }
        $dom = $file -replace '^MethodMap\.', '' -replace '\.cs$', ''
        $key = "$dom|$owner|$name"
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = 1
        $isWired = $wired.ContainsKey($name)
        $prio = if ($rank.ContainsKey("$owner|$name")) { "P0#$($rank["$owner|$name"])" } else { 'PMAP' }
        $rows.Add([pscustomobject]@{ domain = $dom; owner = $owner; name = $name; wired = $(if ($isWired) { 'yes' } else { 'loud-not-wired' }); tfm = 'net48'; priority = $prio; class = 'pure'; vectors = (Get-VectorHint $name $dom); source = $source; rank = $prio })
    }
} else {
    foreach ($name in ($wired.Keys | Sort-Object)) {
        $rows.Add([pscustomobject]@{ domain = ($wired[$name] -join '|'); owner = ''; name = $name; wired = 'yes'; tfm = 'net48'; priority = 'PMAP-待核'; class = 'pure'; vectors = (Get-VectorHint $name ($wired[$name][0])); source = $source; rank = 'PMAP-待核' })
    }
}

if ($Domain)   { $rows = $rows | Where-Object { $_.domain -match $Domain } }
if ($Priority) { $rows = $rows | Where-Object { $_.priority -match $Priority } }

$rows = $rows | Sort-Object @{ Expression = { if ($_.rank -match '^P0#(\d+)$') { [int]$Matches[1] } else { 9999 } } }, domain, owner, name

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$outLines = New-Object System.Collections.Generic.List[string]
$outLines.Add("domain`towner`tname`twired`ttfm`tpriority`tclass`tvectors`tsource")
foreach ($r in $rows) { $outLines.Add("$($r.domain)`t$($r.owner)`t$($r.name)`t$($r.wired)`t$($r.tfm)`t$($r.priority)`t$($r.class)`t$($r.vectors)`t$($r.source)") }
[System.IO.File]::WriteAllLines($Out, $outLines, (New-Object System.Text.UTF8Encoding($true)))

if (-not $Quiet) {
    Write-Host "A6 entries.tsv: $(if ($haveA6) { $entries } else { 'MISSING (degraded: source=methodmap)' })"
    Write-Host "wired case-labels: $($wired.Keys.Count); rows: $(@($rows).Count) -> $Out"
    $byDom = $rows | Group-Object domain | Sort-Object Count -Descending | Select-Object -First 10
    foreach ($g in $byDom) { Write-Host ("  {0,-24} {1}" -f $g.Name, $g.Count) }
    $p0 = @($rows | Where-Object { $_.rank -match '^P0#' })
    Write-Host "P0 rows: $($p0.Count)"
    foreach ($r in $p0) { Write-Host ("  {0,-8} {1,-45} {2}" -f $r.rank, $r.owner, $r.name) }
}
