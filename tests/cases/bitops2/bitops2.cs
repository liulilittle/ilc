//------------------------------------------------------------------------------
// bitops2.cs -- bit-manipulation domain DEEP probe 2 (e2e batch C46):
// runtime shift counts / mask extraction / Interlocked atomics combined with
// bit ops -- byte-exact vs the net48 oracle. Created after the bit domain
// (bitopcase) and the Interlocked value-semantics domain (interlockedcase)
// both landed.
//
// Wired surface exercised (bitopcase/interlockedcase precedents):
//   * int32/uint << >> with RUNTIME shift counts (variables, not constants --
//     Roslyn folds constant shifts at compile time, so every probe below
//     carries the shift in a local to force real shl/shr IL). Shift-count
//     masking follows ECMA &31: count 32 -> 0, 33 -> 1, -1 -> 31.
//   * arithmetic right shift (int >>, sign-extends) vs logical right shift
//     (uint >>, zero-fills).
//   * mask extraction / bitfield pack-unpack on int32.
//   * System.Threading.Interlocked int32 + int64 atomics (increment /
//     decrement / add / exchange / compare-exchange) driving bit operations:
//     atomic bit-set, atomic field-clear, CAS-gated set (compare-exchange
//     round trip), and the "CAS succeeds only when the slot still holds the
//     observed value" semantics -- all single-threaded (threading is NOT a
//     wired surface, interlockedcase precedent), so every result is
//     deterministic.
//
// Known-gap discipline (bitopcase recorded gaps G1-G5, NOT probed here):
//   G1: int64/ulong shift counts >= 32 (or negative) -- the emitter masks
//       &31 instead of &63 -> wrong result. Probe int64 shifts ONLY with
//       counts in [0, 31].
//   G2: shr.un (ulong >>) truncates the left operand to uint32 -> wrong for
//       a ulong with non-zero high 32 bits. Probe ulong shifts ONLY with
//       values < 2^32.
//   G3: cgt.un/clt.un (ulong >/</>=/<=) truncates to uint32. Probe ulong
//       comparisons ONLY with values < 2^32.
//   G4: the long.MinValue literal emits -9223372036854775808LL (beyond
//       LLONG_MAX) -> wrong bit pattern. No long.MinValue/MaxValue literals.
//   G5: int64 division loses the "l" Kind -> emits int32 division. No int64
//       division.
//   Interlocked(ref object) / (ref float/double) overloads are LOUD-rejected
//   (pointer/floating atomics have no runtime icall) -- int32/int64 only.
//------------------------------------------------------------------------------
using System;
using System.Threading;
namespace Cases { public static class BitOps2 {

