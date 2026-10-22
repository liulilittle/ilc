//------------------------------------------------------------------------------
// p0narrow2.cs -- narrow-integer ToString(string format) reference program.
//
// Defect under test (P0-narrow, part 2): SByte / Int16 / UInt16 (and Byte, which
// had the same hole) had NO ToString(string format) wiring in
// MethodMap.Format.cs -- the ps[0] == 0x0E switch fell to `_ => null`, the call
// fell back to the generated-name path and the emitted C++ referenced a
// nonexistent symbol (System_SByte_ToString_<hash> etc.) -- a SILENT transpile
// success followed by a C++ compile failure.
//
// Runs on REAL .NET Framework 4.8 (net48); its output is the ORACLE. The key
// semantic the fix must honor (verified by a net48 probe): the hex specifiers
// X/x format the TYPE's own unsigned bit pattern with minimal digits, so
// (sbyte)-1 "X" is "FF" (NOT the int32 engine's "FFFFFFFF") and (short)-1 "X"
// is "FFFF"; every other specifier is the int32 engine on the extended value.
//
// Coverage: D / X / X2 / X4 / D5 / N0 / N2 / C / E / F / G / P / "" / custom,
// for local, array-element (ADDRESS), boxed, and composite (string.Format)
// shapes, including negative and MinValue/MaxValue boundaries.
//------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;

internal static class P0Narrow2
{
    private static void W(string k, string v)
    {
        Console.WriteLine(k + "=[" + v + "]");
    }

    private static void Dump(string tag, byte b, sbyte sb, short sh, ushort us)
    {
        W(tag + "_byte_D", b.ToString("D"));
        W(tag + "_sbyte_D", sb.ToString("D"));
        W(tag + "_short_D", sh.ToString("D"));
        W(tag + "_ushort_D", us.ToString("D"));

        W(tag + "_byte_X", b.ToString("X"));
        W(tag + "_sbyte_X", sb.ToString("X"));
        W(tag + "_short_X", sh.ToString("X"));
        W(tag + "_ushort_X", us.ToString("X"));

        W(tag + "_byte_X2", b.ToString("X2"));
        W(tag + "_sbyte_X2", sb.ToString("X2"));
        W(tag + "_short_X2", sh.ToString("X2"));
        W(tag + "_ushort_X2", us.ToString("X2"));

        W(tag + "_byte_X4", b.ToString("X4"));
        W(tag + "_sbyte_X4", sb.ToString("X4"));
        W(tag + "_short_X4", sh.ToString("X4"));
        W(tag + "_ushort_X4", us.ToString("X4"));

        W(tag + "_byte_D5", b.ToString("D5"));
        W(tag + "_sbyte_D5", sb.ToString("D5"));
        W(tag + "_short_D5", sh.ToString("D5"));
        W(tag + "_ushort_D5", us.ToString("D5"));

        W(tag + "_byte_N0", b.ToString("N0"));
        W(tag + "_sbyte_N0", sb.ToString("N0"));
        W(tag + "_short_N0", sh.ToString("N0"));
        W(tag + "_ushort_N0", us.ToString("N0"));

        W(tag + "_byte_N2", b.ToString("N2"));
        W(tag + "_sbyte_N2", sb.ToString("N2"));
        W(tag + "_short_N2", sh.ToString("N2"));
        W(tag + "_ushort_N2", us.ToString("N2"));

        W(tag + "_byte_C", b.ToString("C"));
        W(tag + "_sbyte_C", sb.ToString("C"));
        W(tag + "_short_C", sh.ToString("C"));
        W(tag + "_ushort_C", us.ToString("C"));

        W(tag + "_byte_E", b.ToString("E"));
        W(tag + "_sbyte_E", sb.ToString("E"));
        W(tag + "_short_E", sh.ToString("E"));
        W(tag + "_ushort_E", us.ToString("E"));

        W(tag + "_byte_F", b.ToString("F"));
        W(tag + "_sbyte_F", sb.ToString("F"));
        W(tag + "_short_F", sh.ToString("F"));
        W(tag + "_ushort_F", us.ToString("F"));

        W(tag + "_byte_G", b.ToString("G"));
        W(tag + "_sbyte_G", sb.ToString("G"));
        W(tag + "_short_G", sh.ToString("G"));
        W(tag + "_ushort_G", us.ToString("G"));

        W(tag + "_byte_P", b.ToString("P"));
        W(tag + "_sbyte_P", sb.ToString("P"));
        W(tag + "_short_P", sh.ToString("P"));
        W(tag + "_ushort_P", us.ToString("P"));

        W(tag + "_byte_empty", b.ToString(""));
        W(tag + "_short_empty", sh.ToString(""));
        W(tag + "_byte_custom000", b.ToString("000"));
        W(tag + "_sbyte_custom000", sb.ToString("000"));
        W(tag + "_short_custom00000", sh.ToString("00000"));
    }

    private static void Locals()
    {
        Dump("loc", 255, -1, -1, 65535);
        Dump("loc5", 5, 5, 5, 5);
        Dump("locneg", 5, -5, -5, 5);
    }

    private static void Boundaries()
    {
        Dump("bmax", byte.MaxValue, sbyte.MaxValue, short.MaxValue, ushort.MaxValue);
        Dump("bmin", byte.MinValue, sbyte.MinValue, short.MinValue, ushort.MinValue);
    }

