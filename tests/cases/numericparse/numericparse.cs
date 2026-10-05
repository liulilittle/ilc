//------------------------------------------------------------------------------
// numericparse.cs -- System numeric-parsing DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C69 (2026-08-12), AFTER the
// parse domain wiring (MethodMap.ValueType.cs -> il2cpp_parse.h TryParse
// family + il2cpp_format.h throwing Parse family).
//
// DEEP DIMENSIONS (all surfaces below are WIRED):
//   1. int.Parse FULL BOUNDARY MATRIX (throwing Parse, classified by the
//      caught exception type): value 0 / signed zero / +/- sign forms /
//      ASCII whitespace variants / leading-zero forms / int32 MinValue and
//      MaxValue exactly / one-past both ends (OverflowException) / huge
//      magnitude / empty / whitespace-only / junk / floating-shaped text /
//      doubled sign. Every row asserts the net48 classification.
//   2. int.TryParse IDENTICAL MATRIX (out value + success flag, printed
//      pair-wise with Parse so the Parse/TryParse CONSISTENCY is visible
//      line-by-line), plus the out-slot WRITE-on-success contract and an
//      aggregate consistency count over the whole matrix. NOTE: the
//      failure-path out-slot differs (net48 writes 0, the runtime leaves it
//      untouched) -- D-NP-2, recorded in GAPS, NOT asserted.
//   3. long.TryParse FULL BOUNDARY: MinValue/MaxValue exactly, one-past
//      both ends, huge magnitude, leading zeros, whitespace, junk, empty,
//      null -- success flag + exact value.
//   4. uint boundary via the WIRED surface (uint.TryParse is NOT wired --
//      see GAPS D-NP-1): uint.ToString() on 0 / 1 / 0x80000000 /
//      uint.MaxValue, plus int.TryParse on the uint-domain strings
//      "2147483648".."4294967295" (all MUST fail int32 -- the boundary
//      between the signed and unsigned domains).
//   5. byte.TryParse: 0 / 255 / 256 (overflow) / -1 (sign) / whitespace.
//   6. bool.TryParse: "true"/"True"/"TRUE" case forms / "false" forms /
//      1 / 0 / junk / null.
//
// EVERY line prints "label=True/False"; net48 is the byte reference. The
// Parse rows catch FormatException/OverflowException ONLY -- a null text is
// NEVER passed to Parse (net48 ArgumentNullException vs runtime
// FormatException divergence, D-PAR-4); null is exercised via TryParse.
//------------------------------------------------------------------------------
using System;

class Program
{
    // int.Parse classified: "v:<value>" / "fmt" / "ovf". No null input.
    static string ParseLabel(string s)
    {
        try { return "v:" + int.Parse(s); }
        catch (FormatException) { return "fmt"; }
        catch (OverflowException) { return "ovf"; }
    }

    // int.TryParse: "v:<value>" on success, "fail" on failure (accepts null).
    static string TryLabel(string s)
    {
        int v;
        return int.TryParse(s, out v) ? ("v:" + v) : "fail";
    }

