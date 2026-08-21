//------------------------------------------------------------------------------
// guiddeep2.cs -- Guid domain DEEP probe 2 (e2e batch C46): Parse/ToString
// forms + field-wise signed/unsigned comparison -- byte-exact vs the net48
// oracle. Created after the Guid value-slot full chain landed (M6 batch 106:
// uint8_t[16] slot declaration, statement-form NewGuid/Parse writes, "g"
// byte-pointer cast for ldloca receivers).
//
// Wired surface (MethodMap.Guid.cs + guid.h), all probes stay within it:
//   Guid.Parse(string)      -> il2cpp_guid_parse_strict   (throws on invalid)
//   Guid.TryParse(...)      -> il2cpp_guid_parse           (bool)
//   .ToString()             -> il2cpp_guid_to_string       ("D", lowercase)
//   == / !=                 -> il2cpp_guid_equals / !equals
//   Equals(Guid)            -> il2cpp_guid_equals
//   CompareTo(Guid)         -> il2cpp_guid_compare (field-wise)
//
// net48-shape discipline:
//   * ToString() output for a PARSED Guid is DETERMINISTIC (bytes derive from
//     the input string), so the D-form strings are printed byte-exact --
//     lowercase, dashes at 8/13/18/23, even when the input was N-form or
//     uppercase (net48: Parse accepts D/N forms, ToString always emits the
//     lowercase D form).
//   * Parse probes use ONLY the D (36-char, dashes at 8/13/18/23) and N
//     (32-char, no dashes) forms -- the forms BOTH net48 and the runtime
//     accept. The B/P/X/whitespace forms are a RECORDED DEFECT (runtime
//     strict parse is D/N-only, guid.h "Deliberate deviation"), NOT probed.
//   * CompareTo is field-wise in DISPLAY ORDER: Data1, then Data2, then
//     Data3, then Data4 (the D-form group order). net48 (empirically pinned
//     by a live oracle probe) compares the 32/16/16-bit fields as UNSIGNED
//     and the Data4 bytes unsigned: a Guid whose Data1 has the high bit set
//     (0x80000000) sorts AFTER Data1 == 1, and 0x7fffffff sorts BEFORE
//     0x80000000. The CompareTo probes below cover BOTH the low-bit fields
//     (signed == unsigned) AND the high-bit corners (signed != unsigned,
//     e.g. d1_high_gt_low below). The runtime was fixed to match (D-GD-1).
//   * No NewGuid() randomness, no Guid.Empty (unwired BCL static), no
//     ToString(format) (LOUD: D-only implementation), no Equals(object)/
//     CompareTo(object) (LOUD: no boxed Guid runtime class).
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class GuidDeep2 {

    public static void Main()
    {
        // =============== S1: Parse D-form -> ToString() byte-exact ===============
        Guid g1 = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Console.WriteLine("d_str=" + g1.ToString());                     // 00112233-4455-6677-8899-aabbccddeeff
        // round-trip: Parse(ToString()) preserves the value.
        Guid g1b = Guid.Parse(g1.ToString());
        Console.WriteLine("d_rt=" + g1b.ToString());                     // 00112233-4455-6677-8899-aabbccddeeff
        Console.WriteLine("d_rt_eq=" + (g1b == g1));                     // True

        // =============== S2: Parse N-form (32 chars, no dashes) ===============
        Guid n1 = Guid.Parse("00112233445566778899aabbccddeeff");
        Console.WriteLine("n_str=" + n1.ToString());                     // 00112233-4455-6677-8899-aabbccddeeff
        Console.WriteLine("n_eq_d=" + (n1 == g1));                       // True

        // =============== S3: UPPERCASE input -> lowercase ToString() ===============
        Guid up = Guid.Parse("00112233-4455-6677-8899-AABBCCDDEEFF");
        Console.WriteLine("up_str=" + up.ToString());                    // lowercase D
        Console.WriteLine("up_eq=" + (up == g1));                        // True

        // =============== S4: CompareTo field-wise (unsigned fields, net48-pinned) ===============
        // Field dominance order: Data1 > Data2 > Data3 > Data4 (all unsigned).
        Guid d1 = Guid.Parse("00000001-0000-0000-0000-000000000000");
        Guid d2 = Guid.Parse("00000000-0001-0000-0000-000000000000");
        Console.WriteLine("d1_vs_d2=" + (d1.CompareTo(d2) > 0));        // True (Data1: 1 vs 0)
        Console.WriteLine("d2_vs_d1=" + (d2.CompareTo(d1) < 0));        // True
        Guid d3 = Guid.Parse("00000000-0000-0001-0000-000000000000");
        Console.WriteLine("d2_vs_d3=" + (d2.CompareTo(d3) > 0));        // True (Data2: 1 vs 0)
        Guid d4 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Console.WriteLine("d3_vs_d4=" + (d3.CompareTo(d4) > 0));        // True (Data3: 1 vs 0)
        Console.WriteLine("d4_vs_zero=" + (d4.CompareTo(Guid.Parse("00000000-0000-0000-0000-000000000000")) > 0)); // True
        // Reverse direction.
        Guid z = Guid.Parse("00000000-0000-0000-0000-000000000000");
        Console.WriteLine("zero_smallest=" + (z.CompareTo(d1) < 0));    // True
        // Data1 field difference dominates a Data4 difference.
        Guid dom1 = Guid.Parse("00000001-0000-0000-0000-000000000000");
        Guid dom4 = Guid.Parse("00000000-0000-0000-0000-ffffffffffff");
        Console.WriteLine("data1_dominates=" + (dom1.CompareTo(dom4) > 0)); // True (Data1: 1 vs 0)
        // Data4 UNSIGNED bytes: byte[15]=0xff > byte[15]=0x80 (no sign issue in Data1-3).
        Guid e1 = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
        Guid e2 = Guid.Parse("00000000-0000-0000-0000-000000000080");
        Console.WriteLine("d4_last=" + (e1.CompareTo(e2) > 0));          // True 0xff > 0x80
        Guid f1 = Guid.Parse("00000000-0000-0000-0000-000000000080");
        Guid f2 = Guid.Parse("00000000-0000-0000-0000-000000000000");
        Console.WriteLine("d4_byte80=" + (f1.CompareTo(f2) > 0));        // True (0x80 byte unsigned)
        // High-bit fields (D-GD-1 fixed): Data1/Data2/Data3 compare UNSIGNED.
        Guid hb1 = Guid.Parse("80000000-0000-0000-0000-000000000000");  // Data1 = 0x80000000 (2147483648)
        Guid lb1 = Guid.Parse("00000001-0000-0000-0000-000000000000");  // Data1 = 0x00000001
        Console.WriteLine("d1_high_gt_low=" + (hb1.CompareTo(lb1) > 0)); // True (0x80000000 > 1, unsigned)
        Console.WriteLine("d1_high_rev=" + (lb1.CompareTo(hb1) < 0));    // True
        Guid m7 = Guid.Parse("7fffffff-0000-0000-0000-000000000000");   // Data1 = 0x7fffffff
        Console.WriteLine("d1_7fff_lt_8000=" + (m7.CompareTo(hb1) < 0)); // True (0x7fffffff < 0x80000000)
        Guid hb2 = Guid.Parse("00000000-8000-0000-0000-000000000000");  // Data2 = 0x8000 (32768)
        Guid lb2 = Guid.Parse("00000000-0001-0000-0000-000000000000");  // Data2 = 0x0001
        Console.WriteLine("d2_high_gt_low=" + (hb2.CompareTo(lb2) > 0)); // True (0x8000 > 1, unsigned)
        Console.WriteLine("d2_high_rev=" + (lb2.CompareTo(hb2) < 0));    // True
        Guid hb3 = Guid.Parse("00000000-0000-8000-0000-000000000000");  // Data3 = 0x8000 (32768)
        Guid lb3 = Guid.Parse("00000000-0000-0001-0000-000000000000");  // Data3 = 0x0001
        Console.WriteLine("d3_high_gt_low=" + (hb3.CompareTo(lb3) > 0)); // True (0x8000 > 1, unsigned)
        Console.WriteLine("d3_high_rev=" + (lb3.CompareTo(hb3) < 0));    // True
        // Data1 high bit dominates a maxed Data2/3/4 (field order still holds).
        Guid hdom = Guid.Parse("80000000-ffff-ffff-ffff-ffffffffffff"); // Data1 = 0x80000000, rest maxed
        Console.WriteLine("d1_high_dominates=" + (hdom.CompareTo(lb1) > 0)); // True (0x80000000 > 1)
        // all-FF > zero (all fields unsigned).
        Guid zf = Guid.Parse("00000000-0000-0000-0000-000000000000");
        Guid ff = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        Console.WriteLine("ff_vs_zero=" + (ff.CompareTo(zf) > 0));       // True
        Console.WriteLine("zero_vs_ff=" + (zf.CompareTo(ff) < 0));       // True
        // Same value -> CompareTo == 0.
        Guid eqa = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        Guid eqb = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        Console.WriteLine("cmp_zero=" + (eqa.CompareTo(eqb) == 0));      // True

        // =============== S5: == / != / Equals ===============
        Console.WriteLine("eq_op=" + (eqa == eqb));                      // True
        Console.WriteLine("ne_op=" + (eqa != g1));                       // True
        Console.WriteLine("eq_op_neg=" + (eqa != eqb));                  // False
        Console.WriteLine("equals=" + (eqa.Equals(eqb)));                // True
        Console.WriteLine("equals_neg=" + (g1.Equals(eqb)));             // False
        // Equality ignores the input form: N-form == D-form == uppercase.
        Console.WriteLine("eq_multi=" + (n1 == up));                     // True

        // =============== S6: TryParse (bool + out Guid) ===============
        Guid t1;
        bool ok1 = Guid.TryParse("aabbccdd-eeff-0011-2233-445566778899", out t1);
        Console.WriteLine("try_d=" + ok1);                               // True
        Console.WriteLine("try_d_str=" + t1.ToString());                 // aabbccdd-eeff-0011-2233-445566778899
        Guid t2;
        bool ok2 = Guid.TryParse("aabbccddeeff00112233445566778899", out t2);
        Console.WriteLine("try_n=" + ok2);                               // True
        Console.WriteLine("try_n_str=" + t2.ToString());                 // aabbccdd-eeff-0011-2233-445566778899
        Console.WriteLine("try_n_eq=" + (t2 == t1));                     // True
        Guid t3;
        bool ok3 = Guid.TryParse("not-a-guid", out t3);
        Console.WriteLine("try_bad=" + ok3);                             // False
        Guid t4;
        bool ok4 = Guid.TryParse("", out t4);
        Console.WriteLine("try_empty=" + ok4);                           // False
        Guid t5;
        bool ok5 = Guid.TryParse("12345678-1234-1234-1234-12345678901", out t5); // one hex short
        Console.WriteLine("try_short=" + ok5);                           // False

        // =============== S7: Parse rejects invalid input (FormatException) ===============
        bool caught;
        caught = false; try { Guid.Parse(""); } catch (FormatException) { caught = true; }
        Console.WriteLine("parse_empty=" + caught);                      // True
        caught = false; try { Guid.Parse("not-a-guid"); } catch (FormatException) { caught = true; }
        Console.WriteLine("parse_bad=" + caught);                        // True
        caught = false; try { Guid.Parse("12345678-1234-1234-1234-12345678901"); } catch (FormatException) { caught = true; }
        Console.WriteLine("parse_short=" + caught);                      // True
        // Mixed case hex in D-form parses; output is still lowercase.
        Guid mc = Guid.Parse("AaBbCcDd-EeFf-1122-3344-556677889900");
        Console.WriteLine("mixed_str=" + mc.ToString());                 // aabbccdd-eeff-1122-3344-556677889900

        // =============== S8: byte-order sanity (Data1 reversed in D form) ===============
        // Data1 = 0x01020304 (bytes 04 03 02 01), Data2 = 0x0506, Data3 = 0x0708.
        Guid bo = Guid.Parse("01020304-0506-0708-090a-0b0c0d0e0f10");
        Console.WriteLine("byteorder=" + bo.ToString());                 // 01020304-0506-0708-090a-0b0c0d0e0f10
        // Parse of the SAME bytes in reversed Data1 spelling differs.
        Guid br = Guid.Parse("04030201-0506-0708-090a-0b0c0d0e0f10");
        Console.WriteLine("byteorder_ne=" + (bo != br));                 // True
        Console.WriteLine("byteorder_cmp=" + (bo.CompareTo(br) != 0));   // True
    }
}}
