//------------------------------------------------------------------------------
// loud-linq-decorderby.cs -- e2e reference case for Enumerable.OrderBy /
// OrderByDescending over a DECIMAL source (net48 OrderBy<decimal,TKey> with an
// int32 TKey).
//
// The decimal element crosses BY VALUE to the key selector (the InvokeSelDec*
// ABI); the stable sort materializes a fresh decimal[]. Before this change the
// shape LOUD-rejected at the stage-1 gate.
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecOrderBy
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 3m, 1m, 2m };
            Console.WriteLine("ord=" + D(a.OrderBy(x => (int)x).First()));
            Console.WriteLine("ordd=" + D(a.OrderByDescending(x => (int)x).First()));
            Console.WriteLine("done");
        }
    }
}
