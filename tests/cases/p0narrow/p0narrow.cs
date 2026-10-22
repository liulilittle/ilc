//------------------------------------------------------------------------------
// p0narrow.cs -- narrow-integer ToString() value-semantics reference program.
//
// Defect under test (P0-narrow): SByte / Int16 / UInt16 had NO parameterless
// ToString() wiring in MethodMap.ValueType.cs (only Byte did), so the
// resolver fell through to the generated-name path and the emitted C++ called
// a symbol that does not exist (a SILENT transpile success + C++ compile
// failure). Runs on REAL .NET Framework 4.8 (net48) and its printed output is
// the ORACLE the runtime icalls (icall_int32_to_string via the narrow deref
// casts) must match byte-for-byte.
//
// Coverage:
//   1) LOCAL receivers for all four narrow types (incl. byte as the control).
//   2) NEGATIVE / boundary values: sbyte.MinValue (-128), short.MinValue
//      (-32768), ushort.MaxValue (65535), byte.MaxValue (255), and the
//      sign-extension traps (-1, -3, -5).
//   3) ARRAY ELEMENT receivers (arr[i].ToString()) -- the ldelema ADDRESS
//      shape, where a wide read would over-read the 1/2-byte cell.
//   4) FIELD receivers (class field / struct field / static field).
//   5) BOXED receivers ((object)sb).ToString() and object-typed locals --
//      the icall_object_to_string klass dispatch.
//   6) ToString() inside concatenation / StringBuilder.Append / string.Join
//      (the object-slot formatting paths).
//   7) int[].Min(x => (short)x) / Max(x => (byte)x) -- the P0-09 selector
//      aggregate that returns a narrow value which is then formatted.
//------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;

internal struct NarrowBox
{
    public sbyte S;
    public short H;
    public ushort U;
    public byte B;

    public NarrowBox(sbyte s, short h, ushort u, byte b)
    {
        S = s;
        H = h;
        U = u;
        B = b;
    }
}

internal sealed class NarrowFields
{
    public sbyte S;
    public short H;
    public ushort U;
    public byte B;

    public NarrowFields(sbyte s, short h, ushort u, byte b)
    {
        S = s;
        H = h;
        U = u;
        B = b;
    }
}

internal static class P0Narrow
{
    private static sbyte _staticS = -7;
    private static short _staticH = -300;
    private static ushort _staticU = 60000;
    private static byte _staticB = 200;

    private static void W(string k, string v)
    {
        Console.WriteLine(k + "=" + v);
    }

    private static void Locals()
    {
        sbyte sb = -3;
        short s = -5;
        ushort u = 9;
        byte b = 7;
        W("loc_sbyte", sb.ToString());
        W("loc_short", s.ToString());
        W("loc_ushort", u.ToString());
        W("loc_byte", b.ToString());
        W("loc_sbyte_concat", "s=" + sb.ToString());
        W("loc_short_concat", "s=" + s.ToString());
        W("loc_ushort_concat", "u=" + u.ToString());
        W("loc_byte_concat", "b=" + b.ToString());
        W("loc_interp_short", string.Concat("x", s.ToString(), "y"));
    }

    private static void Boundaries()
    {
        sbyte sMin = sbyte.MinValue;
        sbyte sMax = sbyte.MaxValue;
        short hMin = short.MinValue;
        short hMax = short.MaxValue;
        ushort uMin = ushort.MinValue;
        ushort uMax = ushort.MaxValue;
        byte bMin = byte.MinValue;
        byte bMax = byte.MaxValue;
        W("bnd_sbyte_min", sMin.ToString());
        W("bnd_sbyte_max", sMax.ToString());
        W("bnd_short_min", hMin.ToString());
        W("bnd_short_max", hMax.ToString());
        W("bnd_ushort_min", uMin.ToString());
        W("bnd_ushort_max", uMax.ToString());
        W("bnd_byte_min", bMin.ToString());
        W("bnd_byte_max", bMax.ToString());
        sbyte n1 = -1;
        short n1h = -1;
        sbyte n3 = -3;
        short n5 = -5;
        W("bnd_sbyte_neg1", n1.ToString());
        W("bnd_short_neg1", n1h.ToString());
        W("bnd_sbyte_neg3", n3.ToString());
        W("bnd_short_neg5", n5.ToString());
    }

