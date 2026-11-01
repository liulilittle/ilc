//------------------------------------------------------------------------------
// pinvoke-nameclash.cs -- managed struct names that are ALSO Windows SDK
// typedef names (LOGFONTW, TEXTMETRICW, ...) must not be emitted as
// `struct <typedef-name>`, which is ill-formed C++ after <windows.h>:
//   error: using typedef-name 'LOGFONTW' after 'struct'
//
// The structs live in the GLOBAL namespace on purpose: the generated C++ name
// is the sanitized full name, so `Cases.LOGFONTW` would not collide (it
// becomes Cases_LOGFONTW) -- only a global `LOGFONTW` produces the bare name.
//
// Round trip: each P/Invoke hands the struct BY REFERENCE to this case's own
// native fixture (nameclashprobe.dll). The fixture reads the numeric fields at
// their native offsets, returns a checksum of the IN values and writes NEW
// values back; the managed side prints the returned checksum and the updated
// fields, so the oracle and the transpiled runtime must agree on real field
// values, not merely on "it compiles". The ByValTStr field is carried through
// the same call (net48 copies its text into/out of the native buffer; this
// model keeps the managed reference alive) and must survive untouched.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct LOGFONTW
{
    public int lfHeight;
    public int lfWidth;
    public int lfWeight;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string lfFaceName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct TEXTMETRICW
{
    public int tmHeight;
    public int tmAscent;
    public int tmDescent;
    public int tmWeight;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string tmFaceName;
}

public static class NameClashProbe
{
    [DllImport("nameclashprobe.dll")]
    private static extern int lf_roundtrip(ref LOGFONTW lf);

    [DllImport("nameclashprobe.dll")]
    private static extern int tm_roundtrip(ref TEXTMETRICW tm);

    public static void Main()
    {
        var lf = new LOGFONTW();
        lf.lfHeight = 12;
        lf.lfWidth = 34;
        lf.lfWeight = 56;
        lf.lfFaceName = "Segoe UI";
        Record("lf.ret", lf_roundtrip(ref lf));
        Record("lf.height", lf.lfHeight);
        Record("lf.width", lf.lfWidth);
        Record("lf.weight", lf.lfWeight);
        Record("lf.face", lf.lfFaceName);

        var tm = new TEXTMETRICW();
        tm.tmHeight = 7;
        tm.tmAscent = 8;
        tm.tmDescent = 9;
        tm.tmWeight = 10;
        tm.tmFaceName = "Consolas";
        Record("tm.ret", tm_roundtrip(ref tm));
        Record("tm.height", tm.tmHeight);
        Record("tm.ascent", tm.tmAscent);
        Record("tm.descent", tm.tmDescent);
        Record("tm.weight", tm.tmWeight);
        Record("tm.face", tm.tmFaceName);
    }

    private static void Record(string id, int value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    private static void Record(string id, string value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }
}
