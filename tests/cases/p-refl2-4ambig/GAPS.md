# GAPS.md — p-refl2-4ambig (refl2 item 4)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: Type.GetMethod(name) / (name, BindingFlags) AmbiguousMatchException

- **Phenomenon**: the no-types GetMethod query returned the FIRST public
  same-name method when a type exposed multiple overloads (own + base chain).
  net48 (probe-pinned): public same-name overloads on the queried type or its
  bases make GetMethod(name) AMBIGUOUS -> AmbiguousMatchException; a unique
  public name resolves; an absent name is null.
- **Fix (item 4, pre-audit)**: runtime typeinfo.cpp `QueryManagedMethod` (used
  by `il2cpp_typeinfo_get_method` and `il2cpp_typeinfo_get_method_flags`)
  counted the public-instance-static same-name methods across the own + base
  chain: >1 -> AmbiguousMatchException; 1 -> the match; 0 -> null. A same-name
  PRIVATE method does NOT count (net48 hides it under the default binding).
  The exact-signature (`get_method_types`) and raw primitive queries are
  unchanged.

## Audit DEFECT-R2-AME: over-broad ambiguity (same-signature shadowing)

- **Phenomenon**: the raw same-name count threw AmbiguousMatchException for
  DERIVED same-signature shadowing too -- a derived `new` or `override` F()
  together with the base F() (same signature) counted 2 and made the no-types
  GetMethod throw. net48 (audit oracle live): the most-derived same-signature
  method SHADOWS its base-class twin -> unique hit, no throw. Audit red runs:
  auditA-red-{ambhide,ovr2} (runtime threw/missed), auditB shadow3 (runtime
  exit 1 AME while net48 found the override/hide).
- **Fix**: `QueryManagedMethod` now groups candidates by PARAMETER signature
  (arity + per-param full name via the shared ParamEncodingToFullName
  projection) and counts only the most-derived (lowest chain-level) entries of
  each signature key -- base same-signature entries are shadowed (both `new`
  and `override` shadow). Distinct surviving signatures (real overloads) still
  roll up to AmbiguousMatchException; a unique name returns; absent returns
  null. Exact-signature / raw primitive queries unchanged.
- **RED (augmented probe)**: records(oracle=18 runtime=7) diff=11 -- all hide/
  override/baseonly/inherit/stat.new records missing (runtime threw AME on the
  first shadowed query after the original 7 records).
- **GREEN**: 18/18 records byte-exact (original 7 unchanged + hide-by-new,
  virtual+override, base-only same-signature, inherited-not-overridden,
  base-only-unique control, and cross-layer static `new` hiding base instance).

## Known simplification (W7)

- The ambiguity verdict groups by signature using the typeinfo-encoded param
  types. net48's binder would apply best-arity matching in some exotic shapes;
  the probe set pins the document lattice (unique / absent / base-unique /
  private, plus override / new / static-new shadowing), exotic binder paths are
  outside the simplified model.
- A same-signature pair at the SAME chain level (non-inheritance collision,
  e.g. static+instance with identical signature in one type) is not expressible
  in C# (CS0111) so cannot be oracle-pinned; the runtime defensively counts
  same-level pairs as distinct (ambiguous). Cross-layer static/instance
  interplay (derived `new static` hiding base instance F()) IS expressible and
  is pinned by the probe: net48 returns the unique most-derived hit.