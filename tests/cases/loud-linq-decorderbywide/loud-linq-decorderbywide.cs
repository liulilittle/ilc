//------------------------------------------------------------------------------
// loud-linq-decorderbywide.cs -- e2e reference case for Enumerable.OrderBy /
// OrderByDescending over a DECIMAL source with an int64 / double / decimal TKey
// (net48 OrderBy<decimal,TKey>).
//
// The decimal element crosses BY VALUE to the key selector; the stable sort
// materializes a fresh decimal[]. Before this change the shapes LOUD-rejected
// at the stage-1 gate.
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecOrderByWide
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 3m, 1m, 2m };
            Console.WriteLine("i8=" + D(a.OrderBy(x => (long)x).First()));
            Console.WriteLine("d=" + D(a.OrderBy(x => (double)x).First()));
            Console.WriteLine("dec=" + D(a.OrderBy(x => x).First()));
            Console.WriteLine("desci8=" + D(a.OrderByDescending(x => (long)x).First()));
            Console.WriteLine("done");
        }
    }
}
