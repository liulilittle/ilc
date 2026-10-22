//------------------------------------------------------------------------------
// p0linq1 -- LINQ non-int32 materialization/aggregate/select probe (D1/D2/D3).
//
// Three statically analysable statements that used to be rejected LOUD by the
// transpiler even though the runtime can implement them:
//
//   D1  Enumerable.ToList over a NON-int32 source (string[]/long[]/double[]
//       /float[]) -- the old gate threw "materializes only List<int32>".
//   D2  the source-only Sum/Min/Max/Average over decimal[] -- the old element
//       family table folded decimal into "ref" and the call fell through to
//       the phantom "BCL owner has no mapping arm" LOUD.
//       (string Min/Max is NOT here: net48 uses Comparer<string>.Default,
//       which is CULTURE-aware, so it stays a documented LOUD contract --
//       tests/cases/loud-p0-18-strmin-loud.)
//   D3  Enumerable.Select with TResult = decimal (decimal -> decimal and
//       int32 -> decimal).
//
// NOTE on shape: a decimal-PRODUCING icall writes through an explicit result
// slot, so the transpiler requires it in an ASSIGNMENT position (never nested
// inside another expression) -- every decimal aggregate below is therefore
// stored into a local before it is printed.
//
// Every printed value is deterministic: decimal text always goes through
// CultureInfo.InvariantCulture, everything else is a bool/int/number.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public sealed class Widget
    {
        public int V;
        public Widget(int v) { V = v; }
    }

    public static class P0Linq1
    {
        public static decimal Twice(decimal x) { return x * 2m; }
        public static decimal FromInt(int x) { return x * 1m; }

        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            // ===== D1: ToList over non-int32 sources =====
            string[] sa = { "a", "b" };
            List<string> ls = sa.ToList();
            Console.WriteLine("d1str_count=" + ls.Count);
            Console.WriteLine("d1str_0=" + ls[0]);
            Console.WriteLine("d1str_1=" + ls[1]);
            // The result is a fresh list (mutating it never touches the source).
            ls.Add("c");
            Console.WriteLine("d1str_after_add=" + ls.Count + "/" + sa.Length);

            long[] la = { 5L, -7L };
            List<long> ll = la.ToList();
            Console.WriteLine("d1long_count=" + ll.Count);
            Console.WriteLine(ll[0]);
            Console.WriteLine(ll[1]);

            double[] da = { 1.5, -2.25 };
            List<double> ld = da.ToList();
            Console.WriteLine("d1dbl_count=" + ld.Count);
            Console.WriteLine(ld[0]);
            Console.WriteLine(ld[1]);

            float[] fa = { 0.5f, 2.75f };
            List<float> lf = fa.ToList();
            Console.WriteLine("d1flt_count=" + lf.Count);
            Console.WriteLine("d1flt_ok=" + (lf[0] == 0.5f && lf[1] == 2.75f));

            Console.WriteLine("d1empty=" + new string[0].ToList().Count);

            // A user CLASS element rides the same listobj reference domain; the
            // element type must stay tracked so the field read resolves.
            Widget[] ws = { new Widget(1), new Widget(2) };
            List<Widget> lw = ws.ToList();
            Console.WriteLine("d1cls_count=" + lw.Count);
            Console.WriteLine("d1cls_0=" + lw[0].V);
            Console.WriteLine("d1cls_1=" + lw[1].V);

            // ===== D2: source-only decimal aggregates =====
            decimal[] ds = { 1.5m, 2.5m };
            decimal sum = ds.Sum();
            decimal avg = ds.Average();
            decimal min = ds.Min();
            decimal max = ds.Max();
            Console.WriteLine("d2sum=" + D(sum));
            Console.WriteLine("d2avg=" + D(avg));
            Console.WriteLine("d2min=" + D(min));
            Console.WriteLine("d2max=" + D(max));

            decimal[] neg = { -3.25m, 0m, 4.5m };
            decimal nsum = neg.Sum();
            decimal nmin = neg.Min();
            decimal nmax = neg.Max();
            Console.WriteLine("d2sum_neg=" + D(nsum));
            Console.WriteLine("d2min_neg=" + D(nmin));
            Console.WriteLine("d2max_neg=" + D(nmax));

            // Min/Max return the WINNING OPERAND unchanged (scale preserved).
            decimal[] tieA = { 1.50m, 1.5m };
            decimal taMin = tieA.Min();
            decimal taMax = tieA.Max();
            Console.WriteLine("d2tieA_min=" + D(taMin));
            Console.WriteLine("d2tieA_max=" + D(taMax));
            decimal[] tieB = { 1.5m, 1.50m };
            decimal tbMin = tieB.Min();
            decimal tbMax = tieB.Max();
            Console.WriteLine("d2tieB_min=" + D(tbMin));
            Console.WriteLine("d2tieB_max=" + D(tbMax));

            decimal[] one = { 7.25m };
            decimal oMin = one.Min();
            decimal oMax = one.Max();
            decimal oSum = one.Sum();
            Console.WriteLine("d2single=" + D(oMin) + "/" + D(oMax) + "/" + D(oSum));

            decimal[] emptyDec = new decimal[0];
            decimal eSum = emptyDec.Sum();
            Console.WriteLine("d2empty_sum=" + D(eSum));
            Console.WriteLine("d2empty_min_throws=" + EmptyMinThrows(emptyDec));
            Console.WriteLine("d2empty_max_throws=" + EmptyMaxThrows(emptyDec));
            Console.WriteLine("d2empty_avg_throws=" + EmptyAvgThrows(emptyDec));
            Console.WriteLine("d2overflow=" + SumOverflowThrows());

            // ===== D3: Select with TResult = decimal =====
            decimal[] d3 = { 3.7m, 1.2m };
            decimal[] mapped = d3.Select(Twice).ToArray();
            Console.WriteLine("d3len=" + mapped.Length);
            Console.WriteLine("d3v0=" + D(mapped[0]));
            Console.WriteLine("d3v1=" + D(mapped[1]));
            decimal d3sum = d3.Select(Twice).Sum();
            Console.WriteLine("d3sum=" + D(d3sum));

            int[] ints = { 1, 2, 3 };
            decimal[] fromInt = ints.Select(FromInt).ToArray();
            Console.WriteLine("d3int_len=" + fromInt.Length);
            Console.WriteLine("d3int_0=" + D(fromInt[0]));
            Console.WriteLine("d3int_2=" + D(fromInt[2]));

            // The result is a fresh array (source untouched).
            Console.WriteLine("d3fresh=" + (d3[0] == 3.7m));
            Console.WriteLine("d3empty=" + new decimal[0].Select(Twice).ToArray().Length);
        }

        private static bool EmptyMinThrows(decimal[] src)
        {
            try { decimal r = src.Min(); return false; }
            catch (InvalidOperationException) { return true; }
        }

        private static bool EmptyMaxThrows(decimal[] src)
        {
            try { decimal r = src.Max(); return false; }
            catch (InvalidOperationException) { return true; }
        }

        private static bool EmptyAvgThrows(decimal[] src)
        {
            try { decimal r = src.Average(); return false; }
            catch (InvalidOperationException) { return true; }
        }

        private static bool SumOverflowThrows()
        {
            try
            {
                decimal[] big = { decimal.MaxValue, decimal.MaxValue };
                decimal r = big.Sum();
                return false;
            }
            catch (OverflowException) { return true; }
        }
    }
}
