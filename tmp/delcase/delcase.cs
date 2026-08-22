using System;
namespace Cases { public static class DelCase {
    public static void Main() {
        Func<int, int> f = x => x + 1;
        Action<int> a = y => { Console.Write(""); };
        var r1 = Delegate.Combine(f, a);
        Console.WriteLine(r1 == null ? "null" : r1.GetType().Name);
        Action<int> a2 = z => { Console.Write(""); };
        var r2 = Delegate.Combine(a, a2);
        Console.WriteLine(r2 == null ? "null" : r2.GetType().Name);
        var r3 = Delegate.Combine(a, null);
        Console.WriteLine(r3 == null ? "null" : r3.GetType().Name);
    }
}}
