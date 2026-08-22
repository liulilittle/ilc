# E2E Math — System.Math / System.MathF domain

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_math\`
Pipeline: net9 refs → transpiler (il2cppc) → MinGW → byte-exact diff
Result: **ALL-PASS — 37/37 checks, stdout SHA-256 identical to the .NET 9 oracle**

## Verdict

| Stage | Result |
|---|---|
| S1 csc compile (net9 refs) | EXITCODE=0 |
| S2 oracle run (.NET 9.0.19 local runtime) | exit 0, 37 PASS + ALL-PASS |
| S3 il2cppc transpile | EXITCODE=0, 1 type generated |
| S4 cmake MinGW configure + build | EXITCODE=0 (100%) |
| S5 transpiled exe run | exit 0, stderr empty, **37 PASS + ALL-PASS** |
| Diff oracle vs transpiled | **BYTE-EXACT** (both 772 bytes, SHA-256 `9E01C2E2…F99C6D`) |

## Pipeline commands

1. Compile against net9 reference assemblies (Roslyn csc, `-nostdlib+`):
   ```
   dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -nostdlib+ -noconfig -target:exe -langversion:latest ^
     -out:"E:\il2cpp\tmp\e2e_math\e2e_math_net9.dll" ^
     -r:"E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\System.Private.CoreLib.dll" ^
     -r:"E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\System.Console.dll" ^
     -r:"E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\System.Runtime.dll" ^
     "E:\il2cpp\tmp\e2e_math\e2e_math.cs"
   ```
   → EXITCODE=0 (`e2e_math_net9.runtimeconfig.json` written beside the DLL for the oracle host).

2. Oracle run on the local .NET 9 runtime:
   ```
   E:\il2cpp\tmp\net910\net9\dotnet.exe E:\il2cpp\tmp\e2e_math\e2e_math_net9.dll   # > expected.txt
   ```
   → exit 0; 37× `PASS …` + `ALL-PASS`.

3. Transpile:
   ```
   dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll e2e_math_net9.dll gen
   ```
   → EXITCODE=0; string pool 41 literals / 4 methods; 221 temp slots → 184 reused; 1 type generated.

4. Build (MinGW):
   ```
   cmake -S gen -B build_mingw -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
   cmake --build build_mingw
   ```
   → both EXITCODE=0; links `build_mingw\il2cpp_gen_exe.exe`.

5. Run + compare:
   ```
   build_mingw\il2cpp_gen_exe.exe   # > actual.txt ; stderr empty
   ```
   → exit 0; `Compare-Object` on lines: none; SHA-256(expected)==SHA-256(actual)
   = `9E01C2E2846E3A48B5E490AC422B11AD3985758C67B388B692EAEF29EEF99C6D`.

## Coverage (all bound as runtime icalls in gen/src/Program.cpp)

Math (double unless noted): Sqrt, Pow, Abs(int/long/double), Min(int/double),
Max(int/double), Floor, Ceiling, Round (default MidpointRounding.ToEven,
banker's: 0.5→0, 1.5→2, 2.5→2, 3.5→4, −1.5→−2), Truncate, Log (+Log(x, base)),
Log10, Log2, Sign(int/long), IEEERemainder, DivRem(int,int,out int;
long,long,out long), Clamp(int,int,int).

MathF: Sqrt, Pow, Abs, Min, Max, Floor, Ceiling, Round (banker's), Truncate,
Log, Log2, Log10, Sign, IEEERemainder.

Generated-code binding check (occurrences in `gen/src/Program.cpp`): 114 total
`icall_math*` references across 37 distinct symbols — every listed function is a
real call into the runtime icalls (nothing constant-folded away):
`icall_math_{abs_i,abs_l,abs_d,min_i,min_d,max_i,max_d,sqrt_d,pow_d,floor_d,
ceiling_d,round_d,truncate_d,log_d,log_base_d,log10_d,log2_d,sign_i,sign_l,
ieee_remainder_d,divrem_i,divrem_l,clamp_i}` and
`icall_mathf_{sqrt_f,pow_f,abs_f,min_f,max_f,floor_f,ceiling_f,round_f,
truncate_f,log_f,log2_f,log10_f,sign_i,ieee_remainder_f}`.

Full per-check list (oracle and transpiled outputs identical, all PASS):
math.sqrt.exact, math.sqrt.irrational, math.pow.integral, math.pow.fractional,
math.abs.i, math.abs.l, math.abs.d, math.minmax.i, math.minmax.d,
math.floorceil, math.round.banker, math.round.plain, math.truncate,
math.log.natural, math.log.base, math.log10, math.log2, math.sign.i,
math.sign.l, math.ieeeremainder, math.divrem.i, math.divrem.l, math.clamp.i,
mathf.sqrt.exact, mathf.sqrt.irrational, mathf.pow, mathf.abs, mathf.minmax,
mathf.floorceil, mathf.round.banker, mathf.round.plain, mathf.truncate,
mathf.log, mathf.log2, mathf.log10, mathf.sign, mathf.ieeeremainder → ALL-PASS.

## Exclusions (unwired surface, documented not skipped silently)

- `Math.Clamp(long/double)` — only `icall_math_clamp_i` (int32) exists;
  TryMapMathExtra routes ANY 3-arg Clamp to the int32 icall, so only the int
  form is asserted.
- `Math.Sign(double)` — MethodMap.Math.cs routes non-int32 Sign to `sign_l`
  (int64 slot); a double arg would truncate. Only Sign(int)/Sign(long) asserted.
- `MathF.Clamp` — no MethodMap.MathF case (would fall to the generated-name
  path and fail the C++ build).
- `MathF.DivRem` — no such BCL member.
- NaN operand ordering for Min/Max — net48 contract (NaN loses) vs .NET Core
  3+ IEEE-754-2019 propagation is a documented divergence, out of scope; no
  NaN operands fed to Min/Max.

## Artifacts

`E:\il2cpp\tmp\e2e_math\`: e2e_math.cs, e2e_math_net9.dll,
e2e_math_net9.runtimeconfig.json, expected.txt (oracle), actual.txt
(transpiled), actual_err.txt (empty), gen\, build_mingw\.
