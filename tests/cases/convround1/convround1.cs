// convround1 -- the ROUNDING half of conversion, pinned record by record:
// Convert.ToInt32/ToInt64 from double/float/decimal (net48 rounds
// half-to-even FIRST, then range-checks) and the narrow integer targets
// (ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64) at their boundaries.
//
// The counterpart of the plain checked narrowing (convchange1): here the
// interesting part is that Convert ROUNDS while an explicit cast TRUNCATES,
// and that the range test happens on the ROUNDED value.
//
// Every record is tagged; all values are printed through
// CultureInfo.InvariantCulture so the byte stream is culture-free.
using System;
using System.Globalization;

internal static class ConvRound1
{
    private static string I(long v) { return v.ToString(CultureInfo.InvariantCulture); }
    private static string U(ulong v) { return v.ToString(CultureInfo.InvariantCulture); }

    private static void Try(string tag, Func<object> f)
    {
        try { Console.WriteLine("[" + tag + "] " + Show(f())); }
        catch (Exception ex) { Console.WriteLine("[" + tag + "] " + ex.GetType().Name); }
    }

    private static string Show(object o)
    {
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
        if (o is float f) return f.ToString(CultureInfo.InvariantCulture);
        if (o is decimal m) return m.ToString(CultureInfo.InvariantCulture);
        return o.ToString();
    }

