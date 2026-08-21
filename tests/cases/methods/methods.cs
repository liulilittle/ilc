using System;
namespace Cases { public static class Methods {
    public static void Main() {
        Console.WriteLine(Add(3, 4));                 // 7
        Console.WriteLine(Fib(10));                   // 55
        Console.WriteLine(Pow2(5));                   // 32
        Console.WriteLine(Max(10, -3));               // 10
    }
    static int Add(int a, int b) => a + b;
    static int Fib(int n) => n <= 1 ? n : Fib(n - 1) + Fib(n - 2);
    static int Pow2(int n) { int r = 1; while (n-- > 0) r *= 2; return r; }
    static int Max(int a, int b) => a > b ? a : b;
}}
