param(
    [Parameter(Mandatory = $true)][string]$List,
    [Parameter(Mandatory = $true)][string]$Out,
    [ValidateSet("mingw", "msvc", "clang")][string]$Compiler = "mingw",
    [int]$BuildParallel = 4,
    [int]$CaseTimeoutMin = 20
)
$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent $PSScriptRoot
$env:CMAKE_BUILD_PARALLEL_LEVEL = "$BuildParallel"
if ($Compiler -eq "mingw") {
    $env:Path = "F:\il2cpp\.opencode\tmp\ccache-shim;D:\Program Files\mingw\msys2\mingw64\bin;D:\android\sdk\cmake\3.22.1\bin;" + $env:Path
    $env:CCACHE_DIR = "F:\il2cpp\.opencode\tmp\ccache"
    $env:CCACHE_MAXSIZE = "30G"
}
if ($Compiler -eq "msvc") {
    # A2 msvcpre 实测：新 shell 无 cmake/g++/msbuild；mylib 用例需 g++（msys2）；
    # CL=/MP2 让 cl.exe 单例内并行（msvc 分支串行构建时的实测加速）。
    $env:Path = "F:\il2cpp\.opencode\tmp\tools\sccache\sccache-v0.17.0-x86_64-pc-windows-msvc;D:\android\sdk\cmake\3.22.1\bin;D:\Program Files\mingw\msys2\mingw64\bin;" + $env:Path
    $env:CL = "/MP2"
    $env:SCCACHE_DIR = "F:\il2cpp\.opencode\tmp\sccache"
    $env:SCCACHE_IGNORE_SERVER_IO_ERROR = "1"
}

if ($Compiler -eq "clang") {
    # Clang 23 windows-gnu + MSYS2 sysroot; ccache 自动由 cmake\clang-mingw-w64.cmake
    # find_program 挂接。build_clang 无年龄规则: run-tests clang 分支每次构建前清目录
    # (工具链漂移不可复用), ccache 缓存目录本身跨用例保留。
    $env:Path = "F:\il2cpp\.opencode\tmp\ccache-shim;D:\Program Files\mingw\msys2\mingw64\bin;D:\android\sdk\cmake\3.22.1\bin;" + $env:Path
    $env:CCACHE_DIR = "F:\il2cpp\.opencode\tmp\ccache"
    $env:CCACHE_MAXSIZE = "30G"
}
$logDir = Join-Path $Root ".opencode\logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$outDir = Split-Path -Parent $Out
if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$cases = Get-Content -LiteralPath $List | ForEach-Object { $_.Trim() } | Where-Object { $_ }
function Write-ResultRecord {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Case,
        [Parameter(Mandatory = $true)][double]$Mins,
        [Parameter(Mandatory = $true)][datetime]$At
    )
    # Write-side hardening: reject malformed fields and NUL bytes; append as UTF-8 (no BOM)
    # so a hard reset can never leave a half-encoded record tail.
    $st = "$Status".Trim().ToUpperInvariant()
    if (-not ([string]::Equals($st, "PASS", [System.StringComparison]::Ordinal) -or
              [string]::Equals($st, "FAIL", [System.StringComparison]::Ordinal))) {
        throw "invalid result status '$Status'"
    }
    if ("$Case" -notmatch '^[A-Za-z0-9_.-]+$') { throw "invalid case name '$Case'" }
    $line = "{0}`t{1}`t{2}min`t{3}" -f $st, $Case, $Mins, $At.ToString("yyyy-MM-dd HH:mm:ss")
    if ($line.IndexOf([char]0) -ge 0) { throw "NUL byte in result record" }
    [System.IO.File]::AppendAllText($Path, $line + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
}

