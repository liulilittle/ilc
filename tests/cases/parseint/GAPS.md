# GAPS.md -- parseint recorded findings (e2e batch C78, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 116-line
byte-exact). Active probes: int.Parse full boundary matrix (signed zero / +-
forms / ASCII whitespace variants space-tab-LF-CR-FF-VT / leading zeros / int32
MinValue+MaxValue exact / one-past both ends / huge / empty / whitespace-only /
junk / float-shaped / doubled sign / trailing sign / hex-shaped / null),
int.TryParse same matrix + Parse/TryParse consistency + success out-slot write,
Convert.ToInt32(string) vs int.Parse agreement, Convert.ToInt32(string,
fromBase) for base 2/8/10/16 (digit validity, 0x/0X prefix, +/- sign handling,
range wrap vs overflow, invalid base / empty / null / bad digit / >uint32
overflows), Convert.ToString(int, toBase) round-trip, and an aggregate
Parse/TryParse consistency counter. Gaps below are RECORDED, NOT FIXED.

## D-PAR-5 -- culture-aware Parse/TryParse overloads NOT wired

- **Phenomenon**: `int.Parse(string, NumberStyles)` /
  `int.Parse(string, NumberStyles, IFormatProvider)` and the int.TryParse
  3/4-arg overloads are NOT in TryMapValueType (MethodMap.ValueType.cs) -- the
  runtime parse surface is the (string) / (string, out int) shape only. The
  culture-aware overloads fall to the generated-name path -> undefined C++
  symbol -> build fail. The "文化" (culture) dimension of the case is therefore
  probed only through the culture-neutral surface; the overload set is the
  acceptance gap.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.ValueType.cs` (only the
  1-arg Parse and 2-arg (string, out) TryParse arms exist).

## D-NP-2 -- TryParse failure-path out-slot divergence (pre-existing)

- **Phenomenon**: on failure net48 overwrites the out slot with 0 while the
  runtime leaves it untouched (icall_try_parse_int32 only writes on success).
  The case asserts only the SUCCESS-write contract (out_success_write) and the
  success/failure success-flag semantics, never the failure-slot value.
- **Suspected root file**: `src/runtime/src/parse.cpp` icall_try_parse_int32
  (pre-existing, recorded in the batch-69 numericparse GAPS).
