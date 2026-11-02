//------------------------------------------------------------------------------
// pinvoke-bvalarray.cs -- [MarshalAs(UnmanagedType.ByValArray, SizeConst = N)]
// fields are INLINE fixed arrays in the native struct (net48 ABI).
//
// MEASURED on the net48 oracle (Marshal.SizeOf / Marshal.OffsetOf, this seat's
// own probe -- see docs/audit/winform-pinvoke-current.md section 14):
//   BvaIntArray   { int32, int32[4], int32 }              size 24, vals@4, tail@20
//   BvaByteArray  { int32, byte[5] }                      size 12, b@4   (4+5 -> 12)
//   BvaShortArray { int32, int16[3] }                     size 12, s@4   (4+6 -> 12)
//   BvaLongArray  { int32, int64[3] }                     size 32, l@8   (8-byte align)
//   BvaBoolArray  { int32, BOOL[4] }                      size 20, b@4   (bool = 4 bytes!)
//   BvaBool1Array { int32, uint8[4] } (ArraySubType=U1)   size 8,  b@4
//
// The transpiled struct must therefore be the SAME byte layout: an inline
// native array (NOT an Il2CppObject* GC reference). The fixture below reads
// and writes the struct through a pointer, so a GC-pointer field / wrong size
// / wrong element width shows up as a field-value diff against the oracle.
//
// net48 LENGTH semantics (probe-measured): a LONGER managed array truncates
// at SizeConst; a null array marshals as zeros; a SHORTER array throws
// ArgumentException at the marshalling boundary. The last one is documented
// as an out-of-scope boundary (the inline model copies at assignment time,
// where net48 performs no length check), so this case exercises the first
// two only.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct BvaIntArray
{
    public int head;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public int[] vals;

    public int tail;
}

[StructLayout(LayoutKind.Sequential)]
public struct BvaByteArray
{
    public int head;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
    public byte[] b;
}

[StructLayout(LayoutKind.Sequential)]
public struct BvaShortArray
{
    public int head;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public short[] s;
}

[StructLayout(LayoutKind.Sequential)]
public struct BvaLongArray
{
    public int head;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public long[] l;
}

// Default bool elements marshal as 4-byte Win32 BOOL (measured: size 20).
[StructLayout(LayoutKind.Sequential)]
public struct BvaBoolArray
{
    public int head;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public bool[] flags;
}

// Explicit ArraySubType=U1 -> 1-byte cells (measured: size 8).
[StructLayout(LayoutKind.Sequential)]
public struct BvaBool1Array
{
    public int head;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4, ArraySubType = UnmanagedType.U1)]
    public bool[] flags;
}

public static class ByValArrayProbe
{
    [DllImport("bvalarrayprobe.dll")]
    private static extern int int_sizeof();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int int_vals_offset();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int int_tail_offset();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int int_roundtrip(ref BvaIntArray m);

    [DllImport("bvalarrayprobe.dll")]
    private static extern int byte_sizeof();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int byte_offset();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int byte_roundtrip(ref BvaByteArray m);

    [DllImport("bvalarrayprobe.dll")]
    private static extern int short_sizeof();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int short_offset();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int short_roundtrip(ref BvaShortArray m);

    [DllImport("bvalarrayprobe.dll")]
    private static extern int long_sizeof();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int long_offset();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int long_roundtrip(ref BvaLongArray m);

    [DllImport("bvalarrayprobe.dll")]
    private static extern int bool_sizeof();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int bool_offset();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int bool_roundtrip(ref BvaBoolArray m);

    [DllImport("bvalarrayprobe.dll")]
    private static extern int bool1_sizeof();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int bool1_offset();

    [DllImport("bvalarrayprobe.dll")]
    private static extern int bool1_roundtrip(ref BvaBool1Array m);

