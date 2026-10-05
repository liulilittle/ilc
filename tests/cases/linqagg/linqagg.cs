//------------------------------------------------------------------------------
// linqagg.cs -- LINQ AGGREGATION deep probe 2 (e2e batch C62, after the LINQ
// domain landed -- linq.h / linq_map.h / linq_sort.h wired in M6): Sum / Min /
// Max COMBINATIONS and the DERIVED-EMPTY-SEQUENCE truth table, byte-exact vs
// the net48 oracle, dual-compiler (mingw + msvc).
//
// Goes deeper than linqdeep (C15: operator surface) and linqgroup (C58: plain
// aggregate truth tables + filter/select/orderby/take/skip combos): this case
// is ENTIRELY aggregate-focused on the i4 family (il2cpp_linq_sum_i4 /
// min_i4 / max_i4):
//
//   1) S1 CHAINS -- multi-stage aggregate pipelines (Where->Select->Sum/Min/Max,
//      Select->Where->Sum/Min/Max, Take->Select->Sum, Skip->Min/Max,
//      OrderBy->Skip->Take->Sum).
//   2) S2 TIES + DUPLICATES -- arrays with duplicate min/max, all-equal
//      arrays (min == max), single-value filters (min == max == sum/count).
//   3) S3 ZERO / NEGATIVE / BOUNDARY -- mixed-sign sums, zero-count filters,
//      int.MinValue / int.MaxValue present (Min/Max/Sum without overflow).
//   4) S4 RELATION IDENTITIES -- Sum(all) == Sum(even)+Sum(odd), doubled-sum
//      (Select(Dbl).Sum() == 2*Sum), negated min/max symmetry, Take+Skip
//      partition identity, OrderBy sum preservation.
//   5) S5 SINGLE-ELEMENT chains -- the one-element-after-filter shape
//      (Min == Max == value, Sum == value).
//   6) S6 DERIVED-EMPTY truth table -- every aggregate over an EMPTY result
//      (Where(always-false), Take(0), Skip(oversize), empty source), with the
//      Sum->0 / Min/Max->InvalidOperationException / Count->0 / Any->false /
//      All->vacuous-true contract.
//
// Delegate contract (linq.h / linq_map.h): every predicate/selector below is a
// single-cast STATIC-method delegate (no instance-bound receivers in this
// case -- the linqib surface is covered by linqgroup). Sum overflow is CHECKED
// (net48 Enumerable.Sum is checked; the runtime throws OverflowException --
// D-LINQ-3) -- overflow is NOT probed here (it IS in linqgroup s_overflow).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases { public static class LinqAgg {
    // ---- static callbacks (single-cast static-method ABI) ----
    public static bool IsEven(int x) { return x % 2 == 0; }
    public static bool IsOdd(int x) { return (x & 1) == 1; }
    public static bool IsNeg(int x) { return x < 0; }
    public static bool IsZero(int x) { return x == 0; }
    public static bool IsOne(int x) { return x == 1; }
    public static bool IsNine(int x) { return x == 9; }
    public static bool IsBig4(int x) { return x > 4; }
    public static bool AlwaysTrue(int x) { return true; }
    public static bool AlwaysFalse(int x) { return false; }
    public static int Dbl(int x) { return x * 2; }
    public static int Neg(int x) { return -x; }
    public static int Identity(int x) { return x; }
    public static int Square(int x) { return x * x; }

    public static void Main() {
        int[] nums = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
        int[] dup = { 4, 4, 1, 1, 9, 9, 1 };
        int[] flat = { 7, 7, 7, 7 };
        int[] mixed = { -3, 0, 5, -1, 0, 2 };
        int[] bounds = { int.MinValue, 0, int.MaxValue };
        int[] empty = new int[0];
        int[] single = { 42 };

        // ===== 1. multi-stage aggregate chains =====
        Console.WriteLine("c1=" + (nums.Where(IsEven).Select(Dbl).Sum() == 40));
        Console.WriteLine("c2=" + (nums.Where(IsEven).Select(Dbl).Min() == 4));
        Console.WriteLine("c3=" + (nums.Where(IsEven).Select(Dbl).Max() == 16));
        Console.WriteLine("c4=" + (nums.Select(Dbl).Where(IsEven).Sum() == 90));
        Console.WriteLine("c5=" + (nums.Select(Dbl).Where(IsEven).Min() == 2));
        Console.WriteLine("c6=" + (nums.Select(Dbl).Where(IsEven).Max() == 18));
        Console.WriteLine("c7=" + (nums.Take(4).Select(Dbl).Sum() == 36));
        Console.WriteLine("c8=" + (nums.Skip(3).Min() == 1));
        Console.WriteLine("c9=" + (nums.Skip(3).Max() == 8));
        Console.WriteLine("c10=" + (nums.OrderBy(Identity).Skip(2).Take(3).Sum() == 12));
        Console.WriteLine("c11=" + (nums.OrderBy(Identity).Take(5).Sum() == 15));
        Console.WriteLine("c12=" + (nums.Where(IsOdd).Skip(1).Take(2).Sum() == 12));
        Console.WriteLine("c13=" + (nums.Select(Square).Where(IsBig4).Sum() == 280));
        Console.WriteLine("c14=" + (nums.Where(IsEven).Select(Dbl).Where(IsBig4).Sum() == 36));
        Console.WriteLine("c15=" + (nums.Where(IsBig4).Select(Dbl).Where(IsEven).Sum() == 70));
        Console.WriteLine("c16=" + (nums.Select(Dbl).Sum() == 90));

        // ===== 2. ties + duplicates =====
        Console.WriteLine("d1=" + (dup.Min() == 1 && dup.Max() == 9));
        Console.WriteLine("d2=" + (dup.Sum() == 29));
        Console.WriteLine("d3=" + (dup.Where(IsOne).Count() == 3));
        Console.WriteLine("d4=" + (dup.Where(IsOne).Sum() == 3));
        Console.WriteLine("d5=" + (dup.Where(IsOne).Min() == 1));
        Console.WriteLine("d6=" + (dup.Where(IsOne).Max() == 1));
        Console.WriteLine("d7=" + (dup.Where(IsOne).Sum() == dup.Where(IsOne).Count()));
        Console.WriteLine("d8=" + (flat.Min() == 7 && flat.Max() == 7));
        Console.WriteLine("d9=" + (flat.Sum() == 28));
        Console.WriteLine("d10=" + (flat.All(IsEven) == false));
        Console.WriteLine("d11=" + (dup.Where(IsNine).Min() == 9 && dup.Where(IsNine).Max() == 9));
        Console.WriteLine("d12=" + (dup.Where(IsNine).Sum() == 18));

        // ===== 3. zero / negative / boundary =====
        Console.WriteLine("z1=" + (mixed.Sum() == 3));
        Console.WriteLine("z2=" + (mixed.Min() == -3));
        Console.WriteLine("z3=" + (mixed.Max() == 5));
        Console.WriteLine("z4=" + (mixed.Where(IsNeg).Sum() == -4));
        Console.WriteLine("z5=" + (mixed.Where(IsNeg).Min() == -3));
        Console.WriteLine("z6=" + (mixed.Where(IsNeg).Max() == -1));
        Console.WriteLine("z7=" + (mixed.Where(IsZero).Count() == 2));
        Console.WriteLine("z8=" + (mixed.Where(IsZero).Sum() == 0));
        Console.WriteLine("z9=" + (bounds.Min() == int.MinValue));
        Console.WriteLine("z10=" + (bounds.Max() == int.MaxValue));
        Console.WriteLine("z11=" + (bounds.Sum() == -1));
        Console.WriteLine("z12=" + (nums.Where(IsNeg).Sum() == 0));

        // ===== 4. relation identities =====
        Console.WriteLine("r1=" + (nums.Sum() == nums.Where(IsEven).Sum() + nums.Where(IsOdd).Sum()));
        Console.WriteLine("r2=" + (nums.Select(Dbl).Sum() == 2 * nums.Sum()));
        Console.WriteLine("r3=" + (nums.Select(Neg).Max() == -nums.Min()));
        Console.WriteLine("r4=" + (nums.Min() == -nums.Select(Neg).Max()));
        Console.WriteLine("r5=" + (nums.Max() == -nums.Select(Neg).Min()));
        Console.WriteLine("r6=" + (nums.Take(4).Sum() + nums.Skip(4).Sum() == nums.Sum()));
        Console.WriteLine("r7=" + (nums.OrderBy(Identity).Sum() == nums.Sum()));
        Console.WriteLine("r8=" + (dup.Sum() == dup.Where(IsEven).Sum() + dup.Where(IsOdd).Sum()));
        Console.WriteLine("r9=" + (nums.Skip(0).Sum() == nums.Sum()));
        Console.WriteLine("r10=" + (nums.Take(9).Sum() == nums.Sum()));

        // ===== 5. single-element chains =====
        Console.WriteLine("s1=" + (single.Sum() == 42));
        Console.WriteLine("s2=" + (single.Min() == 42 && single.Max() == 42));
        Console.WriteLine("s3=" + (single.Where(IsEven).Sum() == 42));
        Console.WriteLine("s4=" + (single.Where(IsEven).Min() == 42));
        Console.WriteLine("s5=" + (single.Where(IsEven).Max() == 42));
        Console.WriteLine("s6=" + (nums.Where(IsNine).Count() == 1));
        Console.WriteLine("s7=" + (nums.Where(IsNine).Min() == 9 && nums.Where(IsNine).Max() == 9));
        Console.WriteLine("s8=" + (nums.Where(IsNine).Sum() == 9));

        // ===== 6. derived-empty truth table =====
        Console.WriteLine("e1=" + (empty.Sum() == 0));
        Console.WriteLine("e2=" + (empty.Count() == 0));
        Console.WriteLine("e3=" + (empty.Where(AlwaysTrue).Count() == 0));
        Console.WriteLine("e4=" + (empty.Where(AlwaysTrue).Sum() == 0));
        Console.WriteLine("e5=" + (empty.Select(Dbl).Count() == 0));
        Console.WriteLine("e6=" + (empty.Select(Dbl).Sum() == 0));
        Console.WriteLine("e7=" + (empty.Take(0).Count() == 0));
        Console.WriteLine("e8=" + (empty.Take(5).Sum() == 0));
        Console.WriteLine("e9=" + (empty.Skip(0).Count() == 0));
        Console.WriteLine("e10=" + (empty.Skip(5).Sum() == 0));
        Console.WriteLine("e11=" + (empty.Where(AlwaysTrue).Any() == false));
        Console.WriteLine("e12=" + (empty.Where(AlwaysTrue).All(AlwaysTrue) == true));
        Console.WriteLine("e13=" + (empty.Where(AlwaysTrue).FirstOrDefault() == 0));
        Console.WriteLine("e14=" + (nums.Where(AlwaysFalse).Count() == 0));
        Console.WriteLine("e15=" + (nums.Where(AlwaysFalse).Sum() == 0));
        Console.WriteLine("e16=" + (nums.Take(0).Sum() == 0));
        Console.WriteLine("e17=" + (nums.Skip(100).Count() == 0));
        Console.WriteLine("e18=" + (nums.Take(0).Where(AlwaysTrue).Sum() == 0));
        Console.WriteLine("e19=" + (nums.Where(AlwaysTrue).Where(AlwaysFalse).Sum() == 0));
        Console.WriteLine("e20=" + (nums.Skip(100).Where(AlwaysTrue).Count() == 0));
        // Min/Max over derived empties throw InvalidOperationException.
        bool t1 = false;
        try { int x = empty.Min(); } catch (InvalidOperationException) { t1 = true; }
        Console.WriteLine("e21=" + t1);
        bool t2 = false;
        try { int x = empty.Max(); } catch (InvalidOperationException) { t2 = true; }
        Console.WriteLine("e22=" + t2);
        bool t3 = false;
        try { int x = empty.Where(AlwaysTrue).Min(); } catch (InvalidOperationException) { t3 = true; }
        Console.WriteLine("e23=" + t3);
        bool t4 = false;
        try { int x = empty.Where(AlwaysTrue).Max(); } catch (InvalidOperationException) { t4 = true; }
        Console.WriteLine("e24=" + t4);
        bool t5 = false;
        try { int x = empty.Select(Dbl).Min(); } catch (InvalidOperationException) { t5 = true; }
        Console.WriteLine("e25=" + t5);
        bool t6 = false;
        try { int x = empty.Select(Dbl).Max(); } catch (InvalidOperationException) { t6 = true; }
        Console.WriteLine("e26=" + t6);
        bool t7 = false;
        try { int x = nums.Where(AlwaysFalse).Min(); } catch (InvalidOperationException) { t7 = true; }
        Console.WriteLine("e27=" + t7);
        bool t8 = false;
        try { int x = nums.Where(AlwaysFalse).Max(); } catch (InvalidOperationException) { t8 = true; }
        Console.WriteLine("e28=" + t8);
        bool t9 = false;
        try { int x = nums.Take(0).Min(); } catch (InvalidOperationException) { t9 = true; }
        Console.WriteLine("e29=" + t9);
        bool t10 = false;
        try { int x = nums.Skip(100).Max(); } catch (InvalidOperationException) { t10 = true; }
        Console.WriteLine("e30=" + t10);
        bool t11 = false;
        try { int x = nums.Take(0).Where(AlwaysTrue).Min(); } catch (InvalidOperationException) { t11 = true; }
        Console.WriteLine("e31=" + t11);
        bool t12 = false;
        try { int x = nums.Where(AlwaysTrue).Where(AlwaysFalse).Max(); } catch (InvalidOperationException) { t12 = true; }
        Console.WriteLine("e32=" + t12);
    }
}}
