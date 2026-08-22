# E2E Report — e2e_il (System.Threading.Interlocked end-to-end)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_il\`
Pipeline: C# (net9 refs) → il2cppc transpile → MinGW cmake native build → run

## Result

| Metric | Value |
|---|---|
| Checks | 23 |
| Managed oracle (net9) | 23 PASS + ALL-PASS, exit 0 |
| Native (il2cpp → MinGW) | 23 PASS + ALL-PASS, exit 0 |
| Output diff oracle vs native | **0 lines (byte-identical, 24/24)** |

## Coverage (old-value semantics asserted at every step)

| Section | Checks | Contract verified |
|---|---|---|
| Add int | add_int_new_value, add_int_negative | returns NEW sum; location updated; negative addend |
| Add long | add_long_new_value, add_long_negative | returns NEW sum (64-bit); negative addend |
| Exchange int | exchange_int_old_value | returns OLD; stores NEW |
| Exchange long | exchange_long_old_value | returns OLD (64-bit) |
| Exchange float | exchange_float_old_value | returns OLD; stores NEW |
| Exchange double | exchange_double_old_value | returns OLD; stores NEW |
| Exchange object | exchange_object_old_value, exchange_object_twice | ReferenceEquals identity both directions, twice in sequence |
| CompareExchange int | cas_int_hit, cas_int_miss | hit: returns OLD + writes; miss: returns CURRENT, unchanged |
| CompareExchange long | cas_long_hit, cas_long_miss | same, 64-bit |
| CompareExchange double | cas_double_hit, cas_double_miss | same, double |
| Generic Exchange\<int\> | exchange_generic_int | explicit type-arg call → int atomic, returns OLD |
| Inc/Dec chains int | increment_chain_int, decrement_chain_int | each link returns NEW value; final location exact |
| Inc/Dec chains long | increment_chain_long, decrement_chain_long | same, 64-bit |
| Inc/Dec loop chain | increment_loop_chain, decrement_loop_chain | 10x Inc returns 6..15 (sum 105), then 10x Dec returns 14..5 (sum 200) |

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- csc: `C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll` (-nostdlib; refs: System.Private.CoreLib, System.Runtime, System.Console, System.Threading)
- managed oracle: `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_il.exe`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake + MinGW g++, generator `MinGW Makefiles`, `--parallel 16`

## Pipeline evidence

1. csc compile → exit=0 (`out\e2e_il.exe`)
2. Oracle run → 23 PASS + ALL-PASS, exit=0 (`oracle_net9.txt`)
3. Transpile → exit=0, `generated 1 type(s) -> gen`
4. cmake configure → exit=0; build → `[100%] Built target il2cpp_gen_exe`, exit=0
5. Native run → 23 PASS + ALL-PASS, exit=0 (`native_mingw.txt`)
6. `Compare-Object oracle native` → diff lines = 0

Generated wiring spot-check ([READ:gen/src/E2eIlProbe_Program.cpp]): all icalls present
(`il2cpp_interlocked_add/add64/exchange/exchange64/exchange_double/exchange_pointer/
compare_exchange/compare_exchange64/compare_exchange_double/increment/decrement/
increment64/decrement64`); generic `Exchange<int>` lowered to
`il2cpp_interlocked_exchange`; float locals widen to `double` slots so the
`exchange_double` location arg is a correctly-typed `double*`.

## Defect found & fixed during this E2E

**Generic Interlocked dispatch never fired (transpile LOUD-failed).**

- Symptom: `il2cppc error: generic instantiation of System.Threading.Interlocked.Exchange has no BCL mapping in M5` (MethodRefResolver.cs:326).
- Root cause: `Interlocked.Exchange<T>` is a generic *method* on a non-generic type — its MemberRef signature encodes T as **MVAR 0x1E** (ReadParamByte returns it verbatim). The P6 matchers accepted only **VAR 0x13** (the generic-*type* convention copied from List/Dictionary). Match failed → null → LOUD throw.
- Fix (minimal, 2 predicates): `MethodMap.Interlocked.cs` — generic Exchange/CompareExchange arms now accept `ps[0] is 0x13 or 0x1E`. Backup: `tmp/backups/MethodMap.Interlocked.cs.20260822-044742.bak`.
- Blocker fixed en route: `CodeGenerator.cs` had a literal duplicate switch arm (`"System.Converter`2_int_int" => "nullptr"` at :3541 duplicating :3521) → CS8510, tree uncompilable before my session. Removed the unreachable duplicate (compiler-proven dead code). Backup: `tmp/backups/CodeGenerator.cs.<ts>.bak`.

## Regression verification

- Transpiler rebuild: 0 warnings / 0 errors.
- xunit unit suite: 1807 total → **19 failed / 1788 passed — identical before and after my edits** (empirically proven: restored pre-edit source, filtered rerun of the failing classes → same 19 failures; re-applied fix → same 19 failures).
- All 19 are PRE-EXISTING stale tests asserting the old "ThrowsLoud" contract against domains later wired by P6 commits (not mine): MethodMapTestsInterlocked.Resolve_{Exchange,CompareExchange}_RefDouble_ThrowsLoud (stale vs commit 410c5aa float/double wiring), Math SinCos/ModF, Environment ThreadId, Linq OrderBy/Where, Convert narrowing family, DateTime DaysInMonth. Left untouched — they belong to the respective wiring agents.
- Runtime C++ suite (earlier capture today): 154/154 PASS.

## Artifacts

- `E:\il2cpp\tmp\e2e_il\Program.cs` — test program
- `out\e2e_il.exe` + `.runtimeconfig.json`, `gen\`, `build_mingw\il2cpp_gen_exe.exe`
- `oracle_net9.txt`, `native_mingw.txt`, `transpile.log`

**RESULT: ALL-PASS — 23/23 checks byte-identical between net9 oracle and MinGW native executable, exit code 0.**
