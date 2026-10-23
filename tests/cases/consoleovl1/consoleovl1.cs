using System;
using System.Text;

namespace Cases { public static class ConsoleOvl1 {
    public static void Main()
    {
        long l = -9000000000L;
        ulong ul = 18446744073709551615UL;
        uint ui = 4294967295u;
        short s = -12345;
        ushort us = 54321;
        byte b = 200;
        sbyte sby = -100;
        decimal m = 1234.5678m;
        object o = l;

        Console.WriteLine((object)l);
        Console.WriteLine((object)ul);
        Console.WriteLine((object)ui);
        Console.WriteLine((object)s);
        Console.WriteLine((object)us);
        Console.WriteLine((object)b);
        Console.WriteLine((object)sby);
        Console.WriteLine(o);

        Console.Write(l); Console.WriteLine();
        Console.Write(ul); Console.WriteLine();
        Console.Write(m); Console.WriteLine();
        Console.Write((object)l); Console.WriteLine();
        Console.Write((object)ul); Console.WriteLine();
        Console.Write((object)ui); Console.WriteLine();

        Console.WriteLine("a{0}b", (object)l);
        Console.WriteLine("a{0}b{1}", (object)ul, (object)ui);
        Console.WriteLine("{0}", m);

        Console.WriteLine(new[] { 'x', 'y', 'z' });
        Console.WriteLine(new[] { 'p', 'q', 'r' }, 1, 2);

        Console.Write(new[] { 'k', 'l' });
        Console.WriteLine();

        Console.WriteLine("redir_in=" + Console.IsInputRedirected);
        Console.WriteLine("redir_out=" + Console.IsOutputRedirected);
        Console.WriteLine("redir_err=" + Console.IsErrorRedirected);

        Console.WriteLine(1234.5678m);

        StringBuilder sbx = new StringBuilder();
        sbx.Append('A');
        Console.WriteLine(sbx);
    }
}}
