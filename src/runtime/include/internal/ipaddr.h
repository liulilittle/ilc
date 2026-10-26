#pragma once
//------------------------------------------------------------------------------
// ipaddr.h -- System.Net.IPAddress / System.Net.Dns runtime support
// (IPv4-only simplification of net-domain.md T2, design doc section 1.4-1).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline -- with documented simplifications, because this is a BASIC
// DOMAIN (net-domain.md section 1.4, entries 1 and 13):
//
//   * IPv4 only. IPAddress.Parse accepts dotted-decimal quad only; IPv6
//     literals (and every other form net48 accepts) are rejected.
//   * Parse grammar is net48 LEGACY leniency (probes verified 2026-08-09,
//     ipaddr.cpp ParseDottedQuad): 1-4 '.'-separated parts, each part a
//     decimal or "0x"/"0X"-prefixed hexadecimal number, leading zeros are
//     plain decimal ("01" -> 1). The LAST part occupies the trailing bytes
//     as a wider field (3-part tail is 16-bit: "1.2.3" -> 1.2.0.3; 2-part
//     tail is 24-bit: "1.2" -> 1.0.0.2; 1-part is the 32-bit value
//     big-endian: "256" -> 0.0.1.0). Rejected: >4 parts, empty string or
//     segment, signs, whitespace, trailing garbage, out-of-range values.
//   * Parse (NOT TryParse) semantics: null text throws
//     ArgumentNullException, malformed text throws FormatException -- the
//     exceptions ARE the Parse contract (netcase IsFormatException probes,
//     net-domain.md section 2.5). The non-throwing 1/0 form is the net48
//     TryParse variant (not wired; this function implements Parse).
//   * Dns.GetHostAddresses resolves IPv4 (AF_INET) only and returns every
//     IPv4 address for the name; net48 on a dual-stack host returns IPv6
//     first. The numeric-literal form ("127.0.0.1") is also accepted.
//
// Layer note (net-domain.md section 4.3): il2cpp_ipaddress_parse IS the
// wired Parse entry (the transpiler maps System.Net.IPAddress.Parse directly
// onto it -- MethodMap.Net.cs). There is no separate icall_ip_parse layer;
// the FormatException/ArgumentNullException throws below come straight from
// this function (generated code wraps the call in try/catch, netcase
// gen_v6). il2cpp_ipaddress_to_string / get_address_bytes / dns are plain
// C++ helpers (never throw; defensive nullptr handling).
//
// Managed values: Il2CppString* inputs are read as ASCII text (the FIRST
// non-ASCII unit makes the whole input invalid -- IDN is out of scope);
// Il2CppArray* results are allocated through il2cpp_array_new (byte[]
// arrays are primitive arrays: elemSize 1, isRefElement false; the DNS
// result is an object array of byte[4] elements: isRefElement true).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_array.h" // Il2CppArray
#include "il2cpp_object.h"   // Il2CppObject (the object-typed argument)
#include "il2cpp_metadata.h" // Il2CppClass (the box identity check)

namespace il2cpp {

struct Il2CppString; // forward (il2cpp_string.h defines the full layout)

namespace runtime {

// IPAddress.GetHashCode (net48 IPv4): unchecked((int)m_Address) -- the
// little-endian fold of the four address bytes (the host-order slot IS the
// m_Address byte image). A null buffer yields 0 (defensive).
int32_t il2cpp_ipaddress_get_hash_code(const uint8_t bytes4[4]);

// IPAddress.Equals(object) (net48): null -> false, a non-IPAddress object ->
// false, a boxed IPAddress -> the four address bytes (IPv4). The runtime has
// no IPAddress box class (metadata.cpp); a non-null object therefore never
// matches today -- the reachable shapes (null / a foreign object) return
// false. The klass-name check keeps the helper correct if a box lands later.
bool il2cpp_ipaddress_equals_object(const uint8_t a[4], Il2CppObject* obj);

// Parse a dotted-decimal IPv4 literal with the net48 legacy grammar above
// (1-4 parts, hex, leading zeros, wider trailing field). Returns 1 and
// writes the four address bytes into out4[0..3] (host order) on success.
// NET48 PARSE SEMANTICS on failure: null 'text' throws ArgumentNullException;
// malformed text throws FormatException (both C++ DotNetException carriers --
// never returns a failure code). A null 'out4' output buffer is a C-API
// caller bug and returns 0 defensively (no managed analogue).
int32_t il2cpp_ipaddress_parse(Il2CppString* text, uint8_t out4[4]);

// Rebuild the dotted-quad string "d.d.d.d" from the four address bytes
// (host order). A null 'bytes4' yields the empty string "" (defensive,
// documented -- callers always pass the buffer a parse filled).
Il2CppString* il2cpp_ipaddress_to_string(const uint8_t bytes4[4]);

// The four address bytes as a managed byte[] of length 4 (the net48
// GetAddressBytes shape). A null 'bytes4' yields nullptr (defensive).
Il2CppArray* il2cpp_ipaddress_get_address_bytes(const uint8_t bytes4[4]);

// Resolve 'host' (a name or a dotted-quad literal) to all its IPv4
// addresses, returned as an object array (isRefElement true, elemSize
// sizeof(void*)) whose elements are byte[4] managed arrays. Returns nullptr
// when 'host' is null, empty, non-ASCII, fails to resolve, or yields no
// IPv4 address (the caller maps this to SocketException, net-domain.md
// section 2.5). Host names are ASCII-only (IDN out of scope).
Il2CppArray* il2cpp_dns_get_host_addresses(Il2CppString* host);

} // namespace runtime

} // namespace il2cpp
