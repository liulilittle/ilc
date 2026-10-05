# GAPS.md — typeget recorded findings (e2e batch C81, 2026-08-13; D-TYPG-1 fixed 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle byte-exact,
45 lines). Active probes cover Type ACQUISITION depth: typeof folding to
short .Name names across the numeric/boolean/char/string lattice + user
class/struct/enum/nested types, GetType().Name on boxed primitives/class/
enum instances, ARRAY GetType().Name (int[] "Int32[]" + string[] "String[]"
+ object[] "Object[]", the D-TYPG-1 closure), typeof identity (== /
ReferenceEquals), GetHashCode consistency, the static GetTypeCode fold, and
GetType()-vs-typeof identity. The remaining gaps below are RECORDED, NOT
FIXED.

## D-TYPG-1 — FIXED: non-int32 array GetType().Name reports the OBJECT array name

- **Phenomenon**: `new string[2].GetType().Name` rendered "Object[]" on the
  runtime but "String[]" on net48 (byte diff). Root cause: the runtime had
  ONLY two array-class descriptors — int32 `"[System.Int32"` and object
  `"[System.Object"` (array.cpp) — so any reference-element array (string[]
  / user class[] / object[]) reported the OBJECT array's name.
- **Fix (D-TYPG-1)**: the transpiler now passes the ELEMENT class at newarr
  (CodeGenerator.cs ArrayElementClassCpp); array.cpp synthesizes a per-element
  array descriptor named `"[<element full name>"` (string[] -> "[System.String"
  -> GetType().Name "String[]"; net48 parity). System.Object elements reuse
  the shared object-array descriptor. Verified: typeget `arr_str=True` /
  `arr_obj=True` byte-exact vs net48 on mingw + msvc + wsl.
- **Suspected root file**: `src/runtime/src/array.cpp` (element-aware array
  class pool) / `src/il2cppc/Emit/CodeGenerator.cs` (newarr element class).

## D-TYPG-2 — Type.GetTypeCode(enum) folds to Object, net48 reports the underlying type

- **Phenomenon**: `Type.GetTypeCode(typeof(Shade))` — net48 reports the
  enum's UNDERLYING TypeCode (Int32=9, since an enum's TypeCode is that of
  its underlying type); the StackFold TypeCodeOfTypeName fold has no enum
  case and falls back to Object=1 (byte diff).
- **Confirmed**: the probe was removed (recorded, not probed); the static
  GetTypeCode probes stay on int/bool/char/double/string + a user class
  (Object), which agree on both runtimes.
- **Suspected root file**: `src/il2cppc/Ir/StackFold.cs` TypeCodeOfTypeName
  (no enum-underlying-type mapping).

## D-TYPG-3 — typeof(array) ldtoken (TypeSpec SZARRAY) has no fold

- **Phenomenon**: `typeof(int[]).Name` aborts the transpile with `ldtoken
  token kind TypeSpecification (0x1B000001) ... not supported in M3` — the
  array type's ldtoken is a TypeSpecification (SZARRAY) which the Ldtoken
  case does not classify (only TypeDef/TypeRef/Field/Method). This is the
  pre-existing D-TYPEX-4 record; the runtime GetType().Name path is the only
  wired array acquisition.
- **Confirmed**: typeof(array) probes removed; the case probes
  `new int[3].GetType().Name` (wired).
- **Suspected root file**: `src/il2cppc/Ir/StackFold.cs` Ldtoken case.

## D-TYPG-4 — boxed user STRUCT GetType() is unwired (audit H3)

- **Phenomenon**: `new Pt().GetType()` (a user struct) aborts the transpile
  with `box of Cases.Pt not supported -- no runtime box helper (audit H3 +
  M5)`. Boxed primitives and enums box; user structs have no runtime box
  helper.
- **Confirmed**: the probe was removed; GetType() stays on boxed primitives,
  a string, a user class and an enum.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` box emission
  (audit H3).
