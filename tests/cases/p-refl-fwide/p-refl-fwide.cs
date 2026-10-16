//------------------------------------------------------------------------------
// p-refl-fwide.cs -- FieldInfo wide-slot probe (DEFECT-F1 / audit 2A). The
// a16afce4 MetadataLoader widening made the reflection field table carry the
// REAL System typeName for instance long/double/float fields, but the
// TRANSPILER stores every instance value field as int32_t (TypeEmitter
// layout) -- an 8-byte GetValue/SetValue would silently overrun into the
// neighbor field. F1 LOUD-rejects the unrepresentable shapes; the only wide
// slots are STATIC Int64/UInt64 (real int64_t/uint64_t static storage).
// net48 SUCCEEDS on instance long/double/float GetValue, so the LOUD-instance
// assertion cannot be a byte-exact oracle record -- it lives in the C++
// runtime test (test_typeinfo.cpp R2 F1 fixtures). This probe exercises the
// ORACLE-COMPARABLE byte-exact surface: the static Int64/UInt64 8B round-trip
// (net48 = our fixed runtime) plus the int32-family sanity (the int32 path
// must survive the F1 guard untouched).
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class Fw
    {
        public int I;
        public static long SL = 123456789012345L;
        public static ulong UL = 0xDEADBEEFCAFEF00DUL;
        public static int SI = 55;
    }

    public static class PReflFwide
    {
        public static void Main()
        {
            Fw f = new Fw();
            Type t = typeof(Fw);
            FieldInfo fsL = t.GetField("SL");
            FieldInfo fsU = t.GetField("UL");
            FieldInfo fsI = t.GetField("SI");
            FieldInfo fI = t.GetField("I");

            Console.WriteLine("[static.i64.read]=" + ((long)fsL.GetValue(null) == 123456789012345L));
            fsL.SetValue(null, -987654321098L);
            Console.WriteLine("[static.i64.rw]=" + ((long)fsL.GetValue(null) == -987654321098L));
            Console.WriteLine("[static.i64.verify]=" + (Fw.SL == -987654321098L));

            Console.WriteLine("[static.u64.read]=" + ((ulong)fsU.GetValue(null) == 0xDEADBEEFCAFEF00DUL));
            ulong big = 0x1234567890ABCDEFUL;
            fsU.SetValue(null, big);
            Console.WriteLine("[static.u64.rw]=" + ((ulong)fsU.GetValue(null) == big));
            Console.WriteLine("[static.u64.verify]=" + (Fw.UL == big));

            fsI.SetValue(null, 300);
            Console.WriteLine("[static.i32.rw]=" + ((int)fsI.GetValue(null) == 300));
            Console.WriteLine("[static.i32.verify]=" + (Fw.SI == 300));

            fI.SetValue(f, 42);
            Console.WriteLine("[inst.i32.rw]=" + ((int)fI.GetValue(f) == 42));
        }
    }
}