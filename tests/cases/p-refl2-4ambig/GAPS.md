# GAPS.md — p-refl2-4ambig (refl2 item 4)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: Type.GetMethod(name) / (name, BindingFlags) AmbiguousMatchException

- **Phenomenon**: the no-types GetMethod query returned the FIRST public
  same-name method when a type exposed multiple overloads (own + base chain).
  net48 (probe-pinned): public same-name overloads on the queried type or its
  bases make GetMethod(name) AMBIGUOUS -> AmbiguousMatchException; a unique
  public name resolves; an absent name is null.
- **Fix**: runtime typeinfo.cpp `QueryManagedMethod` (used by
  `il2cpp_typeinfo_get_method` and `il2cpp_typeinfo_get_method_flags`) counts
  the public-instance-static same-name methods across the own + base chain:
  >1 -> AmbiguousMatchException; 1 -> the match; 0 -> null. A same-name
  PRIVATE method does NOT count (net48 hides it under the default binding).
  The exact-signature (`get_method_types`) and raw primitive queries are
  unchanged.
- **RED**: `[amb.multi.throws]` False (runtime returned the first hit).
- **GREEN**: all 7 records byte-exact (unique, absent, base-unique,
  priv.unique, and both ambiguity throws).

## Known simplification (W7)

- The ambiguity verdict counts methods by BindingFlags-visibility only
  (arity is not considered -- net48's binder would otherwise try a best
  arity match in some shapes). The probe set pins the document case; exotic
  binder-selection paths are outside the simplified model.