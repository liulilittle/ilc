//------------------------------------------------------------------------------
// console2.cs -- System.Console DEEP e2e reference case (net48 oracle).
//
// Extension of consolecase/consoleio: the WIRED Console surface (MethodMap.
// Console.cs + il2cpp_console.h / runtime.cpp) probed deeper --
//
//   1) Write NO-NEWLINE combos: long interleaved Write sequences mixing every
//      wired Write overload (string/int/uint/bool/char/double), terminated by
//      a bare WriteLine(). Byte-exact concatenation on one line.
//   2) WriteLine FULL format surface: every wired overload -- () / bool /
//      char / int (incl. Int32.MinValue/MaxValue) / uint / long (incl.
//      MaxValue) / ulong (MaxValue) / double / float / string, plus the
//      tracked-box object overload (Int32/Boolean/Char/Double/String).
//   3) String.Format COMPOSITE NESTING: multi-arg fixed arities with a NESTED
//      inner String.Format as an argument, reordered placeholders, repeated
//      index, and {{ }} escaping -- all through the wired icall_string_format
//      kernel (stringbuilder.cpp AppendFormatCore).
//
// RECORDED GAPS (parked here -- each would break the transpile or the diff):
//   * error-stream redirection (Console.Error / SetError / SetOut): get_Error
//     has no MethodMap.Console.cs entry -> generated-name undefined symbol ->
//     build failure (batchB D-CON-2, consoleio parked the same).
//   * over-wide alignment String.Format("{N,width}"): the runtime kernel
//     THROWS NotSupportedException on the alignment branch (D-CON-4). format2
//     already probes this defect actively; console2 parks it to stay green.
//   * Console.WriteLine(string, object) composite overloads: TryMapConsole
//     dispatches on ps[0] only, an arity mismatch (D-CON-5).
//   * Console.ReadKey (blocks on EOF) / IsInputRedirected (LOUD).
//
// Every active line uses ONLY the wired surface; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class Console2 {
    public static void Main() {
        // ---- 1. Write no-newline combos (interleaved wired types) ----
        Console.Write("A"); Console.Write(1); Console.Write(true);
        Console.Write('c'); Console.Write(2.5); Console.WriteLine();
        Console.Write(-7); Console.Write(4000000000u); Console.Write("s");
        Console.Write(false); Console.Write(0.125); Console.WriteLine();
        Console.Write('x'); Console.Write(1234567890); Console.Write(0.0);
        Console.Write("tail"); Console.WriteLine();
        Console.Write(0); Console.Write('0'); Console.Write(true);
        Console.Write(2.75); Console.Write("|"); Console.Write(-1); Console.WriteLine();

        // ---- 2. WriteLine full format surface ----
        Console.WriteLine();
        Console.WriteLine(true);
        Console.WriteLine(false);
        Console.WriteLine('A');
        Console.WriteLine('9');
        Console.WriteLine(0);
        Console.WriteLine(42);
        Console.WriteLine(-7);
        Console.WriteLine(2147483647);
        Console.WriteLine(-2147483648);
        Console.WriteLine(4000000000u);
        Console.WriteLine(0L);
        Console.WriteLine(1234567890123L);
        Console.WriteLine(9223372036854775807L);
        Console.WriteLine(-9876543210L);
        Console.WriteLine(18446744073709551615UL);
        Console.WriteLine(0.5);
        Console.WriteLine(-0.25);
        Console.WriteLine(100.0);
        Console.WriteLine(0.1);
        Console.WriteLine(150.0);
        Console.WriteLine(1e21);
        Console.WriteLine(12345.6789);
        Console.WriteLine(1.5f);
        Console.WriteLine(-2.5f);
        Console.WriteLine(100.0f);
        Console.WriteLine("plain");
        Console.WriteLine("");
        Console.WriteLine((object)42);
        Console.WriteLine((object)(-3));
        Console.WriteLine((object)true);
        Console.WriteLine((object)'X');
        Console.WriteLine((object)2.75);
        Console.WriteLine((object)"boxedstr");

        // ---- 3. String.Format composite nesting (fixed arities, nested) ----
        int iv = 5;
        string sv = "ab";
        double dv = 1.25;
        Console.WriteLine(String.Format("{0}", iv));
        Console.WriteLine(String.Format("[{0}]", sv));
        Console.WriteLine(String.Format("{0}+{1}={2}", iv, 2, iv + dv));
        Console.WriteLine(String.Format("{0} {1} {0}", "x", "y"));
        Console.WriteLine(String.Format("{2} {0} {1}", "p", "q", "r"));
        Console.WriteLine(String.Format("{{{0}}} {2} {1} {{}}", 1, 2, 3));
        Console.WriteLine(String.Format("{0}->{1}", iv, String.Format("{0}:{0}", sv)));
        Console.WriteLine(String.Format("{0}[{1}]{0}", iv, String.Format("({0},{1})", dv, true)));
        Console.WriteLine(String.Format("a}}b {{0}} c", 0));
    }
}}