$done = @{}
if (Test-Path -LiteralPath $Out) {
    # Read-side hardening: strip NUL bytes, drop non-format lines (backup first), rebuild $done
    # from the sanitized set. NB: PowerShell -eq is culture-sensitive and treats U+0000 as
    # ignorable (a NUL-prefixed FAIL still compares equal to FAIL) -> use Ordinal comparisons here.
    $rawLines = [System.IO.File]::ReadAllLines($Out)
    $valid = New-Object System.Collections.Generic.List[string]
    $nulLines = 0
    $malformed = 0
    foreach ($ln in $rawLines) {
        $nul = ([regex]::Matches($ln, [string][char]0)).Count
        $clean = (($ln -replace [string][char]0, "") -replace [string][char]0xFEFF, "").Trim()
        if (-not $clean) { if ($nul -gt 0) { $nulLines++ }; continue }
        $parts = $clean -split "`t"
        $ok = $false
        if ($parts.Count -ge 2) {
            $st = $parts[0].Trim()
            if ([string]::Equals($st, "PASS", [System.StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals($st, "FAIL", [System.StringComparison]::OrdinalIgnoreCase)) {
                if ($parts[1].Trim() -match '^[A-Za-z0-9_.-]+$') { $ok = $true }
            }
        }
        if ($ok) { $valid.Add($clean); $done[$parts[1].Trim()] = $true } else { $malformed++ }
        if ($nul -gt 0) { $nulLines++ }
    }
    Write-Host "results-check $Out : lines=$($rawLines.Count) valid=$($valid.Count) nul-lines=$nulLines malformed=$malformed"
    if ($nulLines -gt 0 -or $malformed -gt 0) {
        $bak = "$Out.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
        Copy-Item -LiteralPath $Out -Destination $bak -Force
        [System.IO.File]::WriteAllLines($Out, $valid, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "results-sanitized: $Out (backup: $bak)"
    }
}
function Test-ZeroedFile {
    # A hard reset on NTFS can leave files full-size but all-NUL; healthy
    # archives/objects never start with 8 zero bytes.
    param([Parameter(Mandatory = $true)][string]$Path)
    try {
        $fs = [System.IO.File]::OpenRead($Path)
        try {
            $buf = New-Object byte[] 8
            $n = $fs.Read($buf, 0, 8)
            if ($n -le 0) { return $false }
            for ($i = 0; $i -lt $n; $i++) { if ($buf[$i] -ne 0) { return $false } }
            return $true
        } finally { $fs.Dispose() }
    } catch { return $false }
}
function Get-ZeroedArtifacts {
    # Archives first (short-circuit), then objects. Returns damaged paths.
    param([Parameter(Mandatory = $true)][string]$BuildDir)
    $bad = New-Object System.Collections.Generic.List[string]
    foreach ($pat in @('*.a', '*.lib')) {
        foreach ($f in @(Get-ChildItem -LiteralPath $BuildDir -Recurse -File -Filter $pat -ErrorAction SilentlyContinue)) {
            if (Test-ZeroedFile -Path $f.FullName) { $bad.Add($f.FullName) }
        }
    }
    if ($bad.Count -gt 0) { return $bad.ToArray() }
    foreach ($pat in @('*.o', '*.obj')) {
        foreach ($f in @(Get-ChildItem -LiteralPath $BuildDir -Recurse -File -Filter $pat -ErrorAction SilentlyContinue)) {
            if (Test-ZeroedFile -Path $f.FullName) { $bad.Add($f.FullName) }
        }
    }
    return $bad.ToArray()
}

foreach ($c in $cases) {
    if ($done.ContainsKey($c)) { Write-Host "SKIP $c (already recorded)"; continue }
    $bd = Join-Path $Root "tests\cases\$c\build_$Compiler"
    # Zero-fill pre-gate: after a hard reset NTFS may leave full-size all-NUL
    # prebuilt libs/objects; delete the build dir so they get rebuilt (avoids
    # spurious link-time 'file format not recognized' FAILs).
    if (Test-Path -LiteralPath $bd) {
        $zeroed = @(Get-ZeroedArtifacts -BuildDir $bd)
        if ($zeroed.Count -gt 0) {
            Write-Host "ZERO-GATE $c ($Compiler): damaged=$($zeroed.Count) -> remove build dir"
            $zeroed | Select-Object -First 5 | ForEach-Object { Write-Host "  offender: $_" }
            Remove-Item -Recurse -Force -LiteralPath $bd -ErrorAction SilentlyContinue
        }
    }

    if ($Compiler -eq "mingw" -and (Test-Path (Join-Path $bd "CMakeCache.txt"))) {
        $cacheText = Get-Content -LiteralPath (Join-Path $bd "CMakeCache.txt") -Raw
        if ($cacheText -match "ming-w64") { Remove-Item -Recurse -Force $bd -ErrorAction SilentlyContinue }
    }
    if ($Compiler -eq "msvc" -and (Test-Path -LiteralPath $bd)) {
        # 旧 cache 绑定已卸载工具集（14.39→14.44）会让 configure 直接失败（A2 实测）；
        # msvc 每例本就全量重编 runtime，无缓存收益，统一删除最稳。
        Remove-Item -Recurse -Force $bd -ErrorAction SilentlyContinue
    }
    $logOut = Join-Path $logDir "$($c)_$Compiler.sweep.log"
    $logErr = Join-Path $logDir "$($c)_$Compiler.sweep.err"
    # 崩溃恢复标记：开始构建前落盘，结束后删除；硬复位腰斩会遗留标记，
    # 由 sweep-supervisor.ps1 启动时清除对应 build_<Compiler> 半写产物后重跑。
    # 用例级硬超时 + 可靠退出码：PS5.1 Start-Process -PassThru + 重定向在非 -Wait 模式下
    # ExitCode 恒 null（会误判全 FAIL）——改用 .NET Process 直启并异步读流。
    $marker = Join-Path $Root ".opencode\tmp\building_$($c).flag"
    New-Item -ItemType File -Force -Path $marker | Out-Null
    $start = Get-Date
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = "powershell.exe"
    # H-3: pin the child's working directory to the repo root. run-tests.ps1
    # runs the net48 oracle and case IO with RELATIVE temp paths; without this
    # the child inherits the launcher's CurrentDirectory (C:\Windows\System32
    # for WMI-started workers), so every relative-path IO case's oracle run
    # fails ("reference run <case> failed").
    $psi.WorkingDirectory = $Root
    $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$Root\scripts\run-tests.ps1`" -Compiler $Compiler -Case $c -ContinueOnError"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    $null = $proc.Start()
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $timedOut = -not $proc.WaitForExit($CaseTimeoutMin * 60000)
    if ($timedOut) {
        & taskkill.exe /T /F /PID $proc.Id 2>$null | Out-Null
    }
    [System.IO.File]::WriteAllText($logOut, $outTask.GetAwaiter().GetResult(), (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($logErr, $errTask.GetAwaiter().GetResult(), (New-Object System.Text.UTF8Encoding($false)))
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    # 后置归档门禁（NDK/Android 通道；IL2CPP_ARCHIVE_GATE=1 时启用）：构建成功后
    # 校验运行时归档（成员数+符号哨兵），失败即把该用例标为 FAIL。
    $gateOk = $true
    if (-not $timedOut -and $proc.ExitCode -eq 0 -and $env:IL2CPP_ARCHIVE_GATE -eq '1') {
        $gateArchive = if ($env:IL2CPP_ARCHIVE_PATH) { $env:IL2CPP_ARCHIVE_PATH } else { Join-Path $Root "tests\cases\$c\build_$Compiler\libil2cpp_runtime.a" }
        $gateSources = if ($env:IL2CPP_ARCHIVE_SOURCES) { $env:IL2CPP_ARCHIVE_SOURCES } else { Join-Path $Root '.opencode/tmp/ndk/files.txt' }
        & (Join-Path $Root 'scripts\archive-gate.ps1') -Archive $gateArchive -SourceList $gateSources
        if ($LASTEXITCODE -ne 0) { $gateOk = $false; Write-Host "ARCHIVE-GATE FAIL $c ($gateArchive)" }
    }
    $status = if ($timedOut -or -not $gateOk) { "FAIL" } else { if ($proc.ExitCode -eq 0) { "PASS" } else { "FAIL" } }
    $mins = [math]::Round(((Get-Date) - $start).TotalMinutes, 1)
    Write-ResultRecord -Path $Out -Status $status -Case $c -Mins $mins -At (Get-Date)
    Write-Host "$status $c ($mins min)"
}
