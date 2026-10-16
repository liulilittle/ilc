# GAPS.md — p-refl4-4tc (refl4 item 4)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, sha-equal).

## Fixed: Type.GetTypeCode(Type) enum-underlying folding + ConstructorInfo.get_Name

- **Phenomenon (REFL-EXPLORE4 B7/C1)**:
  - `Type.GetTypeCode(typeof(<user enum>))` folded to Object(1) instead of the
    enum's UNDERLYING TypeCode (Byte=6 Int16=7 Int32=9 Int64=11); storing the
    fold result in a `System.TypeCode` local was COMPILE-BROKEN (`System_
    TypeCode` mangled type — BCL enums have no generated body).
  - `ConstructorInfo.get_Name` was phantom: the ctor member VALUE is the
    disambiguated `"<FullName>::.ctor"` (GAP-5), so the generic get_Name
    identity/generated-name path could not print net48's ".ctor".
- **Fix**:
  - Transpiler (`StackFold.cs`): `TypeCodeOfTypeName(fullName, types)` now
    consults the user-enum TypeDef (`BaseTypeName=="System.Enum"`) and returns
    the `value__` instance field's underlying TypeCode (SByte=5 Byte=6 Int16=7
    UInt16=8 Int32=9 UInt32=10 Int64=11 UInt64=12 — the same surface
    MethodMap.Enum reads). Non-enum names keep the Object(1) fallback.
    `System.TypeCode` is added to `KnownBclEnumNames` so a BCL enum local
    declares as `int32_t` (the COMPILE-BROKEN fix; the fold result is used
    DIRECTLY as an int constant).
  - Runtime: `il2cpp_ctorinfo_get_name(memberValue)` returns the literal
    ".ctor" for the disambiguated ctor member value (the Name property decodes
    the ".ctor" key; the Invoke dependency on the full
    `"<FullName>::.ctor"` VALUE is untouched).
  - Transpiler (`MethodMap.Object.cs`): `MemberInfo::get_Name` arm for a
    `System.Reflection.ConstructorInfo` receiver routes to the ctor-name
    icall.
- **net48 semantics pinned by the oracle**: enum TypeCode = underlying
  (6/7/9/11); a CLASS still folds to Object(1); ctor Name == ".ctor" (0-arg
  and 1-arg); ctor Invoke still creates after the Name decode.
- **RED**: runtime exit 1, all 9 records missing.
- **GREEN**: 9/9 records byte-exact, firstDiff=-1, sha-equal.

## Known simplification (W7)

- A BCL enum (TypeRef — System.DayOfWeek etc.) has no TypeDef so its metadata
  underlying type is not decoded — keeps Object(1) unless explicitly folded
  (user enums only; the probes use user enums).