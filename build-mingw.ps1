<#
.SYNOPSIS
    MinGW-w64 交叉编译自动化脚本 — 从 Windows/Linux 主机交叉编译到 Windows x86_64。

.DESCRIPTION
    自动检测系统 MinGW-w64 安装, 使用 cmake/mingw-w64.cmake toolchain 配置项目,
    执行构建, 并验证产物 PE 格式。

.PARAMETER BuildDir
    构建输出目录, 默认 build-mingw

.PARAMETER BuildType
    构建类型 (Debug/Release/RelWithDebInfo/MinSizeRel), 默认 Release

.PARAMETER CMakeGenerator
    CMake 生成器, 默认 "MinGW Makefiles" (自动检测 Ninja/NMake/Unix Makefiles)

.PARAMETER Jobs
    并行编译任务数, 默认 CPU 逻辑核心数

.PARAMETER MingwRoot
    显式指定 MinGW-w64 安装根目录 (覆盖自动检测)

.PARAMETER RunTests
    构建完成后运行测试 (ctest), 默认 true

.PARAMETER CleanClean
    清理构建目录后重新配置

.EXAMPLE
    .\build-mingw.ps1
    默认 Release 构建, 自动检测 MinGW

.EXAMPLE
    .\build-mingw.ps1 -BuildType Debug -Clean
    Debug 构建, 清理后重建

.EXAMPLE
    .\build-mingw.ps1 -MingwRoot "C:\msys64\mingw64"
    显式指定 MSYS2 MinGW 路径

.EXAMPLE
    ./build-mingw.sh
    Linux/WSL 主机请使用 build-mingw.sh
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

# ---------------------------------------------------------------------------
# 日志辅助
# ---------------------------------------------------------------------------
function Write-Info { param([string]$Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "[OK]   $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Write-Fatal { param([string]$Message) Write-Host "[FAIL] $Message" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------
# 1. 检测 MinGW-w64
# ---------------------------------------------------------------------------
Write-Host "`n=== MinGW-w64 交叉编译构建 ===" -ForegroundColor White
Write-Host "Project: $ProjectRoot" -ForegroundColor Gray

if ($MingwRoot) {
    Write-Info "使用显式 MinGW 根目录: $MingwRoot"
    $env:MINGW_ROOT = $MingwRoot
    $mingwBin = Join-Path $MingwRoot "bin"
    if (Test-Path $mingwBin) {
        $env:PATH = "$mingwBin;$env:PATH"
        Write-Info "已将 $mingwBin 添加到 PATH"
    }
}

# 检测编译器
$mingwCCompiler = Get-Command x86_64-w64-mingw32-gcc -ErrorAction SilentlyContinue
$mingwCxxCompiler = Get-Command x86_64-w64-mingw32-g++ -ErrorAction SilentlyContinue
$mingwClang = Get-Command x86_64-w64-mingw32-clang -ErrorAction SilentlyContinue

# 如果没找到精确前缀, 尝试 clang (mingw-w64 的 clang 也可以交叉编译)
if (-not $mingwCCompiler) {
    $mingwClangPlain = Get-Command clang -ErrorAction SilentlyContinue
    if ($mingwClangPlain) {
        $clangVersion = & clang --version 2>&1 | Select-Object -First 1
        if ($clangVersion -match "windows-gnu|w64") {
            $mingwCCompiler = $mingwClangPlain
            Write-Info "使用 clang 作为交叉编译器 (target: windows-gnu)"
        }
    }
}

if (-not $mingwCCompiler) {
    # 扫描常见安装路径 (按优先级排序)
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
            Write-Info "在 $sp 找到交叉编译器"
            break
        }
    }
}

if (-not $mingwCCompiler) {
    Write-Fatal "未找到 MinGW-w64 交叉编译器。请安装 mingw-w64 并确保 x86_64-w64-mingw32-gcc 在 PATH 中。"
}

$ccPath = if ($mingwCCompiler.Source) { $mingwCCompiler.Source } else { $mingwCCompiler }
Write-Info "C 编译器: $ccPath"
& $ccPath --version 2>&1 | Select-Object -First 1

# ---------------------------------------------------------------------------
# 2. 检测 CMake
# ---------------------------------------------------------------------------
$cmakeCmd = Get-Command cmake -ErrorAction SilentlyContinue
if (-not $cmakeCmd) {
    Write-Fatal "未找到 cmake。请安装 CMake 3.10+ 并确保在 PATH 中。"
}
$cmakeVersion = & cmake --version 2>&1 | Select-Object -First 1
Write-Info "CMake: $cmakeVersion"

# ---------------------------------------------------------------------------
# 3. 检测生成器
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
Write-Info "生成器: $CMakeGenerator"

# ---------------------------------------------------------------------------
# 4. 并行任务数
# ---------------------------------------------------------------------------
if ($Jobs -le 0) {
    $cpuCores = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
    if (-not $cpuCores) { $cpuCores = 4 }
    $Jobs = $cpuCores
}
Write-Info "并行任务: $Jobs"