    private static void Main()
    {
        // ---- A. Convert.ToInt32(double): half-to-even at the .5 boundary ---
        Try("i32d.0", () => Convert.ToInt32(0.0));
        Try("i32d.05", () => Convert.ToInt32(0.5));
        Try("i32d.15", () => Convert.ToInt32(1.5));
        Try("i32d.25", () => Convert.ToInt32(2.5));
        Try("i32d.35", () => Convert.ToInt32(3.5));
        Try("i32d.45", () => Convert.ToInt32(4.5));
        Try("i32d.n05", () => Convert.ToInt32(-0.5));
        Try("i32d.n15", () => Convert.ToInt32(-1.5));
        Try("i32d.n25", () => Convert.ToInt32(-2.5));
        Try("i32d.n35", () => Convert.ToInt32(-3.5));
        Try("i32d.04999", () => Convert.ToInt32(0.4999));
        Try("i32d.299", () => Convert.ToInt32(2.99));
        Try("i32d.n299", () => Convert.ToInt32(-2.99));

        // ---- B. ToInt32(double) range lattice (checked on the ROUNDED value)
        Try("i32d.max", () => Convert.ToInt32(2147483647.0));
        Try("i32d.min", () => Convert.ToInt32(-2147483648.0));
        Try("i32d.maxm05", () => Convert.ToInt32(2147483646.5));
        Try("i32d.maxm05p", () => Convert.ToInt32(2147483647.5));
        Try("i32d.p31", () => Convert.ToInt32(2147483648.0));
        Try("i32d.minm", () => Convert.ToInt32(-2147483648.5));
        Try("i32d.np31", () => Convert.ToInt32(-2147483649.0));
        Try("i32d.nan", () => Convert.ToInt32(double.NaN));
        Try("i32d.inf", () => Convert.ToInt32(double.PositiveInfinity));
        Try("i32d.ninf", () => Convert.ToInt32(double.NegativeInfinity));

        // ---- C. float sources ride the same rounding path -----------------
        Try("i32f.25", () => Convert.ToInt32(2.5f));
        Try("i32f.35", () => Convert.ToInt32(3.5f));
        Try("i32f.n25", () => Convert.ToInt32(-2.5f));
        Try("i32f.nan", () => Convert.ToInt32(float.NaN));
        Try("i32f.inf", () => Convert.ToInt32(float.PositiveInfinity));

        // ---- D. Convert.ToInt64(double) -----------------------------------
        Try("i64d.25", () => Convert.ToInt64(2.5));
        Try("i64d.35", () => Convert.ToInt64(3.5));
        Try("i64d.n25", () => Convert.ToInt64(-2.5));
        Try("i64d.n35", () => Convert.ToInt64(-3.5));
        Try("i64d.min", () => Convert.ToInt64(-9.2233720368547758E18));
        Try("i64d.p63", () => Convert.ToInt64(9.2233720368547758E18));
        Try("i64d.1e19", () => Convert.ToInt64(1.0E19));
        Try("i64d.nan", () => Convert.ToInt64(double.NaN));
        Try("i64d.inf", () => Convert.ToInt64(double.PositiveInfinity));
        Try("i64f.35", () => Convert.ToInt64(3.5f));

        // ---- E. decimal sources: Convert ROUNDS (cast truncates) ----------
        Try("i32m.35", () => Convert.ToInt32(3.5m));
        Try("i32m.45", () => Convert.ToInt32(4.5m));
        Try("i32m.129", () => Convert.ToInt32(12.9m));
        Try("i32m.n09", () => Convert.ToInt32(-0.9m));
        Try("i32m.n35", () => Convert.ToInt32(-3.5m));
        Try("i32m.maxm04", () => Convert.ToInt32(2147483647.4m));
        Try("i32m.maxp06", () => Convert.ToInt32(2147483647.6m));
        Try("i64m.35", () => Convert.ToInt64(3.5m));
        Try("i64m.n35", () => Convert.ToInt64(-3.5m));
        Try("i64m.maxp06", () => Convert.ToInt64(9223372036854775807.6m));

        // ---- F. Convert.ToByte boundary -----------------------------------
        Try("u8.0", () => Convert.ToByte(0));
        Try("u8.255", () => Convert.ToByte(255));
        Try("u8.256", () => Convert.ToByte(256));
        Try("u8.m1", () => Convert.ToByte(-1));
        Try("u8.35d", () => Convert.ToByte(3.5));
        Try("u8.2545d", () => Convert.ToByte(254.5));
        Try("u8.2555d", () => Convert.ToByte(255.5));
        Try("u8.m05d", () => Convert.ToByte(-0.5));
        Try("u8.255m", () => Convert.ToByte(255m));
        Try("u8.2555m", () => Convert.ToByte(255.5m));

        // ---- G. Convert.ToSByte / ToInt16 / ToUInt16 boundaries -----------
        Try("i8.127", () => Convert.ToSByte(127));
        Try("i8.128", () => Convert.ToSByte(128));
        Try("i8.m128", () => Convert.ToSByte(-128));
        Try("i8.m129", () => Convert.ToSByte(-129));
        Try("i8.m05d", () => Convert.ToSByte(-0.5));
        Try("i16.32767", () => Convert.ToInt16(32767));
        Try("i16.32768", () => Convert.ToInt16(32768));
        Try("i16.m32768", () => Convert.ToInt16(-32768));
        Try("i16.m32769", () => Convert.ToInt16(-32769));
        Try("u16.65535", () => Convert.ToUInt16(65535));
        Try("u16.65536", () => Convert.ToUInt16(65536));
        Try("u16.m1", () => Convert.ToUInt16(-1));
        Try("u16.65534m", () => Convert.ToUInt16(65534m));

        // ---- H. Convert.ToUInt32 / ToUInt64 --------------------------------
        Try("u32.max", () => Convert.ToUInt32(4294967295u));
        Try("u32.maxp", () => Convert.ToUInt32(4294967296L));
        Try("u32.m1", () => Convert.ToUInt32(-1));
        Try("u32.35d", () => Convert.ToUInt32(3.5));
        Try("u32.m05d", () => Convert.ToUInt32(-0.5));
        Try("u64.max", () => Convert.ToUInt64(ulong.MaxValue));
        Try("u64.m1", () => Convert.ToUInt64(-1));
        Try("u64.m05d", () => Convert.ToUInt64(-0.5));

        // ---- I. printed extremes confirm the full-width decimal render -----
        Console.WriteLine("[print.u8] " + Convert.ToByte(255).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[print.u16] " + Convert.ToUInt16(65535).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[print.u32] " + Convert.ToUInt32(4294967295u).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[print.u64] " + Convert.ToUInt64(ulong.MaxValue).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[print.i32min] " + Convert.ToInt32(-2147483648.0).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[print.i64min] " + Convert.ToInt64(-9.2233720368547758E18).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[print.lit] " + I(-2147483648L) + " " + U(18446744073709551615UL));
    }
}
