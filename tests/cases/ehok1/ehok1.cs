// ehok1 (control): single-path return inside a try/finally -- already works,
// must keep working after the fix.
using System;

namespace Cases
{
    public static class EhOk1
    {
        static int R()
        {
            try
            {
                return 1;
            }
            finally
            {
                Console.WriteLine("[fin]=z");
            }
        }

        public static void Main()
        {
            Console.WriteLine("[r]=" + R());
            Console.WriteLine("[done]=1");
        }
    }
}