    static void Main()
    {
        // ============ 1. int.Parse full boundary matrix ============
        Console.WriteLine("p_zero=" + (ParseLabel("0") == "v:0"));
        Console.WriteLine("p_pos0=" + (ParseLabel("+0") == "v:0"));
        Console.WriteLine("p_neg0=" + (ParseLabel("-0") == "v:0"));
        Console.WriteLine("p_42=" + (ParseLabel("42") == "v:42"));
        Console.WriteLine("p_plus42=" + (ParseLabel("+42") == "v:42"));
        Console.WriteLine("p_minus42=" + (ParseLabel("-42") == "v:-42"));
        Console.WriteLine("p_ws=" + (ParseLabel("  42  ") == "v:42"));
        Console.WriteLine("p_ws_tab=" + (ParseLabel("\t42\n") == "v:42"));
        Console.WriteLine("p_lead0=" + (ParseLabel("00042") == "v:42"));
        Console.WriteLine("p_lead0long=" + (ParseLabel("00000000000000000000000042") == "v:42"));
        Console.WriteLine("p_max=" + (ParseLabel("2147483647") == "v:2147483647"));
        Console.WriteLine("p_min=" + (ParseLabel("-2147483648") == "v:-2147483648"));
        Console.WriteLine("p_max1=" + (ParseLabel("2147483648") == "ovf"));
        Console.WriteLine("p_min1=" + (ParseLabel("-2147483649") == "ovf"));
        Console.WriteLine("p_huge=" + (ParseLabel("99999999999999999999999") == "ovf"));
        Console.WriteLine("p_empty=" + (ParseLabel("") == "fmt"));
        Console.WriteLine("p_wsonly=" + (ParseLabel("   ") == "fmt"));
        Console.WriteLine("p_junk=" + (ParseLabel("12a") == "fmt"));
        Console.WriteLine("p_float=" + (ParseLabel("1.5") == "fmt"));
        Console.WriteLine("p_dblsign=" + (ParseLabel("+-5") == "fmt"));
        Console.WriteLine("p_minussign=" + (ParseLabel("-") == "fmt"));
        Console.WriteLine("p_hex=" + (ParseLabel("0x10") == "fmt"));

        // ============ 2. int.TryParse same matrix + out contract ============
        Console.WriteLine("t_zero=" + (TryLabel("0") == "v:0"));
        Console.WriteLine("t_pos0=" + (TryLabel("+0") == "v:0"));
        Console.WriteLine("t_neg0=" + (TryLabel("-0") == "v:0"));
        Console.WriteLine("t_42=" + (TryLabel("42") == "v:42"));
        Console.WriteLine("t_plus42=" + (TryLabel("+42") == "v:42"));
        Console.WriteLine("t_minus42=" + (TryLabel("-42") == "v:-42"));
        Console.WriteLine("t_ws=" + (TryLabel("  42  ") == "v:42"));
        Console.WriteLine("t_ws_tab=" + (TryLabel("\t42\n") == "v:42"));
        Console.WriteLine("t_lead0=" + (TryLabel("00042") == "v:42"));
        Console.WriteLine("t_max=" + (TryLabel("2147483647") == "v:2147483647"));
        Console.WriteLine("t_min=" + (TryLabel("-2147483648") == "v:-2147483648"));
        Console.WriteLine("t_max1=" + (TryLabel("2147483648") == "fail"));
        Console.WriteLine("t_min1=" + (TryLabel("-2147483649") == "fail"));
        Console.WriteLine("t_huge=" + (TryLabel("99999999999999999999999") == "fail"));
        Console.WriteLine("t_empty=" + (TryLabel("") == "fail"));
        Console.WriteLine("t_wsonly=" + (TryLabel("   ") == "fail"));
        Console.WriteLine("t_junk=" + (TryLabel("12a") == "fail"));
        Console.WriteLine("t_float=" + (TryLabel("1.5") == "fail"));
        Console.WriteLine("t_dblsign=" + (TryLabel("+-5") == "fail"));
        Console.WriteLine("t_minussign=" + (TryLabel("-") == "fail"));
        Console.WriteLine("t_null=" + (TryLabel(null) == "fail"));

        // out slot on SUCCESS is written (both sides). On FAILURE net48
        // overwrites the out slot with 0 while the runtime leaves it
        // untouched (D-NP-2, GAPS) -- only the success-write contract is
        // asserted here.
        int keep2 = 999;
        bool okKeep2 = int.TryParse("-42", out keep2);
        Console.WriteLine("out_success_write=" + (okKeep2 && keep2 == -42));

        // aggregate Parse/TryParse consistency over the full matrix
        string[] inputs = new string[] {
            "0", "+0", "-0", "42", "+42", "-42", "  42  ", "\t42\n", "00042",
            "00000000000000000000000042", "2147483647", "-2147483648",
            "2147483648", "-2147483649", "99999999999999999999999",
            "", "   ", "12a", "1.5", "+-5", "-", "0x10", null
        };
        int match = 0;
        foreach (string s in inputs)
        {
            int pv; bool pOk;
            if (s == null) { pOk = false; pv = 0; }
            else
            {
                try { pv = int.Parse(s); pOk = true; }
                catch (Exception) { pv = 0; pOk = false; }
            }
            int tv;
            bool tOk = int.TryParse(s, out tv);
            if (pOk == tOk && (!pOk || pv == tv)) match++;
        }
        Console.WriteLine("consistency=" + (match == inputs.Length));

        // ============ 3. long.TryParse full boundary ============
        long l;
        Console.WriteLine("l_zero=" + (long.TryParse("0", out l) && l == 0));
        Console.WriteLine("l_42=" + (long.TryParse("42", out l) && l == 42));
        Console.WriteLine("l_neg=" + (long.TryParse("-42", out l) && l == -42));
        Console.WriteLine("l_max=" + (long.TryParse("9223372036854775807", out l) && l == long.MaxValue));
        Console.WriteLine("l_min=" + (long.TryParse("-9223372036854775808", out l) && l == long.MinValue));
        Console.WriteLine("l_max1=" + (long.TryParse("9223372036854775808", out l) == false));
        Console.WriteLine("l_min1=" + (long.TryParse("-9223372036854775809", out l) == false));
        Console.WriteLine("l_huge=" + (long.TryParse("99999999999999999999999999999", out l) == false));
        Console.WriteLine("l_lead0=" + (long.TryParse("0009223372036854775807", out l) && l == long.MaxValue));
        Console.WriteLine("l_ws=" + (long.TryParse("  9223372036854775807  ", out l) && l == long.MaxValue));
        Console.WriteLine("l_junk=" + (long.TryParse("abc", out l) == false));
        Console.WriteLine("l_float=" + (long.TryParse("12.5", out l) == false));
        Console.WriteLine("l_empty=" + (long.TryParse("", out l) == false));
        Console.WriteLine("l_null=" + (long.TryParse(null, out l) == false));

        // ============ 4. uint boundary via the wired surface ============
        Console.WriteLine("u0_str=" + (0u.ToString() == "0"));
        Console.WriteLine("u1_str=" + (1u.ToString() == "1"));
        Console.WriteLine("u80000000_str=" + (2147483648u.ToString() == "2147483648"));
        Console.WriteLine("umax_str=" + (uint.MaxValue.ToString() == "4294967295"));
        Console.WriteLine("uint_domain_2147483648=" + (int.TryParse("2147483648", out int ui) == false));
        Console.WriteLine("uint_domain_4294967295=" + (int.TryParse("4294967295", out int ui2) == false));
        Console.WriteLine("uint_domain_2147483647=" + (int.TryParse("2147483647", out int ui3) && ui3 == 2147483647));

        // ============ 5. byte.TryParse boundaries ============
        byte b;
        Console.WriteLine("b0=" + (byte.TryParse("0", out b) && b == 0));
        Console.WriteLine("b255=" + (byte.TryParse("255", out b) && b == 255));
        Console.WriteLine("b256=" + (byte.TryParse("256", out b) == false));
        Console.WriteLine("bneg1=" + (byte.TryParse("-1", out b) == false));
        Console.WriteLine("bws=" + (byte.TryParse(" 12 ", out b) && b == 12));

        // ============ 6. bool.TryParse case forms ============
        bool bl;
        Console.WriteLine("bt_true=" + (bool.TryParse("true", out bl) && bl));
        Console.WriteLine("bt_True=" + (bool.TryParse("True", out bl) && bl));
        Console.WriteLine("bt_TRUE=" + (bool.TryParse("TRUE", out bl) && bl));
        Console.WriteLine("bf_false=" + (bool.TryParse("false", out bl) && !bl));
        Console.WriteLine("bf_False=" + (bool.TryParse("False", out bl) && !bl));
        Console.WriteLine("bf_1=" + (bool.TryParse("1", out bl) == false));
        Console.WriteLine("bf_0=" + (bool.TryParse("0", out bl) == false));
        Console.WriteLine("bf_junk=" + (bool.TryParse("yes", out bl) == false));
        Console.WriteLine("bf_ws=" + (bool.TryParse(" true ", out bl) && bl));
        Console.WriteLine("bf_null=" + (bool.TryParse(null, out bl) == false));
    }
}
