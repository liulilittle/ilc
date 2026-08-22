# ValueTuple Local-Storage Defect — Fix Design

Status: RESEARCH REPORT (read-only investigation; no code changed)
Scope: il2cppc transpiler (`E:\il2cpp\src\il2cppc`), net9 + net10 pipelines.
Deliverable: exact root-cause chain + insertion points for typing IL locals whose
signature type is `GENERICINST System.ValueTuple`N` as the emitted P5 struct
(`System_ValueTuple_N_<fields>`), struct-definition placement, and read-back
(ldfld/ldelema) object-cast bypass.

---

## 1. Defect evidence (fresh, this session)

| Evidence | Location |
|---|---|
| Exclusion note: "the P5 tuple LOCAL-STORAGE path emits `int32_t locN` for the tuple target" | [READ:E:\il2cpp\tmp\icallcase\icallcase.cs:31-33] |
| Current gen_net9 output has NO SinCos/ValueTuple hits (grep "SinCos\|ValueTuple" over `tmp/icallcase/gen_net9/src/Program.cpp` → 0 matches); `loc1` at line 129 is now `Il2CppObject*` holding a string (stloc.1 @0x036B, line 483) — i.e. the test source was regenerated **without** SinCos, confirming exclusion-not-fix | [BASH:Get-ChildItem/grep outputs] |
| Wiring: `case "SinCos": ... I("il2cpp::runtime::icall_math_sincos", 1, false, false, false, new[] { "d" }, tupleReturnFields: new[] { "d", "d" })` | [READ:E:\il2cpp\src\il2cppc\Emit\MethodMap.Math.cs:78-81]; MathF twin at MethodMap.MathF.cs:109 |
| TupleReturnFields contract ("CodeGenerator allocates a ValueTuple local, passes &tuple.Field addresses as the LAST arguments") | [READ:E:\il2cpp\src\il2cppc\Emit\MethodRefResolver.cs:166-172] |

## 2. Root cause chain (exact causal path)

For `var sc = Math.SinCos(x);` Roslyn emits `call (double,double) Math::SinCos(float64)` + `stloc.N`.

1. **Fold split.** StackFold splits call+store into
   `CallStmt(Resolved.TupleReturnFields=["d","d"])` + `AssignStmt(target=locN, Value=DiscardedValueExpr)`
   (pair detected at CodeGenerator.cs:2067-2069).
2. **locKind misclassification.** The generic propagation loop
   ([READ:CodeGenerator.cs:1196-1202]) evaluates `ExprKind(a.Value, locKind)`;
   `DiscardedValueExpr` has no arm and falls to the default `_ => "val"`
   ([READ:CodeGenerator.cs:3255]) → `locKind["locN"] = "val"`.
3. **IL-authoritative kind is bare "v".** `LocalTypeKind`
   ([READ:MethodBodyReader.cs:264-287]): the GENERICINST (0x15) arm resolves
   defName = `System.ValueTuple`2`, which matches neither
   `TaskAwaiter`1` nor `AsyncTaskMethodBuilder`1` (tests at 282-286) → returns
   bare `"v"` (line 286).
4. **Declaration falls through every arm.** In the declaration pass,
   `ilKind = bodyInfo.LocalKinds[di]` ([READ:CodeGenerator.cs:1250-1253]):
   - not `"l"`/`"d"` → skips 1254-1258;
   - not `"r"` → skips 1283-1319;
   - **not prefix `"v:"`** (bare `"v"` has no colon) → skips the struct-typed
     arm at 1325-1368;
   - locKind is `"val"` → reaches the default at [READ:CodeGenerator.cs:1481-1484]
     → emits **`int32_t locN;`**.
5. **P5 emission assigns a struct into it.** The pair handler
   ([READ:CodeGenerator.cs:2067-2105]) defines the struct inline (2082-2086),
   allocates `System_ValueTuple_2_double_double __tuple_locN { };` (2087),
   calls the icall with `(&(__tuple_locN).Item1/2)` out-pointers (2090-2099),
   then emits `{target} = {tupleLocal};` at [READ:CodeGenerator.cs:2101-2102]
   → `int32_t locN = System_ValueTuple_2_double_double;` → **cmake compile error**.

