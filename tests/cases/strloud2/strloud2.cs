//------------------------------------------------------------------------------
// strloud2.cs -- residual System.String overload sweep (net48 oracle).
//
// Every statically-analyzable overload family that the M5 resolver used to
// reject LOUD: the ctor family, the position/range search forms, the
// comparison forms, Format/Concat/Join arities, and the case/normalize
// surface. Runtime variables keep the calls out of the constant folder so
// the transpiler sees the real member references. Each family lives in its
// own method so a transpile gap in one family never hides the others.
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        char[] cs = { 'H', 'e', 'l', 'l', 'o' };
        char ch = 'l';
        int n = 3;
        P("ctor.char.count", new string(ch, n));
        P("ctor.char.count.zero", new string(ch, 0).Length);
        P("ctor.chars", new string(cs));
        P("ctor.chars.range", new string(cs, 1, 3));
    }

    static void IndexOfs()
    {
        string a = "Hello, World";
        char ch = 'l';
        P("indexof.c", a.IndexOf(ch));
        P("indexof.c.from", a.IndexOf(ch, 4));
        P("indexof.c.range", a.IndexOf(ch, 4, 3));
        P("indexof.s", a.IndexOf("World"));
        P("indexof.s.from", a.IndexOf("o", 5));
        P("indexof.s.range", a.IndexOf("o", 5, 3));
        P("indexof.s.sc", a.IndexOf("world", StringComparison.OrdinalIgnoreCase));
        P("indexof.s.from.sc", a.IndexOf("world", 0, StringComparison.OrdinalIgnoreCase));
        P("indexof.s.range.sc", a.IndexOf("world", 0, a.Length, StringComparison.OrdinalIgnoreCase));
    }

    static void IndexOfAnys()
    {
        string a = "Hello, World";
        char[] any = { 'W', 'x' };
        P("indexofany", a.IndexOfAny(any));
        P("indexofany.from", a.IndexOfAny(any, 8));
        P("indexofany.range", a.IndexOfAny(any, 0, a.Length));
        P("lastindexofany", a.LastIndexOfAny(any));
        P("lastindexofany.from", a.LastIndexOfAny(any, a.Length - 1));
        P("lastindexofany.range", a.LastIndexOfAny(any, a.Length - 1, a.Length));
    }

    static void LastIndexOfs()
    {
        string a = "Hello, World";
        char ch = 'l';
        P("lastindexof.c", a.LastIndexOf(ch));
        P("lastindexof.c.from", a.LastIndexOf(ch, 8));
        P("lastindexof.c.range", a.LastIndexOf(ch, 8, 4));
        P("lastindexof.s", a.LastIndexOf("o"));
        P("lastindexof.s.from", a.LastIndexOf("o", 8));
        P("lastindexof.s.range", a.LastIndexOf("o", 8, 4));
        P("lastindexof.s.sc", a.LastIndexOf("WORLD", StringComparison.OrdinalIgnoreCase));
        P("lastindexof.s.from.sc", a.LastIndexOf("WORLD", 8, StringComparison.OrdinalIgnoreCase));
        P("lastindexof.s.range.sc", a.LastIndexOf("WORLD", 8, 5, StringComparison.OrdinalIgnoreCase));
    }

    static void ReplaceCasePadContains()
    {
        string a = "Hello, World";
        P("replace.cc", a.Replace('l', 'L'));
        P("replace.ss", a.Replace("World", "There"));
        P("toupperinv", a.ToUpperInvariant());
        P("tolowerinv", a.ToLowerInvariant());
        P("padright", a.PadRight(a.Length + 2));
        P("padright.c", a.PadRight(a.Length + 2, '.'));
        P("contains.s", a.Contains("World"));
    }

    static void Normalize()
    {
        string composed = "e\u0301";
        P("isnormalized", composed.IsNormalized());
        P("isnormalized.form", composed.IsNormalized(NormalizationForm.FormC));
        string normalized = composed.Normalize();
        P("normalize.len", normalized.Length);
        P("normalize.form.len", composed.Normalize(NormalizationForm.FormC).Length);
    }

    static void CompareOrdinalRange()
    {
        string a = "Hello, World";
        string b = "hello, world";
        P("compareordinal.range", string.CompareOrdinal(a, 0, b, 0, 5));
    }

    static void CompareRange()
    {
        string a = "abc";
        string b = "abd";
        P("compare.range", string.Compare(a, 0, b, 0, 5));
    }

    static void CompareRangeBool()
    {
        string a = "ABC";
        string b = "abc";
        P("compare.range.bool", string.Compare(a, 0, b, 0, 5, true));
    }

    static void CompareRangeSc()
    {
        string a = "ABC";
        string b = "abc";
        P("compare.range.sc", string.Compare(a, 0, b, 0, 5, StringComparison.OrdinalIgnoreCase));
    }

    static void Compares()
    {
        string a = "abc";
        string b = "abd";
        string upper = "ABC";
        string lower = "abc";
        P("compareordinal", string.CompareOrdinal(a, b));
        P("compare", string.Compare(a, b));
        P("compare.bool", string.Compare(upper, lower, true));
        P("compare.sc", string.Compare(upper, lower, StringComparison.OrdinalIgnoreCase));
    }

    static void EqualsOps()
    {
        string a = "Hello, World";
        string b = "hello, world";
        P("equals.obj", a.Equals((object)b));
        P("equals.s", a.Equals(b));
        P("equals.sc", a.Equals(b, StringComparison.OrdinalIgnoreCase));
        P("equals.static", string.Equals(a, b));
        P("equals.static.sc", string.Equals(a, b, StringComparison.OrdinalIgnoreCase));
        P("op.eq", a == b);
        P("op.ne", a != b);
    }

    static void Formats()
    {
        string a = "Hello, World";
        string b = "hello, world";
        int n = 3;
        object[] args = { a, b, n };
        P("format.0", string.Format("x"));
        P("format.1", string.Format("{0}", a));
        P("format.2", string.Format("{0}-{1}", a, b));
        P("format.3", string.Format("{0}-{1}-{2}", a, b, n));
        P("format.array", string.Format("{0}-{1}-{2}", args));
        P("format.prov.1", string.Format(CultureInfo.InvariantCulture, "{0}", a));
        P("format.prov.2", string.Format(CultureInfo.InvariantCulture, "{0}-{1}", a, b));
        P("format.prov.3", string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}", a, b, n));
        P("format.prov.array", string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}", args));
    }

    static void Concats()
    {
        string a = "Hello, World";
        string b = "hello, world";
        int n = 3;
        object o1 = a;
        object o2 = b;
        object o3 = n;
        object[] oa = { o1, o2, o3 };
        string[] sa = { a, b };
        P("concat.objarr", string.Concat(oa));
        P("concat.s2", string.Concat(a, b));
        P("concat.s3", string.Concat(a, b, a));
        P("concat.s4", string.Concat(a, b, a, b));
        P("concat.sarr", string.Concat(sa));
    }

    static void ConcatObj()
    {
        string a = "Hello, World";
        object o1 = a;
        P("concat.obj", string.Concat(o1));
    }

    static void ConcatIEnum()
    {
        string a = "Hello, World";
        string b = "hello, world";
        string[] sa = { a, b };
        P("concat.ienum", string.Concat((IEnumerable<string>)sa));
    }

    static void ConcatIEnumGeneric()
    {
        string a = "Hello, World";
        string b = "hello, world";
        object[] oa = { a, b };
        P("concat.ienum.generic", string.Concat<object>(oa));
    }

    static void Joins()
    {
        string a = "Hello, World";
        string b = "hello, world";
        int n = 3;
        object o1 = a;
        object o2 = b;
        object o3 = n;
        object[] oa = { o1, o2, o3 };
        string[] sa = { a, b };
        P("join.sarr", string.Join(",", sa));
        P("join.objarr", string.Join(",", oa));
        P("join.sarr.range", string.Join(",", sa, 0, 2));
        P("join.ienum", string.Join(",", (IEnumerable<string>)sa));
        P("join.ienum.generic", string.Join<object>(",", oa));
    }

    static void Main()
    {
        Ctors();
        IndexOfs();
        IndexOfAnys();
        LastIndexOfs();
        ReplaceCasePadContains();
        Normalize();
        CompareOrdinalRange();
        CompareRange();
        CompareRangeBool();
        CompareRangeSc();
        Compares();
        EqualsOps();
        Formats();
        Concats();
        ConcatObj();
        ConcatIEnum();
        ConcatIEnumGeneric();
        Joins();
    }
}


