// probe-todict.cs -- b5-gaps §78: Enumerable.ToDictionary over ARRAY sources.
//
// Expected (net48 oracle): ToDictionary(source, keySelector) materializes a
// Dictionary<TKey,TElement>; the 3-arg form maps the element values. The
// default comparer governs key identity; duplicate key -> ArgumentException,
// null key -> ArgumentNullException. Enumeration order follows insertion.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeToDict
    {
        public static void Main()
        {
            // [t1] string keys + string values (element selector x -> "v"+x)
            int[] xs = { 1, 2, 3, 4 };
            var d1 = xs.ToDictionary(x => "k" + x, x => "v" + x);
            Console.WriteLine("t1c:" + d1.Count);
            Console.WriteLine("t1r:" + d1["k3"]);
            Console.WriteLine("t1k:" + d1.ContainsKey("k5"));
            Console.WriteLine("t1m:" + string.Join(",", d1.Keys));

            // [t2] string keys (default ordinal), no element selector
            string[] ss = { "a", "b", "c" };
            var d2 = ss.ToDictionary(x => x);
            Console.WriteLine("t2c:" + d2.Count);
            Console.WriteLine("t2r:" + d2["b"]);

            // [t3] duplicate key -> ArgumentException
            string[] dup = { "a", "a", "b" };
            try { var d3 = dup.ToDictionary(x => x); Console.WriteLine("t3:no-throw c=" + d3.Count); }
            catch (ArgumentException) { Console.WriteLine("t3:dup"); }

            // [t4] null key -> ArgumentNullException
            string[] withNull = { "a", null };
            try { var d4 = withNull.ToDictionary(x => x); Console.WriteLine("t4:no-throw"); }
            catch (ArgumentNullException) { Console.WriteLine("t4:null"); }

            // [t5] null keySelector -> ArgumentNullException
            string[] sn = { "a" };
            try { var d5 = sn.ToDictionary((Func<string, string>)null); Console.WriteLine("t5:no-throw"); }
            catch (ArgumentNullException) { Console.WriteLine("t5:argnull"); }

            // [t6] string keys + string values (no element selector = element)
            string[] words = { "apple", "banana", "cherry" };
            var d6 = words.ToDictionary(w => w.Substring(0, 1));
            Console.WriteLine("t6c:" + d6.Count);
            Console.WriteLine("t6r:" + d6["b"]);
            Console.WriteLine("t6m:" + string.Join(",", d6.Keys));

            // [t7] ToDictionary int keys + string values
            int[] nums = { 7, 8, 9 };
            var d7 = nums.ToDictionary(n => n, n => "v" + n);
            Console.WriteLine("t7c:" + d7.Count);
            Console.WriteLine("t7r:" + d7[8]);
            Console.WriteLine("t7m:" + d7.ContainsKey(9));

            // [t8] empty source -> zero-count dict
            string[] empty = new string[0];
            var d8 = empty.ToDictionary(x => x);
            Console.WriteLine("t8c:" + d8.Count);

            Console.WriteLine("done");
        }
    }
}