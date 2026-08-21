# ILC Roadmap Status — v20 (batch 277, COM stage 20)

Snapshot: HEAD `44ac4d5` (batch 277). Continuation of v19 (winforms1 Phase 110 cap).

## Completed since v19

### COM stage 20 — IDispatch PROPERTY access (`44ac4d5`, batch 277)
- DispatchInvokeManaged gains the DISPATCH_PROPERTYGET (0x2) / DISPATCH_PROPERTYPUT
  (0x4) routing: a member name with no plain typeinfo method falls back to the
  get_<name> (property get, no value arg, int return as VT_I4) or set_<name>
  (property put, single value arg) accessor.
- Fixture IAutoScale gains `int ScaleCountProp { get; set; }`; gen_v5 committed
  (the typeinfo records the property name mapping to get_/set_ accessors).
- test_com section 25 (581 assertions): GetIDsOfNames(ScaleCountProp)->3,
  PROPERTYGET returns VT_I4 7 (ScaleCount preset 7), PROPERTYPUT 5 sets
  ScaleCount to 5, get back 5. test_com PASS.

### winforms1 status (from v19)
- 110 phases / 1031 lines byte-exact; Phase 110 is the cap (Main IL 64KB
  method limit; Phase 111 truncated the transpiled Main). WinForms faces stop
  here unless Main is split (deferred).

### Verification waves (main-agent direct)
- test_com PASS (581), runtime 139/139, unit 1807/1807, winforms1 e2e 1031
  byte-exact — green after batch 277.
- Full-matrix 415/415 effective PASS (v17) still the reference sweep.

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware).
2. #114 concat+sb original crash case (needs the user's original repro).
3. COM stage 21+ (automation type extensions VT_BOOL/VT_UI4, indexed
   properties, new interfaces IOleInPlaceObject/IProvideClassInfo).
4. winforms1 Main split (deferred, high risk).
5. docs/roadmap-status v21 on the next snapshot.
