# ILC Roadmap Status — v29 (batches 327-332, COM stages 70-75)

Snapshot: HEAD `de9bc9a` (batch 332). Continuation of v28.

## Completed since v28

### COM stage 70 — IEnumConnectionPoints view (`71e591c`, batch 327)
- The 7-slot connection-point enumerator (the IConnectionPointContainer
  EnumConnectionPoints return type) at &ccw->enumConnectionPointsVtbl; QI
  {B196B285-...}.
### COM stage 71 — IEnumSTATSTG view (`b5d2472`, batch 328)
- The 7-slot storage-element enumerator (the IStorage EnumElements return
  type) at &ccw->enumStatStgVtbl; QI {0000000D-...}.
### COM stage 72 — IAdviseSink2 view (`df89d81`, batch 329)
- The 8-slot advise-sink view with link-source change (IAdviseSink's
  OnDataChange/OnViewChange/OnRename/OnSave + OnLinkSrcChange, all S_OK) at
  &ccw->adviseSink2Vtbl; QI {00000125-...}.
### COM stage 73 — IAdviseSinkEx view (`0c9a390`, batch 330)
- The 9-slot advise-sink view with view-status change (IAdviseSink's four
  notifications + OnViewStatusChange, all S_OK) at &ccw->adviseSinkExVtbl;
  QI {00000126-...}.
### COM stage 74 — IStream view (`685b1a5`, batch 331)
- The 14-slot byte-stream view (Read/Write/Seek/SetSize/CopyTo/Commit/Revert/
  LockRegion/UnlockRegion/Stat/Clone E_NOTIMPL) at &ccw->streamVtbl; QI
  {0000000C-...}.
### COM stage 75 — IStorage view (`de9bc9a`, batch 332)
- The 18-slot compound-file storage view (CreateStream/OpenStream/
  CreateStorage/OpenStorage/CopyTo/MoveElementTo/Commit/Revert/EnumElements/
  DestroyElement/RenameElement/SetElementTimes/SetClass/SetStateBits/Stat
  E_NOTIMPL) at &ccw->storageVtbl; QI {0000000B-...}.
- test_com sections 75-80 (716 assertions).

## The COM/OLE hosting surface (57 views + full automation)
- IUnknown + IDispatch (identity), IOleObject, IOleClientSite, IOleContainer,
  IOleItemContainer, IEnumUnknown, IEnumString, IEnumFORMATETC,
  IEnumSTATDATA, IEnumOLEVERB, IEnumMoniker, IEnumSTATSTG,
  IConnectionPointContainer + IConnectionPoint + IEnumConnectionPoints,
  IDataObject, IDropTarget (native + managed dispatch), IEnumVARIANT,
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
  (Storage + Stream + EnumSTATSTG), and the UI family
  (LinkContainer/LinkInfo/ObjInfo/Verify, each A/W) are all complete.

### Verification waves (main-agent direct)
- test_com PASS (716), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact, full-matrix 415/415 effective PASS.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. Internal COM/WinForms work continues at the headless-fixture granularity:
   the IDataObject DAdvise/DUnadvise data-notification behavior surface
   (the advise-holder family is present; wiring the actual notification
   path remains), and the WebBrowser re-evaluation remain as next steps.
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
