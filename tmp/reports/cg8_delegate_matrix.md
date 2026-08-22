# CG8 Delegate-Class Construction Support Matrix

- **Date**: 2026-08-22
- **Scope**: research only — which generic delegate TYPES get BclAllocExpr/class emission through CodeGenerator's CG8 machinery; what is wired, where, and what Converter`2 needs.
- **Note on line numbers**: `MethodMap.Array.cs` shifted +7 lines DURING this session (parallel edit today — backup `tmp/backups/edit_array/MethodMap.Array.cs.20260822-040658.bak`). Numbers below are as-of-last-read; content quotes are the stable anchor.

---

## 1. Architecture — TWO construction paths

Which path a delegate construction takes is decided by **`StackFold.IsDelegateType`** (`src/il2cppc/Ir/StackFold.cs:3513-3551`).

### Path A — fold-time (NOT CG8)
`IsDelegateType` matches by name prefix / exact name (StackFold.cs:3516-3540):
`System.Func\``*, `System.Action\``*, `System.Threading.ThreadStart`, `System.Threading.WaitCallback`, `System.Threading.TimerCallback`, `System.Timers.ElapsedEventHandler`, `System.EventHandler`, `System.EventHandler\`1`, plus ANY user type whose base chain reaches `MulticastDelegate`/`Delegate`.

At newobj fold time (StackFold.cs:3032-3068) the newobj+ctor pair collapses into a single
`DelegateNewExpr { MethodToken, Target, TypeId = FNV-1a(typeFullName) }` (StackFold.cs:3060).
CodeGenerator emits it with the REAL type id (CodeGenerator.cs:3107-3121):

```cpp
reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&<method>), <target>, <fnv1a_type_id>))
```

**Consequence: Func`1..N / Action`1..N NEVER reach BclAllocExpr. No CG8 entries exist or are needed for them.**

### Path B — CG8 machinery (delegates IsDelegateType does NOT list)
Falls through to the generic newobj shape (StackFold.cs:3079-3119):
1. `NewObjExpr { TypeFullName }` placeholder temp, and
2. a separate ctor `CallStmt`.

Two CodeGenerator hooks make this compile and run:

| Hook | Location | Job |
|---|---|---|
| ctor rewrite (`isBclDelegateCtor`) | CodeGenerator.cs:2698-2726 | rewrites the ctor CallStmt to `{recv} = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_delegate_new({methodPtr}, {target}, 0))` — **TypeId = 0 (untracked)**, documented tradeoff (BUG-2 batch 177b comment, CodeGenerator.cs:2717-2722: runtime skips Combine type check for these forms) |
| newobj placeholder (`BclAllocExpr`) | CodeGenerator.cs:3413-3535, consumed at 3068-3074 | must return `"nullptr"` for the EXACT instantiation name, else the fallback emits `il2cpp_object_new(&System_Xxx_N_arg_class)` — an **undefined symbol → cmake/link FAIL** |