Secondary instance of the same class: the direct AssignStmt path
([READ:CodeGenerator.cs:2303-2320]) writes the icall straight through
`&((target).Item1)` — the target slot must be struct-typed there too.

### Read-back defect (field reads on a tuple local)

`ldloc.N; ldfld Item1` folds to `FieldExpr{Object=LocExpr(locN), OwnerFullName=...}`:
- OwnerFullName comes from `ResolveField` MemberRef parent TypeSpec →
  `GenericInstantiationName` = `defName + "_" + join("_", args)`
  ([READ:StackFold.cs:3681], parent dispatch at 3822) → e.g.
  `System.ValueTuple`2_double_double`.
- `IsValueTypeOwner` ([READ:StackFold.cs:3130-3134]) does an exact TypeDef
  lookup; BCL ValueTuple has no TypeDef in `types` → **false**.
- `FieldAccess` therefore takes the OBJECT-CAST path
  ([READ:CodeGenerator.cs:5294-5316]):
  `((reinterpret_cast<System_ValueTuple_2_double_double*>(il2cpp_null_check(static_cast<Il2CppObject*>(locN)))))->Item1`
  — `static_cast<Il2CppObject*>` of a struct VALUE does not compile; even for a
  pointer slot it would misread fields past the object header.

### Name-mangling divergence (latent)

The same tuple shape gets different C++ names at three sites today:
- P5 emitter (CodeGenerator.cs:2080): codes via `ExprTypeCpp`
  (2232-2239): `double / float / int64_t / int32_t`;
- StackFold.GenericInstantiationName arg table (3654-3675):
  `double / float / long / int`;
- MetadataLoader.ReadTypeName (809+): `System.Double / System.Int32 / ...`.

Only pure d/f tuples coincide across sites. `(int,int)` / `(long,long)` tuples
would already mismatch. The fix must funnel ALL sites through ONE namer.

## 3. Design

### D1 — Canonical tuple namer (single authority)

Insert next to `ExprTypeCpp` at CodeGenerator.cs:2231-2239:

```csharp
// P5-fix: canonical mangled name for a flat primitive ValueTuple shape.
// Every site that names a tuple struct (P5 definition, local declarations,
// ldobj/ldelema reads) MUST use this -- ad-hoc Sanitize of generic-inst
// names diverges for i/l tuples ("int"/"long" vs "int32_t"/"int64_t").
internal static string TupleStructCppName(string[] fieldCodes) =>
    $"System_ValueTuple_{fieldCodes.Length}_{string.Join("_", fieldCodes.Select(ExprTypeCpp))}";

