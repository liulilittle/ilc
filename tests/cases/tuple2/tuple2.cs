using System;

namespace Cases { public static class Tuple2 {
    static (int, int) Two() { return (3, 4); }
    static (int, string) Mixed() { return (8, "m"); }

    public static void Main() {
        var (x, y) = (3, 4);
        Console.WriteLine("x=" + x + " y=" + y);

        (int a, string b) = Mixed();
        Console.WriteLine("a=" + a + " b=" + b);

        var (p, q) = Two();
        Console.WriteLine("p=" + p + " q=" + q);

        // swap via deconstruction
        int m = 1, n = 2;
        (m, n) = (n, m);
        Console.WriteLine("m=" + m + " n=" + n);

        // deconstruct into existing fields of a local struct
        (int, int, int) tri = (7, 8, 9);
        int s1, s2, s3;
        (s1, s2, s3) = tri;
        Console.WriteLine("s=" + s1 + s2 + s3);

        // deconstruction inside a loop
        int total = 0;
        for (int i = 0; i < 3; i++) {
            var (u, v) = (i, i * 2);
            total += u + v;
        }
        Console.WriteLine("total=" + total);
    }
}}
