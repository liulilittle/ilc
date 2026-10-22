// ehwhl1: break out of a try/finally inside a while(true) loop.
using System;

namespace Cases
{
    public static class EhWhl1
    {
        static void W(string tag, string v)
        {
            Console.WriteLine("[" + tag + "]=" + v);
        }

        public static void Main()
        {
            int n = 0;
            while (true)
            {
                try
                {
                    if (n >= 3) break;
                    W("n" + n, "body");
                    n++;
                }
                finally
                {
                    W("fin" + n, "z");
                }
            }
            Console.WriteLine("[n]=" + n);
            Console.WriteLine("[done]=1");
        }
    }
}