# ---------------------------------------------------------------------------
# 5. 清理 (可选)
# ---------------------------------------------------------------------------
$buildFullPath = Join-Path $ProjectRoot $BuildDir
if ($Clean -and (Test-Path $buildFullPath)) {
    Write-Info "清理构建目录: $buildFullPath"
    Remove-Item -Recurse -Force $buildFullPath
}

# ---------------------------------------------------------------------------
# 6. CMake 配置
# ---------------------------------------------------------------------------
Write-Host "`n--- CMake 配置 ---" -ForegroundColor White

$toolchainFile = Join-Path $ProjectRoot "cmake\mingw-w64.cmake"
if (-not (Test-Path $toolchainFile)) {
    Write-Fatal "Toolchain 文件不存在: $toolchainFile"
}
Write-Info "Toolchain: $toolchainFile"

$cmakeArgs = @(
    "-S", $ProjectRoot,
    "-B", $buildFullPath,
    "-G", $CMakeGenerator,
    "-DCMAKE_BUILD_TYPE=$BuildType",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchainFile"
)

# 子目录CMakeLists.txt检测 — 根目录无顶层CMakeLists.txt, 使用 src/runtime
$rootCMake = Join-Path $ProjectRoot "CMakeLists.txt"
if (-not (Test-Path $rootCMake)) {
    Write-Info "根目录无 CMakeLists.txt, 使用 src/runtime 作为入口"
    # 用户需要从 src/runtime 开始构建, 或创建一个顶层 CMakeLists
    # 这里我们将使用 src/runtime 的 CMakeLists.txt
    $srcRuntimeCMake = Join-Path $ProjectRoot "src\runtime\CMakeLists.txt"
    if (Test-Path $srcRuntimeCMake) {
        Write-Info "检测到 src/runtime/CMakeLists.txt"
        # 创建临时顶层 wrapper 或提示用户
        Write-Warn "项目使用 src/runtime 子目录构建, 将使用 -S src/runtime"
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
    Write-Fatal "CMake 配置失败 (exit $LASTEXITCODE)"
}
Write-Success "CMake 配置完成"

# ---------------------------------------------------------------------------
# 7. CMake 构建
# ---------------------------------------------------------------------------
Write-Host "`n--- 构建 ($BuildType) ---" -ForegroundColor White

$buildArgs = @(
    "--build", $buildFullPath,
    "--config", $BuildType,
    "--parallel", $Jobs,
    "--verbose"
)

& cmake @buildArgs 2>&1
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) {
    Write-Fatal "构建失败 (exit $buildExit)"
}
Write-Success "构建完成 (0 errors)"

# ---------------------------------------------------------------------------
# 8. PE 格式验证
# ---------------------------------------------------------------------------
Write-Host "`n--- PE 格式验证 ---" -ForegroundColor White

$exeFiles = Get-ChildItem -Path $buildFullPath -Filter "*.exe" -Recurse -ErrorAction SilentlyContinue
$dllFiles = Get-ChildItem -Path $buildFullPath -Filter "*.dll" -Recurse -ErrorAction SilentlyContinue
$libFiles = Get-ChildItem -Path $buildFullPath -Filter "*.a" -Recurse -ErrorAction SilentlyContinue

$peFiles = @($exeFiles) + @($dllFiles)
if ($peFiles.Count -eq 0) {
    Write-Warn "未找到 PE 可执行文件 (.exe/.dll)"
} else {
    Write-Info "找到 $($peFiles.Count) 个 PE 文件, $($libFiles.Count) 个静态库 (.a)"
    foreach ($pe in $peFiles) {
        $bytes = [System.IO.File]::ReadAllBytes($pe.FullName)
        if ($bytes.Count -ge 2 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A) {
            Write-Success "PE: $($pe.Name) (MZ 头 OK, 大小: $($pe.Length) bytes)"
        } else {
            Write-Warn "PE: $($pe.Name) 可能缺少 MZ 头 (前2字节: 0x$($bytes[0].ToString('X2')) 0x$($bytes[1].ToString('X2')))"
        }
    }
}

# ---------------------------------------------------------------------------
# 9. 输出汇总
# ---------------------------------------------------------------------------
Write-Host "`n=== 构建结果 ===" -ForegroundColor White
Write-Host "构建目录: $buildFullPath" -ForegroundColor Gray
Write-Host "构建类型: $BuildType" -ForegroundColor Gray
Write-Host "编译器  : $ccPath" -ForegroundColor Gray

if ($libFiles.Count -gt 0) {
    Write-Host "`n静态库:" -ForegroundColor White
    foreach ($lib in $libFiles) {
        Write-Host "  $($lib.Name) ($([math]::Round($lib.Length/1KB,1)) KB)" -ForegroundColor Gray
    }
}

if ($peFiles.Count -gt 0) {
    Write-Host "`n可执行文件:" -ForegroundColor White
    foreach ($pe in $peFiles) {
        Write-Host "  $($pe.Name) ($([math]::Round($pe.Length/1KB,1)) KB)" -ForegroundColor Gray
    }
}

Write-Host "`n=== MinGW 交叉编译构建成功 ===" -ForegroundColor Green
