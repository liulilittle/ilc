//------------------------------------------------------------------------------
// pinvoke-bvaltstr.cs -- [MarshalAs(UnmanagedType.ByValTStr, SizeConst = N)]
// fields are INLINE fixed buffers in the native struct (net48 ABI).
//
// MEASURED on the net48 oracle (this case's own struct, see the seat NOTES):
//   LOGFONTW { int32, int32, int32, ByValTStr(32) }
//       Marshal.SizeOf == 76, Marshal.OffsetOf(lfFaceName) == 12
//       a 32-unit input string truncates to 31 units + NUL
//       a full 32-unit no-NUL native buffer reads back as 32 chars
//   BvtHeadTail { int32, ByValTStr(8), int32 }
//       Marshal.SizeOf == 24, name offset 4, tail offset 20
//
// The transpiled struct must therefore be the SAME byte layout: a char16_t
// inline array (NOT an Il2CppObject* GC reference). The fixture below reads
// and writes the struct through a pointer, so a GC-pointer field / wrong size
// shows up as a field-value diff against the oracle.
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

// A ByValTStr between two scalars: pins the inline buffer OFFSET and the
// struct's total size (tail must stay at offset 20, size 24).
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct BvtHeadTail
{
    public int head;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
    public string name;

    public int tail;
}

public static class ByValTStrProbe
{
    [DllImport("bvaltstrprobe.dll")]
    private static extern int lf_sizeof();

    [DllImport("bvaltstrprobe.dll")]
    private static extern int lf_face_offset();

    [DllImport("bvaltstrprobe.dll")]
    private static extern int lf_roundtrip(ref LOGFONTW lf);

    [DllImport("bvaltstrprobe.dll")]
    private static extern int lf_measure(ref LOGFONTW lf);

    [DllImport("bvaltstrprobe.dll")]
    private static extern int lf_write_full(ref LOGFONTW lf);

    [DllImport("bvaltstrprobe.dll")]
    private static extern int ht_sizeof();

    [DllImport("bvaltstrprobe.dll")]
    private static extern int ht_name_offset();

    [DllImport("bvaltstrprobe.dll")]
    private static extern int ht_tail_offset();

    [DllImport("bvaltstrprobe.dll")]
    private static extern int ht_roundtrip(ref BvtHeadTail ht);

    public static void Main()
    {
        // Native ABI of the struct the CLR marshals: the transpiled C++ struct
        // must agree on every byte the fixture touches below.
        Record("lf.size", lf_sizeof());
        Record("lf.face.offset", lf_face_offset());

        var lf = new LOGFONTW();
        lf.lfHeight = 12;
        lf.lfWidth = 34;
        lf.lfWeight = 56;
        lf.lfFaceName = "Segoe UI";

        // The fixture reads the inline buffer, checksums it with the ints, then
        // writes DIFFERENT values (ints + a new face string) back.
        Record("lf.ret", lf_roundtrip(ref lf));
        Record("lf.height", lf.lfHeight);
        Record("lf.width", lf.lfWidth);
        Record("lf.weight", lf.lfWeight);
        Record("lf.face", lf.lfFaceName);

        // net48 input semantics: SizeConst=32 keeps at most 31 units + NUL.
        lf.lfFaceName = "ABCDEFGHIJKLMNOPQRSTUVWXYZ012345";
        Record("lf.truncate", lf_measure(ref lf));

        // net48 output semantics: a full 32-unit buffer without a NUL unit
        // reads back as 32 chars.
        Record("lf.full.ret", lf_write_full(ref lf));
        Record("lf.full.len", lf.lfFaceName.Length);
        Record("lf.full", lf.lfFaceName);

        // ByValTStr between scalars: offsets + round trip.
        Record("ht.size", ht_sizeof());
        Record("ht.name.offset", ht_name_offset());
        Record("ht.tail.offset", ht_tail_offset());
        var ht = new BvtHeadTail();
        ht.head = 7;
        ht.name = "Hello";
        ht.tail = 9;
        Record("ht.ret", ht_roundtrip(ref ht));
        Record("ht.head", ht.head);
        Record("ht.tail", ht.tail);
        Record("ht.name", ht.name);
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
