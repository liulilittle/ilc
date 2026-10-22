// ehret3: return from a nested try/finally -- BOTH finallys must run,
// inner first then outer.
using System;

namespace Cases
{
    public static class EhRet3
    {
        static void W(string tag, string v)
        {
            Console.WriteLine("[" + tag + "]=" + v);
        }

        static int R(int m)
        {
            try
            {
                try
                {
                    if (m == 1) return 7;
                    W("body", "x");
                }
                finally
                {
                    W("fin_inner", "a");
                }
            }
            finally
            {
                W("fin_outer", "b");
            }
            return 9;
        }

        public static void Main()
        {
            Console.WriteLine("[r1]=" + R(1));
            Console.WriteLine("[r0]=" + R(0));
            Console.WriteLine("[done]=1");
        }
    }
}
