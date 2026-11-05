//------------------------------------------------------------------------------
// probe-pinvoke-shapes.cs -- P/Invoke array-shape survey (research probe):
// iteration 1 (GREEN, byte-exact): byte[,] / double[,] multi-dim struct-cell
// array params and a struct-element array (ProbeS[]) across a DllImport
// boundary. Later iterations measured in .opencode/logs: string[] / class[] /
// object[] are LOUD-rejected (CodeGenerator.cs PinvokeArrayElementCpp).
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Cases
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeS
    {
        public int A;
        public double B;
    }

    public static class ProbePinvokeShapes
    {
        [DllImport("mylib.dll", EntryPoint = "pshape_addr")]
        private static extern IntPtr pshape_addr_b(byte[,] a);
        [DllImport("mylib.dll", EntryPoint = "pshape_isnull")]
        private static extern int pshape_isnull_b(byte[,] a);
        [DllImport("mylib.dll")]
        private static extern int pshape_bdump(byte[,] a, int n, byte[] outp);
        [DllImport("mylib.dll")]
        private static extern int pshape_bstore(byte[,] a, int n);

        [DllImport("mylib.dll", EntryPoint = "pshape_addr")]
        private static extern IntPtr pshape_addr_d(double[,] a);
        [DllImport("mylib.dll", EntryPoint = "pshape_isnull")]
        private static extern int pshape_isnull_d(double[,] a);
        [DllImport("mylib.dll")]
        private static extern int pshape_ddump(double[,] a, int n, double[] outp);

        [DllImport("mylib.dll")]
        private static extern int pshape_slen(ProbeS[] a, int n);
        [DllImport("mylib.dll")]
        private static extern int pshape_sdump(ProbeS[] a, int n, double[] outp);
        [DllImport("mylib.dll")]
        private static extern int pshape_sstore(ProbeS[] a, int n);

        private static void Record(string id, int value)
        {
            Console.WriteLine("[" + id + "]=" + value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Record(string id, long value)
        {
            Console.WriteLine("[" + id + "]=" + value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Record(string id, string value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }

        private static string HexByte(byte[] buf, int n)
        {
            var s = "";
            for (var i = 0; i < n; i++)
            {
                if (i > 0) s += ",";
                s += buf[i].ToString("X2", CultureInfo.InvariantCulture);
            }
            return s;
        }

        private static string JoinD(double[] buf, int n)
        {
            var s = "";
            for (var i = 0; i < n; i++)
            {
                if (i > 0) s += ",";
                s += buf[i].ToString("R", CultureInfo.InvariantCulture);
            }
            return s;
        }

        public static void Main()
        {
            // ---- byte[,] : flat row-major, pin semantics, null ----
            var mb = new byte[2, 2] { { 1, 2 }, { 3, 4 } };
            var ob = new byte[8];
            Record("b.n", pshape_bdump(mb, 4, ob));
            Record("b.dump", HexByte(ob, 4));
            Record("b.addr.nonzero", pshape_addr_b(mb) != IntPtr.Zero ? 1 : 0);
            Record("b.ret", pshape_bstore(mb, 4));
            Record("b.c00", mb[0, 0]);
            Record("b.c01", mb[0, 1]);
            Record("b.c11", mb[1, 1]);
            Record("b.null.isnull", pshape_isnull_b(null));

            // ---- double[,] : flat row-major, null ----
            var md = new double[2, 2] { { 1.5, 2.5 }, { 3.5, 4.5 } };
            var od = new double[8];
            Record("d.n", pshape_ddump(md, 4, od));
            Record("d.dump", JoinD(od, 4));
            Record("d.addr.nonzero", pshape_addr_d(md) != IntPtr.Zero ? 1 : 0);
            Record("d.null.isnull", pshape_isnull_d(null));

            // ---- ProbeS[] : struct-element array, copy-in + copy-back ----
            var s = new ProbeS[] { new ProbeS { A = 1, B = 0.25 }, new ProbeS { A = 10, B = 1.5 } };
            Record("s.len", pshape_slen(s, 2));
            var os = new double[8];
            Record("s.n", pshape_sdump(s, 2, os));
            Record("s.dump", JoinD(os, 2));
            Record("s.ret", pshape_sstore(s, 2));
            Record("s.a0", s[0].A);
            Record("s.b0", (long)(s[0].B * 100));
            Record("s.a1", s[1].A);
            Record("s.b1", (long)(s[1].B * 100));
        }
    }
}