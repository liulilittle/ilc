//------------------------------------------------------------------------------
// closure-struct-capture.cs -- a closure display class field of a USER STRUCT
// or a Nullable<T> keeps a real, GC-correct value slot.
//
// Why this case exists: TypeEmitter.ValueTypeNestedField rejected every nested
// VALUETYPE field that was not one of the five BCL runtime value structs or an
// enum, so a captured user struct was LOUD at transpile time. The contract
// under test:
//   * a captured user struct occupies its REAL metadata layout (natural
//     alignment, size rounded up to the pointer-slot span);
//   * a GC reference INSIDE the captured struct is marked in the display
//     class refFieldBitmap at its absolute slot, so a GC.Collect() (which may
//     compact the heap) still finds and rewrites it -- after the ONLY root is
//     the struct field;
//   * a captured Nullable<T> expands to the underlying T slot plus a
//     hasValue(bool) slot and round-trips both the has-value and no-value
//     paths, for a BCL T and a translated user-struct T.
//
// Determinism: integer / boolean / fixed-literal assertions only.
//------------------------------------------------------------------------------
using System;

public struct CapturedPair
{
    public long Tag;
    public string Name;
    public int Code;

    public CapturedPair(long tag, string name, int code)
    {
        Tag = tag;
        Name = name;
        Code = code;
    }
}

public struct CapturedVec
{
    public int X;
    public int Y;
}

public static class ClosureStructCapture
{
    // S1: a captured user struct with a reference field; the reference is read
    // back through the closure (the display class field slot).
    private static int StructWithRef(int code, string name)
    {
        var p = new CapturedPair(1234567890123L, name, code);
        Func<int> f = () => p.Name.Length * 1000 + p.Code + (int)(p.Tag % 1000);
        return f();
    }

    // A runtime-allocated string (never an interned literal).
    private static string MakeName(int seed)
    {
        return "gc-struct-ref-" + seed.ToString();
    }

    // S2: the captured struct's reference field must survive a GC cycle. The
    // string is created inside a closure and stored into the display class
    // struct field, so the ONLY root is the struct member: a missing bitmap
    // bit lets the collector free it, and the churn after Collect reuses the
    // block with "y" payloads.
    private static int StructWithRefAcrossGc(int seed)
    {
        var p = new CapturedPair(7L, "placeholder", 42);
        Action put = () => { p.Name = MakeName(seed); };
        Func<int> check = () => p.Name.Length * 1000000 + (int)p.Name[0] * 1000 + p.Code;
        put();
        for (int i = 0; i < 4000; i++)
        {
            string junk = new string('x', 64);
            if (junk.Length != 64) return -1;
        }
        GC.Collect();
        // Same-size churn: if the struct's reference bit is missing, the
        // collector frees the captured string and these allocations reuse the
        // block, changing the first char / length read back below.
        for (int i = 0; i < 4000; i++)
        {
            string junk = new string('y', 16);
            if (junk.Length != 16) return -2;
        }
        return check() + (int)p.Tag;
    }

    // S3: a captured struct value is rewritten AFTER the closure exists, so
    // the store goes through the display class field (not a one-shot copy).
    private static int StructRewrite(string first, string second)
    {
        var p = new CapturedPair(1L, first, 1);
        Func<int> f = () => p.Name.Length * 1000 + p.Code + (int)p.Tag;
        p = new CapturedPair(2L, second, 2);
        return f();
    }

    // S4: a captured Nullable<DateTime> with a value: HasValue / Value read
    // through the display class.
    private static int NullableDateWithValue()
    {
        DateTime? d = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Unspecified);
        Func<int> f = () => d.HasValue ? d.Value.Year * 10000 + d.Value.Month * 100 + d.Value.Day : -1;
        return f();
    }

    // S5: a captured Nullable<DateTime> without a value: HasValue false and
    // GetValueOrDefault answers default(DateTime).
    private static int NullableDateNoValue()
    {
        DateTime? d = null;
        Func<int> f = () => d.HasValue ? d.Value.Year : (d.GetValueOrDefault().Year + 5);
        return f();
    }

    // S6: a captured Nullable<DateTime> rewritten after capture.
    private static int NullableDateRewrite()
    {
        DateTime? d = null;
        Func<int> f = () => d.HasValue ? d.Value.Day : -3;
        d = new DateTime(2022, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);
        return f();
    }

    // S7: a captured Nullable<int> with a value (the primitive underlying T).
    private static int NullableIntWithValue(int seed)
    {
        int? n = seed;
        Func<int> f = () => n.HasValue ? n.Value + 1 : -1;
        return f();
    }

    // S10: a captured Nullable<int> without a value.
    private static int NullableIntNoValue()
    {
        int? n = null;
        Func<int> f = () => n.HasValue ? n.Value + 1 : -1;
        return f();
    }

    // S8: a captured Nullable<user struct> (no reference fields) with a value.
    private static int NullableStructWithValue()
    {
        var v = new CapturedVec();
        v.X = 3;
        v.Y = 4;
        CapturedVec? nv = v;
        Func<int> f = () => nv.HasValue ? nv.Value.X * 10 + nv.Value.Y : -1;
        return f();
    }

    // S9: a captured Nullable<user struct> without a value:
    // GetValueOrDefault answers default(CapturedVec).
    private static int NullableStructNoValue()
    {
        CapturedVec? nv = null;
        Func<int> f = () => nv.HasValue ? -2 : (nv.GetValueOrDefault().X + 9);
        return f();
    }

    public static void Main()
    {
        Console.WriteLine("[csc] s1=" + StructWithRef(7, "captured-struct-ref"));
        Console.WriteLine("[csc] s2=" + StructWithRefAcrossGc(11));
        Console.WriteLine("[csc] s3=" + StructRewrite("first", "second"));
        Console.WriteLine("[csc] s4=" + NullableDateWithValue());
        Console.WriteLine("[csc] s5=" + NullableDateNoValue());
        Console.WriteLine("[csc] s6=" + NullableDateRewrite());
        Console.WriteLine("[csc] s7=" + NullableIntWithValue(41));
        Console.WriteLine("[csc] s8=" + NullableStructWithValue());
        Console.WriteLine("[csc] s9=" + NullableStructNoValue());
        Console.WriteLine("[csc] s10=" + NullableIntNoValue());
    }
}