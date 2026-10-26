//------------------------------------------------------------------------------
// sbloud2.cs -- residual System.Text.StringBuilder overload sweep (net48).
//
// The Append/Insert value families (char[], decimal, object, narrow ints,
// float), the AppendLine()/WriteLine arities, the Replace/CopyTo/EnsureCapacity
// shapes, Equals(object), and the provider AppendFormat forms. Each family is
// a separate method so one transpile gap does not hide the others.
//------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;

class Program
{
    static void P(string id, object value)
    {
        Console.WriteLine("[" + id + "] " + value);
    }

    static void Ctors()
    {
        StringBuilder a = new StringBuilder();
        StringBuilder b = new StringBuilder(8);
        StringBuilder c = new StringBuilder("abc");
        StringBuilder d = new StringBuilder("abc", 8);
        StringBuilder e = new StringBuilder("abcdef", 1, 3, 8);
        StringBuilder f = new StringBuilder(4, 32);
        P("ctor.default", a.Length);
        P("ctor.cap", b.Capacity);
        P("ctor.str", c.ToString());
        P("ctor.str.cap", d.ToString());
        P("ctor.range", e.ToString());
        P("ctor.cap.max", f.Capacity);
    }

    static void AppendValues()
    {
        StringBuilder sb = new StringBuilder();
        bool bo = true;
        byte by = 200;
        sbyte sby = -5;
        short sh = -300;
        ushort ush = 60000;
        uint ui = 4000000000u;
        ulong ul = 18000000000000000000ul;
        long l = -9000000000000000000L;
        float fl = 1.5f;
        double db = 2.25;
        char ch = 'Z';
        object ob = "obj";
        string s = "str";
        StringBuilder other = new StringBuilder("other");
        sb.Append(bo).Append('|');
        sb.Append(by).Append('|');
        sb.Append(sby).Append('|');
        sb.Append(sh).Append('|');
        sb.Append(ush).Append('|');
        sb.Append(ui).Append('|');
        sb.Append(ul).Append('|');
        sb.Append(l).Append('|');
        sb.Append(fl).Append('|');
        sb.Append(db).Append('|');
        sb.Append(ch).Append('|');
        sb.Append(ob).Append('|');
        sb.Append(s).Append('|');
        sb.Append(other);
        P("append.values", sb.ToString());
    }

    static void AppendCharArray()
    {
        StringBuilder sb = new StringBuilder();
        char[] cs = { 'a', 'b', 'c' };
        sb.Append(cs);
        P("append.chararray", sb.ToString());
    }

    static void AppendDecimal()
    {
        StringBuilder sb = new StringBuilder();
        decimal dec = 1.2345m;
        sb.Append(dec);
        P("append.decimal", sb.ToString());
    }

    static void AppendRanges()
    {
        StringBuilder sb = new StringBuilder();
        string s = "abcdef";
        char[] cs = { 'a', 'b', 'c', 'd' };
        char ch = 'x';
        sb.Append(s, 1, 3).Append('|');
        sb.Append(cs, 1, 2).Append('|');
        sb.Append(ch, 3);
        P("append.ranges", sb.ToString());
    }

    static void InsertValues()
    {
        StringBuilder sb = new StringBuilder("[]");
        bool bo = true;
        long l = -9000000000000000000L;
        double db = 2.25;
        char ch = 'Z';
        string s = "str";
        sb.Insert(1, bo).Insert(1, '|');
        sb.Insert(1, l).Insert(1, '|');
        sb.Insert(1, db).Insert(1, '|');
        sb.Insert(1, ch).Insert(1, '|');
        sb.Insert(1, s);
        P("insert.values", sb.ToString());
    }

    static void InsertNarrow()
    {
        StringBuilder sb = new StringBuilder("[]");
        byte by = 200;
        sbyte sby = -5;
        short sh = -300;
        ushort ush = 60000;
        uint ui = 4000000000u;
        ulong ul = 18000000000000000000ul;
        sb.Insert(1, by).Insert(1, '|');
        sb.Insert(1, sby).Insert(1, '|');
        sb.Insert(1, sh).Insert(1, '|');
        sb.Insert(1, ush).Insert(1, '|');
        sb.Insert(1, ui).Insert(1, '|');
        sb.Insert(1, ul);
        P("insert.narrow", sb.ToString());
    }

