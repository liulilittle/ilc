$ErrorActionPreference = "Stop"
#
# 分工（与 build-unit-fixtures.ps1）：
#   * 本脚本：tests/fixtures/* 的 BCL/工具夹具 + tests/bench-array/out 基准夹具。
#   * build-unit-fixtures.ps1：tests/cases/<name>/obj/Release/net48/<name>.exe
#     （unit 引用到的 29 例 case fixture；互不重叠、可各自幂等重跑）。
#   fresh scratch/CI 顺序：build-fixtures.ps1 -> build-unit-fixtures.ps1 -> dotnet test tests/unit。
# 以脚本位置为基准解析仓库根，避免 -File 模式下 CWD 不确定导致相对路径失效（与 run-tests.ps1/ci.ps1 同约定）
$Root = Split-Path -Parent $PSScriptRoot
dotnet build "$Root/tests/fixtures/calc/calc.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture build failed" }
# net48 Exe 目标产物是 calc.exe（.NET Framework 4.8 直接运行，用于参考输出对比）。
# 元数据加载器/测试引用统一的 calc.dll 名，故复制一份同名 PE（PEReader 不区分扩展名）。
Copy-Item "$Root/tests/fixtures/out/calc.exe" "$Root/tests/fixtures/out/calc.dll" -Force
# convw：Console.Write(int) + Console.WriteLine() 空参（MethodRefResolver icall 分派测试用）。
dotnet build "$Root/tests/fixtures/convw/convw.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture convw build failed" }
Copy-Item "$Root/tests/fixtures/out/convw.exe" "$Root/tests/fixtures/out/convw.dll" -Force
# overloads：Add(int,int) 与 Add(int,int,int) 重载（方法名哈希含签名 + 方法体签名匹配测试用）。
dotnet build "$Root/tests/fixtures/overloads/overloads.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture overloads build failed" }
Copy-Item "$Root/tests/fixtures/out/overloads.exe" "$Root/tests/fixtures/out/overloads.dll" -Force
# capicalc：C API 导出 fixture——Main 类含 4 参静态方法 Add4（M4 导出签名）。
dotnet build "$Root/tests/fixtures/capicalc/capicalc.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture capicalc build failed" }
Copy-Item "$Root/tests/fixtures/out/capicalc.exe" "$Root/tests/fixtures/out/capicalc.dll" -Force
# uintcmp：uint 无符号比较 (audit H1)——Cgt_un/Clt_un 必须按无符号语义发射。
dotnet build "$Root/tests/fixtures/uintcmp/uintcmp.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture uintcmp build failed" }
Copy-Item "$Root/tests/fixtures/out/uintcmp.exe" "$Root/tests/fixtures/out/uintcmp.dll" -Force
# refarr：引用元素数组 (audit C4) + 引用参数作 GC 根 (audit C1)。
dotnet build "$Root/tests/fixtures/refarr/refarr.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture refarr build failed" }
Copy-Item "$Root/tests/fixtures/out/refarr.exe" "$Root/tests/fixtures/out/refarr.dll" -Force
# boxlong：box 非 Int32 值类型（audit H3）——仅用于负测试（Generate 应 loud 拒绝）。
dotnet build "$Root/tests/fixtures/boxlong/boxlong.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture boxlong build failed" }
Copy-Item "$Root/tests/fixtures/out/boxlong.exe" "$Root/tests/fixtures/out/boxlong.dll" -Force
# props：属性折叠（铁律 2j）——手动/自动/静态/虚拟属性 + 计算属性（不折叠）。
dotnet build "$Root/tests/fixtures/props/props.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture props build failed" }
Copy-Item "$Root/tests/fixtures/out/props.exe" "$Root/tests/fixtures/out/props.dll" -Force
# arrneg：循环边界证明负例（param 数组 + i+1 索引必须保持检查）+ get_length 上界正例。
dotnet build "$Root/tests/fixtures/arrneg/arrneg.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture arrneg build failed" }
Copy-Item "$Root/tests/fixtures/out/arrneg.exe" "$Root/tests/fixtures/out/arrneg.dll" -Force

# extra unit prerequisites (Task A7 verify-06 fix): bench-array + resx.
#   * bench-array: CodeGeneratorTests reads tests/bench-array/out/{bench_array_core.exe,bench_array.exe}.
#   * resx: CliTests/ResourceTests read tests/fixtures/out/resx.exe (2 embedded manifest resources).
#     The pre-fix script never built it (masked in the live repo by a stale exe).
dotnet build "$Root/tests/bench-array/bench_array_core.csproj" -c Release -o "$Root/tests/bench-array/out"
if ($LASTEXITCODE -ne 0) { throw "fixture bench_array_core build failed" }
dotnet build "$Root/tests/bench-array/bench_array.csproj" -c Release -o "$Root/tests/bench-array/out"
if ($LASTEXITCODE -ne 0) { throw "fixture bench_array build failed" }
foreach ($be in @("bench_array_core.exe", "bench_array.exe")) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root "tests/bench-array/out/$be"))) { throw "bench-array fixture missing: $be" }
}
dotnet build "$Root/tests/fixtures/resx/resx.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture resx build failed" }
Copy-Item "$Root/tests/fixtures/out/resx.exe" "$Root/tests/fixtures/out/resx.dll" -Force
if (-not (Test-Path -LiteralPath "$Root/tests/fixtures/out/resx.exe")) { throw "resx fixture missing: resx.exe" }
Write-Host "fixtures built OK"
# bytearr：newarr System.Byte → (1,false)（net-domain T0）——byte[] 分配/ldelem.u1/stelem.i1。
dotnet build "$Root/tests/fixtures/bytearr/bytearr.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture bytearr build failed" }
Copy-Item "$Root/tests/fixtures/out/bytearr.exe" "$Root/tests/fixtures/out/bytearr.dll" -Force
# boxenum：枚举 box（用户 TypeDef + BCL TypeRef）→ int64 carrier（timecase 施工单 2 后续）。
dotnet build "$Root/tests/fixtures/boxenum/boxenum.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture boxenum build failed" }
Copy-Item "$Root/tests/fixtures/out/boxenum.exe" "$Root/tests/fixtures/out/boxenum.dll" -Force
# mainargs：Main(string[] args) 入口签名——entry 必须从 argv 构造 string[] 传参（recurse 施工单）。
dotnet build "$Root/tests/fixtures/mainargs/mainargs.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture mainargs build failed" }
Copy-Item "$Root/tests/fixtures/out/mainargs.exe" "$Root/tests/fixtures/out/mainargs.dll" -Force

# multidim: multidim array param signature (0x14) -- multidim-probe wall D decode fixture.
dotnet build "$Root/tests/fixtures/multidim/multidim.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture multidim build failed" }
Copy-Item "$Root/tests/fixtures/out/multidim.exe" "$Root/tests/fixtures/out/multidim.dll" -Force
