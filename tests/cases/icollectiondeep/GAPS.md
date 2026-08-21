# GAPS.md -- icollectiondeep recorded findings (e2e batch C53, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 43-line byte-exact).

## D-ICOL-1 -- ICollection.IsSynchronized / SyncRoot / CopyTo are NOT wired

- **Phenomenon (probed at build)**: `((ICollection)ht).IsSynchronized`,
  `((ICollection)sl.Keys).CopyTo(dst, 0)` transpile silently but FAIL the C++
  build -- the resolver has no `System.Collections.ICollection` arm for
  get_IsSynchronized / get_SyncRoot / CopyTo (TryMapCollectionInterface,
  MethodMap.Collections.cs, handles only get_Count for the SortedList snapshot
  receivers), so the calls fall to the generated-name path -> undeclared
  identifier.
- **Root cause**: no MethodMap arm (or runtime icall) exists anywhere in the
  src tree for ICollection.IsSynchronized / SyncRoot / CopyTo (grep over
  src/runtime + src/il2cppc returns nothing; D-HT-1 already records
  "Hashtable Synchronized has no implementation").
- **Disposition**: recorded. The deep test exercises the WIRED ICollection
  member (get_Count) plus the IEnumerable/IEnumerator surface only. A wiring
  agent that adds the sync/copy surface can extend the case.

## D-ICOL-2 -- ICollection receivers beyond the SortedList snapshots are not tracked

- **Phenomenon**: `System.Collections.ICollection c = (ICollection)ht;` then
  `c.Count` on a Hashtable / Stack / Queue / ArrayList receiver is NOT
  dispatched (TryMapCollectionInterface dispatches on the tracked receiver
  pseudo-type, which is only set for `SortedList.KeyList` / `.ValueList` and
  the enumerator pseudo-types) -> generated-name undefined symbol at build.
- **Disposition**: recorded. The case covers the wired snapshot receivers.

## D-ICOL-3 -- SortedList Keys/Values: LIVE cached view (net48) vs SNAPSHOT (ILC)

- **Phenomenon (D-SL-2 re-encounter)**: holding `sl.Keys` in a variable and
  reading `.Count` after a later `sl.Add` diverges -- net48's cached KeyList is
  a LIVE view (reflects the mutation), the ILC runtime returns a frozen
  snapshot array (does not reflect it). Probed: the first write-up of this case
  FAILed on ILC for exactly this reason.
- **Disposition**: the case reads a FRESH `sl.Keys` / `sl.Values` snapshot per
  Count access so both sides agree; documented in the case header. The
  enumeration sections never mutate the list between snapshot and use.
