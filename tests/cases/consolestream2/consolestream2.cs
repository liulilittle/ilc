using System;
namespace Cases { public static class ConsoleStream2 {
    //------------------------------------------------------------------------------
    // consolestream2.cs -- Console STREAM VALUE + COMPOSITE surface (net48 oracle,
    // byte-for-byte). Extends consolestream (the string/null/SetOut surface)
    // with the value-typed TextWriter.Write/WriteLine overloads and the
    // composite format forms on Console.Out / Console.Error.
    //
    // The runtime renders each value with the SAME formatter the matching
    // Console.Write/WriteLine core icall uses (so the stream text and the
    // console text agree byte-for-byte) and writes to the stream object's
    // target (stdout / stderr / discard).
    //
    // ALL-ASCII output by design (the harness byte-diff is codepage-fragile for
    // wide-char lines); bool/int/uint/ulong/decimal/float print ASCII.
    //------------------------------------------------------------------------------
    public static void Main() {
        // ---- A. Write (no newline) value surface ----
        Console.Out.Write(42);                      // int
        Console.Out.Write(".");
        Console.Out.Write(4294967295u);            // uint (full range)
        Console.Out.Write(".");
        Console.Out.Write(18446744073709551615UL); // ulong (full range)
        Console.Out.Write(".");
        Console.Out.Write(1234.5678m);              // decimal
        Console.Out.Write(".");
        Console.Out.Write(1.5f);                    // float (double slot)
        Console.Out.Write(".");
        Console.Out.Write(true);                    // bool
        Console.Out.WriteLine();

        // ---- B. WriteLine value surface ----
        Console.Out.WriteLine(42);
        Console.Out.WriteLine(4294967295u);
        Console.Out.WriteLine(18446744073709551615UL);
        Console.Out.WriteLine(1234.5678m);
        Console.Out.WriteLine(1.5f);
        Console.Out.WriteLine(true);

        // ---- C. composite format forms (String.Format expansion) ----
        Console.Out.WriteLine("a{0}b", 7);
        Console.Out.WriteLine("c{0}d{1}e", 8, "x");
        Console.Out.WriteLine("f{0}g{1}h{2}i", 1, 2, 3);
        Console.Out.WriteLine("{0}-{1}", 5, 6);

        // ---- D. char[] overloads ----
        Console.Out.WriteLine(new[] { 'p', 'q', 'r' });
        Console.Out.Write(new[] { 's', 't' });
        Console.Out.WriteLine();

        // ---- E. the Error stream shares the surface ----
        Console.Error.Write(9);
        Console.Error.WriteLine("-err");
    }
} }
