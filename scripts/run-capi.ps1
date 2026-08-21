param(
    [ValidateSet("mingw", "msvc", "wsl")]
    [string]$Compiler = "mingw"
)

$ErrorActionPreference = "Stop"
# run-capi.ps1 -- M4 Task 7 C API smoke test across the toolchains.
# Pipeline: build capicalc fixture -> transpile -> build the static library
# (il2cpp_gen) -> build the external host (tests/capi/main.cpp) against the
# library -> run the host. Expected: "capi smoke: all passed", exit 0.
$Root = Split-Path -Parent $PSScriptRoot
$OutBase = "$Root/build/capi_check"

switch ($Compiler) {
    "mingw" { $Gen = "MinGW Makefiles"; $Out = "$OutBase/mingw" }
    "msvc"  { $Gen = "Visual Studio 17 2022"; $Out = "$OutBase/msvc" }
    "wsl"   { $Gen = ""; $Out = "$OutBase/wsl" }  # WSL 用 -G 缺省 + cmake 在 WSL 内
}

# 1) fixture
& "$PSScriptRoot/build-fixtures.ps1"
if ($LASTEXITCODE -ne 0) { throw "fixtures build failed" }

# 2) transpile
$GenDir = "$Out/gen"
Remove-Item -Recurse -Force $GenDir -ErrorAction SilentlyContinue
dotnet run --project "$Root/src/il2cppc/il2cppc.csproj" -- "$Root/tests/fixtures/out/capicalc.dll" $GenDir
if ($LASTEXITCODE -ne 0) { throw "transpile capicalc failed" }

$BuildDir = "$Out/build"
if ($Compiler -eq "wsl") {
    # Windows 路径 -> WSL 路径（E:\a\b -> /mnt/e/a/b）
    $drive = $GenDir.Substring(0, 1).ToLower()
    $rest = $GenDir.Substring(2).Replace("\", "/")
    $wslGen = "/mnt/$drive$rest"
    # 老版本 cmake（Ubuntu 1804 自带）不认识 -S：用 -H/--build 形式
    # 先清 build 目录：残留 CMakeCache 会指向旧 CAPILLIB_DIR，产生误导性失败
    # WSL 产物无 .exe 后缀（Linux ELF）
    wsl -d Ubuntu1804 bash -lc "cd /mnt/e/il2cpp && rm -rf build/capi_check/wsl/build && cmake -Htests/capi -Bbuild/capi_check/wsl/build -D'CAPILLIB_DIR=$wslGen' && cmake --build build/capi_check/wsl/build && ./build/capi_check/wsl/build/capi_host"
    if ($LASTEXITCODE -ne 0) { throw "capi_host (wsl) failed" }
}
else {
    Remove-Item -Recurse -Force $BuildDir -ErrorAction SilentlyContinue
    if ($Compiler -eq "msvc") {
        & "$PSScriptRoot/vcvars.ps1"
    }
    cmake -S "$Root/tests/capi" -B $BuildDir -DCAPILLIB_DIR="$GenDir" -G $Gen
    if ($LASTEXITCODE -ne 0) { throw "capi configure failed" }
    cmake --build $BuildDir --config Debug
    if ($LASTEXITCODE -ne 0) { throw "capi build failed" }
    $HostExe = if ($Compiler -eq "msvc") { "$BuildDir/Debug/capi_host.exe" } else { "$BuildDir/capi_host.exe" }
    & $HostExe
    if ($LASTEXITCODE -ne 0) { throw "capi_host ($Compiler) failed" }
}
Write-Host "capi smoke passed ($Compiler)"
