// ehcat1: `break`/`continue`/`return` out of a try/CATCH (no finally).
// This is the "leave with no exited finally" path: the leave must become a
// real goto out of the catch clause / try block.
using System;

namespace Cases
{
    public static class EhCat1
    {
        static void W(string tag, string v)
        {
            Console.WriteLine("[" + tag + "]=" + v);
        }

        // break out of a try block that has a catch (no finally).
        static void Brk()
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    if (i == 2) break;
                    W("brk" + i, "body");
                }
                catch (Exception)
                {
                    W("brkCatch", "c");
                }
                W("brkPost" + i, "p");
            }
        }

        // continue out of a try block that has a catch (no finally).
        static void Cnt()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (i == 1) continue;
                    W("cnt" + i, "body");
                }
                catch (Exception)
                {
                    W("cntCatch", "c");
                }
                W("cntPost" + i, "p");
            }
        }

        // return out of a catch handler (leave to the method's ret).
        static int RetFromCatch(int m)
        {
            try
            {
                if (m == 1) throw new InvalidOperationException("x");
                return 5;
            }
            catch (InvalidOperationException)
            {
                return 7;
            }
        }

        public static void Main()
        {
            Brk();
            Cnt();
            Console.WriteLine("[ret1]=" + RetFromCatch(1));
            Console.WriteLine("[ret0]=" + RetFromCatch(0));
            Console.WriteLine("[done]=1");
        }
    }
}