Instantiation name format = `GenericInstantiationName` (CodeGenerator.cs:4729-4784): `defName + "_" + args.Join("_")`, arg bytes → `bool/char/.../int(0x08)/string(0x0E)/object(0x1C)/class(0x12)...`. E.g. `Converter<string,int>` → **`System.Converter`2_string_int`**.

---

## 2. THE MATRIX

| Delegate type | Path | IsDelegateType? | Ctor interception (EmitCallStmt) | BclAllocExpr placeholder(s) | Status |
|---|---|---|---|---|---|
| `Comparison\`1` | B (CG8) | NO | YES — `System_Comparison_` + `__ctor_` (CodeGenerator.cs:2710-2711) | `_int` `_string` `_object` `_class` (CodeGenerator.cs:3531-3534) | **WIRED** (cg8-b23). Covers every element type the delegate-sort consumes: int32/string/object/class (MethodMap.Array.cs:45-46 → `il2cpp_array_sort_int32_comparison` / `_ref_comparison`) |
| `Predicate\`1` | B (CG8) | NO | YES — `System_Predicate_` + `__ctor_` (CodeGenerator.cs:2700-2701) | `_int` only (CodeGenerator.cs:3514) | **WIRED** for int (CG8 findidx D-findidx-2). Complete for current surface: Array Find/FindIndex/FindAll/Exists/TrueForAll accept ONLY `genArgs[0]==0x08` (MethodMap.Array.cs:153-223); `List<T>.Find` is NOT mapped via predicate delegates (no Find/Exists/Predicate cases in MethodMap.List.cs) |
| `Converter\`2` | B (CG8) | NO | YES — `System_Converter_` + `__ctor_` (CodeGenerator.cs:2705-2706, wir9-b23) | `_int_int` ONLY (CodeGenerator.cs:3521) | **PARTIAL — KNOWN GAP**: `System.Converter`2_string_int` missing (see §3) |
| `Func\`1..N` | A (fold) | YES (prefix) | n/a | n/a | **WIRED** via StackFold `DelegateNewExpr`, real TypeId |
| `Action\`1..N` | A (fold) | YES (prefix) | n/a | n/a | **WIRED** via StackFold `DelegateNewExpr`, real TypeId |
| `System.Action` (non-generic) | B (CG8) | NO — IsDelegateType matches only ``Action\``` | YES — `System_Action__ctor_` (CodeGenerator.cs:2699) | `"System.Action" => nullptr` (CodeGenerator.cs:3503) | **WIRED** (M7 CG-FIX excext) |

Consumption side (Path B delegates are consumed identically): the array icalls take the `Il2CppObject*` delegate and unwrap `methodPtr` per binding — static leaf `methodPtr(v)`, instance leaf `methodPtr(target, v)` (`arrayext2.cpp:81-91` typedefs, `InvokeConvertAllI4`/`InvokeConvertAllStrI4` at arrayext2.cpp:424-435, `InvokeFindIndexPred` ~410).

---

## 3. The known gap — `System.Converter`2_string_int`

Evidence chain (all read this session):

1. **Mapping exists**: `Array.ConvertAll` is resolved for `(int,int)` → `icall_array_convert_all_i4_i4` AND `(string,int)` → `icall_array_convert_all_str_i4` (MethodMap.Array.cs:224-240 current / 217-233 at session start).
2. **Runtime exists + proven**: `icall_array_convert_all_str_i4` (`arrayext2.cpp:838`, decl `il2cpp_array_ext2.h:200`); unit tests ALL PASS incl. GC probe and exception paths (`tmp/reports/ut_arr_conv_str.md` — "Result: PASS", exit 0).
3. **Transpiler gap**: `BclAllocExpr` has only `"System.Converter`2_int_int" => "nullptr"` (CodeGenerator.cs:3521). For `ConvertAll<string,int>(strings, int.Parse)` Roslyn emits `newobj System.Converter`2<string,int>` + `ldftn Int32.Parse`. `GenericInstantiationName` yields `System.Converter`2_string_int` → no placeholder → fallback `il2cpp_object_new(&System_Converter_2_string_int_class)` (CodeGenerator.cs:3073-3074) → **undefined symbol → cmake FAIL**. This is the exact failure shape documented for Predicate before its fix (CodeGenerator.cs:3507-3508).
4. **Test exclusion on record**: `tmp/icallcase/icallcase.cs:120-121` — "NOTE: Array.ConvertAll excluded -- Converter`2 delegate-CLASS construction is unwired (CG8 gap, defect logged in discover.md)". (Written pre-wir9-b23; int,int is unlocked now, string,int is not.)
5. **net9 shape on record**: `tmp/icallcase/gen_net9/src/__O.cpp:21` — `{ "<0>__Parse", "System.Converter`2", -1, 1, &__O_sf__0___Parse, 2 }`: Roslyn's cached-delegate pattern stores the `int.Parse` converter in a static field of declared type `System.Converter`2`; its lazy initializer hits the same construction path.

Why the ctor hook alone is NOT enough: interception (CodeGenerator.cs:2705-2706) already matches ANY instantiation by `System_Converter_` prefix, but the discarded newobj placeholder still must COMPILE — hence the missing BclAllocExpr key is the sole blocker.

---

## 4. Implementation plan — add Converter`2 following the Comparison`1 pattern

Comparison`1 precedent: per-instantiation `"nullptr"` keys enumerated for each element type the mapped API surface can produce (CodeGenerator.cs:3531-3534). Converter`2 needs exactly one new key today.

### Change (single line + comment, CodeGenerator.cs BclAllocExpr switch, after line 3521)

