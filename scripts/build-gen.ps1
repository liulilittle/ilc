param(
    [Parameter(Mandatory)][string]$GenDir,
    [ValidateSet("msvc","mingw","wsl")][string]$Compiler = "msvc"
)
$ErrorActionPreference = "Stop"

function ConvertTo-WslPath([string]$winPath) {
    # E:\il2cpp\xxx -> /mnt/e/il2cpp/xxx
    $drive = $winPath.Substring(0,1).ToLower()
    $rest = $winPath.Substring(3).Replace("\","/")
    return "/mnt/$drive/$rest"
}

if ($Compiler -eq "wsl") {
    # WSL cmake 3.10.2 不支持 -S/-B（cmake 3.13 引入），须 cd 进 build 目录 + 绝对源路径。
    $wslGen = ConvertTo-WslPath (Resolve-Path $GenDir)
    $wslRoot = ConvertTo-WslPath (Resolve-Path ".").Path
    # -d Ubuntu1804: 显式锁定 GCC 7.5 兼容基线发行版，避免默认发行版漂移
    wsl -d Ubuntu1804 bash -c "cd `"$wslRoot`" && rm -rf build/wsl && mkdir -p build/wsl && cd build/wsl && cmake -G 'Unix Makefiles' -DCMAKE_BUILD_TYPE=Release `"$wslGen`" && cmake --build ."
    if ($LASTEXITCODE -ne 0) { throw "wsl build failed" }
    $exe = "build/wsl/il2cpp_gen_exe"
} elseif ($Compiler -eq "msvc") {
    . "scripts/vcvars.ps1"
    $generator = "Visual Studio 17 2022"
    $buildDir = "build/msvc"
    cmake -S $GenDir -B $buildDir -G $generator
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed ($buildDir)" }
    cmake --build $buildDir --config Release
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed ($buildDir)" }
    $exe = Join-Path $buildDir "Release\il2cpp_gen_exe.exe"
} else {
    $generator = "MinGW Makefiles"
    $buildDir = "build/mingw"
    cmake -S $GenDir -B $buildDir -G $generator
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed ($buildDir)" }
    cmake --build $buildDir --config Release
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed ($buildDir)" }
    $exe = Join-Path $buildDir "il2cpp_gen_exe.exe"
}
if (-not (Test-Path $exe)) { throw "build failed: $exe missing" }
Write-Host "built: $exe"
