# GAPS.md -- hashsetdeep recorded findings (e2e batch C73, updated hset-b23 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle byte-exact).

## D-HSET-1 -- System.Collections.Generic.HashSet`1 is NOT wired in the transpiler

- **Phenomenon** (recorded batch C73): the case's first draft used a real
  `HashSet<int>` / `HashSet<string>` (Add / Remove / Contains / set algebra).
  Transpiling that draft emitted call sites to
  `System_Collections_Generic_HashSet_1_int_*` symbols but never emitted the
  type, so the C++ build failed with 7 x "use of undeclared identifier".
- **Evidence**: `MethodRefResolver.cs` (grep `HashSet`) had NO owner case for
  `System.Collections.Generic.HashSet`1` -- only List`1 and Dictionary`2 were
  mapped; `include/internal/` had no hashset header (only dictsi_obj.h /
  dictsi.h / hashtable.h / listi4.h).
- **RESOLVED (hset-b23, 2026-08-12)**: D-HSET-1 is CLOSED for HashSet<string>.
  - runtime: `hashset.h` / `hashset.cpp` (new) -- a set-of-unique-members
    GC-OBJECT wrapper (Il2CppHashSetStringObject) whose slot 1 holds a raw
    Il2CppDictSI* (refFieldBitmap 0); each member is a dictsi key with the
    presence-marker value 1. `System_Collections_Generic_HashSet_1_string_class`
    is a runtime GLOBAL (the transpiler newobj fallback emits
    `il2cpp_object_new(&System_Collections_Generic_HashSet_1_string_class)` --
    CodeGenerator.cs BclAllocExpr has no HashSet entry and is read-only in this
    batch; the descriptor rides the dictsi_obj.h include bridge).
  - emitter: `MethodMap.HashSet.cs` (new) maps Add/Contains/Remove/get_Count/
    Clear/.ctor to `icall_hashset_string_*`; `MethodRefResolver.cs` gained the
    owner case `System.Collections.Generic.HashSet`1` (string expansion only,
    LOUD otherwise -- the Dictionary precedent).
  - net48 probe (hsprobe2) pinned the MemberRef signature blobs: Add/Contains/
    Remove = `20 01 02 13 00` (ps [13]); get_Count = `20 00 08`; Clear/.ctor =
    `20 00 01`.
  - tests: `test_hashset.cpp` (new, 9 coverage blocks, PASS mingw) + the
    hashsetdeep Section C now asserts the DIRECT HashSet<string> surface.
- **Remaining**: HashSet<int32> / other instantiations are still NOT wired (the
  owner case LOUD-rejects a non-string receiver). HashSet enumerator /
  CopyTo / set algebra (UnionWith/ExceptWith/IntersectWith) are out of the
  minimal surface.
