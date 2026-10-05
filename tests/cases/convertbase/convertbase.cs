//------------------------------------------------------------------------------
// convertbase.cs -- System.Convert base-conversion DEEP e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C60, AFTER the convert
// domain (convertdeep) is fully wired and PASSing.
//
// THIS case drives the RADIX-CONVERSION surface to exhaustion:
//
//   1) ToString(int, base) -- icall_convert_to_string_i4_base. Bases 2/8/16
//      render the value as UNSIGNED (uint32) binary/octal/lowercase-hex with
//      no leading zeros; base 10 renders the signed decimal. Probes cover the
//      full boundary lattice: 0, 1, -1, powers of two, digit-boundary values
//      (7/8, 15/16, 255/256, 1023/1024, 32767/32768), int.MaxValue /
//      int.MinValue, and bit-pattern constants (0x55555555, 0xAAAAAAAA,
//      0x1FFFFFF) in ALL FOUR legal bases.
//   2) ToInt32(string, base) -- icall_convert_to_int32_string_base. The TIGHT
//      parse contract (no whitespace, '0x'/'0X' prefix ONLY radix 16, '+' for
//      any radix, '-' ONLY radix 10, radix-10 int32 range, radix 2/8/16 uint32
//      accumulation with 0xFFFFFFFF as the cap). Probes cover valid parses,
//      round trips (ToString o ToInt32 == identity), uppercase/mixed-case hex,
//      leading zeros, null->0, and the FormatException / OverflowException /
//      ArgumentOutOfRangeException / ArgumentException exception surface per
//      radix.
//   3) CUSTOM radix boundary (recorded GAP, NOT exercised -- see GAPS.md
//      D-CB-1): the runtime's radix validator accepts 2..36 while net48
//      accepts ONLY {2,8,10,16}, so bases 3..36 DIVERGE (net48 throws
//      ArgumentException, runtime parses). Bases {0,1,37,100} are rejected by
//      BOTH sides (net48: not in {2,8,10,16}; runtime: not in 2..36) -- those
//      shared ArgumentException probes ARE exercised below as the "custom"
//      boundary that stays byte-exact.
//
// Every printed line is "label=True/False" (bool comparison) or an
// exception-type catch probe "label:<ExceptionTypeName>" -- net48 message text
// is OS-localized and never byte-diffed.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ConvertBaseCase {
    public static void Main() {
        // ============ 1. ToString(int, base): radix 2 ============
        Console.WriteLine("t2_0=" + (Convert.ToString(0, 2) == "0"));
        Console.WriteLine("t2_1=" + (Convert.ToString(1, 2) == "1"));
        Console.WriteLine("t2_2=" + (Convert.ToString(2, 2) == "10"));
        Console.WriteLine("t2_3=" + (Convert.ToString(3, 2) == "11"));
        Console.WriteLine("t2_7=" + (Convert.ToString(7, 2) == "111"));
        Console.WriteLine("t2_8=" + (Convert.ToString(8, 2) == "1000"));
        Console.WriteLine("t2_15=" + (Convert.ToString(15, 2) == "1111"));
        Console.WriteLine("t2_16=" + (Convert.ToString(16, 2) == "10000"));
        Console.WriteLine("t2_255=" + (Convert.ToString(255, 2) == "11111111"));
        Console.WriteLine("t2_256=" + (Convert.ToString(256, 2) == "100000000"));
        Console.WriteLine("t2_1023=" + (Convert.ToString(1023, 2) == "1111111111"));
        Console.WriteLine("t2_1024=" + (Convert.ToString(1024, 2) == "10000000000"));
        Console.WriteLine("t2_32767=" + (Convert.ToString(32767, 2) == "111111111111111"));
        Console.WriteLine("t2_32768=" + (Convert.ToString(32768, 2) == "1000000000000000"));
        Console.WriteLine("t2_5555=" + (Convert.ToString(0x55555555, 2) == "1010101010101010101010101010101"));
        // NOTE: 0xAAAAAAAA > int.MaxValue is a UINT literal -- it would bind
        // to the unwired Convert.ToString(uint,int) overload. Cast to int
        // explicitly (the value as int32 == -1431655766, printed as its
        // uint32 two's-complement "1010..." by the wired int32 icall).
        Console.WriteLine("t2_AAAA=" + (Convert.ToString(unchecked((int)0xAAAAAAAA), 2) == "10101010101010101010101010101010"));
        Console.WriteLine("t2_max=" + (Convert.ToString(int.MaxValue, 2) == "1111111111111111111111111111111"));
        Console.WriteLine("t2_min=" + (Convert.ToString(int.MinValue, 2) == "10000000000000000000000000000000"));
        Console.WriteLine("t2_neg1=" + (Convert.ToString(-1, 2) == "11111111111111111111111111111111"));

        // ============ 2. ToString(int, base): radix 8 ============
        Console.WriteLine("t8_0=" + (Convert.ToString(0, 8) == "0"));
        Console.WriteLine("t8_7=" + (Convert.ToString(7, 8) == "7"));
        Console.WriteLine("t8_8=" + (Convert.ToString(8, 8) == "10"));
        Console.WriteLine("t8_15=" + (Convert.ToString(15, 8) == "17"));
        Console.WriteLine("t8_63=" + (Convert.ToString(63, 8) == "77"));
        Console.WriteLine("t8_64=" + (Convert.ToString(64, 8) == "100"));
        Console.WriteLine("t8_511=" + (Convert.ToString(511, 8) == "777"));
        Console.WriteLine("t8_512=" + (Convert.ToString(512, 8) == "1000"));
        Console.WriteLine("t8_4095=" + (Convert.ToString(4095, 8) == "7777"));
        Console.WriteLine("t8_4096=" + (Convert.ToString(4096, 8) == "10000"));
        Console.WriteLine("t8_32767=" + (Convert.ToString(32767, 8) == "77777"));
        Console.WriteLine("t8_32768=" + (Convert.ToString(32768, 8) == "100000"));
        Console.WriteLine("t8_1FFFFFF=" + (Convert.ToString(0x1FFFFFF, 8) == "177777777"));
        Console.WriteLine("t8_max=" + (Convert.ToString(int.MaxValue, 8) == "17777777777"));
        Console.WriteLine("t8_min=" + (Convert.ToString(int.MinValue, 8) == "20000000000"));
        Console.WriteLine("t8_neg1=" + (Convert.ToString(-1, 8) == "37777777777"));

        // ============ 3. ToString(int, base): radix 16 ============
        Console.WriteLine("t16_0=" + (Convert.ToString(0, 16) == "0"));
        Console.WriteLine("t16_f=" + (Convert.ToString(15, 16) == "f"));
        Console.WriteLine("t16_10=" + (Convert.ToString(16, 16) == "10"));
        Console.WriteLine("t16_ff=" + (Convert.ToString(255, 16) == "ff"));
        Console.WriteLine("t16_100=" + (Convert.ToString(256, 16) == "100"));
        Console.WriteLine("t16_3ff=" + (Convert.ToString(1023, 16) == "3ff"));
        Console.WriteLine("t16_400=" + (Convert.ToString(1024, 16) == "400"));
        Console.WriteLine("t16_fff=" + (Convert.ToString(4095, 16) == "fff"));
        Console.WriteLine("t16_1000=" + (Convert.ToString(4096, 16) == "1000"));
        Console.WriteLine("t16_7fff=" + (Convert.ToString(32767, 16) == "7fff"));
        Console.WriteLine("t16_8000=" + (Convert.ToString(32768, 16) == "8000"));
        Console.WriteLine("t16_abcdef=" + (Convert.ToString(0xABCDEF, 16) == "abcdef"));
        Console.WriteLine("t16_max=" + (Convert.ToString(int.MaxValue, 16) == "7fffffff"));
        Console.WriteLine("t16_min=" + (Convert.ToString(int.MinValue, 16) == "80000000"));
        Console.WriteLine("t16_neg1=" + (Convert.ToString(-1, 16) == "ffffffff"));

        // ============ 4. ToString(int, base): radix 10 (signed decimal) ===
        Console.WriteLine("t10_0=" + (Convert.ToString(0, 10) == "0"));
        Console.WriteLine("t10_42=" + (Convert.ToString(42, 10) == "42"));
        Console.WriteLine("t10_neg42=" + (Convert.ToString(-42, 10) == "-42"));
        Console.WriteLine("t10_max=" + (Convert.ToString(int.MaxValue, 10) == "2147483647"));
        Console.WriteLine("t10_min=" + (Convert.ToString(int.MinValue, 10) == "-2147483648"));
        Console.WriteLine("t10_neg1=" + (Convert.ToString(-1, 10) == "-1"));

        // ============ 5. ToInt32(string, base): valid parses ============
        Console.WriteLine("p2_101010=" + (Convert.ToInt32("101010", 2) == 42));
        Console.WriteLine("p2_11111111=" + (Convert.ToInt32("11111111", 2) == 255));
        Console.WriteLine("p2_leading=" + (Convert.ToInt32("00000000101010", 2) == 42));
        Console.WriteLine("p2_max=" + (Convert.ToInt32("1111111111111111111111111111111", 2) == int.MaxValue));
        Console.WriteLine("p2_min=" + (Convert.ToInt32("10000000000000000000000000000000", 2) == int.MinValue));
        Console.WriteLine("p2_neg1=" + (Convert.ToInt32("11111111111111111111111111111111", 2) == -1));
        Console.WriteLine("p8_377=" + (Convert.ToInt32("377", 8) == 255));
        Console.WriteLine("p8_leading=" + (Convert.ToInt32("00000777", 8) == 511));
        Console.WriteLine("p8_max=" + (Convert.ToInt32("17777777777", 8) == int.MaxValue));
        Console.WriteLine("p8_min=" + (Convert.ToInt32("20000000000", 8) == int.MinValue));
        Console.WriteLine("p8_neg1=" + (Convert.ToInt32("37777777777", 8) == -1));
        Console.WriteLine("p16_ff=" + (Convert.ToInt32("ff", 16) == 255));
        Console.WriteLine("p16_FF=" + (Convert.ToInt32("FF", 16) == 255));
        Console.WriteLine("p16_mixed=" + (Convert.ToInt32("aBcDeF", 16) == 0xABCDEF));
        Console.WriteLine("p16_leading=" + (Convert.ToInt32("0000ff", 16) == 255));
        Console.WriteLine("p16_max=" + (Convert.ToInt32("7fffffff", 16) == int.MaxValue));
        Console.WriteLine("p16_min=" + (Convert.ToInt32("80000000", 16) == int.MinValue));
        Console.WriteLine("p16_neg1=" + (Convert.ToInt32("ffffffff", 16) == -1));
        Console.WriteLine("p10_42=" + (Convert.ToInt32("42", 10) == 42));
        Console.WriteLine("p10_max=" + (Convert.ToInt32("2147483647", 10) == int.MaxValue));
        Console.WriteLine("p10_min=" + (Convert.ToInt32("-2147483648", 10) == int.MinValue));
        Console.WriteLine("p10_leading=" + (Convert.ToInt32("00042", 10) == 42));

        // ============ 6. Round trips: ToString o ToInt32 == identity =====
        Console.WriteLine("rt_255_16=" + (Convert.ToInt32(Convert.ToString(255, 16), 16) == 255));
        Console.WriteLine("rt_neg1_2=" + (Convert.ToInt32(Convert.ToString(-1, 2), 2) == -1));
        Console.WriteLine("rt_min_8=" + (Convert.ToInt32(Convert.ToString(int.MinValue, 8), 8) == int.MinValue));
        Console.WriteLine("rt_max_16=" + (Convert.ToInt32(Convert.ToString(int.MaxValue, 16), 16) == int.MaxValue));
        Console.WriteLine("rt_42_10=" + (Convert.ToInt32(Convert.ToString(42, 10), 10) == 42));
        Console.WriteLine("rt_neg42_10=" + (Convert.ToInt32(Convert.ToString(-42, 10), 10) == -42));
        Console.WriteLine("rt_abcde_16=" + (Convert.ToInt32(Convert.ToString(0xABCDE, 16), 16) == 0xABCDE));

        // ============ 7. prefix / sign handling ============
        Console.WriteLine("pre_0x=" + (Convert.ToInt32("0xFF", 16) == 255));
        Console.WriteLine("pre_0X=" + (Convert.ToInt32("0X1F", 16) == 31));
        Console.WriteLine("pre_0x_zero=" + (Convert.ToInt32("0x0", 16) == 0));
        Console.WriteLine("pre_plus16=" + (Convert.ToInt32("+ff", 16) == 255));
        Console.WriteLine("pre_plus8=" + (Convert.ToInt32("+777", 8) == 511));
        Console.WriteLine("pre_plus2=" + (Convert.ToInt32("+101010", 2) == 42));
        Console.WriteLine("pre_plus10=" + (Convert.ToInt32("+42", 10) == 42));
        Console.WriteLine("pre_neg10=" + (Convert.ToInt32("-42", 10) == -42));

        // ============ 8. TIGHT parse: no whitespace anywhere =============
        try { Convert.ToInt32(" 42 ", 10); Console.WriteLine("ws_10:ok"); }
        catch (Exception e) { Console.WriteLine("ws_10:" + e.GetType().Name); }
        try { Convert.ToInt32(" 42", 10); Console.WriteLine("ws_lead:ok"); }
        catch (Exception e) { Console.WriteLine("ws_lead:" + e.GetType().Name); }
        try { Convert.ToInt32("42 ", 10); Console.WriteLine("ws_trail:ok"); }
        catch (Exception e) { Console.WriteLine("ws_trail:" + e.GetType().Name); }
        try { Convert.ToInt32(" ff", 16); Console.WriteLine("ws_hex:ok"); }
        catch (Exception e) { Console.WriteLine("ws_hex:" + e.GetType().Name); }

        // ============ 9. digit validation per radix ======================
        try { Convert.ToInt32("2", 2); Console.WriteLine("dg2:ok"); }
        catch (Exception e) { Console.WriteLine("dg2:" + e.GetType().Name); }
        try { Convert.ToInt32("102", 2); Console.WriteLine("dg2_mid:ok"); }
        catch (Exception e) { Console.WriteLine("dg2_mid:" + e.GetType().Name); }
        try { Convert.ToInt32("8", 8); Console.WriteLine("dg8:ok"); }
        catch (Exception e) { Console.WriteLine("dg8:" + e.GetType().Name); }
        try { Convert.ToInt32("9", 8); Console.WriteLine("dg8_9:ok"); }
        catch (Exception e) { Console.WriteLine("dg8_9:" + e.GetType().Name); }
        try { Convert.ToInt32("G", 16); Console.WriteLine("dg16:ok"); }
        catch (Exception e) { Console.WriteLine("dg16:" + e.GetType().Name); }
        try { Convert.ToInt32("1G", 16); Console.WriteLine("dg16_mid:ok"); }
        catch (Exception e) { Console.WriteLine("dg16_mid:" + e.GetType().Name); }
        try { Convert.ToInt32("12a", 10); Console.WriteLine("dg10:ok"); }
        catch (Exception e) { Console.WriteLine("dg10:" + e.GetType().Name); }

        // ============ 10. '0x' prefix ONLY radix 16 ======================
        try { Convert.ToInt32("0xFF", 2); Console.WriteLine("pre_0x2:ok"); }
        catch (Exception e) { Console.WriteLine("pre_0x2:" + e.GetType().Name); }
        try { Convert.ToInt32("0x10", 8); Console.WriteLine("pre_0x8:ok"); }
        catch (Exception e) { Console.WriteLine("pre_0x8:" + e.GetType().Name); }
        try { Convert.ToInt32("0x1F", 10); Console.WriteLine("pre_0x10:ok"); }
        catch (Exception e) { Console.WriteLine("pre_0x10:" + e.GetType().Name); }

        // ============ 11. '-' ONLY radix 10 ==============================
        try { Convert.ToInt32("-101", 2); Console.WriteLine("neg2:ok"); }
        catch (Exception e) { Console.WriteLine("neg2:" + e.GetType().Name); }
        try { Convert.ToInt32("-777", 8); Console.WriteLine("neg8:ok"); }
        catch (Exception e) { Console.WriteLine("neg8:" + e.GetType().Name); }
        try { Convert.ToInt32("-ff", 16); Console.WriteLine("neg16:ok"); }
        catch (Exception e) { Console.WriteLine("neg16:" + e.GetType().Name); }

        // ============ 12. Overflow per radix =============================
        try { Convert.ToInt32("111111111111111111111111111111111", 2); Console.WriteLine("ovf2:ok"); }
        catch (Exception e) { Console.WriteLine("ovf2:" + e.GetType().Name); }
        try { Convert.ToInt32("40000000000", 8); Console.WriteLine("ovf8:ok"); }
        catch (Exception e) { Console.WriteLine("ovf8:" + e.GetType().Name); }
        try { Convert.ToInt32("100000000", 16); Console.WriteLine("ovf16:ok"); }
        catch (Exception e) { Console.WriteLine("ovf16:" + e.GetType().Name); }
        try { Convert.ToInt32("2147483648", 10); Console.WriteLine("ovf10:ok"); }
        catch (Exception e) { Console.WriteLine("ovf10:" + e.GetType().Name); }
        try { Convert.ToInt32("99999999999", 10); Console.WriteLine("ovf10_big:ok"); }
        catch (Exception e) { Console.WriteLine("ovf10_big:" + e.GetType().Name); }

        // ============ 13. null / empty ===================================
        Console.WriteLine("null2=" + (Convert.ToInt32(null, 2) == 0));
        Console.WriteLine("null8=" + (Convert.ToInt32(null, 8) == 0));
        Console.WriteLine("null16=" + (Convert.ToInt32(null, 16) == 0));
        Console.WriteLine("null10=" + (Convert.ToInt32(null, 10) == 0));
        try { Convert.ToInt32("", 2); Console.WriteLine("empty2:ok"); }
        catch (Exception e) { Console.WriteLine("empty2:" + e.GetType().Name); }
        try { Convert.ToInt32("", 8); Console.WriteLine("empty8:ok"); }
        catch (Exception e) { Console.WriteLine("empty8:" + e.GetType().Name); }
        try { Convert.ToInt32("", 16); Console.WriteLine("empty16:ok"); }
        catch (Exception e) { Console.WriteLine("empty16:" + e.GetType().Name); }

        // ============ 14. custom radix: BOTH sides reject (shared) =======
        try { Convert.ToInt32("10", 0); Console.WriteLine("b0:ok"); }
        catch (Exception e) { Console.WriteLine("b0:" + e.GetType().Name); }
        try { Convert.ToInt32("10", 1); Console.WriteLine("b1:ok"); }
        catch (Exception e) { Console.WriteLine("b1:" + e.GetType().Name); }
        try { Convert.ToInt32("10", 37); Console.WriteLine("b37:ok"); }
        catch (Exception e) { Console.WriteLine("b37:" + e.GetType().Name); }
        try { Convert.ToInt32("10", 100); Console.WriteLine("b100:ok"); }
        catch (Exception e) { Console.WriteLine("b100:" + e.GetType().Name); }
        try { Convert.ToInt32(null, 3); Console.WriteLine("b3null:ok"); }
        catch (Exception e) { Console.WriteLine("b3null:" + e.GetType().Name); }
        try { Convert.ToString(42, 0); Console.WriteLine("ts0:ok"); }
        catch (Exception e) { Console.WriteLine("ts0:" + e.GetType().Name); }
        try { Convert.ToString(42, 37); Console.WriteLine("ts37:ok"); }
        catch (Exception e) { Console.WriteLine("ts37:" + e.GetType().Name); }
    }
}}
