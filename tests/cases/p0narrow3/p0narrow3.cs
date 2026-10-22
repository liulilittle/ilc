//------------------------------------------------------------------------------
// p0narrow3.cs -- IConvertible / IFormattable / Convert.ToString interface
// paths for the narrow integer types (P0-narrow part 3).
//
// Defect under test: these are perfectly legal, statically analyzable net48
// statements that previously made the transpiler DROP the whole enclosing
// method (a runtime NotSupportedException stub) or emit a phantom symbol:
//   ((IConvertible)sb).ToString(provider)          -- only GetTypeCode was wired
//   ((IFormattable)sb).ToString("X", provider)     -- NO System.IFormattable case
//   Convert.ToString(sb) / (sb, provider)          -- "param type 0x04 not wired"
//
// net48 oracle semantics (probe-verified, this file's output is the oracle):
//   * IConvertible.ToString(provider): the provider is IGNORED; the plain
//     ToString() text (sbyte -1 -> "-1", ushort 65535 -> "65535").
//   * IFormattable.ToString(format, provider): EXACTLY ToString(format,
//     provider), INCLUDING the narrow hex width rule -- (sbyte)-1 "X" -> "FF"
//     (not the int32 engine's "FFFFFFFF"), (short)-1 "X" -> "FFFB",
//     (ushort)65535 "X" -> "FFFF", (byte)255 "X4" -> "00FF".
//   * Convert.ToString(narrow) and (narrow, provider): plain decimal text.
//   * Convert.ToString((object)null): "" (empty); Object.ToString on a null
//     receiver: NullReferenceException.
//------------------------------------------------------------------------------

using System;
using System.Globalization;

