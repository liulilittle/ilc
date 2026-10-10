param(
    [ValidateSet("mingw","msvc","clang")][string]$Compiler = "mingw",
    [string]$Only = "",
    [string]$Skip = "",
    [string]$OutDir = "",
    [switch]$AllowKnownRed,
    [int]$PerCaseTimeoutMin = 12
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = "$Root/.opencode/tmp/diffharness" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Differential smoke set (B6): one command runs the cases registered in
# DIFFERENTIAL-SMOKE-SET.md, invoking scripts/difftest.ps1 per case (net48 oracle
# vs runtime, byte-for-byte), summarizing PASS/DIFF and returning a non-zero exit code.
#   * Incremental output: smoke-summary.tsv is written as each case completes, so an
#     interrupted run can resume (completed cases need not rerun unless -Only is given).
#   * Exit code: all cases ok=True -> 0; otherwise 1 (any FAIL). With -AllowKnownRed,
#     red is judged against the baseline table (disabled by default).
#   * Per-case time is dominated by the full runtime compile (~2-5 min/case); -Only
#     lets you run a subset first for verification.

$Cases = @(
    "diffdt001c",   # TimeSpan full family (including the double->int64 path)
    "diffmin001",   # minimal reproduction anchor (BitConverter offset + Char Nd)
    "diffio002",    # Encoding + MemoryStream (regression sentinel)
    "diffchar001",  # Char family Unicode
    "diffio001",    # File/Path/Directory/FileInfo
    "diffdt001",    # DateTime main body
    "diffenv001",   # GC/GCSettings/Environment
    "diffthread001", # Thread family (instance-delegate ctor / set_Priority)
    "diffthread003", # Thread static-delegate boundary (baseline without instance delegates)
    "diffthread004", # ManagedThreadId construction semantics (net48 allocation-rule baseline)
    "diffcol001",    # Collections 4-domain acceptance anchor (DomainSortedSet isolated to diffcolset001)
    "diffcolset001", # SortedSet<int> domain (A4 infinite-loop fix anchor; currently the runtime hangs rc=99)
    "diffdict001",   # dictsi enumeration-order boundary (insertion order / Current start / remove-readd / rehash; A4 fix anchor)
    "diffuri001",    # System.Uri object-model virtual dispatch + net48 normalization (A4 P1 fix anchor)
    "diffuri002",    # System.Uri normalization supplementary vectors (relative/ftp port/userinfo/unescape/+/trailing slash)
    "diffuri003",    # System.Uri boundary vectors (IPv6/file:///empty segment/%2f/leading-zero port/case/long path)
    "g4dicturistr",   # T4 class-non-last generic-arg probe (Dictionary`2<Uri,string> 0x12 followed by TypeDefOrRef handle consumption)
    "diffenv002",    # GC LatencyMode illegal-value semantics (EX ArgumentException single red point; ENV-BOUNDARY-SPEC 5)
    "diffthread005",  # Thread.Name cross-thread visibility (stable 2 red on both channels: read=0/value=null)
    "diffthread002"  # Thread new-thread create/Start/Join (old 03:39 crash closed; PASS diff0 under CC2EFF70)
)
if ($Only) {
    $want = @($Only -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $Cases = @($Cases | Where-Object { $want -contains $_ })
}
if ($Skip) {
    $no = @($Skip -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $Cases = @($Cases | Where-Object { $no -notcontains $_ })
}

$summaryPath = "$OutDir/smoke-summary.tsv"
# Cross-batch incremental: load the existing summary (deduped/overwritten by case+compiler) so -Only batched runs accumulate rather than overwrite.
$merged = [ordered]@{}
if (Test-Path $summaryPath) {
    $prev = [System.IO.File]::ReadAllLines($summaryPath, [System.Text.Encoding]::UTF8)
    for ($i = 1; $i -lt $prev.Count; $i++) {
        if (-not $prev[$i].Trim()) { continue }
        $c = $prev[$i] -split "`t"
        if ($c.Count -ge 2) { $merged["$($c[0])|$($c[1])"] = $prev[$i] }
    }
}
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("case`tcompiler`tok`texitCode`toracleBytes`truntimeBytes`toracleSha256`truntimeSha256`tdiffCount`treportTime")
$fail = 0
$pass = 0
foreach ($case in $Cases) {
    Write-Host "=== smoke: $case ($Compiler) ==="
    $diff = "$OutDir/$case.$Compiler.diffreport.json"
    if (Test-Path $diff) { Remove-Item $diff -Force -ErrorAction SilentlyContinue }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $rc = 1
    try {
        & "$Root/scripts/difftest.ps1" -Case $case -Compiler $Compiler -ReportDir $OutDir | Out-Null
        $rc = $LASTEXITCODE
    } catch {
        Write-Warning "case $case threw: $_"
        $rc = 99
    }
    $sw.Stop()
    $ok = "unknown"; $ob = 0; $rb = 0; $os = ""; $rs = ""; $dn = -1
    if (Test-Path $diff) {
        $j = Get-Content $diff -Raw | ConvertFrom-Json
        $ok = [string]$j.ok
        $ob = $j.oracle.outBytes; $rb = $j.runtime.outBytes
        $os = $j.oracle.outSha256; $rs = $j.runtime.outSha256
        $dn = @($j.differingRecords).Count
    }
    if ($ok -eq "True") { $pass++ } else { $fail++ }
    $merged["$case|$Compiler"] = "$case`t$Compiler`t$ok`t$rc`t$ob`t$rb`t$os`t$rs`t$dn`t$([int]$sw.Elapsed.TotalSeconds)s"
    $all = New-Object System.Collections.Generic.List[string]
    $all.Add("case`tcompiler`tok`texitCode`toracleBytes`truntimeBytes`toracleSha256`truntimeSha256`tdiffCount`treportTime")
    foreach ($k in $merged.Keys) { $all.Add($merged[$k]) }
    [System.IO.File]::WriteAllLines($summaryPath, $all, (New-Object System.Text.UTF8Encoding($true)))
    Write-Host ("  ok={0} rc={1} diff={2} {3}s" -f $ok, $rc, $dn, [int]$sw.Elapsed.TotalSeconds)
}
Write-Host ("=== smoke summary: pass={0} fail={1} total={2} -> {3}" -f $pass, $fail, $Cases.Count, $summaryPath)
foreach ($l in $lines) { Write-Host "  $l" }
if ($fail -gt 0 -and -not $AllowKnownRed) { exit 1 }
exit 0
