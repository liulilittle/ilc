// parsecase2.cs -- PARSE DEEP 2 (e2e batch C24, "Parse 深测 2:
// fromBase/uint wrap -- parse 修复后"). Builds on parsecase (single-layer
// TryParse/Convert surface) and goes DEEPER on the base-N conversions +
// the uint32 two's-complement wraparound that dominates the fromBase
// contract:
//
//   * Convert.ToInt32(string, fromBase) -- net48 ParseNumbers.StringToInt
//     (IsTight|NoSpace): radix must be 2/8/10/16 (else ArgumentException,
//     checked BEFORE the string -- null with an invalid base still throws),
//     null parses as 0, "" throws ArgumentOutOfRangeException, NO whitespace
//     for any radix, '+' accepted for every radix (skipped), '-' only for
//     radix 10, radix 16 strips a "0x"/"0X" prefix.
//   * UINT WRAP: radix 2/8/16 accumulate in uint32 (no int32 range check) --
//     "FFFFFFFF",16 -> -1 and "80000000",16 -> INT32_MIN wrap THROUGH the
//     bit pattern, but exceeding 0xFFFFFFFF (9 hex / 12 octal / 33 binary
//     digits) is OverflowException (no wrap across 32 bits).
//   * Convert.ToString(int, toBase) -- net48 ParseNumbers.IntToString:
//     base 10 signed; bases 2/8/16 render the value as UNSIGNED uint32
//     (two's complement), lowercase hex, no leading zeros.
//   * Round-trips: ToString(ToInt32(s, b), b) == s for the wrap edges
//     (negative values round-trip exactly through their uint32 spelling).
//
// Every row is deterministic (throw-type classification + exact values);
// the exception rows are classified by the caught exception TYPE.
// Expected output: all-bool rows over exact counter values.
using System;

namespace Cases { public static class Parsecase2 {

    // Convert.ToInt32(string, fromBase) classified by the caught exception
    // type (ArgumentOutOfRangeException must be caught BEFORE its base
    // ArgumentException).
    static string Conv(string s, int b)
    {
        try { return "v=" + Convert.ToInt32(s, b); }
        catch (ArgumentOutOfRangeException) { return "oor"; }
        catch (ArgumentException) { return "arg"; }
        catch (FormatException) { return "fmt"; }
        catch (OverflowException) { return "ovf"; }
    }

    // Convert.ToString(int, toBase) -- the only throw is ArgumentException.
    static string ConvS(int v, int b)
    {
        try { return "s=" + Convert.ToString(v, b); }
        catch (ArgumentException) { return "arg"; }
    }

