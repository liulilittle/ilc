//------------------------------------------------------------------------------
// guidops2.cs -- Guid domain OPS probe 2 (e2e batch C65): NewGuid value ops
// + exact CompareTo result values (-1/0/1) + field tie-break chains + byte-
// order semantics -- byte-exact vs the net48 oracle.
//
// Wired surface (MethodMap.Guid.cs + guid.h), all probes stay within it:
//   Guid.NewGuid()          -> il2cpp_guid_new_guid  (RFC 4122 v4 stamp)
//   Guid.Parse(string)      -> il2cpp_guid_parse_strict (throws on invalid)
//   Guid.TryParse(...)      -> il2cpp_guid_parse     (bool)
//   .ToString()             -> il2cpp_guid_to_string ("D", lowercase)
//   == / != / Equals(Guid)  -> il2cpp_guid_equals / !equals
//   CompareTo(Guid)         -> il2cpp_guid_compare   (field-wise unsigned)
//
// net48-shape discipline (guidcase / guiddeep2 precedent):
//   * NewGuid() is RANDOM on both sides -- the probes assert only the
//     properties net48 GUARANTEES (D-shape, hex digits, distinctness,
//     self-equality, round-trips). The RFC 4122 version/variant nibbles are
//     a runtime-specific stamp net48 does NOT carry, so they are never
//     asserted against the oracle.
//   * CompareTo probes pin the EXACT result (-1 / 0 / 1) -- net48 returns
//     these exact values, not just a sign (guiddeep2 only checked >0 / <0).
//   * No ToString(format), no Equals(object)/CompareTo(object), no
//     .ctor(byte[]) (MethodMap.Guid.cs LOUD rejects all three).
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class GuidOps2 {

    public static void Main()
    {
        // ==================== S1: NewGuid value ops ====================
        Guid n1 = Guid.NewGuid();
        Guid n2 = Guid.NewGuid();
        string s1 = n1.ToString();

        // D-shape: 36 chars, dashes at 8/13/18/23, hex elsewhere.
        Console.WriteLine("ng_len=" + (s1.Length == 36));                          // True
        Console.WriteLine("ng_dash=" + (s1[8] == '-' && s1[13] == '-' && s1[18] == '-' && s1[23] == '-')); // True
        bool hexOk = true;
        for (int i = 0; i < s1.Length; i++)
        {
            if (i == 8 || i == 13 || i == 18 || i == 23) continue;
            bool h = (s1[i] >= '0' && s1[i] <= '9') || (s1[i] >= 'a' && s1[i] <= 'f') || (s1[i] >= 'A' && s1[i] <= 'F');
            if (!h) hexOk = false;
        }
        Console.WriteLine("ng_hex=" + hexOk);                                      // True
        // Two NewGuid() calls virtually never collide.
        Console.WriteLine("ng_distinct=" + (n1 != n2));                            // True
        // Self value ops are deterministic regardless of the random value.
        Console.WriteLine("ng_eqself=" + (n1 == n1));                              // True
        Console.WriteLine("ng_ne_self=" + (n1 != n1));                             // False
        Console.WriteLine("ng_equalsequals=" + n1.Equals(n1));                     // True
        Console.WriteLine("ng_cmpself=" + (n1.CompareTo(n1) == 0));                // True
        // D-form round trip: Parse(ToString()) preserves the value.
        Guid r1 = Guid.Parse(s1);
        Console.WriteLine("ng_rt_d=" + (r1 == n1));                                // True
        Console.WriteLine("ng_rt_dstr=" + (r1.ToString() == s1));                  // True
        // N-form (32 chars, no dashes) round trip through TryParse.
        string s1n = s1.Replace("-", "");
        Console.WriteLine("ng_nlen=" + (s1n.Length == 32));                        // True
        Guid r2;
        bool okN = Guid.TryParse(s1n, out r2);
        Console.WriteLine("ng_rt_n=" + (okN && r2 == n1));                         // True
        Console.WriteLine("ng_cmp_rt=" + (r2.CompareTo(n1) == 0));                 // True

        // ==================== S2: exact CompareTo values (-1/0/1) ====================
        Guid a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Console.WriteLine("cmp_neg1=" + (a.CompareTo(b) == -1));                   // True
        Console.WriteLine("cmp_pos1=" + (b.CompareTo(a) == 1));                    // True
        Console.WriteLine("cmp_zero=" + (a.CompareTo(a) == 0));                    // True
        // Different fields still yield exactly 1 (not a magnitude).
        Guid c = Guid.Parse("00000001-0000-0000-0000-000000000000");
        Console.WriteLine("cmp_cross=" + (c.CompareTo(b) == 1));                   // True

        // ==================== S3: field tie-break chains ====================
        // Data1 equal -> Data2 decides (unsigned).
        Guid t2a = Guid.Parse("00000000-0001-0000-0000-000000000000");
        Guid t2b = Guid.Parse("00000000-8000-0000-0000-000000000000");
        Console.WriteLine("tie_d2=" + (t2a.CompareTo(t2b) < 0));                   // True (0x0001 < 0x8000)
        // Data1 + Data2 equal -> Data3 decides (unsigned).
        Guid t3a = Guid.Parse("00000000-0000-0001-0000-000000000000");
        Guid t3b = Guid.Parse("00000000-0000-8000-0000-000000000000");
        Console.WriteLine("tie_d3=" + (t3a.CompareTo(t3b) < 0));                   // True
        // Data1-3 equal -> Data4 byte 9 (second byte of group 4) decides.
        Guid t4a = Guid.Parse("00000000-0000-0000-0000-000000000000");
        Guid t4b = Guid.Parse("00000000-0000-0000-0001-000000000000");             // byte 9 = 0x01
        Console.WriteLine("tie_d4=" + (t4a.CompareTo(t4b) < 0));                   // True
        // Adjacent Data4 bytes: byte 10 precedes byte 11 in compare order.
        Guid t4c = Guid.Parse("00000000-0000-0000-0000-000100000000");             // byte 11 = 0x01
        Guid t4d = Guid.Parse("00000000-0000-0000-0000-010000000000");             // byte 10 = 0x01
        Console.WriteLine("tie_d4mid=" + (t4d.CompareTo(t4c) > 0));                // True (byte 10 first)
        Console.WriteLine("tie_d4mid_ne=" + (t4d != t4c));                         // True
        // Field position dominates value: Data2 = 1 beats Data3 = 0xffff.
        Guid f2 = Guid.Parse("00000000-0001-0000-0000-000000000000");
        Guid f3 = Guid.Parse("00000000-0000-ffff-0000-000000000000");
        Console.WriteLine("field_d2gt_d3=" + (f2.CompareTo(f3) > 0));              // True

        // ==================== S4: byte order (little-endian fields) ====================
        // CompareTo sees Data1 as the D-form group's literal hex VALUE (the
        // little-endian byte storage is parse-internal; ReadLE32U reconstructs
        // the string-order number). So "01020304" (0x01020304) sorts BELOW
        // "04030201" (0x04030201) -- the FIRST group hex order == compare order.
        Guid bo1 = Guid.Parse("01020304-0000-0000-0000-000000000000");
        Guid bo2 = Guid.Parse("04030201-0000-0000-0000-000000000000");
        Console.WriteLine("bo_le=" + (bo1.CompareTo(bo2) > 0));                    // False (0x01020304 < 0x04030201)
        Console.WriteLine("bo_ne=" + (bo1 != bo2));                                // True
        // D-form and N-form of the SAME bytes compare equal.
        Guid nD = Guid.Parse("aabbccdd-eeff-0011-2233-445566778899");
        Guid nN = Guid.Parse("aabbccddeeff00112233445566778899");
        Console.WriteLine("nform_eq=" + (nD == nN));                               // True
        Console.WriteLine("nform_cmp=" + (nD.CompareTo(nN) == 0));                 // True
        // Uppercase N-form parses and equals the lowercase D-form.
        Guid upN;
        bool okUp = Guid.TryParse("AABBCCDDEEFF00112233445566778899", out upN);
        Console.WriteLine("upn_ok=" + okUp);                                       // True
        Console.WriteLine("upn_eq=" + (upN == nD));                                // True

        // ==================== S5: TryParse edge cases ====================
        Guid t;
        bool okNull = Guid.TryParse(null, out t);
        Console.WriteLine("tp_null=" + okNull);                                    // False
        bool okEmpty = Guid.TryParse("", out t);
        Console.WriteLine("tp_empty=" + okEmpty);                                  // False
        bool okBad = Guid.TryParse("12345678-1234-1234-1234-12345678901z", out t);
        Console.WriteLine("tp_nonhex=" + okBad);                                   // False (trailing 'z')
        // Parse throws FormatException on the same malformed input.
        bool caught = false;
        try { Guid.Parse("12345678-1234-1234-1234-12345678901z"); }
        catch (FormatException) { caught = true; }
        Console.WriteLine("parse_nonhex=" + caught);                               // True
    }
}}
