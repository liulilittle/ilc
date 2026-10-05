// consoleio.cs -- Console input/output composite e2e reference case (net48 baseline).
//
// Probes the WIRED System.Console surface only (MethodMap.Console.cs +
// il2cpp_console.h):
//
//   1) WriteLine full format surface -- every wired overload: () / bool /
//      char / int / uint / long / ulong / double / float / string, plus the
//      tracked-box object overload (Int32/Boolean/Char/Double/String).
//   2) Write no-newline combo (string/int/bool/char/double) then a bare
//      WriteLine() to finish the line.
//   3) ReadLine on EOF stdin: the harness launches both the net48 oracle and
//      the transpiled exe with EOF stdin (no redirection), so Console.ReadLine
//      returns null on BOTH sides (net48 EOF semantics; the runtime's
//      icall_console_read_line returns nullptr when std::getline hits EOF) --
//      deterministic, non-blocking.
//   4) printf-style positional composite formatting via String.Format
//      (icall_string_format_0/1/2/3 wired; the stringbuilder.cpp
//      composite-format kernel). Args use locals so the runtime kernel is
//      exercised, not constant-folded.
//
// Recorded gaps (see GAPS.md, NOT called here -- each would break the
// transpile or the harness): Console.Error / SetOut / SetIn (no icalls),
// Console.ReadKey (_getch blocks on EOF), Console.IsInputRedirected (LOUD
// NotSupportedException in MethodMap.Console.cs), String.Format ALIGNMENT
// "{N,width}" (kernel throws NotSupportedException), and the
// Console.WriteLine(string, object) composite overloads (TryMapConsole
// dispatches on ps[0] only -- a 2-arg IL call would mismatch).

using System;
namespace Cases { public static class ConsoleIo {
    public static void Main() {
        // ---- WriteLine full format surface ----
        Console.WriteLine();
        Console.WriteLine(true);
        Console.WriteLine(false);
        Console.WriteLine('A');
        Console.WriteLine(42);
        Console.WriteLine(-7);
        Console.WriteLine(4000000000u);
        Console.WriteLine(1234567890123L);
        Console.WriteLine(18446744073709551615UL);
        Console.WriteLine(3.5);
        Console.WriteLine(-2.25);
        Console.WriteLine(150.0);
        Console.WriteLine(1.5f);
        Console.WriteLine("plain");
        // tracked-box object overloads
        Console.WriteLine((object)42);
        Console.WriteLine((object)(-3));
        Console.WriteLine((object)true);
        Console.WriteLine((object)'X');
        Console.WriteLine((object)2.75);
        Console.WriteLine((object)"boxedstr");

        // ---- Write combo (no newline) then finish the line ----
        Console.Write("w");
        Console.Write(1);
        Console.Write(false);
        Console.Write('c');
        Console.Write(2.5);
        Console.WriteLine();

        // ---- ReadLine on EOF stdin (null on both sides) ----
        string r1 = Console.ReadLine();
        Console.WriteLine("read1_null=" + (r1 == null ? 1 : 0));
        string r2 = Console.ReadLine();
        Console.WriteLine("read2_null=" + (r2 == null ? 1 : 0));

        // ---- String.Format positional composite (kernel exercised via locals) ----
        int iv = 42;
        double dv = 2.5;
        string sv = "str";
        uint uv = 4000000000u;
        ulong ul = 18446744073709551615UL;
        // NOTE: String.Format(string) single-arg overload is NOT probed: Roslyn
        // lowers it to String.Format(format, Array.Empty<object>()), and the
        // transpiler's Array.Empty<T> wiring covers 0x03/0x08/0x0E only -- the
        // 0x1C (object) arm throws LOUD (MethodMap.Array.cs Empty case). See
        // GAPS.md defect D-CON-1.
        Console.WriteLine(String.Format("{0}", iv));
        Console.WriteLine(String.Format("{0} + {1} = {2}", iv, 2, iv + dv));
        Console.WriteLine(String.Format("{0} and {0}", iv));
        Console.WriteLine(String.Format("[{0}]", sv));
        Console.WriteLine(String.Format("{0}|{1}|{2}", dv, true, 'z'));
        Console.WriteLine(String.Format("{0}", uv));
        Console.WriteLine(String.Format("{0}", ul));
        Console.WriteLine(String.Format("{0} end", "mid"));
    }
}}
