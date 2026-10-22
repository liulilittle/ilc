// ehnest1: return from the innermost of two nested try/finally blocks.
// Oracle order: A (inner finally) then B (outer finally), return value 1.
using System;

namespace Cases
{
    public static class EhNest1
    {
        static void A()
        {
            Console.WriteLine("[A]=1");
        }

        static void B()
        {
            Console.WriteLine("[B]=1");
        }

        static void R()
        {
            try
            {
                try
                {
                    return;
                }
                finally
                {
                    A();
                }
            }
            finally
            {
                B();
            }
        }

        public static void Main()
        {
            R();
            Console.WriteLine("[done]=1");
        }
    }
}
