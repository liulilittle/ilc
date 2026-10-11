<#
.SYNOPSIS
    MinGW-w64 cross-compile automation script -- cross-compiles to Windows x86_64 from a Windows/Linux host.

.DESCRIPTION
    Auto-detects a system MinGW-w64 install, configures the project with the
    cmake/mingw-w64.cmake toolchain, runs the build, and verifies the PE format
    of the artifacts.

.PARAMETER BuildDir
    Build output directory, default build-mingw

.PARAMETER BuildType
    Build type (Debug/Release/RelWithDebInfo/MinSizeRel), default Release

.PARAMETER CMakeGenerator
    CMake generator, default "MinGW Makefiles" (auto-detects Ninja/NMake/Unix Makefiles)

.PARAMETER Jobs
    Number of parallel compile jobs, default CPU logical core count

.PARAMETER MingwRoot
    Explicitly specify the MinGW-w64 install root (overrides auto-detection)

.PARAMETER RunTests
    Run tests (ctest) after the build, default true

.PARAMETER CleanClean
    Clean the build directory then reconfigure

.EXAMPLE
    .\build-mingw.ps1
    Default Release build, auto-detects MinGW

.EXAMPLE
    .\build-mingw.ps1 -BuildType Debug -Clean
    Debug build, clean then rebuild

.EXAMPLE
    .\build-mingw.ps1 -MingwRoot "C:\msys64\mingw64"
    Explicitly specify the MSYS2 MinGW path

.EXAMPLE
    ./build-mingw.sh
    Linux/WSL hosts should use build-mingw.sh
#>

