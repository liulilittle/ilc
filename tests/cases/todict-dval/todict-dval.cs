using System;
using System.Collections.Generic;
using System.Linq;

// Section 109: Enumerable.ToDictionary with a double TValue.
// net48 oracle:
//   d1: int keys, double values -> Count 4, d1[0]=0, d1[3]=4.5,
//   ContainsKey(9)=false, TryGetValue(2)=true out=3.0, TryGetValue(9)=false out=0,
//   Values snapshot [0,1.5,3.0,4.5] insertion order,
//   Remove(2) -> true, Count 3, d1[3]=4.5.
//   Duplicate key -> ArgumentException; missing key -> KeyNotFoundException.
//   d2: string keys, double values -> d2["x"]=5.0, d2["y"]=10.0,
//   Values snapshot insertion order.
namespace Cases {
    public static class TodictDouble {
        public static void Main() {
            var d1 = Enumerable.Range(0, 4)
                .ToDictionary(i => i, i => i * 1.5);
            Console.Write("d1count=" + d1.Count);
            Console.Write(" k0=" + d1[0]);
            Console.Write(" k3=" + d1[3]);
            Console.Write(" has9=" + d1.ContainsKey(9));
            Console.Write(" try2=" + d1.TryGetValue(2, out double v1) + "," + v1);
            Console.Write(" try9=" + d1.TryGetValue(9, out double v2) + "," + v2);
            Console.Write(" vals=" + string.Join(",", d1.Values));
            Console.Write(" rem2=" + d1.Remove(2) + "," + d1.Count);
            try { d1[0] = 77.0; Console.Write(" setok=" + d1[0]); } catch { Console.Write(" setbad"); }
            try { d1.Add(0, 99.0); Console.Write(" dupok"); } catch { Console.Write(" dupbad"); }
            try { var m = d1[9]; Console.Write(" missok"); } catch { Console.Write(" missbad"); }

            // string key + double value
            var d2 = new[] { "x", "y" }.ToDictionary(s => s, s => s.Length * 2.5);
            Console.Write(" d2x=" + d2["x"] + " d2y=" + d2["y"]);
            Console.Write(" d2vals=" + string.Join("|", d2.Values));

            Console.WriteLine();
        }
    }
}
