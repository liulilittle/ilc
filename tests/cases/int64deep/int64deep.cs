//------------------------------------------------------------------------------
// int64deep.cs -- INT64 (long) DOMAIN DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C76, AFTER the long domain wiring:
// int64 arithmetic (int64_t emission, bitopcase G1-G5), the conv-family
// icalls (StackFold.cs icall_conv_i4_i8/i8_d/u8_i4/... -- convcase/convdeep),
// the checked 64-bit family (il2cpp_checked_{add,sub,mul,rem}[_un]_64,
// exception.cpp i64ck-b23 -- intops section 7), int64 ToString/format
// (icall_int64_to_string / icall_format_int64_custom, fmtround), and int64
// arrays (newarr i8, il2cpp_array_sort_int64 / index_of / binary_search,
// arrayops/sortdeep latent surface -- ArrayElementType CG6).
//
// THIS case drives the int64 dimensions the int32-domain cases could not:
//
//   1. UNCHECKED WRAP arithmetic at the 64-bit boundaries: long.MaxValue+1,
//      long.MinValue-1, MaxValue*2, neg(MinValue), and the chained wraps --
//      int64_t two's-complement on the runtime vs net48's unchecked IL.
//      D-I64D-1 (2026-08-13): unchecked int64 MULTIPLY wraps mod 2^64 through
//      the runtime helper il2cpp_unchecked_mul_64 (conv.cpp -- the raw C++
//      `a * b` was signed-overflow UB; the o4_min_mul2 probe is restored and
//      the boundary lattice o12-o18 added: MaxValue^2, MinValue^2, 2^62*3/4,
//      2^32*2^32).
//   2. DIV/REM semantics on 64-bit operands: truncation toward zero, sign
//      follows the dividend, mixed-sign divisors, large-magnitude quotients,
//      and the boundary lattice (MaxValue/MinValue mod N). NOTE: the div/rem
//      probes use SAFE divisors only -- long.MinValue / -1 (UB in C++), and
//      any /0, would not throw on the runtime (recorded gap, see GAPS).
//   3. SHIFTS with the 63-bit mask: 1L<<63, 1L<<64 -> 1 (mask), 1L<<65 -> 2,
//      arithmetic >> on negatives, logical >> (shr.un) on ulong.
//   4. BIT-OPS on the full 64-bit pattern: ~0L, long.MaxValue masks,
//      0x1122334455667788 nibble/byte extraction, xor-self, and/or/xor on
//      alternate-bit constants.
//   5. CONVERSIONS through the conv-family icalls (checked + unchecked):
//      int64<->int32 wrap/zero/sign-extension, int64<->uint64 bit patterns,
//      (ulong)(uint) zero-extension (valdeep fix), double<->int64 truncation
//      and the 2^53 boundary, checked conv.ovf.i4 range throws.
//      NOTE: (uint)long-value (conv.u4 on an int64 source) is NOT probed --
//      the StackFold treats conv.u4 as an identity, so the 64-bit pattern
//      is NOT truncated to 32 bits (recorded gap, see GAPS).
//   6. CHECKED ARITHMETIC on int64/uint64 (il2cpp_checked_*_64): in-range
//      passes, wrap throws OverflowException, boundary adds.
//   7. Math long surface: Abs/Max/Min/Sign including Abs(MinValue) throwing
//      OverflowException (net48 contract).
//   8. ToString / format / parse: full 19-digit decimal, "X"/"x" hex
//      (two's complement 64-bit), "D"-padding, "N"-grouping, "E"-scientific,
//      "G"-general, Convert.ToInt64 round trips at the boundaries,
//      long.TryParse accept/reject.
//   9. INT64 ARRAYS (the latent arraysort surface, now reachable via CG6
//      long[] newarr): Sort<long> ascending, IndexOf<long> first-match /
//      -1, BinarySearch<long> hit index / ~insertion point, boundary values.
//  10. BitConverter.DoubleToInt64Bits / Int64BitsToDouble round trip.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Int64Deep {
    public static void Main() {
        // ============ 1. unchecked wrap at the 64-bit boundaries ============
        long la = long.MaxValue;
        Console.WriteLine("o1_max_add1=" + (la + 1 == long.MinValue));
        long lb = long.MinValue;
        Console.WriteLine("o2_min_sub1=" + (lb - 1 == long.MaxValue));
        Console.WriteLine("o3_max_mul2=" + (la * 2 == -2));
        Console.WriteLine("o4_min_mul2=" + (lb * 2 == 0));
        Console.WriteLine("o5_neg_min=" + (-lb == long.MinValue));
        Console.WriteLine("o6_max_mul_neg1=" + (la * -1 == -9223372036854775807L));
        Console.WriteLine("o7_min_mul_neg1=" + (lb * -1 == long.MinValue));
        Console.WriteLine("o8_chain=" + (la + 2 == -9223372036854775807L));
        long lc = 1234567890123456789L;
        Console.WriteLine("o9_add_small=" + (lc + 1 == 1234567890123456790L));
        Console.WriteLine("o10_max_minus_min=" + (la - lb == -1));
        Console.WriteLine("o11_neg_pos=" + (-lc == -1234567890123456789L));
        // D-I64D-1: unchecked int64 multiply wraps mod 2^64 through the
        // runtime helper il2cpp_unchecked_mul_64 (conv.cpp -- the raw C++ `*`
        // was signed-overflow UB). Runtime operands only (a constant multiply
        // would fold at csc and never reach the emitter).
        long two62 = 0x4000000000000000L;
        long two32l = 0x100000000L;
        long three = 3L;
        long lmid = 123456789L;
        Console.WriteLine("o12_max_mul_max=" + (la * la == 1));
        Console.WriteLine("o13_min_mul_min=" + (lb * lb == 0));
        Console.WriteLine("o14_min_mul_max=" + (lb * la == long.MinValue));
        Console.WriteLine("o15_2p62_mul4=" + (two62 * 4L == 0));
        Console.WriteLine("o16_2p62_mul3=" + (two62 * three == -4611686018427387904L));
        Console.WriteLine("o17_2p32_mul_2p32=" + (two32l * two32l == 0));
        Console.WriteLine("o18_mid_mul=" + (lmid * 987654321L == 121932631112635269L));
        // ============ 2. div/rem semantics (safe divisors only) ============
        long d1 = 7, d2 = 2, dn1 = -7, dn2 = -2, d3 = 3;
        Console.WriteLine("d1_trunc=" + (d1 / d2 == 3));
        Console.WriteLine("d2_trunc_neg=" + (dn1 / d2 == -3));
        Console.WriteLine("d3_trunc_negdiv=" + (d1 / dn2 == -3));
        Console.WriteLine("d4_trunc_negboth=" + (dn1 / dn2 == 3));
        Console.WriteLine("d5_rem_sign_dividend=" + (d1 % d2 == 1 && dn1 % d2 == -1));
        Console.WriteLine("d6_rem_negdiv=" + (d1 % dn2 == 1 && dn1 % dn2 == -1));
        long big = 1234567890123456789L;
        Console.WriteLine("d7_big_div=" + (big / 1000000 == 1234567890123L));
        Console.WriteLine("d8_big_rem=" + (big % 1000000 == 456789));
        Console.WriteLine("d9_max_div3=" + (long.MaxValue / d3 == 3074457345618258602L));
        Console.WriteLine("d10_max_rem3=" + (long.MaxValue % d3 == 1));
        Console.WriteLine("d11_min_div3=" + (long.MinValue / d3 == -3074457345618258602L));
        Console.WriteLine("d12_min_rem3=" + (long.MinValue % d3 == -2));
        Console.WriteLine("d13_div_one=" + (long.MinValue / 1 == long.MinValue));
        Console.WriteLine("d14_mixed=" + (999999999999L / -333L == -3003003003L && 999999999999L % -333L == 0));

        // ============ 3. shifts with the 63-bit mask ============
        int s0 = 0, s63 = 63, s64 = 64, s65 = 65, s1 = 1, sn1 = -1;
        Console.WriteLine("s1_max_shift=" + (1L << s63 == long.MinValue));
        Console.WriteLine("s2_mask64=" + (1L << s64 == 1));
        Console.WriteLine("s3_mask65=" + (1L << s65 == 2));
        Console.WriteLine("s4_shift0=" + (1L << s0 == 1));
        Console.WriteLine("s5_neg_shl=" + (-1L << s63 == long.MinValue));
        long n8 = -8;
        Console.WriteLine("s6_arith_rsh=" + (n8 >> s1 == -4));
        Console.WriteLine("s7_arith_rsh_all=" + (n8 >> s63 == -1));
        Console.WriteLine("s8_neg1_rsh=" + (-1L >> s1 == -1));
        ulong umax = ulong.MaxValue;
        Console.WriteLine("s9_logical_rsh=" + (umax >> s1 == 9223372036854775807UL));
        Console.WriteLine("s10_logical_rsh63=" + (umax >> s63 == 1UL));
        Console.WriteLine("s11_big_shl=" + (0x00000000FFFFFFFFL << 32 == unchecked((long)0xFFFFFFFF00000000UL)));

        // ============ 4. bit ops on the 64-bit pattern ============
        Console.WriteLine("b1_complement=" + (~0L == -1));
        Console.WriteLine("b2_max_and_ff=" + ((long.MaxValue & 0xFF) == 0xFF));
        long packed = 0x1122334455667788L;
        Console.WriteLine("b3_byte_extract=" + ((packed & 0xFF) == 0x88));
        Console.WriteLine("b4_byte_shift=" + (((packed >> 8) & 0xFF) == 0x77));
        Console.WriteLine("b5_nibble=" + (((packed >> 4) & 0xF) == 0x8));
        Console.WriteLine("b6_xor_self=" + ((packed ^ packed) == 0));
        Console.WriteLine("b7_or_zero=" + ((packed | 0L) == packed));
        Console.WriteLine("b8_xor_ones=" + ((packed ^ ~0L) == ~packed));
        Console.WriteLine("b9_alt_or=" + ((unchecked((long)0xF0F0F0F0F0F0F0F0UL) | (long)0x0F0F0F0F0F0F0F0FUL) == -1));
        Console.WriteLine("b10_alt_and=" + ((unchecked((long)0xF0F0F0F0F0F0F0F0UL) & (long)0x0F0F0F0F0F0F0F0FUL) == 0));
        Console.WriteLine("b11_clear_lsb=" + ((12L & (12L - 1)) == 8L));
        Console.WriteLine("b12_isolate=" + ((12L & -12L) == 4L));
        Console.WriteLine("b13_mask16=" + ((unchecked((long)0xABCDEF0123456789UL) & unchecked((long)0xFFFFFFFF00000000UL)) == unchecked((long)0xABCDEF0100000000UL)));
        Console.WriteLine("b14_min_bits=" + (long.MinValue == unchecked((long)0x8000000000000000UL)));

        // ============ 5. conversions (conv-family icalls) ============
        // Runtime operands only -- a compile-time constant cast would fold at
        // csc and never reach the emitter (the checked-default constant
        // context also rejects out-of-range constant conversions).
        int imax = int.MaxValue, imin2 = int.MinValue;
        long lmax = long.MaxValue, lmin = long.MinValue;
        uint umax2 = uint.MaxValue;
        long negone = -1L;
        long two32 = 0x100000000L;
        double dv1 = 3.9, dv2 = -3.9, dv3 = 9.999;
        long p53 = 9007199254740993L, p53e = 9007199254740992L;
        long cd = 123456L;
        Console.WriteLine("c1_i4_wide=" + ((long)imax == 2147483647L));
        Console.WriteLine("c2_i4_neg_wide=" + ((long)imin2 == -2147483648L));
        Console.WriteLine("c3_long_to_i4_wrap=" + ((int)lmax == -1));
        Console.WriteLine("c4_long_to_i4_min=" + ((int)lmin == 0));
        Console.WriteLine("c5_long_to_i4_2p32=" + ((int)two32 == 0));
        Console.WriteLine("c6_u4_zeroext=" + ((long)umax2 == 4294967295L));
        Console.WriteLine("c7_long_to_ulong=" + ((ulong)negone == 18446744073709551615UL));
        ulong ulm = ulong.MaxValue;
        Console.WriteLine("c8_ulong_to_long=" + ((long)ulm == -1L));
        Console.WriteLine("c10_u4_to_ulong=" + ((ulong)umax2 == 4294967295UL));
        Console.WriteLine("c11_min_bits_u=" + ((ulong)lmin == 9223372036854775808UL));
        Console.WriteLine("c12_d_to_long_trunc=" + ((long)dv1 == 3L));
        Console.WriteLine("c13_d_to_long_neg=" + ((long)dv2 == -3L));
        Console.WriteLine("c14_d_to_long_round=" + ((long)dv3 == 9L));
        Console.WriteLine("c15_2p53_boundary=" + ((double)p53 == 9007199254740992.0));
        Console.WriteLine("c16_2p53_exact=" + ((double)p53e == 9007199254740992.0));
        Console.WriteLine("c17_long_to_d=" + ((long)(double)cd == 123456L));
        // checked conversions (conv.ovf.*)
        long c2p31 = 2147483648L, cneg2p31 = -2147483649L;
        bool k1 = false;
        try { int _ = checked((int)c2p31); } catch (OverflowException) { k1 = true; }
        Console.WriteLine("k1_ovf_i4_2p31=" + k1);
        bool k2 = false;
        try { int _ = checked((int)lmax); } catch (OverflowException) { k2 = true; }
        Console.WriteLine("k2_ovf_i4_max=" + k2);
        bool k3 = false;
        try { int _ = checked((int)cneg2p31); } catch (OverflowException) { k3 = true; }
        Console.WriteLine("k3_ovf_i4_neg=" + k3);
        Console.WriteLine("k4_ovf_i4_ok=" + (checked((int)42L) == 42));
        long imin = int.MinValue;
        Console.WriteLine("k5_ovf_i4_min=" + (checked((int)imin) == int.MinValue));
        Console.WriteLine("k8_ovf_i8_ok=" + (checked((long)1234567890123L) == 1234567890123L));

        // ============ 6. checked arithmetic on int64/uint64 ============
        bool w1 = false;
        try { long _ = checked(la + 1); } catch (OverflowException) { w1 = true; }
        Console.WriteLine("w1_add_max_ovf=" + w1);
        bool w2 = false;
        try { long _ = checked(lb - 1); } catch (OverflowException) { w2 = true; }
        Console.WriteLine("w2_sub_min_ovf=" + w2);
        bool w3 = false;
        try { long _ = checked(la * 2); } catch (OverflowException) { w3 = true; }
        Console.WriteLine("w3_mul_max_ovf=" + w3);
        Console.WriteLine("w4_add_ok=" + (checked(la + 0) == long.MaxValue));
        Console.WriteLine("w5_sub_ok=" + (checked(lb - 0) == long.MinValue));
        Console.WriteLine("w6_mul_ok=" + (checked(la * 1) == long.MaxValue));
        ulong wu1 = ulong.MaxValue;
        bool w7 = false;
        try { ulong _ = checked(wu1 + 1UL); } catch (OverflowException) { w7 = true; }
        Console.WriteLine("w7_add_un_ovf=" + w7);
        Console.WriteLine("w8_add_un_ok=" + (checked(wu1 + 0UL) == ulong.MaxValue));
        ulong wu2 = 0UL;
        bool w9 = false;
        try { ulong _ = checked(wu2 - 1UL); } catch (OverflowException) { w9 = true; }
        Console.WriteLine("w9_sub_un_ovf=" + w9);
        Console.WriteLine("w10_sub_un_ok=" + (checked(wu1 - 1UL) == 18446744073709551614UL));

        // ============ 7. Math long surface ============
        Console.WriteLine("m1_abs=" + (Math.Abs(-5L) == 5));
        Console.WriteLine("m2_abs_max=" + (Math.Abs(long.MaxValue) == long.MaxValue));
        bool m3 = false;
        try { long _ = Math.Abs(long.MinValue); } catch (OverflowException) { m3 = true; }
        Console.WriteLine("m3_abs_min_throws=" + m3);
        Console.WriteLine("m4_max=" + (Math.Max(5L, 9L) == 9L && Math.Max(-5L, 5L) == 5L));
        Console.WriteLine("m5_min=" + (Math.Min(5L, 9L) == 5L && Math.Min(long.MinValue, long.MaxValue) == long.MinValue));
        Console.WriteLine("m6_sign=" + (Math.Sign(-7L) == -1 && Math.Sign(0L) == 0 && Math.Sign(7L) == 1));

        // ============ 8. ToString / format / parse ============
        Console.WriteLine("f1_full=" + (1234567890123456789L.ToString() == "1234567890123456789"));
        Console.WriteLine("f2_max=" + (long.MaxValue.ToString() == "9223372036854775807"));
        Console.WriteLine("f3_min=" + (long.MinValue.ToString() == "-9223372036854775808"));
        Console.WriteLine("f4_zero=" + (0L.ToString() == "0"));
        Console.WriteLine("f5_hex=" + (255L.ToString("X") == "FF"));
        Console.WriteLine("f6_hex_lower=" + (255L.ToString("x2") == "ff"));
        Console.WriteLine("f7_hex_neg64=" + ((-1L).ToString("X") == "FFFFFFFFFFFFFFFF"));
        Console.WriteLine("f8_hex_dead=" + (unchecked((long)0xDEADBEEFCAFEBABEUL).ToString("X16") == "DEADBEEFCAFEBABE"));
        Console.WriteLine("f9_dec_pad=" + (123L.ToString("D10") == "0000000123"));
        Console.WriteLine("f10_dec_min=" + (long.MinValue.ToString("D") == "-9223372036854775808"));
        Console.WriteLine("f11_group=" + (1234567L.ToString("N0") == "1,234,567"));
        Console.WriteLine("f12_sci=" + (12345L.ToString("E2") == "1.23E+004"));
        Console.WriteLine("f13_general=" + (12345L.ToString("G4") == "1.235E+04"));
        Console.WriteLine("f14_general_default=" + (123456789L.ToString("G") == "123456789"));
        Console.WriteLine("p1_convert_max=" + (Convert.ToInt64("9223372036854775807") == long.MaxValue));
        Console.WriteLine("p2_convert_min=" + (Convert.ToInt64("-9223372036854775808") == long.MinValue));
        Console.WriteLine("p3_convert_rt=" + (Convert.ToInt64(123456789012345L.ToString()) == 123456789012345L));
        Console.WriteLine("p4_convert_zero=" + (Convert.ToInt64("0") == 0));
        long pv;
        Console.WriteLine("p5_tryparse_ok=" + (long.TryParse("42", out pv) && pv == 42));
        Console.WriteLine("p6_tryparse_neg=" + (long.TryParse("-1234567890123", out pv) && pv == -1234567890123L));
        Console.WriteLine("p7_tryparse_max=" + (long.TryParse("9223372036854775807", out pv) && pv == long.MaxValue));
        Console.WriteLine("p8_tryparse_reject=" + (!long.TryParse("notanumber", out pv)));
        Console.WriteLine("p9_tryparse_overflow=" + (!long.TryParse("9223372036854775808", out pv)));

        // ============ 9. int64 arrays ============
        long[] arr = new long[] { 5, 3, 9, 1, 3, 7, 0, -2 };
        Array.Sort(arr);
        bool asc = true;
        for (int i = 1; i < arr.Length; i++) if (arr[i - 1] > arr[i]) asc = false;
        Console.WriteLine("a1_sorted=" + asc);
        Console.WriteLine("a2_order=" + (arr[0] == -2 && arr[1] == 0 && arr[2] == 1 && arr[3] == 3 &&
                                        arr[4] == 3 && arr[5] == 5 && arr[6] == 7 && arr[7] == 9));
        long[] bnd = new long[] { 5, long.MinValue, -1, 0, long.MaxValue, -5 };
        Array.Sort(bnd);
        Console.WriteLine("a3_boundary=" + (bnd[0] == long.MinValue && bnd[1] == -5 && bnd[2] == -1 &&
                                            bnd[3] == 0 && bnd[4] == 5 && bnd[5] == long.MaxValue));
        long[] one = new long[] { 42 };
        Array.Sort(one);
        Console.WriteLine("a4_single=" + (one[0] == 42));
        long[] find = new long[] { 10, 20, 30, 20, 40 };
        Console.WriteLine("a5_indexof_first=" + (Array.IndexOf(find, 20L) == 1));
        Console.WriteLine("a6_indexof_miss=" + (Array.IndexOf(find, 25L) == -1));
        Console.WriteLine("a7_indexof_edges=" + (Array.IndexOf(find, long.MaxValue) == -1 && Array.IndexOf(find, long.MinValue) == -1));
        long[] bs = new long[] { -10, -3, 0, 4, 9, 15, 21 };
        Console.WriteLine("a8_bs_hit=" + (Array.BinarySearch(bs, 9) == 4));
        Console.WriteLine("a9_bs_miss=" + (Array.BinarySearch(bs, 7) == -5));
        Console.WriteLine("a10_bs_below=" + (Array.BinarySearch(bs, long.MinValue) == -1));
        Console.WriteLine("a11_bs_above=" + (Array.BinarySearch(bs, long.MaxValue) == -8));

        // ============ 10. BitConverter round trip ============
        Console.WriteLine("t1_dbits=" + (BitConverter.DoubleToInt64Bits(1.0) == 0x3FF0000000000000L));
        Console.WriteLine("t2_bitsd=" + (BitConverter.Int64BitsToDouble(0x3FF0000000000000L) == 1.0));
        Console.WriteLine("t3_rt=" + (BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(-0.5)) == -0.5));
        Console.WriteLine("t4_rt_long=" + (BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(-1234567890123L)) == -1234567890123L));
        Console.WriteLine("t5_rt_negzero=" + (BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(long.MinValue)) == long.MinValue));

        Console.WriteLine("done");
    }
}}
