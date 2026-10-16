# GAPS.md — p-refl4-2gti (refl4 item 2)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, sha-equal).

## Fixed: IntrospectionExtensions.GetTypeInfo(Type) fold-identity

- **Phenomenon**: `IntrospectionExtensions.GetTypeInfo(Type)` resolved to the
  phantom generated-name path (`System_Reflection_IntrospectionExtensions_
  GetTypeInfo_<hash>` — an undefined symbol, the whole method COMPILE-BROKEN
  at link). net48 returns a `System.Reflection.TypeInfo` (a Type SUBCLASS), so
  every Type member on the result resolves with ordinary Type semantics.
- **Fix**:
  - Runtime: `il2cpp_typeinfo_get_type_info(typeName)` -- the runtime IDENTITY
    (returns the receiver Type VALUE string unchanged). A fold-time
    IdentityMarker would lose the tracked `"System.Type"` receiver hint (the
    folded StringConstExpr result stores no type name); the real icall lets the
    standard static-call path record ReturnTypeName `"System.Type"` so every
    downstream Type member resolves on the Type registry receiver.
  - Transpiler: `System.Reflection.IntrospectionExtensions` owner case for
    `GetTypeInfo(Type)` (csc emits the STATIC extension as `call`), mapping to
    the identity icall with `IsInstance=false`.
  - TypeInfo-only members (`DeclaredMethods` / `DeclaredFields` /
    `DeclaredProperties` / `DeclaredEvents` / `DeclaredMembers` /
    `DeclaredNestedTypes` / `GetDeclared*`) throw explicit LOUD
    NotSupportedException (`System.Reflection.TypeInfo` owner case) — net48
    has them, the simplified model does not.
- **net48 semantics pinned by the oracle**:
  - GetTypeInfo().Name -> the SHORT name; FullName -> the full name;
    IsClass/IsValueType/BaseType/== = the Type-domain answers.
  - `typeof(Enum).GetTypeInfo()` keeps Type semantics (a value type).
  - `typeof(int).GetTypeInfo()` works too (the identity icall survives an
    unregistered BCL name).
- **RED**: runtime exit 1, all 13 records missing (link COMPILE-BROKEN).
- **GREEN**: 13/13 records byte-exact, firstDiff=-1, sha-equal.

## Known simplification (W7)

- The TypeInfo-ONLY surface (Declared*/GetDeclared*) is LOUD by design — not
  probed, must fail at transpile (not a silent wrong answer).