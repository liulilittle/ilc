//------------------------------------------------------------------------------
// consoleinterop.cs -- CONSOLE INTERACTION DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C63 (2026-08-12).
//
// Extends consolecase/consoleio/console2/console3/consoledeep/consolestream
// on the WIRED console surface (MethodMap.Console.cs + console.cpp) with a
// single focused theme: the INTERACTION of the three console sub-domains on
// one deterministic stdout stream:
//
//   A. OUTPUT FORMAT -- the full typed Write/WriteLine overload matrix
//      including BOUNDARY values (int/long min/max, uint/ulong max), raw
//      double/float prints, and composite String.Format "{N,w}" alignment +
//      "{N:f2}" precision (the wired composite kernel).
//   B. ENCODING -- code-unit probes ((int)char, string.Length, Substring,
//      code-point equality) that stay byte-identical regardless of console
//      codepage: ALL raw output is ASCII (the consoledeep/consolestream
//      convention -- wide-char lines are codepage-fragile on this host).
//   C. STREAM -- the error stream FOLDED into stdout
//      (Console.SetError(Console.Out)) so every Console.Error write
//      interleaves in PROGRAM order (order determinization, the consoledeep
//      pattern); SetOut(TextWriter.Null) swallow + restore around a format
//      write; SetOut(null)/SetError(null) ArgumentNullException; getters
//      non-null / distinct / stable.
//
// Every active line prints deterministically on stdout; net48 is the byte
// reference. All bool probes print True/False (per e2e convention).
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class ConsoleInterop {
    public static void Main()
    {
        // ================== A. OUTPUT FORMAT: typed matrix ==================
        // A.1 WriteLine typed overloads -- boundary values + raw formats.
        Console.WriteLine(0);
        Console.WriteLine(-7);
        Console.WriteLine(int.MaxValue);
        Console.WriteLine(int.MinValue);
        Console.WriteLine(4294967295u);
        Console.WriteLine(4000000000u);
        Console.WriteLine(long.MaxValue);
        Console.WriteLine(long.MinValue);
        Console.WriteLine(18446744073709551615UL);
        Console.WriteLine(1234567890123L);
        Console.WriteLine(2.5);
        Console.WriteLine(-0.25);
        Console.WriteLine(100.0);
        Console.WriteLine(0.125);
        Console.WriteLine(1e21);
        Console.WriteLine(3.5);
        Console.WriteLine(-2.25);
        Console.WriteLine(150.0);
        Console.WriteLine(1.5f);
        Console.WriteLine('A');
        Console.WriteLine(true);
        Console.WriteLine(false);
        Console.WriteLine("plain");

        // A.2 tracked-box object overloads (consoleio-proven shapes).
        Console.WriteLine((object)42);
        Console.WriteLine((object)(-3));
        Console.WriteLine((object)true);
        Console.WriteLine((object)'X');
        Console.WriteLine((object)2.75);
        Console.WriteLine((object)"boxedstr");

        // A.3 Write combos -- interleaved no-newline typed writes, one line.
        Console.Write("n="); Console.Write(42); Console.Write("|"); Console.Write(-7);
        Console.Write("|"); Console.Write(4000000000u); Console.Write("|");
        Console.Write(true); Console.Write("|"); Console.Write('c'); Console.Write("|");
        Console.Write(2.5); Console.WriteLine();
        Console.Write("mix="); Console.Write("a"); Console.Write(1); Console.Write(false);
        Console.Write('z'); Console.Write(0.125); Console.Write("tail"); Console.WriteLine();

        // A.4 composite String.Format -- alignment + precision + multi-arg
        //     (the console3-proven wired kernel: stringbuilder.cpp
        //     AppendFormatCore -> icall_format_composite_item).
        Console.WriteLine(String.Format("[{0,5}]", 42));
        Console.WriteLine(String.Format("[{0,-5}]", "ab"));
        Console.WriteLine(String.Format("[{0,8}]", "hi"));
        Console.WriteLine(String.Format("{0,8:f2}", 3.14159));
        Console.WriteLine(String.Format("[{0,-4}|{1,4}]", "x", "y"));
        Console.WriteLine(String.Format("[{0,5}|{1,6}]", 12, -345));
        Console.WriteLine(String.Format("[{0,5}]", 42) == "[   42]");
        Console.WriteLine(String.Format("[{0,-5}]", "ab") == "[ab   ]");
        Console.WriteLine(String.Format("{0,8:f2}", 3.14159) == "    3.14");
        Console.WriteLine(String.Format("{2} {0} {1}", "x", "y", "z") == "z x y");

        // ================== B. ENCODING: code-unit probes ==================
        // ASCII-only output; the probes pin the UTF-16 code-unit surface.
        Console.WriteLine((int)'A' == 65);
        Console.WriteLine((int)'0' == 48);
        Console.WriteLine((int)'z' == 122);
        Console.WriteLine("hello".Length == 5);
        Console.WriteLine("hello".Substring(1, 3) == "ell");
        Console.WriteLine("A\u00e9B".Length == 3);
        Console.WriteLine("A\u00e9B".Substring(1, 1) == "\u00e9");
        Console.WriteLine("tab\tend".Length == 7);
        Console.WriteLine((int)'\t' == 9);
        Console.WriteLine("line\nfeed".Substring(4, 1) == "\n");

        // ================== C. STREAM: fold + swallow + restore ==================
        // C.1 getters: non-null, distinct, stable (consoledeep pattern).
        Console.WriteLine("g_out_nn=" + (Console.Out != null));
        Console.WriteLine("g_err_nn=" + (Console.Error != null));
        Console.WriteLine("g_distinct=" + !ReferenceEquals(Console.Out, Console.Error));
        Console.WriteLine("g_out_stable=" + ReferenceEquals(Console.Out, Console.Out));

        // C.2 fold the error stream onto stdout BEFORE any output: every
        //     Console.Error write then lands on stdout in PROGRAM order.
        Console.SetError(Console.Out);
        Console.Error.Write("err1");
        Console.Out.Write("|out1|");
        Console.Error.WriteLine("err2");
        Console.Error.Write("err3");
        Console.Write("|out2|");
        Console.Error.WriteLine("err4");
        Console.Error.WriteLine("err5");

        // C.3 SetOut(TextWriter.Null) swallow + restore around a format write.
        TextWriter savedOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        Console.WriteLine("swallowed1");
        Console.Write("swallowed2");
        Console.Out.WriteLine(String.Format("[{0,4}]", 7));
        Console.SetOut(savedOut);
        Console.WriteLine("back1");
        Console.WriteLine("back2");

        // C.4 SetOut(null) / SetError(null) -> ArgumentNullException.
        bool threw = false;
        try { Console.SetOut(null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("setout_null=" + threw);
        threw = false;
        try { Console.SetError(null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("seterror_null=" + threw);

        // C.5 final getters still route (stream objects remain valid writers).
        TextWriter o = Console.Out;
        Console.WriteLine("final_out_nn=" + (o != null));
        Console.WriteLine("final_err_nn=" + (Console.Error != null));
    }
}}