    public static void Main()
    {
        // ---- int32[4] between two scalars: offsets + size + values ----------
        Record("int.size", int_sizeof());
        Record("int.offset", int_vals_offset());
        Record("int.tail.offset", int_tail_offset());

        var m = new BvaIntArray();
        m.head = 7;
        m.vals = new int[] { 10, 11, 12, 13 };
        m.tail = 9;
        Record("int.ret", int_roundtrip(ref m));
        Record("int.head", m.head);
        Record("int.tail", m.tail);
        Record("int.len", m.vals.Length);
        Record("int.vals", Ints(m.vals));

        // net48 truncates a LONGER source array at SizeConst (probe-measured).
        var mt = new BvaIntArray();
        mt.head = 1;
        mt.vals = new int[] { 10, 11, 12, 13, 14, 15 };
        mt.tail = 2;
        Record("int.trunc.ret", int_roundtrip(ref mt));
        Record("int.trunc.head", mt.head);
        Record("int.trunc.tail", mt.tail);
        Record("int.trunc.len", mt.vals.Length);
        Record("int.trunc.vals", Ints(mt.vals));

        // A null array marshals as all-zero cells (probe-measured).
        var mn = new BvaIntArray();
        mn.head = 1;
        mn.vals = null;
        mn.tail = 2;
        Record("int.null.ret", int_roundtrip(ref mn));
        Record("int.null.head", mn.head);
        Record("int.null.tail", mn.tail);
        Record("int.null.len", mn.vals.Length);
        Record("int.null.vals", Ints(mn.vals));

        // ---- byte[5]: struct tail rounds up to the int32 alignment ----------
        Record("byte.size", byte_sizeof());
        Record("byte.offset", byte_offset());
        var b = new BvaByteArray();
        b.head = 7;
        b.b = new byte[] { 20, 21, 22, 23, 24 };
        Record("byte.ret", byte_roundtrip(ref b));
        Record("byte.head", b.head);
        Record("byte.len", b.b.Length);
        Record("byte.vals", Bytes(b.b));

        // ---- short[3]: total extent 10 rounds up to 12 ---------------------
        Record("short.size", short_sizeof());
        Record("short.offset", short_offset());
        var s = new BvaShortArray();
        s.head = 7;
        s.s = new short[] { 300, 301, 302 };
        Record("short.ret", short_roundtrip(ref s));
        Record("short.head", s.head);
        Record("short.len", s.s.Length);
        Record("short.vals", Shorts(s.s));

        // ---- long[3]: 8-byte elements + 8-byte alignment (offset 8) --------
        Record("long.size", long_sizeof());
        Record("long.offset", long_offset());
        var l = new BvaLongArray();
        l.head = 7;
        l.l = new long[] { 9000000000L, 9000000001L, 9000000002L };
        Record("long.ret", long_roundtrip(ref l));
        Record("long.head", l.head);
        Record("long.len", l.l.Length);
        Record("long.vals", Longs(l.l));

        // ---- bool[4]: native BOOL is 4 bytes, managed cell is 1 byte ------
        Record("bool.size", bool_sizeof());
        Record("bool.offset", bool_offset());
        var bo = new BvaBoolArray();
        bo.head = 7;
        bo.flags = new bool[] { true, false, true, false };
        Record("bool.ret", bool_roundtrip(ref bo));
        Record("bool.head", bo.head);
        Record("bool.len", bo.flags.Length);
        Record("bool.vals", Bools(bo.flags));

        // ---- bool[4] with explicit ArraySubType=U1: 1-byte cells ----------
        Record("bool1.size", bool1_sizeof());
        Record("bool1.offset", bool1_offset());
        var bo1 = new BvaBool1Array();
        bo1.head = 7;
        bo1.flags = new bool[] { true, true, false, false };
        Record("bool1.ret", bool1_roundtrip(ref bo1));
        Record("bool1.head", bo1.head);
        Record("bool1.len", bo1.flags.Length);
        Record("bool1.vals", Bools(bo1.flags));
    }

    private static void Record(string id, int value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    private static void Record(string id, string value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    private static string Ints(int[] a)
    {
        var s = "";
        for (var i = 0; i < a.Length; i++)
        {
            if (i > 0) s = s + ",";
            s = s + a[i];
        }
        return s;
    }

    private static string Bytes(byte[] a)
    {
        var s = "";
        for (var i = 0; i < a.Length; i++)
        {
            if (i > 0) s = s + ",";
            s = s + a[i];
        }
        return s;
    }

    private static string Shorts(short[] a)
    {
        var s = "";
        for (var i = 0; i < a.Length; i++)
        {
            if (i > 0) s = s + ",";
            s = s + a[i];
        }
        return s;
    }

    private static string Longs(long[] a)
    {
        var s = "";
        for (var i = 0; i < a.Length; i++)
        {
            if (i > 0) s = s + ",";
            s = s + a[i];
        }
        return s;
    }

    private static string Bools(bool[] a)
    {
        var s = "";
        for (var i = 0; i < a.Length; i++)
        {
            s = s + (a[i] ? 1 : 0);
        }
        return s;
    }
}
