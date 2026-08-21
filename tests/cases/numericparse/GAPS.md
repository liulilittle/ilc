# GAPS.md — numericparse recorded findings (e2e batch C69, 2026-08-12)

Case status: net48 oracle 82-line byte-exact reference written; dual-compiler
PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## D-NP-1 — uint.TryParse / uint.Parse NOT wired

- **Phenomenon**: `uint.TryParse(string, out uint)` and `uint.Parse(string)`
  have NO MethodMap.ValueType case (System.UInt32 maps only ToString/Equals)
  and NO runtime icall (il2cpp_parse.h has int32/int64/double/bool/byte only)
  → falls through to the generated-name path (undefined extern at C++ link).
- **Causal chain**: the parse domain's wired surface is int.Parse (throwing,
  icall_string_parse_int), int/long/double/bool/byte TryParse. The unsigned
  parse family was never added to il2cpp_parse.h. The case therefore probes
  the uint BOUNDARY through the wired side: uint.ToString() on
  0/1/0x80000000/MaxValue, and int.TryParse on the uint-domain strings
  "2147483648".."4294967295" (all MUST fail int32).
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.ValueType.cs` +
  `src/runtime/include/internal/il2cpp_parse.h`.

## D-NP-2 — TryParse out-slot on FAILURE: net48 writes 0, runtime leaves untouched

- **Phenomenon**: `int keep = 12345; int.TryParse("not-a-number", out keep);`
  — net48 overwrites `keep` with **0** on failure; the runtime's
  icall_try_parse_* leaves the out slot UNTOUCHED (documented in
  il2cpp_parse.h:14-16 "the out parameter is LEFT UNTOUCHED"). The out-slot
  divergence only matters when the caller reads the out variable after a
  failed TryParse.
- **Probe**: `out_fail_keep` printed **False** on net48 (keep became 0, not
  12345); the assertion was REMOVED from the case (batch rule -- only the
  success-write contract `out_success_write` is asserted, which both sides
  agree on).
- **Disposition**: recorded, NOT fixed. A runtime change to write 0 on
  failure would match net48 exactly.

## D-NP-3 — Parse(null): net48 ArgumentNullException vs runtime FormatException

- Inherited from parseops D-PAR-4: `int.Parse(null)` throws
  ArgumentNullException on net48 but FormatException in the runtime's
  icall_string_parse_int (il2cpp_format.h). Null is therefore exercised
  through TryParse only (`t_null=True`, `l_null=True`, `bf_null=True` --
  all return false on both sides).

## N-NP-1 — int.Parse 各进制 / styles overloads: LOUD (inherited D-PAR-1/3)

- `Convert.ToInt32(string, fromBase)` IS wired (icall_convert_to_int32_string_base)
  and covered by parsecase2; the NumberStyles/IFormatProvider TryParse
  overloads (D-PAR-3) and long.Parse (D-PAR-2) remain unwired. long is probed
  through TryParse only (`l_*` rows).
