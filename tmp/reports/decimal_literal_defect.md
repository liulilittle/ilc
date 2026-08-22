# Decimal Constant-Literal Emitter Defect — Full Analysis

**Date:** 2026-08-22 · **Agent:** research (report-only) · **Scope:** net9 E2E icallcase decimal defect
**Probe artifacts:** `E:\il2cpp\tmp\decprobe\` (probe.cs, decprobe.dll, dumpil.ps1, gen/) — no source files touched, no runtime builds, no git.

---

## 0. Executive summary

1. **Roslyn lowering (probe-proven):** a C# decimal literal (`6m`, `0.1m`, `-0.5m`, 28-digit, >int32) is lowered to **`ldc.i4`/`ldc.i8` constant pushes + `newobj System.Decimal::.ctor(int32 | int64 | int32,int32,int32,bool,uint8)`**. There is **no `ldc.r8` form and no ldtoken/data-blob form** for decimal constants (the Constant table cannot hold decimals; even the 28-digit max literal uses the 5-arg ctor). The special case `0m` lowers to **`ldsflda System.Decimal::Zero`** (BCL static field).
2. **The net9 E2E failure signature** ("icall_decimal_* writing 16 bytes through `&t10`, t10 declared `int32_t`") was **root-caused to TempSlotPass**, not to CodeGenerator's constant-fold typing: TempSlotPass.KindOf's ResultKind list lacked `"dec"`, so decimal-result temps were classified `"val"` and **merged onto shared int32 slots**; the emitter's per-name last-wins typing then declared the merged slot `int32_t` while the result-slot-first icall wrote 16 bytes through it. **Fixed today** at `TempSlotPass.cs:241-246` (+ defense-in-depth pass `CodeGenerator.cs:1132-1147`); memory.md:9 records the fix.
3. **Reproduction against the current transpiler** (built 2026-08-22 04:58): local/operand-position literals now emit **correctly typed** code (`il2cpp::runtime::Il2CppDecimal t0;` + `icall_decimal_ctor_int32((&t0), 6LL)`). The historical defect does not reproduce for literals.
4. **Two LIVE adjacent defects were found by the probe:**
   - **(L1) Decimal VALUE parameters are emitted `int32_t`** while call sites take their address as `const Il2CppDecimal*` → uncompilable output (int32_t* does not convert) / wrong ABI. Proof: generated `Probe_C_...` below.
   - **(L2) Decimal RETURN types are unsupported**: `return type 17 (VALUETYPE) is not supported by M3` LOUD throw at `CodeGenerator.cs:6791`.
5. One latent hygiene issue: the standalone pass at `CodeGenerator.cs:1145` seeds kind string `"il2cpp::Il2CppDecimal"` (missing `runtime::`) — currently always overwritten before declaration, but a landmine if propagation order ever changes.

---

## 1. The evidence site and its current state

Task-cited evidence: `tmp/icallcase/gen_net9/src/Program.cpp` ~1015-1030.

Current file state (re-verified this session):
- `Program.cpp:133` — `int32_t t10;`
- `Program.cpp:1028-1030` — `icall_decimal_parse((&t300), ...)`, `icall_decimal_parse((&t301), ...)`, `icall_decimal_add((&loc23), (&(t300)), (&(t301)))` — all targets correctly declared `il2cpp::runtime::Il2CppDecimal` (`Program.cpp:291-293`).
- `Program.cpp:1042` — `t10 = il2cpp::runtime::icall_decimal_compare(...)` — an **int32 result into an int32 slot: correct**.

So the checked-in gen_net9 output is **post-fix and post-workaround**: the test source `tmp/icallcase/icallcase.cs:160-163` documents the workaround ("decimal LITERALS (0.1m) hit a constant-fold emitter defect (int32 slot passed as Il2CppDecimal*) ... values come from Parse here") and contains zero decimal literals. The cited line range is the region where the defect manifested in the pre-fix build.

## 2. How Roslyn lowers `6m` — probe results

Method: compiled `tmp/decprobe/probe.cs` with
`dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nostdlib+ -noconfig -optimize+ -r:<net9 System.Private.CoreLib.dll>` into `tmp/decprobe/decprobe.dll`; dumped IL with a System.Reflection.Metadata disassembler written for this task (`tmp/decprobe/dumpil.ps1`, works inside pwsh — no ildasm/dotnet-ildasm needed).

| C# source | IL lowering (net9 Roslyn, optimize+) |
|---|---|
| `6m` | `ldc.i4.6; newobj System.Decimal::.ctor(int32)` |
| `0.1m` | `ldc.i4 1; ldc.i4 0; ldc.i4 0; ldc.i4 0; ldc.i4 1; newobj ::.ctor(int32,int32,int32,bool,uint8)` (lo=1, mid=0, hi=0, sign=false, scale=1) |
| `-0.5m` | `ldc.i4 5; ldc.i4 0; ldc.i4 0; ldc.i4 1; ldc.i4 1; newobj ::.ctor_5` (sign=true, scale=1) |
| `9999999999999m` (>int32) | `ldc.i8 9999999999999; newobj ::.ctor(int64)` |
| 28-digit `1234567890123456789012345678m` | `ldc.i4 <lo>; ldc.i4 <mid>; ldc.i4 <hi>; ldc.i4 0; ldc.i4 0; newobj ::.ctor_5` — **no blob form even at max mantissa** |
| `x == 6m` | `ldarg.0; ldc.i4.6; newobj ::.ctor(int32); call System.Decimal::op_Equality` (operators take the operands **by ref** in metadata: `!0&`) |
| `n < 0m` | `...; ldsflda System.Decimal::Zero; call op_LessThan` — **zero literal = BCL static field** |
| `const decimal F = 6m` (definition + use) | `.cctor`: `ldc.i4.6; newobj ::.ctor(int32); stobj F`. Use sites re-emit the ctor sequence (constant re-folding). No Constant-table entry, no FieldRVA data blob. |

**Answer to task question (1): neither `ldc.r8` nor an ldtoken'd Decimal Constant blob occurs.** The only forms an emitter must handle are the three ctors (+ `ldsflda Zero/One/MinusOne`). Note `ldc.r8` belongs to *double* literals (handled by the M5 `DoubleConstExpr`/"d" path), and ldtoken blobs belong to `ReadOnlySpan<byte>`/array init data — a different domain.

## 3. Transpiler pipeline for the literal shapes (file:line walkthrough)

1. **StackFold.EmitNewObj** (`src/il2cppc/Ir/StackFold.cs:2968-3029`): `System.Decimal` is in the `isValueTypeNewobj` list (`:2998-2999`, present in every backup since 8/11). Fold output:
   ```
   AssignStmt { Target=tN, Value=ValueTypeNewExpr{TypeFullName="System.Decimal"} }
   CallStmt    { Function="vctor_<token>", Args=[LocExpr tN, <constant ctor args>] }
   push LocExpr tN
   ```
2. **vctor emission** (`src/il2cppc/Emit/CodeGenerator.cs:2438-2474`): resolves the token via MethodRefResolver; for `icall_decimal*`/`icall_biginteger*` CppNames the receiver slot is ADDRESSED (`:2455-2457`) → `icall_decimal_ctor_int32((&tN), 6LL)`.
3. **Ctor mappings** (`src/il2cppc/Emit/MethodMap.Decimal.cs:250-288`): `.ctor(int32|uint32|int64|uint64|5-arg)` → `icall_decimal_ctor_*` with `ResultKind "dec"`; float/double ctors LOUD `NotSupportedException` (deferred VarDecFromR4/R8).
4. **Temp typing** — two mirrors that MUST stay in sync:
   - CodeGenerator.ExprKind (`CodeGenerator.cs:3221-3256`): `ValueTypeNewExpr => TypeCppName(...)` (`:3233`) → `"System.Decimal" => "il2cpp::runtime::Il2CppDecimal"` (`:5378`); `CallExpr c when c.ResultKind is ... "dec" ...` (`:3244`).
   - TempSlotPass.KindOf (`src/il2cppc/Opt/TempSlotPass.cs:229-253`): `ValueTypeNewExpr => "vt:<name>"` sentinel (`:237`, excluded from merging via EligibleKinds `:67-68`); the ResultKind list at `:245-246` gained `"dec"/"ip4"/"bi"/"dto"` **today**.
5. **Declaration** (`CodeGenerator.cs:1481-1484` fallthrough): any non-code kind string is emitted verbatim as the C++ type; `"dec"` has its own branch at `:1457-1461`.

## 4. Root cause of the net9 E2E failure (historical)

`TempSlotPass.Rename` is wired post-Fold pre-EmitBody (`CodeGenerator.cs:722`; wired since ≥ 8/11 per backups). Its private KindOf mirror classified a `"dec"`-result temp as `"val"` (old default arm), making it **eligible to merge with genuine int32 temps** (same-kind check `TempSlotPass.cs:192`). After merging, CodeGenerator's locKind is **per-name, forward last-wins** (`CodeGenerator.cs:1196-1202`); when an int32-valued assignment to the shared name came last, the slot was declared `int32_t` while the earlier result-slot-first icall wrote 16 bytes through it:

```cpp
int32_t t10;                                        // merged slot, mis-typed
...
il2cpp::runtime::icall_decimal_parse((&t10), s);    // 16-byte write -> 12-byte stack overrun
```

This matches the recorded root cause in `.opencode/memory.md:9` ("根因：dec temp 被当 val 与 int32 槽合并→16 字节写越界") and the fixes applied there:
- `TempSlotPass.cs:241-246` — `"dec"/"ip4"/"bi"/"dto"` added to the KindOf ResultKind list.
- `CodeGenerator.cs:1132-1147` — standalone pass typing dec-result AssignStmt targets (defense in depth).

**Literal attribution correction:** the literal-newobj temp itself was never mergeable (vt-sentinel) and was always typed correctly by ExprKind+TypeCppName (present in all backups back to 20260811). The failing sites were the Parse/arithmetic icall result temps; the test-source comment (`icallcase.cs:161-163`) and memory item (4) attribute it to "literals" because the original fixture mixed both forms and both were replaced by Parse. With today's TempSlotPass fix, the whole class is closed — verified by reproduction below.

## 5. Reproduction against the current transpiler (evidence)

`dotnet src/il2cppc/bin/Release/net8.0/il2cppc.dll decprobe.dll gen` (binary built 2026-08-22 04:58, i.e. includes all of today's fixes) on a probe containing `x == 6m`, local `0.1m`, `-0.5m`, `9999999999999m`. Generated `gen/src/Probe.cpp`:

```cpp
IL2CPP_NOINLINE int32_t Probe_L_2790a2a0087f43d8(void)
{
    il2cpp::runtime::Il2CppDecimal t0;                 // correct 16-byte type
    il2cpp::runtime::Il2CppDecimal t1;
    t0 = il2cpp::runtime::Il2CppDecimal { };
    il2cpp::runtime::icall_decimal_ctor_5((&t0), 1LL, 0LL, 0LL, 0LL, 1LL);   // 0.1m
    t1 = il2cpp::runtime::Il2CppDecimal { };
    il2cpp::runtime::icall_decimal_ctor_5((&t1), 1LL, 0LL, 0LL, 0LL, 1LL);
    return il2cpp::runtime::icall_decimal_equals((&(t0)), (&(t1)));
}
```

All four methods emit correctly-typed slots and addressed ctor icalls (`-0.5m` → `ctor_5((&t0), 5,0,0,1,1)`; big literal → `ctor_int64((&t0), 9999999999999LL)`; `0m` comparisons → `&(System_Decimal_sf_Zero)` with inline BCL storage emitted at `Probe.cpp:11-13`). **The historical literal defect does not reproduce.**

## 6. LIVE defects found by the probe (current binary)

### L1 — Decimal VALUE parameter declared int32_t (silent-ABI / uncompilable)

`static bool C(decimal x) => x == 6m;` generates:

```cpp
IL2CPP_NOINLINE int32_t Probe_C_5e740afed9fb0fc9(int32_t arg0)     // <-- decimal param as int32_t
{
    il2cpp::runtime::Il2CppDecimal t0;
    t0 = il2cpp::runtime::Il2CppDecimal { };
    il2cpp::runtime::icall_decimal_ctor_int32((&t0), 6LL);
    return il2cpp::runtime::icall_decimal_equals((&(arg0)), (&(t0)));  // <-- 16-byte read through &arg0
}
```

Root cause: `ParamTypesCpp` (`CodeGenerator.cs:6867-6901`) switch has arms only for `d/f/l/q/n/r:/F:`; a VALUETYPE param encodes as `V:System.Decimal` (visible in the emitted typeinfo sig `"b(V:System.Decimal)"`, `Probe.cpp:20`) and falls to `_ => "int32_t"` (`:6898`). The call site's `"dec"` cast code then takes the address of a 4-byte object for a `const Il2CppDecimal*` parameter. Same gap affects Guid/DateTime/TimeSpan/user-struct value params (for field-accessing structs it fails LOUD at C++ compile; for address-consumed BCL structs it is a hard compile error `int32_t* → const Il2CppDecimal*` — or silent corruption if a cast is ever added).

### L2 — Decimal RETURN type unsupported (LOUD)

Any method returning `decimal` aborts transpilation: `il2cppc error: return type 17 is not supported by M3` at `ReturnTypeCpp` (`CodeGenerator.cs:6791`). SignatureTypeCode 17 = VALUETYPE; the switch has no 0x11 arm and receives only the type code (no token/full name).

### L3 — Latent bad kind string in the standalone dec pass

`CodeGenerator.cs:1145` seeds `locKind[target] = "il2cpp::Il2CppDecimal"` (missing `runtime::`). Today it is always overwritten by the generic propagation loop (`:1196-1202` → ExprKind `:3244` → `"dec"` → correct declaration branch `:1457-1461`), so it is unreachable dead weight — but if propagation order or reachability ever changes, the raw string would be emitted verbatim by the `:1481-1482` fallthrough as an invalid C++ type. Recommend normalizing to `"dec"`.

## 7. Fix design (exact insertion points)

### F1 — close L1 (decimal/BCL value-type params)

**Primary site:** `ParamTypesCpp` switch, `CodeGenerator.cs:6879-6898`. Insert before the `_ => "int32_t"` arm:

```csharp
// decimal-domain: a VALUETYPE value param ("V:<FullName>") is the runtime
// struct -- int32_t truncates the slot that "dec"-cast icall sites address
// (&arg0 as const Il2CppDecimal*). Mirrors PinvokeParamList's V: arm.
var v when v.StartsWith("V:", StringComparison.Ordinal) => TypeCppName(v[2..]),
```

`TypeCppName` (`:5367-5384+`) already maps `System.Decimal` → `il2cpp::runtime::Il2CppDecimal`, DateTime/TimeSpan/DateTimeOffset/BigInteger → their runtime structs, and falls back to sanitized user-struct names — one arm covers the whole class. Verify callers: managed decl site `GenerateCore` `:745`; arg rendering already emits `(&(arg0))` so no call-site change.

**Secondary site (same defect via virtual dispatch):** `VirtualParamTypesCpp`, `CodeGenerator.cs:3787` — replace the ternary with the same V:-aware mapping (it currently forces every non-`r:` param to `int32_t`).

**Test-first pin (B7):** extend the decprobe with `C(decimal x)` and assert the generated C++ declares `il2cpp::runtime::Il2CppDecimal arg0` BEFORE the fix (watch it fail), then flip green after.

### F2 — close L2 (VALUETYPE returns)

Mirror the P/Invoke precedent `PinvokeReturnStructName` (`CodeGenerator.cs:5100-5119`), which re-decodes the signature blob to recover a VALUETYPE return's full name:

- Add `ManagedReturnStructName(md, m)` (same blob decode: header → generic arity → param count → retByte `0x11` → TypeDefOrRef full name) near `:5100`.
- At the `ReturnTypeCpp` call site in `GenerateCore` (`:743-760`): consult it first; map `System.Decimal` → `il2cpp::runtime::Il2CppDecimal` (by-value 16-byte return; MinGW/GCC x64 returns it in RAX:RDX pairs per SysV/Microsoft x64 struct-return rules for 16-byte trivially-copyable PODs — verify with a conformance test against the net9 oracle).
- Extend `TrailingReturn` (`:6799-6805`) with the matching zero-init (`il2cpp::runtime::Il2CppDecimal{}`) so the C4716 guard stays consistent.
- Alternative (smaller): keep returns LOUD but emit a precise diagnostic naming the unsupported value type instead of the bare code `17`.

### F3 — hygiene (L3)

`CodeGenerator.cs:1145`: change the seeded string `"il2cpp::Il2CppDecimal"` → `"dec"` (the canonical kind the declaration branch consumes). No behavioral change today; removes the order-dependence.

### Regression guards

- Re-run icallcase net9/net10 e2e (expect unchanged 89/90; decimal checks remain Parse-based until the fixture un-workaround).
- Un-workaround `icallcase.cs:160-169` by restoring literal forms (`dm == 0.3m` etc.) once F1 lands, so the e2e permanently pins the literal path (the current fixture no longer exercises it — that is how this class stayed invisible).
- Unit mirror test: assert `TempSlotPass.KindOf` and `CodeGenerator.ExprKind` agree on the full ResultKind set (the two-mirror divergence WAS this bug; a sync test makes the next addition of a kind impossible to forget).

## 8. Requirement enumeration (task items)

| # | Requirement | Status |
|---|---|---|
| 1 | Determine Roslyn lowering of `6m` (ldc.i4+conv vs ldc.r8 vs ldtoken blob) via own-dir probe | **COMPLETE** — §2: always `newobj .ctor(int32/int64/5-arg)` with constant args; never ldc.r8/ldtoken; `0m` → `ldsflda Zero`. Evidence: `tmp/decprobe/dumpil.ps1` output this session |
| 2 | Read CodeGenerator.cs constant-fold paths handling ldc.r8/ldtoken Decimal blobs | **COMPLETE** — §3: no such blob path exists for decimals; the relevant paths are EmitNewObj fold (StackFold.cs:2968-3029), vctor emission (CodeGenerator.cs:2438-2474), ExprKind/TypeCppName typing (:3233/:3244/:5378), declaration fallthrough (:1481-1484); ldc.r8 belongs to the double-literal path (DoubleConstExpr/"d") |
| 3 | Propose exact fix design (file:line insertion points) | **COMPLETE** — §7: F1 ParamTypesCpp :6879-6898 + VirtualParamTypesCpp :3787; F2 ReturnTypeCpp caller :743-760 mirroring :5100-5119 + TrailingReturn :6799-6805; F3 :1145 |
| 4 | Write analysis to tmp/reports/decimal_literal_defect.md | **COMPLETE** — this document |

Constraints honored: writes confined to `tmp/decprobe/` + `tmp/reports/`; sources read-only; no runtime builds; no git.

## 9. Artifact inventory

```
tmp/decprobe/probe.cs          # literal probe (v2: void/bool signatures)
tmp/decprobe/decprobe.dll      # compiled against net9 System.Private.CoreLib (csc 8.0.205)
tmp/decprobe/dumpil.ps1        # SRM-based IL disassembler (pwsh-hosted, no ildasm)
tmp/decprobe/gen/              # CURRENT transpiler output for the probe (evidence §5, §6-L1)
tmp/reports/decimal_literal_defect.md   # this report
```
