// ehret2: value return from inside a try with a finally -- two return paths.
// Oracle: R(1) == 10, R(0) == 20, and fin runs exactly once per call.
using System;

namespace Cases
{
    public static class EhRet2
    {
        static void W(string tag, string v)
        {
            Console.WriteLine("[" + tag + "]=" + v);
        }

        static int R(int m)
        {
            try
            {
                if (m == 1) return 10;
                return 20;
            }
            finally
            {
                W("fin", "z");
            }
        }

        public static void Main()
        {
            Console.WriteLine("[r1]=" + R(1));
            Console.WriteLine("[r0]=" + R(0));
            Console.WriteLine("[done]=1");
        }
    }
}
