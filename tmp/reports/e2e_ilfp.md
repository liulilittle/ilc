# E2E Report — e2e_ilfp (Interlocked float/double/generic Exchange)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_ilfp\`
Pipeline: net9 refs → managed oracle → il2cppc transpile → MinGW cmake → native run → byte-exact diff

## Result

**ALL-PASS — 12/12 checks PASS in the native generated executable; stdout byte-exact identical to the .NET 9 managed oracle (372 B == 372 B); both exit codes 0; stderr empty on both sides.**

## Inputs

- net9 refs/oracle: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\` (+ `net9\dotnet.exe`)
- csc: `C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll` (`-nostdlib`, refs: System.Private.CoreLib/System.Runtime/System.Console)
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ `C:\Program Files\ming-w64\bin`, generator `MinGW Makefiles`

## Program under test (`Program.cs`, namespace E2eIlFp)

12 deterministic ASCII checks, each printing `PASS <name>` / `FAIL <name>`, terminated by `ALL-PASS` (or `ALL-FAIL failures=N`). -0.0 detected arithmetically (`v == 0 && 1/v < 0`) — no extra API surface:

| Check | Semantics exercised |
|---|---|
| F1 | `Exchange(ref float)` returns OLD (1.5f→2.5f) |
| F2 | `Exchange(ref float)` OLD side carries exact -0.0f pattern |
| F3 | `Exchange(ref float)` NEW side lands as exact -0.0f |
| D1–D3 | same three for `Exchange(ref double)` |
| C1 | `CompareExchange(ref double)` hit: ret OLD 5.5, stores 6.5 |
| C2 | miss: comparand 100.0 → store rejected, ret current 6.5 |
| C3 | **+0.0 location vs -0.0 comparand MUST MISS** (numerically equal, bitwise distinct) |
| C4 | **-0.0 location vs -0.0 comparand HITS** (bitwise equal) |
| G1/G2 | generic `Interlocked.Exchange<long>(ref, …)` old-value semantics, positive + negative |

Oracle run first proved real .NET 9 semantics match expectations (bitwise fp CAS): 12/12 PASS + ALL-PASS, exit 0.

## Pipeline evidence

1. **Compile**: csc exit=0 → `out\e2e_ilfp.exe`; hand-written `out\e2e_ilfp.runtimeconfig.json` (Microsoft.NETCore.App 9.0.19).
2. **Managed oracle**: `net9\dotnet.exe out\e2e_ilfp.exe` → 12× PASS + ALL-PASS, exit=0 (captured `oracle.txt`, 372 B).
3. **Transpile** (first attempt): **FAILED** — `il2cppc error: generic instantiation of System.Threading.Interlocked.Exchange has no BCL mapping in M5` (exit=2). See defect below.
4. **Transpile** (after fix): exit=0, `generated 1 type(s) -> gen`. Generated `E2eIlFp_Program.cpp` wires all 12 callsites to the correct icalls: 6× `il2cpp_interlocked_exchange_double`, 4× `il2cpp_interlocked_compare_exchange_double`, 2× `il2cpp_interlocked_exchange64` (generic long) — verified by grep over `gen/src/E2eIlFp_Program.cpp:169-270`.
5. **MinGW build**: configure + `cmake --build --parallel 16` exit=0 → `build_mingw\il2cpp_gen_exe.exe`.
6. **Native run**: 12× PASS + ALL-PASS, exit=0, stderr empty (`native.txt`, 372 B).
7. **Diff**: `[System.Linq.Enumerable]::SequenceEqual(oracle, native) = True`.

## Defect found & root cause (transpiler)

**Symptom**: transpile LOUD-failed on the generic `Exchange<long>` call.

**Evidence** (metadata probe over `out\e2e_ilfp.exe`): the cross-assembly MemberRef signature is
`[10 01 | 02 | 1E 00 | 10 1E 00 | 1E 00]` = GENERIC(1 param), count=2, **ret = MVAR(0x1E)+index 0x00**, p0 = BYREF MVAR, p1 = MVAR.

**Root cause chain**:
1. `MethodRefResolver.RawSignature` consumed trailing data for GENERICINST/TypeDefOrRef/SZARRAY returns but NOT for bare VAR/MVAR returns → the return's compressed index byte leaked into ps[0], shifting every param: ps read `[0x00, 0x1E]` instead of `[0x1E, 0x1E]`. (`MethodSignatureCoder.EncodeReturn` already handled this exact case — genlist precedent.)
2. The P6 generic gates in `MethodMap.Interlocked.cs` matched only `ps[0] == 0x13` (VAR); a generic METHOD's own type parameter arrives as MVAR `0x1E`, so even an aligned ps would have missed.

**Fixes** (backups in `tmp\e2e_ilfp\backups\*.20260822-045207.bak`):
- `Emit\MethodRefResolver.cs` — added `else if (ret is 0x13 or 0x1E) _ = sig.ReadCompressedInteger();` to RawSignature's return-consumption chain (this session).
- `Emit\MethodMap.Interlocked.cs` — generic gates widened to `ps[0] is 0x13 or 0x1E` (landed by a concurrent wiring agent at 04:49, mid-session; my duplicate hunk was removed after merge-conflict detection, leaving one coherent fix).

## Verification after fix

| Check | Outcome |
|---|---|
| il2cppc rebuild | 0 errors, 0 warnings |
| e2e pipeline (steps 4-7 above) | ALL-PASS, byte-exact, exit 0 |
| `run-tests.ps1 -Compiler mingw -Case interlockedcase` (int32/int64 Interlocked regression through the same resolver) | **PASS 1/1** |
| Transpiler unit suite | 1788/1807; 19 failures — see attribution below |

### Unit-suite failure attribution (not caused by this fix)

All 19 failures are stale `ThrowsLoud` tests asserting that shapes now mapped by today's concurrent wiring agents must throw. Proof of non-involvement of the RawSignature change: it executes only when the RETURN byte is 0x13/0x1E; every failing test synthesizes signatures with other return bytes (verified per-test: 0x08/0x01/0x0D/0x05/0x15…), so the new branch is unreachable in all of them. Failure domains map exactly onto files modified today by other agents (`MethodMap.Convert*.cs` 04:38-04:39, `MethodMap.MathExtra.cs` 03:53, `MethodMap.Environment*.cs` 04:15-04:19, Linq/TimeExt2 suites) plus the pre-P6-stale `Resolve_Exchange_RefDouble_ThrowsLoud` / `Resolve_CompareExchange_RefDouble_ThrowsLoud` pair (written 2026-08-07 "fp atomics have no icall"; P6 wired them and matches their param bytes regardless of the tests' mismatched ret byte — already red before this session's edits).

## Files touched outside exclusive dir

- `src/il2cppc/Emit/MethodRefResolver.cs` (one else-if branch; backup taken)
- No git operations. Backups: `tmp/e2e_ilfp/backups/`.

## Artifacts

`tmp\e2e_ilfp\`: Program.cs, out\ (exe+runtimeconfig), oracle.txt/oracle_err.txt, gen\, build_mingw\, native.txt/native_err.txt, backups\
