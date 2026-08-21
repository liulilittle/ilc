//------------------------------------------------------------------------------
// consolestream.cs -- Console STREAM DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C12 (2026-08-09).
//
// Goes DEEPER than console3 (which pinned the console STREAM surface after the
// M6 batch-35 wiring: Console.Out/Console.Error getters, SetOut/SetError and
// the stream TextWriter.Write/WriteLine icalls -- il2cpp_console.h). Focus:
//   A. the DEFAULT Out stream's Write/WriteLine surface (string / no-arg /
//      null-text -- a null text writes only the terminator, net48 semantics);
//   B. the DEFAULT Error stream's surface (contiguous stderr block);
//   C. SetOut(TextWriter.Null) SWALLOWS subsequent Out writes; SetOut back to
//      the captured original stream RESUMES them (restore cycle);
//   D. SetError(TextWriter.Null) swallows Error writes; restore resumes them;
//   E. DEFECT probes: SetOut(null) / SetError(null) -- net48 throws
//      ArgumentNullException, ours (icall_console_set_out/_error treat a null
//      writer as TextWriter.Null == nullptr, i.e. the discarding stream) does
//      NOT throw -- pinned at the END of the case with a restore, so only the
//      two E_*_ane lines diverge (D-CON-*).
//
// ALL-ASCII output by design: wide-char lines through the harness are
// codepage-fragile (console3's wide lines differ on this host -- the net48
// side decodes via the ANSI codepage, the transpiled side via UTF-8), so
// every probe here stays in the ASCII range for a clean byte diff.
//
// net48 merged-capture note: the harness runs the exe with 2>&1; the merged
// stream ordering is deterministic per harness and identical across the net48
// run and the transpiled run (console3 proved the pattern), so stdout-grouped
// and stderr-grouped blocks keep the diff stable.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class ConsoleStream {
    public static void Main() {
        // ============ A. default Out stream surface ============
        Console.Out.Write("A1a");
        Console.Out.Write("A1b");
        Console.Out.WriteLine();
        Console.Out.WriteLine("A2_line");
        Console.Out.WriteLine();
        Console.Out.WriteLine((string)null);
        Console.Out.Write("A5_no_nl");
        Console.Out.WriteLine();

        // ============ B. default Error stream surface (contiguous) ============
        Console.Error.WriteLine("B1_err");
        Console.Error.Write("B2_err_no_nl");
        Console.Error.WriteLine();
        Console.Error.WriteLine("B3_err");

        // ============ C. SetOut(TextWriter.Null) swallow + restore ============
        System.IO.TextWriter origOut = Console.Out;
        Console.Out.WriteLine("C1_before");
        Console.SetOut(System.IO.TextWriter.Null);
        Console.WriteLine("C2_swallowed");
        Console.Out.WriteLine("C3_swallowed");
        Console.SetOut(origOut);
        Console.Out.WriteLine("C4_restored");
        Console.WriteLine("C5_restored");

        // ============ D. SetError(TextWriter.Null) swallow + restore ============
        System.IO.TextWriter origErr = Console.Error;
        Console.Error.WriteLine("D1_before_err");
        Console.SetError(System.IO.TextWriter.Null);
        Console.Error.WriteLine("D2_swallowed_err");
        Console.SetError(origErr);
        Console.Error.WriteLine("D3_restored_err");

        // ============ E. DEFECT probes: SetOut/SetError(null) -- D-CON-* ============
        // net48: ArgumentNullException (the writer must not be null); ours:
        // a null writer is TextWriter.Null (== nullptr, the discarding stream)
        // -> no throw -> the catch never fires -> False. The restore after
        // each keeps the case self-healing (only the E_*_ane lines diverge).
        bool aneOut = false;
        try { Console.SetOut(null); } catch (ArgumentNullException) { aneOut = true; }
        Console.SetOut(origOut);
        Console.WriteLine("E_out_ane=" + aneOut);
        bool aneErr = false;
        try { Console.SetError(null); } catch (ArgumentNullException) { aneErr = true; }
        Console.SetError(origErr);
        Console.WriteLine("E_err_ane=" + aneErr);
    }
}}
