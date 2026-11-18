using System;
using System.Drawing;
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
        // Only the REGISTERED struct family is attempted: a user struct is a
        // documented LOUD boundary for SizeOf/OffsetOf (no layout registry), and
        // exercising it would abort the whole run before the wired cases report.
        int sizePt = Marshal.SizeOf(typeof(Point));
        Expect(sizePt > 0, "a.sizeof_point=" + sizePt);

        int sizeRect = Marshal.SizeOf(typeof(Rectangle));
        Expect(sizeRect > 0, "b.sizeof_rect=" + sizeRect);

        // OffsetOf is deliberately NOT asserted against a BCL type: net8 rejects
        // every System.Drawing field as "not a marshaled member", so the oracle
        // itself cannot establish a value to compare against. Adding an own
        // struct would instead hit ILC's "not a registered struct" boundary, so
        // this probe leaves OffsetOf out rather than assert an unverifiable number.

        IntPtr mem = Marshal.AllocHGlobal(sizePt);
        try
        {
            Marshal.WriteInt32(mem, 11);
            Expect(Marshal.ReadInt32(mem) == 11, "d.hglobal_roundtrip=11");
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }

        return _fail == 0 ? 0 : 1;
    }
}
