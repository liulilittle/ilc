# GAPS.md — p-refl-accessor (GAP-3 / P1)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: PropertyInfo.GetGetMethod()/GetSetMethod() unfrozen

- **Phenomenon**: the reflection accessor surface (`PropertyInfo.
  GetGetMethod()` / `GetSetMethod()`) was parked since reflprop2's D-PRP2-1 —
  `GetGetMethod` fell to the generated-name path / a mapping gap, the transpiler
  produced "this method was not transpiled". net48 returns the public accessor
  MethodInfo (non-null) or null when the property lacks that accessor.
- **Fix**: the typeinfo property table already carries `getterName` /
  `setterName` (TypeEmitter), so the runtime resolves the property by the
  deepest-declaring-type registry scan and returns the PUBLIC accessor NAME
  string (a MethodInfo VALUE) or null; MethodMap.Reflect.cs wires
  `GetGetMethod`/`GetSetMethod` to `il2cpp_typeinfo_property_get_accessor(_,
  0/1)` with a compile-time dir literal.
- **RED**: transpile mapping gap (method not transpiled).
- **GREEN**: all 6 records byte-exact.