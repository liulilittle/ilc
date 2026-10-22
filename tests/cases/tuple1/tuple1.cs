using System;

namespace Cases { public static class Tuple1 {
    static (int, string) MakePair(int a, string b) { return (a, b); }

    static (int, double) MakeNum(int a, double b) { return (a, b); }

    public static void Main() {
        var t = (1, "a");
        Console.WriteLine("t1=" + t.Item1);
        Console.WriteLine("t2=" + t.Item2);

        var n = (3, 4.5);
        Console.WriteLine("n1=" + n.Item1);
        Console.WriteLine("n2=" + n.Item2);

        var p = MakePair(7, "seven");
        Console.WriteLine("p=" + p.Item1 + "/" + p.Item2);

        var q = MakeNum(9, 2.5);
        Console.WriteLine("q=" + q.Item1 + "/" + q.Item2);

        var three = (1, "x", 2.5);
        Console.WriteLine("three=" + three.Item1 + "," + three.Item2 + "," + three.Item3);

        var four = (1, 2, 3, 4);
        Console.WriteLine("four=" + four.Item1 + four.Item2 + four.Item3 + four.Item4);

        // assignment / copy semantics of a value type
        var copy = t;
        copy.Item1 = 42;
        Console.WriteLine("orig=" + t.Item1 + " copy=" + copy.Item1);
    }
}}
