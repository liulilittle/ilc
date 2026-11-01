//------------------------------------------------------------------------------
// pinvoke-structret2.cs -- STATIC-LINK by-value struct DllImport RETURNS.
//
// Predecessor case pinvoke-structreturn covered the DYNAMIC (SHARED library,
// resolved through il2cpp_pinvoke_resolve) struct-return path. This case
// covers the STATIC-direct path (the native archive is linked into
// il2cpp_gen_exe and the call is a direct extern "C" call) and the RESULT
// TEMP-SLOT shape:
//
//   * a struct return assigned to a managed local (IL local signature names
//     the value type -- the green baseline);
//   * a struct return flowing into a COMPILER temp (passed by value to a user
//     method, passed by value to another native entry, or one of two calls in
//     a single expression) -- the temp must be declared as the struct, not
//     int32_t (an int64 field would only receive its low 32 bits);
//   * direct `call().field` projection;
//   * enum returns stay the int32 native ABI (NOT the 16-byte generated enum
//     struct: naming it makes the C++ caller pass a hidden sret pointer while
//     a native int32 callee returns in EAX).
//
// Ret2Mixed { int32, int64 } is the truncation sensor: its int64 field sits
// after a 4-byte field (8 bytes of padding), so a 32-bit result slot cannot
// carry it and any wrong carrier is visible in the bytes, not only at build.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public struct Ret2Pair
    {
        public int X;
        public int Y;
    }

    public struct Ret2Wide
    {
        public long V;
    }

    public struct Ret2Mixed
    {
        public int Tag;
        public long Big;
    }

    public enum Ret2Mode
    {
        None = 0,
        One = 1,
        Two = 2,
        Three = 3,
        Five = 5
    }

    public static class PinvokeStructRet2
    {
        public static class Native
        {
            [DllImport("structret2stat")]
            public static extern Ret2Pair mk_pair(int seed);
            [DllImport("structret2stat")]
            public static extern Ret2Wide mk_wide(long seed);
            [DllImport("structret2stat")]
            public static extern Ret2Mixed mk_mixed(int seed);
            [DllImport("structret2stat")]
            public static extern Ret2Mode mk_mode(int seed);

            [DllImport("structret2stat")]
            public static extern int sum_pair(Ret2Pair p);
            [DllImport("structret2stat")]
            public static extern long sum_wide(Ret2Wide w);
            [DllImport("structret2stat")]
            public static extern long sum_mixed(Ret2Mixed m);
        }

        // Struct-typed parameters keep these OUT of the inline pass (its gate
        // rejects struct params), so calling them with a P/Invoke result
        // FORCES that result through a real compiler temp slot.
        private static int PairCode(Ret2Pair p) => p.X * 1000 + p.Y;

        private static long WideCode(Ret2Wide w) => w.V ^ 0x1122334455667788L;

        private static long MixedCode(Ret2Mixed m) => (long)m.Tag * 1000000 + (m.Big & 0x7fffffff);

        public static void Main()
        {
            // Baseline: the IL local signature names the struct, so the slot is
            // authoritative even without the call-result kind.
            Ret2Pair p = Native.mk_pair(3);
            Console.WriteLine("[s2.pair.loc]=" + p.X + "," + p.Y);

            // Direct projection off the call (a single-use temp folds away).
            Console.WriteLine("[s2.pair.direct]=" + Native.mk_pair(5).X);

            // TEMP SLOT: the result feeds a user method that takes the struct
            // by value (not inlined -- struct-param gate).
            Console.WriteLine("[s2.pair.temp]=" + PairCode(Native.mk_pair(7)));

            // int64 field: local, direct projection, and the temp slot.
            Ret2Wide w = Native.mk_wide(0x0102030405060708L);
            Console.WriteLine("[s2.wide.loc]=" + w.V);
            Console.WriteLine("[s2.wide.direct]=" + Native.mk_wide(0x1112131415161718L).V);
            Console.WriteLine("[s2.wide.temp]=" + WideCode(Native.mk_wide(0x0102030405060708L)));

            // 16-byte aggregate with an int64 field after an int32 field: a
            // 32-bit carrier loses Big entirely.
            Ret2Mixed m = Native.mk_mixed(9);
            Console.WriteLine("[s2.mixed.loc]=" + m.Tag + "," + m.Big);
            Console.WriteLine("[s2.mixed.direct]=" + Native.mk_mixed(13).Big);
            Console.WriteLine("[s2.mixed.temp]=" + MixedCode(Native.mk_mixed(11)));

            // Enum return regression: the native ABI is the int32 backing.
            Console.WriteLine("[s2.mode.direct]=" + (int)Native.mk_mode(5));
            Ret2Mode mode = Native.mk_mode(3);
            Console.WriteLine("[s2.mode.loc]=" + (int)mode);

            // TEMP SLOT feeding another native entry: the struct result is a
            // by-value argument on the static-direct call.
            Console.WriteLine("[s2.native.pair]=" + Native.sum_pair(Native.mk_pair(17)));
            Console.WriteLine("[s2.native.wide]=" + Native.sum_wide(Native.mk_wide(100)));
            Console.WriteLine("[s2.native.mixed]=" + Native.sum_mixed(Native.mk_mixed(19)));

            // Two struct-returning calls in one expression: both results live
            // in compiler temps until the addition consumes them.
            Console.WriteLine("[s2.pair.two]=" + (Native.mk_pair(21).X + Native.mk_pair(22).Y));
        }
    }
}
