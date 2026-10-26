//------------------------------------------------------------------------------
// loud-linq-decsetops.cs -- e2e reference case for the DECIMAL-source set /
// concat / equality operators (net48 Distinct / Except / Concat /
// DefaultIfEmpty / SequenceEqual over a decimal[] source).
//
// The decimal equality is icall_decimal_compare == 0 (net48 decimal.Equals).
// Before this change the shapes LOUD-rejected at the stage-1 gate.
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecSetOps
    {
        public static void Main()
        {
            decimal[] a = { 1m, 2m, 2m, 3m };
            Console.WriteLine("distinct=" + a.Distinct().Count());
            Console.WriteLine("except=" + a.Except(new decimal[] { 2m }).Count());
            Console.WriteLine("concat=" + a.Concat(new decimal[] { 4m }).Count());
            Console.WriteLine("die=" + new decimal[0].DefaultIfEmpty(9m).First());
            Console.WriteLine("die2=" + a.DefaultIfEmpty(9m).Count());
            Console.WriteLine("seq=" + a.SequenceEqual(new decimal[] { 1m, 2m, 2m, 3m }));
            Console.WriteLine("done");
        }
    }
}
