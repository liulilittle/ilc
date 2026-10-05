using System;
namespace Cases { public static class BoxExt2 {
    public static void Main() {
        // boxing / unboxing byte
        object b = (object)(byte)255;
        Console.WriteLine((byte)b);                   // 255
        // boxing / unboxing sbyte
        object sb = (object)(sbyte)-1;
        Console.WriteLine((sbyte)sb);                 // -1
        // boxing / unboxing short
        object s = (object)(short)-32768;
        Console.WriteLine((short)s);                  // -32768
        // boxing / unboxing ushort
        object us = (object)(ushort)65535;
        Console.WriteLine((ushort)us);                // 65535
        // boxing / unboxing long
        object l = (object)123456789L;
        Console.WriteLine((long)l);                   // 123456789
        object l2 = (object)long.MaxValue;
        Console.WriteLine((long)l2);                  // 9223372036854775807
        // boxing / unboxing ulong
        object ul = (object)9876543210UL;
        Console.WriteLine((ulong)ul);                 // 9876543210
        // boxing / unboxing float (single)
        object f = (object)1.5f;
        Console.WriteLine((float)f);                  // 1.5
        // Equals on boxed values
        Console.WriteLine(l.Equals((object)123456789L) ? 1 : 0);    // 1
        Console.WriteLine(f.Equals((object)1.5f) ? 1 : 0);          // 1
        Console.WriteLine(((object)(byte)1).Equals((object)(byte)2) ? 1 : 0);  // 0
    }
}}
