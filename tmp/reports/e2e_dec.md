# E2E Report: e2e_dec — decimal.Parse probe (net9 refs / transpiler / MinGW)

- Date: 2026-08-22
- Exclusive dir: `E:\il2cpp\tmp\e2e_dec\`
- Verdict: **ALL-PASS** (4/4 checks, oracle vs native byte-identical, both exit 0)

## Pipeline (standard: net9 refs → transpiler → MinGW → own program)

| Stage | Command | Result |
|---|---|---|
| Build C# probe against net9 ref assemblies | `dotnet build e2e_dec.csproj -c Release -o out` | 0 errors, 0 warnings |
| Oracle run on private .NET 9.0.19 runtime | `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_dec.dll > expected.txt` | rc=0 |
| Transpile IL → C++ | `dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_dec.dll gen` | exit=0, 1 type(s) |
| MinGW cmake configure | `cmake -S gen -B build_mingw -G "MinGW Makefiles"` | exit=0 |
| MinGW build | `cmake --build build_mingw` | 100%, il2cpp_gen_exe.exe linked |
| Native run | `build_mingw\il2cpp_gen_exe.exe > actual.txt` | rc=0 |
| Byte diff | MD5(expected) vs MD5(actual) | **IDENTICAL** (`BCA8F51599062E7348118B7D66AFC47E`) |

## net9 refs wiring (SDK 8.0.205 host)

Direct `<TargetFramework>net9.0</TargetFramework>` fails with NETSDK1045 and a
direct `PackageReference` on `Microsoft.NETCore.App.Ref` fails with
NU1202/NU1213 (DotnetPlatform package). Working recipe:

- TFM net8.0 + `DisableImplicitFrameworkReferences=true` + `UseAppHost=false`
- explicit `<Reference HintPath>` into
  `$(NuGetPackageRoot)microsoft.netcore.app.ref\9.0.6\ref\net9.0\`
  (`System.Runtime.dll`, `System.Console.dll`)
- oracle executed by the private runtime via hand-pinned
  `out/e2e_dec.runtimeconfig.json` → `Microsoft.NETCore.App 9.0.19`

## Program under test

`Program.cs` uses **ONLY `decimal.Parse`** — zero decimal literals anywhere
(grep for decimal-literal patterns over the source: no matches). This routes
around the known emitter defect for decimal literal constants.

## Checks (native output = oracle output, byte-for-byte)

```
t1_add_01_02_03=PASS        Parse("0.1")+Parse("0.2")==Parse("0.3")   [t1_sum=0.3]
t2_mul_2_3_6=PASS           Parse("2")*Parse("3")==Parse("6")         [t2_prod=6]
t3_compare_ordering=PASS    Compare chain -2.5<0<0.0001<1<123.45,
                            -999.99<-2.5, scale-eq "1.10"=="1.100"
                            [cmp_neg_zero=-1 cmp_zero_tiny=-1 cmp_one_mid=-1 cmp_scale_eq=0]
t4_roundtrip_123_45=PASS    Parse("123.45").ToString()=="123.45"      [t4_str=123.45]
ALL-PASS
```

Per-check PASS lines plus the final ALL-PASS were printed by BOTH the net9
oracle process and the transpiled native executable; stdout bytes identical
(CRLF, ASCII), exit codes 0/0.

## Artifacts

- `tmp/e2e_dec/Program.cs`, `tmp/e2e_dec/e2e_dec.csproj`
- `tmp/e2e_dec/out/` (net9-ref IL build), `expected.txt`, `oracle_err.txt` (empty)
- `tmp/e2e_dec/gen/` (transpiled CMake project)
- `tmp/e2e_dec/build_mingw/il2cpp_gen_exe.exe`, `actual.txt`, `native_err.txt` (empty)

No git operations performed; no `.opencode` state written.
