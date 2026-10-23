//------------------------------------------------------------------------------
// flagsenum2.cs -- [Flags] enum with NO composite members (regression guard).
//
// Canonical shape {None=0, A=1, B=2, C=4, D=8}. This already worked before the
// attribute-bit fix (the shape heuristic happened to classify it correctly);
// it must KEEP working after IsFlagsCapable reads the real [Flags] bit.
//------------------------------------------------------------------------------
using System;

namespace Cases {

[Flags]
enum G { None = 0, A = 1, B = 2, C = 4, D = 8 }

public static class FlagsEnum2 {
    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        W("ts0", G.None.ToString());
        W("ts1", G.A.ToString());
        W("ts3", (G.A | G.B).ToString());
        W("ts5", (G.A | G.C).ToString());
        W("ts7", (G.A | G.B | G.C).ToString());
        W("ts15", (G.A | G.B | G.C | G.D).ToString());
        W("ts16", ((G)16).ToString());
        W("tsneg", ((G)(-1)).ToString());

        W("g5", (G.A | G.C).ToString("G"));
        W("g15", (G.A | G.B | G.C | G.D).ToString("G"));
        W("f5", (G.A | G.C).ToString("F"));
        W("f7", (G.A | G.B | G.C).ToString("F"));
        W("f16", ((G)16).ToString("F"));
        W("fneg", ((G)(-1)).ToString("F"));
        W("d5", (G.A | G.C).ToString("D"));
        W("x15", (G.A | G.B | G.C | G.D).ToString("X"));

        W("bx3", ((Enum)(G.A | G.B)).ToString());
        W("bxf5", ((Enum)(G.A | G.C)).ToString("F"));
        W("done", "1");
    }
} }
