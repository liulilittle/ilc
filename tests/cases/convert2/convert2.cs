//------------------------------------------------------------------------------
// convert2.cs -- Convert domain DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C83, AFTER the convert domain is
// fully wired and convertfull (C74) covers the cross-conversion matrix.
//
// THIS case drives the WIRED radix/BIT-PLANE surface to exhaustion with a
// probe set convertbase/convertfull did NOT use -- the FULL 32-BIT PATTERN
// lattice in bases 2/8/16: alternating bit masks (0x0F0F0F0F / 0xF0F0F0F0 /
// 0x01010101 / 0x00FF00FF / 0xAAAAAAAA), the classic 0x12345678 / 0xDEADBEEF
// markers, byte/word boundary values (0x55, 0xFF00, 0x100), and the two
// sign extremes plus -2 -- rendered as the UNSIGNED uint32 two's-complement
// (bases 2/8/16; base 10 stays signed decimal). Plus the round-trip identity
// (ToString o ToInt32 == value) and CROSS-RADIX identity (the same value
// parsed back from its base-2/8/16 renderings) for every pattern.
//
// Wired surface (MethodMap.Convert.cs -> convert.cpp), all probes stay within
// it:
//   ToString(int, base)   -- icall_convert_to_string_i4_base (2/8/16 render
//                            the value as uint32, no leading zeros; 10 signed)
//   ToInt32(string, base) -- icall_convert_to_int32_string_base (tight parse,
//                            uint32 accumulation for bases 2/8/16)
//   ToInt32(string) / ToInt64(string) / ToDouble(string) -- the string
//                            family (convertfull already drives the lattice)
//
// RECORDED UNWIRED (NOT probed -- they would fall through to the generated-
// name path and abort the C++ build; see GAPS.md D-C2-1..3):
//   * Convert.ChangeType(object, Type) -- no runtime icall
//   * Convert.ToObject(Type, object)    -- no runtime icall (boxed enum
//     requirement, MethodMap.Enum.cs)
//   * Convert.ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64/ToSingle/
//     ToDecimal -- LOUD narrowing rejections (MethodMap.ConvertExtra.cs)
//
// net48-shape discipline (convertbase precedent): every line prints
// "label=True/False"; bases outside {2,8,10,16} throw ArgumentException on
// both sides (convert.cpp IsValidRadix == the net48 set) and are NOT probed.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Convert2Case {
    public static void Main() {
        // ============ 1. base-2 renderings of full 32-bit patterns ============
        Console.WriteLine("b2_0f0f=" + (Convert.ToString(0x0F0F0F0F, 2) == "1111000011110000111100001111"));
        Console.WriteLine("b2_f0f0=" + (Convert.ToString(unchecked((int)0xF0F0F0F0), 2) == "11110000111100001111000011110000"));
        Console.WriteLine("b2_0101=" + (Convert.ToString(0x01010101, 2) == "1000000010000000100000001"));
        Console.WriteLine("b2_ff00ff=" + (Convert.ToString(0x00FF00FF, 2) == "111111110000000011111111"));
        Console.WriteLine("b2_aaaa=" + (Convert.ToString(unchecked((int)0xAAAAAAAA), 2) == "10101010101010101010101010101010"));
        Console.WriteLine("b2_1234=" + (Convert.ToString(0x12345678, 2) == "10010001101000101011001111000"));
        Console.WriteLine("b2_dead=" + (Convert.ToString(unchecked((int)0xDEADBEEF), 2) == "11011110101011011011111011101111"));
        Console.WriteLine("b2_55=" + (Convert.ToString(0x55, 2) == "1010101"));
        Console.WriteLine("b2_ff00=" + (Convert.ToString(0xFF00, 2) == "1111111100000000"));
        Console.WriteLine("b2_100=" + (Convert.ToString(0x100, 2) == "100000000"));
        Console.WriteLine("b2_min=" + (Convert.ToString(int.MinValue, 2) == "10000000000000000000000000000000"));
        Console.WriteLine("b2_neg2=" + (Convert.ToString(-2, 2) == "11111111111111111111111111111110"));
        Console.WriteLine("b2_max=" + (Convert.ToString(int.MaxValue, 2) == "1111111111111111111111111111111"));

        // ============ 2. base-8 renderings of the same patterns ============
        Console.WriteLine("b8_0f0f=" + (Convert.ToString(0x0F0F0F0F, 8) == "1703607417"));
        Console.WriteLine("b8_f0f0=" + (Convert.ToString(unchecked((int)0xF0F0F0F0), 8) == "36074170360"));
        Console.WriteLine("b8_0101=" + (Convert.ToString(0x01010101, 8) == "100200401"));
        Console.WriteLine("b8_ff00ff=" + (Convert.ToString(0x00FF00FF, 8) == "77600377"));
        Console.WriteLine("b8_aaaa=" + (Convert.ToString(unchecked((int)0xAAAAAAAA), 8) == "25252525252"));
        Console.WriteLine("b8_1234=" + (Convert.ToString(0x12345678, 8) == "2215053170"));
        Console.WriteLine("b8_dead=" + (Convert.ToString(unchecked((int)0xDEADBEEF), 8) == "33653337357"));
        Console.WriteLine("b8_55=" + (Convert.ToString(0x55, 8) == "125"));
        Console.WriteLine("b8_ff00=" + (Convert.ToString(0xFF00, 8) == "177400"));
        Console.WriteLine("b8_100=" + (Convert.ToString(0x100, 8) == "400"));
        Console.WriteLine("b8_min=" + (Convert.ToString(int.MinValue, 8) == "20000000000"));
        Console.WriteLine("b8_neg2=" + (Convert.ToString(-2, 8) == "37777777776"));
        Console.WriteLine("b8_max=" + (Convert.ToString(int.MaxValue, 8) == "17777777777"));

        // ============ 3. base-16 renderings (lowercase, uint32) ============
        Console.WriteLine("b16_0f0f=" + (Convert.ToString(0x0F0F0F0F, 16) == "f0f0f0f"));
        Console.WriteLine("b16_f0f0=" + (Convert.ToString(unchecked((int)0xF0F0F0F0), 16) == "f0f0f0f0"));
        Console.WriteLine("b16_0101=" + (Convert.ToString(0x01010101, 16) == "1010101"));
        Console.WriteLine("b16_ff00ff=" + (Convert.ToString(0x00FF00FF, 16) == "ff00ff"));
        Console.WriteLine("b16_aaaa=" + (Convert.ToString(unchecked((int)0xAAAAAAAA), 16) == "aaaaaaaa"));
        Console.WriteLine("b16_1234=" + (Convert.ToString(0x12345678, 16) == "12345678"));
        Console.WriteLine("b16_dead=" + (Convert.ToString(unchecked((int)0xDEADBEEF), 16) == "deadbeef"));
        Console.WriteLine("b16_55=" + (Convert.ToString(0x55, 16) == "55"));
        Console.WriteLine("b16_ff00=" + (Convert.ToString(0xFF00, 16) == "ff00"));
        Console.WriteLine("b16_100=" + (Convert.ToString(0x100, 16) == "100"));
        Console.WriteLine("b16_min=" + (Convert.ToString(int.MinValue, 16) == "80000000"));
        Console.WriteLine("b16_neg2=" + (Convert.ToString(-2, 16) == "fffffffe"));
        Console.WriteLine("b16_max=" + (Convert.ToString(int.MaxValue, 16) == "7fffffff"));

        // ============ 4. round-trip identity: ToString o ToInt32 ============
        Console.WriteLine("rt_0f0f=" + (Convert.ToInt32(Convert.ToString(0x0F0F0F0F, 16), 16) == 0x0F0F0F0F));
        Console.WriteLine("rt_f0f0=" + (Convert.ToInt32(Convert.ToString(unchecked((int)0xF0F0F0F0), 16), 16) == unchecked((int)0xF0F0F0F0)));
        Console.WriteLine("rt_dead=" + (Convert.ToInt32(Convert.ToString(unchecked((int)0xDEADBEEF), 16), 16) == unchecked((int)0xDEADBEEF)));
        Console.WriteLine("rt_1234=" + (Convert.ToInt32(Convert.ToString(0x12345678, 16), 16) == 0x12345678));
        Console.WriteLine("rt_ff00=" + (Convert.ToInt32(Convert.ToString(0xFF00, 16), 16) == 0xFF00));
        Console.WriteLine("rt_min16=" + (Convert.ToInt32(Convert.ToString(int.MinValue, 16), 16) == int.MinValue));
        Console.WriteLine("rt_neg2_8=" + (Convert.ToInt32(Convert.ToString(-2, 8), 8) == -2));
        Console.WriteLine("rt_aaaa_8=" + (Convert.ToInt32(Convert.ToString(unchecked((int)0xAAAAAAAA), 8), 8) == unchecked((int)0xAAAAAAAA)));
        Console.WriteLine("rt_0101_2=" + (Convert.ToInt32(Convert.ToString(0x01010101, 2), 2) == 0x01010101));
        Console.WriteLine("rt_max_2=" + (Convert.ToInt32(Convert.ToString(int.MaxValue, 2), 2) == int.MaxValue));

        // ============ 5. CROSS-RADIX identity: the value survives 2/8/16 ============
        Console.WriteLine("cx_1234=" + (Convert.ToInt32(Convert.ToString(0x12345678, 2), 2)
            == Convert.ToInt32(Convert.ToString(0x12345678, 8), 8)
            && Convert.ToInt32(Convert.ToString(0x12345678, 8), 8)
            == Convert.ToInt32(Convert.ToString(0x12345678, 16), 16)));
        Console.WriteLine("cx_f0f0=" + (Convert.ToInt32(Convert.ToString(unchecked((int)0xF0F0F0F0), 2), 2)
            == Convert.ToInt32(Convert.ToString(unchecked((int)0xF0F0F0F0), 8), 8)
            && Convert.ToInt32(Convert.ToString(unchecked((int)0xF0F0F0F0), 8), 8)
            == unchecked((int)0xF0F0F0F0)));
        Console.WriteLine("cx_dead=" + (Convert.ToInt32(Convert.ToString(unchecked((int)0xDEADBEEF), 2), 2)
            == unchecked((int)0xDEADBEEF)
            && Convert.ToInt32(Convert.ToString(unchecked((int)0xDEADBEEF), 8), 8)
            == unchecked((int)0xDEADBEEF)));
        Console.WriteLine("cx_55=" + (Convert.ToInt32(Convert.ToString(0x55, 2), 2)
            == Convert.ToInt32(Convert.ToString(0x55, 8), 8)
            && Convert.ToInt32(Convert.ToString(0x55, 8), 8)
            == Convert.ToInt32(Convert.ToString(0x55, 16), 16)));
        Console.WriteLine("cx_min=" + (Convert.ToInt32(Convert.ToString(int.MinValue, 2), 2)
            == Convert.ToInt32(Convert.ToString(int.MinValue, 8), 8)
            && Convert.ToInt32(Convert.ToString(int.MinValue, 8), 8)
            == Convert.ToInt32(Convert.ToString(int.MinValue, 16), 16)));

        // ============ 6. base-10 stays signed (radix divergence probe) ============
        Console.WriteLine("t10_neg=" + (Convert.ToString(-252645136, 10) == "-252645136"));
        Console.WriteLine("t10_min=" + (Convert.ToString(int.MinValue, 10) == "-2147483648"));
        Console.WriteLine("t10_aaaa=" + (Convert.ToString(unchecked((int)0xAAAAAAAA), 10) == "-1431655766"));

        // ============ 7. tight-parse round trips (string -> base) ============
        Console.WriteLine("p2_dead=" + (Convert.ToInt32("11011110101011011011111011101111", 2) == unchecked((int)0xDEADBEEF)));
        Console.WriteLine("p8_dead=" + (Convert.ToInt32("33653337357", 8) == unchecked((int)0xDEADBEEF)));
        Console.WriteLine("p16_dead=" + (Convert.ToInt32("deadbeef", 16) == unchecked((int)0xDEADBEEF)));
        Console.WriteLine("p16_DEAD=" + (Convert.ToInt32("DEADBEEF", 16) == unchecked((int)0xDEADBEEF)));
        Console.WriteLine("p2_ff00=" + (Convert.ToInt32("1111111100000000", 2) == 0xFF00));
        Console.WriteLine("p16_ff00ff=" + (Convert.ToInt32("ff00ff", 16) == 0x00FF00FF));
        Console.WriteLine("p8_0101=" + (Convert.ToInt32("100200401", 8) == 0x01010101));
        Console.WriteLine("p16_55=" + (Convert.ToInt32("55", 16) == 0x55));
    }
}}
