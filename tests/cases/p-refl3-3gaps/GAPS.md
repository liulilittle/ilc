# GAPS.md — p-refl3-3gaps (refl3 item 3)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: CustomAttributeExtensions.IsDefined(MemberInfo, Type) (adjacent gap)

- **Phenomenon**: while wiring items 1-2, the probe revealed that the net48
  surface `typeof(X).IsDefined(typeof(T))` binds the STATIC EXTENSION
  `CustomAttributeExtensions.IsDefined(MemberInfo element, Type attributeType)`
  (net48 IL-owner, probe-verified via ildasm), NOT the instance
  `MemberInfo.IsDefined(Type)`. The extension had no MethodMap arm -> a
  phantom generated-name C++ call (`System_Reflection_CustomAttributeExtensions_
  IsDefined_<hash>` undeclared, making the cmake build FAIL -- the item-3 RED).
- **Fix**: `MethodMap.CustomAttributeExtensions.cs` wires `IsDefined(MemberInfo,
  Type)` with net48 default inherit = TRUE (appended literal) -> the managed-
  string existence primitive `il2cpp_attribute_is_defined_named` (same
  CollectAttributes walk as the item-1 IsDefined surface). Kept the instance
  `MemberInfo.IsDefined(Type)` 1-arg arm (MethodMap.Object.cs, same inherit
  default) for completeness.
- **RED**: cmake FAIL (undeclared extension symbol); with the arm disabled the
  runtime exits 1 and all 9 records are missing.
- **GREEN**: 9/9 records byte-exact (1-arg own/plain/wrongtype/inherit, the
  1-arg-vs-2-arg default cross-check, item-1 inherited singular cross-checks on
  a multi-attribute type, and an item-2 GetParameters cross-check on a
  parameterless ctor).

## Known simplification (W7)

- Same as item 1: `IsDefined` answers through the TYPE-level attribute registry
  keyed by element type name; method/field (name-string) receivers are not
  routed (the RF-N6 member registry has no attributeType-filtered query).