//------------------------------------------------------------------------------
// p-gn10jag.cs -- GN-10 blind-spot probe: JAGGED SZARRAY generic argument
// (int[][] -- TypeSpec `15 12 <box> 01 1D 1D 08`). Double-locked on shared
// Box<T> and non-shared Box3<T> (Echo<U> by-value MVAR forces per-instance).
//
// At HEAD the three producers' ReadM3ArrayArg complex branch consumed the inner
// SZARRAY payload and returned the flat token "array", while MethodBodyReader.
// ArrayGenericArgSuffix recursed to "[[int]]" (with '['). The '[' difference
// changes both the clone NAME and the CollapseInstantiation decision
// (EnsureClone's `!gSuffix.Contains('[')` keeps the element-aware clone), so
// the definition-side clone and the call side forked -> g++ "was not declared".
//
// The fix makes the complex branch recurse for 0x1D/0x14 elements, producing
// the MethodBodyReader token "[[int]]".
//
// A green control (Box2<string[]> -> "Box2_1__string__") rides alongside: the
// simple SZARRAY-of-string shape already agreed across all producers.
//
// net48 is the reference baseline; every line must print True byte-exact.
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Box<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

class Box3<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
    public static void Echo<U>(U u) { }
}

class Box2<T> {
    T item;
    public void Set(T v) { item = v; }
    public T Get() { return item; }
}

public static class PGn10Jag {
    public static void Main() {
        // ---- 1. int[][] (jagged SZARRAY) ----
        int[][] j = new int[2][];
        j[0] = new int[1];
        Box<int[][]> b = new Box<int[][]>();
        b.Set(j);
        Console.WriteLine(b.Get() == j);                   // True
        Console.WriteLine(b.Get()[0].Length == 1);         // True
        Box3<int[][]> b3 = new Box3<int[][]>();
        b3.Set(j);
        Console.WriteLine(b3.Get() == j);                  // True

        // ---- 2. control: string[] (simple SZARRAY element) ----
        string[] s = new string[2];
        Box2<string[]> b2 = new Box2<string[]>();
        b2.Set(s);
        Console.WriteLine(b2.Get() == s);                  // True
    }
}}
