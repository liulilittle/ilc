using System;
using System.Net;

namespace Cases { public static class NetCase {
    // net48 reference oracle for the IP layer (ipaddr.cpp: Parse/ToString).
    // NO sockets / NO external network -- dotted-quad parse/format only, so
    // the reference run is deterministic and sandbox-safe. Bool-only output
    // per net-domain.md e2e convention (assert caught flag, never exception
    // message text). Transpiler wiring is PENDING (no MethodMap.NetIP.cs);
    // this case is the pinned contract for the icall layer to satisfy.
    static bool RoundTrips(string s) {
        try { return IPAddress.Parse(s).ToString() == s; }
        catch (FormatException) { return false; }
    }
    static bool IsFormatException(string s) {
        try { IPAddress.Parse(s); return false; }
        catch (FormatException) { return true; }
    }
    public static void Main() {
        // --- valid dotted quads: Parse -> ToString round-trip ---
        Console.WriteLine(RoundTrips("127.0.0.1"));       // True
        Console.WriteLine(RoundTrips("0.0.0.0"));         // True
        Console.WriteLine(RoundTrips("255.255.255.255")); // True
        Console.WriteLine(RoundTrips("1.2.3.4"));         // True
        Console.WriteLine(RoundTrips("192.168.1.1"));     // True
        // --- malformed input: net48 throws FormatException (caught) ---
        Console.WriteLine(IsFormatException("127.0.0.256"));  // octet > 255
        Console.WriteLine(IsFormatException("999.0.0.1"));    // octet > 255
        Console.WriteLine(IsFormatException("1.2.3"));        // three parts
        Console.WriteLine(IsFormatException("1.2.3.4.5"));    // five parts
        Console.WriteLine(IsFormatException("1.2.3.4."));     // trailing dot
        Console.WriteLine(IsFormatException(""));             // empty string
        Console.WriteLine(IsFormatException(" 127.0.0.1"));   // leading space
        Console.WriteLine(IsFormatException("127.0.0.1 "));   // trailing space
        Console.WriteLine(IsFormatException("abc.def.gh.ij"));// non-numeric
        // net48 legacy parser leniency probes (see ipaddr.cpp inet_pton-strict
        // grammar -- expected divergence surface once wired):
        Console.WriteLine(IsFormatException("01.2.3.4"));     // leading zero
        Console.WriteLine(IsFormatException("0x7f.0.0.1"));   // hex form
    }
}}
