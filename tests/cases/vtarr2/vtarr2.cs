//------------------------------------------------------------------------------
// vtarr2 -- D: value-type arrays whose element token is a GENERIC
// INSTANTIATION TypeSpec (Nullable`1<int>, ValueTuple`2<int,int>). The
// ArrayElementType name lookup resolves a TypeSpec to "System.Object" today,
// so the array is allocated as a GC REFERENCE array over value payload.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class VtArr2
    {
        static void W(string k, string v) { Console.WriteLine("[" + k + "]" + v); }

        public static void Main()
        {
            int?[] ns = new int?[3];
            ns[0] = 5;
            ns[1] = null;
            ns[2] = 7;
            W("n.len", ns.Length.ToString());
            W("n.0", ns[0].HasValue + "/" + ns[0].GetValueOrDefault());
            W("n.1", (ns[1].HasValue ? "T" : "F"));
            W("n.2", ns[2].HasValue + "/" + ns[2].GetValueOrDefault());
            int nsum = 0;
            for (int i = 0; i < ns.Length; i++) if (ns[i].HasValue) nsum += ns[i].GetValueOrDefault();
            W("n.sum", nsum.ToString());

            (int, int)[] ts = new (int, int)[3];
            ts[0] = (1, 2);
            ts[1] = (3, 4);
            ts[2] = (5, 6);
            W("t.len", ts.Length.ToString());
            W("t.0", ts[0].Item1 + "," + ts[0].Item2);
            W("t.1", ts[1].Item1 + "," + ts[1].Item2);
            W("t.2", ts[2].Item1 + "," + ts[2].Item2);
            int tsum = 0;
            foreach (var t in ts) tsum += t.Item1 + t.Item2;
            W("t.sum", tsum.ToString());
        }
    }
}
