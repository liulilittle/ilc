using System;

namespace Cases
{
    public static class StringFind
    {
        public static void Main()
        {
            string s = "The Quick Brown Fox Jumps Over The Lazy Dog";
            // IndexOf(string, StringComparison)
            Console.WriteLine(s.IndexOf("quick", StringComparison.Ordinal));          // -1
            Console.WriteLine(s.IndexOf("quick", StringComparison.OrdinalIgnoreCase)); // 4
            Console.WriteLine(s.IndexOf("QUICK", StringComparison.OrdinalIgnoreCase)); // 4
            Console.WriteLine(s.IndexOf("The", StringComparison.Ordinal));             // 0
            // IndexOf(string, int, StringComparison)
            Console.WriteLine(s.IndexOf("the", 1, StringComparison.OrdinalIgnoreCase)); // 31
            Console.WriteLine(s.IndexOf("the", 32, StringComparison.OrdinalIgnoreCase)); // -1
            // IndexOf(string, int, int, StringComparison)
            Console.WriteLine(s.IndexOf("brown", 0, 10, StringComparison.OrdinalIgnoreCase)); // 10
            Console.WriteLine(s.IndexOf("brown", 0, 9, StringComparison.OrdinalIgnoreCase));  // -1
            // LastIndexOf(string, StringComparison)
            Console.WriteLine(s.LastIndexOf("the", StringComparison.OrdinalIgnoreCase)); // 31
            Console.WriteLine(s.LastIndexOf("THE", StringComparison.Ordinal));          // -1
            // LastIndexOf(string, int, StringComparison)
            Console.WriteLine(s.LastIndexOf("the", 30, StringComparison.OrdinalIgnoreCase)); // 0
            // LastIndexOf(string, int, int, StringComparison)
            Console.WriteLine(s.LastIndexOf("the", 35, 10, StringComparison.OrdinalIgnoreCase)); // 31
            Console.WriteLine(s.LastIndexOf("the", 35, 4, StringComparison.OrdinalIgnoreCase));  // -1
            // empty value
            Console.WriteLine(s.IndexOf("", StringComparison.Ordinal));               // 0
            Console.WriteLine(s.LastIndexOf("", StringComparison.Ordinal));          // 43
        }
    }
}