// local-sig / generic-inst element byte -> P5 field code; null = unsupported shape.
internal static string? TupleFieldCode(byte b) => b switch
{
    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i",
    0x0A or 0x0B => "l",
    0x0C => "f",
    0x0D => "d",
    _ => null,
};
```

Refactor the existing name construction at [EDIT POINT] CodeGenerator.cs:2080 to
call `TupleStructCppName(trfTuple)` (behavior identical for d/f tuples).

### D2 — LocalTypeKind recognizes ValueTuple GENERICINST

File: `Il\MethodBodyReader.cs`, GENERICINST arm at lines 264-287.
After `defName` is resolved (267-273) and before the awaiter test (282):

```csharp
// P5-fix: a flat primitive System.ValueTuple`N local is a TUPLE STRUCT slot,
// not a bare "v". Decode every generic argument to its P5 field code; ANY
// non-primitive argument (nested tuple/byref/handle/array) leaves the shape
// unsupported -> keep the legacy "v" fallback. Trailing-data consumption is
// unchanged (primitives carry none), so later locals stay aligned.
if (defName != null && defName.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
{
    var argc0 = /* already-read argc */;
    var codes = new string?[argc0];
    var ok = true;
    for (var i = 0; i < argc0 && ok; i++)
    {
        var ab = sig.ReadByte();
        if (ab is 0x11 or 0x12) _ = sig.ReadTypeHandle();      // consume as today
        else if (ab is 0x1D or 0x1F) _ = sig.ReadByte();
        else if (ab is 0x13 or 0x1E) _ = sig.ReadCompressedInteger();
        codes[i] = TupleFieldCode(ab);
        ok = codes[i] != null;
    }
    if (ok) return "tuple:" + CodeGenNames.TupleStructCppName(codes!);
}
```

Return kind format: `"tuple:System_ValueTuple_2_double_double"` — a NEW rich
kind parallel to the existing `"v:<FullName>"` convention (documented at
MethodBodyReader.cs:197-198). Note: `LocalKinds` crosses assembly boundaries
only as strings inside il2cppc, so referencing the Emit-side helper requires
either making the helper `internal` in a shared static class (e.g.
`CodeGenNames`) or duplicating the 4-entry byte→code map locally and letting
CodeGenerator do the mangling from `"tuple:d,d"` (alternative B — keeps Il
layer free of Emit references; recommended: return `"tuple:" + string.Join(",", codes)`
and mangle in CodeGenerator).

### D3 — Declaration-pass arm for tuple locals

File: `Emit\CodeGenerator.cs`. Insert between line 1258 (end of the l/d branch)
and line 1259 (comment block of the "r" branch):

```csharp
// P5-fix: an IL-declared GENERICINST System.ValueTuple`N local
// ("tuple:<codes>" -- MethodBodyReader.LocalTypeKind) is authoritative, same
// doctrine as the int64/double/"r"/"v:" branches above: declare the slot as
// the canonical P5 tuple struct. Zero-init mirrors the P5 stack local
// (line 2087) so a GC frame scan never sees garbage (tuples wired here are
// all-primitive, so no frame roots are needed -- unlike the "v:" ref-field
// arm below).
if (ilKind != null && ilKind.StartsWith("tuple:", StringComparison.Ordinal))
{
    w.Line($"{TupleStructCppName(ilKind[6..].Split(','))} {d} {{ }};");
    continue;
}
```

This alone fixes the reported defect: `locN` becomes
`System_ValueTuple_2_double_double locN { };` and the store at line 2101 compiles.

### D4 — Seed locKind for fold temps (covers BOTH P5 shapes)

Temps (`tN`) have no IL signature, so D3 cannot type them. Two seeds:

(a) **ExprKind arm** — insert in `ExprKind` between lines 3244 and 3245:

```csharp
// P5-fix: a tuple-return icall assigned to a temp types the temp as the
// tuple struct (direct AssignStmt path, CodeGenerator 2303-2320), and any
// LocExpr copy of it propagates the struct kind transitively.
CallExpr c when c.Resolved?.TupleReturnFields is { Length: > 0 } trfK
    => TupleStructCppName(trfK),
```

The existing generic loop (1196-1202) then seeds `locKind[target]` itself — no
new pass required. Declaration flows through the existing catch-all at
1481-1482 (`{kind} {d};` — fires because the kind is neither ""/"ref"/"val").

(b) **Split-pair seed** — the CallStmt+DiscardedValueExpr pair's target gets
`"val"` from ExprKind(DiscardedValueExpr). Insert immediately AFTER the M5
wiring loop at [EDIT POINT] CodeGenerator.cs:1206-1208 (before `var refLocals`
at 1209), mirroring the M5 pattern:

```csharp
// P5-fix: the split CallStmt(TupleReturnFields)+AssignStmt(DiscardedValueExpr)
// pair stores the filled tuple into its target -- the IL signature may not be
// decoded as a tuple (fold temps), so re-seed from the CALL's wiring. Runs
// after the generic loop so it wins over ExprKind(DiscardedValueExpr)="val".
for (var i = 0; i < stmts.Count; i++)
{
    if (!reachable[i]) continue;
    if (stmts[i] is CallStmt csT && csT.Resolved?.TupleReturnFields is { Length: > 0 } trfS
        && i + 1 < stmts.Count && stmts[i + 1] is AssignStmt asT
        && asT.Value is StackFold.DiscardedValueExpr)
        locKind[asT.Target] = TupleStructCppName(trfS);
}
```

With (a)+(b), every tuple slot — IL local or temp — carries the struct-name
kind, and copies (`locB = locA`) propagate it via the `LocExpr` arm (3249).

### D5 — Struct definition placement: generated.h, emitted once

Doctrine already states "struct layout definitions all live in generated.h
(cross-type visible)" ([READ:CodeGenerator.cs:488-491]). Today the P5 emitter
defines the struct INLINE mid-function with a TU-wide macro guard
(CodeGenerator.cs:2082-2086) — legal but fragile: the declaration block for
`locN` is emitted at function entry (lines 1217-1485), which can precede the
inline definition site, and other TUs get nothing.

Fix:
1. Add `var tupleStructDefs = new SortedSet<string>();` near line 480 (next to
   `allSrc`).
2. At both P5 emission sites (split path ~2078-2086 and direct path — add the
   same registration at 2308-2320), register the full definition line:
   `tupleStructDefs.Add($"struct {TupleStructCppName(codes)} {{ {fields}; }};");`
   (SortedSet ⇒ deterministic order across runs.)
3. Emit into the header AFTER the per-type loop completes and BEFORE the write
   at line 856 — concretely insert between [EDIT POINT] CodeGenerator.cs:804
   (end of per-type foreach) and line 806 (`// static field storage declarations`):

```csharp
generatedHeader.Line();
generatedHeader.Line("// P5 ValueTuple struct shapes referenced by generated bodies");
foreach (var def in tupleStructDefs)
    generatedHeader.Line(def);
```

Ordering inside generated.h is irrelevant for correctness (every .cpp includes
`"generated.h"` at its top, CodeGenerator.cs:526, and only function BODIES use
the type), but this placement keeps all type definitions in one section.
4. DELETE the inline `#ifndef IL2CPP_TUPLEDEF_*` block at CodeGenerator.cs:2082-2086
   (keep the `{tupleTypeName} {tupleLocal} {{ }};` allocation at 2087). The macro
   guard becomes dead once the header owns the single definition (ODR-clean,
   cross-TU visible — required by the ldobj/ldelema paths in D7 which may fire
   in a TU that never executes the P5 split path).

### D6 — Field reads bypass the object-cast (ldfld/ldflda/stfld)

Root fix is ONE predicate change in StackFold — the emitters already have the
value-type receiver path:

[EDIT POINT] `Ir\StackFold.cs:3130-3134`:

```csharp
static bool IsValueTypeOwner(string ownerFullName, IReadOnlyList<TypeDef> types)
{
    // P5-fix: BCL System.ValueTuple`N instantiations are value-type receivers
    // but have NO TypeDef in `types` (never transpiled) -- the exact-match
    // lookup missed them and field reads took the null-check + object-cast
    // path (static_cast<Il2CppObject*>(structValue) does not compile).
    if (ownerFullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
        return true;
    var t = types.FirstOrDefault(x => x.FullName == ownerFullName);
    return t != null && t.IsValueType;
}
```

OwnerFullName arrives pre-mangled with args (`System.ValueTuple`2_double_double`,
StackFold.cs:3681), so the arity-carrying prefix matches. This flag propagates
through Ldfld/Ldflda/Stfld arms (StackFold.cs:408, 423, 430) and every IR clone
site (Normalize.cs, InlinePass.cs, DcePass.cs, TempSlotPass.cs, AllocPromoPass.cs
— all copy `IsValueTypeObject` verbatim, verified by grep).

