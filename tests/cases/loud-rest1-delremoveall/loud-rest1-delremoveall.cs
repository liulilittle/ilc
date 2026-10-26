using System;
namespace Cases { public static class LoudRest1DelRemoveAll {
    static int F1(int x) { Console.WriteLine("F1:" + x); return x; }
    static int F2(int x) { Console.WriteLine("F2:" + x); return x; }
    public static void Main() {
        // Delegate.RemoveAll: net48 removes EVERY occurrence of the value's
        // invocation list (the runtime's Remove removes only the LAST). all =
        // [F1, F2, F1, F2, F1]; removing all F1 leaves [F2, F2].
        Func<int,int> a = F1;
        Func<int,int> b = F2;
        Func<int,int> all = a + b + a + b + a;
        Console.WriteLine(all.GetInvocationList().Length);
        Func<int,int> rest = (Func<int,int>)Delegate.RemoveAll(all, a);
        Console.WriteLine(rest.GetInvocationList().Length);
        rest(7);
    }
}}
