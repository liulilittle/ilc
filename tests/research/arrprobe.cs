// arrprobe.cs — array instruction family (newarr/ldelem.*/stelem.*/ldlen/ldelema) net48 evidence.
// Covers: negative size, out-of-bounds read/write, type mismatch (stelem.ref covariance violation), null arrays, ref elements.
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
        // ---- newarr: size semantics ----
        Pi("newarr: new int[5].Length", new int[5].Length);
        Pi("newarr: new int[0].Length", new int[0].Length);
        {
            // C# rejects the constant new int[-1] (CS0248); a negative size must go through a variable -> runtime check
            int negative = -1;
            Try(() => { var a = new int[negative]; P("len=" + a.Length); }, "newarr: new int[-1]");
            Try(() => { var a = new int[int.MaxValue]; P("len=" + a.Length); }, "newarr: new int[int.MaxValue]");
        }

        // ---- ldlen ----
        Pi("ldlen: Arr3.Length", Arr3.Length);
        Try(() => { int[] a = null; int n = a.Length; P("len=" + n); }, "ldlen: null array");

        // ---- ldelem: boundaries ----
        Pi("ldelem: Arr3[1]", Arr3[1]);
        Try(() => { int v = Arr3[3]; P("v=" + v); }, "ldelem: out-of-bounds [3] (len=3)");
        Try(() => { int v = Arr3[-1]; P("v=" + v); }, "ldelem: negative index [-1]");

        // ---- stelem: boundaries ----
        Try(() => { Arr3[3] = 9; }, "stelem: out-of-bounds write [3]");
        Try(() => { Arr3[-1] = 9; }, "stelem: negative index write [-1]");
        {
            var a = new int[2];
            a[1] = 42;
            Pi("stelem: a[1]=42", a[1]);
        }

        // ---- stelem.ref: array covariance + type check (ArrayTypeMismatchException) ----
        {
            string[] sa = new string[2];
            object[] oa = sa;          // string[] -> object[] covariance OK
            oa[0] = "x";               // OK
            Pi("stelem.ref: object[] stores string", (oa[0] as string).Length);
            Try(() => oa[1] = 5, "stelem.ref: object[] stores int (string[] underlying)"); // ArrayTypeMismatchException
            object[] oa2 = new object[2];
            oa2[0] = 5;                // OK: object[] can hold any reference (int gets boxed)
            Pi("stelem.ref: object[] stores boxed int", ((int)oa2[0]));
        }

        // ---- ldelema (ref element) ----
        {
            int[] a = { 1, 2, 3 };
            ref int r = ref a[1];      // ldelema
            r = 42;
            Pi("ldelema: ref a[1] = 42", a[1]);
            Try(() => { ref int r2 = ref a[5]; r2 = 0; }, "ldelema: out-of-bounds [5]");
        }

        // ---- value-type arrays: elements are copied by value ----
        {
            var d = new DateTime[2];
            d[0] = new DateTime(2020, 1, 1);
            var v = d[0];
            v = v.AddDays(1);
            Pi("valuetype array: d[0].Year", d[0].Year);       // 2020 (copying doesn't affect the array)
            Pi("valuetype array: v.Year", v.Year);             // 2020 (copied value)
            ref DateTime rd = ref d[1];                        // ldelema on a value type
            rd = new DateTime(1999, 12, 31);
            Pi("valuetype ldelema: d[1].Year", d[1].Year);    // 1999
        }

        // ---- multi-dimensional arrays (newobj + stelem.rank, not newarr) ----
        {
            int[,] m = new int[2, 3];
            m[1, 2] = 7;
            Pi("md-array: m[1,2]", m[1, 2]);
            Pi("md-array: m.Rank", m.Rank);
            Pi("md-array: m.GetLength(0)", m.GetLength(0));
        }
    }
}