Emitter behavior with the flag set (no changes needed):
- plain value local receiver (`ldloc.N; ldfld Item1`): FieldAccess
  CodeGenerator.cs:5306 → `(&(locN))->Item1` ✔
- address receiver (`ldloca.s`; constrained call / ldflda): 5305 → `locN->Item1` ✔
- ref-field wrap: tuples are all-primitive here, `IsRefField` stays false ✔

### D7 — ldelema / raw-address reads

`ldelema_<tok>` returns a width-independent raw pointer as `intptr_t`
(CodeGenerator.cs:5911-5917). Two consumer shapes:

1. **`ldobj_<tok>` through the address** (5873-5897): dereference is
   `(*reinterpret_cast<{TypeCppName(ldobjName)}*>(addr))` where `ldobjName =
   TypeTokenFullName(md, tok)` — for a ValueTuple TypeSpec this yields
   `System.ValueTuple`2<double,double>` (MetadataLoader.GenericInstantiationName,
   MetadataLoader.cs:783-792) → Sanitize → `System_ValueTuple_2_double_double`.
   Coincidentally correct ONLY for d/f tuples; `(int,double)` would sanitize to
   `System_ValueTuple_2_System_Int32_double` ≠ header name. Fix: in the ldobj
   handler, before the switch at 5880, normalize ValueTuple tokens:

```csharp
// P5-fix: route ValueTuple ldobj through the CANONICAL tuple namer --
// Sanitize of the generic-inst full name diverges for i/l field tuples.
if (ldobjName.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
{
    var codes = TupleCodesFromGenericInstName(md, ldobjTok); // decode TypeSpec args (GenericInstantiationName-style consumption)
    if (codes != null) ldobjName = TupleStructCppName(codes);
}
```

2. **`ldelema; ldfld` directly** (address receiver, no ldobj): with D6 the
   FieldAccess value-type arm receives `Object = CallExpr(ldelema_…)` — an
   rvalue `intptr_t`; neither 5304 arm fits and 5306 would emit `(&(rvalue))->`
   (compile error). Add a third receiver arm at [EDIT POINT]
   CodeGenerator.cs:5304 (before the LocAddrExpr check):

```csharp
// P5-fix: a raw element/base address (ldelema intptr_t, folded byref) is
// already a POINTER -- reinterpret it to the owner struct; no address-of,
// no null check (a value-type element address cannot be null).
if (obj is CallExpr lc && lc.Function.StartsWith("ldelema_", StringComparison.Ordinal))
    return $"reinterpret_cast<{TypeCppName(owner)}*>({objCode})->{fieldCppName}";
```

`TypeCppName(owner)` for `System.ValueTuple`2_double_double` sanitizes to the
canonical name for d/f tuples; for full generality route it through
`TupleStructCppName` when `owner` starts with `System.ValueTuple`` (same
normalization as ldobj). Note this arm also benefits user-struct arrays
(`arr[i].field`), which currently hit the same rvalue-address-of hazard.

## 4. Requirement checklist

| # | Requirement | Status |
|---|---|---|
| R1 | Type GENERICINST `System.ValueTuple`2` locals as `System_ValueTuple_N_<fields>` | DESIGNED — D2 (kind decode, MethodBodyReader.cs:264-287) + D3 (declaration arm, insert CodeGenerator.cs:1258/1259) + D4 (temp seeds, ExprKind 3244/3245 + post-1208 pass) |
| R2 | Struct definition at file scope, once | DESIGNED — generated.h via SortedSet collected during emission; emit between CodeGenerator.cs:804 and :806; delete inline guard 2082-2086 (D5) |
| R3 | ldelema/field reads bypass object-cast | DESIGNED — D6 (IsValueTypeOwner prefix match, StackFold.cs:3130-3134 → FieldAccess value arms 5304-5306) + D7 (ldobj normalization 5876-5897; ldelema receiver arm at 5304) |
| R4 | Exact file:line insertion points | PROVIDED throughout (all [EDIT POINT]s cite current line numbers) |
| R5 | Report only, written to tmp/reports/valuetuple_local_design.md | DONE (this file) |

## 5. Out of scope / explicit non-goals

- Nested tuples (`ValueTuple`3<...,ValueTuple<...>>`), 8-arg TRest collapse,
  reference-typed tuple fields: the resolver only wires d/f/l/i
  (MethodRefResolver.cs:167); the D2 decoder rejects any non-primitive arg and
  keeps the legacy `"v"` fallback (loud failure preserved downstream).
- Boxed tuples (`box ValueTuple`2`): unwired surface.
- Tuple RETURN values from user methods (declarations never mention tuple
  structs today; icalls are void + out-pointers).

## 6. Verification plan (for the implementer)

1. Un-exclude the tests: remove the note at tmp/icallcase/icallcase.cs:31-33 and
   add `Check("math.sincos", Math.SinCos(0.5).Sin == Math.Sin(0.5) && ...)` +
   a MathF.SinCos check (float tuple exercises the `f` code path).
2. Regenerate net9 + net10 (`gen_net9/src/Program.cpp` should contain
   `struct System_ValueTuple_2_double_double` ZERO times and generated.h once;
   `locN` declared as the struct).
3. cmake build both TFMs → zero errors (C0 gate).
4. Run → `PASS math.sincos` / `PASS mathf.sincos`.
5. Negative probe: a `(int,long)` tuple consumer (e.g. `Math.DivRem` variant or
   hand-written IL) to prove the canonical namer unification (D1) — this shape
   was silently divergent before the fix.
