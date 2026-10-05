using System;
namespace Cases { public static class MathCase {
    // net48 does not ship Math.Clamp (added in .NET Core 2.0 /
    // .NET Standard 2.1); emulate its exact semantics here so the baseline
    // case can still print the clamped results.
    private static int Clamp(int value, int min, int max) {
        if (min > max) { throw new ArgumentException("min cannot be greater than max"); }
        if (value < min) { return min; }
        if (value > max) { return max; }
        return value;
    }
    public static void Main() {
        Console.WriteLine(Math.Abs(-5));        // 5
        Console.WriteLine(Math.Abs(5));         // 5
        Console.WriteLine(Math.Abs(-5L));       // 5 (long)
        Console.WriteLine(Math.Max(3, 7));      // 7
        Console.WriteLine(Math.Min(3, 7));      // 3
        Console.WriteLine(Math.Max(3L, 7L));    // 7 (long)
        Console.WriteLine(Math.Min(3L, 7L));    // 3 (long)
        Console.WriteLine(Math.Sign(-9));       // -1
        Console.WriteLine(Math.Sign(0));        // 0
        Console.WriteLine(Math.Sign(9));        // 1
        Console.WriteLine(Math.Sign(-9L));      // -1 (long)
        Console.WriteLine(Clamp(5, 1, 10));     // 5
        Console.WriteLine(Clamp(0, 1, 10));     // 1
        Console.WriteLine(Clamp(12, 1, 10));    // 10
        Console.WriteLine(Math.Sqrt(2.0));      // 1.4142135623730951
        Console.WriteLine(Math.Sqrt(9.0));      // 3
        Console.WriteLine(Math.Pow(2.0, 10.0)); // 1024
        Console.WriteLine(Math.Floor(3.7));     // 3
        Console.WriteLine(Math.Floor(-3.7));    // -4
        Console.WriteLine(Math.Ceiling(3.2));   // 4
        Console.WriteLine(Math.Ceiling(-3.2));  // -3
        Console.WriteLine(Math.Round(0.5));     // 0 (banker's rounding)
        Console.WriteLine(Math.Round(1.5));     // 2 (banker's rounding)
        Console.WriteLine(Math.Round(2.5));     // 2 (banker's rounding)
        Console.WriteLine(Math.Round(-1.5));    // -2 (banker's rounding)
        Console.WriteLine(Math.Abs(-2.5));      // 2.5
        Console.WriteLine(Math.Max(1.5, 2.5));  // 2.5
        Console.WriteLine(Math.Min(1.5, 2.5));  // 1.5
    }
}}
