using System;

namespace ArrNeg
{
    public static class ArrNeg
    {
        // Index is NOT the loop induction variable itself: i+1 reaches 100
        // (the array length) at i=99 -- the original throws
        // IndexOutOfRangeException, so the bounds check MUST be kept.
        public static int WriteShift()
        {
            int[] a = new int[100];
            for (int i = 0; i < 100; i++) a[i + 1] = i;
            return a.Length;
        }

        // Array is a PARAMETER: no fresh-newarr proof (the bound a.Length
        // equals the length, but the array may be null / any array) -- the
        // null + bounds checks MUST be kept.
        public static int Sum(int[] a)
        {
            int s = 0;
            for (int i = 0; i < a.Length; i++) s += a[i];
            return s;
        }

        // Positive shape for the get_length-bound proof: fresh newarr + loop
        // bound a.Length -- the bounds check CAN be elided.
        public static int SumFresh()
        {
            int[] a = new int[1000];
            int s = 0;
            for (int i = 0; i < a.Length; i++) s += a[i];
            return s;
        }

        public static void Main()
        {
            Console.WriteLine(Sum(new int[] { 1, 2, 3 }));
            Console.WriteLine(SumFresh());
        }
    }
}
