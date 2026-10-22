// ehcnt1: continue out of a try that has a finally, inside a for loop.
using System;

namespace Cases
{
    public static class EhCnt1
    {
        static void W(string tag, string v)
        {
            Console.WriteLine("[" + tag + "]=" + v);
        }

        public static void Main()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (i == 1) continue;
                    W("i" + i, "body");
                }
                finally
                {
                    W("fin" + i, "z");
                }
                W("post" + i, "p");
            }
            Console.WriteLine("[done]=1");
        }
    }
}
