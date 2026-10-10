$ErrorActionPreference = "Stop"
#
# Division of labor (with build-unit-fixtures.ps1):
#   * This script: BCL/tooling fixtures for tests/fixtures/* + benchmark fixtures in tests/bench-array/out.
#   * build-unit-fixtures.ps1: tests/cases/<name>/obj/Release/net48/<name>.exe
#     (the 29 case fixtures referenced by unit tests; non-overlapping, each idempotently re-runnable).
#   fresh scratch/CI order: build-fixtures.ps1 -> build-unit-fixtures.ps1 -> dotnet test tests/unit.
# Resolve the repo root from the script location, avoiding relative-path failures when CWD is
# undefined in -File mode (same convention as run-tests.ps1/ci.ps1)
$Root = Split-Path -Parent $PSScriptRoot
dotnet build "$Root/tests/fixtures/calc/calc.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture build failed" }
# net48 Exe target produces calc.exe (.NET Framework 4.8, run directly for reference-output comparison).
# The metadata loader/tests reference the unified calc.dll name, so copy a same-named PE (PEReader ignores the extension).
Copy-Item "$Root/tests/fixtures/out/calc.exe" "$Root/tests/fixtures/out/calc.dll" -Force
# convw: Console.Write(int) + Console.WriteLine() with no args (for MethodRefResolver icall dispatch tests).
dotnet build "$Root/tests/fixtures/convw/convw.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture convw build failed" }
Copy-Item "$Root/tests/fixtures/out/convw.exe" "$Root/tests/fixtures/out/convw.dll" -Force
# overloads: Add(int,int) and Add(int,int,int) overloads (for method-name hashing with signature + method-body signature matching tests).
dotnet build "$Root/tests/fixtures/overloads/overloads.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture overloads build failed" }
Copy-Item "$Root/tests/fixtures/out/overloads.exe" "$Root/tests/fixtures/out/overloads.dll" -Force
# capicalc: C API export fixture -- the Main class contains a 4-arg static method Add4 (M4 export signature).
dotnet build "$Root/tests/fixtures/capicalc/capicalc.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture capicalc build failed" }
Copy-Item "$Root/tests/fixtures/out/capicalc.exe" "$Root/tests/fixtures/out/capicalc.dll" -Force
# uintcmp: uint unsigned comparison (audit H1) -- Cgt_un/Clt_un must be emitted with unsigned semantics.
dotnet build "$Root/tests/fixtures/uintcmp/uintcmp.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture uintcmp build failed" }
Copy-Item "$Root/tests/fixtures/out/uintcmp.exe" "$Root/tests/fixtures/out/uintcmp.dll" -Force
# refarr: reference-element array (audit C4) + reference parameters as GC roots (audit C1).
dotnet build "$Root/tests/fixtures/refarr/refarr.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture refarr build failed" }
Copy-Item "$Root/tests/fixtures/out/refarr.exe" "$Root/tests/fixtures/out/refarr.dll" -Force
# boxlong: box a non-Int32 value type (audit H3) -- used only for a negative test (Generate should loudly reject).
dotnet build "$Root/tests/fixtures/boxlong/boxlong.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture boxlong build failed" }
Copy-Item "$Root/tests/fixtures/out/boxlong.exe" "$Root/tests/fixtures/out/boxlong.dll" -Force
# props: property folding (iron rule 2j) -- manual/auto/static/virtual properties + computed properties (not folded).
dotnet build "$Root/tests/fixtures/props/props.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture props build failed" }
Copy-Item "$Root/tests/fixtures/out/props.exe" "$Root/tests/fixtures/out/props.dll" -Force
# arrneg: loop-bound proof negative cases (param array + i+1 index must keep the check) + get_length upper-bound positive cases.
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
# bytearr: newarr System.Byte -> (1,false) (net-domain T0) -- byte[] allocation/ldelem.u1/stelem.i1.
dotnet build "$Root/tests/fixtures/bytearr/bytearr.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture bytearr build failed" }
Copy-Item "$Root/tests/fixtures/out/bytearr.exe" "$Root/tests/fixtures/out/bytearr.dll" -Force
# boxenum: enum box (user TypeDef + BCL TypeRef) -> int64 carrier (timecase work order 2 follow-up).
dotnet build "$Root/tests/fixtures/boxenum/boxenum.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture boxenum build failed" }
Copy-Item "$Root/tests/fixtures/out/boxenum.exe" "$Root/tests/fixtures/out/boxenum.dll" -Force
# mainargs: Main(string[] args) entry signature -- entry must build a string[] from argv and pass it (recurse work order).
dotnet build "$Root/tests/fixtures/mainargs/mainargs.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture mainargs build failed" }
Copy-Item "$Root/tests/fixtures/out/mainargs.exe" "$Root/tests/fixtures/out/mainargs.dll" -Force

# multidim: multidim array param signature (0x14) -- multidim-probe wall D decode fixture.
dotnet build "$Root/tests/fixtures/multidim/multidim.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture multidim build failed" }
Copy-Item "$Root/tests/fixtures/out/multidim.exe" "$Root/tests/fixtures/out/multidim.dll" -Force
# pinvokedelegate fixture was moved to tests/cases/pinvokedelegate by P-D2
# (e2e case, built via run-tests.ps1); no longer a fixtures/ entry.

# fnptrret (P-D3 P0): a DllImport RETURN of fnptr type (delegate* unmanaged[Cdecl])
# must transpile to a full-width void* extern decl + void* result temp (was
# silent int32_t truncation of the x64 address). Unit-only fixture.
dotnet build "$Root/tests/fixtures/fnptrret/fnptrret.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture fnptrret build failed" }
Copy-Item "$Root/tests/fixtures/out/fnptrret.exe" "$Root/tests/fixtures/out/fnptrret.dll" -Force

# fnptrparam (P-D3 P1): a DllImport PARAM of fnptr type must transpile to a
# void* extern decl (was LOUDLY rejected; P-D3 wires F:->void*). Unit-only fixture.
dotnet build "$Root/tests/fixtures/fnptrparam/fnptrparam.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture fnptrparam build failed" }
Copy-Item "$Root/tests/fixtures/out/fnptrparam.exe" "$Root/tests/fixtures/out/fnptrparam.dll" -Force

# fnptrfield (P-D3 P1): a `delegate*` INSTANCE field must make
# CodeGenerator.Generate LOUDLY reject (NotSupportedException) -- the old path
# silently downgraded the field to an int32_t storage slot (write void* ->
# compile fail; read -> x64 truncation). Unit-only fixture (transpile EXPECTED
# to fail, same rule as pinvokedelegate).
dotnet build "$Root/tests/fixtures/fnptrfield/fnptrfield.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture fnptrfield build failed" }
Copy-Item "$Root/tests/fixtures/out/fnptrfield.exe" "$Root/tests/fixtures/out/fnptrfield.dll" -Force

# fnptrretuser (P-D3 P1): a USER method returning a `delegate*` (fnptr 0x1B)
# must be LOUDLY rejected by ReturnTypeCpp (no 0x1B arm). P-D3 red-test lock
# only (wiring is a follow-up). Unit-only fixture (transpile EXPECTED to fail).
dotnet build "$Root/tests/fixtures/fnptrretuser/fnptrretuser.csproj" -c Release -o "$Root/tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture fnptrretuser build failed" }
Copy-Item "$Root/tests/fixtures/out/fnptrretuser.exe" "$Root/tests/fixtures/out/fnptrretuser.dll" -Force
