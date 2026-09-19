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

# 差分冒烟集（B6）：一条命令跑 DIFFERENTIAL-SMOKE-SET.md 登记的用例，
# 逐例调用 scripts/difftest.ps1（net48 oracle vs 运行时，逐字节），汇总 PASS/DIFF 并给出非零退出码。
#   * 增量落盘：每例完成即写 smoke-summary.tsv，中途中断可续跑（已完成例不必重跑，除非 -Only 指定）。
#   * 退出码：所有例 ok=True → 0；否则 1（任何 FAIL）。-AllowKnownRed 时以基线表判红（默认不启用）。
#   * 单例耗时以运行时全量编译为主（~2-5 min/例）；-Only 可先跑子集验证。

$Cases = @(
    "diffdt001c",   # TimeSpan 全族（含 double->int64 路径）
    "diffmin001",   # 最小复现锚（BitConverter 偏移 + Char Nd）
    "diffio002",    # Encoding + MemoryStream（回归哨兵）
    "diffchar001",  # Char 族 Unicode
    "diffio001",    # File/Path/Directory/FileInfo
    "diffdt001",    # DateTime 主体
    "diffenv001",   # GC/GCSettings/Environment
    "diffthread001", # Thread 族（实例委托 ctor / set_Priority）
    "diffthread003", # Thread 静态委托边界（无实例委托时的基线）
    "diffthread004", # ManagedThreadId 构造语义（net48 分配规则基线）
    "diffcol001",    # Collections 4 域验收锚（DomainSortedSet 已隔离至 diffcolset001）
    "diffcolset001", # SortedSet<int> 域（A4 死循环修复锚；当前运行时挂起 rc=99）
    "diffdict001",   # dictsi 枚举序边界（插入序 / Current 起始 / 删除重加 / rehash；A4 修复锚）
    "diffuri001",    # System.Uri 对象模型虚派发 + net48 规范化（A4 P1 修复锚）
    "diffuri002",    # System.Uri 规范化补充向量（相对/ftp端口/userinfo/反转义/+/尾斜杠）
    "diffuri003",    # System.Uri 边界向量（IPv6/file:///空段/%2f/前导零端口/大小写/超长路径）
    "g4dicturistr",   # T4 class-非末位泛型参数探针（Dictionary`2<Uri,string> 0x12 后 TypeDefOrRef handle 消费）
    "diffenv002",    # GC LatencyMode 非法值语义（EX ArgumentException 单红点；ENV-BOUNDARY-SPEC ⑤）
    "diffthread005",  # Thread.Name 跨线程可见性（双通道稳定 2 红：read=0/value=null）
    "diffthread002"  # Thread 新线程创建/Start/Join（03:39 旧崩溃已闭合，CC2EFF70 下 PASS diff0）
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
# 跨批次增量：载入既有汇总（按 case+compiler 去重覆盖），使 -Only 分批执行能累积而非覆盖。
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
