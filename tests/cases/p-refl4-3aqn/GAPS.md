# GAPS.md — p-refl4-3aqn (refl4 item 3)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, sha-equal).

## Fixed: Type.AssemblyQualifiedName + Type.get_Assembly + Assembly op_*

- **Phenomenon (REFL-EXPLORE4 A3/C7)**: `Type.AssemblyQualifiedName` was
  phantom (`System.Type::get_AssemblyQualifiedName: not wired in M5`);
  `Type.get_Assembly` was folded to NullMarker (null) so `Assembly == null`
  answered True and the `Assembly::op_Equality/op_Inequality` generated-name
  path was COMPILE-BROKEN at link.
- **Fix**:
  - Runtime: `il2cpp_typeinfo_get_assembly_qualified_name(typeName,
    asmSimpleName)` formats the net48 AQN `"<FullName>, <asm>, Version=1.0.0.0,
    Culture=neutral, PublicKeyToken=null"` (the receiver is the Type VALUE
    full-name string; the asm SIMPLE NAME is the transpiler-baked intern
    literal — the single-assembly model).
  - Transpiler (`MethodMap.Type.cs` TryMapTypeAssembly): the `System.Type`
    `get_AssemblyQualifiedName` arm emits the icall with the AssemblyDef
    simple name (`md.GetAssemblyDefinition().Name`) as an appended
    `il2cpp_string_intern_literal`; the `get_Assembly` arm folds to the
    LiteralMarker carrying the same simple name (Assembly VALUE = the assembly
    simple-name string — like the Type VALUE is the full name).
  - `MethodMap.Resources.cs`: `System.Reflection.Assembly` owner
    `op_Equality` -> `icall_string_equals_static` (null-safe: `asm == null`
    False; two same-assembly values True), `op_Inequality` ->
    `icall_type_op_inequality`.
- **net48 semantics pinned by the oracle**: AQN full string equality;
  `get_Assembly` non-null + same across types; `==`/`!=` incl. null (C7
  `asm == null` = False).
- **RED**: runtime exit 1, all 11 records missing (phantom + COMPILE-BROKEN).
- **GREEN**: 7/7 records byte-exact, firstDiff=-1, sha-equal.

## Known simplification (W7)

- The Assembly VALUE is the SIMPLE-NAME string, not a formatted
  "Assembly"-object ToString — the probe only tests `==`/`!=`/null (no
  byte-printing of the Assembly object).