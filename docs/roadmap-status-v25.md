# ILC Roadmap Status — v25 (batches 284-285, COM stages 27-28)

Snapshot: HEAD `80765a6` (batch 285). Continuation of v24 (COM stage 27).

## Completed since v24

### COM stage 27 — IOleInPlaceObject in-place view (`53070ec`, batch 284)
- The 9-slot in-place view (GetWindow null hwnd S_OK, InPlaceDeactivate /
  UIDeactivate S_OK, the rest E_NOTIMPL) at &ccw->inPlaceVtbl; QI for
  IID_IOleInPlaceObject {00000113-...}. test_com section 32.

### IOleWindow clarification (`8196fea`, batch 284b)
- IID_IOleWindow {00000112-...} is the SAME GUID as IID_IOleObject -- no
  independent IOleWindow identity (the proposed stage-28 QI branch could never
  be reached; pinned by a 16-byte iid dump). Removed.

### COM stage 28 — IProvideClassInfo view (`80765a6`, batch 285)
- The 4-slot class-info view (IUnknown + GetClassInfo E_NOTIMPL with a null
  out -- no ITypeInfo built) at &ccw->classInfoVtbl; QI for
  IID_IProvideClassInfo {B196B283-BAB4-101A-B69C-00AA00341D07}. test_com
  section 33 (602 assertions).

## The COM/OLE hosting surface (complete)
- Identity (IUnknown + IDispatch), IOleObject, IOleClientSite,
  IConnectionPointContainer + IConnectionPoint + IEnumConnectionPoints,
  IDataObject, IDropTarget (native + managed dispatch), IEnumVARIANT,
  IOleInPlaceObject, IProvideClassInfo, typed-RCW + IDispatch automation (full
  type surface: VT_I4/R8/BSTR/BOOL/UI4/I8/UI8 args and returns + property
  get/put + DISPID_PROPERTYPUT named put).

### Verification waves (main-agent direct)
- test_com PASS (602), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact, full-matrix 415/415 effective PASS — green after batch 285.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. Internal COM/WinForms work is largely exhausted at the current
   headless-fixture granularity. WebBrowser re-evaluation (the automation +
   hosting surface is complete) and general named-argument dispatch remain as
   larger next steps.
4. winforms1 Main split (deferred, high risk; capped at 110 phases).