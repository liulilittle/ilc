using System;
using System.Diagnostics;

// processsecure.cs -- net48 reference oracle for the SecureString Process.Start
// forms (MethodMap.Process.cs + pathproc.h nb8 PROC-SECURE).
//
// The net48 Process.Start(fileName, userName, SecureString password, domain)
// and Start(fileName, arguments, userName, SecureString password, domain)
// overloads (Process.cs:2288/2299) build a ProcessStartInfo with
// UseShellExecute=false and launch. With a NULL/empty UserName the launch uses
// the CURRENT credentials (CreateProcessW) -- the deterministic path probed
// here (the child is exit42.exe, a GUI-subsystem child whose WinMain returns
// 42, so no console is created).
//
// The credential-launch path (a non-empty UserName -> CreateProcessWithLogonW)
// is OS/credential-state dependent and is reported as a Win32Exception by the
// runtime (documented deviation); it is not diffable here.
//
// The relative exe path resolves against the harness CWD (tests\cases\
// processsecure\exit42.exe), matching processcase.
namespace Cases { public static class ProcessSecure {
    static void Main()
    {
        Process p = Process.Start("tests\\cases\\processsecure\\exit42.exe", null, null, null);
        p.WaitForExit();
        Console.WriteLine("s4_code=" + (p.ExitCode == 42));

        Process p2 = Process.Start("tests\\cases\\processsecure\\exit42.exe", "", null, null, null);
        p2.WaitForExit();
        Console.WriteLine("s5_code=" + (p2.ExitCode == 42));
    }
} }
