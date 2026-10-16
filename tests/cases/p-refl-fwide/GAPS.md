# GAPS.md — p-refl-fwide (DEFECT-F1 / audit 2A)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: FieldBoxType kinds 10-13 silent int32-slot overrun

- **Phenomenon**: the a16afce4 MetadataLoader widening made the reflection
  field table carry the REAL System typeName for instance long/double/float
  fields (`"System.Int64"` etc.), but the transpiler stores every instance
  value field as `int32_t` (TypeEmitter layout: "int32_t field; // value
  fields: int32_t at slot n*pointerSize"). `FieldInfo.GetValue` dispatched on
  the typeName and read 8 BYTES from the 4-byte slot (overread into the
  neighbor field); `SetValue` wrote 8 bytes over a 4-byte slot (neighbor-field
  overwrite). Static Single/Double were equally unsafe: StaticFieldSemantics
  has no Single/Double arm, so their static storage is int32_t too.
- **Fix**: `WideSlotUnrepresentable(field, kind)` — kinds 10-13 (Int64/UInt64/
  Single/Double) LOUD-reject (`il2cpp_throw_not_supported_exception`) unless
  the field is STATIC Int64/UInt64 (flags bit1 static), the ONLY shape with a
  real 8-byte carrier in this AOT model. Static Int64/UInt64 keep the working
  8B path (probe-verified round-trip below).
- **RED**: instance `fi64` GetValue/SetValue silently read/wrote an int32 slot
  as int64 (the R2 C++ fixture asserted the old 8B instance round-trip).
- **GREEN**: 
  - C++ runtime test_typeinfo R2: instance Int64 Get/Set LOUD-rejects
    (NotSupportedException); static Int64 GetValue(null)/SetValue(null) keeps
    the 8-byte path; static Single/Double Get/Set LOUD-reject.
  - this probe: 9/9 records byte-exact (static Int64/UInt64 8B round-trip
    through the working staticAddr path, static int32, instance int32).

## Scope boundary

- The LOUD-instance assertion itself cannot be a byte-exact net48 oracle
  record (net48 *succeeds* on instance long GetValue), so it is pinned in the
  C++ runtime test (the F1 R2 fixtures), not here.