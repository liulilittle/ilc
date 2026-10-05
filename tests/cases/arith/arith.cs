using System;
namespace Cases { public static class Arith {
    public static void Main() {
        Console.WriteLine(2 + 3 * 4);      // 14
        Console.WriteLine((2 + 3) * 4);    // 20
        Console.WriteLine(17 / 5);         // 3
        Console.WriteLine(17 % 5);         // 2
        Console.WriteLine(-7 / 2);         // -3 (trunc)
        Console.WriteLine(-7 % 2);         // -1
        Console.WriteLine(1 << 10);        // 1024
        Console.WriteLine(1024 >> 3);      // 128
        Console.WriteLine(0x0F & 0x33);    // 3
        Console.WriteLine(0x0F | 0x30);    // 63
        Console.WriteLine(0xFF ^ 0x0F);    // 240
        Console.WriteLine(~0);             // -1
    }
}}
