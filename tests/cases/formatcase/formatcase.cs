using System;
namespace Cases { public static class FormatCase {
    public static void Main() {
        // int32/int64 decimal ToString (maps to icall_int32_to_string /
        // icall_int64_to_string once the transpiler wires the mapping).
        Console.WriteLine(123.ToString());
        Console.WriteLine((-42).ToString());
        Console.WriteLine(0.ToString());
        Console.WriteLine(int.MinValue.ToString());
        Console.WriteLine(int.MaxValue.ToString());
        Console.WriteLine(1234567890123456789L.ToString());
        Console.WriteLine(long.MinValue.ToString());
        // bool ToString (.NET semantics: capitalized True/False).
        Console.WriteLine(true.ToString());
        Console.WriteLine(false.ToString());
        // char ToString.
        Console.WriteLine('A'.ToString());
        // double ToString (net48 default "G15": 15 significant digits).
        Console.WriteLine(3.5.ToString());
        Console.WriteLine((1.0 / 3.0).ToString());
        Console.WriteLine(Math.PI.ToString());
        Console.WriteLine((0.1 + 0.2).ToString());
        Console.WriteLine(1e-4.ToString());
        Console.WriteLine(1e-5.ToString());
        Console.WriteLine(1e-6.ToString());
        Console.WriteLine(100000000000000.0.ToString());
        Console.WriteLine(1e15.ToString());
        Console.WriteLine(1.5e20.ToString());
        Console.WriteLine(2.5e-10.ToString());
        Console.WriteLine(123456789012345678.0.ToString());
        Console.WriteLine(double.NaN.ToString());
        // R5: ∞ ToString() output lines avoid byte comparison -- net48 console
        // encodes U+221E as GBK A1 DE, the ilc runtime writes UTF-8 E2 88 9E.
        // Assert the formatted CONTENT in-process (both sides compare their own
        // U+221E string) -- the output is a plain "True", encoding-free.
        Console.WriteLine(double.PositiveInfinity.ToString() == "\u221E");
        Console.WriteLine(double.NegativeInfinity.ToString() == "-\u221E");
        Console.WriteLine((-0.0).ToString());
        Console.WriteLine(double.Epsilon.ToString());
        // int.Parse results (maps to icall_string_parse_int).
        Console.WriteLine(int.Parse("42").ToString());
        Console.WriteLine(int.Parse("  -17  ").ToString());
        Console.WriteLine(int.Parse("+8").ToString());
        Console.WriteLine(int.Parse("00042").ToString());
        // hex (maps to icall_int32_to_string_hex).
        Console.WriteLine(255.ToString("X"));
        Console.WriteLine(16.ToString("X"));
        Console.WriteLine(0.ToString("X"));
        Console.WriteLine((-1).ToString("X"));
        Console.WriteLine(int.MinValue.ToString("X"));
        // double.Parse result (maps to icall_string_parse_double).
        Console.WriteLine(double.Parse("2.5e2").ToString());
    }
}}
