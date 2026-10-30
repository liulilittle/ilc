//------------------------------------------------------------------------------
// probe-listrange -- the List<T> RANGE / PREDICATE operator surface for every
// element family, pinned against the net48 oracle byte-for-byte.
//
// Before impl-listrange the transpiler routed every one of these members to
// LOUD for the listobj element families (and for List<int> the range overloads
// of IndexOf/LastIndexOf/BinarySearch had no runtime entry), so a program using
// them either refused to transpile or silently called a not-transpiled stub.
//
// Covered surface (per element family):
//   AddRange / InsertRange / RemoveRange / GetRange (result Count/index/foreach)
//   RemoveAll / LastIndexOf(1,2,3-arg) / Find / FindLast / FindIndex /
//   FindLastIndex / Exists / TrueForAll / ForEach / FindAll / Reverse() /
//   Reverse(index,count) / BinarySearch(1,2,4-arg) / Sort(Comparison<T>) /
//   Sort(index,count,comparer) / CopyTo / IndexOf(1,2,3-arg)
//
// Families: int32 (listi4 domain), reference (string), int64, float, double,
// the narrow kinds byte/char/bool/uint, decimal, DateTime, TimeSpan, Guid,
// DateTimeOffset and a user value struct.
//
// Edge coverage: empty / single-element / remove-everything / out-of-range /
// negative count / null collection / null comparer / short destination array.
//
// Equality specials: long.MaxValue, double NaN and -0.0, decimal 1.0m vs 1.00m
// (value-equal, scale-preserving), DateTime same ticks but different Kind
// (net48 DateTime.Equals compares InternalTicks only, so the Kind must NOT
// decide the hit).
//
// Predicate ABI width: float/double arguments must not be truncated (the
// delegates receive the widened carrier and the exact value), and the BCL
// value structs cross BY VALUE (ForEach fingerprints read full values).
//
// KNOWN LOUD SHAPES (deliberately not called -- a LOUD rejects the method at
// transpile time instead of silently mis-mapping):
//   * List<int>.IndexOf/LastIndexOf/BinarySearch range or comparer overloads
//     (the listi4 domain has the 1-arg carriers only);
//   * List<int>.AddRange/InsertRange over a non-int32[] collection (listi4
//     requires the int32 array source);
//   * List<user struct>.BinarySearch and .IndexOf(value,index[,count])
//     (the template carriers exist but the transpiler arms are not wired);
//   * List<DateTimeOffset>.Sort() with no comparer (no IComparable vtable).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cases
{
    public static class ProbeListRange
    {
        public struct Pt
        {
            public int X;
            public int Y;
            public Pt(int x, int y) { X = x; Y = y; }
        }

        private static void W(string id, string v) { Console.WriteLine("[" + id + "]=" + v); }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string L(long v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string U(uint v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string B(bool v) { return v ? "true" : "false"; }
        private static string S(string v) { return v == null ? "<null>" : v; }
        private static string M(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string DB(double v) { return L(BitConverter.DoubleToInt64Bits(v)); }
        private static string FB(float v) { return U(BitConverter.ToUInt32(BitConverter.GetBytes(v), 0)); }
        private static string DT(DateTime v) { return L(v.Ticks) + "/" + I((int)v.Kind); }
        private static string TS(TimeSpan v) { return L(v.Ticks); }
        private static string GD(Guid v) { return v.ToString(); }
        private static string DTO(DateTimeOffset v) { return L(v.Ticks) + "/" + L(v.Offset.Ticks); }
        private static string P(Pt v) { return I(v.X) + "," + I(v.Y); }

        private static void T(string id, Action a)
        {
            try { a(); W(id, "ok"); }
            catch (Exception ex) { W(id, "EX:" + ex.GetType().Name); }
        }

        //--------------------------------------------------------------------------
        // List<int> (the listi4 AOT domain; range IndexOf/LastIndexOf/BinarySearch
        // comparer overloads stay LOUD there -- see the header note).
        //--------------------------------------------------------------------------
        private static string JI(List<int> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + I(l[i]); }
            return "[" + s + "]";
        }

        private static void IntOps()
        {
            List<int> l = new List<int>(new int[] { 5, 3, 9, 3, 7 });
            W("i4.base", JI(l));
            List<int> ar = new List<int>(new int[] { 7 });
            ar.AddRange(new int[] { 6, 4 });
            W("i4.addrange", JI(ar));
            List<int> ins = new List<int>(new int[] { 1, 2, 3 });
            ins.InsertRange(1, new int[] { 10, 11 });
            W("i4.insertrange", JI(ins));
            List<int> rr = new List<int>(new int[] { 1, 2, 3, 4 });
            rr.RemoveRange(1, 2);
            W("i4.removerange", JI(rr));
            List<int> g = l.GetRange(1, 3);
            int gc = 0;
            foreach (int x in g) { gc = gc + 1; }
            W("i4.getrange", I(g.Count) + "/" + I(g[0]) + "/" + I(g[2]) + "/" + I(gc));
            List<int> ra = new List<int>(new int[] { 1, 2, 3, 2, 4 });
            W("i4.removeall", I(ra.RemoveAll(x => x == 2)) + "/" + JI(ra));
            W("i4.lastindexof", I(l.LastIndexOf(3)) + "/" + I(l.LastIndexOf(42)));
            W("i4.find", I(l.Find(x => x > 8)) + "/" + I(l.Find(x => x > 99)));
            W("i4.findlast", I(l.FindLast(x => x < 8)) + "/" + I(l.FindLast(x => x < 0)));
            W("i4.findindex", I(l.FindIndex(x => x == 9)) + "/" + I(l.FindIndex(x => x == 42)));
            W("i4.findlastindex", I(l.FindLastIndex(x => x == 3)) + "/" + I(l.FindLastIndex(x => x == 42)));
            W("i4.exists", B(l.Exists(x => x == 9)) + "/" + B(l.Exists(x => x == 42)));
            W("i4.trueforall", B(l.TrueForAll(x => x > 0)) + "/" + B(l.TrueForAll(x => x > 3)));
            int fc = 0; int fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x);
            W("i4.foreach", I(fc) + "/" + I(fp));
            W("i4.findall", JI(l.FindAll(x => x > 3)));
            List<int> rv = new List<int>(new int[] { 1, 2, 3, 4 });
            rv.Reverse();
            List<int> rvr = new List<int>(new int[] { 1, 2, 3, 4 });
            rvr.Reverse(1, 2);
            W("i4.reverse", JI(rv) + "/" + JI(rvr));
            List<int> bs = new List<int>(new int[] { -2147483648, -1, 5, 9, 2147483647 });
            W("i4.bsearch", I(bs.BinarySearch(5)) + "/" + I(bs.BinarySearch(6)) + "/" + I(bs.BinarySearch(-2147483648)));
            List<int> so = new List<int>(new int[] { 5, 3, 9, 1 });
            so.Sort((x, y) => x < y ? -1 : (x > y ? 1 : 0));
            List<int> sr = new List<int>(new int[] { 5, 4, 1, 3 });
            sr.Sort(1, 2, Comparer<int>.Default);
            W("i4.sort", JI(so) + "/" + JI(sr));
            int[] dst = new int[7];
            bs.CopyTo(dst, 1);
            W("i4.copyto", I(dst[1]) + "/" + I(dst[5]));
            W("i4.indexof", I(l.IndexOf(3)) + "/" + I(l.IndexOf(42)));
        }

        //--------------------------------------------------------------------------
        // List<string> (reference elements, including nulls)
        //--------------------------------------------------------------------------
        private static string JS(List<string> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + S(l[i]); }
            return "[" + s + "]";
        }

        private static void StringOps()
        {
            List<string> l = new List<string>();
            l.AddRange(new string[] { "bb", "a", "dd", "a", "ccc" });
            W("str.base", JS(l) + "/" + I(l.Count));
            List<string> ar = new List<string>();
            ar.Add("ee");
            ar.AddRange(new string[] { "ff", "gg" });
            ar.AddRange(l);
            W("str.addrange", JS(ar));
            List<string> ins = new List<string>();
            ins.AddRange(new string[] { "aa", "bb", "cc" });
            ins.InsertRange(1, new string[] { "x", "y" });
            ins.InsertRange(0, l);
            W("str.insertrange", JS(ins));
            List<string> rr = new List<string>();
            rr.AddRange(new string[] { "a", "b", "c", "d" });
            rr.RemoveRange(1, 2);
            W("str.removerange", JS(rr));
            List<string> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (string x in g) { gc = gc + 1; }
            W("str.getrange", I(g.Count) + "/" + S(g[0]) + "/" + S(g[1]) + "/" + I(gc));
            List<string> ra = new List<string>();
            ra.AddRange(new string[] { "a", "bb", "a", null, "c" });
            W("str.removeall", I(ra.RemoveAll(x => x == "a")) + "/" + JS(ra));
            List<string> rn = new List<string>();
            rn.AddRange(new string[] { "a", null, "b", null });
            W("str.removeall_null", I(rn.RemoveAll(x => x == null)) + "/" + JS(rn));
            W("str.null_indexof", I(rn.IndexOf(null)) + "/" + I(rn.LastIndexOf(null)) + "/" + I(rn.FindLastIndex(x => x == null)));
            W("str.lastindexof", I(l.LastIndexOf("a")) + "/" + I(l.LastIndexOf("a", 2)) + "/" + I(l.LastIndexOf("a", 3, 2)));
            W("str.find", S(l.Find(x => x.Length > 1)) + "/" + S(l.Find(x => x.Length > 9)));
            W("str.findlast", S(l.FindLast(x => x.Length == 1)) + "/" + S(l.FindLast(x => x.Length > 9)));
            W("str.findindex", I(l.FindIndex(x => x.Length > 2)) + "/" + I(l.FindIndex(x => x.Length > 9)));
            W("str.findlastindex", I(l.FindLastIndex(x => x.Length == 1)) + "/" + I(l.FindLastIndex(x => x.Length > 9)));
            W("str.exists", B(l.Exists(x => x == "dd")) + "/" + B(l.Exists(x => x == "zz")));
            W("str.trueforall", B(l.TrueForAll(x => x.Length > 0)) + "/" + B(l.TrueForAll(x => x.Length > 1)));
            int fc = 0; int fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x.Length);
            W("str.foreach", I(fc) + "/" + I(fp));
            W("str.findall", JS(l.FindAll(x => x.Length == 1)));
            List<string> rv = new List<string>();
            rv.AddRange(new string[] { "a", "b", "c" });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("str.reverse", JS(rv));
            List<string> bs = new List<string>();
            bs.AddRange(new string[] { "a", "c", "e", "g" });
            W("str.bsearch", I(bs.BinarySearch("e")) + "/" + I(bs.BinarySearch("d")) + "/" + I(bs.BinarySearch("f")) + "/" + I(bs.BinarySearch(1, 2, "e", null)));
            List<string> so = new List<string>();
            so.AddRange(new string[] { "a", "bb", "ccc" });
            so.Sort((x, y) => y.Length - x.Length);
            List<string> sr = new List<string>();
            sr.AddRange(new string[] { "a", "d", "c", "b" });
            sr.Sort(1, 2, null);
            W("str.sort", JS(so) + "/" + JS(sr));
            string[] dst = new string[7];
            bs.CopyTo(dst, 1);
            W("str.copyto", S(dst[1]) + "/" + S(dst[4]) + "/" + S(dst[0]));
            W("str.indexof", I(l.IndexOf("a")) + "/" + I(l.IndexOf("a", 1)) + "/" + I(l.IndexOf("a", 3, 2)));
        }

        //--------------------------------------------------------------------------
        // List<long> (int64 carriers) + long.MaxValue equality special
        //--------------------------------------------------------------------------
        private static string JLong(List<long> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + L(l[i]); }
            return "[" + s + "]";
        }

        private static void LongOps()
        {
            List<long> l = new List<long>();
            l.AddRange(new long[] { 5L, 9223372036854775807L, 9L, 9223372036854775807L, 1L });
            W("long.base", JLong(l) + "/" + I(l.Count));
            List<long> ar = new List<long>();
            ar.Add(7L);
            ar.AddRange(new long[] { 6L, 4L });
            ar.AddRange(l);
            W("long.addrange", JLong(ar));
            List<long> ins = new List<long>();
            ins.AddRange(new long[] { 1L, 2L, 3L });
            ins.InsertRange(1, new long[] { 10L, 11L });
            ins.InsertRange(0, l);
            W("long.insertrange", JLong(ins));
            List<long> rr = new List<long>();
            rr.AddRange(new long[] { 1L, 2L, 3L, 4L });
            rr.RemoveRange(1, 2);
            W("long.removerange", JLong(rr));
            List<long> g = l.GetRange(1, 3);
            int gc = 0;
            foreach (long x in g) { gc = gc + 1; }
            W("long.getrange", I(g.Count) + "/" + L(g[0]) + "/" + L(g[2]) + "/" + I(gc));
            List<long> ra = new List<long>();
            ra.AddRange(new long[] { 1L, 2L, 3L, 2L, 4L });
            W("long.removeall", I(ra.RemoveAll(x => x == 2L)) + "/" + JLong(ra));
            W("long.lastindexof", I(l.LastIndexOf(9223372036854775807L)) + "/" + I(l.LastIndexOf(9223372036854775807L, 3)) + "/" + I(l.LastIndexOf(9223372036854775807L, 3, 2)));
            W("long.find", L(l.Find(x => x > 8L)) + "/" + L(l.Find(x => x > 9223372036854775806L)));
            W("long.findlast", L(l.FindLast(x => x < 8L)) + "/" + L(l.FindLast(x => x < 0L)));
            W("long.findindex", I(l.FindIndex(x => x == 9L)) + "/" + I(l.FindIndex(x => x == 42L)));
            W("long.findlastindex", I(l.FindLastIndex(x => x == 9223372036854775807L)) + "/" + I(l.FindLastIndex(x => x == 42L)));
            W("long.exists", B(l.Exists(x => x == 9L)) + "/" + B(l.Exists(x => x == 42L)));
            W("long.trueforall", B(l.TrueForAll(x => x > 0L)) + "/" + B(l.TrueForAll(x => x > 3L)));
            int fc = 0; long fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x % 1000L);
            W("long.foreach", I(fc) + "/" + L(fp));
            W("long.findall", JLong(l.FindAll(x => x > 8L)));
            List<long> rv = new List<long>();
            rv.AddRange(new long[] { 1L, 2L, 3L, 4L });
            rv.Reverse();
            rv.Reverse(1, 2);
            W("long.reverse", JLong(rv));
            List<long> bs = new List<long>();
            bs.AddRange(new long[] { -9223372036854775808L, -1L, 5L, 9L, 9223372036854775807L });
            W("long.bsearch", I(bs.BinarySearch(5L)) + "/" + I(bs.BinarySearch(6L)) + "/" + I(bs.BinarySearch(5L, null)) + "/" + I(bs.BinarySearch(1, 3, 5L, null)));
            List<long> so = new List<long>();
            so.AddRange(new long[] { 5L, 3L, 9L, 1L });
            so.Sort((x, y) => x < y ? -1 : (x > y ? 1 : 0));
            List<long> sr = new List<long>();
            sr.AddRange(new long[] { 5L, 4L, 1L, 3L });
            sr.Sort(1, 2, null);
            W("long.sort", JLong(so) + "/" + JLong(sr));
            long[] dst = new long[7];
            bs.CopyTo(dst, 1);
            W("long.copyto", L(dst[1]) + "/" + L(dst[5]));
            W("long.indexof", I(l.IndexOf(9L)) + "/" + I(l.IndexOf(9223372036854775807L, 2)) + "/" + I(l.IndexOf(9223372036854775807L, 2, 1)) + "/" + I(l.IndexOf(1L, 0, 2)));
        }

        //--------------------------------------------------------------------------
        // List<double> (NaN / -0.0 equality specials) and List<float> (width)
        //--------------------------------------------------------------------------
        private static string JDb(List<double> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + DB(l[i]); }
            return "[" + s + "]";
        }

        private static void DoubleOps()
        {
            List<double> l = new List<double>();
            l.AddRange(new double[] { 2.5, 3.5, double.NaN, 9.5, 3.5 });
            W("dbl.base", JDb(l) + "/" + I(l.Count));
            List<double> z = new List<double>();
            z.AddRange(new double[] { 0.0, -0.0, 1.5 });
            W("dbl.zero_eq", I(z.IndexOf(-0.0)) + "/" + I(z.LastIndexOf(-0.0)) + "/" + I(z.RemoveAll(x => x == 0.0)) + "/" + I(z.Count));
            W("dbl.nan_eq", I(l.IndexOf(double.NaN)) + "/" + I(l.LastIndexOf(double.NaN)) + "/" + I(l.IndexOf(double.NaN, 0, 5)) + "/" + B(l.Exists(x => double.IsNaN(x))));
            List<double> ar = new List<double>();
            ar.Add(1.25);
            ar.AddRange(new double[] { 4.5, 6.75 });
            ar.AddRange(l);
            W("dbl.addrange", JDb(ar));
            List<double> ins = new List<double>();
            ins.AddRange(new double[] { 1.0, 2.0 });
            ins.InsertRange(1, new double[] { 3.0, 4.0 });
            W("dbl.insertrange", JDb(ins));
            List<double> rr = new List<double>();
            rr.AddRange(new double[] { 1.0, 2.0, 3.0, 4.0 });
            rr.RemoveRange(1, 2);
            W("dbl.removerange", JDb(rr));
            List<double> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (double x in g) { gc = gc + 1; }
            W("dbl.getrange", I(g.Count) + "/" + DB(g[0]) + "/" + DB(g[1]) + "/" + I(gc));
            List<double> ra = new List<double>();
            ra.AddRange(new double[] { 1.5, 2.5, 1.5 });
            W("dbl.removeall", I(ra.RemoveAll(x => x > 2.0)) + "/" + JDb(ra));
            W("dbl.lastindexof", I(l.LastIndexOf(3.5)) + "/" + I(l.LastIndexOf(3.5, 2)) + "/" + I(l.LastIndexOf(3.5, 3, 2)));
            W("dbl.find", DB(l.Find(x => x > 9.0)) + "/" + DB(l.Find(x => x > 99.0)));
            W("dbl.findlast", DB(l.FindLast(x => x < 9.0)) + "/" + DB(l.FindLast(x => x < 0.0)));
            W("dbl.findindex", I(l.FindIndex(x => x > 9.0)) + "/" + I(l.FindIndex(x => x > 99.0)));
            W("dbl.findlastindex", I(l.FindLastIndex(x => x < 3.0)) + "/" + I(l.FindLastIndex(x => x < 0.0)));
            W("dbl.exists", B(l.Exists(x => x == 3.5)) + "/" + B(l.Exists(x => x == 42.0)));
            W("dbl.trueforall", B(l.TrueForAll(x => x > 0.0)) + "/" + B(l.TrueForAll(x => x > 3.0)));
            int fc = 0; double fp = 0.0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x);
            W("dbl.foreach", I(fc) + "/" + DB(fp));
            W("dbl.findall", JDb(l.FindAll(x => x > 3.0)));
            List<double> rv = new List<double>();
            rv.AddRange(new double[] { 1.0, 2.0, 3.0 });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("dbl.reverse", JDb(rv));
            List<double> bs = new List<double>();
            bs.AddRange(new double[] { -1.0, 0.0, 1.5, 4.0 });
            W("dbl.bsearch", I(bs.BinarySearch(-0.0)) + "/" + I(bs.BinarySearch(2.0)) + "/" + I(bs.BinarySearch(1.5, null)) + "/" + I(bs.BinarySearch(1, 2, 0.0, null)));
            List<double> so = new List<double>();
            so.AddRange(new double[] { double.NaN, 1.5, -1.5, 0.0 });
            so.Sort((x, y) => x < y ? -1 : (x > y ? 1 : (double.IsNaN(x) ? (double.IsNaN(y) ? 0 : -1) : (double.IsNaN(y) ? 1 : 0))));
            List<double> sr = new List<double>();
            sr.AddRange(new double[] { 5.0, 4.0, 1.0, 3.0 });
            sr.Sort(1, 2, null);
            W("dbl.sort", JDb(so) + "/" + JDb(sr));
            double[] dst = new double[7];
            bs.CopyTo(dst, 1);
            W("dbl.copyto", DB(dst[1]) + "/" + DB(dst[4]));
            W("dbl.indexof", I(l.IndexOf(3.5)) + "/" + I(l.IndexOf(3.5, 3)) + "/" + I(l.IndexOf(9.5, 0, 3)));
        }

        private static string JF(List<float> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + FB(l[i]); }
            return "[" + s + "]";
        }

        private static void FloatOps()
        {
            List<float> l = new List<float>();
            l.AddRange(new float[] { 1.5f, 3.25f, 0.1f, 3.25f, 7.75f });
            W("flt.base", JF(l) + "/" + I(l.Count));
            W("flt.width", FB(l.Find(x => x == 0.1f)) + "/" + I(l.FindIndex(x => x == 0.1f)) + "/" + B(l.Exists(x => x > 1.4f)));
            List<float> ar = new List<float>();
            ar.Add(2.0f);
            ar.AddRange(new float[] { 4.0f, 6.0f });
            ar.AddRange(l);
            W("flt.addrange", JF(ar));
            List<float> ins = new List<float>();
            ins.AddRange(new float[] { 1.0f, 2.0f });
            ins.InsertRange(1, new float[] { 3.0f, 4.0f });
            W("flt.insertrange", JF(ins));
            List<float> rr = new List<float>();
            rr.AddRange(new float[] { 1.0f, 2.0f, 3.0f, 4.0f });
            rr.RemoveRange(1, 2);
            W("flt.removerange", JF(rr));
            List<float> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (float x in g) { gc = gc + 1; }
            W("flt.getrange", I(g.Count) + "/" + FB(g[0]) + "/" + FB(g[1]) + "/" + I(gc));
            List<float> ra = new List<float>();
            ra.AddRange(new float[] { 1.5f, 2.5f, 1.5f });
            W("flt.removeall", I(ra.RemoveAll(x => x > 2.0f)) + "/" + JF(ra));
            W("flt.lastindexof", I(l.LastIndexOf(3.25f)) + "/" + I(l.LastIndexOf(3.25f, 2)) + "/" + I(l.LastIndexOf(3.25f, 3, 2)));
            W("flt.find", FB(l.Find(x => x > 7.0f)) + "/" + FB(l.Find(x => x > 99.0f)));
            W("flt.findlast", FB(l.FindLast(x => x < 2.0f)) + "/" + FB(l.FindLast(x => x < 0.0f)));
            W("flt.findindex", I(l.FindIndex(x => x > 7.0f)) + "/" + I(l.FindIndex(x => x > 99.0f)));
            W("flt.findlastindex", I(l.FindLastIndex(x => x < 2.0f)) + "/" + I(l.FindLastIndex(x => x < 0.0f)));
            W("flt.exists", B(l.Exists(x => x == 3.25f)) + "/" + B(l.Exists(x => x == 42.0f)));
            W("flt.trueforall", B(l.TrueForAll(x => x > 0.0f)) + "/" + B(l.TrueForAll(x => x > 3.0f)));
            int fc = 0; float fp = 0.0f;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x);
            W("flt.foreach", I(fc) + "/" + FB(fp));
            W("flt.findall", JF(l.FindAll(x => x > 3.0f)));
            List<float> rv = new List<float>();
            rv.AddRange(new float[] { 1.0f, 2.0f, 3.0f });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("flt.reverse", JF(rv));
            List<float> bs = new List<float>();
            bs.AddRange(new float[] { -1.0f, 0.0f, 1.5f, 4.0f });
            W("flt.bsearch", I(bs.BinarySearch(1.5f)) + "/" + I(bs.BinarySearch(2.0f)) + "/" + I(bs.BinarySearch(0.0f, null)) + "/" + I(bs.BinarySearch(1, 2, -1.0f, null)));
            List<float> so = new List<float>();
            so.AddRange(new float[] { 5.0f, 3.0f, 9.0f, 1.0f });
            so.Sort((x, y) => x < y ? -1 : (x > y ? 1 : 0));
            List<float> sr = new List<float>();
            sr.AddRange(new float[] { 5.0f, 4.0f, 1.0f, 3.0f });
            sr.Sort(1, 2, null);
            W("flt.sort", JF(so) + "/" + JF(sr));
            float[] dst = new float[7];
            bs.CopyTo(dst, 1);
            W("flt.copyto", FB(dst[1]) + "/" + FB(dst[4]));
            W("flt.indexof", I(l.IndexOf(3.25f)) + "/" + I(l.IndexOf(3.25f, 3)) + "/" + I(l.IndexOf(7.75f, 0, 3)));
        }

        //--------------------------------------------------------------------------
        // Narrow families: byte / char / bool / uint
        //--------------------------------------------------------------------------
        private static string JByte(List<byte> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + I(l[i]); }
            return "[" + s + "]";
        }

        private static void ByteOps()
        {
            List<byte> l = new List<byte>();
            l.AddRange(new byte[] { 5, 200, 9, 200, 1 });
            W("byte.base", JByte(l));
            List<byte> ar = new List<byte>();
            ar.Add(7);
            ar.AddRange(new byte[] { 6, 4 });
            ar.AddRange(l);
            W("byte.addrange", JByte(ar));
            List<byte> ins = new List<byte>();
            ins.AddRange(new byte[] { 1, 2, 3 });
            ins.InsertRange(1, new byte[] { 10, 11 });
            W("byte.insertrange", JByte(ins));
            List<byte> rr = new List<byte>();
            rr.AddRange(new byte[] { 1, 2, 3, 4 });
            rr.RemoveRange(1, 2);
            W("byte.removerange", JByte(rr));
            List<byte> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (byte x in g) { gc = gc + 1; }
            W("byte.getrange", I(g.Count) + "/" + I(g[0]) + "/" + I(g[1]) + "/" + I(gc));
            List<byte> ra = new List<byte>();
            ra.AddRange(new byte[] { 1, 2, 3, 2, 4 });
            W("byte.removeall", I(ra.RemoveAll(x => x == 2)) + "/" + JByte(ra));
            W("byte.lastindexof", I(l.LastIndexOf(200)) + "/" + I(l.LastIndexOf(200, 2)) + "/" + I(l.LastIndexOf(200, 3, 2)));
            W("byte.find", I(l.Find(x => x > 100)) + "/" + I(l.Find(x => x > 250)));
            W("byte.findlast", I(l.FindLast(x => x < 9)) + "/" + I(l.FindLast(x => x > 250)));
            W("byte.findindex", I(l.FindIndex(x => x == 9)) + "/" + I(l.FindIndex(x => x == 42)));
            W("byte.findlastindex", I(l.FindLastIndex(x => x == 200)) + "/" + I(l.FindLastIndex(x => x == 42)));
            W("byte.exists", B(l.Exists(x => x == 9)) + "/" + B(l.Exists(x => x == 42)));
            W("byte.trueforall", B(l.TrueForAll(x => x > 0)) + "/" + B(l.TrueForAll(x => x > 3)));
            int fc = 0; int fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x);
            W("byte.foreach", I(fc) + "/" + I(fp));
            W("byte.findall", JByte(l.FindAll(x => x > 100)));
            List<byte> rv = new List<byte>();
            rv.AddRange(new byte[] { 1, 2, 3 });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("byte.reverse", JByte(rv));
            List<byte> bs = new List<byte>();
            bs.AddRange(new byte[] { 0, 5, 9, 200, 255 });
            W("byte.bsearch", I(bs.BinarySearch(9)) + "/" + I(bs.BinarySearch(6)) + "/" + I(bs.BinarySearch(9, null)) + "/" + I(bs.BinarySearch(1, 3, 9, null)));
            List<byte> so = new List<byte>();
            so.AddRange(new byte[] { 5, 3, 9, 1 });
            so.Sort((x, y) => x < y ? -1 : (x > y ? 1 : 0));
            List<byte> sr = new List<byte>();
            sr.AddRange(new byte[] { 5, 4, 1, 3 });
            sr.Sort(1, 2, null);
            W("byte.sort", JByte(so) + "/" + JByte(sr));
            byte[] dst = new byte[7];
            bs.CopyTo(dst, 1);
            W("byte.copyto", I(dst[1]) + "/" + I(dst[5]));
            W("byte.indexof", I(l.IndexOf(200)) + "/" + I(l.IndexOf(200, 2)) + "/" + I(l.IndexOf(200, 2, 2)));
        }

        private static string JChar(List<char> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + I(l[i]); }
            return "[" + s + "]";
        }

        private static void CharOps()
        {
            List<char> l = new List<char>();
            l.AddRange(new char[] { 'e', 'c', 'z', 'c', 'a' });
            W("char.base", JChar(l));
            List<char> ar = new List<char>();
            ar.Add('m');
            ar.AddRange(new char[] { 'n', 'o' });
            ar.AddRange(l);
            W("char.addrange", JChar(ar));
            List<char> ins = new List<char>();
            ins.AddRange(new char[] { 'a', 'b' });
            ins.InsertRange(1, new char[] { 'x', 'y' });
            W("char.insertrange", JChar(ins));
            List<char> rr = new List<char>();
            rr.AddRange(new char[] { 'a', 'b', 'c', 'd' });
            rr.RemoveRange(1, 2);
            W("char.removerange", JChar(rr));
            List<char> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (char x in g) { gc = gc + 1; }
            W("char.getrange", I(g.Count) + "/" + I(g[0]) + "/" + I(g[1]) + "/" + I(gc));
            List<char> ra = new List<char>();
            ra.AddRange(new char[] { 'a', 'b', 'a', 'c' });
            W("char.removeall", I(ra.RemoveAll(x => x == 'a')) + "/" + JChar(ra));
            W("char.lastindexof", I(l.LastIndexOf('c')) + "/" + I(l.LastIndexOf('c', 2)) + "/" + I(l.LastIndexOf('c', 3, 2)));
            W("char.find", I(l.Find(x => x > 'm')) + "/" + I(l.Find(x => x > 'z')));
            W("char.findlast", I(l.FindLast(x => x < 'e')) + "/" + I(l.FindLast(x => x > 'z')));
            W("char.findindex", I(l.FindIndex(x => x == 'z')) + "/" + I(l.FindIndex(x => x == 'q')));
            W("char.findlastindex", I(l.FindLastIndex(x => x == 'c')) + "/" + I(l.FindLastIndex(x => x == 'q')));
            W("char.exists", B(l.Exists(x => x == 'z')) + "/" + B(l.Exists(x => x == 'q')));
            W("char.trueforall", B(l.TrueForAll(x => x > 'a')) + "/" + B(l.TrueForAll(x => x > 'm')));
            int fc = 0; int fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x);
            W("char.foreach", I(fc) + "/" + I(fp));
            W("char.findall", JChar(l.FindAll(x => x <= 'c')));
            List<char> rv = new List<char>();
            rv.AddRange(new char[] { 'a', 'b', 'c' });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("char.reverse", JChar(rv));
            List<char> bs = new List<char>();
            bs.AddRange(new char[] { 'a', 'c', 'e', 'g' });
            W("char.bsearch", I(bs.BinarySearch('e')) + "/" + I(bs.BinarySearch('d')) + "/" + I(bs.BinarySearch('c', null)) + "/" + I(bs.BinarySearch(1, 2, 'e', null)));
            List<char> so = new List<char>();
            so.AddRange(new char[] { 'e', 'c', 'z', 'a' });
            so.Sort((x, y) => x < y ? -1 : (x > y ? 1 : 0));
            List<char> sr = new List<char>();
            sr.AddRange(new char[] { 'e', 'd', 'a', 'c' });
            sr.Sort(1, 2, null);
            W("char.sort", JChar(so) + "/" + JChar(sr));
            char[] dst = new char[7];
            bs.CopyTo(dst, 1);
            W("char.copyto", I(dst[1]) + "/" + I(dst[4]));
            W("char.indexof", I(l.IndexOf('c')) + "/" + I(l.IndexOf('c', 3)) + "/" + I(l.IndexOf('c', 1, 1)));
        }

        private static string JBool(List<bool> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + B(l[i]); }
            return "[" + s + "]";
        }

        private static void BoolOps()
        {
            List<bool> l = new List<bool>();
            l.AddRange(new bool[] { true, false, true, false, true });
            W("bool.base", JBool(l));
            List<bool> ar = new List<bool>();
            ar.Add(false);
            ar.AddRange(new bool[] { true, false });
            ar.AddRange(l);
            W("bool.addrange", JBool(ar));
            List<bool> ins = new List<bool>();
            ins.AddRange(new bool[] { true, true });
            ins.InsertRange(1, new bool[] { false, true });
            W("bool.insertrange", JBool(ins));
            List<bool> rr = new List<bool>();
            rr.AddRange(new bool[] { true, false, true, false });
            rr.RemoveRange(1, 2);
            W("bool.removerange", JBool(rr));
            List<bool> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (bool x in g) { gc = gc + 1; }
            W("bool.getrange", I(g.Count) + "/" + B(g[0]) + "/" + B(g[1]) + "/" + I(gc));
            List<bool> ra = new List<bool>();
            ra.AddRange(new bool[] { true, false, true });
            W("bool.removeall", I(ra.RemoveAll(x => !x)) + "/" + JBool(ra));
            W("bool.lastindexof", I(l.LastIndexOf(true)) + "/" + I(l.LastIndexOf(true, 2)) + "/" + I(l.LastIndexOf(true, 3, 2)));
            W("bool.find", B(l.Find(x => !x)) + "/" + B(l.Find(x => false)));
            W("bool.findlast", B(l.FindLast(x => x)) + "/" + B(l.FindLast(x => false)));
            W("bool.findindex", I(l.FindIndex(x => !x)) + "/" + I(l.FindIndex(x => false)));
            W("bool.findlastindex", I(l.FindLastIndex(x => !x)) + "/" + I(l.FindLastIndex(x => false)));
            W("bool.exists", B(l.Exists(x => !x)) + "/" + B(l.Exists(x => false)));
            W("bool.trueforall", B(l.TrueForAll(x => x)) + "/" + B(l.TrueForAll(x => !x)));
            int fc = 0; int fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + (x ? 1 : 0));
            W("bool.foreach", I(fc) + "/" + I(fp));
            W("bool.findall", JBool(l.FindAll(x => x)));
            List<bool> rv = new List<bool>();
            rv.AddRange(new bool[] { true, false, true });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("bool.reverse", JBool(rv));
            List<bool> bs = new List<bool>();
            bs.AddRange(new bool[] { false, false, true, true });
            W("bool.bsearch", I(bs.BinarySearch(true)) + "/" + I(bs.BinarySearch(false)) + "/" + I(bs.BinarySearch(true, null)) + "/" + I(bs.BinarySearch(1, 2, false, null)));
            List<bool> so = new List<bool>();
            so.AddRange(new bool[] { true, false, true, false });
            so.Sort((x, y) => (x ? 1 : 0) - (y ? 1 : 0));
            List<bool> sr = new List<bool>();
            sr.AddRange(new bool[] { true, true, false, false });
            sr.Sort(1, 2, null);
            W("bool.sort", JBool(so) + "/" + JBool(sr));
            bool[] dst = new bool[7];
            bs.CopyTo(dst, 1);
            W("bool.copyto", B(dst[1]) + "/" + B(dst[2]));
            W("bool.indexof", I(l.IndexOf(false)) + "/" + I(l.IndexOf(false, 2)) + "/" + I(l.IndexOf(true, 1, 2)));
        }

        private static string JUInt(List<uint> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + U(l[i]); }
            return "[" + s + "]";
        }

        private static void UIntOps()
        {
            List<uint> l = new List<uint>();
            l.AddRange(new uint[] { 1u, 4000000000u, 2u, 4000000000u, 5u });
            W("uint.base", JUInt(l));
            List<uint> ar = new List<uint>();
            ar.Add(7u);
            ar.AddRange(new uint[] { 6u, 4u });
            ar.AddRange(l);
            W("uint.addrange", JUInt(ar));
            List<uint> ins = new List<uint>();
            ins.AddRange(new uint[] { 1u, 2u, 3u });
            ins.InsertRange(1, new uint[] { 10u, 11u });
            W("uint.insertrange", JUInt(ins));
            List<uint> rr = new List<uint>();
            rr.AddRange(new uint[] { 1u, 2u, 3u, 4u });
            rr.RemoveRange(1, 2);
            W("uint.removerange", JUInt(rr));
            List<uint> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (uint x in g) { gc = gc + 1; }
            W("uint.getrange", I(g.Count) + "/" + U(g[0]) + "/" + U(g[1]) + "/" + I(gc));
            List<uint> ra = new List<uint>();
            ra.AddRange(new uint[] { 1u, 4000000000u, 2u });
            W("uint.removeall", I(ra.RemoveAll(x => x > 3000000000u)) + "/" + JUInt(ra));
            W("uint.lastindexof", I(l.LastIndexOf(4000000000u)) + "/" + I(l.LastIndexOf(4000000000u, 2)) + "/" + I(l.LastIndexOf(4000000000u, 3, 2)));
            W("uint.find", U(l.Find(x => x > 3000000000u)) + "/" + U(l.Find(x => x > 4294967294u)));
            W("uint.findlast", U(l.FindLast(x => x > 3000000000u)) + "/" + U(l.FindLast(x => x > 4294967294u)));
            W("uint.findindex", I(l.FindIndex(x => x > 3000000000u)) + "/" + I(l.FindIndex(x => x > 4294967294u)));
            W("uint.findlastindex", I(l.FindLastIndex(x => x > 3000000000u)) + "/" + I(l.FindLastIndex(x => x > 4294967294u)));
            W("uint.exists", B(l.Exists(x => x == 4000000000u)) + "/" + B(l.Exists(x => x == 42u)));
            W("uint.trueforall", B(l.TrueForAll(x => x > 0u)) + "/" + B(l.TrueForAll(x => x > 3u)));
            int fc = 0; long fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x % 1000u);
            W("uint.foreach", I(fc) + "/" + L(fp));
            W("uint.findall", JUInt(l.FindAll(x => x > 3000000000u)));
            List<uint> rv = new List<uint>();
            rv.AddRange(new uint[] { 1u, 2u, 3u });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("uint.reverse", JUInt(rv));
            List<uint> bs = new List<uint>();
            bs.AddRange(new uint[] { 1u, 5u, 2147483648u, 4000000000u });
            W("uint.bsearch_unsigned", I(bs.BinarySearch(4000000000u)) + "/" + I(bs.BinarySearch(3000000000u)) + "/" + I(bs.BinarySearch(2147483648u, null)));
            List<uint> so = new List<uint>();
            so.AddRange(new uint[] { 4000000000u, 1u, 2147483648u, 5u });
            so.Sort((x, y) => x < y ? -1 : (x > y ? 1 : 0));
            W("uint.sort_unsigned", JUInt(so));
            uint[] dst = new uint[7];
            bs.CopyTo(dst, 1);
            W("uint.copyto", U(dst[1]) + "/" + U(dst[4]));
            W("uint.indexof", I(l.IndexOf(4000000000u)) + "/" + I(l.IndexOf(4000000000u, 2)) + "/" + I(l.IndexOf(4000000000u, 2, 1)));
        }

        //--------------------------------------------------------------------------
        // List<decimal> (1.0m vs 1.00m: value-equal, scale-preserving)
        //--------------------------------------------------------------------------
        private static string JDec(List<decimal> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + M(l[i]); }
            return "[" + s + "]";
        }

        private static void DecimalOps()
        {
            List<decimal> l = new List<decimal>();
            l.AddRange(new decimal[] { 1.0m, 2.5m, 1.00m, 3m, 2.5m });
            W("dec.base", JDec(l));
            W("dec.scale", M(l[0]) + "/" + M(l[2]) + "/" + I(l.IndexOf(1.00m)) + "/" + I(l.LastIndexOf(1.0m)));
            List<decimal> ar = new List<decimal>();
            ar.Add(7.5m);
            ar.AddRange(new decimal[] { 6.25m, 4.5m });
            ar.AddRange(l);
            W("dec.addrange", JDec(ar));
            List<decimal> ins = new List<decimal>();
            ins.AddRange(new decimal[] { 1m, 2m });
            ins.InsertRange(1, new decimal[] { 10m, 11m });
            W("dec.insertrange", JDec(ins));
            List<decimal> rr = new List<decimal>();
            rr.AddRange(new decimal[] { 1m, 2m, 3m, 4m });
            rr.RemoveRange(1, 2);
            W("dec.removerange", JDec(rr));
            List<decimal> g = l.GetRange(0, 2);
            int gc = 0;
            foreach (decimal x in g) { gc = gc + 1; }
            W("dec.getrange", I(g.Count) + "/" + M(g[0]) + "/" + M(g[1]) + "/" + I(gc));
            List<decimal> ra = new List<decimal>();
            ra.AddRange(new decimal[] { 1.0m, 2.5m, 1.00m });
            W("dec.removeall", I(ra.RemoveAll(x => x == 1.0m)) + "/" + JDec(ra));
            W("dec.lastindexof", I(l.LastIndexOf(2.5m)) + "/" + I(l.LastIndexOf(2.5m, 3)) + "/" + I(l.LastIndexOf(2.5m, 3, 2)));
            W("dec.find", M(l.Find(x => x > 2m)) + "/" + M(l.Find(x => x > 99m)));
            W("dec.findlast", M(l.FindLast(x => x < 3m)) + "/" + M(l.FindLast(x => x < 0m)));
            W("dec.findindex", I(l.FindIndex(x => x == 3m)) + "/" + I(l.FindIndex(x => x == 42m)));
            W("dec.findlastindex", I(l.FindLastIndex(x => x == 1.00m)) + "/" + I(l.FindLastIndex(x => x == 42m)));
            W("dec.exists", B(l.Exists(x => x == 1.00m)) + "/" + B(l.Exists(x => x == 42m)));
            W("dec.trueforall", B(l.TrueForAll(x => x > 0m)) + "/" + B(l.TrueForAll(x => x > 1.5m)));
            int fc = 0; decimal fp = 0m;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x);
            W("dec.foreach", I(fc) + "/" + M(fp));
            W("dec.findall", JDec(l.FindAll(x => x > 1.5m)));
            List<decimal> rv = new List<decimal>();
            rv.AddRange(new decimal[] { 1m, 2m, 3m });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("dec.reverse", JDec(rv));
            List<decimal> bs = new List<decimal>();
            bs.AddRange(new decimal[] { 1.0m, 2.5m, 3.0m, 4.0m });
            W("dec.bsearch", I(bs.BinarySearch(2.5m)) + "/" + I(bs.BinarySearch(2.0m)) + "/" + I(bs.BinarySearch(1.00m, null)) + "/" + I(bs.BinarySearch(1, 2, 2.5m, null)));
            List<decimal> so = new List<decimal>();
            so.AddRange(new decimal[] { 5m, 3m, 9m, 1m });
            so.Sort((x, y) => x < y ? -1 : (x > y ? 1 : 0));
            List<decimal> sr = new List<decimal>();
            sr.AddRange(new decimal[] { 5m, 4m, 1m, 3m });
            sr.Sort(1, 2, null);
            W("dec.sort", JDec(so) + "/" + JDec(sr));
            decimal[] dst = new decimal[7];
            bs.CopyTo(dst, 1);
            W("dec.copyto", M(dst[1]) + "/" + M(dst[4]));
            W("dec.indexof", I(l.IndexOf(2.5m)) + "/" + I(l.IndexOf(2.5m, 3)) + "/" + I(l.IndexOf(2.5m, 3, 2)));
        }

        //--------------------------------------------------------------------------
        // List<DateTime> (same ticks, different Kind => still Equal; ticks-only
        // ordering) and List<TimeSpan>
        //--------------------------------------------------------------------------
        private static string JDt(List<DateTime> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + DT(l[i]); }
            return "[" + s + "]";
        }

        private static void DateTimeOps()
        {
            DateTime d0 = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            DateTime d1 = new DateTime(2021, 5, 6, 7, 8, 9);
            DateTime d1u = new DateTime(2021, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            DateTime d2 = new DateTime(1999, 12, 31);
            List<DateTime> l = new List<DateTime>();
            l.AddRange(new DateTime[] { d0, d1, d1u, d2, d0 });
            W("dt.base", JDt(l) + "/" + I(l.Count));
            W("dt.kind_eq", I(l.IndexOf(d1u)) + "/" + I(l.LastIndexOf(d1)) + "/" + I(l.IndexOf(d0)) + "/" + B(l.Exists(x => x.Ticks == d1.Ticks && x.Kind == DateTimeKind.Utc)));
            W("dt.kind_find", DT(l.Find(x => x.Ticks == d1.Ticks && x.Kind == DateTimeKind.Utc)) + "/" + I(l.FindLastIndex(x => x.Ticks == d1.Ticks)));
            List<DateTime> rd = new List<DateTime>();
            rd.AddRange(new DateTime[] { d0, d1, d2 });
            W("dt.remove_ticks_only", B(rd.Remove(d1u)) + "/" + I(rd.Count) + "/" + JDt(rd));
            List<DateTime> ar = new List<DateTime>();
            ar.Add(d2);
            ar.AddRange(new DateTime[] { d0, d1 });
            ar.AddRange(l);
            W("dt.addrange", JDt(ar));
            List<DateTime> ins = new List<DateTime>();
            ins.AddRange(new DateTime[] { d2, d0 });
            ins.InsertRange(1, new DateTime[] { d1, d1u });
            W("dt.insertrange", JDt(ins));
            List<DateTime> rr = new List<DateTime>();
            rr.AddRange(new DateTime[] { d0, d1, d2 });
            rr.RemoveRange(1, 2);
            W("dt.removerange", JDt(rr));
            List<DateTime> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (DateTime x in g) { gc = gc + 1; }
            W("dt.getrange", I(g.Count) + "/" + DT(g[0]) + "/" + DT(g[1]) + "/" + I(gc));
            List<DateTime> ra = new List<DateTime>();
            ra.AddRange(new DateTime[] { d0, d1, d1u, d2 });
            W("dt.removeall_ticks", I(ra.RemoveAll(x => x.Ticks == d0.Ticks)) + "/" + JDt(ra));
            W("dt.lastindexof", I(l.LastIndexOf(d0)) + "/" + I(l.LastIndexOf(d0, 3)) + "/" + I(l.LastIndexOf(d0, 3, 2)));
            W("dt.find", DT(l.Find(x => x.Year == 1999)) + "/" + DT(l.Find(x => x.Year == 1800)));
            W("dt.findlast", DT(l.FindLast(x => x.Year == 2020)) + "/" + DT(l.FindLast(x => x.Year == 1800)));
            W("dt.findindex", I(l.FindIndex(x => x.Year == 1999)) + "/" + I(l.FindIndex(x => x.Year == 1800)));
            W("dt.findlastindex", I(l.FindLastIndex(x => x.Year == 2021)) + "/" + I(l.FindLastIndex(x => x.Year == 1800)));
            W("dt.exists", B(l.Exists(x => x.Year == 2021)) + "/" + B(l.Exists(x => x.Year == 1800)));
            W("dt.trueforall", B(l.TrueForAll(x => x.Year > 1900)) + "/" + B(l.TrueForAll(x => x.Year > 2000)));
            int fc = 0; long fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x.Ticks % 1000);
            W("dt.foreach", I(fc) + "/" + L(fp));
            W("dt.findall", JDt(l.FindAll(x => x.Ticks == d1.Ticks)));
            List<DateTime> rv = new List<DateTime>();
            rv.AddRange(new DateTime[] { d2, d0, d1 });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("dt.reverse", JDt(rv));
            List<DateTime> bs = new List<DateTime>();
            bs.AddRange(new DateTime[] { d2, d0, d1 });
            W("dt.bsearch_ticks", I(bs.BinarySearch(d1u)) + "/" + I(bs.BinarySearch(d0)) + "/" + I(bs.BinarySearch(new DateTime(2010, 1, 1))) + "/" + I(bs.BinarySearch(1, 2, d0, null)));
            List<DateTime> so = new List<DateTime>();
            so.AddRange(new DateTime[] { d1u, d2, d0 });
            so.Sort((x, y) => x.Ticks < y.Ticks ? -1 : (x.Ticks > y.Ticks ? 1 : 0));
            W("dt.sort_ticks", JDt(so));
            DateTime[] dst = new DateTime[7];
            bs.CopyTo(dst, 1);
            W("dt.copyto_kind", DT(dst[1]) + "/" + DT(dst[3]));
            W("dt.indexof", I(l.IndexOf(d0)) + "/" + I(l.IndexOf(d0, 1)) + "/" + I(l.IndexOf(d2, 0, 2)));
        }

        private static string JTs(List<TimeSpan> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + TS(l[i]); }
            return "[" + s + "]";
        }

        private static void TimeSpanOps()
        {
            List<TimeSpan> l = new List<TimeSpan>();
            l.AddRange(new TimeSpan[] { TimeSpan.FromMinutes(90), TimeSpan.FromSeconds(-5), TimeSpan.FromHours(2), TimeSpan.FromHours(2), TimeSpan.Zero });
            W("ts.base", JTs(l));
            List<TimeSpan> ar = new List<TimeSpan>();
            ar.Add(TimeSpan.FromDays(1));
            ar.AddRange(new TimeSpan[] { TimeSpan.FromSeconds(1) });
            ar.AddRange(l);
            W("ts.addrange", JTs(ar));
            List<TimeSpan> ins = new List<TimeSpan>();
            ins.AddRange(new TimeSpan[] { TimeSpan.FromSeconds(2) });
            ins.InsertRange(1, new TimeSpan[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4) });
            W("ts.insertrange", JTs(ins));
            List<TimeSpan> rr = new List<TimeSpan>();
            rr.AddRange(new TimeSpan[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3) });
            rr.RemoveRange(1, 2);
            W("ts.removerange", JTs(rr));
            List<TimeSpan> g = l.GetRange(0, 2);
            int gc = 0;
            foreach (TimeSpan x in g) { gc = gc + 1; }
            W("ts.getrange", I(g.Count) + "/" + TS(g[0]) + "/" + TS(g[1]) + "/" + I(gc));
            List<TimeSpan> ra = new List<TimeSpan>();
            ra.AddRange(new TimeSpan[] { TimeSpan.FromHours(2), TimeSpan.FromSeconds(-5), TimeSpan.FromHours(2) });
            W("ts.removeall", I(ra.RemoveAll(x => x.Ticks > 0)) + "/" + JTs(ra));
            W("ts.lastindexof", I(l.LastIndexOf(TimeSpan.FromHours(2))) + "/" + I(l.LastIndexOf(TimeSpan.FromHours(2), 2)) + "/" + I(l.LastIndexOf(TimeSpan.FromHours(2), 3, 2)));
            W("ts.find", TS(l.Find(x => x.Ticks < 0)) + "/" + TS(l.Find(x => x.Ticks > 10000000000L)));
            W("ts.findlast", TS(l.FindLast(x => x == TimeSpan.FromHours(2))) + "/" + TS(l.FindLast(x => x.Ticks > 10000000000L)));
            W("ts.findindex", I(l.FindIndex(x => x.Ticks < 0)) + "/" + I(l.FindIndex(x => x.Ticks > 10000000000L)));
            W("ts.findlastindex", I(l.FindLastIndex(x => x == TimeSpan.FromHours(2))) + "/" + I(l.FindLastIndex(x => x.Ticks > 10000000000L)));
            W("ts.exists", B(l.Exists(x => x.Ticks == 0)) + "/" + B(l.Exists(x => x.Ticks == 12345)));
            W("ts.trueforall", B(l.TrueForAll(x => x.Ticks < 10000000000L)) + "/" + B(l.TrueForAll(x => x.Ticks > 0)));
            int fc = 0; long fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x.Ticks % 1000);
            W("ts.foreach", I(fc) + "/" + L(fp));
            W("ts.findall", JTs(l.FindAll(x => x.Ticks > 0)));
            List<TimeSpan> rv = new List<TimeSpan>();
            rv.AddRange(new TimeSpan[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3) });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("ts.reverse", JTs(rv));
            List<TimeSpan> bs = new List<TimeSpan>();
            bs.AddRange(new TimeSpan[] { TimeSpan.FromSeconds(-5), TimeSpan.Zero, TimeSpan.FromMinutes(90), TimeSpan.FromHours(2) });
            W("ts.bsearch", I(bs.BinarySearch(TimeSpan.FromMinutes(90))) + "/" + I(bs.BinarySearch(TimeSpan.FromSeconds(30))) + "/" + I(bs.BinarySearch(TimeSpan.Zero, null)) + "/" + I(bs.BinarySearch(1, 2, TimeSpan.Zero, null)));
            List<TimeSpan> so = new List<TimeSpan>();
            so.AddRange(new TimeSpan[] { TimeSpan.FromHours(2), TimeSpan.FromSeconds(-5), TimeSpan.Zero });
            so.Sort((x, y) => x.Ticks < y.Ticks ? -1 : (x.Ticks > y.Ticks ? 1 : 0));
            W("ts.sort", JTs(so));
            TimeSpan[] dst = new TimeSpan[7];
            bs.CopyTo(dst, 1);
            W("ts.copyto", TS(dst[1]) + "/" + TS(dst[4]));
            W("ts.indexof", I(l.IndexOf(TimeSpan.FromHours(2))) + "/" + I(l.IndexOf(TimeSpan.FromHours(2), 3)) + "/" + I(l.IndexOf(TimeSpan.Zero, 0, 4)));
        }

        //--------------------------------------------------------------------------
        // List<Guid> and List<DateTimeOffset> (16-byte value cells)
        //--------------------------------------------------------------------------
        private static Guid G(int a)
        {
            return new Guid(a, 0, 0, (byte)a, 0, 0, 0, 0, 0, 0, 0);
        }

        private static string JGd(List<Guid> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + GD(l[i]); }
            return "[" + s + "]";
        }

        private static void GuidOps()
        {
            List<Guid> l = new List<Guid>();
            l.AddRange(new Guid[] { G(3), G(1), G(2), G(1), G(4) });
            W("guid.base", JGd(l));
            List<Guid> ar = new List<Guid>();
            ar.Add(G(9));
            ar.AddRange(new Guid[] { G(8), G(7) });
            ar.AddRange(l);
            W("guid.addrange", JGd(ar));
            List<Guid> ins = new List<Guid>();
            ins.AddRange(new Guid[] { G(1), G(2) });
            ins.InsertRange(1, new Guid[] { G(5), G(6) });
            W("guid.insertrange", JGd(ins));
            List<Guid> rr = new List<Guid>();
            rr.AddRange(new Guid[] { G(1), G(2), G(3), G(4) });
            rr.RemoveRange(1, 2);
            W("guid.removerange", JGd(rr));
            List<Guid> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (Guid x in g) { gc = gc + 1; }
            W("guid.getrange", I(g.Count) + "/" + GD(g[0]) + "/" + GD(g[1]) + "/" + I(gc));
            List<Guid> ra = new List<Guid>();
            ra.AddRange(new Guid[] { G(1), G(2), G(1) });
            W("guid.removeall", I(ra.RemoveAll(x => x == G(1))) + "/" + JGd(ra));
            W("guid.lastindexof", I(l.LastIndexOf(G(1))) + "/" + I(l.LastIndexOf(G(1), 2)) + "/" + I(l.LastIndexOf(G(1), 3, 2)));
            W("guid.find", GD(l.Find(x => x == G(4))) + "/" + GD(l.Find(x => x == G(42))));
            W("guid.findlast", GD(l.FindLast(x => x == G(1))) + "/" + GD(l.FindLast(x => x == G(42))));
            W("guid.findindex", I(l.FindIndex(x => x == G(2))) + "/" + I(l.FindIndex(x => x == G(42))));
            W("guid.findlastindex", I(l.FindLastIndex(x => x == G(1))) + "/" + I(l.FindLastIndex(x => x == G(42))));
            W("guid.exists", B(l.Exists(x => x == G(4))) + "/" + B(l.Exists(x => x == G(42))));
            W("guid.trueforall", B(l.TrueForAll(x => x != G(42))) + "/" + B(l.TrueForAll(x => x == G(1))));
            int fc = 0; int fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + (x == G(1) ? 1 : 0));
            W("guid.foreach", I(fc) + "/" + I(fp));
            W("guid.findall", JGd(l.FindAll(x => x == G(1))));
            List<Guid> rv = new List<Guid>();
            rv.AddRange(new Guid[] { G(1), G(2), G(3) });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("guid.reverse", JGd(rv));
            List<Guid> bs = new List<Guid>();
            bs.AddRange(new Guid[] { G(1), G(2), G(3), G(4) });
            W("guid.bsearch", I(bs.BinarySearch(G(3))) + "/" + I(bs.BinarySearch(G(9))) + "/" + I(bs.BinarySearch(G(2), null)) + "/" + I(bs.BinarySearch(1, 2, G(3), null)));
            List<Guid> so = new List<Guid>();
            so.AddRange(new Guid[] { G(3), G(1), G(4), G(2) });
            so.Sort((x, y) => x.CompareTo(y));
            W("guid.sort", JGd(so));
            Guid[] dst = new Guid[7];
            bs.CopyTo(dst, 1);
            W("guid.copyto", GD(dst[1]) + "/" + GD(dst[4]));
            W("guid.indexof", I(l.IndexOf(G(1))) + "/" + I(l.IndexOf(G(1), 2)) + "/" + I(l.IndexOf(G(4), 0, 3)));
        }

        private static string JDto(List<DateTimeOffset> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + DTO(l[i]); }
            return "[" + s + "]";
        }

        private static void DateTimeOffsetOps()
        {
            DateTimeOffset o0 = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
            DateTimeOffset o1 = new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.Zero);
            DateTimeOffset o1b = new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.Zero);
            DateTimeOffset o2 = new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero);
            List<DateTimeOffset> l = new List<DateTimeOffset>();
            l.AddRange(new DateTimeOffset[] { o0, o1, o1b, o2, o0 });
            W("dto.base", JDto(l));
            List<DateTimeOffset> ar = new List<DateTimeOffset>();
            ar.Add(o2);
            ar.AddRange(new DateTimeOffset[] { o0 });
            ar.AddRange(l);
            W("dto.addrange", JDto(ar));
            List<DateTimeOffset> ins = new List<DateTimeOffset>();
            ins.AddRange(new DateTimeOffset[] { o2, o0 });
            ins.InsertRange(1, new DateTimeOffset[] { o1 });
            W("dto.insertrange", JDto(ins));
            List<DateTimeOffset> rr = new List<DateTimeOffset>();
            rr.AddRange(new DateTimeOffset[] { o0, o1, o2 });
            rr.RemoveRange(1, 2);
            W("dto.removerange", JDto(rr));
            List<DateTimeOffset> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (DateTimeOffset x in g) { gc = gc + 1; }
            W("dto.getrange", I(g.Count) + "/" + DTO(g[0]) + "/" + DTO(g[1]) + "/" + I(gc));
            List<DateTimeOffset> ra = new List<DateTimeOffset>();
            ra.AddRange(new DateTimeOffset[] { o0, o1, o1b });
            W("dto.removeall", I(ra.RemoveAll(x => x == o1)) + "/" + JDto(ra));
            W("dto.lastindexof", I(l.LastIndexOf(o0)) + "/" + I(l.LastIndexOf(o0, 3)) + "/" + I(l.LastIndexOf(o0, 3, 2)));
            W("dto.find", DTO(l.Find(x => x.Year == 1999)) + "/" + DTO(l.Find(x => x.Year == 1800)));
            W("dto.findlast", DTO(l.FindLast(x => x.Year == 2021)) + "/" + DTO(l.FindLast(x => x.Year == 1800)));
            W("dto.findindex", I(l.FindIndex(x => x == o1b)) + "/" + I(l.FindIndex(x => x.Year == 1800)));
            W("dto.findlastindex", I(l.FindLastIndex(x => x == o1)) + "/" + I(l.FindLastIndex(x => x.Year == 1800)));
            W("dto.exists", B(l.Exists(x => x == o1b)) + "/" + B(l.Exists(x => x.Year == 1800)));
            W("dto.trueforall", B(l.TrueForAll(x => x.Year > 1900)) + "/" + B(l.TrueForAll(x => x.Year > 2000)));
            int fc = 0; long fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x.Ticks % 1000 + x.Offset.Ticks % 7);
            W("dto.foreach", I(fc) + "/" + L(fp));
            W("dto.findall", JDto(l.FindAll(x => x == o1)));
            List<DateTimeOffset> rv = new List<DateTimeOffset>();
            rv.AddRange(new DateTimeOffset[] { o2, o0, o1 });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("dto.reverse", JDto(rv));
            List<DateTimeOffset> bs = new List<DateTimeOffset>();
            bs.AddRange(new DateTimeOffset[] { o2, o0, o1 });
            W("dto.bsearch", I(bs.BinarySearch(o1b)) + "/" + I(bs.BinarySearch(new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero))) + "/" + I(bs.BinarySearch(o0, null)));
            List<DateTimeOffset> so = new List<DateTimeOffset>();
            so.AddRange(new DateTimeOffset[] { o1, o2, o0 });
            so.Sort((x, y) => x.Ticks < y.Ticks ? -1 : (x.Ticks > y.Ticks ? 1 : 0));
            W("dto.sort", JDto(so));
            DateTimeOffset[] dst = new DateTimeOffset[7];
            bs.CopyTo(dst, 1);
            W("dto.copyto", DTO(dst[1]) + "/" + DTO(dst[3]));
            W("dto.indexof", I(l.IndexOf(o0)) + "/" + I(l.IndexOf(o0, 1)) + "/" + I(l.IndexOf(o2, 0, 2)));
        }

        //--------------------------------------------------------------------------
        // List<Pt> (user value struct: by-value delegates, memcmp equality)
        //--------------------------------------------------------------------------
        private static string JPt(List<Pt> l)
        {
            string s = "";
            for (int i = 0; i < l.Count; i++) { if (i > 0) { s = s + ","; } s = s + P(l[i]); }
            return "[" + s + "]";
        }

        private static void StructOps()
        {
            List<Pt> l = new List<Pt>();
            l.AddRange(new Pt[] { new Pt(5, 1), new Pt(3, 2), new Pt(5, 3), new Pt(3, 2), new Pt(1, 4) });
            W("pt.base", JPt(l));
            List<Pt> ar = new List<Pt>();
            ar.Add(new Pt(9, 9));
            ar.AddRange(new Pt[] { new Pt(8, 8) });
            ar.AddRange(l);
            W("pt.addrange", JPt(ar));
            List<Pt> ins = new List<Pt>();
            ins.AddRange(new Pt[] { new Pt(1, 1) });
            ins.InsertRange(1, new Pt[] { new Pt(6, 6), new Pt(7, 7) });
            W("pt.insertrange", JPt(ins));
            List<Pt> rr = new List<Pt>();
            rr.AddRange(new Pt[] { new Pt(1, 1), new Pt(2, 2), new Pt(3, 3) });
            rr.RemoveRange(1, 2);
            W("pt.removerange", JPt(rr));
            List<Pt> g = l.GetRange(1, 2);
            int gc = 0;
            foreach (Pt x in g) { gc = gc + 1; }
            W("pt.getrange", I(g.Count) + "/" + P(g[0]) + "/" + P(g[1]) + "/" + I(gc));
            List<Pt> ra = new List<Pt>();
            ra.AddRange(new Pt[] { new Pt(5, 1), new Pt(3, 2), new Pt(5, 3) });
            W("pt.removeall", I(ra.RemoveAll(x => x.X == 5)) + "/" + JPt(ra));
            W("pt.lastindexof", I(l.LastIndexOf(new Pt(3, 2))) + "/" + I(l.LastIndexOf(new Pt(3, 2), 2)) + "/" + I(l.LastIndexOf(new Pt(5, 3), 3, 2)));
            W("pt.find", P(l.Find(x => x.X > 4)) + "/" + P(l.Find(x => x.X > 40)));
            W("pt.findlast", P(l.FindLast(x => x.X > 4)) + "/" + P(l.FindLast(x => x.X > 40)));
            W("pt.findindex", I(l.FindIndex(x => x.Y == 3)) + "/" + I(l.FindIndex(x => x.Y == 42)));
            W("pt.findlastindex", I(l.FindLastIndex(x => x.X == 3)) + "/" + I(l.FindLastIndex(x => x.X == 42)));
            W("pt.exists", B(l.Exists(x => x.X == 1)) + "/" + B(l.Exists(x => x.X == 42)));
            W("pt.trueforall", B(l.TrueForAll(x => x.X > 0)) + "/" + B(l.TrueForAll(x => x.X > 4)));
            int fc = 0; int fp = 0;
            l.ForEach(x => fc = fc + 1);
            l.ForEach(x => fp = fp + x.X * 100 + x.Y);
            W("pt.foreach", I(fc) + "/" + I(fp));
            W("pt.findall", JPt(l.FindAll(x => x.X == 5)));
            List<Pt> rv = new List<Pt>();
            rv.AddRange(new Pt[] { new Pt(1, 1), new Pt(2, 2), new Pt(3, 3) });
            rv.Reverse();
            rv.Reverse(1, 1);
            W("pt.reverse", JPt(rv));
            List<Pt> so = new List<Pt>();
            so.AddRange(new Pt[] { new Pt(3, 1), new Pt(1, 2), new Pt(4, 3), new Pt(2, 4) });
            so.Sort((x, y) => x.X < y.X ? -1 : (x.X > y.X ? 1 : 0));
            W("pt.sort", JPt(so));
            Pt[] dst = new Pt[7];
            l.CopyTo(dst, 1);
            W("pt.copyto", P(dst[1]) + "/" + P(dst[5]));
            W("pt.indexof", I(l.IndexOf(new Pt(5, 1))) + "/" + I(l.IndexOf(new Pt(5, 1), 1)) + "/" + I(l.IndexOf(new Pt(3, 2), 0, 2)));
        }

        //--------------------------------------------------------------------------
        // Boundaries: empty / single / remove-all / out-of-range / negative
        // count. The listobj domain (long) and the listi4 domain (int) both
        // carry their own validation, so each set is exercised once per domain.
        //--------------------------------------------------------------------------
        private static void EdgeOps()
        {
            List<long> e = new List<long>();
            W("edge.empty", I(e.Count) + "/" + I(e.IndexOf(1L)) + "/" + I(e.LastIndexOf(1L))
                + "/" + I(e.FindIndex(x => true)) + "/" + I(e.FindLastIndex(x => true))
                + "/" + L(e.Find(x => true)) + "/" + L(e.FindLast(x => true))
                + "/" + B(e.Exists(x => true)) + "/" + B(e.TrueForAll(x => false))
                + "/" + I(e.FindAll(x => true).Count) + "/" + I(e.RemoveAll(x => true))
                + "/" + I(e.BinarySearch(1L)) + "/" + I(e.GetRange(0, 0).Count));
            e.Reverse();
            e.Reverse(0, 0);
            e.RemoveRange(0, 0);
            e.Sort((x, y) => 0);
            e.Sort(0, 0, null);
            e.AddRange(new long[0]);
            long[] ed = new long[0];
            e.CopyTo(ed, 0);
            int efc = 0;
            e.ForEach(x => efc = efc + 1);
            W("edge.empty2", I(e.Count) + "/" + I(ed.Length) + "/" + I(efc));

            List<long> s1 = new List<long>();
            s1.Add(7L);
            W("edge.single", I(s1.Count) + "/" + L(s1.Find(x => true)) + "/" + I(s1.FindIndex(x => true))
                + "/" + I(s1.FindLastIndex(x => true)) + "/" + I(s1.BinarySearch(7L)) + "/" + I(s1.BinarySearch(8L))
                + "/" + I(s1.IndexOf(7L)) + "/" + I(s1.LastIndexOf(7L)) + "/" + I(s1.RemoveAll(x => true)) + "/" + I(s1.Count));

            List<long> ra = new List<long>();
            ra.AddRange(new long[] { 1L, 2L, 3L });
            ra.RemoveRange(0, 3);
            W("edge.removerange_all", I(ra.Count) + "/" + B(ra.TrueForAll(x => false)) + "/" + I(ra.FindAll(x => true).Count));

            T("edge.getrange_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.GetRange(1, 9); });
            T("edge.getrange_neg_count", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.GetRange(0, -1); });
            T("edge.getrange_neg_index", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.GetRange(-1, 0); });
            T("edge.removerange_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.RemoveRange(1, 9); });
            T("edge.removerange_neg_count", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.RemoveRange(0, -1); });
            T("edge.reverse_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.Reverse(1, 9); });
            T("edge.reverse_neg_count", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.Reverse(0, -1); });
            T("edge.insertrange_oob", () => { List<long> z = new List<long>(); z.InsertRange(9, new long[] { 3L }); });
            T("edge.insertrange_neg", () => { List<long> z = new List<long>(); z.InsertRange(-1, new long[] { 3L }); });
            T("edge.addrange_null", () => { List<long> z = new List<long>(); z.AddRange(null); });
            T("edge.insertrange_null", () => { List<long> z = new List<long>(); z.InsertRange(0, null); });
            T("edge.indexof_start_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.IndexOf(1L, 9); });
            T("edge.indexof_count_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.IndexOf(1L, 0, 9); });
            T("edge.indexof_count_neg", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.IndexOf(1L, 0, -1); });
            T("edge.lastindexof_idx_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.LastIndexOf(1L, 9); });
            T("edge.lastindexof_count_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.LastIndexOf(1L, 0, 9); });
            T("edge.lastindexof_count_neg", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.LastIndexOf(1L, 0, -1); });
            T("edge.lastindexof_empty_idx", () => { List<long> z = new List<long>(); z.LastIndexOf(1L, 0); });
            T("edge.bsearch_range_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.BinarySearch(0, 9, 1L, null); });
            T("edge.bsearch_range_neg", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.BinarySearch(0, -1, 1L, null); });
            T("edge.sort_range_oob", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.Sort(0, 9, null); });
            T("edge.sort_range_neg", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.Sort(0, -1, null); });
            T("edge.sort_comparison_null", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.Sort((Comparison<long>)null); });
            T("edge.copyto_short", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); long[] d = new long[1]; z.CopyTo(d, 0); });
            T("edge.copyto_neg", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); long[] d = new long[3]; z.CopyTo(d, -1); });
            T("edge.copyto_null", () => { List<long> z = new List<long>(); z.AddRange(new long[] { 1L, 2L }); z.CopyTo(null, 0); });

            List<int> ei = new List<int>();
            W("edge.i4_empty", I(ei.Count) + "/" + I(ei.FindIndex(x => true)) + "/" + I(ei.BinarySearch(1))
                + "/" + I(ei.RemoveAll(x => true)) + "/" + I(ei.GetRange(0, 0).Count));
            ei.Reverse();
            ei.Reverse(0, 0);
            ei.RemoveRange(0, 0);
            ei.Sort((x, y) => 0);
            ei.Sort(0, 0, Comparer<int>.Default);
            T("edge.i4_getrange_oob", () => { List<int> z = new List<int>(new int[] { 1, 2 }); z.GetRange(0, 9); });
            T("edge.i4_removerange_neg", () => { List<int> z = new List<int>(new int[] { 1, 2 }); z.RemoveRange(0, -1); });
            T("edge.i4_insertrange_oob", () => { List<int> z = new List<int>(new int[] { 1, 2 }); z.InsertRange(9, new int[] { 3 }); });
            T("edge.i4_copyto_short", () => { List<int> z = new List<int>(new int[] { 1, 2 }); int[] d = new int[1]; z.CopyTo(d, 0); });
        }

        public static void Main()
        {
            IntOps();
            StringOps();
            LongOps();
            DoubleOps();
            FloatOps();
            ByteOps();
            CharOps();
            BoolOps();
            UIntOps();
            DecimalOps();
            DateTimeOps();
            TimeSpanOps();
            GuidOps();
            DateTimeOffsetOps();
            StructOps();
            EdgeOps();
            Console.WriteLine("done");
        }
    }
}
