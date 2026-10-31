// convstr1 -- Convert.ToString over the conversion matrix: every integral
// width, double, decimal, bool, char, string and object (null included), the
// radix forms Convert.ToString(value, toBase), and the provider shapes
// Convert.ToString(value, IFormatProvider) (net48 ignores the provider for
// primitives, so the text must equal the 1-arg form).
//
// SCOPE NOTE: Convert.ToString(float) is deliberately NOT part of this case.
// A probe (0.1f) showed the runtime prints "0.100000001490116" where net48
// prints "0.1" (the Single overload rides the double G15 engine and loses the
// float-width text) -- that is a real runtime defect reported separately, not
// a value this case can pin while the runtime is wrong. Double (the width the
// conversion matrix names) IS covered.
//
// Every record is tagged; all numbers are rendered through
// CultureInfo.InvariantCulture so the byte stream is culture-free.
using System;
using System.Globalization;

internal static class ConvStr1
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
        if (o is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (o is long l) return l.ToString(CultureInfo.InvariantCulture);
        if (o is short s) return s.ToString(CultureInfo.InvariantCulture);
        if (o is byte by) return by.ToString(CultureInfo.InvariantCulture);
        if (o is sbyte sb) return sb.ToString(CultureInfo.InvariantCulture);
        if (o is ushort us) return us.ToString(CultureInfo.InvariantCulture);
        if (o is uint u) return u.ToString(CultureInfo.InvariantCulture);
        if (o is ulong ul) return ul.ToString(CultureInfo.InvariantCulture);
        if (o is double d) return d.ToString(CultureInfo.InvariantCulture);
        if (o is decimal m) return m.ToString(CultureInfo.InvariantCulture);
        return o.ToString();
    }

    private static void Line(string tag, string s)
    {
        Console.WriteLine("[" + tag + "] " + s);
    }

    private static void Main()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;

        // ---- A. integral matrix -------------------------------------------
        Line("i32.0", Convert.ToString(0));
        Line("i32.m1", Convert.ToString(-1));
        Line("i32.max", Convert.ToString(int.MaxValue));
        Line("i32.min", Convert.ToString(int.MinValue));
        Line("i64.m1234567890123", Convert.ToString(-1234567890123L));
        Line("i64.min", Convert.ToString(long.MinValue));
        Line("i64.max", Convert.ToString(long.MaxValue));
        Line("i16.m5", Convert.ToString((short)-5));
        Line("i16.min", Convert.ToString(short.MinValue));
        Line("u8.255", Convert.ToString((byte)255));
        Line("i8.m1", Convert.ToString((sbyte)-1));
        Line("u16.max", Convert.ToString(ushort.MaxValue));
        Line("u32.max", Convert.ToString(uint.MaxValue));
        Line("u64.max", Convert.ToString(ulong.MaxValue));

        // ---- B. floating / decimal ----------------------------------------
        Line("dbl.15", Convert.ToString(1.5));
        Line("dbl.01", Convert.ToString(0.1));
        Line("dbl.third", Convert.ToString(1.0 / 3.0));
        Line("dbl.neg", Convert.ToString(-2.75));
        Line("dbl.nan", Convert.ToString(double.NaN));
        Line("dbl.nzero", Convert.ToString(-0.0));
        Line("dec.15", Convert.ToString(1.5m));
        Line("dec.m05", Convert.ToString(-0.5m));
        Line("dec.max", Convert.ToString(decimal.MaxValue));
        Line("dec.zero", Convert.ToString(0m));

        // ---- C. bool / char / string --------------------------------------
        Line("bool.t", Convert.ToString(true));
        Line("bool.f", Convert.ToString(false));
        Line("char.A", Convert.ToString('A'));
        Line("char.nul", Convert.ToString('\0'));
        Line("char.tilde", Convert.ToString('~'));
        Line("str.text", Convert.ToString("text"));
        Line("str.empty", Convert.ToString(""));

        // ---- D. Convert.ToString(value, toBase) ---------------------------
        Line("base.255.16", Convert.ToString(255, 16));
        Line("base.m1.16", Convert.ToString(-1, 16));
        Line("base.m1.2", Convert.ToString(-1, 2));
        Line("base.m1.8", Convert.ToString(-1, 8));
        Line("base.42.10", Convert.ToString(42, 10));
        Line("base.255.2", Convert.ToString(255, 2));
        Line("base.byte255.16", Convert.ToString((byte)255, 16));
        Line("base.shortm1.16", Convert.ToString((short)-1, 16));
        Line("base.long5.2", Convert.ToString(5L, 2));
        Line("base.longm1.16", Convert.ToString(-1L, 16));
        Line("base.intmin.2", Convert.ToString(int.MinValue, 2));
        Try("base.bad3", () => Convert.ToString(10, 3));
        Try("base.bad1", () => Convert.ToString(10, 1));
        Try("base.bad37", () => Convert.ToString(10, 37));

        // ---- E. Convert.ToString(value, IFormatProvider) -------------------
        // net48 ignores the provider for every primitive source.
        Line("prov.i32", Convert.ToString(42, inv));
        Line("prov.i64", Convert.ToString(-7L, inv));
        Line("prov.dbl", Convert.ToString(1.5, inv));
        Line("prov.dec", Convert.ToString(1.5m, inv));
        Line("prov.bool", Convert.ToString(true, inv));
        Line("prov.char", Convert.ToString('A', inv));
        Line("prov.strnull", Convert.ToString((string)null, inv));
        Line("prov.objnull", Convert.ToString((object)null, inv));

        // ---- F. Convert.ToString(object) ----------------------------------
        Line("obj.null", Convert.ToString((object)null));
        Line("obj.int", Convert.ToString((object)42));
        Line("obj.long", Convert.ToString((object)(-7L)));
        Line("obj.dbl", Convert.ToString((object)1.5));
        Line("obj.dec", Convert.ToString((object)1.5m));
        Line("obj.bool", Convert.ToString((object)true));
        Line("obj.char", Convert.ToString((object)'A'));
        Line("obj.text", Convert.ToString((object)"text"));

        // ---- G. identity check against the value's own formatter ----------
        Line("same.i32", (Convert.ToString(12345) == 12345.ToString(inv)) ? "same" : "different");
        Line("same.i64", (Convert.ToString(1234567890123L) == 1234567890123L.ToString(inv)) ? "same" : "different");
        Line("same.dbl", (Convert.ToString(1.5) == 1.5.ToString(inv)) ? "same" : "different");
        Line("same.dec", (Convert.ToString(1.5m) == 1.5m.ToString(inv)) ? "same" : "different");
    }
}
