# W11 F4: 32-bit `refFieldBitmap` slot >= 32 known limitation

Status: **static reliability risk, trigger on current HEAD = UNVERIFIED**
(do NOT claim an empirically observed GC mis-collection; do NOT claim "no problem"
because no natural trigger has been proved). This file is the F4-c registration;
the behaviour change itself is F4-a (reject instead of silently omitting).

## The structural limit

- `TypeDef.RefFieldBitmap` is a 32-bit `uint`. `1u << slot` is undefined for
  `slot >= 32`.
- `src/runtime/src/gc.cpp:705` and `src/runtime/src/gc.cpp:844` both scan
  `for (int32_t slot = 1; slot < 32 && slot < slotLimit; ++slot)`.
- Therefore a managed **reference** instance field whose slot index / absolute
  slot is >= 32 can be neither recorded in the bitmap nor scanned by the
  collector: an object reachable only through that field could be collected
  while still referenced. `gc.cpp:626-629` `FailLoud("mark worklist overflow")`
  is a worklist-capacity guard, NOT a bitmap-overflow guard (no causal link).

## Why it is a risk and not a proven live defect

- The B w11 probe `run-w11probes.ps1` reported
  `[RED] F4 STATIC PATH CONFIRMED`: `slot=32 bitmap-recorded=False
  gc-scanned=False`, with `TRIGGER ... = UNVERIFIED`. That proves the source-level
  omission path exists, not that a current-HEAD program hit a GC mis-collection.
- Precise per-type slot counting of all `tests/cases` was NOT completed here;
  a coarse scan (field declaration counts) is not a valid slot computation
  (it includes static / non-reference / value fields and ignores base-chain
  accumulation). Per-type slot count = **UNVERIFIED**.
  Verification method: drive `TypeEmitter.LayoutInstanceFields` (and
  `LayoutValueTypeFields` / `MarkEmbeddedReferences`) over every translated
  `TypeDef` and record the max reference `SlotIndex` + `absSlot`.

## Trigger paths (A/B/C) and the excluded path (D)

| path | how slot >= 32 arises | covered by |
| --- | --- | --- |
| A single class | 31 value fields after the header, then a reference -> slot 32 | `TypeEmitterF4BitmapTests.A_SingleClass_RefAtSlot32_ThrowsNotSupported` |
| B deep inheritance | base chain accumulates slots; derived's own reference continues the base grid (`TypeEmitter.cs:1074-1079`) | `TypeEmitterF4BitmapTests.B_DeepInheritance_OwnRefAtSlot32_ThrowsNotSupported` |
| C embedded value | inner reference of a user struct at `baseSlot + b` (`TypeEmitter.cs:1555-1570`) | `TypeEmitterF4BitmapTests.C_EmbeddedStruct_InnerRefAbsSlot32_ThrowsNotSupported` |
| D array | `isRefElem` element-level scan (`gc.cpp:691`, `gc.cpp:827`) -- a separate GC branch, NOT the `refFieldBitmap` slot loop | **EXCLUDED** (not part of F4) |

The value-type layout site (`TypeEmitter.cs:1269` / `LayoutValueTypeFields`) is
covered by `TypeEmitterF4BitmapTests.ValueType_RefAtSlot32_ThrowsNotSupported`.

## F4-a behaviour (current HEAD, this cycle)

Three former `warn + skip` sites (`TypeEmitter.cs:1136`, `:1269`, `:1563`) now
throw `NotSupportedException` at transpile time, matching the existing precedent
at `TypeEmitter.cs:117` (and ~24 other `NotSupportedException` sites). Rejecting
is strictly safer than silently emitting a wrong bitmap. Value fields beyond
slot 31 remain legal (no bitmap bit needed) -- guarded by
`ValueFieldsBeyondSlot32_StillAllowed`; references below slot 32 still recorded --
`RefBelowSlot32_StillRecordedInBitmap`.

Scope: F4-b (widening `RefFieldBitmap` to a multi-word table + both gc.cpp loops
+ `MarkEmbeddedReferences`, an ABI/layout change) is **OUT OF SCOPE** for this
cycle (no natural trigger proved; high risk, low benefit). A future F4-b must
update the metadata/ABI layout, `instanceSize`, both GC loops and embedded-value
bitmaps together, and pass a GC-survival probe first.

## GC-survival probe (UNVERIFIED, future work)

A runtime probe that constructs a >31-reference-field object, forces a GC, and
asserts the object reachable ONLY through the slot>=32 field is still alive has
NOT been built/run in this cycle (it needs either F4-b or a deliberately
constructed transpile input that is now rejected by F4-a). Verification method:
after a future F4-b widens the bitmap, build such a program with the real
`difftest.ps1 -Compiler mingw` chain, force `GC.Collect()`, and diff the alive
marker against the net48 oracle (green evidence mtime must be later than the
current HEAD ci `2026-11-13 01:44:58 +0800`). Until then: **UNVERIFIED**.
