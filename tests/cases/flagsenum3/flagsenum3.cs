//------------------------------------------------------------------------------
// flagsenum3.cs -- NON-[Flags] enum whose member shape LOOKS like flags.
//
// {Zero=0, One=1, Two=2} is exactly the shape the pre-fix runtime used to
// detect "flags capability" (zero member + power-of-two members). net48 says
// otherwise: [Flags] is an attribute. A plain enum NEVER decomposes in the
// DEFAULT form --
//   value 3 -> "3"     (not "One, Two")
//   value 4 -> "4"
//   value 1 -> "One"   (named members still print by name)
//   value 0 -> "Zero"
// Format "F" is different (net48 probe): "F" decomposes for EVERY enum, so
// value 3 with "F" is "One, Two" while the default ToString is "3".
//------------------------------------------------------------------------------
using System;

namespace Cases {

enum P { Zero = 0, One = 1, Two = 2 }

public static class FlagsEnum3 {
    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        W("ts0", P.Zero.ToString());
        W("ts1", P.One.ToString());
        W("ts2", P.Two.ToString());
        W("ts3", ((P)3).ToString());
        W("ts4", ((P)4).ToString());
        W("ts7", ((P)7).ToString());
        W("tsneg", ((P)(-1)).ToString());

        W("g3", ((P)3).ToString("G"));
        W("g4", ((P)4).ToString("G"));
        W("f3", ((P)3).ToString("F"));
        W("f7", ((P)7).ToString("F"));
        W("f1", P.One.ToString("F"));
        W("fneg", ((P)(-1)).ToString("F"));
        W("d3", ((P)3).ToString("D"));
        W("x3", ((P)3).ToString("X"));

        W("bx3", ((Enum)(P)3).ToString());
        W("bxf3", ((Enum)(P)3).ToString("F"));
        W("done", "1");
    }
} }
