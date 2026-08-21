using System;

namespace Cases
{
    // Bitwise deep-chain: int32/uint/int64/ulong & | ^ ~ and << >> (incl.
    // negative operands, negative/large shift counts, mask extraction, bit
    // counting loops, unchecked overflow, x&(x-1) trick).
    // All bool outputs (== known values). NOTE: constant expressions are
    // folded at compile time by Roslyn (bitwise ops are computed at compile
    // time), so the runtime path must be carried by local variables -- apart
    // from the explicit unchecked constants, every expression below uses
    // variables, ensuring the IL really contains and/or/xor/not/shl/shr.
    public static class BitOpCase
    {
        public static void Main()
        {
            // ================= int32: & | ^ ~ =================
            int a = 0x0F0F;
            int b = 0x33CC;
            Console.WriteLine((a & b) == 0x030C);        // True  0x0F0F & 0x33CC = 0x030C
            Console.WriteLine((a | b) == 0x3FCF);        // True  0x0F0F | 0x33CC = 0x3FCF
            Console.WriteLine((a ^ b) == 0x3CC3);        // True  0x0F0F ^ 0x33CC = 0x3CC3
            Console.WriteLine((~a) == -3856);            // True  ~0x0F0F = -3856
            Console.WriteLine((~b) == -13261);           // True  ~0x33CC = -13261
            Console.WriteLine((a & 0) == 0);             // True
            Console.WriteLine((a | 0) == a);             // True
            Console.WriteLine((a ^ a) == 0);             // True
            Console.WriteLine((~a & b) == 0x30C0);       // True  ~a=0xF0F0 → &0x33CC = 0x30C0
            Console.WriteLine((a | ~b) == -12481);       // True  0x0F0F | 0xFFFFCC33 = 0xFFFFCF3F = -12481
            Console.WriteLine((~(a | b)) == -16336);     // True  ~0x3FCF = -16336
            Console.WriteLine((~(a & b)) == -781);       // True  ~0x030C = -781
            Console.WriteLine((~(a ^ b)) == -15556);     // True  ~0x3CC3 = -15556

            // ================= int32: << >> shift (runtime shift count) =================
            int s1 = 1;
            Console.WriteLine((s1 << 4) == 16);          // True  1 << 4
            Console.WriteLine((s1 << 32) == 1);          // True  32 & 31 = 0 → 1<<0
            Console.WriteLine((s1 << 33) == 2);          // True  33 & 31 = 1 → 1<<1
            Console.WriteLine((s1 << 63) == int.MinValue); // True  63 & 31 = 31 → 1<<31
            Console.WriteLine((s1 << -1) == int.MinValue); // True  -1 & 31 = 31 → 1<<31
            Console.WriteLine((s1 << -33) == int.MinValue); // True  -33 & 31 = 31 → 1<<31
            int n1 = -8;
            Console.WriteLine((n1 >> 1) == -4);          // True  arithmetic right shift (sign extension)
            Console.WriteLine((n1 >> 31) == -1);         // True  all sign bits
            Console.WriteLine((n1 >> 33) == -4);         // True  33 & 31 = 1 → -8>>1
            Console.WriteLine((n1 >> -1) == -1);         // True  -1 & 31 = 31 → -8>>31
            Console.WriteLine((n1 << 2) == -32);         // True
            Console.WriteLine((n1 << -1) == 0);          // True  -8<<31 = 0 (low 3 bits shifted out)
            int s2 = unchecked((int)0x80000000u);
            Console.WriteLine((s2 >> 4) == unchecked((int)0xF8000000)); // True arithmetic shift right fills 1s
            Console.WriteLine((s2 >> 31) == -1);         // True

            // ================= uint: & | ^ ~ =================
            uint ua = 0x80000000u;
            uint ub = 0x0F0F0000u;
            Console.WriteLine((ua & ub) == 0u);          // True  bit 31 and the low 28 bits do not overlap
            Console.WriteLine((ua | ub) == 0x8F0F0000u); // True
            Console.WriteLine((ua ^ ub) == 0x8F0F0000u); // True  disjoint bit sets -> | == ^
            Console.WriteLine((~ua) == 0x7FFFFFFFu);     // True  ~0x80000000
            Console.WriteLine((~0u) == uint.MaxValue);   // True
            Console.WriteLine((ua & 0xFFu) == 0u);       // True  low 8 bits are 0
            Console.WriteLine((ub | 0xFFu) == 0x0F0F00FFu); // True
            Console.WriteLine((ua ^ 0x80000000u) == 0u); // True  XOR self-inverse
            Console.WriteLine((ub & ~0x0000FFFFu) == 0x0F0F0000u); // True high-bit mask preserved
            Console.WriteLine(((ua | ub) & 0xFFFFu) == 0u); // True low 16 bits are 0

            // ================= uint: << >> shift (shr.un logical right shift) =================
            uint u1 = 0x80000000u;
            Console.WriteLine((u1 >> 31) == 1u);         // True  logical right shift 31 -> 1
            Console.WriteLine((u1 >> 32) == 0x80000000u); // True  32 & 31 = 0 -> original value
            Console.WriteLine((u1 >> 33) == 0x40000000u); // True  33 & 31 = 1 -> 0x40000000
            Console.WriteLine((u1 >> -1) == 1u);         // True  -1 & 31 = 31 -> 0x80000000>>31
            Console.WriteLine((u1 << 1) == 0u);          // True  2^31 << 1 wraps the bit type -> 0
            Console.WriteLine((u1 << 32) == 0x80000000u); // True  32 & 31 = 0
            Console.WriteLine((u1 << 33) == 0u);         // True  33 & 31 = 1 -> 0x80000000<<1
            Console.WriteLine((1u << 31) == 0x80000000u); // True  constant folding (compile time)
            uint u2 = 0x12345678u;
            Console.WriteLine((u2 >> 4) == 0x01234567u); // True  logical right shift
            Console.WriteLine((u2 >> 28) == 0x1u);       // True
            Console.WriteLine((u2 << 12) == 0x45678000u); // True

            // ================= int64: & | ^ ~ =================
            long la = 0x0123456789ABCDEFL;
            long lb = unchecked((long)0xF0F0F0F0F0F0F0F0ul);
            Console.WriteLine((la & 0xFFL) == 0xEFL);    // True  low-byte extraction
            Console.WriteLine((la & lb) == 0x0020406080A0C0E0L); // True
            Console.WriteLine((la | lb) == unchecked((long)0xF1F3F5F7F9FBFDFFul)); // True
            Console.WriteLine((la ^ lb) == unchecked((long)0xF1D3B597795B3D1Ful)); // True
            Console.WriteLine((~la) == -81985529216486896L); // True  ~0x0123456789ABCDEF
            Console.WriteLine((~0L) == -1L);             // True
            long lneg = -0x123456789L;
            Console.WriteLine((lneg & 0xFFL) == 0x77L);  // True  -0x123456789 low byte 0x77
            Console.WriteLine((~lneg) == 4886718344L);   // True  ~(-4886718345) = 4886718344
            long lzero = 0L;
            Console.WriteLine((~lzero) == -1L);          // True

            // ================= int64: << >> shift (shift count < 32, safe zone) =================
            long ls1 = 1L;
            Console.WriteLine((ls1 << 4) == 16L);        // True
            Console.WriteLine((ls1 << 31) == 2147483648L); // True  2^31
            Console.WriteLine((ls1 >> 4) == 0L);         // True
            long lns = -64L;
            Console.WriteLine((lns >> 2) == -16L);       // True  arithmetic right shift
            Console.WriteLine((lns >> 31) == -1L);       // True
            long lhi = unchecked((long)0x8000000000000000ul); // long.MinValue
            // Gap G4: the int64 constant long.MinValue is emitted as
            // -9223372036854775808LL (beyond LLONG_MAX; C++ reads it as ULL, so
            // the value becomes +2^63) -> this line transpiles to False (.NET: True)
            Console.WriteLine((lhi >> 4) == -576460752303423488L); // True 0xF800000000000000
            Console.WriteLine((~lhi) == long.MaxValue);  // True  ~(2^63 negative) = 2^63-1
            Console.WriteLine((lhi & 0xFFFFL) == 0L);    // True  low 16 bits with the high bit set

            // ================= ulong: & | ^ ~ and low-32-bit-value shifts =================
            ulong ua64 = 0xFFFFFFFF00000000ul;
            ulong ub64 = 0x00000000FFFFFFFFul;
            Console.WriteLine((ua64 & ub64) == 0ul);     // True
            Console.WriteLine((ua64 | ub64) == 0xFFFFFFFFFFFFFFFFul); // True
            Console.WriteLine((ua64 ^ ub64) == 0xFFFFFFFFFFFFFFFFul); // True
            Console.WriteLine((~ua64) == 0x00000000FFFFFFFFul); // True
            Console.WriteLine((ulong.MaxValue >> 32) == 0xFFFFFFFFul); // True  constant folding
            ulong ulv = 0x80000000ul;                    // value < 2^32: shr.un truncation path is lossless
            Console.WriteLine((ulv >> 31) == 1ul);       // True
            Console.WriteLine((ulv >> 4) == 0x08000000ul); // True
            ulong ul2 = 0x100000000ul;                   // 2^32, << does not truncate (Shl shifts int64 directly)
            Console.WriteLine((ul2 << 4) == 0x1000000000ul); // True  2^36
            Console.WriteLine((ul2 & 0xFFul) == 0ul);    // True
            Console.WriteLine((ul2 ^ ul2) == 0ul);       // True

            // ================= mask extraction / packing =================
            int h1 = 0x12, h2 = 0x34, h3 = 0x56, h4 = 0x78;
            int packed = (h1 << 24) | (h2 << 16) | (h3 << 8) | h4;
            Console.WriteLine(packed == 0x12345678);     // True  packing
            Console.WriteLine(((packed >> 24) & 0xFF) == 0x12); // True
            Console.WriteLine(((packed >> 16) & 0xFF) == 0x34); // True
            Console.WriteLine(((packed >> 8) & 0xFF) == 0x56);  // True
            Console.WriteLine((packed & 0xFF) == 0x78);  // True
            Console.WriteLine(((packed >> 16) & 0xFFFF) == 0x1234); // True 16-bit mask
            // 64-bit byte split: long shift counts (>= 32) hit the &31 emission gap
            // (G1); the high 32 bits use division instead -- but int64 division has
            // the same gap (G5, Kind lost, emitted as int32) -> the 4 lines below
            // transpile to False
            long lp = 0x0123456789ABCDEFL;
            long low = lp & 0xFFFFFFFFL;
            long hi = lp / 0x100000000L;
            Console.WriteLine((low == 0x89ABCDEFL));     // True  low 32 bits
            Console.WriteLine((hi == 0x01234567L));      // True  high 32 bits
            Console.WriteLine(((low >> 24) & 0xFFL) == 0x89L); // True
            Console.WriteLine(((low >> 16) & 0xFFL) == 0xABL); // True
            Console.WriteLine(((low >> 8) & 0xFFL) == 0xCDL);  // True
            Console.WriteLine((low & 0xFFL) == 0xEFL);   // True
            Console.WriteLine(((hi >> 16) & 0xFFL) == 0x23L); // True
            Console.WriteLine(((hi >> 8) & 0xFFL) == 0x45L);  // True
            Console.WriteLine((hi & 0xFFL) == 0x67L);    // True

            // ================= bit-counting loops (x & (x-1) trick) =================
            int pc1 = 0;
            int v1 = 0x5A5A;
            while (v1 != 0) { v1 &= v1 - 1; pc1++; }
            Console.WriteLine(pc1 == 8);                 // True  0x5A5A has 8 set bits
            int pc2 = 0;
            int v2 = unchecked((int)0x80000001u);
            while (v2 != 0) { v2 &= v2 - 1; pc2++; }
            Console.WriteLine(pc2 == 2);                 // True  0x80000001 has 2 set bits
            uint pc3 = 0;
            uint v3 = 0xFFFFFFFFu;
            while (v3 != 0) { v3 &= v3 - 1; pc3++; }
            Console.WriteLine(pc3 == 32);                // True  uint with all 32 bits set
            int pc4 = 0;
            long v4 = 0x7FFFFFFFFFFFFFFFL;
            while (v4 != 0) { v4 &= v4 - 1; pc4++; }
            Console.WriteLine(pc4 == 63);                // True  long all-ones except the sign bit
            int pc5 = 0;
            int v5 = 0x55555555;
            while (v5 != 0) { pc5 += v5 & 1; v5 >>= 1; }
            Console.WriteLine(pc5 == 16);                // True  per-bit shift counting
            int t12 = 12;
            Console.WriteLine((t12 & (t12 - 1)) == 8);   // True  x&(x-1) clears the lowest set bit
            Console.WriteLine((t12 & (t12 - 1)) < t12);  // True
            int t13 = t12 & (t12 - 1);
            Console.WriteLine((t13 & (t13 - 1)) == 0);   // True  two steps clear it
            int pw2 = 0x4000;
            Console.WriteLine((pw2 & (pw2 - 1)) == 0);   // True  power-of-two check
            int pw3 = 0x4001;
            Console.WriteLine((pw3 & (pw3 - 1)) == 0);   // False

            // ================= overflow checks (unchecked: runtime wrap-around) =================
            int ov1 = 0x40000000;
            Console.WriteLine(ov1 + ov1 == int.MinValue); // True  2^30+2^30 wraps to -2^31
            Console.WriteLine(unchecked(ov1 + ov1) == int.MinValue); // True explicit unchecked
            long ov2 = long.MaxValue;
            Console.WriteLine(ov2 + 1L == long.MinValue); // True  int64 wrap-around
            Console.WriteLine(unchecked((int)0x80000000u) == int.MinValue); // True constant unchecked
            Console.WriteLine(unchecked((long)0xFFFFFFFFFFFFFFFFul) == -1L); // True
            Console.WriteLine(unchecked(int.MinValue * -1) == int.MinValue); // True 2^31 wrap-around
            Console.WriteLine((~ov1 + 1) == -0x40000000); // True  two's-complement negation
            Console.WriteLine((~0x40000000) == -1073741825); // True constant folding

            // ============== known-gap probes (the transpiler does not follow ECMA-335; see .opencode/discover.md) ==============
            // G1: int64/ulong shift counts should use a 6-bit mask (& 63), but the
            //     emitter always applies & 31 (CodeGenerator.cs Shl/Shr path) ->
            //     wrong results for shift counts >= 32 (or negative).
            long gp1 = 1L;
            Console.WriteLine((gp1 << 33) == 8589934592L);  // Gap: .NET 2^33; emitted (1<<(33&31)) = 2
            Console.WriteLine((gp1 << 63) == long.MinValue); // Gap: .NET 2^63; emitted 1<<31 = 2147483648
            long gp2 = -8L;
            Console.WriteLine((gp2 >> 33) == -1L);          // Gap: .NET 33&63=33 -> -1; emitted -8>>1 = -4
            long gp3 = 1L;
            Console.WriteLine((gp3 << -1) == long.MinValue); // Gap: .NET -1&63=63 -> 2^63; emitted -1&31=31 -> 2^31
            // G2: shr.un (ulong >>) truncates the left operand to uint32_t (ShrUn path)
            //     -> wrong result for a logical right shift of a ulong with a
            //     non-zero high 32 bits.
            ulong gp4 = 0x100000002ul;
            Console.WriteLine((gp4 >> 32) == 1ul);          // Gap: .NET (2^32+2)>>32 = 1; emitted (uint32)=2>>0 = 2
            // G3: cgt.un/clt.un (ulong compare) apply the same uint32_t truncation
            //     -> wrong when the high 32 bits participate.
            ulong gp5 = 0x100000000ul;
            Console.WriteLine(gp5 > 0xFFFFFFFFul);          // Gap: .NET 2^32>2^32-1=True; emitted (uint32)0>4294967295=False
            // G4: int64 constant long.MinValue emits -9223372036854775808LL (beyond
            //     LLONG_MAX; C++ reads it as ULL) -> value becomes +2^63, so every
            //     line touching its bit pattern / shift is wrong (see lhi >> 4 above).
            // G5: int64 division loses BinaryExpr.Kind ("l" not propagated) -> Div
            //     emits the int32 branch `(int32_t)a / (int32_t)b` -> a large constant
            //     truncates to 0, i.e. divide-by-zero UB (see the hi 4 lines above).
        }
    }
}
