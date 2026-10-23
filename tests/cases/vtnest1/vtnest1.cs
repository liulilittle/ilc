using System;

namespace Cases { public static class VtNest1 {
    public static void Main() {
        (int, (int, int)) a = (1, (2, 3));
        Console.WriteLine("a1=" + a.Item1);
        Console.WriteLine("a2=" + a.Item2.Item1);
        Console.WriteLine("a3=" + a.Item2.Item2);
        Console.WriteLine("at=" + a.ToString());

        ((int, int), (string, string)) b = ((1, 2), ("x", "y"));
        Console.WriteLine("b=" + b.ToString());
        Console.WriteLine("b00=" + b.Item1.Item1 + b.Item1.Item2 + b.Item2.Item1 + b.Item2.Item2);

        (int, (int, (int, int))) c = (1, (2, (3, 4)));
        Console.WriteLine("c=" + c.ToString());
        Console.WriteLine("c4=" + c.Item2.Item2.Item2);
    }
}}