// ehret1: void return from inside a try with a finally -- CONDITIONAL path.
// Oracle: R(1) prints before, fin (the return must skip "after");
//         R(0) prints before, after, fin.
using System;

namespace Cases
{
    public static class EhRet1
    {
        static void W(string tag, string v)
        {
            Console.WriteLine("[" + tag + "]=" + v);
        }

        static void R(int m)
        {
            try
            {
                W("before", "x");
                if (m == 1) return;
                W("after", "y");
            }
            finally
            {
                W("fin", "z");
            }
        }

        public static void Main()
        {
            R(1);
            R(0);
            Console.WriteLine("[done]=1");
        }
    }
}
