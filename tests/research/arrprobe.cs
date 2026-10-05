// arrprobe.cs — 数组指令族（newarr/ldelem.*/stelem.*/ldlen/ldelema）net48 实证。
// 边界：负尺寸、越界读写、类型不匹配（stelem.ref 协变违例）、null 数组、ref 元素。
using System;

class ArrProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pi(string label, int v) => P(label + " = " + v);
    static void Try(Action a, string label)
    {
        try { a(); P(label + " -> no exception"); }
        catch (Exception e) { P(label + " -> " + e.GetType().Name); }
    }

    static int[] Arr3 = new[] { 1, 2, 3 };

    static void Main()
    {
        // ---- newarr：尺寸语义 ----
        Pi("newarr: new int[5].Length", new int[5].Length);
        Pi("newarr: new int[0].Length", new int[0].Length);
        {
            // C# 拒绝 new int[-1] 常量（CS0248），负尺寸必须走变量 → 运行时检查
            int negative = -1;
            Try(() => { var a = new int[negative]; P("len=" + a.Length); }, "newarr: new int[-1]");
            Try(() => { var a = new int[int.MaxValue]; P("len=" + a.Length); }, "newarr: new int[int.MaxValue]");
        }

        // ---- ldlen ----
        Pi("ldlen: Arr3.Length", Arr3.Length);
        Try(() => { int[] a = null; int n = a.Length; P("len=" + n); }, "ldlen: null 数组");

        // ---- ldelem：边界 ----
        Pi("ldelem: Arr3[1]", Arr3[1]);
        Try(() => { int v = Arr3[3]; P("v=" + v); }, "ldelem: 越界 [3] (len=3)");
        Try(() => { int v = Arr3[-1]; P("v=" + v); }, "ldelem: 负索引 [-1]");

        // ---- stelem：边界 ----
        Try(() => { Arr3[3] = 9; }, "stelem: 越界写 [3]");
        Try(() => { Arr3[-1] = 9; }, "stelem: 负索引写 [-1]");
        {
            var a = new int[2];
            a[1] = 42;
            Pi("stelem: a[1]=42", a[1]);
        }

        // ---- stelem.ref：数组协变 + 类型检查（ArrayTypeMismatchException）----
        {
            string[] sa = new string[2];
            object[] oa = sa;          // string[] → object[] 协变 OK
            oa[0] = "x";               // OK
            Pi("stelem.ref: object[] 存 string", (oa[0] as string).Length);
            Try(() => oa[1] = 5, "stelem.ref: object[] 存 int（string[] 底）"); // ArrayTypeMismatchException
            object[] oa2 = new object[2];
            oa2[0] = 5;                // OK：object[] 可存任意引用（int 被装箱）
            Pi("stelem.ref: object[] 存 int 装箱", ((int)oa2[0]));
        }

        // ---- ldelema（ref 元素）----
        {
            int[] a = { 1, 2, 3 };
            ref int r = ref a[1];      // ldelema
            r = 42;
            Pi("ldelema: ref a[1] = 42", a[1]);
            Try(() => { ref int r2 = ref a[5]; r2 = 0; }, "ldelema: 越界 [5]");
        }

        // ---- 值类型数组：元素为值拷贝 ----
        {
            var d = new DateTime[2];
            d[0] = new DateTime(2020, 1, 1);
            var v = d[0];
            v = v.AddDays(1);
            Pi("valuetype 数组: d[0].Year", d[0].Year);       // 2020（拷贝修改不影响数组）
            Pi("valuetype 数组: v.Year", v.Year);             // 2020（拷贝）
            ref DateTime rd = ref d[1];                        // ldelema 值类型
            rd = new DateTime(1999, 12, 31);
            Pi("valuetype ldelema: d[1].Year", d[1].Year);    // 1999
        }

        // ---- 多维数组（newobj + stelem.rank，非 newarr）----
        {
            int[,] m = new int[2, 3];
            m[1, 2] = 7;
            Pi("md-array: m[1,2]", m[1, 2]);
            Pi("md-array: m.Rank", m.Rank);
            Pi("md-array: m.GetLength(0)", m.GetLength(0));
        }
    }
}
