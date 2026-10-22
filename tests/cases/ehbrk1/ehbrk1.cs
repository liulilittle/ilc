// ehbrk1: break out of a try that has a finally, inside a for loop.
using System;

namespace Cases
{
    public static class EhBrk1
    {
        static void W(string tag, string v)
        {
            Console.WriteLine("[" + tag + "]=" + v);
        }

        public static void Main()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (i == 2) break;
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
