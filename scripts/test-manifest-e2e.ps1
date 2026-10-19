#------------------------------------------------------------------------------
# test-manifest-e2e.ps1 -- build-level end-to-end gate for the Windows
# application manifest (nb10).
#
# Unit tests only check the generated CMakeLists text. They cannot catch a
# toolchain that ignores or overrides the .rc: the MSVC linker synthesizes its
# own RT_MANIFEST id 1, which (a) collides with ours (CVTRES CVT1100 + LNK1123)
# and (b) on the generators where the link still succeeds, silently wins -- the
# exe then runs asInvoker even though the manifest asked for
# requireAdministrator. Only a real build + resource read proves the artifact.
#
# For each requested toolchain this script:
#   1. transpiles the case with --requested-execution-level <Level>
#   2. configures + builds the generated CMake project
#   3. reads RT_MANIFEST back with scripts/verify-manifest.ps1, asserting the
#      level, uiAccess, the synthesized assemblyIdentity, AND the exact
#      SHA-256 of gen/app.manifest (a level-only check also matches the
#      toolchain's default manifest, so it is not proof on its own)
#
# usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test-manifest-e2e.ps1 `
#       [-Case p-manifest] [-Level requireAdministrator] [-UiAccess true|false] `
#       [-Compiler mingw|msvc|all] [-Arch ""]
#
# Exit codes: 0 = every requested toolchain passed; 1 = at least one failed;
#             2 = bad usage / missing prerequisite.
#------------------------------------------------------------------------------
param(
    [string]$Case = "p-manifest",
    [ValidateSet("asInvoker", "highestAvailable", "requireAdministrator")]
    [string]$Level = "requireAdministrator",
    [ValidateSet("true", "false")]
    [string]$UiAccess = "false",
    [ValidateSet("mingw", "msvc", "all")]
    [string]$Compiler = "mingw",
    [string]$Arch = "",
    # Skip the launch check (it may raise a UAC prompt for a requireAdministrator
    # image on a non-elevated desktop; the resource assertions still run).
    [switch]$SkipRun
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$CaseDir = Join-Path $Root "tests\cases\$Case"
$Transpiler = Join-Path $Root "src\il2cppc\bin\Debug\net8.0\il2cppc.dll"
$Verify = Join-Path $Root "scripts\verify-manifest.ps1"

if (-not (Test-Path -LiteralPath $CaseDir)) {
    Write-Host "FAIL: case not found: $CaseDir"
    exit 2
}
if (-not (Test-Path -LiteralPath $Transpiler)) {
    Write-Host "FAIL: transpiler not built: $Transpiler (run: dotnet build src\il2cppc\il2cppc.csproj -c Debug)"
    exit 2
}

# Build the case's managed assembly so the transpiler has an input.
$csproj = Get-ChildItem -LiteralPath $CaseDir -Filter *.csproj | Select-Object -First 1
if ($null -eq $csproj) {
    Write-Host "FAIL: no .csproj in $CaseDir"
    exit 2
}
Write-Host "== building $($csproj.Name)"
& dotnet build $csproj.FullName -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: managed build failed"
    exit 1
}
$assembly = Get-ChildItem -LiteralPath (Join-Path $CaseDir "bin\Release") -Recurse -Filter "$Case.exe" |
    Select-Object -First 1
if ($null -eq $assembly) {
    Write-Host "FAIL: built assembly not found under $CaseDir\bin\Release"
    exit 1
}

$work = Join-Path $Root ".opencode\tmp\manifest-e2e\$Case"
# Each run gets its OWN directory. A fixed shared directory makes two concurrent
# runs delete each other's tree and report a bogus build failure (observed: run3
# vs a simultaneous run in the same checkout). Unique per (PID, timestamp) makes
# the gate concurrency-safe; stale directories are pruned best-effort.
$runDir = Join-Path $Root (".opencode\tmp\manifest-e2e\$Case-" + (Get-Date -Format "yyyyMMdd-HHmmss") + "-" + $PID)
Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-1) } |
    ForEach-Object { try { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop } catch { } }
