// o4boxedfield.cs -- O4 probe: reflection FieldInfo.GetValue/SetValue on a
// BOXED USER STRUCT. The Il2CppFieldInfo.offset column is the STRUCT-RELATIVE
// compact offset (starts at 0 for the first field), but the runtime GetValue/
// SetValue added it directly to the object base -- a boxed struct's payload
// lives 8 bytes past the base (the object header), so the read went to
// base+offset (the klass pointer for field 0) instead of base+8+offset. The
// klass join ALSO failed for value types: the struct's TypeInfo table carried
// klass=nullptr (P25b descriptors exist but the reflection table did not
// back-reference them), so GetValue/SetValue bailed out before the offset.
// The boxed struct has three int32 fields (offsets 0/4/8 -- these exercises
// read/write payload bytes 8/12/16, across the header boundary).
//
// net48 oracle truth:
//   X=5 Y=6 Z=7
//   back=55,66,77
// Today the transpiled side prints empty GetValue results and the original
// box (5,6,7) on the unbox round-trip -- deterministic diff, locked RED.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

public static class Program
{
    public static void Main()
    {
        S s = new S(5, 6, 7);
        object box = s;
        var t = typeof(S);
        var fx = t.GetField("X");
        var fy = t.GetField("Y");
        var fz = t.GetField("Z");
        Console.WriteLine("X=" + fx.GetValue(box));
        Console.WriteLine("Y=" + fy.GetValue(box));
        Console.WriteLine("Z=" + fz.GetValue(box));
        fx.SetValue(box, 55);
        fy.SetValue(box, 66);
        fz.SetValue(box, 77);
        S back = (S)box;
        Console.WriteLine("back=" + back.X + "," + back.Y + "," + back.Z);
    }
}

struct S
{
    public int X;
    public int Y;
    public int Z;
    public S(int x, int y, int z) { X = x; Y = y; Z = z; }
}