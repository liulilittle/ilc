//------------------------------------------------------------------------------
// randomsample.cs -- the Random.Sample() protected hook (net48 oracle,
// byte-for-byte). net48 Sample() == NextDouble(): InternalSample() scaled by
// 1/Int32.MaxValue. The runtime's icall_random_next_double is exactly that
// Sample helper, so both routes agree on a fixed seed.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class MyRandom : Random
    {
        public MyRandom(int seed) : base(seed) { }

        public double CallSample()
        {
            return Sample();
        }
    }

    public static class RandomSample
    {
        public static void Main()
        {
            var r = new MyRandom(12345);
            Console.WriteLine("sample=" + r.CallSample().ToString("R", System.Globalization.CultureInfo.InvariantCulture));

            var r2 = new MyRandom(12345);
            Console.WriteLine("nextdouble=" + r2.NextDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