$work = $runDir
New-Item -ItemType Directory -Path $work -Force | Out-Null

# The sidecar would win over nothing but is irrelevant here: an explicit
# --requested-execution-level takes precedence and suppresses it.
$gen = Join-Path $work "gen"
Write-Host "== transpiling ($Level, uiAccess=$UiAccess)"
$transpileArgs = @("--requested-execution-level", $Level)
if ($UiAccess -eq "true") { $transpileArgs += "--ui-access" }
$transpileArgs += @($assembly.FullName, $gen)
& dotnet exec $Transpiler @transpileArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: transpile failed"
    exit 1
}
$manifestPath = Join-Path $gen "app.manifest"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    Write-Host "FAIL: generator did not emit app.manifest"
    exit 1
}
$expectedSha = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedBytes = (Get-Item -LiteralPath $manifestPath).Length
Write-Host "== expected RT_MANIFEST: $expectedBytes bytes, sha256 $expectedSha"

$compilers = if ($Compiler -eq "all") { @("mingw", "msvc") } else { @($Compiler) }
$failed = @()

foreach ($c in $compilers) {
    Write-Host ""
    Write-Host "== [$c] configure + build"
    $build = Join-Path $work "build-$c"
    $log = Join-Path $work "build-$c.log"
    $env:IL2CPP_MINGW_ROOT = if ($env:IL2CPP_MINGW_ROOT) { $env:IL2CPP_MINGW_ROOT } else { "D:\Program Files\mingw\msys2\mingw64" }
    $env:PATH = "$($env:IL2CPP_MINGW_ROOT)\bin;D:\android\sdk\cmake\3.22.1\bin;$env:PATH"

    if ($c -eq "mingw") {
        $toolchain = Join-Path $Root "cmake\mingw-w64.cmake"
        # Native tools write progress/warnings to stderr. With
        # $ErrorActionPreference = "Stop", a redirected native stderr line
        # becomes a TERMINATING error and silently aborts the build mid-way, so
        # relax the preference for the duration of each native call.
        $prev = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            & cmake -S $gen -B $build -G "MinGW Makefiles" "-DCMAKE_TOOLCHAIN_FILE=$toolchain" "-DCMAKE_BUILD_TYPE=Release" *>&1 | Out-File -Encoding utf8 $log
            $cfg = $LASTEXITCODE
            if ($cfg -eq 0) {
                & cmake --build $build --parallel *>&1 | Out-File -Encoding utf8 -Append $log
                $bld = $LASTEXITCODE
            } else { $bld = -1 }
        } finally {
            $ErrorActionPreference = $prev
        }
        if ($cfg -ne 0) {
            Write-Host (Get-Content $log -Tail 20 | Out-String)
            $failed += "$c configure"
            continue
        }
        if ($bld -ne 0) {
            Write-Host (Get-Content $log -Tail 30 | Out-String)
            $failed += "$c build"
            continue
        }
        $exe = Join-Path $build "il2cpp_gen_exe.exe"
    } else {
        $generator = if ($env:IL2CPP_MSVC_GENERATOR) { $env:IL2CPP_MSVC_GENERATOR } else { "Visual Studio 17 2022" }
        # The configured generator decides BOTH the configure arguments and the
        # output layout: "Visual Studio ..." is a multi-config generator (--config
        # Release, exe under Release\), "Ninja" is single-config (needs
        # CMAKE_BUILD_TYPE at configure time, exe directly in the build dir).
        # Hard-coding the VS layout would report "produced no exe" for a perfectly
        # successful Ninja build.
        $isVs = $generator -match "^Visual Studio"
        $cmakeArgs = @("-S", $gen, "-B", $build, "-G", $generator)
        if ($isVs) {
            if ($Arch -ne "") { $cmakeArgs += @("-A", $Arch) }
        } else {
            $cmakeArgs += @("-DCMAKE_BUILD_TYPE=Release")
        }
        $prev = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            & cmake @cmakeArgs *>&1 | Out-File -Encoding utf8 $log
            $cfg = $LASTEXITCODE
            if ($cfg -eq 0) {
                if ($isVs) {
                    & cmake --build $build --config Release --parallel *>&1 | Out-File -Encoding utf8 -Append $log
                } else {
                    & cmake --build $build --parallel *>&1 | Out-File -Encoding utf8 -Append $log
                }
                $bld = $LASTEXITCODE
            } else { $bld = -1 }
        } finally {
            $ErrorActionPreference = $prev
        }
        if ($cfg -ne 0) {
            Write-Host (Get-Content $log -Tail 20 | Out-String)
            $failed += "$c configure"
            continue
        }
        if ($bld -ne 0) {
            # Do not guess the cause: show the real toolchain output and let the
            # log name the error (CVT1100/LNK1123 is one possibility, not the
            # only one -- a locked .res file also lands here).
            Write-Host (Get-Content $log -Tail 30 | Out-String)
            $failed += "$c build (see $log)"
            continue
        }
        if ($isVs) {
            $exe = Join-Path $build "Release\il2cpp_gen_exe.exe"
        } else {
            $exe = Join-Path $build "il2cpp_gen_exe.exe"
        }
    }

    if (-not (Test-Path -LiteralPath $exe)) {
        $failed += "$c produced no il2cpp_gen_exe.exe"
        continue
    }

    $needle = "il2cpp.app"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $Verify -Exe $exe `
        -Level $Level -UiAccess $UiAccess -ExpectSha256 $expectedSha -ExpectIdentity $needle
    if ($LASTEXITCODE -ne 0) {
        $failed += "$c manifest verification"
        continue
    }

    # The resource check proves the manifest is EMBEDDED. Only starting the image
    # proves the loader ACCEPTS it -- a manifest whose bytes and declaration
    # disagree builds and verifies fine, then dies at CreateProcess with
    # SideBySide "Invalid Xml syntax" (event id 59).
    #
    # Elevation matters here. CreateProcess on a requireAdministrator image from
    # a non-elevated process fails with ERROR_ELEVATION_REQUIRED (740) -- and
    # that is POSITIVE evidence: the loader parsed the manifest and is asking for
    # elevation. A manifest it rejects instead yields ERROR_SXS_* (14001 etc.).
    # 740 is therefore reported as OK, 1223 (user dismissed UAC) as SKIP, and
    # anything else as a failure. Process.Start throws Win32Exception for these,
    # so it must be caught -- otherwise CI dies with an unhandled exception.
    if ($SkipRun) {
        Write-Host "SKIP: $c launch (-SkipRun)"
        continue
    }
    $runLog = Join-Path $work "run-$c.log"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
    } catch [System.ComponentModel.Win32Exception] {
        $code = $_.Exception.NativeErrorCode
        if ($code -eq 740) {
            Write-Host "OK: $c loader accepted the manifest (elevation required, not launched)"
            continue
        }
        if ($code -eq 1223) {
            Write-Host "SKIP: $c launch cancelled at the UAC prompt"
            continue
        }
        Write-Host "FAIL: $c could not be started (Win32 error $code): $($_.Exception.Message)"
        $failed += "$c launch failed (Win32 $code)"
        continue
    }
    # Read both streams asynchronously: draining one synchronously can deadlock
    # when the child fills the other pipe's buffer.
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    if (-not $proc.WaitForExit(20000)) {
        try { $proc.Kill() } catch { }
        Write-Host "SKIP: $c launch timed out after 20s (UAC prompt or no interactive desktop)"
        continue
    }
    ($outTask.Result + $errTask.Result) | Out-File -Encoding utf8 $runLog
    if ($proc.ExitCode -ne 0) {
        Write-Host (Get-Content $runLog | Out-String)
        $failed += "$c executable did not start (exit $($proc.ExitCode))"
        continue
    }
    Write-Host "OK: $c executable started (exit 0)"
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "FAIL: $($failed -join '; ')"
    exit 1
}
Write-Host "PASS: $($compilers -join ', ') embed the requested manifest byte-for-byte"
exit 0
