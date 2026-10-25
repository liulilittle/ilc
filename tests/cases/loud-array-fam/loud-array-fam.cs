//------------------------------------------------------------------------------
// loud-array-fam.cs -- e2e reference case for the Array static members over the
// NON-int32 / NON-string element families (the residual LOUD surface).
//
// The runtime already ships the delegate/resize/empty/reverse kernels for the
// int64 / double / string families and the narrow-integral/float reverse and
// empty factories, but the transpiler did NOT wire them, so every one of these
// calls LOUD-rejected with:
//
//   'Array.FindLast<T> generic argument 0x.. not wired: only int32 (0x08) ...'
//   'Array.Empty<T> generic argument 0x.. not wired in M5'
//   'Array.Reverse<T> generic argument 0x.. not wired in M5'
//
// This case drives the wired members and asserts exact element VALUES plus the
// net48 boundary semantics. Every line prints "label=..."; net48 is the byte
// reference.
//------------------------------------------------------------------------------
using System;

class Prog
{
    static void Main()
    {
        // ---- FindLast over long[] / double[] / string[] ----
        long[] la = new long[] { 5, 3, 9, 1, 7 };
        Console.WriteLine("fl_i8=" + Array.FindLast(la, x => x > 4));
        double[] da = new double[] { 1.5, 2.5, 3.5, 2.5 };
        Console.WriteLine("fl_d=" + Array.FindLast(da, x => x > 2.0));
        string[] sa = new string[] { "bb", "aa", "cc", "aa" };
        Console.WriteLine("fl_str=" + Array.FindLast(sa, x => x == "aa"));

        // ---- ForEach over long[] / double[] / string[] ----
        long lsum = 0;
        Array.ForEach(la, x => lsum += x);
        Console.WriteLine("fe_i8=" + lsum);
        double dsum = 0;
        Array.ForEach(da, x => dsum += x);
        Console.WriteLine("fe_d=" + dsum);
        int scount = 0;
        Array.ForEach(sa, x => scount += x.Length);
        Console.WriteLine("fe_str=" + scount);

        // ---- FindAll over double[] ----
        double[] all = Array.FindAll(da, x => x > 2.0);
        Console.WriteLine("fa_d=" + all.Length + " " + all[0] + all[1]);

        // ---- Resize over double[] (grow + shrink) ----
        double[] r = new double[] { 1.5, 2.5, 3.5 };
        Array.Resize(ref r, 5);
        Console.WriteLine("rs_d5=" + r.Length + " " + r[0] + r[2] + r[4]);
        Array.Resize(ref r, 2);
        Console.WriteLine("rs_d2=" + r.Length + " " + r[1]);

        // ---- Empty over float / the narrow integrals ----
        Console.WriteLine("em_f=" + Array.Empty<float>().Length);
        Console.WriteLine("em_sb=" + Array.Empty<sbyte>().Length);
        Console.WriteLine("em_by=" + Array.Empty<byte>().Length);
        Console.WriteLine("em_bo=" + Array.Empty<bool>().Length);
        Console.WriteLine("em_sh=" + Array.Empty<short>().Length);
        Console.WriteLine("em_us=" + Array.Empty<ushort>().Length);
        Console.WriteLine("em_ui=" + Array.Empty<uint>().Length);

        // ---- Reverse over float / the narrow integrals ----
        float[] fa = new float[] { 1f, 2f, 3f, 4f };
        Array.Reverse(fa);
        Console.WriteLine("rv_f=" + fa[0] + fa[1] + fa[2] + fa[3]);
        sbyte[] sba = new sbyte[] { 1, 2, 3, 4 };
        Array.Reverse(sba);
        Console.WriteLine("rv_sb=" + sba[0] + sba[1] + sba[2] + sba[3]);
        byte[] bya = new byte[] { 1, 2, 3, 4 };
        Array.Reverse(bya);
        Console.WriteLine("rv_by=" + bya[0] + bya[1] + bya[2] + bya[3]);
        bool[] boa = new bool[] { true, false, true, false };
        Array.Reverse(boa);
        Console.WriteLine("rv_bo=" + boa[0] + boa[1] + boa[2] + boa[3]);
        short[] sha = new short[] { 1, 2, 3, 4 };
        Array.Reverse(sha);
        Console.WriteLine("rv_sh=" + sha[0] + sha[1] + sha[2] + sha[3]);
        ushort[] usa = new ushort[] { 1, 2, 3, 4 };
        Array.Reverse(usa);
        Console.WriteLine("rv_us=" + usa[0] + usa[1] + usa[2] + usa[3]);
        char[] cha = new char[] { 'a', 'b', 'c', 'd' };
        Array.Reverse(cha);
        Console.WriteLine("rv_ch=" + cha[0] + cha[1] + cha[2] + cha[3]);
        uint[] uia = new uint[] { 1, 2, 3, 4 };
        Array.Reverse(uia);
        Console.WriteLine("rv_ui=" + uia[0] + uia[1] + uia[2] + uia[3]);
    }
}
