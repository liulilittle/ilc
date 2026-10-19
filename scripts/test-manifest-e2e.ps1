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
# A previous run's MSBuild/msbuild node can still hold app.res open, which makes
# Remove-Item fail and would then be misreported as a build failure. Retry, and
# if the directory still cannot be cleared, build into a fresh sibling instead of
# failing: the point of this gate is the artifact, not a fixed directory.
function Remove-WorkTree([string]$path) {
    for ($i = 0; $i -lt 5; $i++) {
        if (-not (Test-Path -LiteralPath $path)) { return $true }
        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            return $true
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    return -not (Test-Path -LiteralPath $path)
}
if (-not (Remove-WorkTree $work)) {
    $work = Join-Path $Root (".opencode\tmp\manifest-e2e\$Case-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    Write-Host "== note: previous work dir is locked, using $work"
}
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
        $args = @("-S", $gen, "-B", $build, "-G", $generator)
        if ($Arch -ne "") { $args += @("-A", $Arch) }
        $prev = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            & cmake @args *>&1 | Out-File -Encoding utf8 $log
            $cfg = $LASTEXITCODE
            if ($cfg -eq 0) {
                & cmake --build $build --config Release --parallel *>&1 | Out-File -Encoding utf8 -Append $log
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
        $exe = Join-Path $build "Release\il2cpp_gen_exe.exe"
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

    # The resource check proves the manifest is EMBEDDED. Only running the image
    # proves the loader ACCEPTS it -- a manifest whose bytes and declaration
    # disagree builds and verifies fine, then dies at CreateProcess with
    # SideBySide "Invalid Xml syntax" (event id 59).
    #
    # A requireAdministrator image may raise a UAC prompt on a non-elevated
    # desktop, which would hang an unattended CI run. The launch is therefore
    # time-boxed: a timeout is reported as SKIP (not a failure), a non-zero exit
    # IS a failure (the loader rejected the manifest).
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
    $proc = [System.Diagnostics.Process]::Start($psi)
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
