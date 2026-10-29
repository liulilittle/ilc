//------------------------------------------------------------------------------
// probe-califloat -- RED probe for the b5-gaps float-carrier observation #2:
// the calli (fnptr) result classification folds a float return into the
// double slot.
//
// StackFold.EmitCalli (ResultKind switch: `0x0C or 0x0D => "d"`) and
// CodeGenerator.CalliReturnCpp (`0x0C or 0x0D => "double"`) BOTH treat a
// float (0x0C) calli return as double: the emitted indirect call is
// ((double(*)(...))fnptr)(...). But a float-returning C# method is
// transpiled as a C++ `float` function (4-byte XMM0 return, see the
// zmatrix gen shapes `float Cases_ZMatrix_S_f_...(double)`), so reading it
// through a double fn-pointer mixes the callee's XMM0[63:32] residue into
// the result (the list-orderkey-float bug class). The delegate-Invoke path
// already has the correct precedent: DelegateInvokeResultKind maps
// R4 -> "float" (not "d"), and the FAccDFn runtime entries read
// float-returning leaves through float(*) fn-pointers.
//
// Probe: delegate*<float> / delegate*<float,float> leaves with
// precision-sensitive float arithmetic (1.0f/3.0f, per-step R4 rounding) and
// a multi-use result (which lands in a NAMED temp slot -- the P-D3 callimulti
// pattern that motivated ResultKind), each paired with a delegate*<double>
// control. The net48 oracle prints value ("R") plus raw bits: the float
// results must match the oracle bit-exactly to pass.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class ProbeCalifloat
    {
        static float F0(float x) { return x / 3.0f; }
        static float F1(float x) { return x * 3.0f + 0.1f; }
        static float F2(float a, float b) { return a / b; }
        // Residue maker: a double-returning call leaves non-zero bits in
        // XMM0[63:32]; the float return that follows only rewrites the low
        // 32 bits, so a double fn-pointer read sees the residue.
        static double Res() { return 7.25; }
        static float FR(float x) { Res(); return x / 3.0f; }
        static double D0(double x) { return x / 3.0; }
        static double D1(double x) { return x * 3.0 + 0.1; }

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

        public static unsafe void Main()
        {
            // float calli: single use.
            delegate*<float, float> p0 = &F0;
            float v0 = p0(1.0f);
            Console.WriteLine("c_f0=" + FB(v0));

            // float calli: MULTI-USE result (named temp slot, ResultKind "d").
            float m = p0(1.0f);
            Console.WriteLine("c_f0m=" + FB(m) + "+" + FB(m + m));

            // float calli with float rounding in the body.
            delegate*<float, float> p1 = &F1;
            float v1 = p1(1.0f / 3.0f);
            Console.WriteLine("c_f1=" + FB(v1));
            float v1b = p1(v1);
            Console.WriteLine("c_f1b=" + FB(v1b));

            // two-float-arg calli (params ride the widened double F-stack).
            delegate*<float, float, float> p2 = &F2;
            Console.WriteLine("c_f2=" + FB(p2(1.0f, 3.0f)));

            // residue arm: FR calls a double-returning method first.
            delegate*<float, float> pr = &FR;
            Console.WriteLine("c_fres=" + FB(pr(1.0f)));

            // double controls over the same shapes.
            delegate*<double, double> q0 = &D0;
            double w0 = q0(1.0);
            Console.WriteLine("c_d0=" + DB(w0));
            double wm = q0(1.0);
            Console.WriteLine("c_d0m=" + DB(wm + wm));
            delegate*<double, double> q1 = &D1;
            Console.WriteLine("c_d1=" + DB(q1(1.0 / 3.0)));

            Console.WriteLine("done");
        }
    }
}
