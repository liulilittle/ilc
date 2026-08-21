//------------------------------------------------------------------------------
// ipaddressdeep.cs -- System.Net.IPAddress DEEP probe (e2e batch C70).
// Extends netcase (first-generation: dotted-quad round-trips + basic
// FormatException cases) with the SPECIFIC parse/format-surface deep probes
// requested by the wiring audit:
//
//   * ROUND-TRIP SURFACE     -- Parse(s).ToString() == s over the standard
//     dotted-quad families (localhost / zero / broadcast / private /
//     public / max-octet). Every valid form round-trips byte-for-byte.
//   * LEGACY FORMS           -- the net48 legacy parser accepts 1-4 parts:
//     3-part "1.2.3" packs the tail as a 16-bit field, 2-part "1.2" as a
//     24-bit field, 1-part as the whole 32-bit big-endian value; hex
//     ("0x"/"0X" prefix) and leading-zero decimal parts are accepted.
//     Each canonical ToString is pinned.
//   * FORMAT-EXCEPTION DEEP  -- malformed forms (too many parts, empty /
//     leading / trailing dots, empty segments, signs, whitespace, trailing
//     garbage, out-of-range octets, dangling hex prefix) all throw
//     FormatException (caught -> bool).
//   * PARSE(NULL)            -- net48 Parse(null) throws
//     ArgumentNullException (caught -> bool).
//
// Every probe prints a BOOL (round-trip / exception-type assertion) -- never
// raw exception message text (net-domain.md e2e convention). net48 is the
// reference baseline (the runtime's ipaddr.cpp implements the net48 legacy
// dotted-quad grammar, probes verified 2026-08-09, so pinned canonical
// strings match byte-for-byte).
//
// DELIBERATELY NOT PROBED (documented runtime gap -- do NOT call here):
//   * IPAddress.TryParse(string, out IPAddress) -- LOUD (MethodMap.Net.cs):
//     the wired parse layer implements Parse semantics (throws); the
//     non-throwing 1/0 TryParse variant is runtime work (net-domain.md
//     section 4.3). Recorded D-IPD-1.
//   * GetAddressBytes / get_AddressFamily / Equals / GetHashCode -- LOUD
//     (MethodMap.Net.cs default: "Parse(string)/ToString() only"). Recorded
//     D-IPD-1.
//------------------------------------------------------------------------------
using System;
using System.Net;

namespace Cases { public static class IPAddressDeep {

    // Parse(s).ToString() == s : net48 round-trip contract for a valid form.
    static bool RoundTrips(string s) {
        try { return IPAddress.Parse(s).ToString() == s; }
        catch (Exception) { return false; }
    }

    // Parse(s) succeeds and its canonical ToString equals 'expected'.
    static bool Canonical(string s, string expected) {
        try { return IPAddress.Parse(s).ToString() == expected; }
        catch (Exception) { return false; }
    }

    // Parse(s) throws FormatException (the malformed-form contract).
    static bool IsFormatException(string s) {
        try { IPAddress.Parse(s); return false; }
        catch (FormatException) { return true; }
        catch (Exception) { return false; }
    }

    // Parse(null) throws ArgumentNullException (net48 contract).
    static bool IsArgumentNullException() {
        try { IPAddress.Parse(null); return false; }
        catch (ArgumentNullException) { return true; }
        catch (Exception) { return false; }
    }

    public static void Main()
    {
        // ===== S1: round-trip surface -- valid dotted-quad families =====
        Console.WriteLine("rt_loopback=" + RoundTrips("127.0.0.1"));
        Console.WriteLine("rt_zero=" + RoundTrips("0.0.0.0"));
        Console.WriteLine("rt_bcast=" + RoundTrips("255.255.255.255"));
        Console.WriteLine("rt_private=" + RoundTrips("192.168.1.1"));
        Console.WriteLine("rt_public=" + RoundTrips("8.8.8.8"));
        Console.WriteLine("rt_10=" + RoundTrips("10.0.0.255"));
        Console.WriteLine("rt_maxoct=" + RoundTrips("255.0.255.0"));
        Console.WriteLine("rt_low=" + RoundTrips("1.2.3.4"));
        Console.WriteLine("rt_172=" + RoundTrips("172.16.0.1"));

        // ===== S2: legacy forms -- canonical ToString pins =====
        Console.WriteLine("lg_4part=" + Canonical("1.2.3.4", "1.2.3.4"));
        Console.WriteLine("lg_3part=" + Canonical("1.2.3", "1.2.0.3"));       // 16-bit tail
        Console.WriteLine("lg_2part=" + Canonical("1.2", "1.0.0.2"));        // 24-bit tail
        Console.WriteLine("lg_1part=" + Canonical("256", "0.0.1.0"));        // 32-bit value
        Console.WriteLine("lg_1part1=" + Canonical("0", "0.0.0.0"));
        Console.WriteLine("lg_1partmax=" + Canonical("4294967295", "255.255.255.255"));
        Console.WriteLine("lg_hex4=" + Canonical("0x7f.0.0.1", "127.0.0.1"));
        Console.WriteLine("lg_hex1=" + Canonical("0x7F000001", "127.0.0.1")); // 32-bit hex value
        Console.WriteLine("lg_zero=" + Canonical("01.2.3.4", "1.2.3.4"));    // leading zero
        Console.WriteLine("lg_zero2=" + Canonical("000.000.000.000", "0.0.0.0"));
        Console.WriteLine("lg_hexupper=" + Canonical("0XFF.0.0.1", "255.0.0.1"));

        // ===== S3: format-exception deep -- malformed forms =====
        Console.WriteLine("fe_5part=" + IsFormatException("1.2.3.4.5"));
        Console.WriteLine("fe_6part=" + IsFormatException("1.2.3.4.5.6"));
        Console.WriteLine("fe_traildot=" + IsFormatException("1.2.3.4."));
        Console.WriteLine("fe_leadot=" + IsFormatException(".1.2.3.4"));
        Console.WriteLine("fe_emptymid=" + IsFormatException("1..2.3.4"));
        Console.WriteLine("fe_empty=" + IsFormatException(""));
        Console.WriteLine("fe_leadspace=" + IsFormatException(" 127.0.0.1"));
        Console.WriteLine("fe_trailspace=" + IsFormatException("127.0.0.1 "));
        Console.WriteLine("fe_tab=" + IsFormatException("127.0.0.1\t"));
        Console.WriteLine("fe_nl=" + IsFormatException("127.0.0.1\n"));
        Console.WriteLine("fe_nonnumeric=" + IsFormatException("abc.def.gh.ij"));
        Console.WriteLine("fe_neg=" + IsFormatException("1.2.3.-4"));
        Console.WriteLine("fe_plus=" + IsFormatException("+1.2.3.4"));
        Console.WriteLine("fe_trailgarbage=" + IsFormatException("1.2.3.4x"));
        Console.WriteLine("fe_octhi=" + IsFormatException("1.2.3.256"));
        Console.WriteLine("fe_firsthi=" + IsFormatException("256.1.1.1"));
        Console.WriteLine("fe_tailhi=" + IsFormatException("1.2.65536"));
        Console.WriteLine("fe_valuehi=" + IsFormatException("4294967296"));
        Console.WriteLine("fe_danglinghex=" + IsFormatException("0x"));
        Console.WriteLine("fe_hexlead=" + IsFormatException("0x.0.0.1"));

        // ===== S4: Parse(null) -> ArgumentNullException =====
        Console.WriteLine("an_null=" + IsArgumentNullException());
    }
}}
