//------------------------------------------------------------------------------
// probe-i64seed -- RED probe for the emitter locKind int64-parameter gap
// (the floatfix 7b0ee161 residual: only the "d"/"f" arms were seeded).
//
// The emitter's locKind seeding (CodeGenerator.EmitBody param loop) maps
// every by-value parameter kind EXCEPT "l"/"q" (int64/uint64). An unseeded
// long parameter falls to the "val" default, so ExprIsInt64(LocExpr arg)
// is FALSE and every int32/int64-ambiguous binary path folds at INT32
// width:
//   * a / b over two long params -> il2cpp_checked_div(int32_t, int32_t)
//     (the helper takes int32 args -> implicit int64->int32 truncation);
//   * a % b -> il2cpp_checked_rem, same truncation;
//   * a << n / a >> n with a param shift count -> the 5-bit (31) mask,
//     never the int64 6-bit (63) mask;
//   * (ulong)a >> n -> the uint32 cast + 5-bit mask (bitopcase G2/G5
//     class, but for PARAMETERS the IL local kinds do not cover the slot);
//   * (ulong)a > (ulong)b -> the uint32 compare (bitopcase G3 class);
//   * long a + long b (unchecked) -> raw C++ add without the uint64 wrap.
// All shapes only fire for param-vs-param operands: a long LOCAL is
// declared from the IL local signature (authoritative "l"), and a long
// constant operand (Int64ConstExpr) is recognized directly, so the
// existing probes (bitopcase/int64deep) passed despite the gap.
//
// Probe: static methods with long/ulong parameters carrying values beyond
// the int32 range through div/rem/shifts/unsigned compares/unchecked add
// and sub. The net48 oracle prints each result; the runtime must match
// byte-for-byte.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ProbeI64Seed
    {
        static long DivLL(long a, long b) { return a / b; }
        static long RemLL(long a, long b) { return a % b; }
        static long AddLL(long a, long b) { return unchecked(a + b); }
        static long SubLL(long a, long b) { return unchecked(a - b); }
        static long ShlLL(long a, int n) { return a << n; }
        static long ShrLL(long a, int n) { return a >> n; }
        static long ShrULL(ulong a, int n) { return unchecked((long)(a >> n)); }
        static bool GtULL(ulong a, ulong b) { return a > b; }
        static bool LtULL(ulong a, ulong b) { return a < b; }
        static bool EqNegOne(long a) { return a == -1L; }
        static long MergeLL(bool pick, long a, long b) { long m = pick ? a : b; return m / 3L; }

        public static void Main()
        {
            long big = 0x100000000L;          // 2^32 -- beyond int32
            long big2 = 0x300000000L;         // 3 * 2^32

            Console.WriteLine("div=" + DivLL(big2, 7L));
            Console.WriteLine("div2=" + DivLL(big, 0x100000001L));
            Console.WriteLine("rem=" + RemLL(big2, 0x7FFFFFFFFL));
            Console.WriteLine("add=" + AddLL(big, big2));
            Console.WriteLine("sub=" + SubLL(big2, big));
            Console.WriteLine("shl=" + ShlLL(1L, 33));
            Console.WriteLine("shl2=" + ShlLL(big, 2));
            Console.WriteLine("shr=" + ShrLL(-8589934592L, 2));
            Console.WriteLine("shru=" + ShrULL(0xFFFFFFFFFFFFFFFFUL, 1));
            Console.WriteLine("shru2=" + ShrULL(0x1FFFFFFFFUL, 4));
            Console.WriteLine("gtu=" + GtULL(0x100000000UL, 1UL));
            Console.WriteLine("gtu2=" + GtULL(1UL, 0x100000000UL));
            Console.WriteLine("ltu=" + LtULL(0x8000000000000000UL, 1UL));
            Console.WriteLine("eqneg=" + EqNegOne(0x1FFFFFFFFL));
            Console.WriteLine("eqneg2=" + EqNegOne(-1L));
            Console.WriteLine("merge=" + MergeLL(true, big, 5L));
            Console.WriteLine("merge2=" + MergeLL(false, big, 5L));
            Console.WriteLine("done");
        }
    }
}
