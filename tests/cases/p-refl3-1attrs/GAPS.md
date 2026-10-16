# GAPS.md — p-refl3-1attrs (refl3 item 1)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: CustomAttributeExtensions generic + IsDefined surface

- **Phenomenon**: three reflection surfaces were phantom (REFL-EXPLORE2 z P08/P09/P10):
  - `CustomAttributeExtensions.GetCustomAttribute<T>(MemberInfo)` / `GetCustomAttributes<T>(MemberInfo)`
    (generic extension, MethodSpec) — "generic instantiation has no BCL mapping".
  - `MemberInfo.IsDefined(Type, bool)` — no MethodMap arm.
  - `Attribute.IsDefined(MemberInfo, Type[, bool])` — no MethodMap arm.
- **Fix**:
  - New `MethodMap.CustomAttributeExtensions.cs`: the generic singular/plural
    route the receiver ELEMENT (a Type VALUE full-name string for typeof(X)
    receivers) plus the generic-argument T full name (genArgNames[0], injected
    as `il2cpp::il2cpp_string_intern_literal`) to the existing
    `il2cpp_attribute_get_single_ex_named` / `_custom_ex_named` registry
    (attribute.cpp CollectAttributes: attributeType assignability + inherit
    walk + AttributeUsage). net48 default inherit=1 for both generic forms.
  - `MethodMap.Object.cs`: `MemberInfo.IsDefined(Type, bool)` → the new
    managed-string existence primitive `il2cpp_attribute_is_defined_named`.
  - `MethodMap.Attribute.cs`: `Attribute.IsDefined(element, Type[, bool])` →
    the same primitive (2-arg default inherit=1, 3-arg explicit bool).
  - `attribute.cpp/.h`: `il2cpp_attribute_is_defined_named` mirrors the
    `_ex_named` element/attributeType resolution (ResolveElements) and answers
    `CollectAttributes(..., inherit) > 0` — the net48 existence contract.
- **net48 semantics pinned by the oracle**:
  - singular generic present -> the attribute instance (non-null), absent/other
    type -> null; inherit default TRUE (base-chain attr found via the generic).
  - plural generic -> the attribute array; `(object[])` cast over the returned
    IEnumerable exposes the SAME length byte-exact (an attribute array's
    runtime klass is the object-array class, castclass object[] passes).
  - `Attribute.GetCustomAttribute(element, attributeType)` (static 2-arg
    singular) defaults inherit=TRUE (oracle `gastat.singular.inherit=False` —
    this supersedes the stale "singular defaults inherit=false" comment in
    attrinherit.cs; attrinherit passes unchanged because its runtime arm already
    injects inherit=1).
  - IsDefined 2-arg defaults inherit=true; 3-arg explicit.
- **RED**: runtime exit 1, all 27 records missing (phantom).
- **GREEN**: 27/27 records byte-exact (present/null/wrongtype/assignability/
  inherit for the generic singular+plural; IsDefined own/inherit/filter for
  both the instance and static forms).

## Known simplification (W7)

- The generic extensions and IsDefined are answered through the TYPE-level
  attribute registry keyed by element name. Method/field/property (name-string)
  receivers are not routed by these arms (the RF-N6 member registry has no
  attributeType-filtered query); the probe's receivers are Type VALUES (typeof),
  whose full-name string IS the type-level registry key — the byte-exact scope.
- `GetCustomAttribute<T>`/`GetCustomAttributes<T>` on non-MemberInfo elements
  (ParameterInfo/Module/Assembly) fail LOUD (no net48 form in the probe set).