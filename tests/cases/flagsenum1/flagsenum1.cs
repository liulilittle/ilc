//------------------------------------------------------------------------------
// flagsenum1.cs -- [Flags] enum WITH COMPOSITE MEMBERS (the rtflags defect).
//
// net48 semantics: [Flags] is an ATTRIBUTE, not a member shape. A [Flags] enum
// that also declares composite members (AB = A|B, All = A|B|C) must still
// decompose unnamed values into their named bit components:
//   value 5 = A|C      -> "A, C"   (AB=3 is NOT a subset of 5)
//   value 6 = B|C      -> "B, C"
//   value 7 = All      -> "All"    (exact member match wins)
//   value 8            -> "8"      (uncovered bit stays numeric)
//   value -1           -> "-1"
//
// The pre-fix runtime detected flags-capability from the member SHAPE (zero
// member + every non-zero member a power of two). AB=3/All=7 are not powers of
// two, so the whole enum was judged non-flags and every combination printed
// NUMERIC -- a silent wrong value.
//
// The second enum (Nested) is a [Flags] enum declared INSIDE a class: it
// exercises the TypeEmitter.ExpandNestedTypes path, which must carry the same
// attribute bit as a top-level TypeDef.
//------------------------------------------------------------------------------
using System;

namespace Cases {

[Flags]
enum F { None = 0, A = 1, B = 2, C = 4, AB = 3, All = 7 }

public static class FlagsEnum1 {
    [Flags]
    public enum Nested { None = 0, X = 1, Y = 2, XY = 3 }

    static void W(string k, string v) { Console.WriteLine(k + "=[" + v + "]"); }

    public static void Main() {
        // --- ToString() default ("G") ---
        W("ts0", F.None.ToString());
        W("ts1", F.A.ToString());
        W("ts3", F.AB.ToString());
        W("ts5", (F.A | F.C).ToString());
        W("ts6", (F.B | F.C).ToString());
        W("ts7", F.All.ToString());
        W("ts7c", ((F)7).ToString());
        W("ts8", ((F)8).ToString());
        W("tsneg", ((F)(-1)).ToString());

        // --- explicit "G" ---
        W("g5", (F.A | F.C).ToString("G"));
        W("g6", (F.B | F.C).ToString("G"));
        W("g8", ((F)8).ToString("G"));
        W("gneg", ((F)(-1)).ToString("G"));

        // --- explicit "F" (flags decomposition form) ---
        W("f5", (F.A | F.C).ToString("F"));
        W("f6", (F.B | F.C).ToString("F"));
        W("f7", ((F)7).ToString("F"));
        W("f8", ((F)8).ToString("F"));
        W("fneg", ((F)(-1)).ToString("F"));

        // --- "D" decimal / "X" hex are width-only, never decomposed ---
        W("d5", (F.A | F.C).ToString("D"));
        W("dneg", ((F)(-1)).ToString("D"));
        W("x5", (F.A | F.C).ToString("X"));
        W("xneg", ((F)(-1)).ToString("X"));

        // --- boxed Enum reference shape (klass rides on the box) ---
        W("bx5", ((Enum)(F.A | F.C)).ToString());
        W("bx7", ((Enum)(F)7).ToString());
        W("bxf6", ((Enum)(F.B | F.C)).ToString("F"));

        // --- nested [Flags] enum (ExpandNestedTypes path) ---
        W("n3", (Nested.X | Nested.Y).ToString());
        W("nXY", Nested.XY.ToString());
        W("n3f", (Nested.X | Nested.Y).ToString("F"));
        W("done", "1");
    }
} }
