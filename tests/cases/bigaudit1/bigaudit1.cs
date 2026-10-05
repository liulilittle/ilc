//------------------------------------------------------------------------------
// bigaudit1.cs -- G6 audit probe: BigInteger division/remainder where a
// quotient digit reaches 0xFFFFFFFF. The runtime's DivModMagnitude binary
// search used a uint32 lo index, so lo = mid + 1 wrapped to 0 when mid was
// 0xFFFFFFFF and the division never terminated. net48 reference outputs are
// the plain decimal values printed below.
// Deterministic: no time/random/paths/culture dependence.
//------------------------------------------------------------------------------

using System;
using System.Numerics;

class BigAudit1
{
    static void Main()
    {
        // Minimal trigger: the single-limb value 0xFFFFFFFF divided by 1.
        BigInteger big = new BigInteger(4294967295L);
        Console.WriteLine((big / 1).ToString());              // 4294967295
        Console.WriteLine((big / new BigInteger(1)).ToString()); // 4294967295

        // Remainder path shares DivModMagnitude.
        Console.WriteLine((big % 1).ToString());              // 0

        // Negative dividend keeps the same quotient-digit boundary.
        Console.WriteLine((new BigInteger(-4294967295L) / 1).ToString()); // -4294967295

        // Ordinary division sanity.
        Console.WriteLine((new BigInteger(5) / new BigInteger(3)).ToString()); // 1
        Console.WriteLine("done");
    }
}
