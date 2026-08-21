using System;

namespace AuditFix
{
    public static class UintCmp
    {
        public static void Main()
        {
            uint a = 0xFFFFFFFFu;
            uint b = 1u;
            Console.WriteLine(a > b ? 1 : 0);   // 1 (unsigned comparison)
            Console.WriteLine(a < b ? 1 : 0);   // 0
            Console.WriteLine(a == b ? 1 : 0);  // 0
            Console.WriteLine(Compare(a, b));   // 1 (parameter path)
            Console.WriteLine(Compare(b, a));   // 0
            Console.WriteLine(Compare(a, a));   // 0
        }

        static int Compare(uint x, uint y)
        {
            return x > y ? 1 : 0;
        }
    }
}
