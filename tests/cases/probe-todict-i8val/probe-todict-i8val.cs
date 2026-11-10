using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeToDictI8Val
    {
        static string J(long[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(System.Globalization.CultureInfo.InvariantCulture); } return s; }

        public static void Main()
        {
            // int32 keys + int64 values (the element selector form).
            int[] xs = { 3, 1, 2 };
            var d1 = xs.ToDictionary(x => x, x => (long)x * 10);
            Console.WriteLine("[c] " + d1.Count);
            Console.WriteLine("[v3] " + d1[3]);
            Console.WriteLine("[v1] " + d1[1]);
            Console.WriteLine("[has2] " + d1.ContainsKey(2));
            Console.WriteLine("[has9] " + d1.ContainsKey(9));
            Console.WriteLine("[vals] " + J(d1.Values.ToArray()));

            // string keys + int64 values.
            string[] ss = { "a", "b", "c" };
            var d2 = ss.ToDictionary(x => x, x => (long)x.Length);
            Console.WriteLine("[c2] " + d2.Count);
            Console.WriteLine("[v2_b] " + d2["b"]);

            // int32 keys + int64 values: duplicate key -> ArgumentException.
            try { var d4 = new[] { 1, 1 }.ToDictionary(x => x, x => (long)x); Console.WriteLine("[dup]=(none)"); }
            catch (Exception ex) { Console.WriteLine("[dup]=" + ex.GetType().Name); }

            // TryGetValue over an int64 value.
            long got = 0;
            Console.WriteLine("[try] " + d1.TryGetValue(2, out got) + ":" + got);
            Console.WriteLine("[try9] " + d1.TryGetValue(9, out got) + ":" + got);

            // Remove + get_Count over an int64-value wrapper.
            Console.WriteLine("[rem] " + d1.Remove(1) + ":" + d1.Count);
        }
    }
}
