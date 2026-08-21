# GAPS.md -- enumflags2 recorded findings (e2e batch C78, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 47-line
byte-exact). Active probes drive the boxed-Enum FLAGS surface on the WIRED
surface (all stringenum-proven shapes): ToString on boxed Enum references
(single member / zero / [Flags] decomposition "F1, F2" / unnamed numeric),
HasFlag (self / absent / combined / zero / none / full-combination components),
bit-operator combos ((Enum) cast | & ^ ~ with (int) round-trips), Enum.Parse
name/numeric/whitespace-trimmed round-trips, and GetName / GetNames /
GetUnderlyingType. Gaps below are RECORDED, NOT FIXED.

> NAMING: this case is `enumflags2` because a git-tracked PASSING case named
> `enumflags` already exists (e2e batch 13) and the batch rule forbids
> modifying existing files; the deep HasFlag/ToString surface (unwired when
> enumflags was written) lives here.

## D-EF2-1 -- Enum.Parse combined-name / overflow-exception divergence

- **Phenomenon**: net48 Enum.Parse accepts COMMA-SEPARATED member names
  ("F1, F4" -> combined value) and throws OverflowException for an
  out-of-range numeric; the runtime il2cpp_enum_parse_boxed (enumext.cpp:937)
  parses single member names / numerics only and reports EVERY failure as
  ArgumentException ("Requested value was not found"). The comma-name and
  huge-numeric probe rows were REMOVED (net48 semantics diverge from the
  runtime); single-name / numeric / trimmed-whitespace rows are byte-exact.
- **Suspected root file**: `src/runtime/src/enumext.cpp` il2cpp_enum_parse_boxed
  (no comma-decomposition grammar; no OverflowException/FormatException split).

## D-EF2-2 -- IsFlagsCapable rejects a non-power-of-two named member

- **Phenomenon**: the runtime ToString decomposition gate
  `IsFlagsCapable` (enumext.cpp) requires EVERY non-zero member to be an exact
  power of two. An enum with an explicit `All = 15` member (like the original
  enumflags case's `Access.All`) fails the gate, so a combined value that has
  NO exact member name (e.g. 3) prints its NUMERIC value instead of net48's
  "F1, F2" decomposition. This case's `Bits` enum keeps ONLY power-of-two
  members (None/F1/F2/F4/F8) so every combined value decomposes; enums with a
  named composite member remain a divergence.
- **Suspected root file**: `src/runtime/src/enumext.cpp` IsFlagsCapable.

## D-EF2-3 -- (Enum) cast of a boxed Parse result emits System_Enum_class

- **Phenomenon**: `Enum parsed = (Enum)Enum.Parse(typeof(Bits), "5")` -- the
  explicit (Enum) cast of the icall-returned box triggers an isinst against
  `System_Enum_class`, an undeclared descriptor (no runtime class exists for
  the System.Enum base). The probe was rewritten to compare the Parse result
  via `.Equals((Enum)Bits...)` (the stringenum-proven shape), which needs no
  System.Enum-class descriptor.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` (the boxed
  icall result cast path; the Enum-typed-receiver ABI uses il2cpp_enum_* by
  name string, not a base-class descriptor).
