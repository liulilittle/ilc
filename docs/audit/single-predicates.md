# System.Single predicates and Parse/TryParse

Status: GREEN (`probe-single`, byte-exact against the net48 oracle, sha
`1cfa29be2293c3c20ed23d5ac427fbab3fdde8ad26fd3c361184c8eaecb9a117`, 489B).

## Net48 measurements

| Behaviour | net48 |
|---|---|
| `IsNaN(NaN)` / `IsInfinity(+inf)` / `IsPositiveInfinity(+inf)` / `IsNegativeInfinity(-inf)` | true |
| `IsInfinity(NaN)` / `IsPositiveInfinity(NaN)` / `IsNegativeInfinity(NaN)` | false |
| `IsInfinity(1f)` and friends | false |
| `Parse("1.5")` (all four overloads) | 1.5 |
| `Parse("-0")` | 0 (the R format collapses -0) |
| `Parse("abc")` / `Parse(null)` / `Parse("1e999")` | FormatException / ArgumentNullException / OverflowException |
| `TryParse` failure | false with the out slot zeroed |
| `NaN == NaN` / `NaN < 1f` | false / false |

## Implementation shape

* `icall_single_is_nan` / `_is_positive_infinity` / `_is_negative_infinity` /
  `_is_infinity` are the 32-bit IEEE-754 bit tests (`sign 1 + exponent 8 +
  mantissa 23`).  The value arrives widened to double (the runtime's float
  carrier) and is narrowed BEFORE the bit test, so the check reads the float's
  own pattern -- testing the widened double would give the wrong exponent
  field.
* `icall_string_parse_single_styles` / `icall_try_parse_single_styles` reuse
  the double parse core and narrow the result, throwing OverflowException past
  `Single.MaxValue` (net48: NOT a silent Infinity).
* `icall_string_parse_single` / `icall_try_parse_single` bake in the default
  NumberStyles (Float|AllowThousands = 231) and the current culture.

## Two ABI traps the probe caught

1. **The BYREF out parameter of `Single.TryParse` decodes as `0x0C`, not the
   `0x0D` the Double domain uses.**  Matching only `0x0D` left the member
   unmapped.
2. **The out slot is a `double*`** (the float carrier), so the icall writes a
   double even though the managed slot is a float.

## The emitter cannot inject two arguments

`ArgOrder` slot `-1` emits `nullptr` and IGNORES the `"lit:"` cast, so a
`"lit:231"` cast only survives when the slot maps to a real IL argument.  A
one-argument `Parse(string)` therefore cannot be served by the three-parameter
styles icall; it gets its own one-parameter entry point.

## Mutation evidence

Flipping the positive-infinity bit pattern to the negative one turns the probe
RED at stdout record 55; restoring it returns the sha above.

## Still unmapped

`Single.GetTypeCode` needs the `TypeCode` enum surface.
