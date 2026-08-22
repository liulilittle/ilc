# e2e_bits — E2E report: `System.BitConverter` roundtrips through the full pipeline

- **Date:** 2026-08-22
- **Exclusive dir:** `E:\il2cpp\tmp\e2e_bits\` (no git, no .opencode)
- **Pipeline:** net9 refs (`Microsoft.NETCore.App.Ref` **9.0.6**) → `dotnet build` → il2cppc transpiler → CMake + MinGW → native `il2cpp_gen_exe.exe`
- **Scope:** `GetBytes(int)`/`ToInt32` roundtrip (258, -1); `GetBytes(double)`/`ToDouble` roundtrip (3.14); `ToString(byte[])` hex form

## Wiring check (before writing the program)

- `ToString(byte[])` single-arity form **is wired** → `il2cpp_bitconverter_to_hex_string`
  (`src/il2cppc/Emit/MethodMap.BitConverter.cs:106-116`) → tested, no skip-note needed.
  (`get_IsLittleEndian` is NOT wired, same file :117-118 → deliberately not used.)
- String `==` wired (`src/il2cppc/Emit/MethodMap.String.cs:251`).
- Runtime contract: little-endian, uppercase hex pairs `-` separated, empty→""
  (`src/runtime/include/internal/bitconverter.h`).

## Build steps (all in exclusive dir)

```powershell
# 1. C# -> IL PE (net9 reference assemblies; SDK 8.0.205 compiles against the
#    cached 9.0.6 ref pack via direct HintPath references)
dotnet build e2e_bits.csproj -c Release -o out        # OK, 0 warn / 0 err -> out/e2e_bits.dll

# 2. Transpile IL -> C++ (3 types generated, EXIT=0)
dotnet run --project E:\il2cpp\src\il2cppc -- out\e2e_bits.dll gen

# 3. Native build (MinGW)
cmake -S gen -B build_mingw -G "MinGW Makefiles"      # CFG_EXIT=0
cmake --build build_mingw --config Release            # BUILD_EXIT=0 -> il2cpp_gen_exe.exe
```

csproj notes (deviations from the `tmp/e2e_sb` template, forced by toolchain):
- `PackageReference Microsoft.NETCore.App.Ref 9.0.6` under TFM net8.0 → NU1202/NU1213
  (package is DotnetPlatform-typed, net9.0-only). Replaced with direct `<Reference><HintPath>`
  into `$(NuGetPackageRoot)microsoft.netcore.app.ref\9.0.6\ref\net9.0\`
  (`System.Runtime.dll`, `System.Runtime.Extensions.dll` [BitConverter], `System.Console.dll`).
- `UseAppHost=false` (MSB3030: no apphost without implicit framework refs); output is the
  pure-IL `e2e_bits.dll`, which is what the transpiler consumes.

## Generated icall mapping (evidence)

`Select-String gen\src\*.cpp 'il2cpp_bitconverter_\w+'` → exactly the expected five:
`il2cpp_bitconverter_get_bytes_int32`, `il2cpp_bitconverter_to_int32`,
`il2cpp_bitconverter_get_bytes_double`, `il2cpp_bitconverter_to_double`,
`il2cpp_bitconverter_to_hex_string`.

## Run — transpiled native (MinGW)

```
E:\il2cpp\tmp\e2e_bits\build_mingw\il2cpp_gen_exe.exe
PASS GetBytes/ToInt32 roundtrip 258
PASS GetBytes/ToInt32 roundtrip -1
PASS GetBytes/ToDouble roundtrip 3.14
PASS ToString hex 02-01-FF
ALL-PASS
EXIT=0   STDERR=<empty>
```

## Run — managed oracle (same IL on real .NET 9 runtime)

`out\e2e_bits.runtimeconfig.json` lacks a framework section (DisableImplicitFrameworkReferences
side effect), so the oracle copy lives in `oracle\` with an explicit
`Microsoft.NETCore.App 9.0.0` framework ref, executed by the standalone net9 host
(`tmp\net910\net9\dotnet.exe`; the installed 8.0.5 runtime correctly refuses the
`System.Runtime, Version=9.0.0.0` reference — major-version binding):

```
PASS GetBytes/ToInt32 roundtrip 258
PASS GetBytes/ToInt32 roundtrip -1
PASS GetBytes/ToDouble roundtrip 3.14
PASS ToString hex 02-01-FF
ALL-PASS
EXIT=0
```

**Native output == managed oracle output, byte-for-byte.**

## Coverage matrix

| # | Requirement | Result |
|---|---|---|
| 1 | `GetBytes(258)` → `ToInt32` roundtrip identity (+ length 4) | PASS |
| 2 | `GetBytes(-1)` → `ToInt32` roundtrip identity (sign preservation) | PASS |
| 3 | `GetBytes(3.14)` → `ToDouble` roundtrip bit-exact identity (+ length 8) | PASS |
| 4 | `ToString(byte[])` hex form (wired → tested): `{02,01,FF}` → `"02-01-FF"` | PASS |
| 5 | Program prints per-check PASS lines + final ALL-PASS | PASS |
| 6 | Transcript at `E:\il2cpp\tmp\reports\e2e_bits.md` | this file |

**4/4 functional checks PASS on both engines; ALL-PASS; exit code 0.**

## Scope compliance

All artifacts confined to `E:\il2cpp\tmp\e2e_bits\` (+ this report); repo sources untouched;
no git operations; no `.opencode` usage.
