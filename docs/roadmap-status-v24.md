# ILC Roadmap Status — v24 (batch 284, COM stage 27)

Snapshot: HEAD `53070ec` (batch 284). Continuation of v23 (COM stages 21-26).

## Completed since v23

### COM stage 27 — IOleInPlaceObject in-place view (`53070ec`, batch 284)
- The 9-slot in-place view (IUnknown + GetWindow / ContextSensitiveHelp /
  InPlaceDeactivate / UIDeactivate / SetObjectRects / ReactivateAndUndo) lives
  at &ccw->inPlaceVtbl; QI for IID_IOleInPlaceObject {00000113-...} hands it.
  GetWindow writes a null hwnd (headless, S_OK); InPlaceDeactivate /
  UIDeactivate return S_OK; the rest are E_NOTIMPL.
- test_com section 32 (600 assertions): QI + GetWindow null + Deactivate/
  UIDeactivate S_OK + view Release balances the QI AddRef (the section-24
  final release needs refCount back to 1).
- Debug note: the C++ anonymous-`namespace` binding trap (an `extern int` inside
  the anonymous namespace resolves to the ANONYMOUS member, not the enclosing
  scope) -- the counters were dropped in favor of a self-contained view.

## The COM/OLE hosting surface
- Identity (IUnknown + IDispatch), IOleObject, IOleClientSite,
  IConnectionPointContainer + IConnectionPoint + IEnumConnectionPoints,
  IDataObject, IDropTarget (native + managed dispatch), IEnumVARIANT,
  IOleInPlaceObject, typed-RCW + IDispatch automation (full type surface:
  VT_I4/R8/BSTR/BOOL/UI4/I8/UI8 args and returns + property get/put +
  DISPID_PROPERTYPUT named put).

### Verification waves (main-agent direct)
- test_com PASS (600), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact — green after batch 284.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. COM stage 28+ (general named-argument dispatch, further OLE views,
   WebBrowser re-evaluation with the now-complete automation + hosting surface).
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
