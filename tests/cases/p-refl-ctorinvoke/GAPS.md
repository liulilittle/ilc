# GAPS.md — p-refl-ctorinvoke (GAP-5 / P1)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: ConstructorInfo.Invoke(object[]) + Type.GetConstructor(Type[])

- **Phenomenon**: no runtime path existed for the net48 ctor surface:
  `typeof(C).GetConstructor(new Type[]{...})` and
  `ctorInfo.Invoke(new object[]{...})` (creating a new instance) both failed
  LOUD at transpile.
- **Fix**: the typeinfo method table carries the ctors (name ".ctor", flags
  bit2 = ctor, bit0 = public) — the same table GetConstructors() /
  il2cpp_methodinfo_invoke use.
  - `il2cpp_typeinfo_get_constructor(typeName, types)`: matches a PUBLIC ctor
    by the Type[] arity and returns the DISAMBIGUATED ctor member value
    `"<FullName>::.ctor"` (the carrier the Invoke later resolves) or null.
  - `il2cpp_constructorinfo_invoke(memberValue, args)`: parses the declaring
    type, joins the typeinfo, allocates a fresh instance and RUNS the ctor on
    it through il2cpp_methodinfo_invoke's instance-'this' injection (the ctor
    fnPtr is the void(C* this, args...) ABI).
- **RED**: both calls threw LOUD (transpile mapping gap).
- **GREEN**: all 7 records byte-exact.

## Note (W7 boundary)

- `ConstructorInfo.Name` on the disambiguated value is NOT printed by the
  probe; only the constructed object's observable state is compared, so the
  member-value disambiguation is byte-exact-invisible.