    static void InsertFloat()
    {
        StringBuilder sb = new StringBuilder("[]");
        float fl = 1.5f;
        sb.Insert(1, fl);
        P("insert.float", sb.ToString());
    }

    static void InsertCharArray()
    {
        StringBuilder sb = new StringBuilder("[]");
        char[] cs = { 'a', 'b', 'c' };
        sb.Insert(1, cs);
        P("insert.chararray", sb.ToString());
    }

    static void InsertDecimal()
    {
        StringBuilder sb = new StringBuilder("[]");
        decimal dec = 1.2345m;
        sb.Insert(1, dec);
        P("insert.decimal", sb.ToString());
    }

    static void InsertObject()
    {
        StringBuilder sb = new StringBuilder("[]");
        object ob = "obj";
        sb.Insert(1, ob);
        P("insert.object", sb.ToString());
    }

    static void InsertRanges()
    {
        StringBuilder sb = new StringBuilder("[]");
        string s = "abcdef";
        char[] cs = { 'a', 'b', 'c', 'd' };
        sb.Insert(1, s, 2);
        sb.Insert(1, cs, 1, 2);
        P("insert.ranges", sb.ToString());
    }

    static void AppendLineFamily()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("abc");
        P("appendline", sb.ToString().Replace("\r\n", "<crlf>"));
    }

    static void AppendFormatFamily()
    {
        StringBuilder sb = new StringBuilder();
        object o1 = "a";
        object o2 = "b";
        object o3 = "c";
        object[] oa = { o1, o2, o3 };
        sb.AppendFormat("{0}", o1).Append('|');
        sb.AppendFormat("{0}-{1}", o1, o2).Append('|');
        sb.AppendFormat("{0}-{1}-{2}", o1, o2, o3).Append('|');
        sb.AppendFormat("{0}-{1}-{2}", oa).Append('|');
        sb.AppendFormat(CultureInfo.InvariantCulture, "{0}", o1).Append('|');
        sb.AppendFormat(CultureInfo.InvariantCulture, "{0}-{1}", o1, o2).Append('|');
        sb.AppendFormat(CultureInfo.InvariantCulture, "{0}-{1}-{2}", o1, o2, o3).Append('|');
        sb.AppendFormat(CultureInfo.InvariantCulture, "{0}-{1}-{2}", oa);
        P("appendformat", sb.ToString());
    }

    static void ReplaceFamily()
    {
        StringBuilder sb = new StringBuilder("abcabcabc");
        sb.Replace("ab", "X");
        sb.Replace('c', 'Y');
        sb.Replace("X", "Z", 0, 2);
        sb.Replace('Y', 'W', 0, 3);
        P("replace", sb.ToString());
    }

    static void CopyEnsureEquals()
    {
        StringBuilder sb = new StringBuilder("abcdef");
        char[] dst = new char[4];
        sb.CopyTo(1, dst, 0, 3);
        P("copyto", new string(dst));
        P("ensure", sb.EnsureCapacity(64));
        StringBuilder same = sb;
        StringBuilder other = new StringBuilder("abcdef");
        P("equals.sb", sb.Equals(same));
        P("equals.obj", sb.Equals((object)same));
        P("equals.other", sb.Equals(other));
        P("equals.obj.other", sb.Equals((object)other));
    }

    static void ToStringRange()
    {
        StringBuilder sb = new StringBuilder("abcdef");
        P("tostring.range", sb.ToString(1, 3));
    }

    static void Main()
    {
        Ctors();
        AppendValues();
        AppendCharArray();
        AppendDecimal();
        AppendRanges();
        InsertValues();
        InsertNarrow();
        InsertFloat();
        InsertCharArray();
        InsertDecimal();
        InsertObject();
        InsertRanges();
        AppendLineFamily();
        AppendFormatFamily();
        ReplaceFamily();
        CopyEnsureEquals();
        ToStringRange();
    }
}
