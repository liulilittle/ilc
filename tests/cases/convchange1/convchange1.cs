// convchange1 -- the "change" side of conversion: the object/IConvertible
// dispatch that Convert.ChangeType is built on, plus explicit cast vs Convert
// differences and checked/unchecked integer changes.
//
// SCOPE NOTE (measured): Convert.ChangeType itself is NOT wired by the
// transpiler -- il2cppc drops the whole method with
//   "System.Convert::ChangeType: not wired in M5 (BCL owner has no mapping arm ...)"
// so a case that called it could never be byte-exact. This case therefore
// pins the surface ChangeType dispatches THROUGH: Convert.To*(object) (the
// boxed IConvertible path), IConvertible.GetTypeCode, and the explicit
// cast / Convert / checked-unchecked lattice. Every record is tagged and
// culture-invariant (CultureInfo.InvariantCulture everywhere a value is
// stringified; chars print as their UTF-16 code unit so control characters
// never reach the byte stream).
using System;
using System.Globalization;

internal static class ConvChange1
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
        if (o is uint u) return u.ToString(CultureInfo.InvariantCulture);
        if (o is ulong ul) return ul.ToString(CultureInfo.InvariantCulture);
        if (o is double d) return d.ToString(CultureInfo.InvariantCulture);
        if (o is float f) return f.ToString(CultureInfo.InvariantCulture);
        if (o is decimal m) return m.ToString(CultureInfo.InvariantCulture);
        return o.ToString();
    }

    private static void Main()
    {
        // ---- A. boxed-object dispatch: Convert.To*(object) ----------------
        // This is the IConvertible path Convert.ChangeType uses internally.
        Try("i32.obj.int", () => Convert.ToInt32((object)42));
        Try("i32.obj.long", () => Convert.ToInt32((object)42L));
        Try("i32.obj.short", () => Convert.ToInt32((object)(short)42));
        Try("i32.obj.byte", () => Convert.ToInt32((object)(byte)42));
        Try("i32.obj.dbl", () => Convert.ToInt32((object)3.9));
        Try("i32.obj.dblhalf", () => Convert.ToInt32((object)3.5));
        Try("i32.obj.bool", () => Convert.ToInt32((object)true));
        Try("i32.obj.char", () => Convert.ToInt32((object)'A'));
        Try("i32.obj.dec", () => Convert.ToInt32((object)3.5m));
        Try("i32.obj.str", () => Convert.ToInt32((object)"42"));
        Try("i32.obj.strws", () => Convert.ToInt32((object)"  -42  "));
        Try("i32.obj.strbad", () => Convert.ToInt32((object)"12x"));
        Try("i32.obj.null", () => Convert.ToInt32((object)null));
        Try("i32.obj.plain", () => Convert.ToInt32((object)new object()));
        Try("i32.obj.ulongmax", () => Convert.ToInt32((object)ulong.MaxValue));

        Try("i64.obj.int", () => Convert.ToInt64((object)42));
        Try("i64.obj.dbl", () => Convert.ToInt64((object)3.5));
        Try("i64.obj.str", () => Convert.ToInt64((object)"-9223372036854775808"));
        Try("i64.obj.null", () => Convert.ToInt64((object)null));
        Try("i64.obj.bad", () => Convert.ToInt64((object)"9223372036854775808"));

        Try("dbl.obj.int", () => Convert.ToDouble((object)42));
        Try("dbl.obj.single", () => Convert.ToDouble((object)1.5f));
        Try("dbl.obj.dec", () => Convert.ToDouble((object)1.5m));
        Try("dbl.obj.str", () => Convert.ToDouble((object)"2.5"));
        Try("dbl.obj.null", () => Convert.ToDouble((object)null));

        Try("bool.obj.int0", () => Convert.ToBoolean((object)0));
        Try("bool.obj.long", () => Convert.ToBoolean((object)1L));
        Try("bool.obj.dbl", () => Convert.ToBoolean((object)0.0));
        Try("bool.obj.str", () => Convert.ToBoolean((object)"false"));
        Try("bool.obj.null", () => Convert.ToBoolean((object)null));

        Try("char.obj.int", () => Convert.ToChar((object)65));
        Try("char.obj.char", () => Convert.ToChar((object)'A'));
        Try("char.obj.str", () => Convert.ToChar((object)"A"));
        Try("char.obj.null", () => Convert.ToChar((object)null));
        Try("char.obj.over", () => Convert.ToChar((object)70000));

        Try("str.obj.null", () => Convert.ToString((object)null));
        Try("str.obj.int", () => Convert.ToString((object)42));
        Try("str.obj.text", () => Convert.ToString((object)"text"));
        Try("str.obj.bool", () => Convert.ToString((object)true));

        // ---- B. explicit cast vs Convert ---------------------------------
        // A cast truncates; Convert rounds half-to-even.
        double d39 = 3.9, dn39 = -3.9, d35 = 3.5;
        Console.WriteLine("[cast.int.39] " + ((int)d39).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[cast.int.n39] " + ((int)dn39).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[conv.int.39] " + Convert.ToInt32(d39).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[conv.int.n39] " + Convert.ToInt32(dn39).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[cast.int.35] " + ((int)d35).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[conv.int.35] " + Convert.ToInt32(d35).ToString(CultureInfo.InvariantCulture));
        decimal m39 = 3.9m, m35 = 3.5m;
        Console.WriteLine("[cast.int.m39] " + ((int)m39).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[conv.int.m39] " + Convert.ToInt32(m39).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[cast.int.m35] " + ((int)m35).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[conv.int.m35] " + Convert.ToInt32(m35).ToString(CultureInfo.InvariantCulture));
        float f39 = 3.99f;
        Console.WriteLine("[cast.int.f39] " + ((int)f39).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[conv.int.f39] " + Convert.ToInt32(f39).ToString(CultureInfo.InvariantCulture));

        // ---- C. checked / unchecked integer change -----------------------
        int i300 = 300;
        short s40000 = unchecked((short)40000);
        Console.WriteLine("[unchk.byte300] " + unchecked((byte)i300).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[unchk.short40000] " + s40000.ToString(CultureInfo.InvariantCulture));
        Try("chk.byte300", () => checked((byte)i300));
        Try("conv.byte300", () => Convert.ToByte(i300));
        int i40000 = 40000;
        Try("chk.short40000", () => checked((short)i40000));
        Try("conv.short40000", () => Convert.ToInt16(40000));
        uint umax = uint.MaxValue;
        Console.WriteLine("[unchk.int.umax] " + unchecked((int)umax).ToString(CultureInfo.InvariantCulture));
        Try("chk.int.umax", () => checked((int)umax));
        Try("conv.int.umax", () => Convert.ToInt32(umax));
        int neg = -1;
        Console.WriteLine("[unchk.uint.m1] " + unchecked((uint)neg).ToString(CultureInfo.InvariantCulture));
        Try("chk.uint.m1", () => checked((uint)neg));
        Try("conv.uint.m1", () => Convert.ToUInt32(neg));

        // ---- D. IConvertible.GetTypeCode on boxed values ------------------
        // (int) of the TypeCode enum keeps the record independent of enum
        // ToString support.
        object oi = 42, os = "x", ob = true, od = 1.5, om = 1.5m;
        Console.WriteLine("[tc.int] " + ((int)((IConvertible)oi).GetTypeCode()).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[tc.str] " + ((int)((IConvertible)os).GetTypeCode()).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[tc.bool] " + ((int)((IConvertible)ob).GetTypeCode()).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[tc.dbl] " + ((int)((IConvertible)od).GetTypeCode()).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[tc.dec] " + ((int)((IConvertible)om).GetTypeCode()).ToString(CultureInfo.InvariantCulture));
        Try("tc.null", () => ((IConvertible)(object)null).GetTypeCode());

        // ---- E. IConvertible.ToInt32 identity on a boxed Int32 ------------
        object boxedInt = 1234;
        Console.WriteLine("[iconv.i32] " + ((IConvertible)boxedInt).ToInt32(CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture));

        // ---- F. string parse lattice shared by the object dispatch --------
        Try("parse.trim", () => Convert.ToInt32("  42\t"));
        Try("parse.plus", () => Convert.ToInt32("+7"));
        Try("parse.empty", () => Convert.ToInt32(""));
        Try("parse.hex", () => Convert.ToInt32("0x1F", 16));
        Try("parse.badbase", () => Convert.ToInt32("10", 3));
    }
}