```csharp
// cg8-convstr: the CONVERTER delegate used by Array.ConvertAll<string,int>
// (string[], int.Parse) -- the str_i4 icall is mapped (MethodMap.Array.cs)
// and runtime-proven (test_arr_conv_str), but the newobj placeholder fell to
// the generic alloc path (&System_Converter_2_string_int_class -- undefined
// symbol). Same CG8 contract as Converter`2_int_int above: the ctor CallStmt
// is intercepted in EmitCallStmt (System_Converter_ prefix) and rewritten to
// il2cpp::il2cpp_delegate_new; nullptr is the correct zero-cost placeholder.
"System.Converter`2_string_int" => "nullptr",
```

Nothing else changes:
- ctor interception: already live (CodeGenerator.cs:2705-2706);
- resolver mapping: already live (MethodMap.Array.cs:235-238);
- runtime icall: already live + unit-tested (arrayext2.cpp:838).

### Verification (TDD order — pin the failing build first if desired)

1. **Pin**: uncomment/re-add a ConvertAll check in `tmp/icallcase/icallcase.cs` (~line 120), covering BOTH shapes:
   - `Array.ConvertAll(new[]{1,2,3}, x => x * 2)` (int,int — regression guard),
   - `Array.ConvertAll(new[]{"1","22"}, int.Parse)` (string,int — the gap).
   Expect: cmake FAIL with `undefined reference/reference to \`System_Converter_2_string_int_class'` BEFORE the fix (bug evidence), clean build AFTER.
2. **Build** the icallcase E2E (cmake generate + build) — zero undefined-symbol errors.
3. **Run**: expect `arr.convertall` checks green; delegate arrives at `icall_array_convert_all_str_i4` as a single-cast delegate created by `il2cpp_delegate_new(methodPtr, target, 0)`; static `int.Parse` binds target=nullptr → `ConvertAllStrI4Fn` arm (arrayext2.cpp:431-435).
4. **net9 variant**: rebuild the net9 icallcase gen so `<0>__Parse`'s lazy initializer compiles through the same path.

### Deliberately NOT in scope (C7 surgical scope)

- Other Converter instantiations (`<double,double>` etc.): unreachable — every unmapped ConvertAll shape throws loudly in MethodMap.Array.cs:239-240 before any consumer exists. Add keys only when a surface maps.
- `Comparison`1_uint/long` placeholders: `Sort<uint32/int64>` with a Comparison delegate currently throws (`MethodMap.Array.cs:54`; ArraySort yield domain takes plain function pointers, `MethodMap.ArraySort.cs:66`). If those ever gain delegate-sort entries, their placeholders must be added in the same change.
- TypeId=0 untracked-delegate divergence (Path B) vs real FNV-1a id (Path A): existing documented tradeoff (CodeGenerator.cs:2717-2722); heterogeneous-Combine checking stays skipped for CG8 delegates. No change.

---

## 5. Requirement checklist

| Requirement | Status | Evidence |
|---|---|---|
| Locate `System_Comparison_` precedent | [COMPLETE] | CodeGenerator.cs:2710 (ctor hook), :3526/:3531-3534 (placeholders); MethodMap.Array.cs:38 comment |
| Comparison`1 wired? file:line? | [COMPLETE] | §2 row 1 — CodeGenerator.cs:2710-2711, 3531-3534 |
| Predicate`1 how constructed? | [COMPLETE] | §2 row 2 — CG8 Path B: nullptr placeholder (3514) + ctor rewrite (2700-2701) → `il2cpp_delegate_new(...,0)`; consumed by Array find/exists icalls; List<T>.Find not predicate-mapped |
| Func<>/Func<,>/Action<>/Action<,> status | [COMPLETE] | §2 rows 4-5 — Path A fold-time `DelegateNewExpr` (StackFold.cs:3042-3068, CodeGenerator.cs:3107-3121); no CG8 entries needed |
| Converter`2 known-missing confirmation | [COMPLETE] | §3 — placeholder `_int_int` only (3521); `_string_int` absent; icallcase.cs:120 exclusion; __O.cpp:21 net9 shape |
| What's needed to add Converter`2 | [COMPLETE] | §4 — one BclAllocExpr key + verification plan |
| Report written, read-only elsewhere | [COMPLETE] | this file; no source edits, no builds, no git, no .opencode |
