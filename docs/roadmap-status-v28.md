# ILC Roadmap Status — v28 (batches 292-326, COM stages 35-69)

Snapshot: HEAD `bcf4274` (batch 326). Continuation of v27.

## Completed since v27

### COM stage 35 — IEnumString view (`8cb7d8b`, batch 292)
- The 7-slot string-enumerator view (Next/Skip/Reset/Clone E_NOTIMPL) at
  &ccw->enumStringVtbl; QI {00000101-...}.
### COM stage 36 — IEnumFORMATETC view (`2b0db5c`, batch 293)
- The 7-slot format-enumerator view (the IDataObject EnumFormatEtc return
  type) at &ccw->enumFormatEtcVtbl; QI {00000103-...}.
### COM stage 37 — IPersistStream view (`f4ab3f1`, batch 294)
- The 8-slot stream-persistence view (GetClassID/IsDirty/Load/Save/
  GetSizeMax E_NOTIMPL) at &ccw->persistStreamVtbl; QI {00000109-...}.
### COM stage 38 — IPersistFile view (`b5921f6`, batch 295)
- The 9-slot file-persistence view at &ccw->persistFileVtbl; QI
  {0000010B-...}.
### COM stage 39 — IPersist view (`33c841d`, batch 296)
- The 4-slot base-persistence view (the root of the persistence family) at
  &ccw->persistVtbl; QI {0000010C-...}.
### COM stage 40 — IOleInPlaceActiveObject view (`6940b55`, batch 297)
- The 12-slot in-place active-object view (GetWindow null hwnd) at
  &ccw->inPlaceActiveObjectVtbl; QI {00000117-...}. The in-place activation
  chain (Object + Site + UIWindow + Frame + ActiveObject) is complete.
### COM stage 41 — IPersistPropertyBag view (`4f42b89`, batch 298)
- The 7-slot property-bag persistence view at &ccw->persistPropertyBagVtbl;
  QI {37D84F60-...}.
### COM stage 42 — IParseDisplayName view (`910e773`, batch 299)
- The 4-slot display-name parser view at &ccw->parseDisplayNameVtbl; QI
  {0000011A-...}.
### COM stage 43 — IViewObject view (`c3f32bd`, batch 300)
- The 9-slot view-object view at &ccw->viewObjectVtbl; QI {0000010D-...}.
### COM stage 44 — IEnumSTATDATA view (`4afa219`, batch 301)
- The 7-slot STATDATA enumerator (the IDataObject EnumAdvise return type) at
  &ccw->enumStatDataVtbl; QI {00000105-...}.
### COM stage 45 — IEnumOLEVERB view (`58e3682`, batch 302)
- The 7-slot OLEVERB enumerator (the IOleObject EnumVerbs return type) at
  &ccw->enumOleVerbVtbl; QI {00000104-...}.
### COM stage 46 — IEnumMoniker view (`df59e45`, batch 303)
- The 7-slot moniker enumerator at &ccw->enumMonikerVtbl; QI {00000102-...}.
### COM stage 47 — IViewObject2 view (`aa480bb`, batch 304)
- The 10-slot view-object extension view (IViewObject + GetExtent) at
  &ccw->viewObject2Vtbl; QI {00000127-...}.
### COM stage 48 — IOleLink view (`3c118ee`, batch 305)
- The 12-slot OLE link view at &ccw->oleLinkVtbl; QI {0000011D-...}.
### COM stage 49 — IMoniker view (`20ff098`, batch 306)
- The 23-slot moniker view (IMoniker derives from IPersistStream) at
  &ccw->monikerVtbl; QI {0000000F-...}.
### COM stage 50 — IRunningObjectTable view (`264156c`, batch 307)
- The 10-slot ROT view at &ccw->runningObjectTableVtbl; QI {00000010-...}.
### COM stage 51 — IBindCtx view (`ede3368`, batch 308)
- The 13-slot bind-context view at &ccw->bindCtxVtbl; QI {0000000E-...}.
### COM stage 52 — IOleAdviseHolder view (`7f3371e`, batch 309)
- The 6-slot advise-holder view at &ccw->oleAdviseHolderVtbl; QI
  {00000111-...}.
### COM stage 53 — IDataAdviseHolder view (`0a0ccf8`, batch 310)
- The 6-slot data advise-holder view at &ccw->dataAdviseHolderVtbl; QI
  {00000110-...}.
### COM stage 54 — IOleCache view (`b6c7a84`, batch 311)
- The 8-slot OLE cache view at &ccw->oleCacheVtbl; QI {0000011E-...}.
### COM stage 55 — IOleCache2 view (`0a4fe87`, batch 312)
- The 10-slot OLE cache extension view at &ccw->oleCache2Vtbl; QI
  {00000128-...}.
### COM stage 56 — IOleCacheControl view (`a083a83`, batch 313)
- The 5-slot cache-control view at &ccw->oleCacheControlVtbl; QI
  {00000129-...}.
