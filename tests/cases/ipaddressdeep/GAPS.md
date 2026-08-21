# GAPS.md -- ipaddressdeep recorded findings (e2e batch C70, 2026-08-12)

Case status: net48 oracle 41-line byte-exact reference written; dual-compiler
PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## D-IPD-1 -- IPAddress.TryParse(string, out IPAddress) NOT wired (LOUD)

- MethodMap.Net.cs wires Parse(string) / ToString() only; TryParse (and
  GetAddressBytes / get_AddressFamily / Equals / GetHashCode) fall to the
  default NotSupportedException: "the ipaddr.h parse layer covers
  Parse(string)/ToString() only; ... the icall_ip_* object layer of
  net-domain.md section 4.3 is runtime work".
- The wired parse layer implements Parse semantics (throws FormatException /
  ArgumentNullException); the non-throwing 1/0 TryParse variant is a
  separate runtime function (ipaddr.h: "the non-throwing 1/0 form is the
  net48 TryParse variant (not wired; this function implements Parse)").
- NOT probed here. The case covers the wired Parse/ToString surface:
  round-trips, the net48 legacy 1-4-part grammar with canonical ToString
  pins, and the full malformed-form FormatException set.

## Verified byte-exact contract (this case pins, no gap)

- net48 legacy grammar round-trips: 4-part dotted quads, 3-part (16-bit
  tail -> 1.2.0.3), 2-part (24-bit tail -> 1.0.0.2), 1-part (32-bit value
  big-endian -> 0.0.1.0), hex "0x"/"0X" parts, leading-zero decimals.
- FormatException on: >4 parts, trailing/leading dots, empty segment,
  empty string, surrounding whitespace/tab/newline, non-numeric, signs,
  trailing garbage, out-of-range octets/values, dangling "0x".
- Parse(null) throws ArgumentNullException.
