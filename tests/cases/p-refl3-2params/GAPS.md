# GAPS.md — p-refl3-2params (refl3 item 2)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: MethodBase.GetParameters() + the ParameterInfo property surface

- **Phenomenon**: `MethodBase.GetParameters()` and the entire
  `ParameterInfo` object face (Name / ParameterType / Position / IsOut) were
  phantom (REFL-EXPLORE2 z P07): no MethodMap arm, no runtime data source.
- **Fix**:
  - Transpiler: `MetadataLoader` reads each method's Param table
    (`mdInfo.GetParameters()` — names + `ParamAttributes.Out`) into
    `MethodDef.ParamNames` / `ParamOutFlags`; `TypeEmitter` bakes a compact
    `paramMeta` string (`"a|0;b|0"`, `'|'`- and `';'`-joined, per-param
    `name|outFlag`) into the new trailing `Il2CppMethodInfo.paramMeta` field
    (zero-delta for older tables: nullptr -> GetParameters answers empty).
    Generic clones carry the same metadata (CodeGenerator clone site).
  - Runtime: `il2cpp_typeinfo_get_parameters(memberName)` (MethodBase.
    GetParameters) scans the typeinfo registry for the DEEPEST type declaring
    the method (or parses a disambiguated `<FullName>::.ctor` value), resolves
    the method/ctor (own + base chain), splits the signature param encodings
    (up to 8 -- the exact-match 4-cap doesn't apply to enumeration), and
    builds a reference array of ParameterInfo VALUE strings.
  - ParameterInfo VALUE codec: `"name|typeFullName|position|flags"` — the
    narrow model's compact string. `ParamTypeValueName` decodes one signature
    param encoding into the Type VALUE full name (`i`->System.Int32,
    `r:System.String`->System.String, `r:i[]`->`[System.Int32` (array
    descriptor), `B:`/BYREF -> pointee name).
  - Runtime property decoders: `il2cpp_parameterinfo_get_name` /
    `get_parameter_type` (returns a Type VALUE) / `get_position` / `is_out`.
  - MethodMap: `GetParameters` on MethodInfo/MethodBase/ConstructorInfo (net48
    `() `overload), plus the `System.Reflection.ParameterInfo` owner case with
    the four property arms.
- **net48 semantics pinned by the oracle**:
  - arity (params.Length): parameterized ctors/methods count, parameterless -> 0.
  - Name is the metadata param name; Position is the 0-based signature slot.
  - `IsOut` distinguishes `out` (True) from `ref` (False) — the
    `ParamAttributes.Out` bit, not the BYREF type marker.
  - ParameterType is a Type VALUE (full name / short name / array checks /
    `typeof` equality all resolve on the Type domain).
- **RED**: runtime exit 1, all ParameterInfo records missing (phantom).
- **GREEN**: 25/25 records byte-exact (ctor+methods arities, names, positions,
  out flags, parameter types incl. `int[]` arrays / `object` / primitives /
  string, `ParameterType.FullName`/`.Name`/`.IsArray`/`.GetElementType`).

## Known simplification (W7)

- `ParameterInfo.ParameterType.IsByRef` is NOT reproduced (the probe omits it):
  the narrow Type VALUE codec decodes a BYREF param's type as the POINTEE's
  full name, and `Type.IsByRef` has no name-string predicate. net48 reports a
  `System.Int32&` class for `ref int`; the model reports `System.Int32`.
- Methods with >8 parameters are rejected (empty array) — the probe set is
  <=5; the shared signature-split route is bounded for cold reflection paths.
- The `paramMeta` name encoding rejects names containing `|` or `;` (impossible
  for C# identifiers; a hand-written .il edge fails LOUD at transpile).