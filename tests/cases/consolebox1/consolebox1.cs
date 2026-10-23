using System;

namespace Cases { public static class ConsoleBox1 {
    public static void Main()
    {
        int i = 42;
        long l = -9000000000L;
        ulong ul = 18446744073709551615UL;
        short s = -12345;
        ushort us = 54321;
        byte b = 200;
        sbyte sby = -100;
        DayOfWeek dow = DayOfWeek.Wednesday;
        char c = 'Z';
        bool t = true;
        double d = 3.14159265358979;
        float f = 1.5f;

        Console.WriteLine((object)i);
        Console.WriteLine((object)l);
        Console.WriteLine((object)ul);
        Console.WriteLine((object)s);
        Console.WriteLine((object)us);
        Console.WriteLine((object)b);
        Console.WriteLine((object)sby);
        Console.WriteLine((object)dow);
        Console.WriteLine((object)c);
        Console.WriteLine((object)t);
        Console.WriteLine((object)d);
        Console.WriteLine((object)f);
        Console.WriteLine((object)null);
        Console.WriteLine((object)"text");
    }
}}