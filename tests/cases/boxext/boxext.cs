using System;
namespace Cases { public static class BoxExt {
    public static void Main() {
        // boxing / unboxing bool, char, double (extended primitives)
        object b = true;
        Console.WriteLine((bool)b ? 1 : 0);          // 1
        object bf = false;
        Console.WriteLine((bool)bf ? 1 : 0);         // 0
        object c = 'A';
        Console.WriteLine((char)c);                  // A
        object d = 3.14;
        Console.WriteLine(d);                        // 3.14
        Console.WriteLine((double)d);                // 3.14
        // Equals on boxed values (boxed-value equality per type)
        object i1 = 42, i2 = 42, i3 = 43;
        Console.WriteLine(i1.Equals(i2) ? 1 : 0);    // 1
        Console.WriteLine(i1.Equals(i3) ? 1 : 0);    // 0
        Console.WriteLine(((object)true).Equals((object)true) ? 1 : 0);    // 1
        Console.WriteLine(((object)'A').Equals((object)'A') ? 1 : 0);      // 1
        Console.WriteLine(((object)3.14).Equals((object)3.14) ? 1 : 0);    // 1
        Console.WriteLine(((object)3.14).Equals((object)3.15) ? 1 : 0);    // 0
        Console.WriteLine(((object)double.NaN).Equals((object)double.NaN) ? 1 : 0); // 1
        Console.WriteLine(i1.Equals(d) ? 1 : 0);     // 0 (different types)
        // type mismatch unbox -> InvalidCastException
        try { object o = 42; char ch = (char)o; Console.WriteLine(0); }
        catch (InvalidCastException) { Console.WriteLine(1); }            // 1
        try { object o = true; int x = (int)o; Console.WriteLine(0); }
        catch (InvalidCastException) { Console.WriteLine(1); }            // 1
    }
}}
