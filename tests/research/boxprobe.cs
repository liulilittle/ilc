// boxprobe.cs — 装箱族（box/unbox.any/isinst/castclass）net48 实证。
// 关键：装箱快照拷贝、盒对象引用身份（无值类型驻留）、isinst/castclass 的 null 输入、
// 类型不匹配失败语义、unbox.any null → NRE。
using System;

class BoxProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pb(string label, bool v) => P(label + " = " + (v ? 1 : 0));
    static void Pi(string label, int v) => P(label + " = " + v);
    static void Try(Action a, string label)
    {
        try { a(); P(label + " -> no exception"); }
        catch (Exception e) { P(label + " -> " + e.GetType().Name); }
    }

    struct S
    {
        public int X;
        public override string ToString() => "S(" + X + ")";
    }

    class A { }
    class B : A { }

    static void Main()
    {
        // ---- box：值快照拷贝 ----
        {
            S s = new S { X = 1 };
            object o = s;          // box：拷贝
            s.X = 2;
            Pi("box: 装箱快照（改原值不影响盒）", ((S)o).X);   // 1
        }

        // ---- box 身份：同一值两次装箱 = 不同对象（无驻留）----
        {
            object a = 5;
            object b = 5;
            Pb("box 身份: a == b（引用比较）", a == b);        // false
            Pb("box 身份: a.Equals(b)（值比较）", a.Equals(b)); // true
            object c = a;
            Pb("box 身份: a == c（同引用）", a == c);          // true
        }

        // ---- box 大整型/其他类型 ----
        {
            object l = 5L;
            Pb("isinst: 盒 long 是 long", l is long);
            Pb("isinst: 盒 long 是 int", l is int);
            Pl("unbox.any: (long)盒 long", (long)l);
            object d = 2.5;
            P("unbox.any: (double)盒 double = " + (double)d);
        }

        // ---- unbox.any：null → NullReferenceException ----
        Try(() => { int v = (int)(object)null; P("v=" + v); }, "unbox.any: (int)null");

        // ---- unbox.any：类型不匹配 → InvalidCastException ----
        Try(() => { string s = (string)(object)5; P("s=" + s); }, "unbox.any: (string)盒 int");
        Try(() => { long v = (long)(object)5; P("v=" + v); }, "unbox.any: (long)盒 int");

        // ---- isinst：类型匹配返回原引用 / 不匹配返回 null（不抛）----
        {
            object o = 5;
            Pb("isinst: 盒 int is int", o is int);
            Pb("isinst: 盒 int is string", o is string);
            object nullObj = null;
            Pb("isinst: null is string", nullObj is string);   // false
            A a = new B();
            Pb("isinst: B 实例 is A（基类）", a is A);
            Pb("isinst: B 实例 is B", a is B);
            Pb("isinst: A 实例 is B", new A() is B);
        }

        // ---- castclass：null 输入 → null（不抛）----
        {
            string s = (string)(object)null;
            P("castclass: (string)null = " + (s == null ? "<null>" : s));
        }

        // ---- castclass：不匹配 → InvalidCastException ----
        Try(() => { string s = (string)(object)5; }, "castclass: (string)盒 int");

        // ---- castclass：isinst 判定（先 as 后强制）----
        {
            object o = "hello";
            string s = (string)o;
            Pi("castclass: (string)object[字符串]", s.Length);
        }

        // ---- 盒 double 的 isinst 精度（位模式）----
        {
            object d1 = 0.1;
            object d2 = 0.1;
            Pb("isinst: 两次盒 0.1 引用相等", d1 == d2);
            Pb("isinst: 两次盒 0.1 Equals", d1.Equals(d2));
        }
    }

    static void Pl(string label, long v) => P(label + " = " + v);
}
