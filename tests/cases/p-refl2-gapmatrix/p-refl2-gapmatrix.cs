// p-refl2-gapmatrix: one probe method per API so the transpiler reports each
// unwired surface independently (a fold failure stubs only that method).
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cases
{
    public interface IGap { int GM(); }
    public abstract class GapBase { public abstract int GA(); public int GB() { return 1; } }
    public class GapDerived : GapBase, IGap
    {
        public override int GA() { return 2; }
        public int GM() { return 3; }
    }
    public enum GapEnum { A, B }
    public struct GapStruct { public int X; }

    public static class PRefl2Matrix
    {
        static void P(string tag, object v) { Console.WriteLine("[" + tag + "]=[" + (v == null ? "<null>" : v.ToString()) + "]"); }

        // A: Type identity/name family
        static void A01() { P("a01", typeof(int).Name); }
        static void A02() { P("a02", typeof(int?).Name); }
        static void A03() { P("a03", typeof(int[]).Name); }
        static void A04() { P("a04", typeof(int[,]).Name); }
        static void A05() { P("a05", typeof(List<int>).Name); }
        static void A06() { P("a06", typeof(int?).FullName); }
        static void A07() { P("a07", typeof(int[,]).FullName); }
        static void A08() { P("a08", typeof(int[]).GetArrayRank()); }
        static void A09() { P("a09", typeof(int[,]).GetArrayRank()); }
        static void A10() { P("a10", typeof(int).GetArrayRank()); }
        static void A11() { P("a11", typeof(int?).IsGenericType); }
        static void A12() { P("a12", typeof(int?).IsGenericTypeDefinition); }
        static void A13() { P("a13", typeof(int?).IsConstructedGenericType); }
        static void A14() { P("a14", typeof(List<int>).IsConstructedGenericType); }
        static void A15() { P("a15", typeof(List<>).IsConstructedGenericType); }
        static void A16() { P("a16", typeof(int).IsAbstract); }
        static void A17() { P("a17", typeof(GapBase).IsAbstract); }
        static void A18() { P("a18", typeof(int).IsSealed); }
        static void A19() { P("a19", typeof(GapBase).IsSealed); }
        static void A20() { P("a20", typeof(int).IsPublic); }
        static void A21() { P("a21", typeof(int).IsNested); }
        static void A22() { P("a22", typeof(GapStruct).IsNested); }
        static void A23() { P("a23", typeof(GapStruct).IsValueType); }
        static void A24() { P("a24", typeof(GapEnum).IsEnum); }
        static void A25() { P("a25", typeof(IGap).IsInterface); }
        static void A26() { P("a26", typeof(GapDerived).BaseType == null ? "<null>" : typeof(GapDerived).BaseType.Name); }
        static void A27() { P("a27", typeof(int).DeclaringType == null ? "<null>" : "x"); }
        static void A28() { P("a28", typeof(int).MetadataToken); }
        static void A29() { P("a29", typeof(int).GetHashCode() == typeof(int).GetHashCode()); }
        static void A30() { P("a30", typeof(int).Equals(typeof(int))); }
        static void A31() { P("a31", typeof(int).IsPrimitive); }
        static void A32() { P("a32", typeof(string).IsClass); }
        static void A33() { P("a33", typeof(int).IsAssignableFrom(typeof(int))); }
        static void A34() { P("a34", typeof(GapBase).IsAssignableFrom(typeof(GapDerived))); }
        static void A35() { P("a35", typeof(GapDerived).IsSubclassOf(typeof(GapBase))); }
        static void A36() { P("a36", typeof(GapDerived).IsInstanceOfType(new GapDerived())); }
        static void A37() { P("a37", typeof(IGap).IsAssignableFrom(typeof(GapDerived))); }
        static void A38() { P("a38", typeof(GapDerived).GetInterfaces().Length); }
        static void A39() { P("a39", typeof(GapDerived).GetInterface("IGap") == null ? "<null>" : typeof(GapDerived).GetInterface("IGap").Name); }
        static void A40() { P("a40", (int)Type.GetTypeCode(typeof(int))); }

        // A: generic/array construction
        static void A41() { P("a41", typeof(List<int>).GetGenericTypeDefinition().Name); }
        static void A42() { P("a42", typeof(List<int>).GetGenericArguments().Length); }
        static void A43() { P("a43", typeof(List<int>).GetGenericArguments()[0].Name); }
        static void A44() { P("a44", typeof(int).MakeArrayType().Name); }
        static void A45() { P("a45", typeof(int).MakeByRefType().Name); }
        static void A46() { P("a46", typeof(int).MakePointerType().Name); }
        static void A47() { P("a47", typeof(int[]).GetElementType().Name); }
        static void A48() { P("a48", typeof(int).GetElementType() == null ? "<null>" : "x"); }

        // A: Type.GetType family
        static void A49() { P("a49", Type.GetType("System.Int32") == null ? "<null>" : "hit"); }
        static void A50() { P("a50", Type.GetType("System.Int32", false) == null ? "<null>" : "hit"); }
        static void A51() { P("a51", Type.GetType("System.Int32", true, true) == null ? "<null>" : "hit"); }
        static void A52() { P("a52", Type.GetType("No.Such.Type") == null ? "<null>" : "hit"); }

        // B: MemberInfo/Field/Property/Method
        static void B01() { var m = typeof(GapDerived).GetMethod("GA"); P("b01", m.Name); }
        static void B02() { var m = typeof(GapDerived).GetMethod("GA"); P("b02", m.DeclaringType.Name); }
        static void B03() { var m = typeof(GapDerived).GetMethod("GA"); P("b03", m.ReflectedType.Name); }
        static void B04() { var m = typeof(GapDerived).GetMethod("GA"); P("b04", (int)m.MemberType); }
        static void B05() { var m = typeof(GapDerived).GetMethod("GA"); P("b05", m.MetadataToken); }
        static void B06() { var m = typeof(GapDerived).GetMethod("GA"); P("b06", m.ReturnType.Name); }
        static void B07() { var m = typeof(GapDerived).GetMethod("GB"); P("b07", m.IsVirtual); }
        static void B08() { var m = typeof(GapBase).GetMethod("GA"); P("b08", m.IsAbstract); }
        static void B09() { var m = typeof(GapDerived).GetMethod("GA"); P("b09", m.IsStatic); }
        static void B10() { var m = typeof(GapDerived).GetMethod("GA"); P("b10", m.IsPublic); }
        static void B11() { var m = typeof(GapDerived).GetMethod("GA"); P("b11", m.IsGenericMethod); }
        static void B12() { var m = typeof(GapDerived).GetMethod("GA"); P("b12", m.Invoke(new GapDerived(), null)); }
        static void B13() { var m = typeof(GapDerived).GetMethod("GA"); var ps = m.GetParameters(); P("b13", ps.Length); }
        static void B14() { var f = typeof(GapStruct).GetField("X"); P("b14", f.Name); }
        static void B15() { var f = typeof(GapStruct).GetField("X"); P("b15", f.FieldType.Name); }
        static void B16() { var f = typeof(GapStruct).GetField("X"); P("b16", f.IsStatic); }
        static void B17() { var f = typeof(GapStruct).GetField("X"); P("b17", f.IsInitOnly); }
        static void B18() { var f = typeof(GapStruct).GetField("X"); P("b18", f.IsLiteral); }
        static void B19() { var p = typeof(GapDerived).GetProperty("Missing"); P("b19", p == null ? "<null>" : "hit"); }
        static void B20() { var c = typeof(GapDerived).GetConstructor(new Type[0]); P("b20", c.Name); }
        static void B21() { var c = typeof(GapDerived).GetConstructor(new Type[0]); P("b21", c.Invoke(null) == null ? "<null>" : "hit"); }

        // C: Activator/Assembly
        static void C01() { P("c01", Activator.CreateInstance(typeof(GapDerived)) == null ? "<null>" : "hit"); }
        static void C02() { P("c02", Activator.CreateInstance(typeof(GapDerived), new object[0]) == null ? "<null>" : "hit"); }
        static void C03() { P("c03", Activator.CreateInstance(typeof(GapDerived), false) == null ? "<null>" : "hit"); }
        static void C04() { var a = typeof(int).Assembly; P("c04", a.GetName().Name); }
        static void C05() { var a = typeof(int).Assembly; P("c05", a.GetType("Cases.GapDerived") == null ? "<null>" : "hit"); }
        static void C06() { var a = typeof(int).Assembly; P("c06", a.GetTypes().Length > 0); }
        static void C07() { P("c07", Assembly.GetExecutingAssembly().GetName().Name); }
        static void C08() { P("c08", Assembly.GetEntryAssembly().GetName().Name); }
        static void C09() { var a = typeof(int).Assembly; P("c09", a.FullName == null ? "<null>" : "hit"); }

        // D: attributes
        static void D01() { P("d01", typeof(GapDerived).GetCustomAttributes(false).Length); }
        static void D02() { P("d02", typeof(GapDerived).IsDefined(typeof(SerializableAttribute), false)); }

        // E: handles
        static void E01() { P("e01", Type.GetTypeFromHandle(typeof(int).TypeHandle).Name); }
        static void E02() { var h = typeof(GapDerived).TypeHandle; P("e02", Type.GetTypeFromHandle(h).Name); }
        static void E03() { var m = typeof(GapDerived).GetMethod("GA"); P("e03", m.MethodHandle.Value != IntPtr.Zero); }
        static void E04() { var f = typeof(GapStruct).GetField("X"); P("e04", f.FieldHandle.Value != IntPtr.Zero); }

        static void R(string tag, Action a) { try { a(); } catch (Exception e) { Console.WriteLine("[" + tag + ".err]=" + e.GetType().Name); } }

        public static void Main()
        {
            R("A01", A01); R("A02", A02); R("A03", A03); R("A04", A04); R("A05", A05); R("A06", A06); R("A07", A07); R("A08", A08); R("A09", A09); R("A10", A10);
            R("A11", A11); R("A12", A12); R("A13", A13); R("A14", A14); R("A15", A15); R("A16", A16); R("A17", A17); R("A18", A18); R("A19", A19); R("A20", A20);
            R("A21", A21); R("A22", A22); R("A23", A23); R("A24", A24); R("A25", A25); R("A26", A26); R("A27", A27); R("A28", A28); R("A29", A29); R("A30", A30);
            R("A31", A31); R("A32", A32); R("A33", A33); R("A34", A34); R("A35", A35); R("A36", A36); R("A37", A37); R("A38", A38); R("A39", A39); R("A40", A40);
            R("A41", A41); R("A42", A42); R("A43", A43); R("A44", A44); R("A45", A45); R("A46", A46); R("A47", A47); R("A48", A48); R("A49", A49); R("A50", A50);
            R("A51", A51); R("A52", A52);
            R("B01", B01); R("B02", B02); R("B03", B03); R("B04", B04); R("B05", B05); R("B06", B06); R("B07", B07); R("B08", B08); R("B09", B09); R("B10", B10);
            R("B11", B11); R("B12", B12); R("B13", B13); R("B14", B14); R("B15", B15); R("B16", B16); R("B17", B17); R("B18", B18); R("B19", B19); R("B20", B20); R("B21", B21);
            R("C01", C01); R("C02", C02); R("C03", C03); R("C04", C04); R("C05", C05); R("C06", C06); R("C07", C07); R("C08", C08); R("C09", C09);
            R("D01", D01); R("D02", D02);
            R("E01", E01); R("E02", E02); R("E03", E03); R("E04", E04);
        }
    }
}