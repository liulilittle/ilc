//------------------------------------------------------------------------------
// loud-linq-decselref.cs -- e2e reference case for Enumerable.Select over a
// DECIMAL source with a REFERENCE result (net48 Select<decimal,TResult> with a
// reference TResult, e.g. x => x.ToString()).
//
// The selector returns Il2CppObject*; the result is a fresh reference array.
// Before this change the shape LOUD-rejected at the stage-1 gate.
//
// Every printed value is deterministic.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecSelRef
    {
        public static void Main()
        {
            decimal[] a = { 1m, 2m, 3m };
            var s = a.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray();
            Console.WriteLine("sel=" + s[0] + "," + s[1] + "," + s[2]);
            Console.WriteLine("done");
        }
    }
}
