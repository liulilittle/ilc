# GAPS.md -- sldeep recorded findings (e2e batch C37, 2026-08-11)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 54-line byte-exact).

## D-SL-1 -- mixed-TYPE keys: net48 throws, ILC defines an order (deliberate divergence)

- **Phenomenon (probed on net48)**: `sl.Add(5, "five"); sl.Add("a", "a")` throws
  `InvalidOperationException` (Comparer.Default -> Int32.CompareTo(string)).
  The ILC runtime instead sorts a boxed Int32 BEFORE a string (a deterministic
  total order, documented in sortedlist.h).
- **Causal chain**: net48 Comparer.Default only compares same-type IComparable
  pairs; the runtime's compare has a documented mixed-key arm.
- **Disposition**: NOT probeable in an e2e diff (the net48 oracle throws, so a
  mixed probe cannot pass both sides). The runtime side is covered by the C++
  unit test (test_sortedlist.cpp §10). Recorded so maintainers know the e2e
  cannot guard this surface.
- **Suspect**: none -- intended per sortedlist.h.

## D-SL-2 -- Keys/Values are LIVE cached views on net48, SNAPSHOTS on ILC (divergence)

- **Phenomenon (probed on net48)**: `ReferenceEquals(sl.Keys, sl.Keys)` is True
  (net48 caches a single KeyList wrapper), and a later Add IS reflected in the
  cached Keys view (live view). The ILC runtime returns a fresh SNAPSHOT array
  per call that does not reflect later mutations (sortedlist.h).
- **Disposition**: NOT probeable in an e2e diff; the case uses the subset both
  sides agree on (non-null, distinct Keys/Values, sorted key order via
  GetKey/GetByIndex). Runtime snapshot behavior covered by test_sortedlist.cpp
  §15.
- **Suspect**: none -- intended per sortedlist.h.

## D-SL-3 -- ICollection.Count / IEnumerable enumeration of Keys/Values (FIXED, slic-b23)

- **Original phenomenon**: `sl.Keys` has compile-time type System.Collections.
  ICollection; `keys.Count` (ICollection.get_Count) and `foreach (object k in
  sl.Keys)` (IEnumerable.GetEnumerator / IEnumerator.MoveNext / get_Current /
  Reset) had NO MethodMap arm -> generated-name undefined symbol -> link
  failure. The foreach finally's `isinst System.IDisposable` + Dispose also
  referenced the missing System_IDisposable_class / enumerator-dispose symbols.
- **Root cause**: no System.Collections.ICollection / IEnumerable / IEnumerator
  wiring existed in the transpiler; get_Keys/get_Values did not track the
  snapshot's receiver type; no enumerator object existed in the runtime.
- **Fix (slic-b23, 2026-08-11)**: 
  1. Runtime (sortedlist.cpp/h): a snapshot enumerator object
     (Il2CppArrayEnumerator: obj + snapshot array GC-ref + next-index cursor)
     with icalls il2cpp_sortedlist_snapshot_get_count / get_enumerator /
     enumerator_move_next / get_current / reset / dispose; the
     System_IDisposable_class interface descriptor (registered lazily).
  2. MethodMap.SortedList.cs: get_Keys/get_Values ReturnTypeName ->
     "System.Collections.SortedList.KeyList"/".ValueList" (receiver tracked).
  3. MethodMap.Collections.cs (new): TryMapCollectionInterface -- ICollection.
     get_Count / IEnumerable.GetEnumerator dispatch on the snapshot receiver
     pseudo-type; IEnumerator.MoveNext/get_Current/Reset dispatch on the
     enumerator pseudo-type.
  4. MethodRefResolver.cs: dispatch case lines for the three interface owners
     + IDisposable::Dispose keeps streams on icall_stream_close and maps the
     untracked enumerator receiver to the no-op enumerator dispose.
- **Verification**: sldeep now probes the full interface surface (section 8:
  ICollection.Count on Keys/Values, foreach enumeration of both, explicit
  IEnumerator protocol with Reset); net48 oracle 54-line byte-exact on BOTH
  mingw and msvc. Unit: 14 new MethodMapTests.Collections tests; runtime
  test_sortedlist.cpp §16 enumerator tests exit=0; regression sweep
  (dictcase/streamops/hashtablecase/interface/collections2/resops/listops/
  hashchain/stackq/hashtable2/dictedge) all PASS.
- **Disposition**: closed -- the interface surface is wired; the snapshot
  CONTENT is now observable through Count / foreach (both sides agree while the
  list is not mutated between snapshot and use).
