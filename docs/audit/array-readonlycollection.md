# Array.AsReadOnly<T> -- the ReadOnlyCollection<T> runtime

Status: GREEN (`probe-arrayreadonly`, 9 lines, byte-exact against the net48 oracle).

## What was missing

`Array.AsReadOnly<T>(T[])` is the last public `System.Array` member that had no
mapping arm. It could not be wired alone: its result type
`System.Collections.ObjectModel.ReadOnlyCollection<T>` had no runtime at all, so
even the call site had nothing to return. The wiring probe reported both the
method (`AsReadOnly`) and every member a caller then touches (`get_Count`,
`get_Item`, `Contains`, `IndexOf`, `CopyTo`, `GetEnumerator`) as unwired.

## What net48 does

`Array.AsReadOnly<T>` returns `new ReadOnlyCollection<T>(array)`. The wrapper
stores the array and forwards reads to it; it never copies. Two consequences
the probe pins:

* **It is a VIEW.** Writing `a[1] = 42` after the wrapper was created changes
  what `ro[1]` answers (`view:42`, not `view:8`).
* **`Count` is the array length**, not a snapshot taken at construction.

`CopyTo` copies the wrapped payload into the destination array, and
`GetEnumerator` returns an enumerator over the wrapped array.

## What the implementation does

`src/runtime/include/internal/readonlycollection.h` +
`src/runtime/src/readonlycollection.cpp`:

* `Il2CppReadOnlyCollection { Il2CppObject obj; Il2CppArray* list; }` -- one
  reference field, so `refFieldBitmap = 0b00010u` and the array stays reachable.
  Keeping the array alive is the whole contract; a wrapper whose `list` was
  collected would read freed memory.
* The class descriptor `System.Collections.ObjectModel.ReadOnlyCollection`1`
  registers with `instanceSize = 16`.
* `il2cpp_readonlycollection_create` stores the array. It is a view by
  construction: no copy, no length snapshot.
* Two accessor pairs, chosen by the element family:
  * `_get_item_i4` / `_contains_i4` / `_index_of_i4` -- an unboxed `int32_t`
    element read through `il2cpp_array_get_elem_ptr`.
  * `_get_item_obj` -- a reference element read through
    `il2cpp_array_get_value_at_offset` (the same helper `Array.GetValue` uses,
    so the reference/value split is not re-implemented here).
* `il2cpp_readonlycollection_copy_to` copies the payload with `memcpy` and then
  runs the write barrier, because the destination may be a reference array.
* `il2cpp_readonlycollection_get_enumerator` reuses the shared
  `Il2CppArrayEnumerator` descriptor; the element READ then boxes value
  elements on the object carrier (`il2cpp_ienum_get_current_obj` routes that
  descriptor through `ArrayEnumRef`), which is what makes `foreach (int v in ro)`
  work.
* Every entry point roots the receiver with `GcRoot` before touching `list`:
  the receiver is the only thing keeping the array alive, so an allocation in
  between must not collect it.

Transpiler side: `MethodMap.Array.cs` maps `AsReadOnly` to
`il2cpp_readonlycollection_create` and passes the array through unchanged
(the cast table's `"a"` would force `Il2CppArray*`, but the runtime entry point
takes the managed object pointer). `MethodMap.ReadOnlyCollection.cs` maps the
members, and `MethodRefResolver.cs` dispatches the owner to it.

### Why the element family comes from `instName`, not `genArgs`

A member call on a CLOSED instantiation (`ReadOnlyCollection<int>::get_Count`)
carries no MethodSpec generic arguments, so `genArgs` is null at every such
call site. The instantiation name (`ReadOnlyCollection`1_int`) is the only
signal available, and `MethodMap.List.cs` already reads it the same way. Using
`genArgs` here emitted the `_obj` accessor for an `int[]` wrapper, which is a
compile error, not a wrong value -- it failed loudly, which is the reason the
defect was visible at all.

## Evidence

```
[BASH] probe-arrayreadonly oracle:
       count:3 / idx:7:9 / view:42 / sum:58 / contains:True:False
       indexof:2 / copyto:7:42:9 / ref:2:q / empty:0
[BASH] difftest -Case probe-arrayreadonly:
       case=probe-arrayreadonly compiler=mingw ok=True
       oracle  code=0 out=100B sha=1b848d5c2f91b716b3ea13d022acdc011628412816a661680357d0a37823f4e6
       runtime code=0 out=100B sha=1b848d5c2f91b716b3ea13d022acdc011628412816a661680357d0a37823f4e6
       firstDiff stdout=-1 stderr=-1 diff=0
[BASH] mutation -- il2cpp_readonlycollection_get_count returns 0:
       count:0 / ref:0   (RED: two lines diverge from the oracle)
[BASH] restore -> count:3 / ref:2, all 9 lines match (GREEN)
[BASH] g++ -std=c++17 -c: readonlycollection / arrayext / arraysort / array / arrayextra
       -> 0 errors
[BASH] dotnet build src/il2cppc/il2cppc.csproj -c Release -> 0 warnings, 0 errors
[BASH] check_decl_symmetry.py -> REAL GAP: none; ABI checked=5697 match=5697
       mismatch=0 skipped=0
```

## Scope limit

`ReadOnlyCollection<T>` is implemented as far as `Array.AsReadOnly` needs it:
the members a caller reaches through the returned wrapper. Constructing one
directly (`new ReadOnlyCollection<int>(list)` where the argument is an
`IList<T>`) takes the BCL constructor, which is a different, unmodelled path --
this runtime models the WRAPPED ARRAY, not an arbitrary `IList<T>`.
