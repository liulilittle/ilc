// bitprobe.cs — bitwise instruction family (and/or/xor/shl/shr/shr.un) net48 evidence.
// Key points: shift-count masking (int32 masked with &31, int64 masked with &63), shr arithmetic (sign extension) / shr.un logical.
using System;

class BitProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pi(string label, int v) => P(label + " = " + v);
    static void Pl(string label, long v) => P(label + " = " + v);
    static void Pu(string label, ulong v) => P(label + " = " + v);

    static int N1 = -1;
    static int IMIN = int.MinValue;      // 0x80000000
    static int IMIN1 = int.MinValue + 1; // 0x80000001
    static long LMIN = long.MinValue;    // 0x8000000000000000
    static long Lm1 = -1L;               // 0xFFFFFFFFFFFFFFFF

    static void Main()
    {
        // ---- and/or/xor ----
        Pi("and: 0x0F & 0x33", 0x0F & 0x33);
        Pi("or: 0x0F | 0x30", 0x0F | 0x30);
        Pi("xor: 0x0F ^ 0x33", 0x0F ^ 0x33);
        Pi("and: -1 & 0xFF", N1 & 0xFF);          // 255
        Pi("xor: x ^ x = 0", 12345 ^ 12345);
        Pi("and: x & 0 = 0", 12345 & 0);

        // ---- shl (wraparound, no unsigned overflow check) ----
        Pi("shl: 1 << 31", 1 << 31);              // int.MinValue (wraparound)
        Pi("shl: 1 << 30", 1 << 30);

        // ---- shr (arithmetic right shift: sign extension for negatives) ----
        Pi("shr: 0x80000000 >> 1 [arith]", IMIN >> 1);   // 0xC0000000 = -1073741824
        Pi("shr: 0x80000001 >> 1 [arith]", IMIN1 >> 1);  // 0xC0000000
        Pi("shr: -1 >> 5 [arith]", N1 >> 5);             // -1

        // ---- shr.un (logical right shift: high bits filled with 0) ----
        Pi("shr.un: (uint)0x80000000 >> 1", (int)(((uint)IMIN) >> 1));  // 0x40000000 = 1073741824
        Pi("shr.un: (uint)-1 >> 8", (int)(((uint)N1) >> 8));            // 0x00FFFFFF = 16777215

        // ---- shift-count masking (int32 & 31) ----
        Pi("shl: 1 << 33 [33&31=1]", 1 << 33);           // 2
        Pi("shl: 1 << -1 [-1&31=31]", 1 << -1);          // int.MinValue
        Pi("shl: 1 << -33 [-33&31=31]", 1 << -33);       // int.MinValue
        Pi("shr: 0x80000000 >> 33 [33&31=1]", IMIN >> 33); // arithmetic shift by 1 bit = 0xC0000000

        // ---- int64 (count masked with &63) ----
        Pl("shl: 1L << 63", 1L << 63);                   // long.MinValue
        Pl("shl: 1L << 65 [65&63=1]", 1L << 65);         // 2
        Pl("shr: 0x8000000000000000 >> 63 [arith]", LMIN >> 63);   // -1
        Pu("shr.un: (ulong)0x8000000000000000 >> 63", (ulong)LMIN >> 63); // 1
        Pl("shr.un: (ulong)-1L >> 62", (long)((ulong)Lm1 >> 62));        // 3
        Pl("shl: 3L << 62 [wraparound]", 3L << 62);            // 0xC000000000000000 = -4611686018427387904

        // ---- shr.un vs shr on int64 comparison ----
        Pu("shr.un: (ulong)0xFFFFFFFFFFFFFFFF >> 4", (ulong)Lm1 >> 4);    // 0x0FFFFFFFFFFFFFFF
        Pl("shr: -1L >> 4 [arith]", Lm1 >> 4);                             // -1
    }
}
