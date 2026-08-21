using System;
namespace Cases { public static class Delegates {
    static int Twice(int x) => x * 2;
    static int PlusTen(int x) => x + 10;
    public static void Main() {
        Func<int, int> f = Twice;
        Console.WriteLine(f(21));          // 42
        f = PlusTen;
        Console.WriteLine(f(5));           // 15
        Func<int, int> g = Twice;
        Console.WriteLine(g(4));           // 8
        Console.WriteLine(g(3) + f(1));    // 6 + 11 = 17
    }
}}
