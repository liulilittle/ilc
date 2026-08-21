using System;
namespace Cases { public static class Overflow {
    public static void Main() {
        int a = int.MaxValue;
        Console.WriteLine(a + 1);            // runtime wrap: -2147483648
        int b = int.MinValue;
        Console.WriteLine(b - 1);            // 2147483647
        int c = -2147483647;
        Console.WriteLine(-(c - 1));         // -(-2147483648) wraps -> -2147483648
        int d = int.MaxValue;
        Console.WriteLine(d * 2);            // -2
        // audit H9: INT_MIN / -1 throws OverflowException (C++ would SIGFPE).
        // NB: int.MinValue / -1 cannot be written literally -- it is a
        // compile-time constant expression and CS0220 rejects the checked
        // compile-time overflow. Runtime operands below keep the intended
        // runtime semantics: division/remainder overflow always throws,
        // independent of the checked/unchecked context.
        int minv = int.MinValue;
        try {
            int z = minv / -1;
            Console.WriteLine(z);
        } catch (OverflowException) {
            Console.WriteLine("ovf div");    // ovf div
        }
        try {
            int z = minv % -1;
            Console.WriteLine(z);
        } catch (OverflowException) {
            Console.WriteLine("ovf rem");    // ovf rem
        }
        Console.WriteLine(int.MinValue / 1); // -2147483648 (no overflow)
    }
}}
