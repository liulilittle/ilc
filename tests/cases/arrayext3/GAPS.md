# GAPS.md -- arrayext3 unwired/divering surface (e2e batch C58, 2026-08-12)

Case status: **PASS mingw + PASS msvc** (60 deterministic bool probes, all True;
net48 oracle == transpiled runtime byte-exact). The active probes cover the
WIRED int32 predicate surface (Array.Find / Exists / TrueForAll / ConvertAll /
Resize / FindIndex(4-arg)). Every gap below was VERIFIED empirically
(transpile abort / read of the resolver) and is recorded without fixing, per
batch rules.

## D-ARF-1 -- Array.FindAll<T> not wired (MethodMap layer)

- **Phenomenon**: a call to `Array.FindAll<int>(int[], Predicate<int>)` has no
  mapping anywhere in the transpiler: `grep FindAll src/` returns ZERO hits --
  `MethodMap.Array.cs` has Find/Exists/TrueForAll/ConvertAll/Resize but no
  `FindAll` case, and the runtime (il2cpp_array_ext2.h) defines no
  `icall_array_find_all_*`. Resolution falls through TryMapArray ->
  TryMapArraySort -> the LOUD "generic instantiation of System.Array.FindAll
  has no BCL mapping" NotSupportedException, which aborts the WHOLE transpile.
- **Impact**: the batch title names FindAll; because an unwired probe aborts
  the transpile (run-tests.ps1 throws at the transpile stage), no FindAll
  probe can exist in a PASSing case. The FindAll semantics (a NEW array of
  every matching element, empty -> empty) are instead approximated by the
  ConvertAll -> Find/Exists/TrueForAll combos probed here.
- **Fix path (future)**: add an int32 `icall_array_find_all_i4` (returns a new
  Il2CppArray*, same single-cast static-methodPtr ABI as icall_array_find_i4)
  + a MethodMap.Array.cs `case "FindAll"` with genArgs[0] == 0x08.

## D-ARF-2 -- Array.FindIndex 2/3-arg overloads abort at transpile

- **Phenomenon**: `Array.FindIndex(int[], Predicate<int>)` (2-arg) and the
  (array, start, match) / (array, match, count) 3-arg overloads resolve to the
  SAME 4-param icall `icall_array_find_index`; the resolver hardcodes
  `ParamCount=4` (MethodMap.Array.cs:150). StackFold pops 4 args from an IL
  stack that only pushed 2/3 -> `call at offset 0xXXXX pops 4 args but the
  stack has only 0 (resolved=il2cpp::runtime::icall_array_find_index)`,
  aborting the transpile (empirically hit on the first draft of this case).
- **Impact**: only the 4-arg window form `FindIndex(array, start, count,
  match)` transpiles (the arrayext2 precedent). All windows here are explicit
  4-arg.
- **Fix path (future)**: the resolver must map by `count` -- 2-arg ->
  (array, match) with default start=0/count=length folded at the runtime
  (the runtime's 2/3-arg "fold to the 4-arg shape" comment assumes this
  folding exists; it does not today).

## D-ARF-3 -- MethodMap says "no reorder facility" for FindIndex arg order

- **Phenomenon**: the MethodMap.Array.cs:140-148 comment documents that the
  IL pushes (array, startIndex, count, match) but the icall parameter order is
  (arr, predicate, start, count); the comment claims "no reordering facility"
  and references the IcallArgOrder reorder (commit 7f323aa, `find_index
  {0,3,1,2}`). The 4-arg probes PASS byte-exact on both compilers, so the
  reorder IS effective today -- this entry documents that the stale comment
  (saying it is NOT) should be refreshed when the area is next touched.
