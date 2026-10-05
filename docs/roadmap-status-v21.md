# ILC Roadmap Status — v21 (batches 277-280, COM stages 20-23)

Snapshot: HEAD `2cfcb2f` (batch 280). Continuation of v20 (COM stage 20).

## Completed since v20

### COM stage 20 — IDispatch PROPERTY access (`44ac4d5`, batch 277)
- DISPATCH_PROPERTYGET (0x2) / DISPATCH_PROPERTYPUT (0x4) routing via the
  get_<name> / set_<name> prefix fallback in DispatchInvokeManaged.
- Fixture IAutoScale `int ScaleCountProp { get; set; }`; gen_v5.
- test_com section 25 (581 assertions): get returns VT_I4 7, put 5 sets, get 5.

### COM stage 21 — VT_BOOL + VT_UI4 arguments (`7be6357`, batch 278)
- Phase-7 VARIANT validation accepts VT_BOOL (11) / VT_UI4 (19); both ride the
  4-byte lVal slot.
- Fixture `int Adjust(bool, uint)` ("i(b,i)"); gen_v6.
- test_com section 26 (584): Adjust(TRUE, 10) -> VT_I4 11.

### COM stage 22 — VT_BOOL/VT_UI4 return types (`fc3c3a4`, batch 279)
- The int32 return VARIANT follows the signature ('b' -> VT_BOOL, 'u' ->
  VT_UI4, else VT_I4) across the 0/1/2-arg call shapes.
- Fixture `bool IsEnabled()` / `int GetCount()`; gen_v7 (UInt32 RETURN is an
  M3-unsupported type -- GetCount returns int).
- test_com section 27 (587): IsEnabled -> VT_BOOL TRUE, GetCount -> VT_I4 3.

### COM stage 23 — VT_BSTR return (`2cfcb2f`, batch 280)
- The managed string return is SysAllocStringLen'd into a caller-owned BSTR
  (bstrVal@8) instead of the raw Il2CppString* pointer; null string -> null
  bstrVal.
- Fixture `string GetLabel()` ("r:System.String()"); gen_v8.
- test_com section 28 (590): GetLabel -> VT_BSTR "auto".

### Verification waves (main-agent direct)
- test_com PASS (590), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact — green after batches 277-280.
- Full-matrix 415/415 effective PASS (v17) still the reference sweep.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. COM stage 24+ (named-argument dispatch, VT_I8 64-bit automation, new
   interfaces IOleInPlaceObject/IProvideClassInfo).
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
5. docs/roadmap-status v22 on the next snapshot.
