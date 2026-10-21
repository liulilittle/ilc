using System;
using System.Linq;

// P0-11: Enumerable.Any() (no predicate) reads only the array header, so it
// works for every element family -- int32[], string[], object[]/class[] and an
// empty array. List<int>.Any() also RESOLVES (unit-pinned), but its runtime
// receiver carrier is a List object, not an array, and has no any kernel yet
// (tracked in .opencode/tmp/b5-gaps.md).

namespace Cases
{
    public sealed class AnyProbe
    {
        public int Value;
    }

    public static class LoudP011Any
    {
        public static void Main()
        {
            int[] ints = { 1, 2 };
            int[] emptyInts = new int[0];
            string[] strs = { "a" };
            string[] emptyStrs = new string[0];
            object[] objs = { new object() };
            AnyProbe[] probes = { new AnyProbe() };


            Console.WriteLine("[p0-11-int]=" + ints.Any());
            Console.WriteLine("[p0-11-int-empty]=" + emptyInts.Any());
            Console.WriteLine("[p0-11-str]=" + strs.Any());
            Console.WriteLine("[p0-11-str-empty]=" + emptyStrs.Any());
            Console.WriteLine("[p0-11-obj]=" + objs.Any());
            Console.WriteLine("[p0-11-class]=" + probes.Any());

        }
    }
}