internal static class P0Narrow3
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");

    private static void W(string k, string v)
    {
        Console.WriteLine(k + "=[" + v + "]");
    }

    private static string OrEx(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "EX:" + e.GetType().Name; }
    }

    // ---- IConvertible.ToString(provider) ----
    private static void IConvertiblePaths()
    {
        sbyte sb = -1;
        short sh = -5;
        byte b = 255;
        ushort us = 65535;
        W("ic_sbyte_null", ((IConvertible)sb).ToString(null));
        W("ic_sbyte_inv", ((IConvertible)sb).ToString(Inv));
        W("ic_sbyte_zh", ((IConvertible)sb).ToString(Zh));
        W("ic_short_inv", ((IConvertible)sh).ToString(Inv));
        W("ic_byte_inv", ((IConvertible)b).ToString(Inv));
        W("ic_ushort_inv", ((IConvertible)us).ToString(Inv));
        W("ic_sbyte_min", ((IConvertible)sbyte.MinValue).ToString(Inv));
        W("ic_short_min", ((IConvertible)short.MinValue).ToString(Inv));
        W("ic_ushort_max", ((IConvertible)ushort.MaxValue).ToString(Inv));
        W("ic_byte_max", ((IConvertible)byte.MaxValue).ToString(Inv));
        // non-narrow controls (already worked, guards the shared arm)
        W("ic_int_inv", ((IConvertible)(-1)).ToString(Inv));
        W("ic_long_inv", ((IConvertible)(-1234567890123L)).ToString(Inv));
        W("ic_double_inv", ((IConvertible)1.5).ToString(Inv));
        W("ic_bool_inv", ((IConvertible)true).ToString(Inv));
        W("ic_char_inv", ((IConvertible)'A').ToString(Inv));
        // concat / string-building shapes
        W("ic_concat", "v=" + ((IConvertible)sb).ToString(Inv) + "/" + ((IConvertible)us).ToString(Inv));
    }

    // ---- IFormattable.ToString(format, provider) ----
    private static void IFormattablePaths()
    {
        sbyte sb = -1;
        short sh = -5;
        byte b = 255;
        ushort us = 65535;
        W("if_sbyte_D_inv", ((IFormattable)sb).ToString("D", Inv));
        W("if_sbyte_D5_inv", ((IFormattable)sb).ToString("D5", Inv));
        W("if_sbyte_X_inv", ((IFormattable)sb).ToString("X", Inv));
        W("if_sbyte_X2_inv", ((IFormattable)sb).ToString("X2", Inv));
        W("if_sbyte_X4_inv", ((IFormattable)sb).ToString("X4", Inv));
        W("if_sbyte_N0_inv", ((IFormattable)sb).ToString("N0", Inv));
        W("if_sbyte_C_inv", ((IFormattable)sb).ToString("C", Inv));
        W("if_sbyte_C_zh", ((IFormattable)sb).ToString("C", Zh));
        W("if_sbyte_P_inv", ((IFormattable)sb).ToString("P", Inv));
        W("if_sbyte_nullfmt", ((IFormattable)sb).ToString(null, Inv));
        W("if_sbyte_empty", ((IFormattable)sb).ToString("", Inv));

        W("if_short_X_inv", ((IFormattable)sh).ToString("X", Inv));
        W("if_short_X4_inv", ((IFormattable)sh).ToString("X4", Inv));
        W("if_short_D5_inv", ((IFormattable)sh).ToString("D5", Inv));
        W("if_short_min_X_inv", ((IFormattable)short.MinValue).ToString("X", Inv));

        W("if_byte_X_inv", ((IFormattable)b).ToString("X", Inv));
        W("if_byte_X2_inv", ((IFormattable)b).ToString("X2", Inv));
        W("if_byte_X4_inv", ((IFormattable)b).ToString("X4", Inv));

        W("if_ushort_X_inv", ((IFormattable)us).ToString("X", Inv));
        W("if_ushort_N0_inv", ((IFormattable)us).ToString("N0", Inv));
        W("if_ushort_max_X_inv", ((IFormattable)ushort.MaxValue).ToString("X", Inv));

        // non-narrow controls
        W("if_int_X_inv", ((IFormattable)(-1)).ToString("X", Inv));
        W("if_long_X_inv", ((IFormattable)(-1L)).ToString("X", Inv));
        W("if_double_F2_inv", ((IFormattable)1.5).ToString("F2", Inv));
        W("if_float_F2_inv", ((IFormattable)1.5f).ToString("F2", Inv));
        W("if_uint_X_inv", ((IFormattable)(4000000000u)).ToString("X", Inv));

        W("if_bad_Q", OrEx(() => ((IFormattable)sb).ToString("Q", Inv)));
        W("if_concat", "v=" + ((IFormattable)sh).ToString("X", Inv));
    }

    // ---- Convert.ToString ----
    private static void ConvertPaths()
    {
        sbyte sb = -1;
        short sh = -5;
        byte b = 255;
        ushort us = 65535;
        W("cv_sbyte", Convert.ToString(sb));
        W("cv_short", Convert.ToString(sh));
        W("cv_byte", Convert.ToString(b));
        W("cv_ushort", Convert.ToString(us));
        W("cv_sbyte_inv", Convert.ToString(sb, Inv));
        W("cv_short_inv", Convert.ToString(sh, Inv));
        W("cv_byte_inv", Convert.ToString(b, Inv));
        W("cv_ushort_inv", Convert.ToString(us, Inv));
        W("cv_sbyte_zh", Convert.ToString(sb, Zh));
        W("cv_sbyte_min", Convert.ToString(sbyte.MinValue));
        W("cv_short_min", Convert.ToString(short.MinValue));
        W("cv_ushort_max", Convert.ToString(ushort.MaxValue));
        W("cv_byte_max", Convert.ToString(byte.MaxValue));
        // controls (int/long/double + provider were already wired)
        W("cv_int", Convert.ToString(5));
        W("cv_int_inv", Convert.ToString(5, Inv));
        W("cv_long", Convert.ToString(7L));
        W("cv_long_inv", Convert.ToString(7L, Inv));
        W("cv_double_inv", Convert.ToString(1.5, Inv));
        W("cv_bool", Convert.ToString(true));
        W("cv_char", Convert.ToString('A'));
        // object overload (null -> "")
        W("cv_null_obj", Convert.ToString((object)null));
        W("cv_null_obj_inv", Convert.ToString((object)null, Inv));
        W("cv_null_str", Convert.ToString((string)null));
        W("cv_obj_sbyte", Convert.ToString((object)sb));
        W("cv_obj_ushort", Convert.ToString((object)us));
        W("cv_obj_bool", Convert.ToString((object)true));
        // concat shapes
        W("cv_concat", "v=" + Convert.ToString(sb) + "/" + Convert.ToString(us, Inv));
    }

    // ---- array-element / field receivers ----
    private static sbyte _fS = -1;
    private static short _fH = -5;
    private static ushort _fU = 65535;
    private static byte _fB = 255;

    private static void ReceiverShapes()
    {
        sbyte[] sa = new sbyte[] { -1, -128, 127 };
        short[] ha = new short[] { -1, -32768, 32767 };
        ushort[] ua = new ushort[] { 65535, 0, 40000 };
        byte[] ba = new byte[] { 255, 0, 128 };
        for (int i = 0; i < sa.Length; i++)
        {
            W("arr_ic_sbyte_" + i, ((IConvertible)sa[i]).ToString(Inv));
            W("arr_if_sbyte_" + i, ((IFormattable)sa[i]).ToString("X", Inv));
            W("arr_cv_sbyte_" + i, Convert.ToString(sa[i]));
        }
        for (int i = 0; i < ha.Length; i++)
        {
            W("arr_if_short_" + i, ((IFormattable)ha[i]).ToString("X", Inv));
            W("arr_cv_short_" + i, Convert.ToString(ha[i], Inv));
        }
        for (int i = 0; i < ua.Length; i++)
        {
            W("arr_if_ushort_" + i, ((IFormattable)ua[i]).ToString("X", Inv));
            W("arr_ic_ushort_" + i, ((IConvertible)ua[i]).ToString(Inv));
        }
        for (int i = 0; i < ba.Length; i++)
        {
            W("arr_if_byte_" + i, ((IFormattable)ba[i]).ToString("X2", Inv));
            W("arr_cv_byte_" + i, Convert.ToString(ba[i]));
        }
        W("fld_ic_sbyte", ((IConvertible)_fS).ToString(Inv));
        W("fld_if_short", ((IFormattable)_fH).ToString("X", Inv));
        W("fld_if_ushort", ((IFormattable)_fU).ToString("X", Inv));
        W("fld_cv_byte", Convert.ToString(_fB));
    }

    private static void Main()
    {
        IConvertiblePaths();
        IFormattablePaths();
        ConvertPaths();
        ReceiverShapes();
    }
}
