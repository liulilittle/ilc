//------------------------------------------------------------------------------
// loud-linq-fam-ref.cs -- e2e reference case for the FAMILY-GENERIC LINQ
// surface (linq_family.h) over the REFERENCE element family (string[]).
//
// The runtime ships one cell-descriptor kernel per operator with the cell
// code as the trailing literal; the reference cell is IL2CPP_LINQ_CELL_REF
// (15). The transpiler's reference-source block (MethodMap.Linq.cs) now wires
// them, so these operators over a string[] no longer LOUD-reject.
//
// Drives Take/Skip/Reverse/Concat/Distinct/Except/Union/Intersect/
// DefaultIfEmpty/LongCount/SequenceEqual/Contains/ElementAt over string[]
// and asserts exact element VALUES plus the net48 boundary semantics.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class LoudLinqFamRef
    {
        public static void Main()
        {
            string[] words = { "apple", "banana", "cherry", "apple", "date" };
            string[] empty = new string[0];
            string[] other = { "cherry", "fig" };

            // ---- Take / Skip over string[] ----
            string[] t2 = words.Take(2).ToArray();
            Console.WriteLine("take2=" + (t2.Length == 2 && t2[0] == "apple" && t2[1] == "banana"));
            Console.WriteLine("takeneg=" + (words.Take(-1).ToArray().Length == 0));
            Console.WriteLine("take99=" + (words.Take(99).ToArray().Length == 5));
            string[] s2 = words.Skip(2).ToArray();
            Console.WriteLine("skip2=" + (s2.Length == 3 && s2[0] == "cherry" && s2[2] == "date"));
            Console.WriteLine("skipneg=" + (words.Skip(-1).ToArray().Length == 5));
            Console.WriteLine("skip99=" + (words.Skip(99).ToArray().Length == 0));

            // ---- Reverse over string[] ----
            string[] rev = words.Reverse().ToArray();
            Console.WriteLine("rev=" + (rev.Length == 5 && rev[0] == "date" && rev[4] == "apple"));

            // ---- Concat over string[] ----
            string[] cat = words.Take(2).Concat(words.Skip(2)).ToArray();
            Console.WriteLine("concat=" + (cat.Length == 5 && cat[0] == "apple" && cat[2] == "cherry" && cat[4] == "date"));

            // ---- Distinct over string[] (first-appearance) ----
            string[] dist = words.Distinct().ToArray();
            Console.WriteLine("distinct=" + (dist.Length == 4 && dist[0] == "apple" && dist[1] == "banana"
                && dist[2] == "cherry" && dist[3] == "date"));

            // ---- Except / Union / Intersect over string[] ----
            string[] exc = words.Except(other).ToArray();
            Console.WriteLine("except=" + (exc.Length == 3 && exc[0] == "apple" && exc[1] == "banana" && exc[2] == "date"));
            string[] inter = words.Intersect(other).ToArray();
            Console.WriteLine("intersect=" + (inter.Length == 1 && inter[0] == "cherry"));
            string[] uni = words.Union(other).ToArray();
            Console.WriteLine("union=" + (uni.Length == 5 && uni[0] == "apple" && uni[4] == "fig"));

            // ---- DefaultIfEmpty over an empty string[] ----
            string[] die = empty.DefaultIfEmpty().ToArray();
            Console.WriteLine("defaultifempty=" + (die.Length == 1 && die[0] == null));
            string[] diefull = words.DefaultIfEmpty().ToArray();
            Console.WriteLine("defaultifemptyfull=" + (diefull.Length == 5 && diefull[0] == "apple"));

            // ---- LongCount over string[] ----
            Console.WriteLine("longcount=" + (words.LongCount() == 5L));

            // ---- SequenceEqual over string[] ----
            Console.WriteLine("seqeq=" + words.SequenceEqual(words.ToArray()));
            Console.WriteLine("seqneq=" + (!words.SequenceEqual(other)));

            // ---- Contains over string[] ----
            Console.WriteLine("contains=" + words.Contains("cherry"));
            Console.WriteLine("containsno=" + (!words.Contains("grape")));

            // ---- ElementAt over string[] ----
            Console.WriteLine("elementat=" + (words.ElementAt(2) == "cherry"));
        }
    }
}
