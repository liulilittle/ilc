using System;

namespace Cases { public static class Sanitize1 {
    static int Add(int a, int b) {
        return Local(a) + b;
        int Local(int x) { return x * 2; }
    }

    // nested local function (local inside local) + capture
    static int Nested(int seed) {
        int captured = seed + 1;
        int Outer(int v) {
            int Inner(int w) { return w + captured; }
            return Inner(v) * 2;
        }
        return Outer(seed);
    }

    // recursive local function
    static int Fact(int n) {
        return Go(n);
        int Go(int k) { return k <= 1 ? 1 : k * Go(k - 1); }
    }

    // local function taking/returning a struct, capturing a local
    struct Pair { public int A; public int B; }
    static int StructLocal(int n) {
        Pair p; p.A = n; p.B = n + 2;
        int Sum(Pair q) { return q.A + q.B; }
        return Sum(p);
    }

    public static void Main() {
        Console.WriteLine("add=" + Add(3, 4));
        Console.WriteLine("nested=" + Nested(5));
        Console.WriteLine("fact=" + Fact(5));
        Console.WriteLine("struct=" + StructLocal(10));
    }
}}