### COM stage 57 — IOleItemContainer view (`4e75d18`, batch 314)
- The 8-slot item-container view at &ccw->oleItemContainerVtbl; QI
  {0000011C-...}.
### COM stage 58 — IOleUILinkContainer view (`349de59`, batch 315)
- The 9-slot UI link-container view at &ccw->oleUILinkContainerVtbl; QI
  {0000012A-...}.
### COM stage 59 — IOleUILinkInfo view (`ee9dfbd`, batch 316)
- The 10-slot UI link-info view at &ccw->oleUILinkInfoVtbl; QI
  {0000012B-...}.
### COM stage 60 — IOleUIObjInfo view (`05f6bf3`, batch 317)
- The 7-slot UI object-info view at &ccw->oleUIObjInfoVtbl; QI
  {0000012C-...}.
### COM stage 61 — IOleUIVerify view (`1154afc`, batch 318)
- The 4-slot UI verify view at &ccw->oleUIVerifyVtbl; QI {0000012D-...}.
### COM stage 62 — IOleUILinkContainerA view (`da0751e`, batch 319)
- The 9-slot ANSI UI link-container view at &ccw->oleUILinkContainerAVtbl;
  QI {0000012E-...}.
### COM stage 63 — IOleUILinkInfoA view (`6dd95e1`, batch 320)
- The 10-slot ANSI UI link-info view at &ccw->oleUILinkInfoAVtbl; QI
  {0000012F-...}.
### COM stage 64 — IOleUIObjInfoA view (`dfe2abe`, batch 321)
- The 7-slot ANSI UI object-info view at &ccw->oleUIObjInfoAVtbl; QI
  {00000130-...}.
### COM stage 65 — IOleUIVerifyA view (`3e48f37`, batch 322)
- The 4-slot ANSI UI verify view at &ccw->oleUIVerifyAVtbl; QI
  {00000131-...}.
### COM stage 66 — IOleUILinkContainerW view (`7f2fc47`, batch 323)
- The 9-slot Unicode UI link-container view at &ccw->oleUILinkContainerWVtbl;
  QI {00000132-...}.
### COM stage 67 — IOleUILinkInfoW view (`553e4c2`, batch 324)
- The 10-slot Unicode UI link-info view at &ccw->oleUILinkInfoWVtbl; QI
  {00000133-...}.
### COM stage 68 — IOleUIObjInfoW view (`53ed32d`, batch 325)
- The 7-slot Unicode UI object-info view at &ccw->oleUIObjInfoWVtbl; QI
  {00000134-...}.
### COM stage 69 — IOleUIVerifyW view (`bcf4274`, batch 326)
- The 4-slot Unicode UI verify view at &ccw->oleUIVerifyWVtbl; QI
  {00000135-...}.
- test_com sections 40-74 (700 assertions).

## The COM/OLE hosting surface (51 views + full automation)
- IUnknown + IDispatch (identity), IOleObject, IOleClientSite, IOleContainer,
  IOleItemContainer, IEnumUnknown, IEnumString, IEnumFORMATETC,
  IEnumSTATDATA, IEnumOLEVERB, IEnumMoniker, IConnectionPointContainer +
  IConnectionPoint + IEnumConnectionPoints, IDataObject, IDropTarget (native
  + managed dispatch), IEnumVARIANT, IOleInPlaceObject, IOleInPlaceSite,
  IOleInPlaceUIWindow, IOleInPlaceFrame, IOleInPlaceActiveObject,
  IProvideClassInfo, IAdviseSink, IOleAdviseHolder, IDataAdviseHolder,
  IPersist, IPersistStream, IPersistFile, IPersistPropertyBag,
  IParseDisplayName, IViewObject, IViewObject2, IOleLink, IMoniker,
  IRunningObjectTable, IBindCtx, IOleCache, IOleCache2, IOleCacheControl,
  IOleUILinkContainer(+A/W), IOleUILinkInfo(+A/W), IOleUIObjInfo(+A/W),
  IOleUIVerify(+A/W), typed-RCW + full IDispatch automation type surface
  (VT_I4/R8/BSTR/BOOL/UI4/I8/UI8 args + returns + property get/put +
  DISPID_PROPERTYPUT named put).
- The in-place activation chain (Object + Site + UIWindow + Frame +
  ActiveObject), the persistence family (Persist + Stream + File +
  PropertyBag), the enumerator family (VARIANT/Unknown/String/FORMATETC/
  STATDATA/OLEVERB/Moniker), the cache family (Cache + Cache2 +
  CacheControl), the advise-holder family (Ole + Data), and the UI family
  (LinkContainer/LinkInfo/ObjInfo/Verify, each A/W) are all complete.

### Verification waves (main-agent direct)
- test_com PASS (700), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact, full-matrix 415/415 effective PASS.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. Internal COM/WinForms work continues at the headless-fixture granularity:
   the IDataObject DAdvise/DUnadvise data-notification behavior surface
   (the advise-holder family is present; wiring the actual notification
   path remains), and the WebBrowser re-evaluation remain as next steps.
4. winforms1 Main split (deferred, high risk; capped at 110 phases).
