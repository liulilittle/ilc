# GAPS.md -- gccompact recorded findings (e2e batch C79, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 27-line
byte-exact). Active probes cover the GC COMPACTION depth: a 30-round
compaction loop over a 20-node doubly-rooted chain (every weak ref stays
alive and ReferenceEquals its original node -- the weak slot REWRITE to the
moved object), forward/backward reference-FIELD update, strong GCHandle
slots following the compaction, cross-reference update in both directions,
reference-array element update, dead-target reclaim under compaction, and
generation promotion. Gaps below are RECORDED, NOT FIXED (this batch
creates test cases only).

## D-GCMP-1 -- compaction moves OBJECTS on the runtime; probes only assert REWRITE, not address movement

- **Phenomenon**: the M3 collector (gc.cpp) is COMPACTING: Gen0 survivors are
  physically copied to the promote segment and every root/weak slot/reference
  field is rewritten through the forward map. net48 also compacts the small
  object heap. There is NO managed observable of the physical address change
  itself -- the strongest portable probe is that a WeakReference slot still
  ReferenceEquals its ORIGINAL node after a compaction (proving the slot was
  REWRITTEN, not left stale), which this case asserts on every loop round.
- **Confirmed**: no probe removed; all 27 lines pass byte-exact on both
  compilers. Address-level movement is not directly observable from managed
  code on either runtime (ReferenceEquals on a rooted object is identity).
- **Suspected root file**: `src/runtime/src/gc.cpp` (UpdateReferencesAfterCompaction).

## D-GCMP-2 -- Node cross-links exercise the reference-field write barrier path

- **Phenomenon**: `a.Next = b` / `b.Prev = a` emit il2cpp_write_barrier
  stores (old->young card marking). This case builds back-edges BEFORE the
  pressure loop and verifies both directions survive 15 further collections,
  so a missed card / stale slot would flip xr_ab/xr_ba. All pass on both
  compilers -- no defect observed, noted only as the path being exercised.
- **Confirmed**: probes pass; recorded as an exercised path, not a gap.
