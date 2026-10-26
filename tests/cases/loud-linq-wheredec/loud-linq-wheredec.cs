//------------------------------------------------------------------------------
// loud-linq-wheredec.cs -- e2e reference case for Enumerable.Where over a
// DECIMAL source (net48 Where<decimal>(source, Func<decimal,bool>)).
//
// The predicate takes the decimal BY VALUE (the InvokeSelDecBool ABI); the
// result is a fresh decimal[] with the matching elements. Before this change
// the shape LOUD-rejected at the stage-1 gate ("the stage-1 runtime LINQ
// surface is int32 (0x08) element-family for this operator").
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqWhereDec
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static void Main()
        {
            decimal[] a = { 1m, 2m, 3m };
            var w = a.Where(x => x > 1m).ToArray();
            Console.WriteLine("where_n=" + w.Length + " sum=" + D(w.Sum()));
            Console.WriteLine("done");
        }
    }
}