    public static void Main()
    {
        // ============ A. Convert.ToInt32(string, fromBase) -- radix 2 ============
        Console.WriteLine("b2_0=" + Conv("0", 2));               // v=0
        Console.WriteLine("b2_1010=" + Conv("1010", 2));         // v=10
        Console.WriteLine("b2_plus=" + Conv("+1010", 2));        // v=10 ('+' every radix)
        Console.WriteLine("b2_neg=" + Conv("-101", 2));          // arg (no '-' for 2/8/16)
        Console.WriteLine("b2_baddigit=" + Conv("2", 2));        // fmt ('2' not binary)
        Console.WriteLine("b2_ws=" + Conv(" 101", 2));           // fmt (no whitespace)
        Console.WriteLine("b2_ws2=" + Conv("101 ", 2));          // fmt
        Console.WriteLine("b2_max32=" + Conv("11111111111111111111111111111111", 2)); // v=-1 (wrap)
        Console.WriteLine("b2_min32=" + Conv("10000000000000000000000000000000", 2)); // v=-2147483648 (wrap)
        Console.WriteLine("b2_33bits=" + Conv("100000000000000000000000000000000", 2)); // ovf (33 bits)

        // ============ B. radix 8 ============
        Console.WriteLine("b8_0=" + Conv("0", 8));               // v=0
        Console.WriteLine("b8_777=" + Conv("777", 8));           // v=511
        Console.WriteLine("b8_7777777777=" + Conv("7777777777", 8));   // v=1073741823 (10 digits)
        Console.WriteLine("b8_neg1=" + Conv("37777777777", 8));  // v=-1 (0xFFFFFFFF wrap)
        Console.WriteLine("b8_min=" + Conv("20000000000", 8));   // v=-2147483648 (0x80000000 wrap)
        Console.WriteLine("b8_ovf_2p32=" + Conv("40000000000", 8));    // ovf (2^32)
        Console.WriteLine("b8_ovf_12dig=" + Conv("777777777777", 8));  // ovf (12 digits)
        Console.WriteLine("b8_neg=" + Conv("-40000000000", 8));  // arg

        // ============ C. radix 16 ============
        Console.WriteLine("b16_f=" + Conv("F", 16));             // v=15
        Console.WriteLine("b16_ff=" + Conv("ff", 16));           // v=255
        Console.WriteLine("b16_FF=" + Conv("FF", 16));           // v=255
        Console.WriteLine("b16_0xFF=" + Conv("0xFF", 16));       // v=255 (0x prefix)
        Console.WriteLine("b16_plus0X=" + Conv("+0X1F", 16));    // v=31
        Console.WriteLine("b16_neg=" + Conv("-FF", 16));         // arg
        Console.WriteLine("b16_abcdef=" + Conv("ABCDEF", 16));   // v=11259375
        Console.WriteLine("b16_max=" + Conv("7FFFFFFF", 16));    // v=2147483647
        Console.WriteLine("b16_wrap_min=" + Conv("80000000", 16)); // v=-2147483648 (wrap)
        Console.WriteLine("b16_wrap_neg1=" + Conv("FFFFFFFF", 16)); // v=-1 (wrap)
        Console.WriteLine("b16_9dig=" + Conv("100000000", 16));  // ovf (9 hex digits)
        Console.WriteLine("b16_9F=" + Conv("FFFFFFFFF", 16));    // ovf
        Console.WriteLine("b16_prefixonly=" + Conv("0x", 16));   // fmt (no digits)
        Console.WriteLine("b16_prefixbad=" + Conv("0xG", 16));   // fmt (invalid digit)

        // ============ D. radix 10 (signed, range-checked) ============
        Console.WriteLine("b10_42=" + Conv("42", 10));           // v=42
        Console.WriteLine("b10_neg42=" + Conv("-42", 10));       // v=-42
        Console.WriteLine("b10_plus=" + Conv("+2147483647", 10));// v=2147483647
        Console.WriteLine("b10_max=" + Conv("2147483647", 10));  // v=2147483647
        Console.WriteLine("b10_min=" + Conv("-2147483648", 10)); // v=-2147483648
        Console.WriteLine("b10_neg0=" + Conv("-0", 10));         // v=0
        Console.WriteLine("b10_leadzeros=" + Conv("0000000000000000000001", 10)); // v=1
        Console.WriteLine("b10_ovf=" + Conv("2147483648", 10));  // ovf
        Console.WriteLine("b10_negovf=" + Conv("-2147483649", 10)); // ovf
        Console.WriteLine("b10_11dig=" + Conv("99999999999", 10)); // ovf (11 sig digits)
        Console.WriteLine("b10_ws=" + Conv(" 42 ", 10));         // fmt
        Console.WriteLine("b10_empty=" + Conv("", 10));          // oor (ArgumentOutOfRangeException)
        Console.WriteLine("b10_null=" + Conv(null, 10));         // v=0 (net48 null rule)

        // ============ E. invalid radix (checked before the string) ============
        Console.WriteLine("bx_3=" + Conv("10", 3));              // arg (base 3)
        Console.WriteLine("bx_0=" + Conv("10", 0));              // arg (base 0)
        Console.WriteLine("bx_36=" + Conv("10", 36));            // arg (base 36)
        Console.WriteLine("bx_null3=" + Conv(null, 3));          // arg (radix first)

        // ============ F. Convert.ToString(int, toBase) ============
        Console.WriteLine("s0_2=" + ConvS(0, 2));                // s=0
        Console.WriteLine("s10_2=" + ConvS(10, 2));              // s=1010
        Console.WriteLine("sn1_2=" + ConvS(-1, 2));              // s=11111111111111111111111111111111
        Console.WriteLine("sn1_8=" + ConvS(-1, 8));              // s=37777777777
        Console.WriteLine("sn1_16=" + ConvS(-1, 16));            // s=ffffffff
        Console.WriteLine("smin_16=" + ConvS(int.MinValue, 16)); // s=80000000
        Console.WriteLine("smin_2=" + ConvS(int.MinValue, 2));   // s=10000000000000000000000000000000
        Console.WriteLine("smin_8=" + ConvS(int.MinValue, 8));   // s=20000000000
        Console.WriteLine("smax_16=" + ConvS(int.MaxValue, 16)); // s=7fffffff
        Console.WriteLine("s255_16=" + ConvS(255, 16));          // s=ff (lowercase)
        Console.WriteLine("s255_8=" + ConvS(255, 8));            // s=377
        Console.WriteLine("sn42_16=" + ConvS(-42, 16));          // s=ffffffd6
        Console.WriteLine("s_bad3=" + ConvS(7, 3));              // arg
        Console.WriteLine("s_bad1=" + ConvS(5, 1));              // arg

        // ============ G. uint-wrap round-trips ============
        Console.WriteLine("rt_neg1=" + Convert.ToString(Convert.ToInt32("FFFFFFFF", 16), 16)); // ffffffff
        Console.WriteLine("rt_min=" + Convert.ToString(Convert.ToInt32("80000000", 16), 16));  // 80000000
        Console.WriteLine("rt_rt1=" + (Convert.ToInt32(Convert.ToString(-1, 16), 16) == -1));  // True
        Console.WriteLine("rt_rt2=" + (Convert.ToInt32(Convert.ToString(int.MinValue, 8), 8) == int.MinValue)); // True
        Console.WriteLine("rt_rt3=" + (Convert.ToInt32(Convert.ToString(int.MinValue, 2), 2) == int.MinValue)); // True
        Console.WriteLine("rt_rt4=" + (Convert.ToInt32(Convert.ToString(123456789, 16), 16) == 123456789)); // True
    }
}}
