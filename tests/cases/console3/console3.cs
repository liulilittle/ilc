//------------------------------------------------------------------------------
// console3.cs -- System.Console DEEP e2e reference case (net48 oracle).
// Extends consolecase/consoleio/console2 on the WIRED surface
// (MethodMap.Console.cs + console.cpp):
//
//   1) Write COMBOS: long interleaved no-newline Write sequences mixing every
//      wired Write overload (string / int / uint / bool / char / double),
//      terminated by a bare WriteLine() -- byte-exact concatenation per line.
//   2) WIDE CHARS: raw printing of BMP wide characters through Write(char),
//      Write(string), WriteLine(string) and the tracked-box WriteLine(object)
//      (Char box) -- the runtime renders UTF-16 code units via UTF-8
//      (PrintCharCode / WriteStringUtf8; net48 oracle captured through the
//      harness decodes both sides to the same code points); PLUS code-unit /
//      Length probes that stay encoding-independent.
//   3) ALIGNMENT: composite String.Format "{N,w}" / "{N,w:spec}" (the wired
//      format kernel -- format2/format3 proven) printed raw AND compared
//      against the net48 string.
//
// DEFECT PROBES (recorded, NOT fixed -- batch rule): the ERROR STREAM and
// REDIRECTION surface is NOT wired -- Console.Error / Console.Out have no
// MethodMap.Console.cs entry (batchB D-CON-2; console2 parked the same) and
// Console.SetOut / Console.SetError are unmapped -> the resolver falls to the
// generated-name path -> undefined C++ identifier -> cmake build fails. The
// probes below pin the net48 semantics (stderr writes interleave into the
// 2>&1 capture; SetOut/SetError(null writer) swallow subsequent output).
//
// Every active line prints deterministically; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Console3 {
    public static void Main() {
        // ---- 1. Write combos: interleaved wired types, one line each ----
        Console.Write("n="); Console.Write(42); Console.Write("|"); Console.Write(-7);
        Console.Write("|"); Console.Write(4000000000u); Console.Write("|");
        Console.Write(true); Console.Write("|"); Console.Write(false); Console.WriteLine();
        Console.Write("d="); Console.Write(2.5); Console.Write("|"); Console.Write(0.125);
        Console.Write("|"); Console.Write(100.0); Console.Write("|"); Console.Write(-0.25);
        Console.Write("|"); Console.Write(1e21); Console.WriteLine();
        Console.Write("c="); Console.Write('A'); Console.Write("|"); Console.Write('9');
        Console.Write("|"); Console.Write('0'); Console.Write("|"); Console.Write("tail"); Console.WriteLine();
        Console.Write("mix="); Console.Write(1234567890); Console.Write(0.0); Console.Write('x');
        Console.Write(3.25); Console.Write("z"); Console.WriteLine();

        // ---- 2. wide chars: raw print + code-unit / Length probes ----
        Console.Write("w="); Console.Write('\u4E2D'); Console.Write('\u6587');
        Console.Write(' '); Console.Write('\u00E9'); Console.Write(' '); Console.Write('\u03A9'); Console.WriteLine();
        Console.WriteLine("ws=" + "\u4E2D\u6587X");
        Console.Write("\u03A9=\u4E2D"); Console.Write('!'); Console.Write("\u6587"); Console.WriteLine();
        Console.WriteLine("w0=" + ((int)'\u4E2D' == 0x4E2D));
        Console.WriteLine("w1=" + ((int)'\u00E9' == 0xE9));
        Console.WriteLine("w2=" + ((int)'\u03A9' == 0x3A9));
        Console.WriteLine("w3=" + ("\u4E2D\u6587X".Length == 3));
        Console.WriteLine("w4=" + ("\u4E2D\u6587X".Substring(1, 1) == "\u6587"));
        Console.WriteLine((object)'\u4E2D');
        Console.WriteLine((object)'\u03A9');
        Console.Write("ws2="); Console.Write('\u4E2D'); Console.Write('\u00E9'); Console.Write('\u03A9'); Console.WriteLine();

        // ---- 3. alignment via composite String.Format (wired kernel) ----
        Console.WriteLine(String.Format("[{0,5}]", 42));
        Console.WriteLine(String.Format("[{0,-5}]", "ab"));
        Console.WriteLine(String.Format("[{0,8}]", "hi"));
        Console.WriteLine(String.Format("{0,8:f2}", 3.14159));
        Console.WriteLine(String.Format("[{0,3}]", 12345));
        Console.WriteLine(String.Format("[{0,-4}|{1,4}]", "x", "y"));
        Console.WriteLine("a0=" + (String.Format("[{0,5}]", 42) == "[   42]"));
        Console.WriteLine("a1=" + (String.Format("[{0,-5}]", "ab") == "[ab   ]"));
        Console.WriteLine("a2=" + (String.Format("[{0,10:f2}]", 3.14159) == "[      3.14]"));
        Console.WriteLine("a3=" + (String.Format("[{0,3}]", 12345) == "[12345]"));

        // ===== DEFECT PROBES: error stream + redirection (NOT wired) =====
        Console.Error.WriteLine("errline1");
        Console.Error.Write("err"); Console.Error.WriteLine();
        Console.Out.WriteLine("outline1");
        Console.SetOut(System.IO.TextWriter.Null);
        Console.SetError(System.IO.TextWriter.Null);
        Console.WriteLine("after_setout");
        Console.Error.WriteLine("after_seterror");
    }
}}
