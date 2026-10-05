# GAPS.md -- linqgroup unwired/divering surface (e2e batch C58, 2026-08-12)

Case status: **PASS mingw + PASS msvc** (74 deterministic bool probes, all
True; net48 oracle == transpiled runtime byte-exact). The active probes cover
the AGGREGATE family (Sum/Min/Max, incl. checked-overflow + empty throws),
filter/select/order-by/slice combinations, materialization, the fixed-key
bucket grouping PATTERN and the empty-sequence truth table. The gap below was
VERIFIED empirically (transpile abort) and is recorded without fixing, per
batch rules.

## D-LINQ-4 -- Enumerable.GroupBy not wired (MethodMap layer)

- **Phenomenon**: `Enumerable.GroupBy<TSource,TKey>` has no mapping in
  MethodMap.Linq.cs (the switch covers Count/Any/All/Sum/Min/Max/Where/Select/
  ToArray/ToList/First/FirstOrDefault/Take/Skip/OrderBy -- no GroupBy case)
  and no runtime icall exists (grep GroupBy linq*.cpp/h -> zero hits). A call
  falls through the resolver's default to the LOUD "no BCL mapping" path, which
  aborts the WHOLE transpile (empirically confirmed on a 2-line probe: the
  transpile exits non-zero with the NotSupportedException naming the missing
  mapping).
- **Impact**: no GroupBy probe can exist in a PASSing case. The "grouping"
  intent of this case is instead exercised through FIXED-KEY BUCKET
  AGGREGATION (section 7: even/odd + mod-3 buckets -> per-bucket Count/Sum,
  with the bucket totals adding back to the plain Sum) -- the reachable
  aggregation shape, and the section 2/3 filter+aggregate combinations.
- **Fix path (future)**: a GroupBy<TSource,TKey> icall returning a tracked
  grouping structure (runtime side) + a MethodMap.Linq.cs case (transpiler
  side); out of scope here.
