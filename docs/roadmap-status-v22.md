# ILC Roadmap Status — v22 (batches 278-281, COM stages 21-24)

Snapshot: HEAD `7bff827` (batch 281). Continuation of v21 (COM stage 20).

## Completed since v21

### COM stage 21 — VT_BOOL + VT_UI4 arguments (`7be6357`, batch 278)
- Phase-7 VARIANT validation accepts VT_BOOL (11) / VT_UI4 (19); both ride the
  4-byte lVal slot. Fixture `int Adjust(bool, uint)`; gen_v6. test_com
  section 26 (584): Adjust(TRUE, 10) -> VT_I4 11.

### COM stage 22 — VT_BOOL/VT_UI4 return types (`fc3c3a4`, batch 279)
- The int32 return VARIANT follows the signature ('b' -> VT_BOOL, 'u' ->
  VT_UI4, else VT_I4) across the 0/1/2-arg call shapes. Fixture `bool
  IsEnabled()` / `int GetCount()`; gen_v7 (UInt32 RETURN is M3-unsupported).
  test_com section 27 (587): IsEnabled -> VT_BOOL TRUE, GetCount -> VT_I4 3.

### COM stage 23 — VT_BSTR return (`2cfcb2f`, batch 280)
- The managed string return is SysAllocStringLen'd into a caller-owned BSTR.
  Fixture `string GetLabel()`; gen_v8. test_com section 28 (590): VT_BSTR
  "auto".

### COM stage 24 — VT_I8/VT_UI8 argument (`7bff827`, batch 281)
- Phase-7 validation + decode accept VT_I8 (20) / VT_UI8 (21) -- llVal@8 into
  the int64 carrier with the single-64-bit-arg call shape (void return only;
  the 64-bit RETURN is not yet carried). Fixture `void SetBig(long)`; the long
  FIELD is M2-unsupported (0x0A) -- the observer stores the low 32 bits in int
  BigLow; gen_v9. test_com section 29 (592): VT_I8 0x123456789A stores the low
  32 bits (0x3456789A; the probe corrected a miscalculated expectation).

### Verification waves (main-agent direct)
- test_com PASS (592), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact — green after batches 278-281.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. COM stage 25+ (named-argument dispatch, 64-bit return carrier, indexed
   properties, new interfaces).
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
5. docs/roadmap-status v23 on the next snapshot.
