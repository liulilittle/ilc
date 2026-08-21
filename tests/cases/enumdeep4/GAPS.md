# GAPS.md -- enumdeep4 recorded findings (e2e batch C83, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 40-line
byte-exact). Active probes drive the Parse / ToString / underlying-value chain
on the WIRED enum surface after the register wiring landed: boxed-enum
ToString (named member / zero / unnamed numeric / negative named / [Flags]
decomposition), Enum.Parse name/numeric/trim/negative round-trips,
(int)-cast arithmetic and comparisons, GetUnderlyingType==int, GetName /
GetNames (declaration order). Gaps below are RECORDED, NOT FIXED.

## D-EN4-1 -- Enum.GetNames returns declaration order; net48 sorts by value

- **Phenomenon**: for `Level { Low = -2, Zero = 0, Mid = 3, High = 9 }`,
  net48 Enum.GetNames returns the members SORTED BY VALUE -- probe-verified
  order `Zero, Mid, High, Low` (GetValues likewise). The runtime's
  il2cpp_enum_get_names_by_type_name walks the REGISTERED table
  (il2cpp_enum_register, declaration order -- CodeGenerator.cs emits
  s_{T}_names from the TypeDef's StaticFields declaration order), so it
  returns `Low, Zero, Mid, High` -- a byte divergence for any enum whose
  value order differs from declaration order. The gns0..gns3 order probes
  were REMOVED from the case (net48 vs runtime disagree); the monotonic
  Access enum (None/Read/Write/Execute) keeps byte-exact order probes
  (gna_len/gna0/gna3).
- **Suspected root file**: `src/runtime/src/enumext.cpp`
  (il2cpp_enum_get_names_by_type_name -- walks the registry table verbatim;
  net48 sorts by value). The transpiler-side register table
  (MethodMap.Enum.cs TryGetUserEnumRegisterInfo) stores declaration order.

## Notes

- `ug_not_long=False` is a genuine net48 value (underlying type is int, not
  long), not a divergence.
- The `(Enum)(Level)5` / `(Enum)(Level)(-1)` casts are the boxed-enum
  reference shape (il2cpp_box_enum); the enum-VALUE ldloca+constrained
  shape stays D-ENU-2 (documented in MethodMap.Enum.cs).
- Non-int enum backings (D-ENM-4: "field type byte 0x05 not supported in
  M2") and the 3-arg Parse / TryParse (ref/out) stay out of scope, as in
  enumdeep3.
