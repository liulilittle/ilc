# GAPS.md -- listq recorded findings (e2e batch C71, 2026-08-12)

Case status: net48 oracle 42-line byte-exact reference written (expected.txt);
dual-compiler PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## Verified byte-exact contract (this case pins, no gap)

- Tail-append + head-consume FIFO: Add 1..6 then RemoveAt(0) x6 returns the
  exact net48 order; Count drains 6->0.
- Interleaved Add/read: constant and computed get_Item indexes, sum via
  traversal, IndexOf first-match on duplicates (20 -> 1), Contains present /
  missing.
- Remove shapes: RemoveAt(0) head / RemoveAt(2) mid / RemoveAt(Count-1) tail,
  element order preserved after each shift (element-by-element assert);
  remove-all-down-to-empty.
- Growth past initial capacity: Add 25 (multiple doublings), full order
  preservation checked on every append (g[g.Count-1] read + compare) plus
  head/mid/tail reads and IndexOf/Contains across growth.
- Clear + reuse: Clear -> Count 0 / IndexOf -1 / Contains false; re-Add reads
  the fresh contents.
- Queue mix: scripted Add/RemoveAt(0)/IndexOf sequence with the final element
  sequence asserted element-by-element.

## Documented gaps (NOT probed -- do NOT uncomment until wired)

The MethodMap.List.cs surface (src/il2cppc/Emit/MethodMap.List.cs) wires ONLY
ctor / get_Count / Clear / Add / get_Item / RemoveAt / IndexOf / Contains.
A call to any other List<int> member falls to the generated-name path -> C++
build fails. The runtime (src/runtime/src/listi4.cpp) DOES implement:
- D-LISTQ-1 set_Item (list[i] = v)   -- no set_Item arm in TryMapList (only
  get_Item); the listi4.cpp:363 il2cpp_list_i4_set_item is unreachable from
  transpiled code.
- D-LISTQ-2 Insert(index, value)     -- listi4.cpp:302 il2cpp_list_i4_insert
  exists but no mapping arm.
- D-LISTQ-3 Remove(value)            -- listi4.cpp:343 il2cpp_list_i4_remove
  exists but no mapping arm.
- D-LISTQ-4 ToArray()                -- listi4.cpp:415 il2cpp_list_i4_to_array
  exists but no mapping arm.
- D-LISTQ-5 capacity ctors           -- runtime has only the parameterless
  il2cpp_list_i4_create.

These are one-line TryMapList additions each (the runtime is already there) --
recorded, not fixed (batch rule).
