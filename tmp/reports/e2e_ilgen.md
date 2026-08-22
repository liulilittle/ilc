# E2E Report — e2e_ilgen (generic-form Interlocked atomics)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_ilgen\`
Pipeline: net9 refs → il2cppc transpile → MinGW cmake native build → run

## Result

**ALL-PASS — 9/9 checks passed in the native generated executable, exit code 0, output byte-identical to the net9 managed oracle.**

One transpiler defect was found and root-cause-fixed to make this pass (§ Transpiler fix).

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll` (rebuilt this run)
- toolchain: cmake (Clang 17.0.6 driver) + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

## Program under test (`Program.cs`, namespace E2eIlGenProbe)

Explicit GENERIC-FORM calls only:

1. `Interlocked.Exchange<int>(ref intVal, 5)` — old returned (41), new stored (5)
2. `Interlocked.CompareExchange<long>(ref longVal, 99, 123456789012345)` — hit: old comparand returned, value stored; miss (expected 42 vs actual 99): current returned, nothing stored
3. `Interlocked.Exchange<object>(ref slot, sentinelB)` — raw reference swap asserted by `ReferenceEquals` against two distinct sentinels

Each check prints one `PASS ...` line, then `ALL-PASS` (or `FAIL ...` / `ALL-FAIL failures=N`, exit 1).

Note: net9's BCL exposes these unconstrained generic overloads — csc accepted all three explicit instantiations against the real 9.0.19 refs on the first try (csc exit=0).

## Managed oracle (net9 runtime)

```
PASS Exchange<int> old=41
PASS Exchange<int> new=5
PASS CompareExchange<long> hit old=123456789012345
PASS CompareExchange<long> hit new=99
PASS CompareExchange<long> miss old=99
PASS CompareExchange<long> miss unchanged=99
PASS Exchange<object> old==sentinelA
PASS Exchange<object> slot==sentinelB
PASS Exchange<object> old!=sentinelB
ALL-PASS
managed exit=0
```

## Transpiler fix (root cause, required for this e2e)

First transpile attempt failed LOUD:

```
il2cppc error: generic instantiation of System.Threading.Interlocked.Exchange has no BCL mapping in M5
```

Root cause chain ([MethodRefResolver.cs `RawSignature`]): a MethodSpec MemberRef to `Exchange<T>(ref T, T)` carries a GENERIC signature whose RETURN type is the method's own type parameter (`ret = VAR 0x13` + compressed index). `RawSignature`'s return-type consumption chain handled `0x15/0x11/0x12/0x40/0x1D/0x1F` but had **no `0x13/0x1E` case**, so the VAR index byte leaked into `ps[0]`: decoded params became `[0x00, 0x13]` instead of `[0x13, 0x13]`. `TryMapInterlocked`'s generic gate (`ps[0]==0x13 && genArgs.Length==1`, MethodMap.Interlocked.cs) never matched → null → loud M5 throw. All three target calls share the shape (all return `T`).

Fix (one clause, mirrors `ReadParamByte`'s existing VAR/MVAR param handling):

```csharp
else if (ret is 0x13 or 0x1E) _ = sig.ReadCompressedInteger();
```

in `RawSignature`'s ret-consumption chain (`E:\il2cpp\src\il2cppc\Emit\MethodRefResolver.cs`). Safe by construction: VAR/MVAR return types only occur in generic method signatures, so no non-generic mapping can shift behavior; single caller (`BclResolveCore`). Pre-edit snapshots: `tmp\e2e_ilgen\backup_MethodRefResolver.cs`, `backup_CodeGenerator.cs`.

Rebuild: `dotnet build src\il2cppc\il2cppc.csproj -c Release` → exit=0, 0 warnings.

Side note: an unrelated pre-existing CS8510 (duplicate `"System.Converter`2_int_int"` switch arm in CodeGenerator.cs, left by another batch) briefly blocked the rebuild; it was removed by the concurrent owner while this agent was working — no action taken here beyond observing the file change.

## Generated wiring evidence (gen/src/E2eIlGenProbe_Program.cpp)

```
loc1 = il2cpp::runtime::il2cpp_interlocked_exchange((&loc0), static_cast<int32_t>(5LL));
loc3 = il2cpp::runtime::il2cpp_interlocked_compare_exchange64((&loc2), static_cast<int64_t>(99LL), static_cast<int64_t>(123456789012345LL));
loc4 = il2cpp::runtime::il2cpp_interlocked_compare_exchange64((&loc2), static_cast<int64_t>(777LL), static_cast<int64_t>(42LL));
loc8 = reinterpret_cast<Il2CppObject*>(il2cpp::runtime::il2cpp_interlocked_exchange_pointer(reinterpret_cast<void**>((&loc7)), loc6));
```

Generic `<int>` → i4 icall, generic `<long>` → i64 icall (hit AND miss sites), generic `<object>` → pointer icall with `(void**)` slot address passthrough — exactly the P6 generic paths in MethodMap.Interlocked.cs.

## Build & native run

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   -> configure exit=0
cmake --build build_mingw --parallel 16            -> [100%] Built target il2cpp_gen_exe; build exit=0
                                                     (generated code compiled clean; warnings are pre-existing runtime stubs)
.\build_mingw\il2cpp_gen_exe.exe                   -> ALL-PASS, native exit=0
```

Native stdout (identical to managed oracle, line for line):

```
PASS Exchange<int> old=41
PASS Exchange<int> new=5
PASS CompareExchange<long> hit old=123456789012345
PASS CompareExchange<long> hit new=99
PASS CompareExchange<long> miss old=99
PASS CompareExchange<long> miss unchanged=99
PASS Exchange<object> old==sentinelA
PASS Exchange<object> slot==sentinelB
PASS Exchange<object> old!=sentinelB
ALL-PASS
native exit=0
```

## Results

| Check | Managed (net9) | Native (il2cpp) |
|---|---|---|
| `Exchange<int>` returns OLD (41) / stores NEW (5) | PASS | PASS |
| `CompareExchange<long>` hit: old comparand / stored | PASS | PASS |
| `CompareExchange<long>` miss: current returned / unchanged | PASS | PASS |
| `Exchange<object>` old==sentinelA / slot==sentinelB / old!=sentinelB | PASS | PASS |

Notes:

- No git operations; no `.opencode` usage; only shared-source touch is the documented `RawSignature` fix above.
