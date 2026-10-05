//------------------------------------------------------------------------------
// consoledeep.cs -- System.Console DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C18 (2026-08-10).
//
// EXTENDS consolecase/consoleio/console2/console3 on the WIRED console-STREAM
// surface (MethodMap.Console.cs get_Out/get_Error/SetOut/SetError +
// console.cpp ConsoleStreamObject + TextWriter.Null):
//
//   1) ERROR-STREAM ORDER DETERMINIZATION: Console.SetError(Console.Out)
//      folds the error stream into stdout BEFORE any output, so every
//      Console.Error.Write/WriteLine lands on the SAME single stream in
//      PROGRAM ORDER -- no stderr/stdout buffering interleave (the console3
//      2>&1 capture hazard where the block-buffered stdout block and the
//      unbuffered stderr lines arrive out of write order). Every line below
//      is therefore byte-deterministic against the net48 oracle.
//   2) REDIRECTION: Console.Out is saved (a real console-stream object),
//      SetOut(System.IO.TextWriter.Null) swallows Console.Write / WriteLine /
//      Console.Out.Write* afterwards, and SetOut(saved) RESTORES output.
//      The same swallow/restore cycle runs on the error side.
//   3) TextWriter.Null: passed to SetOut/SetError it becomes the discarding
//      stream (console.cpp kConsoleStreamNull kind; net48 discards too).
//   4) SetOut(null) / SetError(null) throw ArgumentNullException (net48
//      paramName "newOut"/"newError"; console.cpp kArgumentNullChain).
//   5) getters: Console.Out / Console.Error are non-null, DISTINCT, stable
//      singletons (ReferenceEquals roundtrips).
//
// Every active line prints deterministically on stdout; net48 is the byte
// reference. All writes go through the wired console stream icalls.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class ConsoleDeep {
    public static void Main()
    {
        // ===================== 1. getters =====================
        Console.WriteLine("g_out_nn=" + (Console.Out != null));
        Console.WriteLine("g_err_nn=" + (Console.Error != null));
        Console.WriteLine("g_distinct=" + !ReferenceEquals(Console.Out, Console.Error));
        Console.WriteLine("g_out_stable=" + ReferenceEquals(Console.Out, Console.Out));
        Console.WriteLine("g_err_stable=" + ReferenceEquals(Console.Error, Console.Error));

        // ============= 2. error stream folded into stdout =============
        // Determinizes the 2>&1 capture: ONE stream, PROGRAM order.
        Console.SetError(Console.Out);
        Console.Error.WriteLine("e1");
        Console.Out.WriteLine("o1");
        Console.Error.Write("e2"); Console.Error.WriteLine();
        Console.WriteLine("o2");
        Console.Error.Write("e3"); Console.Out.Write("|"); Console.Error.WriteLine("e4");
        Console.Error.WriteLine("e5");

        // ============= 3. SetOut(TextWriter.Null) swallow + restore ====
        TextWriter savedOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        Console.WriteLine("swallowed1");
        Console.Write("swallowed2");
        Console.Out.WriteLine("swallowed3");
        Console.Out.Write("swallowed4");
        Console.SetOut(savedOut);
        Console.WriteLine("back1");
        Console.WriteLine("back2");

        // ============= 4. SetError(TextWriter.Null) swallow + fold ====
        TextWriter savedErr = Console.Error;
        Console.SetError(TextWriter.Null);
        Console.Error.WriteLine("err_swallowed1");
        Console.Error.WriteLine("err_swallowed2");
        Console.Error.Write("err_swallowed3");
        // fold the error stream back onto the OUT stream (deterministic tail)
        Console.SetError(savedOut);
        Console.Error.WriteLine("err_visible1");
        Console.WriteLine("o_after_err");
        // savedErr == the pre-Null error stream (the Out stream object after
        // section 2's fold) -- verify it is a REAL non-null writer object.
        Console.WriteLine("saved_err_nn=" + (savedErr != null));
        Console.WriteLine("saved_err_isout=" + ReferenceEquals(savedErr, savedOut));

        // ============= 5. SetOut(null) / SetError(null) =============
        bool threw = false;
        try { Console.SetOut(null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("setout_null=" + threw);
        threw = false;
        try { Console.SetError(null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("seterror_null=" + threw);

        // ============= 6. default streams still route (getter-only) ===
        // The default Out/Error singleton objects remain valid writers.
        TextWriter o = Console.Out;
        TextWriter e = Console.Error;
        Console.WriteLine("final_out_nn=" + (o != null));
        Console.WriteLine("final_err_nn=" + (e != null));
    }
}}
