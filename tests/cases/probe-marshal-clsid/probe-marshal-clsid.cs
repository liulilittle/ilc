using System;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int _fail;

    private static void Expect(bool ok, string what)
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        if (!ok) _fail++;
    }

    private static int Main()
    {
        // net8 does NOT return null for an unregistered CLSID: it hands back a
        // transparent RCW whose runtime type is System.__ComObject. That is the
        // real contract, confirmed against the oracle -- an earlier version of
        // this probe assumed null and was wrong.
        Type got = Marshal.GetTypeFromCLSID(Guid.Empty);
        Expect(got != null, "a.not_null=" + (got == null ? "null" : "yes"));
        Expect(got != null && got.FullName == "System.__ComObject",
            "b.type=" + (got == null ? "null" : got.FullName));

        Type again = Marshal.GetTypeFromCLSID(Guid.Empty);
        Expect(again != null && again.FullName == "System.__ComObject",
            "c.repeat=" + (again == null ? "null" : again.FullName));

        return _fail == 0 ? 0 : 1;
    }
}
