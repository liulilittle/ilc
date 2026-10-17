//------------------------------------------------------------------------------
// p-nb4-bi.cs -- nb4 NUM-BI difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the BigInteger surface that was unwired: Parse / TryParse / Pow /
// GreatestCommonDivisor / ToByteArray / op_Explicit(int64|uint32). The probe
// values are net48-pinned small exact integers (the exploration oracle:
// bi.parse/bi.tryparse/bi.pow/bi.gcd/bi.gcd0/bi.bytes) -- no wall-clock or
// platform data, so the diff is scheduling-free.
//
// op_Explicit(int64): 2147483648 is outside int32 but inside int64 -> the
// wide conversion arm. op_Explicit(uint32): 4294967295 (uint.MaxValue) ->
// the uint conversion arm.
//------------------------------------------------------------------------------
using System;
using System.Numerics;

namespace Cases
{
    public static class PNb4Bi
    {
        public static void Main()
        {
            BigInteger p = BigInteger.Parse("1048576");
            Console.WriteLine("[bi.parse]=" + (p == new BigInteger(1048576)));

            BigInteger tp;
            bool ok = BigInteger.TryParse("999", out tp);
            Console.WriteLine("[bi.tryparse]=" + ok + "|" + tp.ToString());

            Console.WriteLine("[bi.pow]=" + BigInteger.Pow(new BigInteger(2), 20).ToString());
            Console.WriteLine("[bi.gcd]=" + BigInteger.GreatestCommonDivisor(new BigInteger(48), new BigInteger(36)).ToString());
            Console.WriteLine("[bi.gcd0]=" + BigInteger.GreatestCommonDivisor(new BigInteger(0), new BigInteger(5)).ToString());
            Console.WriteLine("[bi.bytes]=" + BitConverter.ToString(new BigInteger(256).ToByteArray()));
            Console.WriteLine("[bi.x64]=" + (long)new BigInteger(2147483648L));
            Console.WriteLine("[bi.xu]=" + (uint)new BigInteger(4294967295L));
        }
    }
}