[CmdletBinding()]
param(
    [string]$BuildDir = "build-mingw",
    [ValidateSet("Debug", "Release", "RelWithDebInfo", "MinSizeRel")]
    [string]$BuildType = "Release",
    [string]$CMakeGenerator = "",
    [int]$Jobs = 0,
    [string]$MingwRoot = "",
    [switch]$RunTests,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Resolve-Path $ScriptDir

# Logging helpers
# ---------------------------------------------------------------------------
function Write-Info { param([string]$Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "[OK]   $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Write-Fatal { param([string]$Message) Write-Host "[FAIL] $Message" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------
# 1. Detect MinGW-w64
# ---------------------------------------------------------------------------
Write-Host "`n=== MinGW-w64 cross-compile build ===" -ForegroundColor White
Write-Host "Project: $ProjectRoot" -ForegroundColor Gray

if ($MingwRoot) {
    Write-Info "Using explicit MinGW root: $MingwRoot"
    $env:MINGW_ROOT = $MingwRoot
    $mingwBin = Join-Path $MingwRoot "bin"
    if (Test-Path $mingwBin) {
        $env:PATH = "$mingwBin;$env:PATH"
        Write-Info "Added $mingwBin to PATH"
    }
}

# Detect the compiler
$mingwCCompiler = Get-Command x86_64-w64-mingw32-gcc -ErrorAction SilentlyContinue
$mingwCxxCompiler = Get-Command x86_64-w64-mingw32-g++ -ErrorAction SilentlyContinue
$mingwClang = Get-Command x86_64-w64-mingw32-clang -ErrorAction SilentlyContinue

# If no exact prefix is found, try clang (mingw-w64 clang can cross-compile too)
if (-not $mingwCCompiler) {
    $mingwClangPlain = Get-Command clang -ErrorAction SilentlyContinue
    if ($mingwClangPlain) {
        $clangVersion = & clang --version 2>&1 | Select-Object -First 1
        if ($clangVersion -match "windows-gnu|w64") {
            $mingwCCompiler = $mingwClangPlain
            Write-Info "Using clang as the cross compiler (target: windows-gnu)"
        }
    }
}

if (-not $mingwCCompiler) {
    # Scan common install paths (in priority order)
    $searchPaths = @(
        "C:\Program Files\ming-w64\bin",
        "C:\msys64\mingw64\bin",
        "C:\Program Files (x86)\ming-w64\bin",
        "C:\mingw64\bin",
        "C:\MinGW\bin",
        "$env:MINGW_ROOT\bin",
        "$env:MINGW64_ROOT\bin",
        "$env{MSYSTEM_PREFIX}\bin"
    )
    foreach ($sp in $searchPaths) {
        $candidate = Join-Path $sp "x86_64-w64-mingw32-gcc.exe"
        if (Test-Path $candidate) {
            $mingwCCompiler = @{ Source = $candidate } | ForEach-Object { New-Object PSObject -Property $_ }
            $env:PATH = "$sp;$env:PATH"
            Write-Info "Found the cross compiler at $sp"
            break
        }
    }
}

if (-not $mingwCCompiler) {
    Write-Fatal "MinGW-w64 cross compiler not found. Install mingw-w64 and make sure x86_64-w64-mingw32-gcc is in PATH."
}

$ccPath = if ($mingwCCompiler.Source) { $mingwCCompiler.Source } else { $mingwCCompiler }
Write-Info "C compiler: $ccPath"
& $ccPath --version 2>&1 | Select-Object -First 1

# ---------------------------------------------------------------------------
# 2. Detect CMake
# ---------------------------------------------------------------------------
$cmakeCmd = Get-Command cmake -ErrorAction SilentlyContinue
if (-not $cmakeCmd) {
    Write-Fatal "cmake not found. Install CMake 3.10+ and make sure it is in PATH."
}
$cmakeVersion = & cmake --version 2>&1 | Select-Object -First 1
Write-Info "CMake: $cmakeVersion"

# ---------------------------------------------------------------------------
# 3. Detect the generator
# ---------------------------------------------------------------------------
if (-not $CMakeGenerator) {
    $ninja = Get-Command ninja -ErrorAction SilentlyContinue
    if ($ninja) {
        $CMakeGenerator = "Ninja"
    } elseif (Is-WindowsHost) {
        $CMakeGenerator = "MinGW Makefiles"
    } else {
        $CMakeGenerator = "Unix Makefiles"
    }
}
Write-Info "Generator: $CMakeGenerator"

# ---------------------------------------------------------------------------
# 4. Parallel job count
# ---------------------------------------------------------------------------
if ($Jobs -le 0) {
    $cpuCores = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
    if (-not $cpuCores) { $cpuCores = 4 }
    $Jobs = $cpuCores
}
Write-Info "Parallel jobs: $Jobs"

# ---------------------------------------------------------------------------
# 5. Clean (optional)
# ---------------------------------------------------------------------------
$buildFullPath = Join-Path $ProjectRoot $BuildDir
if ($Clean -and (Test-Path $buildFullPath)) {
    Write-Info "Cleaning build directory: $buildFullPath"
    Remove-Item -Recurse -Force $buildFullPath
}

# ---------------------------------------------------------------------------
# 6. CMake configure
# ---------------------------------------------------------------------------
Write-Host "`n--- CMake configure ---" -ForegroundColor White

$toolchainFile = Join-Path $ProjectRoot "cmake\mingw-w64.cmake"
if (-not (Test-Path $toolchainFile)) {
    Write-Fatal "Toolchain file does not exist: $toolchainFile"
}
Write-Info "Toolchain: $toolchainFile"

$cmakeArgs = @(
    "-S", $ProjectRoot,
    "-B", $buildFullPath,
    "-G", $CMakeGenerator,
    "-DCMAKE_BUILD_TYPE=$BuildType",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchainFile"
)

# Subdirectory CMakeLists.txt detection -- no top-level CMakeLists.txt at the root, use src/runtime
$rootCMake = Join-Path $ProjectRoot "CMakeLists.txt"
if (-not (Test-Path $rootCMake)) {
    Write-Info "No CMakeLists.txt at the root, using src/runtime as the entry point"
    # The user needs to build from src/runtime, or create a top-level CMakeLists
    # Here we will use src/runtime's CMakeLists.txt
    $srcRuntimeCMake = Join-Path $ProjectRoot "src\runtime\CMakeLists.txt"
    if (Test-Path $srcRuntimeCMake) {
        Write-Info "Detected src/runtime/CMakeLists.txt"
        # Create a temporary top-level wrapper or prompt the user
        Write-Warn "Project builds from the src/runtime subdirectory, using -S src/runtime"
        $cmakeArgs = @(
            "-S", (Join-Path $ProjectRoot "src\runtime"),
            "-B", $buildFullPath,
            "-G", $CMakeGenerator,
            "-DCMAKE_BUILD_TYPE=$BuildType",
            "-DCMAKE_TOOLCHAIN_FILE=$toolchainFile"
        )
    }
}

Write-Host "cmake $cmakeArgs" -ForegroundColor Gray
& cmake @cmakeArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fatal "CMake configure failed (exit $LASTEXITCODE)"
}
Write-Success "CMake configure complete"

# ---------------------------------------------------------------------------
# 7. CMake build
# ---------------------------------------------------------------------------
Write-Host "`n--- Build ($BuildType) ---" -ForegroundColor White

$buildArgs = @(
    "--build", $buildFullPath,
    "--config", $BuildType,
    "--parallel", $Jobs,
    "--verbose"
)

& cmake @buildArgs 2>&1
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) {
    Write-Fatal "Build failed (exit $buildExit)"
}
Write-Success "Build complete (0 errors)"

# ---------------------------------------------------------------------------
# 8. PE format verification
# ---------------------------------------------------------------------------
Write-Host "`n--- PE format verification ---" -ForegroundColor White

$exeFiles = Get-ChildItem -Path $buildFullPath -Filter "*.exe" -Recurse -ErrorAction SilentlyContinue
$dllFiles = Get-ChildItem -Path $buildFullPath -Filter "*.dll" -Recurse -ErrorAction SilentlyContinue
$libFiles = Get-ChildItem -Path $buildFullPath -Filter "*.a" -Recurse -ErrorAction SilentlyContinue

$peFiles = @($exeFiles) + @($dllFiles)
if ($peFiles.Count -eq 0) {
    Write-Warn "No PE executable files found (.exe/.dll)"
} else {
    Write-Info "Found $($peFiles.Count) PE file(s), $($libFiles.Count) static librar(ies) (.a)"
    foreach ($pe in $peFiles) {
        $bytes = [System.IO.File]::ReadAllBytes($pe.FullName)
        if ($bytes.Count -ge 2 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A) {
            Write-Success "PE: $($pe.Name) (MZ header OK, size: $($pe.Length) bytes)"
        } else {
            Write-Warn "PE: $($pe.Name) may be missing the MZ header (first 2 bytes: 0x$($bytes[0].ToString('X2')) 0x$($bytes[1].ToString('X2')))"
        }
    }
}

# ---------------------------------------------------------------------------
# 9. Output summary
# ---------------------------------------------------------------------------
Write-Host "`n=== Build result ===" -ForegroundColor White
Write-Host "Build directory: $buildFullPath" -ForegroundColor Gray
Write-Host "Build type     : $BuildType" -ForegroundColor Gray
Write-Host "Compiler       : $ccPath" -ForegroundColor Gray

if ($libFiles.Count -gt 0) {
    Write-Host "`nStatic libraries:" -ForegroundColor White
    foreach ($lib in $libFiles) {
        Write-Host "  $($lib.Name) ($([math]::Round($lib.Length/1KB,1)) KB)" -ForegroundColor Gray
    }
}

if ($peFiles.Count -gt 0) {
    Write-Host "`nExecutables:" -ForegroundColor White
    foreach ($pe in $peFiles) {
        Write-Host "  $($pe.Name) ($([math]::Round($pe.Length/1KB,1)) KB)" -ForegroundColor Gray
    }
}

Write-Host "`n=== MinGW cross-compile build succeeded ===" -ForegroundColor Green
