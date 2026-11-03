using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbePinvokeBoolArray
    {
        // ---- 4-byte native cell (the DEFAULT) ----
        [DllImport("boolarrprobe.dll", EntryPoint = "b4_dump")]
        private static extern int B4Def(bool[] p, int nbytes, int cap, byte[] outBuf);
        [DllImport("boolarrprobe.dll", EntryPoint = "b4_dump")]
        private static extern int B4I4([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4)] bool[] p, int nbytes, int cap, byte[] outBuf);
        [DllImport("boolarrprobe.dll", EntryPoint = "b4_write")]
        private static extern int B4Write(bool[] p, int n);
        [DllImport("boolarrprobe.dll", EntryPoint = "b4_write")]
        private static extern int B4WriteIO([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4), In, Out] bool[] p, int n);

        // ---- 1-byte native cell (U1 / I1) ----
        [DllImport("boolarrprobe.dll", EntryPoint = "b1_dump")]
        private static extern int B1U1([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U1)] bool[] p, int nbytes, int cap, byte[] outBuf);
        [DllImport("boolarrprobe.dll", EntryPoint = "b1_dump")]
        private static extern int B1I1([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I1)] bool[] p, int nbytes, int cap, byte[] outBuf);
        [DllImport("boolarrprobe.dll", EntryPoint = "b1_write")]
        private static extern int B1Write([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U1)] bool[] p, int n);
        [DllImport("boolarrprobe.dll", EntryPoint = "b1_write")]
        private static extern int B1WriteIO([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U1), In, Out] bool[] p, int n);

        [DllImport("boolarrprobe.dll", EntryPoint = "b_isnull")]
        private static extern int IsNull(bool[] p);

        // Read-only seed probes: [Out] must hand the callee a ZEROED buffer.
        // A writing callee cannot show this (it overwrites the whole buffer),
        // so the seed policy needs a probe that only reads -- the same reason
        // the char[]-Ansi case grew ca_peek.
        [DllImport("boolarrprobe.dll", EntryPoint = "b4_peek")]
        private static extern int B4PeekDef(bool[] p, int nbytes, int cap, byte[] outBuf);
        [DllImport("boolarrprobe.dll", EntryPoint = "b4_peek")]
        private static extern int B4PeekOut([Out] bool[] p, int nbytes, int cap, byte[] outBuf);
        [DllImport("boolarrprobe.dll", EntryPoint = "b1_peek")]
        private static extern int B1PeekDef([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U1)] bool[] p, int nbytes, int cap, byte[] outBuf);
        [DllImport("boolarrprobe.dll", EntryPoint = "b1_peek")]
        private static extern int B1PeekOut([MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U1), Out] bool[] p, int nbytes, int cap, byte[] outBuf);

        // A FRESH array per call. net48's copy-back WRITES the managed array, so
        // reusing one instance would let an [Out] call zero the input of a later
        // record -- measured: a shared static array read as all false after an
        // [Out] round-trip, which silently corrupted the expected values.
        private static bool[] Tft() => new bool[] { true, false, true };

        public static void Main()
        {
            byte[] o = new byte[64];

            // --- element width: 4 bytes for the default and I4 ---
            Show("b4.default", B4Def(Tft(), 12, 64, o), o);
            Show("b4.I4", B4I4(Tft(), 12, 64, o), o);

            // --- element width: 1 byte for U1 and I1 ---
            Show("b1.U1", B1U1(Tft(), 3, 64, o), o);
            Show("b1.I1", B1I1(Tft(), 3, 64, o), o);

            // --- null vs empty (only NULL-ness is stable) ---
            Record("bool.null.isnull", IsNull(null));
            Record("bool.empty.isnull", IsNull(new bool[0]));

            // --- SEED policy, observed without writing: [Out] must see zeros ---
            Show("b4.seed.default", B4PeekDef(Tft(), 12, 64, o), o);
            Show("b4.seed.Out", B4PeekOut(Tft(), 12, 64, o), o);
            Show("b1.seed.default", B1PeekDef(Tft(), 3, 64, o), o);
            Show("b1.seed.Out", B1PeekOut(Tft(), 3, 64, o), o);

            // --- copy-back policy, 4-byte element ---
            var d4 = new bool[] { true, false, true };
            Record("b4.write.default.ret", B4Write(d4, d4.Length));
            Record("b4.write.default.e0", d4[0] ? 1 : 0);
            Record("b4.write.default.e1", d4[1] ? 1 : 0);
            Record("b4.write.default.e2", d4[2] ? 1 : 0);

            var io4 = new bool[] { true, false, true };
            Record("b4.write.inout.ret", B4WriteIO(io4, io4.Length));
            Record("b4.write.inout.e0", io4[0] ? 1 : 0);
            Record("b4.write.inout.e1", io4[1] ? 1 : 0);
            Record("b4.write.inout.e2", io4[2] ? 1 : 0);

            // --- copy-back policy, 1-byte element ---
            var d1 = new bool[] { true, false, true };
            Record("b1.write.default.ret", B1Write(d1, d1.Length));
            Record("b1.write.default.e0", d1[0] ? 1 : 0);
            Record("b1.write.default.e1", d1[1] ? 1 : 0);

            var io1 = new bool[] { true, false, true };
            Record("b1.write.inout.ret", B1WriteIO(io1, io1.Length));
            Record("b1.write.inout.e0", io1[0] ? 1 : 0);
            Record("b1.write.inout.e1", io1[1] ? 1 : 0);
            Record("b1.write.inout.e2", io1[2] ? 1 : 0);
        }

        private static void Show(string id, int n, byte[] buf)
        {
            if (n < 0) { Record(id + ".null", 1); return; }
            var s = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) s.Append(buf[i].ToString("X2")).Append(' ');
            Console.WriteLine("[" + id + "]=[" + s.ToString().Trim() + "] n=" + n);
        }

        private static void Record(string id, long value)
        {
            Console.WriteLine("[" + id + "]=" + value);
        }
    }
}
