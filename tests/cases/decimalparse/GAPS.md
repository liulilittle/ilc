# GAPS.md -- decimalparse recorded findings (e2e batch C72, 2026-08-12)

Case status: net48 oracle 81-line byte-exact reference written (expected.txt);
dual-compiler PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## Verified byte-exact contract (this case pins, no gap)

- throwing Parse FULL format matrix: sign forms (+/-/ws+sign), leading/trailing
  ASCII whitespace (space/tab/CR/LF), leading-zero collapse, decimal point in
  every position (".5" -> 0.5, "5." -> 5), thousands separators BETWEEN digits
  ("1,234" / "12,34" / "1,2,3" / "1,000.5" / "1,234,567,890.12345"), scale-28
  value, 29-significant-digit value, exact max/min decimal literals.
- ERROR matrix: overflow past max (both signs), 30-significant-digit overflow,
  FormatException on junk/empty/whitespace-only/double-point/leading-comma/
  exponent ("1e3"/"1E3")/sign-space ("+ 42"); Parse(null) -> ArgumentNullException.
- Parse -> ToString SCALE-PRESERVATION round-trip: "1.50" -> "1.50",
  "0.100" -> "0.100", "0.00" -> "0.00", "000.500" -> "0.500", grouped
  "1,234.50" -> "1234.50".
- TryParse CONSISTENCY: same format matrix on the bool path incl. null ->
  false, overflow -> false, and the SUCCESS value (scale preserved: "1.50").
- Rounding/Floor/Ceiling/Truncate applied to PARSED values: Round 1/2/3-arg,
  ToEven vs AwayFromZero ties (1.005/2.675/4.5/2.25/10.5/12345.6789), negative
  ties, Floor/Ceiling/Truncate on parsed negatives; ToInt32/ToInt64 truncate +
  range on parsed values; parse(ToString(x)) identity round-trip.

## Recorded defects (NOT fixed -- batch rule)

- **D-DECP-1** negative-zero sign preservation: the runtime ParseCore sets the
  sign bit for "-0" (and Ceiling/Truncate of a negative that lands on zero keep
  it), so decimal.Parse("-0").ToString() -> "-0" while net48 normalizes to "0".
  net48 oracle (probe-verified): Parse("-0") -> "0", Ceiling(-0.1) -> "0",
  Truncate(-0.9) -> "0". The case asserts `Parse("-0") == 0m` and
  `Ceiling(...) == 0m` (equality -- true on both sides, since icall_decimal_equals
  treats -0 == 0) instead of the ToString form.
- **D-DECP-2** net48 ROUNDS scale>28 inputs (e.g. "0.00000000000000000000000000001"
  -> 0.0000000000000000000000000000, 29 fractional digits round to 28) while the
  runtime throws OverflowException (decimal_format.cpp ParseCore: scale > 28 ->
  Overflow). Probe-verified net48. NOT probed in this case -- a scale-29 probe
  would byte-diff.
- **D-DECP-3** trailing sign / trailing-or-double comma: net48 NumberStyles.Number
  accepts "42-" -> -42, "42+" -> 42, "1," -> 1, "1,,2" -> 12 (probe-verified);
  the runtime ParseCore only accepts a LEADING sign and requires a digit after
  every comma -> FormatException. NOT probed -- byte-diff would fire.
- **D-DECP-4** TryParse failure out-slot: net48 zeroes the out parameter on
  failure, the runtime leaves it untouched (verify-then-mutate; documented
  deviation, decimal_format.h TRYPARSE CONTRACT). The case asserts only the
  success flag and the SUCCESS value.
