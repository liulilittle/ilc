using System;
namespace Cases { public static class ConsoleCase {
    public static void Main() {
        // WriteLine overloads (net48 reference output drives the runtime's
        // formatting contract -- the C++ unit test asserts the same text):
        Console.WriteLine();          // (empty line)
        Console.WriteLine(true);      // True
        Console.WriteLine('A');       // A
        Console.WriteLine(1.5);       // 1.5
        Console.WriteLine(0.1);       // 0.1
        Console.WriteLine(150.0);     // 150
        Console.WriteLine(1e21);      // 1E+21
        Console.WriteLine(double.NaN);              // NaN
        // R5: net48's console encodes ∞ (U+221E) as GBK A1DE while this
        // runtime writes UTF-8 E2 88 9E -- assert the value as a bool instead
        // (mathext precedent: semantics agree on both sides).
        Console.WriteLine(double.PositiveInfinity.ToString() == "\u221E"); // True
        Console.WriteLine("str");     // str

        // Write overloads (no newline) then finish the line:
        Console.Write("w");   // w
        Console.Write(42);    // 42
        Console.Write(false); // False
        Console.Write('x');   // x
        Console.Write(2.25);  // 2.25
        Console.WriteLine();  // (newline)

        // ReadLine probe PARKED: Console.IsInputRedirected is not wired
        // (MethodRefResolver.cs:383 -- the runtime has icall_console_read_line
        // but no is_input_redirected icall), so the guard cannot transpile.
        // An ungated ReadLine would block on an interactive console, so the
        // whole probe is removed until the icall exists.
    }
}}
