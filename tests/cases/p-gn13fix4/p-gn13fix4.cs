using System;
using System.Collections.Generic;

namespace Cases {

class Pair<A, B> {
    A a; B b;
    public Pair(A x, B y) { a = x; b = y; }
    public A First() { return a; }
    public B Second() { return b; }
}

public static class PGn13Fix4 {
    public static void Main() {
        Pair<int[,], string> p1 = new Pair<int[,], string>(null, "a");
        Console.WriteLine(p1.First() == null);
        Console.WriteLine(p1.Second());

        Pair<int[], string> p2 = new Pair<int[], string>(null, "b");
        Console.WriteLine(p2.First() == null);
        Console.WriteLine(p2.Second());

        Pair<string, string> p3 = new Pair<string, string>("c", "d");
        Console.WriteLine(p3.First() + p3.Second());
    }
}}
