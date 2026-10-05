using System;

public class Swap {
    // P-G3 byref-share probe: no constraint, method body only reads/writes
    // the T slots (object-compatible) -> mask all-true -> SHARED def. The
    // shared clone's byref T parameters are Il2CppObject** slots; value-type
    // callers need the box/unbox bridge (GENERIC-SHARE-DESIGN.md 5.2).
    public static void SwapRef<T>(ref T a, ref T b) { T t = a; a = b; b = t; }
}

namespace Cases { public static class SwapCase {
    public static void Main() {
        int x = 10, y = 20;
        Swap.SwapRef(ref x, ref y);
        Console.WriteLine(x);   // 20
        Console.WriteLine(y);   // 10
        string s1 = "ab", s2 = "cd";
        Swap.SwapRef(ref s1, ref s2);
        Console.WriteLine(s1);  // cd
        Console.WriteLine(s2);  // ab
    }
}}