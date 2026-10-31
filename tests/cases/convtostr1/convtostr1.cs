// convtostr1 -- object.ToString on boxed primitives, the IFormattable
// explicit-interface shape, format-specifier edges (X8/D10/F2/E3/G/N/empty/
// unknown), DateTime format strings, and Nullable<T>.ToString().
//
// SCOPE NOTE: DateTime is exercised through DateTime.ToString(format,
// IFormatProvider); the explicit `((IFormattable)dt)` call is NOT wired by the
// transpiler (probe: receiverType='System.DateTime' -> "not wired in M5", the
// method is dropped and the runtime prints NotSupportedException), so it is
// not used here. Boxed Single is also left out: Convert.ToString(float) has a
// real runtime defect (reported separately); Single.ToString(provider) itself
// is correct but belongs to the Single formatting domain.
//
// SUBSET BOUNDARY (documented in src/runtime/src/formatext.cpp, the
// icall_format_composite_item UInt64 arm): a boxed UInt64 value ABOVE
// long.MaxValue ignores the format specifier and renders plain decimal text,
// so the record below uses (ulong)long.MaxValue -- inside the wired
// signed-engine range -- rather than ulong.MaxValue.
//
// Every record is tagged; providers are CultureInfo.InvariantCulture
// everywhere a format is applied, so the byte stream is culture-free.
using System;
using System.Globalization;

internal static class ConvToStr1
{
    private static void Try(string tag, Func<object> f)
    {
        try { Console.WriteLine("[" + tag + "] " + Show(f())); }
        catch (Exception ex) { Console.WriteLine("[" + tag + "] " + ex.GetType().Name); }
    }

    private static string Show(object o)
    {
        if (o == null) return "<null>";
        if (o is char c) return ((int)c).ToString(CultureInfo.InvariantCulture);
        if (o is bool b) return b ? "True" : "False";
        return (string)o;
    }

    private static void Main()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;

        // ---- A. Object.ToString on boxed primitives -----------------------
        object bi = 42, bl = -7L, bd = 1.5, bm = 1.5m, bb = true, bc = 'A', bs = "text";
        object b8 = (byte)255, sb8 = (sbyte)-1, b16 = (short)-5, bu16 = (ushort)65535;
        object bu32 = 4294967295u, bu64 = ulong.MaxValue, bf = 1.5f;
        Try("box.i32", () => bi.ToString());
        Try("box.i64", () => bl.ToString());
        Try("box.dbl", () => bd.ToString());
        Try("box.dec", () => bm.ToString());
        Try("box.bool", () => bb.ToString());
        Try("box.char", () => bc.ToString());
        Try("box.str", () => bs.ToString());
        Try("box.u8", () => b8.ToString());
        Try("box.i8", () => sb8.ToString());
        Try("box.i16", () => b16.ToString());
        Try("box.u16", () => bu16.ToString());
        Try("box.u32", () => bu32.ToString());
        Try("box.u64", () => bu64.ToString());
        Try("box.single", () => bf.ToString());

        // ---- B. IFormattable.ToString(format, provider) --------------------
        int neg = -1, max = 255;
        Try("if.i32.x8", () => ((IFormattable)max).ToString("X8", inv));
        Try("if.i32.x4", () => ((IFormattable)max).ToString("X4", inv));
        Try("if.i32.d10", () => ((IFormattable)neg).ToString("D10", inv));
        Try("if.i32.g", () => ((IFormattable)max).ToString("G", inv));
        Try("if.i32.empty", () => ((IFormattable)max).ToString("", inv));
        Try("if.i32.null", () => ((IFormattable)max).ToString(null, inv));
        Try("if.i32.n0", () => ((IFormattable)1234567).ToString("N0", inv));
        Try("if.i32.q", () => ((IFormattable)max).ToString("Q", inv));
        Try("if.dbl.f2", () => ((IFormattable)1.23456).ToString("F2", inv));
        Try("if.dbl.e3", () => ((IFormattable)1.23456).ToString("E3", inv));
        Try("if.dbl.g", () => ((IFormattable)1.23456).ToString("G", inv));
        Try("if.dbl.g3", () => ((IFormattable)1.23456).ToString("G3", inv));
        Try("if.dbl.empty", () => ((IFormattable)1.23456).ToString("", inv));
        Try("if.dbl.q", () => ((IFormattable)1.23456).ToString("Q", inv));
        Try("if.dec.f2", () => ((IFormattable)1.5m).ToString("F2", inv));
        Try("if.dec.e3", () => ((IFormattable)1.5m).ToString("E3", inv));
        Try("if.dec.g", () => ((IFormattable)1.5m).ToString("G", inv));
        Try("if.long.x16", () => ((IFormattable)(-1L)).ToString("X16", inv));
        Try("if.u64.x16", () => ((IFormattable)(ulong)long.MaxValue).ToString("X16", inv));

        // ---- C. direct ToString(format, provider) on the numeric types -----
        Console.WriteLine("[f.i32.x8] " + max.ToString("X8", inv));
        Console.WriteLine("[f.i32.x8lc] " + max.ToString("x8", inv));
        Console.WriteLine("[f.i32.d10] " + neg.ToString("D10", inv));
        Console.WriteLine("[f.i32.n0] " + 1234567.ToString("N0", inv));
        Console.WriteLine("[f.dbl.f2] " + 1.23456.ToString("F2", inv));
        Console.WriteLine("[f.dbl.e3] " + 1.23456.ToString("E3", inv));
        Console.WriteLine("[f.dbl.g3] " + 1.23456.ToString("G3", inv));
        Console.WriteLine("[f.dbl.g17] " + 0.1.ToString("G17", inv));
        Console.WriteLine("[f.dec.f2] " + 1.5m.ToString("F2", inv));
        Console.WriteLine("[f.dec.e3] " + 1.5m.ToString("E3", inv));
        Try("f.i32.q", () => max.ToString("Q", inv));
        Try("f.dbl.q", () => 1.23456.ToString("Q", inv));
        Try("f.dec.q", () => 1.5m.ToString("Q", inv));

        // ---- D. DateTime format strings ------------------------------------
        DateTime dt = new DateTime(2024, 2, 29, 13, 45, 6);
        Console.WriteLine("[dt.def] " + dt.ToString(inv));
        Console.WriteLine("[dt.custom] " + dt.ToString("yyyy-MM-dd HH:mm:ss", inv));
        Console.WriteLine("[dt.date] " + dt.ToString("yyyy-MM-dd", inv));
        Console.WriteLine("[dt.compact] " + dt.ToString("yyyyMMdd", inv));
        Console.WriteLine("[dt.O] " + dt.ToString("O", inv));
        Console.WriteLine("[dt.o] " + dt.ToString("o", inv));
        Console.WriteLine("[dt.s] " + dt.ToString("s", inv));
        Console.WriteLine("[dt.literal] " + dt.ToString("qq", inv));

        // ---- E. Nullable<T>.ToString() -------------------------------------
        int? ni = 5;
        int? ne = null;
        double? nd = 1.5;
        bool? nb = true;
        decimal? nm = 1.5m;
        Console.WriteLine("[null.i32] " + ni.ToString());
        Console.WriteLine("[null.empty] <" + ne.ToString() + ">");
        Console.WriteLine("[null.dbl] " + nd.ToString());
        Console.WriteLine("[null.bool] " + nb.ToString());
        Console.WriteLine("[null.dec] " + nm.ToString());
        Console.WriteLine("[null.hasvalue] " + (ni.HasValue ? "1" : "0") + (ne.HasValue ? "1" : "0"));
    }
}

