# ILC Roadmap Status — v26 (batches 284-287, COM stages 27-30)

Snapshot: HEAD `cac03c6` (batch 287). Continuation of v25.

## Completed since v25

### COM stage 27 — IOleInPlaceObject in-place view (`53070ec`, batch 284)
### IOleWindow clarification (`8196fea`) — GUID {00000112} shared with IOleObject, removed.
### COM stage 28 — IProvideClassInfo view (`80765a6`, batch 285)
### COM stage 29 — IOleInPlaceSite container-side view (`3cf7868`, batch 286)
### COM stage 30 — IAdviseSink view (`cac03c6`, batch 287)
- The 7-slot advise-sink view (OnDataChange/OnViewChange/OnRename/OnSave
  all S_OK -- the notification accepted and dropped headless) at
  &ccw->adviseSinkVtbl; QI for IID_IAdviseSink {0000010F-...}.
- test_com sections 32-35 (608 assertions).

## The COM/OLE hosting surface (12 views + full automation)
- IUnknown + IDispatch (identity), IOleObject, IOleClientSite,
  IConnectionPointContainer + IConnectionPoint + IEnumConnectionPoints,
  IDataObject, IDropTarget (native + managed dispatch), IEnumVARIANT,
  IOleInPlaceObject, IOleInPlaceSite, IProvideClassInfo, IAdviseSink,
  typed-RCW + full IDispatch automation type surface (VT_I4/R8/BSTR/BOOL/UI4/
  I8/UI8 args + returns + property get/put + DISPID_PROPERTYPUT named put).
- The in-place activation pair (IOleInPlaceObject + IOleInPlaceSite) is
  complete.

### Verification waves (main-agent direct)
- test_com PASS (608), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact, COM-case matrix 12/12, full-matrix 415/415 effective PASS.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. Internal COM/WinForms work is largely exhausted at the headless-fixture
   granularity. IOleInPlaceFrame / IOleInPlaceUIWindow (the in-place UI
   layer), IEnumString/IEnumUnknown (other enumerators) and the WebBrowser
   re-evaluation remain as larger next steps.
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
