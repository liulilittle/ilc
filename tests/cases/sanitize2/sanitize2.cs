using System;

namespace Cases { public static class Sanitize2 {
    class Holder { public int V; }

    // lambda closure -> <>c__DisplayClass0_N (metadata name has '<', '>')
    static Func<int> MakeAdder(int baseVal) {
        int extra = baseVal + 1;
        return () => baseVal + extra;
    }

    // closure over a field + a captured local
    static Func<int> MakeHolderAdder(Holder h, int bump) {
        return () => h.V + bump;
    }

    // static lambda cached in <>c (the cached-delegate singleton)
    static int StaticLambda() {
        Func<int, int> sq = v => v * v;
        Func<int, int> inc = v => v + 1;
        return sq(7) + inc(1);
    }

    // nested closures: a lambda inside a lambda -> two display classes
    static Func<int, Func<int>> Curried(int a) {
        return b => () => a + b;
    }

    public static void Main() {
        Console.WriteLine("closure=" + MakeAdder(10)());
        var h = new Holder { V = 9 };
        Console.WriteLine("holder=" + MakeHolderAdder(h, 4)());
        Console.WriteLine("static=" + StaticLambda());
        Console.WriteLine("curried=" + Curried(3)(4)());
    }
}}
