using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeToDictI4Val
    {
        static string J(int[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(System.Globalization.CultureInfo.InvariantCulture); } return s; }

        public static void Main()
        {
            // int32 keys + int32 values (the element selector form).
            int[] xs = { 3, 1, 2 };
            var d1 = xs.ToDictionary(x => x, x => x * 10);
            Console.WriteLine("[c] " + d1.Count);
            Console.WriteLine("[v3] " + d1[3]);
            Console.WriteLine("[v1] " + d1[1]);
            Console.WriteLine("[has2] " + d1.ContainsKey(2));
            Console.WriteLine("[has9] " + d1.ContainsKey(9));

            // int32 keys + int32 values, no element selector (value = element).
            var d2 = xs.ToDictionary(x => x);
            Console.WriteLine("[c2] " + d2.Count);
            Console.WriteLine("[v2_3] " + d2[3]);

            // string keys + int32 values (the element selector form).
            string[] ss = { "a", "b", "c" };
            var d3 = ss.ToDictionary(x => x, x => x.Length);
            Console.WriteLine("[c3] " + d3.Count);
            Console.WriteLine("[v3_b] " + d3["b"]);
            Console.WriteLine("[v3_c] " + d3["c"]);

            // int32 keys + int32 values: duplicate key -> ArgumentException.
            try { var d4 = new[] { 1, 1 }.ToDictionary(x => x, x => x); Console.WriteLine("[dup]=(none)"); }
            catch (Exception ex) { Console.WriteLine("[dup]=" + ex.GetType().Name); }

            // Values() snapshot: the boxed int32 values.
            Console.WriteLine("[vals] " + string.Join(",", d1.Values.ToArray()));
        }
    }
}
