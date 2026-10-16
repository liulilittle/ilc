# GAPS.md — p-refl-fvtype (GAP-2 / P1)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: FieldInfo.GetValue/SetValue narrow slot set + static null-target

- **Phenomenon**: `il2cpp_typeinfo_field_get_value/set_value` dispatched only
  Int32/Int64/ref slots; a `bool`/`short`/`uint`/`byte` field's SetValue
  rejected the boxed Boolean/Int16/UInt32/Byte value net48 always passes
  (`Specified cast is not valid: object is not a boxed Int32` / silent null),
  and a STATIC field's `GetValue(null)`/`SetValue(null)` hit the null-target
  NRE.
- **Root cause (two layers)**:
  1. the transpiler field TABLE collapsed every instance primitive to the
     `"System.Int32"` typeName (the reflection field-dispatcher's slot key) —
     MetadataLoader now emits the real System name (Boolean/Int16/UInt32/Byte/
     Int64/…);
  2. the runtime dispatched by typeName over a boolean-family range that
     must EXCLUDE the reference slot (3) — kind 3 (isRef) must be handled after
     the int32-family range; a narrow-family box uses its own carrier.
- **Fix**:
  - runtime: `FieldBoxType` + per-kind box/unbox (Boolean/Char/SByte/Byte/
    Int16/UInt16/Int32/UInt32 int32-family, Long/ULong int64, Double/Single
    double, reference slot), unsupported widths LOUD;
  - static fields (flags bit1) resolve the declaring type by REGISTRY SCAN
    (deepest declaring type) when the target is null — net48 static-field
    doctrine;
  - MetadataLoader: instance primitive fields carry their System name.
- **RED**: boxed Boolean SetValue threw; static null-target NRE.
- **GREEN**: all 10 records byte-exact.