# edit_interlocked.md — MethodMap.Interlocked.cs audit vs runtime signatures

Scope: `src/il2cppc/Emit/MethodMap.Interlocked.cs` (owned file only) verified
line-by-line against `src/runtime/include/internal/il2cpp_interlocked.h` and
`src/runtime/src/interlocked.cpp`. No builds, no git ops (per constraints).

## 1. Runtime contract (verified this pass)

All 19 icalls exist; signatures quoted from il2cpp_interlocked.h:

| icall | signature | h line |
|---|---|---|
| il2cpp_interlocked_increment | `int32_t (int32_t*)` | 46 |
| il2cpp_interlocked_increment64 | `int64_t (int64_t*)` | 49 |
| il2cpp_interlocked_decrement | `int32_t (int32_t*)` | 52 |
| il2cpp_interlocked_decrement64 | `int64_t (int64_t*)` | 55 |
| il2cpp_interlocked_exchange | `int32_t (int32_t*, int32_t)` | 58 |
| il2cpp_interlocked_exchange64 | `int64_t (int64_t*, int64_t)` | 61 |
| il2cpp_interlocked_exchange_pointer | `void* (void**, void*)` | 65 |
| il2cpp_interlocked_add | `int32_t (int32_t*, int32_t)` | 68 |
| il2cpp_interlocked_add64 | `int64_t (int64_t*, int64_t)` | 71 |
| il2cpp_interlocked_compare_exchange | `int32_t (int32_t*, int32_t, int32_t)` | 75 |
| il2cpp_interlocked_compare_exchange64 | `int64_t (int64_t*, int64_t, int64_t)` | 79 |
| il2cpp_interlocked_compare_exchange_pointer | `void* (void**, void*, void*)` | 84 |
| il2cpp_interlocked_speculation_barrier | `void ()` (empty body, cpp:171) | 92 |
| il2cpp_interlocked_exchange_float | `float (float*, float)` | 97 |
| il2cpp_interlocked_exchange_double | `double (double*, double)` | 98 |
| il2cpp_interlocked_compare_exchange_float | `float (float*, float, float)` | 99 |
| il2cpp_interlocked_compare_exchange_double | `double (double*, double, double)` | 100 |
| il2cpp_interlocked_add_float | `float (float*, float)` | 101 |
| il2cpp_interlocked_add_double | `double (double*, double)` | 102 |

Param order in every family is `(location[, value[, comparand]])` — identical to
the managed push order. `IcallArgOrder` (CodeGenerator.cs:6141) contains ONLY
`icall_array_find_index` → identity arg order for all Interlocked icalls. ✓

## 2. Mapping-by-mapping verdicts

Emitter facts the verdicts rest on (all read this pass):
- Cast-code switch (CodeGenerator.cs:6250–6481) has NO `"f"` value cast and NO
  `"addr:f"` address cast. Available relevant codes: `i`→int32_t, `l`→int64_t,
  `d`→double, `pobj`→reinterpret_cast<void**>, `""`=raw passthrough.
- R4/R8 locals AND params are DOUBLE slots: MethodBodyReader.cs:207
  (`0x0C or 0x0D → "d"`), ParamTypesCpp CodeGenerator.cs:6878 (`"d"/"f" → "double"`).
- Result kinds: default=int32 slot, `"l"`=int64 temp (CodeGenerator.cs:1401),
  `"d"`=double temp (:1410); rr=true = reference return (house convention,
  cf. string Concat maps).

| mapping (ps bytes) | target + casts + resultKind | verdict |
|---|---|---|
| Increment/Decrement i4 `[08]` | `_increment`/`_decrement`, [""], rv=T | ✓ exact (`int32_t(int32_t*)`) |
| Increment/Decrement i8 `[0A]` | `_increment64`/`_decrement64`, [""], "l" | ✓ exact |
| Add/ExchangeAdd i8,i8 | `_add64`, ["","l"], "l" | ✓ exact |
| Add/ExchangeAdd i4,i4 | `_add`, ["","i"] | ✓ exact |
| Exchange i8,i8 | `_exchange64`, ["","l"], "l" | ✓ exact |
| Exchange i4,i4 | `_exchange`, ["","i"] | ✓ exact |
| Exchange obj,obj `[1C]` | `_exchange_pointer`, ["pobj",""], rr=T | ✓ exact (`pobj` reinterprets Il2CppObject** → void**; value Il2CppObject* → void* implicit) |
| CompareExchange i8 ×3 | `_compare_exchange64`, ["","l","l"], "l" | ✓ exact |
| CompareExchange i4 ×3 | `_compare_exchange`, ["","i","i"] | ✓ exact |
| CompareExchange obj ×3 | `_compare_exchange_pointer`, ["pobj","",""], rr=T | ✓ exact |
| SpeculationBarrier `[]` | `_speculation_barrier`, rv=F | ✓ exact (empty impl cpp:171) |

### Float/double routing — NOT a mismatch (kept as-is)

