# cast-3 Root Cause Analysis Report

**Date**: 2026-09-06  
**Target**: `E:\il2cpp\src\il2cppc\Emit\MethodMap.Collections.cs`  
**Error**: `cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'`

---

## 1. Symptom Summary

The `cannot-cast` errors in `MethodMap.Collections` domain manifest as C++ compile errors where an `int32_t` value is passed to a parameter expecting `Il2CppObject*` (reference type). The val→ref mismatch occurs when the resolver wires a cast code that does `static_cast<int32_t>(code)` but the icall signature declares `Il2CppObject*`.

**Representative evidence from r77 generation**:
- `System_Collections_SortedList.cpp:916` — `il2cpp::il2cpp_array_get_length(arg1)` where `arg1` is `Il2CppObject*` but the expression evaluates to `int32_t`
- `System_Collections_Queue.cpp:482` — same pattern
- `System_Collections_Stack.cpp:465` — same pattern
- `System_Collections_Generic_List_1_Enumerator_string.cpp:155` — `static_cast<Il2CppObject*>((*reinterpret_cast<int32_t*>(...)))` reads an int32 slot as if it were `Il2CppObject*`

---

## 2. Root Cause Chain

### 2.1 Primary Cause: Missing `"box:i"` Cast Code in EmitIcallCallArgs

The cast switch in `EmitIcallCallArgs` (`CodeGenerator.cs:6588-6794`) handles the per-argument cast codes wired by `MethodMap.Collections`. The `"i"` arm does:

```csharp
"i" => $"static_cast<int32_t>({code})",
```

This is correct when the icall parameter is `int32_t`. But when the icall parameter is `Il2CppObject*` (reference type) and the argument is a value-typed `int32_t` slot, the cast must BOX the value — and the `"box:i"` cast code is **MISSING** from the switch.

The switch has `"unbox:i"` (line 6786) but no `"box:i"`. The default fallback at line 6804 throws `NotSupportedException("icall arg cast code '{cast}' not implemented")`, but this is never reached because the resolver never emits `"box:i"` — it emits `"i"` and trusts the emitter to do the right thing.

### 2.2 Secondary Cause: MethodMap.Collections Wires `"i"` for Ref-Expecting Params

In `MethodMap.Collections.cs`, the `TryMapListEnumerator` method wires:

```csharp
? I("il2cpp::il2cpp_list_i4_enumerator_move_next", 0, true, false, true, new[] { "deref:r" })
```

The `"deref:r"` cast reads an `Il2CppObject*` through its address — correct for the receiver. But the same file also wires `""` (passthrough) for args that ride in `arg[0]` as managed references. When the receiver is a value-type struct (e.g., `List<int>.Enumerator`), the `""` passthrough passes the struct value directly, and the icall receives `int32_t` where it expects `Il2CppObject*`.

### 2.3 Tertiary Cause: FieldAccess Pointer Judgment (Line 7120)

The `FieldAccess` logic at `CodeGenerator.cs:7120`:

```csharp
if (obj is CallExpr pc && (pc.Function.StartsWith("ldelema_", StringComparison.Ordinal) || pc.ResultKind == "ptr"))
    return $"{objCode}->{fieldCppName}";
```

This correctly handles `ldelema_` results (pointer-shaped), but does NOT handle the case where a value-type expression needs its address taken for a ref-param icall. The `ResultKind == "ptr"` guard is insufficient because the fold may emit a value-type `CallExpr` whose `ResultKind` is `"val"` or `"i"` — the address-of is needed but not inserted.

### 2.4 Quaternary Cause: No `TryEmitObjectParamArg` in Current Codebase

The old version of `CodeGenerator.cs` (before the L2 carrier refactor) had a `TryEmitObjectParamArg` function that bridged int-source args to object-param slots via boxing. This function was removed during the L2 carrier unification (see `r17shard\l2_design_kind-unified_20260902.md`), but the `"box:i"` cast code was not added to the cast switch to replace it.

---

## 3. Evidence Chain

