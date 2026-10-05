# ILC Roadmap Status — v30 (batches 333-335, COM stage 76 behavior surface)

Snapshot: HEAD `a467849` (batch 335). Continuation of v29.

## Completed since v29

### COM stage 76 — IDataObject DAdvise/DUnadvise data-notification behavior (`70f6bb0`, batch 333)
- The native data-notification path: DAdvise(FORMATETC*, DWORD advf,
  IAdviseSink*, DWORD* pdwConnection) registers the sink in the per-CCW
  registry (kMaxDataAdvise 4), DUnadvise(DWORD) removes by cookie with
  compaction, and the new every-platform icall_com_data_object_notify fires
  OnDataChange (IAdviseSink vtable slot 3) on every registered sink.
- test_com section 81: register + QI + DAdvise cookie 1 + notify fires
  OnDataChange + DUnadvise + notify reaches nobody + release balances.
### COM stage 76b — stable cookies + multi-sink (`0610c78`, batch 334)
- The registry now assigns STABLE cookies from an incrementing counter
  (dataAdviseNextCookie) stored in a parallel dataAdviseCookies array --
  DUnadvise removes by cookie and compacts WITHOUT renumbering the remaining
  cookies (the OLE contract). Section 81 extended to multi-sink: DAdvise A
  cookie 1 + B cookie 2, notify fires both, DUnadvise(A) compacts, notify
  reaches only B, DUnadvise(B) by stable cookie, notify reaches nobody.
### COM stage 76c — null-sink guard test (`a467849`, batch 335)
- Section 81 asserts DAdvise with a null IAdviseSink is E_INVALIDARG (the
  stage-76 guard).
- test_com sections 75-81 (727 assertions).

## The COM/OLE hosting surface (57 views + 2 behavior surfaces + full automation)
- IUnknown + IDispatch (identity), IOleObject, IOleClientSite, IOleContainer,
  IOleItemContainer, IEnumUnknown, IEnumString, IEnumFORMATETC,
  IEnumSTATDATA, IEnumOLEVERB, IEnumMoniker, IEnumSTATSTG,
  IConnectionPointContainer + IConnectionPoint + IEnumConnectionPoints,
  IDataObject (native + managed dispatch + DAdvise/DUnadvise data
  notification), IDropTarget (native + managed dispatch), IEnumVARIANT,
  IOleInPlaceObject, IOleInPlaceSite, IOleInPlaceUIWindow, IOleInPlaceFrame,
  IOleInPlaceActiveObject, IProvideClassInfo, IAdviseSink, IAdviseSink2,
  IAdviseSinkEx, IOleAdviseHolder, IDataAdviseHolder, IPersist,
  IPersistStream, IPersistFile, IPersistPropertyBag, IParseDisplayName,
  IViewObject, IViewObject2, IOleLink, IMoniker, IRunningObjectTable,
  IBindCtx, IStream, IStorage, IOleCache, IOleCache2, IOleCacheControl,
  IOleUILinkContainer(+A/W), IOleUILinkInfo(+A/W), IOleUIObjInfo(+A/W),
  IOleUIVerify(+A/W), typed-RCW + full IDispatch automation type surface
  (VT_I4/R8/BSTR/BOOL/UI4/I8/UI8 args + returns + property get/put +
  DISPID_PROPERTYPUT named put).
- The in-place activation chain (Object + Site + UIWindow + Frame +
  ActiveObject), the persistence family (Persist + Stream + File +
  PropertyBag), the enumerator family (VARIANT/Unknown/String/FORMATETC/
  STATDATA/OLEVERB/Moniker/ConnectionPoints/STATSTG), the cache family
  (Cache + Cache2 + CacheControl), the advise-holder family (Ole + Data),
  the advise-sink family (Sink + Sink2 + SinkEx), the storage family
  (Storage + Stream + EnumSTATSTG), the UI family
  (LinkContainer/LinkInfo/ObjInfo/Verify, each A/W), and the IDataObject
  data-notification behavior surface (DAdvise/DUnadvise + notify trigger)
  are all complete.

### Verification waves (main-agent direct)
- test_com PASS (727), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact, full-matrix 415/415 effective PASS.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. Internal COM/WinForms work continues at the headless-fixture granularity:
   the IDataObject EnumDAdvise wiring (slot 11 -> the IEnumSTATDATA view,
   platform-gate complexity) and the WebBrowser re-evaluation remain as next
   steps.
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
