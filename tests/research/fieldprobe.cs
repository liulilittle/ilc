// fieldprobe.cs — 字段族（ldfld/stfld/ldsfld/stsfld/ldflda）net48 实证。
// 关键：null 接收者 NRE（ldfld/stfld）、实例访问静态字段不发 null 检查（ldsfld/stsfld）、
// cctor 时机、字段默认值、ldflda 取址、volatile 字段（内存序语义单线程不可观测）。
using System;

class FieldProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pi(string label, int v) => P(label + " = " + v);
    static void Try(Action a, string label)
    {
        try { a(); P(label + " -> no exception"); }
        catch (Exception e) { P(label + " -> " + e.GetType().Name); }
    }

    class Box
    {
        public int X;
        public string S;
        public static int StaticCount;
        public static readonly string StaticCtorTrace;

        static Box()
        {
            StaticCtorTrace = "cctor-ran";
            StaticCount = 7;
        }

        public static int StaticX = 99;
    }

    struct Point
    {
        public int X;
        public int Y;
    }

    volatile int _vol;      // C# volatile → IL volatile. 前缀（ldfld.volatile/stfld.volatile）
    static volatile int _svol;

    static void Main()
    {
        // ---- cctor：静态字段首访触发类型初始化（ldsfld/stsfld 前）----
        P("cctor: 首访前 trace=" + (Box.StaticCtorTrace ?? "<null>"));
        Pi("ldsfld: Box.StaticCount", Box.StaticCount);
        Box.StaticCount = 11;
        Pi("stsfld: Box.StaticCount=11", Box.StaticCount);

        // ---- 实例访问静态字段：C# 7.3 直接禁止（CS0176 实例限定静态成员）。
        //      IL 的 ldsfld/stsfld 操作数只有字段 token，无接收者 → 不存在 null 检查：
        //      语义上"null 实例访问静态字段"不抛 NRE（ECMA II.III.4.28/4.30）。----
        // var b0 = (Box)null; b0.StaticCount = 5;  // CS0176 编译期拒绝

        // ---- 实例字段：默认值 / 读写 ----
        var bb = new Box();
        Pi("ldfld: 默认 int 字段", bb.X);
        P("ldfld: 默认 string 字段 = " + (bb.S == null ? "<null>" : bb.S));
        bb.X = 123;
        Pi("ldfld: 写后读", bb.X);

        // ---- ldfld/stfld null 接收者：NullReferenceException ----
        Try(() => { Box n = null; int v = n.X; P("v=" + v); }, "ldfld: null 接收者");
        Try(() => { Box n = null; n.X = 1; }, "stfld: null 接收者");

        // ---- ldflda：字段取址（ref 写）----
        {
            var pt = new Point { X = 1, Y = 2 };
            ref int rx = ref pt.X;               // ldloca+ldflda（值类型局部）
            rx = 100;
            Pi("ldflda: ref 写 pt.X", pt.X);
        }

        // ---- volatile 字段：单线程读写往返（内存序语义见文档；C# volatile 编译为 volatile. 前缀）----
        {
            var fp = new FieldProbe();
            fp._vol = 7;
            Pi("volatile 字段往返", fp._vol);
            _svol = 13;
            Pi("volatile 静态字段往返", _svol);
        }

        // ---- 实例字段继承：基类字段经派生实例读 ----
        {
            var d = new Derived();
            d.BaseField = 5;
            Pi("ldfld: 派生读基类字段", ((BaseClass)d).BaseField);
        }
    }

    class BaseClass { public int BaseField; }
    class Derived : BaseClass { }
}
