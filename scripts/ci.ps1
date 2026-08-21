$ErrorActionPreference = "Stop"
# 以脚本位置为基准解析仓库根，避免 -File 模式下 CWD 不确定导致相对路径失效
$Root = Split-Path -Parent $PSScriptRoot

# 一键全量 CI：
#   1. 构建转译器（dotnet build src/il2cppc）
#   2. 构建 fixtures（dotnet build tests/fixtures，单测与 e2e 的输入，干净检出自包含）
#   3. 单元测试（dotnet test tests/unit）
#   4. 端到端 MinGW（6 用例，net48 参考 vs 转译产物 stdout 逐字节 diff）
#   5. 端到端 MSVC
#   6. 端到端 WSL（GCC 7.5 基线）
# 任一步失败 → 非零退出；全部通过 → 输出 "=== CI PASS ==="。
# 注意：run-tests.ps1 内部用 Split-Path -Parent $PSScriptRoot 做路径基准，
#       必须以 pwsh 7 调用（Windows PowerShell 5.1 无 $PSScriptRoot 兼容问题，但统一走 pwsh）。

Write-Host "=== 1. build transpiler ==="
dotnet build "$Root/src/il2cppc/il2cppc.csproj"
if ($LASTEXITCODE -ne 0) { throw "transpiler build failed" }

Write-Host "=== 2. build fixtures ==="
pwsh -NoProfile -File "$Root/scripts/build-fixtures.ps1"
if ($LASTEXITCODE -ne 0) { throw "fixtures build failed" }

Write-Host "=== 3. unit tests ==="
dotnet test "$Root/tests/unit/unit.csproj"
if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }

Write-Host "=== 3.5 runtime C++ tests (test_object / test_gc) ==="
cmake -S "$Root/tests/runtime" -B "$Root/build/tests_runtime" -G "MinGW Makefiles" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "runtime tests cmake configure failed" }
cmake --build "$Root/build/tests_runtime" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "runtime tests cmake build failed" }
& "$Root/build/tests_runtime/test_object.exe"
if ($LASTEXITCODE -ne 0) { throw "test_object failed" }
& "$Root/build/tests_runtime/test_gc.exe"
if ($LASTEXITCODE -ne 0) { throw "test_gc failed" }
Write-Host "runtime C++ tests passed (test_object + test_gc)"

Write-Host "=== 4. e2e mingw ==="
pwsh -NoProfile -File "$Root/scripts/run-tests.ps1" -Compiler mingw
if ($LASTEXITCODE -ne 0) { throw "e2e mingw failed" }

Write-Host "=== 5. e2e msvc ==="
pwsh -NoProfile -File "$Root/scripts/run-tests.ps1" -Compiler msvc
if ($LASTEXITCODE -ne 0) { throw "e2e msvc failed" }

Write-Host "=== 6. e2e wsl ==="
pwsh -NoProfile -File "$Root/scripts/run-tests.ps1" -Compiler wsl
if ($LASTEXITCODE -ne 0) { throw "e2e wsl failed" }

Write-Host "=== 7. C API (mingw/msvc/wsl) ==="
pwsh -NoProfile -File "$Root/scripts/run-capi.ps1" -Compiler mingw
if ($LASTEXITCODE -ne 0) { throw "capi mingw failed" }
pwsh -NoProfile -File "$Root/scripts/run-capi.ps1" -Compiler msvc
if ($LASTEXITCODE -ne 0) { throw "capi msvc failed" }
pwsh -NoProfile -File "$Root/scripts/run-capi.ps1" -Compiler wsl
if ($LASTEXITCODE -ne 0) { throw "capi wsl failed" }

Write-Host "=== CI PASS ==="
exit 0