    public static void Main()
    {
        // ================= S1: runtime shift counts (int32) =================
        int one = 1;
        int c0 = 0, c1 = 1, c2 = 2, c3 = 3, c4 = 4;
        Console.WriteLine((one << c0) == 1);          // True
        Console.WriteLine((one << c1) == 2);          // True
        Console.WriteLine((one << c4) == 16);         // True
        Console.WriteLine((one << 16) == 65536);      // True (const, safe fold)
        int c16 = 16;
        Console.WriteLine((one << c16) == 65536);     // True
        // &31 masking: 32 -> 0, 33 -> 1, 63 -> 31.
        int c32 = 32, c33 = 33, c63 = 63;
        Console.WriteLine((one << c32) == 1);         // True 32 & 31 = 0
        Console.WriteLine((one << c33) == 2);         // True 33 & 31 = 1
        Console.WriteLine((one << c63) == int.MinValue); // True 63 & 31 = 31 -> 1<<31
        int cn1 = -1, cn33 = -33;
        Console.WriteLine((one << cn1) == int.MinValue); // True -1 & 31 = 31
        Console.WriteLine((one << cn33) == int.MinValue); // True -33 & 31 = 31

        // ================= S2: arithmetic vs logical right shift =================
        int neg8 = -8;
        Console.WriteLine((neg8 >> c1) == -4);        // True arithmetic (sign-extend)
        Console.WriteLine((neg8 >> c3) == -1);        // True all sign bits
        uint u8000 = 0x80000000u;
        Console.WriteLine((u8000 >> c31_()) == 1u);   // True logical (zero-fill)
        uint uf = 0xF0000000u;
        Console.WriteLine((uf >> c4) == 0x0F000000u); // True logical
        Console.WriteLine((uf >> 28) == 0xFu);        // True const fold

        // ================= S3: mask extraction / bitfield pack-unpack =================
        int packed = 0x12345678;
        Console.WriteLine((packed & 0xFF) == 0x78);       // True
        Console.WriteLine(((packed >> 8) & 0xFF) == 0x56);   // True
        Console.WriteLine(((packed >> 16) & 0xFF) == 0x34);  // True
        Console.WriteLine(((packed >> 24) & 0xFF) == 0x12);  // True
        Console.WriteLine(((packed >> 4) & 0xF) == 0x7);     // True nibble
        Console.WriteLine((packed & 0xFFFF) == 0x5678);      // True halfword
        // bitfield set / clear / test via shifts + masks.
        int field = 0;
        int bit3 = 3;
        field |= (1 << bit3);                            // set bit 3
        Console.WriteLine((field & (1 << bit3)) != 0);   // True
        field &= ~(1 << bit3);                           // clear bit 3
        Console.WriteLine((field & (1 << bit3)) == 0);   // True
        field ^= (1 << bit3);                            // toggle
        Console.WriteLine((field & (1 << bit3)) != 0);   // True
        // mask built from a runtime bit count: (1 << n) - 1.
        int n5 = 5;
        Console.WriteLine(((1 << n5) - 1) == 31);        // True
        int n8 = 8;
        Console.WriteLine(((1 << n8) - 1) == 255);       // True
        // x & (x - 1) lowest-set-bit clear; x & -x isolates it.
        int v12 = 12;
        Console.WriteLine((v12 & (v12 - 1)) == 8);       // True
        Console.WriteLine((v12 & -v12) == 4);            // True
        int v2 = 0x4000;
        Console.WriteLine((v2 & -v2) == 0x4000);         // True power-of-two

        // ================= S4: int64 shifts (counts in [0,31] -- G1-safe) =================
        long lone = 1L;
        int lc2 = 2, lc4 = 4, lc8 = 8, lc31 = 31;
        Console.WriteLine((lone << lc4) == 16L);         // True
        Console.WriteLine((lone << lc8) == 256L);        // True
        Console.WriteLine((lone << lc31) == 2147483648L);// True 2^31
        long lneg = -64L;
        Console.WriteLine((lneg >> lc2) == -16L);        // True arithmetic
        long lv = 0x100000000L;                          // 2^32, shifts with count < 32 safe
        Console.WriteLine((lv >> lc4) == 0x010000000L);  // True
        Console.WriteLine((lv << lc2) == 0x400000000L);  // True

        // ================= S5: ulong (value < 2^32 -- G2/G3-safe) =================
        ulong uv = 0x80000000ul;
        Console.WriteLine((uv >> lc31) == 1ul);          // True logical
        Console.WriteLine((uv >> lc4) == 0x08000000ul);  // True
        ulong uSmall1 = 0xFFFFFFFFul;
        ulong uSmall2 = 0x10000000ul;
        Console.WriteLine(uSmall1 > uSmall2);            // True (values < 2^32)
        Console.WriteLine(uSmall2 < uSmall1);            // True

        // ================= S6: Interlocked int32 + bit ops =================
        // Atomic bit-set via Exchange (returns the OLD value).
        int flags = 0;
        Console.WriteLine(Interlocked.Exchange(ref flags, 1 << 4) == 0); // True old=0
        Console.WriteLine((flags & (1 << 4)) != 0);                      // True
        // Atomic field-clear via CompareExchange gate.
        int cur = flags;
        Console.WriteLine(Interlocked.CompareExchange(ref flags, 0, cur) == cur); // True CAS success
        Console.WriteLine(flags == 0);                                   // True
        // CAS FAILS when the slot no longer holds the observed value.
        int ccur = flags;
        flags = flags | (1 << 1);                        // mutate the slot behind the observation
        Console.WriteLine(Interlocked.CompareExchange(ref flags, 0, ccur) == ccur); // False (slot changed)
        Console.WriteLine((flags & (1 << 1)) != 0);      // True unchanged
        // Interlocked.Increment / Add as a bit-counter.
        int counter = 0;
        Console.WriteLine(Interlocked.Increment(ref counter) == 1);      // True
        Console.WriteLine(Interlocked.Add(ref counter, 1 << 3) == 9);    // True 1 + 8
        Console.WriteLine((counter & 0xF) == 9);                         // True
        Console.WriteLine(Interlocked.Decrement(ref counter) == 8);      // True
        // Interlocked.Add with a mask-derived addend.
        int addend = (1 << 2) | (1 << 4);                // 4 | 16 = 20
        Console.WriteLine(Interlocked.Add(ref counter, addend) == 28);   // True 8 + 20
        Console.WriteLine((counter & 0x1F) == 28);                       // True

        // ================= S7: Interlocked int64 + bit ops =================
        long lflags = 0L;
        Console.WriteLine(Interlocked.Exchange(ref lflags, 1L << 8) == 0L); // True old=0
        Console.WriteLine((lflags & (1L << 8)) != 0L);                      // True
        Console.WriteLine(Interlocked.CompareExchange(ref lflags, 0L, 1L << 8) == (1L << 8)); // True
        Console.WriteLine(lflags == 0L);                                    // True
        long lcounter = 0L;
        Console.WriteLine(Interlocked.Increment(ref lcounter) == 1L);       // True
        Console.WriteLine(Interlocked.Add(ref lcounter, 0x100000000L) == 0x100000001L); // True 1 + 2^32
        Console.WriteLine((lcounter >> 32) == 1L);                          // True high word = 1
        Console.WriteLine((lcounter & 0xFFFFFFFFL) == 1L);                  // True low word = 1
    }

    // A "runtime" shift count of 31 returned through a method (not a constant).
    private static int c31_() { return 31; }
}}
