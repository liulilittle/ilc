using System;
namespace Cases { public static class RandomCase {
    public static void Main() {
        // Fixed seed 42: every run prints the same sequence (determinism
        // contract -- the transpiled output must match byte-for-byte).
        var r = new Random(42);
        Console.WriteLine(r.Next());          // first Next()  [0, int.MaxValue]
        Console.WriteLine(r.Next());          // second Next()
        Console.WriteLine(r.Next());          // third Next()
        Console.WriteLine(r.Next(10));        // [0, 9]
        Console.WriteLine(r.Next(5, 10));     // [5, 9]
        Console.WriteLine(r.NextDouble().ToString("F6")); // [0, 1), fixed 6 decimals
        byte[] buf = new byte[4];
        r.NextBytes(buf);                     // fills all 4 bytes
        Console.WriteLine(string.Join(",", buf));
    }
}}
