//------------------------------------------------------------------------------
// vtarr1 -- D: value-type array ALLOCATION + element read/write (newarr of a
// value type). net48 truth is the reference; every line is [key]value.
//
// Covers: user struct[], decimal[], DateTime[], Guid[], long[], double[],
// plus Length / foreach / for-loop element access.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public struct P
    {
        public int X;
        public int Y;
        public override string ToString() { return X + ":" + Y; }
    }

    public static class VtArr1
    {
        static void W(string k, string v) { Console.WriteLine("[" + k + "]" + v); }

        public static void Main()
        {
            P[] ps = new P[3];
            ps[0].X = 1; ps[0].Y = 2;
            ps[1].X = 3; ps[1].Y = 4;
            ps[2].X = 5; ps[2].Y = 6;
            W("p.len", ps.Length.ToString());
            W("p.0", ps[0].ToString());
            W("p.1", ps[1].ToString());
            W("p.2", ps[2].ToString());
            int psum = 0;
            foreach (P q in ps) psum += q.X + q.Y;
            W("p.foreach", psum.ToString());

            decimal[] ds = new decimal[3];
            ds[0] = 1.5m; ds[1] = -2.25m; ds[2] = 100m;
            W("d.len", ds.Length.ToString());
            W("d.0", ds[0].ToString());
            W("d.1", ds[1].ToString());
            W("d.2", ds[2].ToString());
            decimal dsum = 0m;
            for (int i = 0; i < ds.Length; i++) dsum += ds[i];
            W("d.sum", dsum.ToString());

            DateTime[] dts = new DateTime[2];
            dts[0] = new DateTime(2020, 1, 2, 3, 4, 5);
            dts[1] = new DateTime(2021, 6, 7, 8, 9, 10);
            W("dt.len", dts.Length.ToString());
            W("dt.0", dts[0].ToString("yyyy-MM-dd HH:mm:ss"));
            W("dt.1", dts[1].ToString("yyyy-MM-dd HH:mm:ss"));

            Guid[] gs = new Guid[2];
            gs[0] = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            gs[1] = new Guid("ffeeddcc-bbaa-9988-7766-554433221100");
            W("g.len", gs.Length.ToString());
            W("g.0", gs[0].ToString());
            W("g.1", gs[1].ToString());

            long[] ls = new long[3];
            ls[0] = 4000000000L; ls[1] = -4000000000L; ls[2] = long.MaxValue;
            W("l.len", ls.Length.ToString());
            W("l.0", ls[0].ToString());
            W("l.1", ls[1].ToString());
            W("l.2", ls[2].ToString());

            double[] db = new double[3];
            db[0] = 1.25; db[1] = -3.5; db[2] = 1e300;
            W("db.len", db.Length.ToString());
            W("db.0", db[0].ToString("R"));
            W("db.1", db[1].ToString("R"));
            W("db.2", db[2].ToString("R"));
        }
    }
}
