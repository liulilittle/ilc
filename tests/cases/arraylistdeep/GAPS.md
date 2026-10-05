# GAPS.md -- arraylistdeep recorded findings (e2e batch C67, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 52-line byte-exact).

## D-AL-1 -- ArrayList Sort / BinarySearch / SyncRoot / Synchronized / ToArray / enumerator NOT wired

- **Phenomenon**: MethodMap.ArrayList.cs maps only ctor(0)/Add/get_Item/set_Item/
  get_Count/Clear/RemoveAt/Insert/IndexOf/Contains/Remove. `ArrayList.Sort()`,
  `ArrayList.BinarySearch(...)`, `SyncRoot`, `Synchronized()`, `IsSynchronized`,
  `ToArray()` and GetEnumerator have NO resolver case and NO runtime icall -> the
  task's intended "sorting / binary search / sync" dimensions are not exercisable.
- **Disposition**: recorded, NOT fixed (batch rule). The case exercises the wired
  surface deeply: ordering stability (the sort-order observable), IndexOf/Contains
  value semantics (search), capacity growth, and exception paths.

## D-AL-2 -- ArrayList.Insert into a FRESH list crashes the runtime (null items deref)

- **Phenomenon (probed at runtime)**: `ArrayList k = new ArrayList(); k.Insert(0, "ok")`
  crashes the transpiled exe (0xC0000005, no output) while net48 prints the expected
  value. Root cause: il2cpp_arraylist_insert (arraylist.cpp:301) calls GrowList(list)
  when `list->items == nullptr || count == length`, but GrowList (arraylist.cpp:155)
  reads `list->items->length` unconditionally -- a fresh list created via the
  `il2cpp_object_new(il2cpp_arraylist_class())` newobj path (CodeGenerator.cs:3092)
  has a NULL items array (only the Add path calls EnsureItems first).
- **Probe**: bisected -- the same Insert after at least one Add works; the empty-list
  get_Item/RemoveAt/Insert(1,...) exception paths work (they throw before the deref);
  ONLY Insert(index<=count) into a never-Add-ed list derefs null.
- **Disposition**: recorded, NOT fixed. The case uses the Add-first path (matching
  listops/listdeep3); a future wiring batch should call EnsureItems(list) at the top
  of il2cpp_arraylist_insert (mirroring the Add path at arraylist.cpp:220).