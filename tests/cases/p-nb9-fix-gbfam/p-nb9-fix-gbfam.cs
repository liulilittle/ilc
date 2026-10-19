using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-gbfam -- nb9 fix batch M2/P0-3: GroupBy TElement families.
//
// The mapping accepted int64/double element selectors (ElemFamily 0x0A/0x0D)
// but the emitted call was ill-typed: get_key's signature followed the ELEMENT
// family instead of the key family, so the generated C++ did not compile (C0
// failure). The element family must only affect the BUCKET storage, never the
// key accessor.
//
// net48 oracle (host .NET Framework 4.x):
//   [gb_i4]=1=5,0=4,
//   [gb_i8]=1=5000000000|0=4000000000|
//   [gb_d]=1=5.5|0=4.5|
//   [gb_i8_rs]=1:5,0:4,
//   [gb_d_rs]=1#5,0#4,
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixGbfam
    {
        public static void Main()
        {
            int[] data = { 1, 2, 3, 4, 5 };
            var sb = new StringBuilder();

            foreach (var g in data.GroupBy(x => x % 2))
                sb.Append(g.Key + "=" + g.Count() + ",");
            Console.WriteLine("[gb_i4]=" + sb.ToString());

            // TElement = long (boxed bucket storage).
            sb = new StringBuilder();
            foreach (var g in data.GroupBy(x => x % 2, x => (long)x * 1000000000L))
            {
                sb.Append(g.Key + "=");
                foreach (long v in g) sb.Append(v + "|");
                sb.Append(",");
            }
            Console.WriteLine("[gb_i8]=" + sb.ToString());

            // TElement = double.
            sb = new StringBuilder();
            foreach (var g in data.GroupBy(x => x % 2, x => x + 0.5))
            {
                sb.Append(g.Key + "=");
                foreach (double v in g) sb.Append(v + "|");
                sb.Append(",");
            }
            Console.WriteLine("[gb_d]=" + sb.ToString());

            // TElement = long + result selector.
            sb = new StringBuilder();
            foreach (string s in data.GroupBy(x => x % 2, x => (long)x, (k, vals) => k + ":" + vals.Count()))
                sb.Append(s + ",");
            Console.WriteLine("[gb_i8_rs]=" + sb.ToString());

            // TElement = double + result selector.
            sb = new StringBuilder();
            foreach (string s in data.GroupBy(x => x % 2, x => x + 0.5, (k, vals) => k + "#" + vals.Count()))
                sb.Append(s + ",");
            Console.WriteLine("[gb_d_rs]=" + sb.ToString());
        }
    }
}
