using System;
namespace Cases { public static class Math2 {
    // net48 has no Math.Clamp (added in .NET Core 2.0 / .NET Standard 2.1);
    // emulate its exact semantics so the case still covers the clamp shape.
    private static int Clamp(int value, int min, int max) {
        if (min > max) { throw new ArgumentException("min cannot be greater than max"); }
        if (value < min) { return min; }
        if (value > max) { return max; }
        return value;
    }
    public static void Main() {
        // --- Abs (int/long/double) ---
        Console.WriteLine(Math.Abs(-2147483647));            // 2147483647
        Console.WriteLine(Math.Abs(2147483647));             // 2147483647
        Console.WriteLine(Math.Abs(-5L));                    // 5
        Console.WriteLine(Math.Abs(5L));                     // 5
        Console.WriteLine(Math.Abs(-2.5));                   // 2.5
        Console.WriteLine(Math.Abs(2.5));                    // 2.5
        try { Console.WriteLine(Math.Abs(int.MinValue)); } catch (OverflowException e) {
            Console.WriteLine(e.GetType().Name);             // OverflowException
        }
        try { Console.WriteLine(Math.Abs(long.MinValue)); } catch (OverflowException e) {
            Console.WriteLine(e.GetType().Name);             // OverflowException
        }
        // --- Sign (int/long/double) ---
        Console.WriteLine(Math.Sign(-9));                    // -1
        Console.WriteLine(Math.Sign(0));                     // 0
        Console.WriteLine(Math.Sign(9));                     // 1
        Console.WriteLine(Math.Sign(-9L));                   // -1
        Console.WriteLine(Math.Sign(0L));                    // 0
        Console.WriteLine(Math.Sign(9L));                    // 1
        Console.WriteLine(Math.Sign(-2.5));                  // -1
        Console.WriteLine(Math.Sign(0.0));                   // 0
        Console.WriteLine(Math.Sign(-0.0));                  // 0 (sign of zero is 0)
        Console.WriteLine(Math.Sign(2.5));                   // 1
        // --- Max / Min (int/long/double) ---
        Console.WriteLine(Math.Max(3, 7));                   // 7
        Console.WriteLine(Math.Max(7, 3));                   // 7
        Console.WriteLine(Math.Min(3, 7));                   // 3
        Console.WriteLine(Math.Max(-3, -7));                 // -3
        Console.WriteLine(Math.Min(-3, -7));                 // -7
        Console.WriteLine(Math.Max(3L, 7L));                 // 7
        Console.WriteLine(Math.Min(3L, 7L));                 // 3
        Console.WriteLine(Math.Max(1.5, 2.5));               // 2.5
        Console.WriteLine(Math.Min(1.5, 2.5));               // 1.5
        Console.WriteLine(Math.Max(-1.5, -2.5));             // -1.5
        Console.WriteLine(Math.Min(-1.5, -2.5));             // -2.5
        Console.WriteLine(Math.Max(0.0, -0.0));              // 0 (net48: a>=b ? a : b)
        Console.WriteLine(Math.Min(0.0, -0.0));              // 0
        // --- Clamp (emulated; net48 has no Math.Clamp) ---
        Console.WriteLine(Clamp(5, 1, 10));                  // 5
        Console.WriteLine(Clamp(0, 1, 10));                  // 1
        Console.WriteLine(Clamp(12, 1, 10));                 // 10
        Console.WriteLine(Clamp(-5, -10, -1));               // -5
        try { Console.WriteLine(Clamp(5, 10, 1)); } catch (ArgumentException e) {
            Console.WriteLine(e.GetType().Name);             // ArgumentException
        }
        // --- Sqrt / Pow ---
        Console.WriteLine(Math.Sqrt(2.0));                   // 1.4142135623731 (net48 G15)
        Console.WriteLine(Math.Sqrt(9.0));                   // 3
        Console.WriteLine(Math.Sqrt(0.0));                   // 0
        Console.WriteLine(Math.Pow(2.0, 10.0));              // 1024
        Console.WriteLine(Math.Pow(2.0, -1.0));              // 0.5
        Console.WriteLine(Math.Pow(0.0, 0.0));               // 1
        Console.WriteLine(Math.Pow(5.0, 3.0));               // 125
        // --- Floor / Ceiling ---
        Console.WriteLine(Math.Floor(3.7));                  // 3
        Console.WriteLine(Math.Floor(-3.7));                 // -4
        Console.WriteLine(Math.Floor(5.0));                  // 5
        Console.WriteLine(Math.Ceiling(3.2));                // 4
        Console.WriteLine(Math.Ceiling(-3.2));               // -3
        Console.WriteLine(Math.Ceiling(5.0));                // 5
        // --- Round (net48 default = banker's / ToEven) ---
        Console.WriteLine(Math.Round(0.5));                  // 0
        Console.WriteLine(Math.Round(1.5));                  // 2
        Console.WriteLine(Math.Round(2.5));                  // 2
        Console.WriteLine(Math.Round(3.5));                  // 4
        Console.WriteLine(Math.Round(-1.5));                 // -2
        Console.WriteLine(Math.Round(-2.5));                 // -2
        Console.WriteLine(Math.Round(2.4));                  // 2
        Console.WriteLine(Math.Round(2.6));                  // 3
        Console.WriteLine(Math.Round(3.14159, 2));           // 3.14
        Console.WriteLine(Math.Round(2.675, 2));             // 2.67 (binary representation)
        // --- Truncate ---
        Console.WriteLine(Math.Truncate(3.7));               // 3
        Console.WriteLine(Math.Truncate(-3.7));              // -3
        Console.WriteLine(Math.Truncate(0.5));               // 0
        Console.WriteLine(Math.Truncate(-0.5));              // 0
        Console.WriteLine(Math.Truncate(2.0));               // 2
    }
}}
