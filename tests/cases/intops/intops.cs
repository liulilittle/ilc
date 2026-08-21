//------------------------------------------------------------------------------
// intops.cs -- INT DOMAIN DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C61, after the int domain landed:
// unchecked wrap-around arithmetic (overflow case), checked CONVERSIONS
// (conv.ovf.i4/i8 -> icall_conv_ovf_*, convdeep), checked DIV/REM overflow
// (il2cpp_checked_div/rem, exception.cpp), and the bit-op family
// (bitopcase / bitops2) are all wired.
//
// THIS case drives the OVERFLOW + CHECKED + BIT-OP COMBINATIONS on top of
// the wired layer:
//
//   1) UNCHECKED overflow wrap: int32 add/sub/mul/neg at the Max/Min
//      boundaries wrap (the default unchecked context) -- MaxValue+1 ==
//      MinValue, MinValue-1 == MaxValue, MaxValue*2 == -2, neg(MinValue) ==
//      MinValue, and chained wrap expressions.
//   2) CHECKED CONVERSIONS (conv.ovf.i4): long -> int32 range checks via
//      icall_conv_ovf_i4_i8 -- in-range pass, 2^31 / -2^31-1 throw
//      OverflowException, boundary MinValue legal. Checked int32 -> int64
//      (conv.ovf.i8) never overflows.
//   3) CHECKED DIVISION/REMAINDER overflow (il2cpp_checked_div/rem):
//      int.MinValue / -1 and int.MinValue % -1 throw OverflowException
//      (C++ would be SIGFPE UB); x / 0 throws DivideByZeroException.
//   4) BIT-OP combos: runtime shift counts with &31 masking, arithmetic vs
//      logical right shift, mask extraction / bitfield pack-unpack, x&(x-1)
//      lowest-bit clear, x&-x isolation, ~ complement round trips, and
//      combined arith+bit chains (shift->mask->compare).
//   5) OVERFLOW + BIT combination: wrap-around arithmetic feeding a mask/
//      shift result (the two domains compose on the same int32 slot).
//   6) CHECKED add/sub/mul overflow (D-INT-1, fixed): StackFold folds
//      add.ovf/sub.ovf/mul.ovf (signed + _un unsigned) to the runtime
//      il2cpp_checked_{add,sub,mul}[_un] helpers (exception.cpp), which
//      throw OverflowException on wrap (net48 semantics). int32/uint32
//      in-range + boundary + overflow probes.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class IntOps {
    public static void Main() {
        // ============ 1. unchecked overflow wrap (default context) ============
        int a = int.MaxValue;
        Console.WriteLine("o1_max_add1=" + (a + 1 == int.MinValue));
        int b = int.MinValue;
        Console.WriteLine("o2_min_sub1=" + (b - 1 == int.MaxValue));
        int c = int.MaxValue;
        Console.WriteLine("o3_max_mul2=" + (c * 2 == -2));
        int d = int.MinValue;
        Console.WriteLine("o4_min_mul2=" + (d * 2 == 0));
        int e = int.MinValue;
        Console.WriteLine("o5_neg_min=" + (-e == int.MinValue));
        Console.WriteLine("o6_max_mul_min1=" + (c * -1 == -2147483647));
        Console.WriteLine("o7_min_mul_min1=" + (d * -1 == int.MinValue)); // 2^31 wrap
        // chained wrap: MaxValue + 2 -> MinValue+1.
        Console.WriteLine("o8_chain=" + (a + 2 == -2147483647));

        // ============ 2. checked conversions (conv.ovf.i4) ============
        long l1 = 2147483648L;
        bool c1 = false;
        try { int z = checked((int)l1); Console.WriteLine("x1 " + z); }
        catch (OverflowException) { c1 = true; }
        Console.WriteLine("x1_2p31=" + c1);
        long l2 = -2147483649L;
        bool c2 = false;
        try { int z = checked((int)l2); Console.WriteLine("x2 " + z); }
        catch (OverflowException) { c2 = true; }
        Console.WriteLine("x2_neg2p31_1=" + c2);
        long l3 = 42L;
        Console.WriteLine("x3_inrange=" + (checked((int)l3) == 42));
        long l4 = int.MinValue;
        Console.WriteLine("x4_boundary=" + (checked((int)l4) == int.MinValue));
        long l5 = int.MaxValue;
        Console.WriteLine("x5_max=" + (checked((int)l5) == int.MaxValue));

        // ============ 3. checked division / remainder overflow ============
        int minv = int.MinValue;
        bool dv = false;
        try { int z = minv / -1; Console.WriteLine("d1 " + z); }
        catch (OverflowException) { dv = true; }
        Console.WriteLine("d1_min_div_neg1=" + dv);
        bool rm = false;
        try { int z = minv % -1; Console.WriteLine("d2 " + z); }
        catch (OverflowException) { rm = true; }
        Console.WriteLine("d2_min_rem_neg1=" + rm);
        bool dz = false;
        int zero1 = 0;
        try { int z = 5 / zero1; Console.WriteLine("d3 " + z); }
        catch (DivideByZeroException) { dz = true; }
        Console.WriteLine("d3_div0=" + dz);
        bool dzr = false;
        int zero2 = 0;
        try { int z = 5 % zero2; Console.WriteLine("d4 " + z); }
        catch (DivideByZeroException) { dzr = true; }
        Console.WriteLine("d4_rem0=" + dzr);
        Console.WriteLine("d5_ok=" + (minv / 1 == int.MinValue));
        Console.WriteLine("d6_sign=" + (-7 / 2 == -3 && -7 % 2 == -1));

        // ============ 4. bit-op combos ============
        int one = 1;
        int s0 = 0, s1 = 1, s4 = 4, s16 = 16, s31 = 31, s32 = 32, s33 = 33, sn1 = -1;
        Console.WriteLine("b1_shl=" + ((one << s16) == 65536));
        Console.WriteLine("b2_mask31=" + ((one << s32) == 1));   // 32 & 31 = 0
        Console.WriteLine("b3_mask33=" + ((one << s33) == 2));   // 33 & 31 = 1
        Console.WriteLine("b4_maskneg1=" + ((one << sn1) == int.MinValue)); // -1 & 31 = 31
        int neg8 = -8;
        Console.WriteLine("b5_arshift=" + ((neg8 >> s1) == -4));
        Console.WriteLine("b6_arshift_all=" + ((neg8 >> s31) == -1));
        uint uf = 0xF0000000u;
        Console.WriteLine("b7_lshift=" + ((uf >> s4) == 0x0F000000u));
        int packed = 0x12345678;
        Console.WriteLine("b8_mask=" + ((packed & 0xFF) == 0x78));
        Console.WriteLine("b9_extract=" + (((packed >> 8) & 0xFF) == 0x56));
        Console.WriteLine("b10_nibble=" + (((packed >> 4) & 0xF) == 0x7));
        int v12 = 12;
        Console.WriteLine("b11_clear_lsb=" + ((v12 & (v12 - 1)) == 8));
        Console.WriteLine("b12_isolate=" + ((v12 & -v12) == 4));
        Console.WriteLine("b13_complement=" + (~0 == -1));
        Console.WriteLine("b14_dbl_compl=" + (~(~12) == 12));
        Console.WriteLine("b15_xor_self=" + ((v12 ^ v12) == 0));
        Console.WriteLine("b16_mask_n=" + (((one << 5) - 1) == 31));
        int n5 = 5;
        Console.WriteLine("b17_mask_n_runtime=" + (((one << n5) - 1) == 31));

        // ============ 5. overflow + bit combination ============
        int ov1 = int.MaxValue;
        Console.WriteLine("m1_wrap_xor=" + ((ov1 + 1) == int.MinValue));
        int wrapped = ov1 + 1;                     // int.MinValue (wraps)
        Console.WriteLine("m2_wrap_shl=" + ((wrapped >> s4) == -134217728));
        Console.WriteLine("m3_wrap_mask=" + ((wrapped & 0x0FFFFFFF) == 0));
        int neg1 = -1;
        Console.WriteLine("m4_neg_shift=" + ((neg1 << s4) == -16));
        Console.WriteLine("m5_neg_mask=" + ((neg1 & 0xFFFF0000) == unchecked((int)0xFFFF0000)));
        // shift-mask chain on a packed value.
        int field = 0;
        int bit3 = 3;
        field |= (1 << bit3);
        Console.WriteLine("m6_set=" + ((field & (1 << bit3)) != 0));
        field &= ~(1 << bit3);
        Console.WriteLine("m7_clear=" + ((field & (1 << bit3)) == 0));
        field ^= (1 << bit3);
        Console.WriteLine("m8_toggle=" + ((field & (1 << bit3)) != 0));

        // ============ 6. checked add/sub/mul overflow (D-INT-1) ============
        int cadd = int.MaxValue;
        bool c1ov = false;
        try { int z = checked(cadd + 1); Console.WriteLine("c1 " + z); }
        catch (OverflowException) { c1ov = true; }
        Console.WriteLine("c1_add_max_ovf=" + c1ov);
        int csub = int.MinValue;
        bool c2ov = false;
        try { int z = checked(csub - 1); Console.WriteLine("c2 " + z); }
        catch (OverflowException) { c2ov = true; }
        Console.WriteLine("c2_sub_min_ovf=" + c2ov);
        int cmul = int.MaxValue;
        bool c3ov = false;
        try { int z = checked(cmul * 2); Console.WriteLine("c3 " + z); }
        catch (OverflowException) { c3ov = true; }
        Console.WriteLine("c3_mul_max2_ovf=" + c3ov);
        bool c3b = false;
        try { int z = checked(csub * -1); Console.WriteLine("c3b " + z); }
        catch (OverflowException) { c3b = true; }
        Console.WriteLine("c3b_mul_minxneg1_ovf=" + c3b);
        Console.WriteLine("c4_add_ok=" + (checked(cadd + 0) == int.MaxValue));
        Console.WriteLine("c5_add_boundary=" + (checked(csub + (int.MaxValue - 1)) == -2));
        Console.WriteLine("c6_sub_ok=" + (checked(csub - 0) == int.MinValue));
        Console.WriteLine("c7_mul_ok=" + (checked(cmul * 1) == int.MaxValue));
        // uint (add.ovf.un / sub.ovf.un / mul.ovf.un -> _un helpers).
        uint ubig = uint.MaxValue;
        bool cu1 = false;
        try { uint z = checked(ubig + 1u); Console.WriteLine("c8 " + z); }
        catch (OverflowException) { cu1 = true; }
        Console.WriteLine("c8_add_un_ovf=" + cu1);
        Console.WriteLine("c9_add_un_ok=" + (checked(ubig + 0u) == uint.MaxValue));
        uint uzero = 0u;
        bool cu2 = false;
        try { uint z = checked(uzero - 1u); Console.WriteLine("c10 " + z); }
        catch (OverflowException) { cu2 = true; }
        Console.WriteLine("c10_sub_un_ovf=" + cu2);
        Console.WriteLine("c11_sub_un_ok=" + (checked(ubig - 1u) == 4294967294u));
        uint umul = 65536u;
        bool cu3 = false;
        try { uint z = checked(umul * umul); Console.WriteLine("c12 " + z); }
        catch (OverflowException) { cu3 = true; }
        Console.WriteLine("c12_mul_un_ovf=" + cu3);
        uint umul2 = 65535u;
        Console.WriteLine("c13_mul_un_ok=" + (checked(umul2 * umul2) == 4294836225u));

        // ============ 7. checked add/sub/mul on int64/uint64 (i64ck-b23) ============
        // add.ovf/sub.ovf/mul.ovf on long operands must fold to the WIDTH-aware
        // runtime helper (il2cpp_checked_*_64, exception.cpp) -- the D-INT-1
        // int32-only helpers LOUD-rejected 64-bit operands. net48 semantics:
        // in-range passes, wrap/overflow throws OverflowException.
        long la = long.MaxValue;
        bool cl1ov = false;
        try { long z = checked(la + 1); Console.WriteLine("l1 " + z); }
        catch (OverflowException) { cl1ov = true; }
        Console.WriteLine("l1_add_max_ovf=" + cl1ov);
        long lb = long.MinValue;
        bool cl2ov = false;
        try { long z = checked(lb - 1); Console.WriteLine("l2 " + z); }
        catch (OverflowException) { cl2ov = true; }
        Console.WriteLine("l2_sub_min_ovf=" + cl2ov);
        long lc = long.MaxValue;
        bool cl3ov = false;
        try { long z = checked(lc * 2); Console.WriteLine("l3 " + z); }
        catch (OverflowException) { cl3ov = true; }
        Console.WriteLine("l3_mul_max2_ovf=" + cl3ov);
        bool cl3bov = false;
        try { long z = checked(lb * -1); Console.WriteLine("l3b " + z); }
        catch (OverflowException) { cl3bov = true; }
        Console.WriteLine("l3b_mul_minxneg1_ovf=" + cl3bov);
        Console.WriteLine("l4_add_ok=" + (checked(la + 0) == long.MaxValue));
        Console.WriteLine("l5_add_boundary=" + (checked(lb + (long.MaxValue - 1)) == -2));
        Console.WriteLine("l6_sub_ok=" + (checked(lb - 0) == long.MinValue));
        Console.WriteLine("l7_mul_ok=" + (checked(lc * 1) == long.MaxValue));
        // uint64 checked (add.ovf.un / sub.ovf.un / mul.ovf.un -> _un_64 helpers).
        ulong lubig = ulong.MaxValue;
        bool lu1 = false;
        try { ulong z = checked(lubig + 1UL); Console.WriteLine("l8 " + z); }
        catch (OverflowException) { lu1 = true; }
        Console.WriteLine("l8_add_un_ovf=" + lu1);
        Console.WriteLine("l9_add_un_ok=" + (checked(lubig + 0UL) == ulong.MaxValue));
        ulong luzero = 0UL;
        bool lu2 = false;
        try { ulong z = checked(luzero - 1UL); Console.WriteLine("l10 " + z); }
        catch (OverflowException) { lu2 = true; }
        Console.WriteLine("l10_sub_un_ovf=" + lu2);
        Console.WriteLine("l11_sub_un_ok=" + (checked(lubig - 1UL) == 18446744073709551614UL));
        ulong lumul = 4294967296UL;
        bool lu3 = false;
        try { ulong z = checked(lumul * lumul); Console.WriteLine("l12 " + z); }
        catch (OverflowException) { lu3 = true; }
        Console.WriteLine("l12_mul_un_ovf=" + lu3);
        ulong lumul2 = 4294967295UL;
        Console.WriteLine("l13_mul_un_ok=" + (checked(lumul2 * lumul2) == 18446744065119617025UL));
    }
}}
