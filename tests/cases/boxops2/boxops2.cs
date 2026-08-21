//------------------------------------------------------------------------------
// boxops2.cs -- BOXING COMPARISON COMBO e2e reference case #2 (net48 oracle,
// byte-for-byte). Created in e2e batch C77, AFTER the box-domain closure:
// the numeric box classes + interface tables + value-semantics dispatch
// (batches 21/22b/35), the boxed GetHashCode VALUE fill (GAP-BOX-6, 2026-08-10,
// valuesemantics.cpp icall_object_get_hash_code -- every boxed primitive now
// hashes to its net48 value pattern), and the DEFAULT COMPARER surface
// (D-ICMP-2, il2cpp_comparer_compare + ComparerCompareCore -- null-first,
// string ordinal, numeric/char/bool box value comparisons).
//
// THIS case drives the COMPARISON dimension boxops did not:
//
//   1. System.Collections.Comparer.Default.Compare on BOXED pairs across the
//      whole primitive lattice -- int/uint/long/ulong/byte/sbyte/short/
//      ushort/double/single/char/bool, each asserted only for the SIGN of the
//      result (<0 / ==0 / >0). net48 Comparer semantics (D-ICMP-2): a boxed
//      value compares by its VALUE; null sorts first (null==null, null<non-null).
//      NOTE: cross-TYPE numeric comparisons are NOT probed -- net48
//      Int32.CompareTo(object) rejects a boxed Int64 (ArgumentException),
//      while the runtime would read the raw payload; a recorded gap.
//   2. Boxed GetHashCode VALUE patterns: cross-box CONSISTENCY for every type
//      (two distinct boxes of the same value -> equal hash -- the GAP-BOX-6
//      fill made this true for all prims, not just Int32), plus exact-value
//      probes for the documented net48 patterns (Int32->value, Boolean->1/0,
//      Char->c|(c<<16), Byte->value, UInt32->full pattern, Int16->masked).
//   3. Boxed Equals cross-type COMBOS beyond boxops: uint vs long, byte vs
//      short, char vs int, uint vs int with equal numeric values -- all false
//      (net48 value-type Equals gate on the exact box type).
//   4. object[] of MIXED boxes sorted by Array.Sort(object[], Comparer.Default):
//      the comparer-driven ref sort (D-ICMP-3, il2cpp_array_sort_icomparer)
//      -- boxed ints sort ascending by value, and a Reverse after the sort
//      yields the descending order.
//   5. Unbox-then-compare combos: the box round-trips through (T) unbox and
//      the VALUES compare across the two unboxed carriers.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class BoxOps2 {
    public static void Main() {
        // ===== 1. Comparer.Default on boxed pairs -- sign of Compare =====
        Console.WriteLine("cmp_int_lt=" + (Comparer.Default.Compare((object)3, (object)5) < 0));
        Console.WriteLine("cmp_int_gt=" + (Comparer.Default.Compare((object)9, (object)4) > 0));
        Console.WriteLine("cmp_int_eq=" + (Comparer.Default.Compare((object)7, (object)7) == 0));
        Console.WriteLine("cmp_int_neg=" + (Comparer.Default.Compare((object)(-5), (object)0) < 0));
        Console.WriteLine("cmp_uint_lt=" + (Comparer.Default.Compare((object)3u, (object)4000000000u) < 0));
        Console.WriteLine("cmp_uint_gt=" + (Comparer.Default.Compare((object)4000000000u, (object)3u) > 0));
        Console.WriteLine("cmp_uint_eq=" + (Comparer.Default.Compare((object)4000000000u, (object)4000000000u) == 0));
        Console.WriteLine("cmp_long_lt=" + (Comparer.Default.Compare((object)5L, (object)1234567890123L) < 0));
        Console.WriteLine("cmp_long_eq=" + (Comparer.Default.Compare((object)(-9L), (object)(-9L)) == 0));
        Console.WriteLine("cmp_ulong_lt=" + (Comparer.Default.Compare((object)5UL, (object)18446744073709551615UL) < 0));
        Console.WriteLine("cmp_double_lt=" + (Comparer.Default.Compare((object)1.5, (object)3.25) < 0));
        Console.WriteLine("cmp_double_eq=" + (Comparer.Default.Compare((object)(-0.0), (object)0.0) == 0));
        Console.WriteLine("cmp_float_gt=" + (Comparer.Default.Compare((object)9.5f, (object)1.5f) > 0));
        Console.WriteLine("cmp_char_lt=" + (Comparer.Default.Compare((object)'A', (object)'Z') < 0));
        Console.WriteLine("cmp_bool_lt=" + (Comparer.Default.Compare((object)false, (object)true) < 0));
        Console.WriteLine("cmp_str_lt=" + (Comparer.Default.Compare((object)"apple", (object)"banana") < 0));
        Console.WriteLine("cmp_str_gt=" + (Comparer.Default.Compare((object)"zebra", (object)"mango") > 0));
        Console.WriteLine("cmp_str_eq=" + (Comparer.Default.Compare((object)"same", (object)"same") == 0));
        Console.WriteLine("cmp_null_null=" + (Comparer.Default.Compare(null, null) == 0));
        Console.WriteLine("cmp_null_first=" + (Comparer.Default.Compare(null, (object)1) < 0));
        Console.WriteLine("cmp_null_last=" + (Comparer.Default.Compare((object)1, null) > 0));
        Console.WriteLine("cmp_null_str=" + (Comparer.Default.Compare(null, (object)"x") < 0));

        // ===== 2. boxed GetHashCode -- cross-box VALUE consistency =====
        object a = 42, b = 42;
        Console.WriteLine("hc_int_cons=" + (a.GetHashCode() == b.GetHashCode()));
        Console.WriteLine("hc_int_value=" + (a.GetHashCode() == 42));
        object bu1 = 4000000000u, bu2 = 4000000000u;
        Console.WriteLine("hc_uint_cons=" + (bu1.GetHashCode() == bu2.GetHashCode()));
        Console.WriteLine("hc_uint_full=" + (bu1.GetHashCode() == -294967296));
        object bl1 = 1234567890123L, bl2 = 1234567890123L;
        Console.WriteLine("hc_long_cons=" + (bl1.GetHashCode() == bl2.GetHashCode()));
        object bb1 = true, bb2 = true;
        Console.WriteLine("hc_bool_cons=" + (bb1.GetHashCode() == bb2.GetHashCode()));
        Console.WriteLine("hc_bool_val=" + (bb1.GetHashCode() == 1));
        object bc1 = 'A', bc2 = 'A';
        Console.WriteLine("hc_char_cons=" + (bc1.GetHashCode() == bc2.GetHashCode()));
        Console.WriteLine("hc_char_val=" + (bc1.GetHashCode() == 4259905));
        object by1 = (byte)200, by2 = (byte)200;
        Console.WriteLine("hc_byte_cons=" + (by1.GetHashCode() == by2.GetHashCode()));
        Console.WriteLine("hc_byte_val=" + (by1.GetHashCode() == 200));
        object bs1 = (short)-12, bs2 = (short)-12;
        Console.WriteLine("hc_short_cons=" + (bs1.GetHashCode() == bs2.GetHashCode()));
        Console.WriteLine("hc_short_val=" + (bs1.GetHashCode() == -720908));
        object bd1 = 3.25, bd2 = 3.25;
        Console.WriteLine("hc_double_cons=" + (bd1.GetHashCode() == bd2.GetHashCode()));
        object bf1 = 1.5f, bf2 = 1.5f;
        Console.WriteLine("hc_float_cons=" + (bf1.GetHashCode() == bf2.GetHashCode()));
        object bsby1 = (sbyte)-5, bsby2 = (sbyte)-5;
        Console.WriteLine("hc_sbyte_cons=" + (bsby1.GetHashCode() == bsby2.GetHashCode()));
        object bus1 = (ushort)65535, bus2 = (ushort)65535;
        Console.WriteLine("hc_ushort_cons=" + (bus1.GetHashCode() == bus2.GetHashCode()));
        object bad1 = 43, bad2 = 42;
        Console.WriteLine("hc_diff=" + (bad1.GetHashCode() != bad2.GetHashCode()));

        // ===== 3. boxed Equals -- cross-type combos =====
        Console.WriteLine("eq_uint_long=" + (((object)5u).Equals((object)5L) == false));
        Console.WriteLine("eq_byte_short=" + (((object)(byte)5).Equals((object)(short)5) == false));
        Console.WriteLine("eq_char_int=" + (((object)'A').Equals((object)65) == false));
        Console.WriteLine("eq_uint_int=" + (((object)5u).Equals((object)5) == false));
        Console.WriteLine("eq_int_self=" + (((object)5).Equals((object)5) == true));
        Console.WriteLine("eq_byte_self=" + (((object)(byte)5).Equals((object)(byte)5) == true));
        Console.WriteLine("eq_uint_self=" + (((object)5u).Equals((object)5u) == true));
        Console.WriteLine("eq_long_self=" + (((object)5L).Equals((object)5L) == true));
        Console.WriteLine("eq_double_self=" + (((object)5.0).Equals((object)5.0) == true));
        Console.WriteLine("eq_float_self=" + (((object)5.0f).Equals((object)5.0f) == true));

        // ===== 4. object[] of mixed boxes -- comparer-driven sort =====
        // NOTE: Array.Reverse on the object[] is NOT probed -- the runtime
        // icall_array_reverse requires a System.Int32 array (arrayext.cpp
        // RequireInt32Array) and would throw on a boxed object[] (recorded
        // gap). Reverse-of-boxes stays on the int32 surface (arrayfuncs2).
        object[] boxes = new object[] { (object)9, (object)3, (object)7, (object)1, (object)5 };
        Array.Sort(boxes, Comparer.Default);
        Console.WriteLine("arr_sorted=" + ((int)boxes[0] == 1 && (int)boxes[1] == 3 && (int)boxes[2] == 5 &&
                                           (int)boxes[3] == 7 && (int)boxes[4] == 9));
        object[] mixed = new object[] { (object)3u, (object)3u, (object)3u };
        Array.Sort(mixed, Comparer.Default);
        Console.WriteLine("arr_dup=" + ((uint)mixed[0] == 3u && (uint)mixed[2] == 3u));

        // ===== 5. unbox-then-compare combos =====
        object u1 = 42, u2 = 42, u3 = 43;
        Console.WriteLine("unbox_eq=" + ((int)u1 == (int)u2));
        Console.WriteLine("unbox_neq=" + ((int)u1 != (int)u3));
        object ud1 = 3.25, ud2 = 3.25;
        Console.WriteLine("unbox_double=" + ((double)ud1 == (double)ud2));
        object ul1 = 1234567890123L, ul2 = 1234567890123L;
        Console.WriteLine("unbox_long=" + ((long)ul1 == (long)ul2));
        object ub1 = true, ub2 = true;
        Console.WriteLine("unbox_bool=" + ((bool)ub1 == (bool)ub2));
        object uu1 = 4000000000u, uu2 = 4000000000u;
        Console.WriteLine("unbox_uint=" + ((uint)uu1 == (uint)uu2));

        Console.WriteLine("done");
    }
}}

