//------------------------------------------------------------------------------
// probe-floatagg -- RED probe for the b5-gaps float-carrier observation #1:
// the List<T> Aggregate SEED form folds a float accumulator into the double
// seed kernel.
//
// MethodMap.Linq.cs (Aggregate count>=3 arm) classifies the accumulator
// family with `0x0C or 0x0D => "d"`, so
// List<float>.Aggregate(seed, Func<float,float,float>) emits
// il2cpp_linq_list_aggregate_seed_d (the seed rides a double parameter;
// there is no _seed_f list entry). The runtime list wrapper delegates to the
// array kernel FoldD (linq.cpp): acc is a C++ double and every fold step
// calls the user lambda through DBinDFn = double(*)(double,double) -- while
// a Func<float,float,float> leaf is transpiled as C++ `float f(double,double)`
// (4-byte float RETURN per the MS x64 ABI; parameters ride the widened
// double F-stack). Reading that float return through a double fn-pointer
// mixes the callee's XMM0[63:32] residue into every step -- the same
// list-orderkey-float bug class fixed in impl-orderfam with the float-typed
// FAccDFn entries. Even a clean ABI read would still accumulate in double,
// which cannot reproduce net48's per-step R4 rounding.
//
// The probe accumulates precision-sensitive data (0.1f repeated, mixed
// magnitudes where float and double rounding DIVERGE) with a List<double>
// control over the same shapes. The net48 oracle prints value ("R") plus
// raw bit patterns, so a widening carrier or an ABI misread is byte-visible:
// float accumulation must match the oracle EXACTLY (bits included) to pass.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeFloatAgg
    {
        private static string FB(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.ToUInt32(BitConverter.GetBytes(v), 0).ToString(CultureInfo.InvariantCulture);
        }

        private static string DB(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture) + "/" +
                   BitConverter.DoubleToInt64Bits(v).ToString(CultureInfo.InvariantCulture);
        }

        // Receiver-type-preserved shapes: a static helper parameter keeps the
        // List<T> tracked type, so the resolver should reach the LIST domain
        // (il2cpp_linq_list_aggregate_seed_d) instead of falling through to
        // the array family with a raw List object.
        private static float AggF(List<float> l) { return l.Aggregate(0.0f, (acc, x) => acc + x); }
        private static float AggF1(List<float> l) { return l.Aggregate(1.0f, (acc, x) => acc + x); }
        private static double AggD(List<double> l) { return l.Aggregate(0.0, (acc, x) => acc + x); }
        // int/long/no-seed controls: does the List.Aggregate route break for
        // EVERY element family (a route fall-through) or the float one only?
        private static int AggI(List<int> l) { return l.Aggregate(0, (acc, x) => acc + x); }
        private static long AggL(List<long> l) { return l.Aggregate(0L, (acc, x) => acc + x); }
        private static float AggNoSeed(List<float> l) { return l.Aggregate((acc, x) => acc + x); }
        private static int AggNoSeedI(List<int> l) { return l.Aggregate((acc, x) => acc + x); }

        private static List<float> MakeFloatList()
        {
            return new List<float> { 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f };
        }
        private static float AggregateNoSeedFloat() { return MakeFloatList().Aggregate((acc, x) => acc + x); }
        // A REAL float[] (4-byte cells) with the seed form: the array-family
        // arm maps srcFam "f" to il2cpp_linq_aggregate_seed_f_d (a DOUBLE
        // element reader) -- a real float[] has elemSize 4.
        private static float AggArrF(float[] a) { return a.Aggregate(0.0f, (acc, x) => acc + x); }
        private static double AggArrD(double[] a) { return a.Aggregate(0.0, (acc, x) => acc + x); }

        public static void Main()
        {
            // 1) float seed form, precision-sensitive: 0.1f x10 from seed 0.
            List<float> lf = new List<float> { 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f };
            float fa = lf.Aggregate(0.0f, (acc, x) => acc + x);
            Console.WriteLine("f_seed0=" + FB(fa));

            // 2) seed 1.0f (the orderfam observation shape).
            float fa1 = lf.Aggregate(1.0f, (acc, x) => acc + x);
            Console.WriteLine("f_seed1=" + FB(fa1));

            // 3) mixed magnitudes: float rounds (2^24 + 1 is not representable,
            //    the +1 steps vanish), double accumulation carries them.
            List<float> lm = new List<float> { 16777216.0f, 1.0f, 1.0f, 1.0f };
            float ma = lm.Aggregate(0.0f, (acc, x) => acc + x);
            Console.WriteLine("f_mix=" + FB(ma));

            // 4/5/6) double controls over the same shapes.
            List<double> ld = new List<double> { 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1 };
            double da = ld.Aggregate(0.0, (acc, x) => acc + x);
            Console.WriteLine("d_seed0=" + DB(da));

            double da1 = ld.Aggregate(1.0, (acc, x) => acc + x);
            Console.WriteLine("d_seed1=" + DB(da1));

            List<double> ldm = new List<double> { 16777216.0, 1.0, 1.0, 1.0 };
            double dm = ldm.Aggregate(0.0, (acc, x) => acc + x);
            Console.WriteLine("d_mix=" + DB(dm));

            // 7/8/9) helper shapes: receiver type preserved through the
            // parameter local (exercises the List-domain entry).
            Console.WriteLine("h_f_seed0=" + FB(AggF(lf)));
            Console.WriteLine("h_f_seed1=" + FB(AggF1(lf)));
            Console.WriteLine("h_d_seed0=" + DB(AggD(ld)));

            // 10/11/12/13) int/long/no-seed controls: route coverage.
            List<int> li = new List<int> { 1, 2, 3, 4 };
            List<long> ll = new List<long> { 1L, 2L, 3L, 4L };
            Console.WriteLine("h_i_seed=" + AggI(li).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("h_l_seed=" + AggL(ll).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("h_f_noseed=" + FB(AggregateNoSeedFloat()));
            Console.WriteLine("h_i_noseed=" + AggNoSeedI(li).ToString(CultureInfo.InvariantCulture));

            // 14/15) REAL float[]/double[] seed form (array-domain controls).
            float[] af = new float[] { 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f };
            double[] ad = new double[] { 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1 };
            Console.WriteLine("a_f_seed0=" + FB(AggArrF(af)));
            Console.WriteLine("a_d_seed0=" + DB(AggArrD(ad)));

            Console.WriteLine("done");
        }
    }
}
