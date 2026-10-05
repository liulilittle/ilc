# GAPS.md -- stringenum recorded findings (e2e batch C61, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 47-line
byte-exact, all True). Active probes cover the WIRED StringReader / boxed-enum
surface.

## D-STREN-1 -- enum-VALUE ToString (ldloca+constrained) is unreachable end-to-end

- **Phenomenon**: `DayOfWeek.Sunday.ToString()` (an enum VALUE receiver, not an
  Enum-typed reference) compiles to `ldloca` + `constrained.` + `callvirt`
  System.Object::ToString. The transpiler emits a local declaration typed
  `System_DayOfWeek loc21;` -- the BCL value-type enum has NO class descriptor
  in this runtime -- which fails to compile (`unknown type name
  'System_DayOfWeek'`, D-ENU-2 family). This aborts the whole cmake build.
- **Impact**: the DayOfWeek name probes in this case go through the DateTime
  boxed path (`new DateTime(...).DayOfWeek` compared to `DayOfWeek.Monday`),
  which routes through the boxed-enum WriteLine arm (timecase L8 precedent).
  The enum-VALUE `.ToString()` shape stays commented-out (D-ENU-2).
- **Fix path (future)**: the ldloca+constrained callvirt receiver needs a
  name-bearing enum descriptor (the REGISTER WIRING REPORT e.2 item in
  MethodMap.Enum.cs) OR a value-shape ToString fold; see enumdeep/enumdeep3
  D-ENM-3/D-ENU-2.

## D-STREN-2 -- Enum.GetName / Parse / HasFlag / ToString(format) LOUD (recorded, unprobed)

- **Phenomenon**: `Enum.GetName/Format/IsDefined/GetNames/GetValues/ToObject/
  HasFlag/CompareTo` and `Enum.Parse/TryParse` throw NotSupportedException in
  MethodMap.Enum.cs (lines 212-256: TryParse-shaped icalls need ref/out; no
  array-returning enum primitives; no flags semantics primitive). Probing them
  would abort the transpile, so this case covers only the WIRED boxed ToString
  / GetUnderlyingType / Equals surface.
- **Fix path (future)**: per the individual LOUD rejection messages.