`Exchange/CompareExchange(ref float …)` (ps 0x0C) routes to
`il2cpp_interlocked_exchange_double` / `..._compare_exchange_double`
(["","d"(,"d")], resultKind "d"). The task asked specifically:
- *exchange_double takes double\* location?* YES (h:98) — and the emitted
  location IS a `double*`: a `ref float` local is an 8-byte double slot
  (MethodBodyReader.cs:207), passed uncast by the documented ldloca convention.
- *compare_exchange_float signature?* `float(float*, float, float)` (h:99) —
  it exists but is UNUSABLE here: its `float*` location cannot accept the
  double-slot address (no `"addr:f"` cast code exists; adding one requires
  CodeGenerator.cs, outside this file's ownership). Routing R4 to
  `*_float` would be a guaranteed C++ compile error at every call site.

This is the established float-in-double model, same rationale as
MethodMap.MathF.cs:8-19 ("every float ARGUMENT passes through uncast … every
float RESULT rides ResultKind \"d\"") and MethodMap.ValueType.cs:115 ("float
values live in double slots"). Value param `static_cast<double>` and result
kind "d" match `double(double*, double)` exactly. Atomicity is preserved over
the full slot; value bits exact.

Known model edge (pre-existing, unchanged): a ref-float targeting real 4-byte
storage (float field via ldflda → `(&obj->f)`, or array element — ldelema even
renders as intptr_t, CodeGenerator.cs:5916) would not type-match ANY available
icall/cast today. Not fixable within this file; noted for the orchestrator.

## 3. DEFECTS FOUND → FIXED (this pass)

Generic `Exchange<T>` / `CompareExchange<T>` (ps[0]==0x13 VAR) dispatched on
genArgs[0] but accepted only `{0x12 class, 0x1C object}` for the pointer arms.
Legal `where T : class` instantiations arrive with OTHER reference-type
encodings and fell to `_ => null` → unmapped call:

- **0x0E (string)** — documented arrival byte: genArgs are produced by
  ReadParamByte (MethodRefResolver.cs:266), which returns 0x0E raw;
  MethodMap.ArraySort.cs:12 documents `genArgs[0]: … 0x0E string`.
- **0x15 (GenericInst)** e.g. `List<int>` — clean parse, ReadParamByte
  cs:1312-1322 consumes marker+handle+args, returns 0x15.
- **0x1D/0x1F (sz/mvar arrays)** — ReadParamByte cs:1310 consumes the element,
  returns the marker.

Fix: both pointer arms now accept
`0x0E or 0x12 or 0x15 or 0x1C or 0x1D or 0x1F`. Correctness: the IR's uniform
reference representation makes EVERY reference-type slot an `Il2CppObject*`
(CodeGenerator.cs:3057-3058), so the raw-pointer swap target is identical for
all of them — no per-type divergence possible. Value-type encodings still fall
to null (LOUD downstream rejection), as required.

Edits: MethodMap.Interlocked.cs:81-87 (Exchange arm) and :109-110
(CompareExchange arm). Syntax mirrors the existing `0x0C or 0x0D` arm form.

## 4. Gaps reported, NOT wired (out of fix scope)

1. `Interlocked.Add(ref float/double, …)` (.NET Core 3.0+ overloads; NOT in the
   net48 baseline) is unmapped → null, although the runtime ships
   `il2cpp_interlocked_add_float` (h:101) / `il2cpp_interlocked_add_double`
   (h:102). If ever needed: route BOTH to `add_double` ["","d"], resultKind
   "d" (same float-in-double reasoning as §2). Left unwired: net48 surface
   doesn't reach it; adding speculative surface violates surgical scope.
2. `T` = bounded array (0x14 ARRAY) as generic arg: ReadParamByte has no 0x14
   case (MethodRefResolver.cs:1306-1323) so the signature parse itself would
   corrupt upstream of this map — wiring my side would not help. Pre-existing
   resolver limitation, outside owned file.
3. Non-generic object forms require ps[0]==0x1C exactly — correct, since the
   declared non-generic overload parameter IS object; string can only arrive
   via the generic path (now fixed).

## 5. Requirement checklist

- [x] Every mapping's CppName verified to exist in runtime (§1/§2) — 17/17 wired names exist, namespace prefix `il2cpp::runtime::` matches all.
- [x] Param counts/order verified (identity order; no IcallArgOrder entries) — §1.
- [x] Param casts vs runtime param types verified — §2 table.
- [x] Result kinds vs runtime return types verified — §2 (default i4 / "l" i8 / "d" r8 / rr=T void*).
- [x] exchange_double location question answered (double* — matches emitted double-slot addresses) — §2 float section.
- [x] compare_exchange_float signature answered (exists, unusable without addr:f cast) — §2 float section.
- [x] Generic branches per-genArgs targets audited; ref-type encoding gaps FIXED — §3.
- [x] Only the owned file edited; no builds; no git; no .opencode writes.
