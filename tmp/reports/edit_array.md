# edit_array — System.Array surface sweep (2026-08-22)

Editor scope: `src/il2cppc/Emit/MethodMap.Array.cs` + `MethodMap.ArraySort.cs` (ArraySort.cs: **no changes needed** — see §5).
Backups: `tmp/backups/edit_array/*.20260822-040658.bak`.

---

## 1. GetValue/SetValue (1-D) — WIRED

**Finding:** the runtime already has a transpiler-facing direct-call domain built for exactly this wiring — `arrayextra.h` ("the MethodRefResolver / CodeGenerator layer calls these directly", arrayextra.h:7-9):

- `il2cpp::il2cpp_array_get_value(Il2CppArray*, int32_t) -> Il2CppObject*` (arrayextra.h:115, impl arrayextra.cpp:274)
- `il2cpp::il2cpp_array_set_value(Il2CppArray*, Il2CppObject*, int32_t)` (arrayextra.h:127, impl arrayextra.cpp:317)

Element-kind support is RUNTIME-DISPATCHED (`isRefElem` / `elemSize` switch), so one mapping covers every supported kind [READ:arrayextra.cpp:278-306,321-336]:
- reference arrays → slot itself (SetValue: write barrier, null legal)
- byte/int32/int64 value arrays → box/unbox via boxing.cpp shared layout (`icall_box_byte` / `il2cpp_box_int32` / `icall_box_long`; wrong boxed type → InvalidCastException, null on value array → ANE)
- any other stride → NotSupportedException

**Wired** (MethodMap.Array.cs:293-316):
- `GetValue`, `count==1 && ps[0]==0x08 (I4)` → `I("il2cpp::il2cpp_array_get_value", 1, true, true, true, {"a","i"})`
- `SetValue`, `count==2 && ps[1]==0x08 (I4)` → `I("il2cpp::il2cpp_array_set_value", 2, false, false, true, {"a","","i"})`

Cast/flag precedents verified: `"a"` = exact-address reinterpret to `Il2CppArray*` (CodeGenerator.cs:6289); instance+ref-return shape `(true,true,true)` is standard (MethodMap.ArrayList.cs:53 get_Item); instance receiver consumes casts[0] (CopyTo precedent, MethodMap.Array.cs CopyTo). Arg order is positional-safe: IL push (value, index) == C++ (arr, value, index).

**Stays LOUD:** `GetValue(long)` / `SetValue(object, long)` overloads (ps index byte 0x0A) — no int64-index entry exists in ANY runtime domain; a blind `static_cast<int32_t>` would truncate. MD / params-int[] arities likewise.

## 2. AsReadOnly — NO (confirmed)

No icall or direct-call entry supports it (grep across `src/runtime` headers+sources: zero hits for AsReadOnly). Semantics require a live `ReadOnlyCollection<T>` wrapper object (an IList decorator allocating a managed object of a BCL class the runtime does not model). Decision: **not wired**, stays unwired. No edit made.

## 3. Constrained Copy shapes — WIRED (alias of icall_array_copy)

net48 `Array.ConstrainedCopy` has ONLY the 5-arg form. Its contract — all validation precedes ANY mutation — is exactly what `icall_array_copy` implements: null → indices → ranges → element-kind `ArrayTypeMismatchException` all throw BEFORE the memmove [READ:arrayext.cpp:185-235]. Reference copies move slots with write barriers; overlap-safe memmove.

**Wired** (MethodMap.Array.cs:143-152): `ConstrainedCopy count==5` → same `icall_array_copy` mapping as `Copy`. Other counts LOUD.