| # | Evidence | Location |
|---|----------|----------|
| 1 | `"i"` cast does `static_cast<int32_t>` — no boxing | `CodeGenerator.cs:6590` |
| 2 | `"unbox:i"` exists but `"box:i"` missing | `CodeGenerator.cs:6786` |
| 3 | Default arm throws — `"box:i"` never reaches here | `CodeGenerator.cs:6804` |
| 4 | `TryMapListEnumerator` wires `"deref:r"` for receiver | `MethodMap.Collections.cs:170` |
| 5 | `TryMapCollectionInterface` wires `""` passthrough for SortedList/Stack/Queue receivers | `MethodMap.Collections.cs:68-135` |
| 6 | r77 `SortedList.cpp:916` — `il2cpp_array_get_length(arg1)` val→ref mismatch | `out\mscorlib_gen_r77\src\System_Collections_SortedList.cpp:916` |
| 7 | r77 `List_1_Enumerator_string.cpp:155` — `static_cast<Il2CppObject*>((*reinterpret_cast<int32_t*>(...)))` | `out\mscorlib_gen_r77\src\...\List_1_Enumerator_string.cpp:155` |
| 8 | L2 design doc confirms `TryEmitObjectParamArg` removal | `r17shard\l2_design_kind-unified_20260902.md:215` |
| 9 | FieldAccess `ldelema_` guard at line 7120 — correct but insufficient | `CodeGenerator.cs:7120` |

---

## 4. Fix Plan

### 4.1 Add `"box:i"` Cast Code to EmitIcallCallArgs (Primary Fix)

Add the following arm to the cast switch in `EmitIcallCallArgs` (after `"unbox:i"` at line 6786):

```csharp
"box:i" => (TypeKindResolver.ResolveExprKind(args[srcIdx], _curLocKinds) == "ref" || code.Contains("Il2CppObject*", StringComparison.Ordinal)) ? code : $"il2cpp::il2cpp_box_int32(nullptr, static_cast<int32_t>({code}))",
```

This mirrors the existing `"box:i"` arm in the ldelem/stelem switch (line 8686 in old version) and the `MethodMap.List.cs` precedent.

### 4.2 Wire `"box:i"` in MethodMap.Collections (Secondary Fix)

In `TryMapListEnumerator`, change the cast list from `new[] { "deref:r" }` to handle the case where the receiver is a value-type struct. For the `MoveNext`/`get_Current`/`Reset` icalls, the receiver rides in `arg[0]` as `Il2CppObject*` — when the receiver is a boxed struct, `"deref:r"` is correct; when it's a val, `"box:i"` is needed.

### 4.3 Extend FieldAccess Address-of Guard (Tertiary Fix)

At `CodeGenerator.cs:7120`, add a guard for value-type expressions that need address-of for ref-param icalls:

```csharp
if (obj is CallExpr pc && pc.ResultKind == "val" && _curLocKinds.TryGetValue(pc.Function, out var pcKind) && pcKind == "i")
    return $"(&({objCode}))->{fieldCppName}";
```

---

## 5. Verification Plan

1. `dotnet build src/il2cppc` → 0 errors 0 warnings (C0)
2. Regenerate r17/r18 and confirm `cannot cast from type 'int32_t'` count decreases
3. Spot-check `System_Collections_SortedList.cpp`, `System_Collections_Queue.cpp`, `System_Collections_Stack.cpp` for correct `il2cpp_box_int32` insertion
4. Run `run-tests 438/438` to confirm no regression

---

## 6. Risk Assessment

- **Probability**: High — the missing `"box:i"` arm is a deterministic gap; every val→ref icall arg hits it
- **Severity**: High — produces C++ compile errors that block the entire transpile
- **Regression risk**: Low — the `"box:i"` arm only fires when the resolver explicitly wires `"box:i"`; existing `"i"` and `""` arms are untouched
- **Merge conflict**: None — the fix is additive (new switch arm) and does not touch the L2 carrier logic

---

*Report generated by subagent analysis of `MethodMap.Collections.cs`, `CodeGenerator.cs`, and r77/r17 generation artifacts.*
