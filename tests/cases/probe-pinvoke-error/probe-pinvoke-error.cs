//------------------------------------------------------------------------------
// probe-pinvoke-error.cs -- Marshal.GetLastPInvokeError / SetLastPInvokeError.
//
// These two are the only user-callable unwired icalls in the shipped
// System.Private.CoreLib 8.0.31 (docs/audit/corelib-p0-shipped-recut.md): both
// are public on a public type and both were `unmapped` by the wiring probe.
//
// The contract: SetLastPInvokeError(n) makes GetLastPInvokeError() answer n on
// the same thread. net48 has neither member, so this probe targets net8.0 and
// every line states an invariant value -- no addresses, no culture text.
//
// Every line must match the net8 oracle byte for byte.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

public static class Program
{
    public static void Main()
    {
        // 1. Setting a value makes it readable back.
        Marshal.SetLastPInvokeError(1234);
        Console.WriteLine("a.roundtrip=" + Marshal.GetLastPInvokeError());

        // 2. A second write replaces the first (the slot is not append-only).
        Marshal.SetLastPInvokeError(4321);
        Console.WriteLine("b.replace=" + Marshal.GetLastPInvokeError());

        // 3. Zero is a legal value, not a "no value" sentinel.
        Marshal.SetLastPInvokeError(0);
        Console.WriteLine("c.zero=" + Marshal.GetLastPInvokeError());

        // 4. The full signed range round-trips (the slot is int32, and a
        //    negative Win32 error code is the common real case).
        Marshal.SetLastPInvokeError(-1);
        Console.WriteLine("d.negative=" + Marshal.GetLastPInvokeError());

        // 5. GetLastWin32Error reads the SAME slot (net8 forwards it to
        //    GetLastPInvokeError), so a value set here must be visible there.
        Marshal.SetLastPInvokeError(5678);
        Console.WriteLine("e.win32=" + Marshal.GetLastWin32Error());
    }
}