Unchanged limitation (pre-existing, documented at the Copy case): the 3-arg `Copy(Array, Array, int)` shape needs two injected zeros the emitter cannot produce (no constant-injection facility in `EmitIcallCallArgs` beyond `PrependZeroArg`/`AppendLiterals` which don't fit mid-signature zeros).

## 4. Exists/Find family genArg coverage beyond 0x08 — NOTHING wireable today

The ext2 header comments say "int32 arrays only" on every predicate/converter/resize entry, and the implementations ENFORCE it: all 13 call sites route through `RequireInt32ArrayExt2` = `isRefElem || elemSize != 4 → throw` [READ:arrayext2.cpp:113-118 + call sites 493-955]. The only non-i4 source variant in the whole family is `icall_array_convert_all_str_i4` (string[]→int32[], already wired).

Per-method unsupported genArgs (all need NEW runtime icalls before any wiring):

| Method | Wired today | Unsupported genArgs (needs-new-icall) |
|---|---|---|
| FindIndex<T> | 0x08 | 0x03 char, 0x09 u4, 0x0A i8, 0x0E string, 0x12 class, 0x1C object |
| Find<T> | 0x08 | same set |
| FindAll<T> | 0x08 | same set |
| Exists<T> | 0x08 | same set |
| TrueForAll<T> | 0x08 | same set |
| Resize<T> | 0x08 | same set |
| ConvertAll<TIn,TOut> | (0x08,0x08), (0x0E,0x08) | e.g. (0x08,0x0E) i4→string, (0x0A,0x0A) i8→i8, (0x12,*) class sources |

A generic new-icall shape would be `icall_array_<op>_elem<T>` parameterized by (elemSize, isRefElem) + per-kind compare/unbox — the same (elemSize, isRefElement) constants `icall_array_empty_*` already bake per-type.

## 5. Sort keySelector overload — ABSENT from the runtime surface

Grep across `src/runtime`: `keySelector` appears ONLY in linq_map.h GroupBy comments (linq_map.h:213,242). arraysort.h offers: typed no-comparer sorts (int32/uint32/int64/string), partial-range sorts `(arr, index, length)` (arraysort.h:139-142), delegate sorts (`_int32_comparison`/`_ref_comparison`), and the IComparer sort. **No keys/items parallel sort and no keySelector projection sort exists.** If a BCL surface demands it → needs-new-icall (e.g. `il2cpp_array_sort_by_key_i4(arr, converter)` reusing the ConvertAll per-binding delegate ABI). Nothing wired; ArraySort.cs untouched.

## Discovered during sweep — NOT fixed (outside task items)

1. **Clear mis-routes for byte[]**: non-generic `Clear` maps to `icall_array_clear_int32` (MethodMap.Array.cs Clear case) while `arrayextra.h:103` provides `il2cpp_array_clear_byte` — a byte[] Clear dies with runtime NotSupportedException instead of clearing.
2. **Reverse range is int32-only**: `Reverse` count==3 → `icall_array_reverse_range` (int32-only), while arraysort.h:155-159 ships typed range variants (`_uint32/_int64/_string/_object`) and arrayextra.h:78-96 full-array variants — unwired.
3. **FindIndex<int> arity landmine**: the mapping ignores `count` and returns pc=4 even for the 2/3-arg overloads; EmitIcallCallArgs iterates `args.Count` slots (CodeGenerator.cs:6200), so a 2-arg call emits a too-few-arguments C++ call (compile-time loud, but misleading). Also a doc contradiction: il2cpp_array_ext2.h:151-153 claims "the 2-arg and 3-arg net48 overloads ... are wired ... by the MethodMap", while MethodMap.Array.cs:164-172 says the reorder is not expressible resolver-side (it now IS expressible via the `IcallArgOrder` table added at CodeGenerator.cs:6141-6146 — which already carries the FindIndex reorder).
4. **Sort<T>(T[], int, int) range sorts**: runtime entries exist (arraysort.h:139-142) but genArgs+count==3 falls into TryMapArray's `_ => throw` arm — LOUD despite runtime support existing.

## Verification status

- Edits are resolver-table-only switch cases mirroring adjacent verified patterns (exact `I()` signature confirmed at MethodRefResolver.cs:1140-1155).
- **Build/typecheck NOT run — forbidden by task constraints (NO builds).** Syntax risk is minimal but nonzero; recommend `dotnet build` on the il2cppc project when permitted.
- Runtime facts cited from headers AND implementation sources this session: arrayextra.h/.cpp, arrayext.cpp, arrayext2.h/.cpp, arraysort.h, il2cpp_array.h, CodeGenerator.cs, MethodRefResolver.cs.
