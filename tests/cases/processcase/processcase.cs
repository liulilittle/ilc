using System;
using System.Diagnostics;

// processcase.cs -- net48 reference oracle for the System.Diagnostics.Process
// domain: the PID handle-gap mapping (MethodMap.Process.cs + pathproc.h).
//
// Probe (the task's core probe, environment-adapted): Start(exe, args) ->
// WaitForExit() -> ExitCode == 42. The child is exit42.exe -- a GUI-subsystem
// child built from exit42.c (WinMain returns 42) -- instead of cmd.exe:
// net48's Process.Start(string, string) launches console children via
// ShellExecuteEx, which creates a NEW console -- blocked in this headless
// session (cmd/ping children hang forever; verified). A GUI child needs no
// console and a fixed WinMain return value makes the exit code exact. The
// transpiled side uses CreateProcessW directly (CREATE_NO_WINDOW, pathproc.h)
// and is console-independent -- both sides must print the same line:
//
//   exit code 42: True
//   current pid > 0: True
//
// The second probe is the GetCurrentProcess mapping probe: the static
// factory folds to il2cpp_process_get_current_process_id (handle-gap PID
// value slot) and get_Id folds to its receiver (the pid itself), so
// Process.GetCurrentProcess().Id > 0 must print True on both sides.
//
// The relative exe path resolves against the harness CWD (E:\il2cpp for
// both the net48 reference run and the generated exe run -- run-tests.ps1
// does not change the caller's working directory).
//
// StandardOutput.ReadToEnd is NOT diffable here: net48 throws
// InvalidOperationException on StandardOutput for a ShellExecute'd process
// (no redirection), while the transpiled side reads the redirected capture
// -- an inherent oracle divergence, so the stdout chain is verified
// transpile-only (see the domain report). The ProcessStartInfo form that
// would enable net48 redirection is LOUD-rejected (no object model), and
// stdin is never redirected (pathproc.cpp STARTF_USESTDHANDLES: inherited
// or NUL) -- StandardInput is LOUD-rejected too.
class ProcessCase
{
    static void Main()
    {
        Process p = Process.Start("tests\\cases\\processcase\\exit42.exe", "");
        p.WaitForExit();
        Console.WriteLine("exit code 42: " + (p.ExitCode == 42));
        Console.WriteLine("current pid > 0: " + (Process.GetCurrentProcess().Id > 0));
    }
}
