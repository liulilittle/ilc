//------------------------------------------------------------------------------
// probe-lasterror.cs -- SetLastError=true pre-call semantics probe
// (net48 oracle == transpiled runtime byte-exact).
//
// MEASURED net48 (.NET Framework 4.8, x64) behavior, verified with this probe
// and with tests/cases/pinvokeext (oracle line 7 = "123" -- a stale value
// captured after a successful SetLastError=true call):
//
//   * .NET Framework 4.8 does NOT clear the OS last-error slot before a
//     SetLastError=true P/Invoke. A callee that SUCCEEDS without touching the
//     error slot therefore leaves the PREVIOUS error in place, and the
//     post-call capture stores that stale value (Marshal.GetLastWin32Error
//     answers it).
//   * SetLastError=false never clears and never captures.
//
// The record ids below spell out the oracle contract:
//   [b.sle_true.after_fail.slot] is 126 (STALE) on net48 -- NOT 0. Emitting an
//   il2cpp_pinvoke_clear_last_error() before the call (the .NET Core 3.0+
//   behavior) would print 0 here and diverge from the net48 oracle.
//
// Determinism: no raw pointer/varying values; every record is a fixed int or
// boolean derived from fixed inputs.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbeLastError
    {
        // Direct OS last-error control (SetLastError / GetLastError): both are
        // declared SetLastError=false so neither clears nor captures.
        [DllImport("kernel32.dll", EntryPoint = "SetLastError", SetLastError = false)]
        public static extern void NativeSetLastError(int code);

        [DllImport("kernel32.dll", EntryPoint = "GetLastError", SetLastError = false)]
        public static extern int NativeGetLastError();

        // A failing call that sets ERROR_INVALID_HANDLE (6) and is captured.
        [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
        public static extern int CloseHandleSle(IntPtr handle);

        // A call that always succeeds and never touches the last-error slot.
        // Declared twice: SetLastError=true / SetLastError=false.
        [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess", SetLastError = true)]
        public static extern IntPtr GetCurrentProcessSle();

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess", SetLastError = false)]
        public static extern IntPtr GetCurrentProcessNoSle();

        public static void Main()
        {
            // [a] capture works: a failing SetLastError=true call captures 6.
            NativeSetLastError(0);
            int closed = CloseHandleSle(IntPtr.Zero);
            Console.WriteLine("[a.fail.ret]=" + (closed == 0));
            Console.WriteLine("[a.fail.slot]=" + Marshal.GetLastWin32Error());

            // [b] failure -> success (the success does not touch last error).
            // net48: NO pre-call clear -> the captured slot keeps the stale 6.
            // A clear-before-call runtime would answer 0 (net48 divergence).
            NativeSetLastError(126);
            IntPtr proc = GetCurrentProcessSle();
            Console.WriteLine("[b.pid_nonzero]=" + ((long)proc != 0));
            Console.WriteLine("[b.sle_true.after_fail.slot]=" + Marshal.GetLastWin32Error());
            Console.WriteLine("[b.sle_true.after_fail.os]=" + NativeGetLastError());

            // [c] two consecutive successes after a preset error.
            NativeSetLastError(126);
            GetCurrentProcessSle();
            GetCurrentProcessSle();
            Console.WriteLine("[c.two_success.slot]=" + Marshal.GetLastWin32Error());

            // [d] SetLastError=false must not clear the OS slot.
            NativeSetLastError(126);
            GetCurrentProcessNoSle();
            Console.WriteLine("[d.sle_false.os]=" + NativeGetLastError());

            // [e] failure -> success -> failure.
            NativeSetLastError(0);
            CloseHandleSle(IntPtr.Zero);
            Console.WriteLine("[e.fail1.slot]=" + Marshal.GetLastWin32Error());
            GetCurrentProcessSle();
            Console.WriteLine("[e.success.slot]=" + Marshal.GetLastWin32Error());
            CloseHandleSle(IntPtr.Zero);
            Console.WriteLine("[e.fail2.slot]=" + Marshal.GetLastWin32Error());
        }
    }
}
