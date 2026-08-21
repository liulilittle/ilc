using System;
namespace Cases { public static class FnPtr {
    static int Add(int a, int b) => a + b;
    static int Twice(int x) => x * 2;
    static void Print(int v) => Console.WriteLine(v);
    public static void Main() {
        unsafe {
            delegate*<int, int, int> p = &Add;
            Console.WriteLine(p(3, 4));     // 7
            delegate*<int, int> q = &Twice;
            Console.WriteLine(q(21));       // 42
            delegate*<int, int> q2 = &Twice;
            Console.WriteLine(q2(5) + q(4)); // 10 + 8 = 18
            delegate*<int, void> v = &Print;
            v(99);                          // 99
        }
    }
}}
