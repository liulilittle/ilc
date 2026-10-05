# GAPS.md -- parsefloat recorded findings (e2e batch C84, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 127-line byte-exact).

## D-PF-1 -- float formatting: net48 G7 vs ILC G15 double text

- **Phenomenon**: `Console.WriteLine(0.1f)` / `float.ToString()` on a
  non-exactly-representable float prints "0.1" on net48 (float G7, 7
  significant digits) but "0.100000001490116" on ILC (the float lives in a
  double slot and routes to the G15 double formatter).
- **Disposition**: the float dimension tests ONLY exactly-representable
  floats (0.5f, -2.25f, 0.125f, ...) whose G7 and G15 texts agree.
- **Suspect**: fixing requires a float-aware formatter (G7) for Single on
  the ILC side.

## D-PF-2 -- "1,000" group separators: net48 accepts, ILC rejects

- **Phenomenon**: net48 `double.Parse(string)` defaults to
  `NumberStyles.Float | AllowThousands`, so "1,000" parses to 1000. The
  ILC double grammar has no group separators ("1,000" -> FormatException) --
  a documented runtime deviation ("no group separators in double text").
- **Disposition**: the comma-grouped row was removed from the matrix.

## D-PF-3 -- System.Single.Parse / Single.TryParse NOT wired

- `float.Parse(string)` / `float.TryParse(string, out float)` fall to the
  generated-name path (no MethodMap.ValueType arm for Single Parse/TryParse;
  the runtime has only the double parse icalls). The case uses the double
  surface, which is the ILC single-equivalent (float values live in double
  slots).

## D-PF-4 -- 3/4-arg Parse/TryParse overloads NOT wired

- `double.Parse(string, NumberStyles)` / `(string, NumberStyles,
  IFormatProvider)` and the TryParse 3/4-arg forms are not wired
  (D-PAR-5 style). Not probed.

## D-PF-5 -- double.Parse(null): ArgumentNull vs FormatException

- net48 `double.Parse(null)` throws ArgumentNullException; the ILC
  icall_string_parse_double(null) throws FormatException ("String reference
  not set..."). Not probed (TryParse(null) -> false is the only null row,
  no-throw on both sides).
