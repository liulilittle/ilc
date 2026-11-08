// probe-todict-list.cs -- b5-gaps section 83: Enumerable.ToDictionary over a
// List<T> SOURCE (int32 and string elements). Uses the WIRED dictgen string
// value surface (int32->string and string->string: only string values are
// serveable -- the ToDictionary result is a DICTGEN wrapper, and an int32
// value would route it through the incompatible dictsi icalls). Instance
// surface: get_Item / get_Count / get_Keys / get_Values / ContainsKey;
// int->string dictionaries also enumerate via the snapshot pair enumerator
// foreach. Also: the elementSelector (3-generic) form, the ArgumentException
// on a duplicate key, and GC-inside-selector over a growing List<int>.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeToDictList
    {
        static string KeysOfIs(Dictionary<int, string> d)
        {
            string s = "";
            foreach (var kv in d) s += kv.Key + "=" + kv.Value + ";";
            return s;
        }

        public static void Main()
        {
            // [d1] int keys + string values via elementSelector over a
            // growing List<int> (snapshot semantics).
            List<int> xs = new List<int>();
            xs.Add(5); xs.Add(3); xs.Add(8);
            var d1 = xs.ToDictionary(x => x, x => "v" + x);
            Console.WriteLine("d1c:" + d1.Count);
            Console.WriteLine("d1:" + KeysOfIs(d1));
            Console.WriteLine("d1i5:" + d1[5]);
            Console.WriteLine("d1k3:" + d1.ContainsKey(3));
            Console.WriteLine("d1k9:" + d1.ContainsKey(9));

            // [d2] elementSelector doubling the key (int keys -> string values).
            var d2 = xs.ToDictionary(x => x * 2, x => x.ToString());
            Console.WriteLine("d2c:" + d2.Count);
            Console.WriteLine("d2:" + KeysOfIs(d2));
            Console.WriteLine("d2i6:" + d2[6]);

            // [d3] duplicate key -> net48 throws ArgumentException.
            List<int> dup = new List<int> { 1, 2, 2 };
            try { dup.ToDictionary(x => x, x => "v" + x); Console.WriteLine("d3:no-throw"); }
            catch (ArgumentException) { Console.WriteLine("d3:arg-ex"); }

            // [d4] string keys + string values (2-generic form over List<string>):
            // get_Keys / get_Values snapshot surfaces (foreach over dict STAYS
            // LOUD for the string->string domain).
            List<string> ws = new List<string> { "a", "b", "c" };
            var d4 = ws.ToDictionary(w => w, w => w.ToUpper());
            Console.WriteLine("d4c:" + d4.Count);
            Console.WriteLine("d4a:" + d4["a"]);
            Console.WriteLine("d4cb:" + d4.ContainsKey("b"));
            Console.WriteLine("d4cz:" + d4.ContainsKey("z"));
            Console.WriteLine("d4keys:" + String.Join(",", d4.Keys));
            Console.WriteLine("d4vals:" + String.Join(",", d4.Values));

            // [d5] string keys + string values via elementSelector (string->string gen).
            List<string> n5 = new List<string> { "x", "y" };
            var d5 = n5.ToDictionary(w => w, w => "len:" + w.Length);
            Console.WriteLine("d5c:" + d5.Count);
            Console.WriteLine("d5:" + d5["x"] + ";" + d5["y"]);
            Console.WriteLine("d5keys:" + String.Join(",", d5.Keys));

            // [d6] GC.Collect inside the elementSelector over a growing List<int>.
            List<int> g6 = new List<int>();
            for (int i = 1; i <= 5; ++i) g6.Add(i);
            var d6 = g6.ToDictionary(x => x, x => { GC.Collect(); return x * x + "!"; });
            Console.WriteLine("d6c:" + d6.Count);
            Console.WriteLine("d6:" + KeysOfIs(d6));

            Console.WriteLine("done");
        }
    }
}