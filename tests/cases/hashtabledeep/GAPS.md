# GAPS.md -- hashtabledeep defects (e2e batch C52, 2026-08-12)

Case status: **PASS dual-compiler (mingw + msvc)** -- the active probes cover
the WIRED Hashtable surface (il2cpp_hashtable.h + MethodMap.Hashtable.cs):
mixed key types (string / boxed int32 / custom reference) in one table, per-
family hash/identity semantics, duplicate/edge semantics, Keys/Values
snapshot non-null surface, mixed growth. The gaps below are RECORDED, NOT
FIXED (this batch creates test cases only).

## D-HT-2 -- Hashtable.Synchronized / IsSynchronized / SyncRoot not wired (no source anywhere)

- **Phenomenon**: any call to `Hashtable.Synchronized(h)` would abort at the
  transpile stage (no method mapping). No probe in hashtabledeep.cs uses it.
  The "synchronized wrapper" dimension requested by the batch worklist is
  therefore OUT of reach and recorded here instead of probed.
- **Causal chain**: `grep Synchronized` over src/runtime and src/il2cppc
  returns NOTHING (only an unrelated "synchronized" comment in
  safehandle.h:32 and monitor.cpp:262). MethodMap.Hashtable.cs has no
  Synchronized case; the runtime has no `il2cpp_hashtable_synchronized`
  icall.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Hashtable.cs` (add a
  Synchronized case) + `src/runtime/include/internal/il2cpp_hashtable.h` +
  hashtable.cpp (add the wrapper icall).

## D-HT-1 -- boxed double/char/bool/other value-type keys: net48 value-hash vs runtime address-hash (divergence)

- **Phenomenon**: net48 hashes a boxed `double 5.0` by VALUE (two boxes of
  5.0 are the SAME key), but the runtime hashes every non-string/non-Int32
  key by ADDRESS (`5.0` box != `5.0` box). A cross-box value lookup would
  therefore DIFF-FAIL (hit on net48, miss on runtime). hashtabledeep.cs
  deliberately probes ONLY string / boxed-int32 / custom-reference keys (the
  three families where net48 and the runtime agree) -- see the case header.
- **Causal chain**: hashtable.cpp hash contract (documented in
  il2cpp_hashtable.h:41-53): String -> ordinal content hash, boxed System.
  Int32 -> value hash, EVERY OTHER KEY -> address hash (pointer >> 3).
  net48's default Hashtable comparer calls object.GetHashCode()/Equals()
  which for Double is value-based.
- **Suspected root file**: `src/runtime/src/hashtable.cpp` -- extend the
  boxed-value hash to the Double (and ideally Char/Bool/etc.) families to
  match net48 value semantics. This is a REAL behavioral divergence, not a
  harness artifact.

## D-HT-3 -- Keys[i]/Values[i] positional alignment and raw enumeration order not asserted

- **Phenomenon**: net48 Keys/Values are LIVE views; the runtime returns
  SNAPSHOT arrays. Their raw enumeration ORDER is unspecified on net48
  (hash-dependent) and bucket-index on the runtime, so neither the order nor
  positional alignment is a byte-stable contract across the two runtimes.
  hashtabledeep.cs asserts only the ORDER-INDEPENDENT snapshot surface
  (non-null, distinct, element count == Count).
- **Not a fixable defect** in the test domain; consistent with hashdeep.cs
  and sldeep's recorded constraints.

## D-CAST-NULL -- castclass on null: transpiled code throws instead of returning null (cross-cutting)

- **Phenomenon**: `(string)null` (castclass of a null reference) must yield
  null per ECMA-335 (castclass on null is a no-op). The transpiled C++
  emits `is_instance(obj, &System_String_class) ? cast : throw
  InvalidCastException`, and `il2cpp_is_instance(nullptr, ...)` returns
  false (boxing.cpp:119), so a null object reaches the throw arm. Verified
  empirically: the first hashtabledeep build DIFF-FAILED at runtime with
  `Unhandled managed exception: Unable to cast object to the target type`
  on `(string)hi[-1]` where `hi[-1]` is null. Removed from the case (the
  probe now uses `hi[-1] == null`, no cast).
- **Causal chain**: the castclass emission (CodeGenerator.cs) lacks the null
  short-circuit that ECMA castclass requires. It affects ANY `(T)x` where x
  is an object-typed expression that can be null (not just Hashtable lookups).
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` castclass
  emission -- emit `(obj == nullptr ? nullptr : is_instance ? cast :
  throw)`. Recorded, NOT fixed (batch constraint: test cases only).
