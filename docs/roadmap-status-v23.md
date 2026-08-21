# ILC Roadmap Status — v23 (batches 278-283, COM stages 21-26)

Snapshot: HEAD `4b6a5e2` (batch 283). Continuation of v22 (COM stage 21).

## Completed since v22

### COM stage 21 — VT_BOOL + VT_UI4 arguments (`7be6357`, batch 278)
### COM stage 22 — VT_BOOL/VT_UI4 return types (`fc3c3a4`, batch 279)
### COM stage 23 — VT_BSTR return (`2cfcb2f`, batch 280)
### COM stage 24 — VT_I8/VT_UI8 argument (`7bff827`, batch 281)
### COM stage 25 — VT_I8 64-bit return (`5e1bad3`, batch 282)
  - The int64 return carrier (hasLRet) -- 'l' -> VT_I8, 'q' -> VT_UI8.
  - kCcwMaxMembers raised 8->12 (the 9th member hit DISP_E_UNKNOWNNAME).
### COM stage 26 — property put with DISPID_PROPERTYPUT named arg (`4b6a5e2`, batch 283)
  - The phase-7 named-argument rejection now accepts the ONE standard
    property-put shape: cNamed 1 with rgdispidNamedArgs[0] = DISPID_PROPERTYPUT
    (-3) and the value in rgvarg[0].
- test_com sections 26-31 (596 assertions); fixtures Adjust/IsEnabled/GetCount/
  GetLabel/SetBig/GetBig + gen_v6..v10.

## The IDispatch automation surface is now COMPLETE
- Arguments: VT_I4 / VT_R8 / VT_BSTR / VT_BOOL / VT_UI4 / VT_I8 / VT_UI8.
- Returns: VT_I4 / VT_R8 / VT_BSTR / VT_BOOL / VT_UI4 / VT_I8 / VT_UI8.
- Property get/put + the standard DISPID_PROPERTYPUT named-put shape.

### Verification waves (main-agent direct)
- test_com PASS (596), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact — green after batches 278-283.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. COM stage 27+ (general named-argument dispatch beyond the property-put
   marker, new interfaces IOleInPlaceObject/IProvideClassInfo, WebBrowser
   re-evaluation with the now-complete automation surface).
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
5. docs/roadmap-status v24 on the next snapshot.
