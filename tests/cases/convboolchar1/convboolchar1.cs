// convboolchar1 -- bool/char conversion surface. Every record is tagged and
// deterministic (no culture-, locale- or encoding-dependent APIs) so the
// output is byte-comparable between net48 and the transpiled runtime.
using System;
using System.Globalization;

internal static class ConvBoolChar1
{
    private static void Try(string tag, Func<object> f)
    {
        try { Console.WriteLine("[" + tag + "] " + Show(f())); }
        catch (Exception ex) { Console.WriteLine("[" + tag + "] " + ex.GetType().Name); }
    }

    private static string Show(object o)
    {
        if (o == null) return "<null>";
        if (o is bool b) return b ? "True" : "False";
        return o.ToString();
    }

    private static void Main()
    {
        // bool.ToString is exactly "True"/"False"
        Console.WriteLine("[b.tostr.true] " + true.ToString());
        Console.WriteLine("[b.tostr.false] " + false.ToString());

        // Convert.ToBoolean from the integral / string shapes
        Try("b.from_int1", () => Convert.ToBoolean(1));
        Try("b.from_int0", () => Convert.ToBoolean(0));
        Try("b.from_int2", () => Convert.ToBoolean(2));
        Try("b.from_dbl", () => Convert.ToBoolean(0.5));
        Try("b.from_str_true", () => Convert.ToBoolean("True"));
        Try("b.from_str_false", () => Convert.ToBoolean("false"));
        Try("b.from_str_x", () => Convert.ToBoolean("yes"));
        Try("b.from_sbyte", () => Convert.ToBoolean((sbyte)-1));
        Try("b.from_long", () => Convert.ToBoolean(4L));

        // char <-> int and char.ToString
        Console.WriteLine("[c.tostr.a] " + 'a'.ToString());
        Console.WriteLine("[c.tostr.cr] " + ((int)'\r').ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[c.neg] " + ((int)(char)0xFFFF).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("[c.arith] " + (char)('a' + 1));
        Console.WriteLine("[c.cast] " + (int)'Z');

        Try("c.from_int", () => Convert.ToChar(65));
        Try("c.from_int_hi", () => Convert.ToChar(0xFFFF));
        Try("c.from_int_over", () => Convert.ToChar(0x10000));
        Try("c.from_str", () => Convert.ToChar("x"));
        Try("c.from_str2", () => Convert.ToChar("xy"));

        // Convert.ToInt32(char) / ToBoolean(char) are InvalidCastException
        Try("i32.from_char", () => Convert.ToInt32('a'));
        Try("b.from_char", () => Convert.ToBoolean('a'));

        // char classification predicates (ASCII + a BMP letter + whitespace)
        char[] probe = { '0', '9', 'a', 'Z', ' ', '\t', '\u00e9', '\u0660' };
        for (int i = 0; i < probe.Length; i++)
        {
            char c = probe[i];
            Console.WriteLine("[cls." + i + "." + ((int)c).ToString(CultureInfo.InvariantCulture) + "] "
                + (char.IsDigit(c) ? "1" : "0")
                + (char.IsLetter(c) ? "1" : "0")
                + (char.IsWhiteSpace(c) ? "1" : "0")
                + (char.IsUpper(c) ? "1" : "0")
                + (char.IsLower(c) ? "1" : "0")
                + (char.IsPunctuation(c) ? "1" : "0"));
        }

        // bool arithmetic / precedence shapes that must not change value
        bool t = true, f = false;
        Console.WriteLine("[b.ops] " + (t & f) + " " + (t | f) + " " + (t ^ f) + " " + (!t));
    }
}
