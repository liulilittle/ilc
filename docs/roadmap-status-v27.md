# ILC Roadmap Status — v27 (batches 286-291, COM stages 29-34)

Snapshot: HEAD `7f0bc58` (batch 291). Continuation of v26.

## Completed since v26

### COM stage 29 — IOleInPlaceSite container-side view (`3cf7868`, batch 286)
### COM stage 30 — IAdviseSink view (`cac03c6`, batch 287)
### COM stage 31 — IOleInPlaceUIWindow view (`35ceb99`, batch 288)
### COM stage 32 — IOleInPlaceFrame view (`24ca8c2`, batch 289)
- The 15-slot in-place-frame view (IOleInPlaceUIWindow + InsertMenus/SetMenu/
  RemoveMenus/SetStatusText/EnableModeless/TranslateAccelerator; GetWindow
  null hwnd, SetActiveObject S_OK, the rest E_NOTIMPL) at
  &ccw->inPlaceFrameVtbl; QI {00000116-...}. The in-place UI layer
  (UIWindow + Frame) is complete.
### COM stage 33 — IOleContainer view (`3fd386a`, batch 290)
- The 6-slot container view (ParseDisplayName/EnumObjects/LockContainer
  E_NOTIMPL) at &ccw->oleContainerVtbl; QI {0000011B-...}.
### COM stage 34 — IEnumUnknown view (`7f0bc58`, batch 291)
- The 7-slot unknown-enumerator view (Next/Skip/Reset/Clone E_NOTIMPL --
  the IOleContainer EnumObjects return type) at &ccw->enumUnknownVtbl; QI
  {00000100-...}.
- test_com sections 34-39 (621 assertions).

## The COM/OLE hosting surface (16 views + full automation)
- IUnknown + IDispatch (identity), IOleObject, IOleClientSite, IOleContainer,
  IEnumUnknown, IConnectionPointContainer + IConnectionPoint +
  IEnumConnectionPoints, IDataObject, IDropTarget (native + managed
  dispatch), IEnumVARIANT, IOleInPlaceObject, IOleInPlaceSite,
  IOleInPlaceUIWindow, IOleInPlaceFrame, IProvideClassInfo, IAdviseSink,
  typed-RCW + full IDispatch automation type surface (VT_I4/R8/BSTR/BOOL/UI4/
  I8/UI8 args + returns + property get/put + DISPID_PROPERTYPUT named put).
- The in-place activation chain (Object + Site + UIWindow + Frame) is
  complete.

### Verification waves (main-agent direct)
- test_com PASS (621), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact, COM-case matrix 12/12, full-matrix 415/415 effective PASS.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. Internal COM/WinForms work continues at the headless-fixture granularity:
   IEnumString (the string enumerator), IDataObject DAdvise/DUnadvise data
   notification surface, IParseDisplayName, and the WebBrowser re-evaluation
   remain as next steps.
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
