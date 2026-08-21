// bitprobe.cs — 位运算族（and/or/xor/shl/shr/shr.un）net48 实证。
// 关键：移位计数掩码（int32 掩 &31、int64 掩 &63）、shr 算术（符号扩展）/ shr.un 逻辑。
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

        // ---- shl（回绕，无符号溢出检查）----
        Pi("shl: 1 << 31", 1 << 31);              // int.MinValue（回绕）
        Pi("shl: 1 << 30", 1 << 30);

        // ---- shr（算术右移：负数符号扩展）----
        Pi("shr: 0x80000000 >> 1 [算术]", IMIN >> 1);   // 0xC0000000 = -1073741824
        Pi("shr: 0x80000001 >> 1 [算术]", IMIN1 >> 1);  // 0xC0000000
        Pi("shr: -1 >> 5 [算术]", N1 >> 5);             // -1

        // ---- shr.un（逻辑右移：高位补 0）----
        Pi("shr.un: (uint)0x80000000 >> 1", (int)(((uint)IMIN) >> 1));  // 0x40000000 = 1073741824
        Pi("shr.un: (uint)-1 >> 8", (int)(((uint)N1) >> 8));            // 0x00FFFFFF = 16777215

        // ---- 移位计数掩码（int32 & 31）----
        Pi("shl: 1 << 33 [33&31=1]", 1 << 33);           // 2
        Pi("shl: 1 << -1 [-1&31=31]", 1 << -1);          // int.MinValue
        Pi("shl: 1 << -33 [-33&31=31]", 1 << -33);       // int.MinValue
        Pi("shr: 0x80000000 >> 33 [33&31=1]", IMIN >> 33); // 算术移 1 位 = 0xC0000000

        // ---- int64（计数掩 &63）----
        Pl("shl: 1L << 63", 1L << 63);                   // long.MinValue
        Pl("shl: 1L << 65 [65&63=1]", 1L << 65);         // 2
        Pl("shr: 0x8000000000000000 >> 63 [算术]", LMIN >> 63);   // -1
        Pu("shr.un: (ulong)0x8000000000000000 >> 63", (ulong)LMIN >> 63); // 1
        Pl("shr.un: (ulong)-1L >> 62", (long)((ulong)Lm1 >> 62));        // 3
        Pl("shl: 3L << 62 [回绕]", 3L << 62);            // 0xC000000000000000 = -4611686018427387904

        // ---- 移位对 int64 的 shr.un 与 shr 对照 ----
        Pu("shr.un: (ulong)0xFFFFFFFFFFFFFFFF >> 4", (ulong)Lm1 >> 4);    // 0x0FFFFFFFFFFFFFFF
        Pl("shr: -1L >> 4 [算术]", Lm1 >> 4);                             // -1
    }
}
