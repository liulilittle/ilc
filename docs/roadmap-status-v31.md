# ILC Roadmap Status — v31 (full-matrix re-scan + WebBrowser re-evaluation)

Snapshot: HEAD `6b040f1` (batch 337). Continuation of v30.

## Completed since v30

### Full-matrix 415-case re-scan (2026-08-19, main-agent direct)
- All 415 `tests/cases` cases re-run after the 10 COM commits (batches
  328-337): **415/415 effective PASS** (414 byte-exact + virtbench
  bench-class checksum-identical -166660532; decformat flaky rerun PASS).
- Method: `tmp/run-matrix-parallel.ps1` (ForEach-Object -Parallel throttle 4,
  per-job incremental `tmp/matrix_job_<i>.txt` persistence, `$LASTEXITCODE`
  pass/fail since run-tests.ps1 emits Write-Host to the information stream).
- Zero regression from the COM surface work.

### WebBrowser re-evaluation (2026-08-19) — decision: NOT implemented
- IWebBrowser2 is a **71-slot vtable** (3 IUnknown + 4 IDispatch + 25
  IWebBrowser + 20 IWebBrowserApp + 19 IWebBrowser2, exdisp.h) — 3x larger
  than the largest existing view (IMoniker 23 slots).
- The hosting mechanism it would exercise (IDispatch automation,
  IOleObject/IOleClientSite/IConnectionPointContainer) is already fully
  implemented and tested (stages 1-76).
- No natural MANAGED implementer exists in the ILC test fixtures: the
  WebBrowser control is a NATIVE ActiveX control the managed side CONSUMES
  through the RCW direction; real browser hosting requires the native engine
  (SHDocVw/ieframe) which cannot run headless.
- A CCW IWebBrowser2 view would be a ~71-slot stub surface with no behavioral
  value beyond the already-proven mechanism. Decision: document as
  out-of-scope (com.cpp ITypeInfo contract block); revisit only if a consumer
  actually hosts a managed IWebBrowser2 implementer.

## The COM/OLE hosting surface (57 views + 3 behavior surfaces + full automation)
- IUnknown + IDispatch (identity), IOleObject, IOleClientSite, IOleContainer,
  IOleItemContainer, IEnumUnknown, IEnumString, IEnumFORMATETC,
  IEnumSTATDATA, IEnumOLEVERB, IEnumMoniker, IEnumSTATSTG,
  IConnectionPointContainer + IConnectionPoint + IEnumConnectionPoints,
  IDataObject (native + managed dispatch + DAdvise/DUnadvise data
  notification + EnumDAdvise wiring), IDropTarget (native + managed
  dispatch), IEnumVARIANT, IOleInPlaceObject, IOleInPlaceSite,
  IOleInPlaceUIWindow, IOleInPlaceFrame, IOleInPlaceActiveObject,
  IProvideClassInfo, IAdviseSink, IAdviseSink2, IAdviseSinkEx,
  IOleAdviseHolder, IDataAdviseHolder, IPersist, IPersistStream,
  IPersistFile, IPersistPropertyBag, IParseDisplayName, IViewObject,
  IViewObject2, IOleLink, IMoniker, IRunningObjectTable, IBindCtx, IStream,
  IStorage, IOleCache, IOleCache2, IOleCacheControl, IOleUILinkContainer(+A/W),
  IOleUILinkInfo(+A/W), IOleUIObjInfo(+A/W), IOleUIVerify(+A/W), typed-RCW +
  full IDispatch automation type surface (VT_I4/R8/BSTR/BOOL/UI4/I8/UI8 args
  + returns + property get/put + DISPID_PROPERTYPUT named put).
- The in-place activation chain, persistence family, enumerator family, cache
  family, advise-holder family, advise-sink family, storage family, UI family
  (each A/W), and the IDataObject data-notification behavior surface
  (DAdvise/DUnadvise + notify trigger + EnumDAdvise) are all complete.

### Verification waves (main-agent direct)
- test_com PASS (728), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact, full-matrix 415/415 effective PASS.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware) —
   documented verification procedure + blocker record.
2. #114 concat+sb original crash case (needs the user's original repro) —
   deep investigation of the StringBuilder/concat code paths.
3. winforms1 Main split (deferred, high risk; capped at 110 phases).