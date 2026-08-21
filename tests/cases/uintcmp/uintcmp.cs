using System;

namespace Cases
{
    public static class UintCmp
    {
        public static void Main()
        {
            // Unsigned comparisons (audit H1): uint values live in int32_t
            // slots; Cgt_un/Clt_un must compare as unsigned or every value
            // with the high bit set compares wrong.
            uint a = 0xFFFFFFFFu;
            uint b = 1u;
            Console.WriteLine(a > b ? 1 : 0);   // 1
            Console.WriteLine(a < b ? 1 : 0);   // 0
            Console.WriteLine(a == b ? 1 : 0);  // 0
            Console.WriteLine(Compare(a, b));   // 1 (parameter path)
            Console.WriteLine(Compare(b, a));   // 0
            Console.WriteLine(Compare(a, a));   // 0
            Console.WriteLine(0xFFFFFFFEu > 1u ? 1 : 0); // 1 (constant fold path)
            Console.WriteLine(0x80000000u > 1u ? 1 : 0); // 1
        }

        static int Compare(uint x, uint y)
        {
            return x > y ? 1 : 0;
        }
    }
}