    private static void Arrays()
    {
        sbyte[] sa = new sbyte[] { -1, -128, 127, 0 };
        short[] ha = new short[] { -1, -32768, 32767, 0 };
        ushort[] ua = new ushort[] { 65535, 0, 40000, 1 };
        byte[] ba = new byte[] { 255, 0, 128, 1 };
        for (int i = 0; i < sa.Length; i++)
        {
            W("arr_sbyte_" + i + "_X", sa[i].ToString("X"));
            W("arr_sbyte_" + i + "_D5", sa[i].ToString("D5"));
            W("arr_sbyte_" + i + "_N0", sa[i].ToString("N0"));
        }
        for (int i = 0; i < ha.Length; i++)
        {
            W("arr_short_" + i + "_X", ha[i].ToString("X"));
            W("arr_short_" + i + "_X4", ha[i].ToString("X4"));
            W("arr_short_" + i + "_D", ha[i].ToString("D"));
        }
        for (int i = 0; i < ua.Length; i++)
        {
            W("arr_ushort_" + i + "_X", ua[i].ToString("X"));
            W("arr_ushort_" + i + "_N0", ua[i].ToString("N0"));
        }
        for (int i = 0; i < ba.Length; i++)
        {
            W("arr_byte_" + i + "_X", ba[i].ToString("X"));
            W("arr_byte_" + i + "_X2", ba[i].ToString("X2"));
        }
        W("arr_sbyte_concat", "v=" + sa[0].ToString("X"));
        W("arr_short_concat", "v=" + ha[1].ToString("X"));
    }

    private static void Boxed()
    {
        object ob = (byte)255;
        object osb = (sbyte)-1;
        object osh = (short)-1;
        object ous = (ushort)65535;
        W("box_byte_X", ob.ToString());
        // Object.ToString ignores the format (the boxed narrow types do not
        // expose ToString(format) through System.Object); the FORMATTED boxed
        // shape goes through composite formatting below.
        W("box_sbyte_tostring", osb.ToString());
        W("box_short_tostring", osh.ToString());
        W("box_ushort_tostring", ous.ToString());

        W("comp_byte_X", string.Format("{0:X}", (byte)255));
        W("comp_sbyte_X", string.Format("{0:X}", (sbyte)-1));
        W("comp_short_X", string.Format("{0:X}", (short)-1));
        W("comp_ushort_X", string.Format("{0:X}", (ushort)65535));
        W("comp_byte_X4", string.Format("{0:X4}", (byte)255));
        W("comp_sbyte_D5", string.Format("{0:D5}", (sbyte)-1));
        W("comp_short_D5", string.Format("{0:D5}", (short)-5));
        W("comp_byte_N0", string.Format("{0:N0}", (byte)255));
        W("comp_ushort_N2", string.Format("{0:N2}", (ushort)65535));
        W("comp_byte_C", string.Format("{0:C}", (byte)255));
        W("comp_sbyte_C", string.Format("{0:C}", (sbyte)-1));
        W("comp_short_F", string.Format("{0:F}", (short)-32768));
        W("comp_ushort_E", string.Format("{0:E}", (ushort)65535));
        W("comp_byte_align", string.Format("[{0,10:X}]", (byte)255));
        W("comp_sbyte_align_l", string.Format("[{0,-10:X}]", (sbyte)-1));
        W("comp_byte_empty", string.Format("{0:}", (byte)255));
        W("comp_short_bad", FormatOrEx(() => string.Format("{0:Q}", (short)-1)));
    }

    private static string FormatOrEx(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "EX:" + e.GetType().Name; }
    }

    private static void Providers()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        byte b = 255;
        sbyte sb = -1;
        short sh = -1;
        ushort us = 65535;
        W("inv_byte_X", b.ToString("X", inv));
        W("inv_sbyte_X", sb.ToString("X", inv));
        W("inv_short_X", sh.ToString("X", inv));
        W("inv_ushort_X", us.ToString("X", inv));
        W("inv_byte_X2", b.ToString("X2", inv));
        W("inv_sbyte_D5", sb.ToString("D5", inv));
        W("inv_short_N0", sh.ToString("N0", inv));
        W("inv_byte_N0", b.ToString("N0", inv));
        W("inv_byte_C", b.ToString("C", inv));
        W("inv_sbyte_C", sb.ToString("C", inv));
        W("inv_byte_P", b.ToString("P", inv));
        W("inv_short_P", sh.ToString("P", inv));
        W("inv_ushort_D", us.ToString("D", inv));
    }

    private static void Errors()
    {
        W("err_byte_Q", FormatOrEx(() => ((byte)255).ToString("Q")));
        W("err_sbyte_Q", FormatOrEx(() => ((sbyte)-1).ToString("Q")));
        W("err_short_Q", FormatOrEx(() => ((short)-1).ToString("Q")));
        W("err_ushort_Q", FormatOrEx(() => ((ushort)65535).ToString("Q")));
    }

    private static void Builder()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(((byte)255).ToString("X"));
        sb.Append('|');
        sb.Append(((sbyte)-1).ToString("X"));
        sb.Append('|');
        sb.Append(((short)-1).ToString("X"));
        sb.Append('|');
        sb.Append(((ushort)65535).ToString("X"));
        W("sb", sb.ToString());
        W("sb_fmt", string.Format("{0:X}-{1:X}-{2:X}-{3:X}", (byte)255, (sbyte)-1, (short)-1, (ushort)65535));
    }

    private static void Main()
    {
        Locals();
        Boundaries();
        Arrays();
        Boxed();
        Providers();
        Errors();
        Builder();
    }
}
