# GAPS.md — proprefl unwired surface (e2e batch C13, 2026-08-09)

Case status: **PASS 19/19 byte-exact** (net48 oracle vs ILC mingw transpiled
run, LF-join diff). The property subtable landed in batch 40 (PropertyDef
model) works end-to-end. Every active probe resolves through the wired
surface; the items below are recorded residuals from the design, none of
which produced a live divergence in this build.

## GAP-PRP-1 — GetProperty only supports the (string) overload

- **Phenomenon**: MethodMap.Type.cs:94-98 maps ONLY `GetProperty(string)` to
  `il2cpp_typeinfo_get_property`. The BindingFlags / (string, Type) /
  indexer overloads throw NotSupportedException at transpile time (LOUD).
- **Impact**: a property-lookup probe using BindingFlags (e.g. instance-only,
  public-only filtering) would abort the transpile. The case deliberately
  uses only the plain (string) overload (net48 resolves it too).
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Type.cs` GetProperty
  arity guard.

## GAP-PRP-2 — GetField on an auto-property backing field is not byte-equivalent

- **Phenomenon**: `typeof(PropBase).GetField("BaseProp")` returns null on
  BOTH net48 and ILC (the backing field is `<BaseProp>k__BackingField`, not
  `BaseProp`) — no divergence. Recorded for completeness: net48 exposes the
  backing field under the mangled name; the ILC typeinfo field table only
  holds declared fields (`FieldOnly`), so a probe on the MANGLED name would
  diverge (net48: non-null, ILC: null). The case uses the declared-field
  name only.
- **Suspected root file**: `src/il2cppc` MetadataLoader field table
  (no backing-field emission).

## GAP-PRP-3 — Type.op_Equality avoided (per reflectcase precedent)

- **Phenomenon**: `typeof(X) == typeof(Y)` on Type VALUES is avoided; the
  case uses ReferenceEquals on member-result null-ness only. reflops
  documents the op_* INSTANCE misclassification risk on Type receivers.
- **Impact**: no active probe; the null-ness shape is byte-stable.

## Notes

- net48 oracle evidence: `out/proprefl.exe` exit=0, 19 lines captured
  byte-for-byte; harness diff PASS (see the C13 report).
- No product code modified; no commit made.