    private static void Arrays()
    {
        sbyte[] sa = new sbyte[] { 0, -1, -128, 127, 42 };
        short[] ha = new short[] { 0, -1, -32768, 32767, 1234 };
        ushort[] ua = new ushort[] { 0, 1, 65535, 40000, 9 };
        byte[] ba = new byte[] { 0, 1, 255, 128, 7 };
        for (int i = 0; i < sa.Length; i++) W("arr_sbyte_" + i, sa[i].ToString());
        for (int i = 0; i < ha.Length; i++) W("arr_short_" + i, ha[i].ToString());
        for (int i = 0; i < ua.Length; i++) W("arr_ushort_" + i, ua[i].ToString());
        for (int i = 0; i < ba.Length; i++) W("arr_byte_" + i, ba[i].ToString());
        // The concatenation shape over an array element (the ADDRESS receiver
        // inside a string build, not just a bare call).
        W("arr_short_concat", "h=" + ha[2].ToString());
        W("arr_sbyte_concat", "s=" + sa[2].ToString());
        W("arr_ushort_concat", "u=" + ua[2].ToString());
    }

    private static void Fields()
    {
        NarrowFields f = new NarrowFields(-11, -2222, 55555, 111);
        W("fld_class_sbyte", f.S.ToString());
        W("fld_class_short", f.H.ToString());
        W("fld_class_ushort", f.U.ToString());
        W("fld_class_byte", f.B.ToString());

        NarrowBox nb = new NarrowBox(-12, -3333, 44444, 99);
        W("fld_struct_sbyte", nb.S.ToString());
        W("fld_struct_short", nb.H.ToString());
        W("fld_struct_ushort", nb.U.ToString());
        W("fld_struct_byte", nb.B.ToString());

        W("fld_static_sbyte", _staticS.ToString());
        W("fld_static_short", _staticH.ToString());
        W("fld_static_ushort", _staticU.ToString());
        W("fld_static_byte", _staticB.ToString());
    }

    private static void Boxed()
    {
        sbyte sb = -3;
        short s = -5;
        ushort u = 9;
        byte b = 7;
        object osb = sb;
        object os = s;
        object ou = u;
        object ob = b;
        W("box_sbyte", osb.ToString());
        W("box_short", os.ToString());
        W("box_ushort", ou.ToString());
        W("box_byte", ob.ToString());
        W("box_cast_sbyte", ((object)sb).ToString());
        W("box_cast_short", ((object)s).ToString());
        W("box_cast_ushort", ((object)u).ToString());
        W("box_cast_byte", ((object)b).ToString());
        object omin = (object)sbyte.MinValue;
        object ohmin = (object)short.MinValue;
        object oumax = (object)ushort.MaxValue;
        object obmax = (object)byte.MaxValue;
        W("box_sbyte_min", omin.ToString());
        W("box_short_min", ohmin.ToString());
        W("box_ushort_max", oumax.ToString());
        W("box_byte_max", obmax.ToString());
        W("box_concat", "v=" + osb.ToString() + "/" + os.ToString());
    }

    private static void ObjectSlots()
    {
        sbyte sb = -3;
        short s = -5;
        ushort u = 9;
        byte b = 7;
        StringBuilder sbld = new StringBuilder();
        sbld.Append(sb.ToString());
        sbld.Append(',');
        sbld.Append(s.ToString());
        sbld.Append(',');
        sbld.Append(u.ToString());
        sbld.Append(',');
        sbld.Append(b.ToString());
        W("sb_append", sbld.ToString());
        W("join", string.Join("|", new string[] { sb.ToString(), s.ToString(), u.ToString(), b.ToString() }));
        W("concat_obj", string.Concat((object)sb, (object)s, (object)u, (object)b));
    }

    private static void LinqNarrow()
    {
        int[] xs = new int[] { 3, 1, 4, 1, 5, 9, 2, 6 };
        short minShort = xs.Min(x => (short)x);
        W("linq_min_short", minShort.ToString());
        byte maxByte = xs.Max(x => (byte)x);
        W("linq_max_byte", maxByte.ToString());
        int[] neg = new int[] { -5, -9, -2 };
        short minNeg = neg.Min(x => (short)x);
        W("linq_min_short_neg", minNeg.ToString());
    }

    private static void Main()
    {
        Locals();
        Boundaries();
        Arrays();
        Fields();
        Boxed();
        ObjectSlots();
        LinqNarrow();
    }
}
