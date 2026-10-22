using System;

namespace Cases { public static class Tuple3 {
    public static void Main() {
        var named = (A: 1, B: 2);
        Console.WriteLine("a=" + named.A + " b=" + named.B);
        Console.WriteLine(named.ToString());

        var mixed = (Name: "x", Score: 2.5);
        Console.WriteLine(mixed.ToString());
        Console.WriteLine("n=" + mixed.Name + " s=" + mixed.Score);

        var one = new ValueTuple<int>(42);
        Console.WriteLine(one.Item1);

        var eq1 = (1, "a");
        var eq2 = (1, "a");
        Console.WriteLine("eq=" + eq1.Equals(eq2));
        Console.WriteLine("hash=" + (eq1.GetHashCode() == eq2.GetHashCode()));
        Console.WriteLine("neq=" + (1, "a").Equals((2, "a")));

        var dt = (1, 2.5);
        Console.WriteLine(dt.ToString());
        Console.WriteLine("h=" + (dt.GetHashCode() == (1, 2.5).GetHashCode()));
    }
}}
