//------------------------------------------------------------------------------
// p-refl-fvtype.cs -- FieldInfo.GetValue/SetValue slot-width probe (GAP-2 /
// P1). net48 writes EVERY public narrow primitive field through reflection and
// reads it back: int32 (Int32), bool, short, uint, byte -- the int32-family
// carriers the runtime slots; string the reference slot. The runtime
// previously accepted only a boxed Int32 for SetValue (a Boolean/Int16/UInt32
// value threw "object is not a boxed Int32" -- the value-boxed shape net48
// always uses) and a STATIC field's GetValue(null)/SetValue(null) hit the
// null-target NRE (net48: legal on static fields). Every record is a
// round-trip bool verdict, byte-stable.
//
// PROBE STRUCTURE (o4boxedfield precedent): all FieldInfo handles are fetched
// FIRST, then set, then read -- the transpiler's temp allocator never
// interleaves a void SetValue result with a pending Type receiver.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class Fv
    {
        public int I;
        public bool B;
        public short S;
        public uint U;
        public byte Y;
        public string G;
        public static int SI = 100;
        public static string SStr = "boot";
    }

    public static class PReflFvtype
    {
        public static void Main()
        {
            Fv f = new Fv();
            Type t = typeof(Fv);
            FieldInfo fI = t.GetField("I");
            FieldInfo fB = t.GetField("B");
            FieldInfo fS = t.GetField("S");
            FieldInfo fU = t.GetField("U");
            FieldInfo fY = t.GetField("Y");
            FieldInfo fG = t.GetField("G");
            FieldInfo fsI = t.GetField("SI");
            FieldInfo fsS = t.GetField("SStr");

            fI.SetValue(f, 42);
            fB.SetValue(f, true);
            fS.SetValue(f, (short)7);
            fU.SetValue(f, 3000000000u);
            fY.SetValue(f, (byte)200);
            fG.SetValue(f, "zap");
            fsI.SetValue(null, 200);
            fsS.SetValue(null, "after");

            Console.WriteLine("[int.rw]=" + ((int)fI.GetValue(f) == 42));
            Console.WriteLine("[bool.rw]=" + ((bool)fB.GetValue(f) == true));
            Console.WriteLine("[short.rw]=" + ((short)fS.GetValue(f) == 7));
            Console.WriteLine("[uint.rw]=" + ((uint)fU.GetValue(f) == 3000000000u));
            Console.WriteLine("[byte.rw]=" + ((byte)fY.GetValue(f) == 200));
            Console.WriteLine("[str.rw]=" + ((string)fG.GetValue(f) == "zap"));
            Console.WriteLine("[static.int.wr]=" + ((int)fsI.GetValue(null) == 200));
            Console.WriteLine("[static.int.verify]=" + (Fv.SI == 200));
            Console.WriteLine("[static.str.wr]=" + ((string)fsS.GetValue(null) == "after"));
            Console.WriteLine("[static.str.verify]=" + (Fv.SStr == "after"));
        }
    }
}