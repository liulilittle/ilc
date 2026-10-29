//------------------------------------------------------------------------------
// probe-sfieldfloat.cs -- RED/GREEN probe: STATIC double/float field storage
// width.
//
// The transpiler decodes a static field signature through
// MetadataLoader.DecodeExtendedStaticType, which classified ELEMENT_TYPE_R4
// (0x0C) / R8 (0x0D) as FieldTypeKind.Int32 ("the historical int32 value
// slot"). The static storage was therefore emitted as int32_t, so
//
//     static double D = 3.5;  D++;      -> D == 3 (not 4.5)
//     static float  F = 2.5f; F++;      -> F == 3 (not 3.5)
//
// -- the value is truncated to int32 at every store/load. Instance fields and
// locals keep their real width (FieldTypeKind.Float/Double), so this probe
// pins the static-field surface only:
//   * zero default + initializer (.cctor) + readonly initializer
//   * ++ / -- (prefix result value + preserved old value)
//   * compound assignment (+=, -=, *=, /=) with a double and an int operand
//   * cross-method access (helper getter/accumulator)
//   * argument passing and return
//   * exact bit images via BitConverter.DoubleToInt64Bits (no float formatting
//     involved, so a truncation cannot hide behind a decimal rendering)
// The int static field is the control: it must keep the int32_t slot.
//------------------------------------------------------------------------------
using System;

namespace Cases {
public static class SFieldFloat {
    // static field surface
    public static double D;                    // zero default
    public static double DInit = 3.5;          // initializer (.cctor)
    public static readonly double DRO = 1.25;  // readonly initializer
    public static float F;                     // zero default
    public static float FInit = 2.5f;          // initializer (.cctor)
    public static int I = 7;                   // control: int32 slot
    // 64-bit static field: the SAME width-consumer code path (a field read
    // used directly as an arithmetic operand) must keep the full width too.
    public static long SL;

    private static double s_acc;
    private static float s_facc;

    static void AddD(double v) { s_acc += v; }
    static double GetD() { return s_acc; }
    static void AddF(float v) { s_facc += v; }
    static float GetF() { return s_facc; }
    static double Twice(double v) { return v * 2; }
    static float Half(float v) { return v / 2; }

    public static void Main() {
        Console.WriteLine("[zero] {0} {1}", D, F);
        Console.WriteLine("[init] {0} {1} {2}", DInit, FInit, DRO);
        Console.WriteLine("[ctrl-int] {0}", I);

        // ---- increment / decrement ----
        D = 3.5; D++;
        Console.WriteLine("[post-d] {0}", D);
        D = 3.5; double oldD = D++;
        Console.WriteLine("[post-d-old] {0} {1}", oldD, D);
        D = 3.5; D--;
        Console.WriteLine("[dec-d] {0}", D);
        F = 2.5f; F++;
        Console.WriteLine("[post-f] {0}", F);
        F = 2.5f; float oldF = F--;
        Console.WriteLine("[post-f-old] {0} {1}", oldF, F);

        // ---- compound assignment ----
        D = 3.5; D += 0.25;
        Console.WriteLine("[cmp-d-add] {0}", D);
        D = 3.5; D -= 1;
        Console.WriteLine("[cmp-d-sub] {0}", D);
        D = 3.5; D *= 2;
        Console.WriteLine("[cmp-d-mul] {0}", D);
        D = 3.5; D /= 2;
        Console.WriteLine("[cmp-d-div] {0}", D);
        F = 2.5f; F += 0.25f;
        Console.WriteLine("[cmp-f-add] {0}", F);
        F = 2.5f; F -= 0.5f;
        Console.WriteLine("[cmp-f-sub] {0}", F);
        F = 2.5f; F *= 2;
        Console.WriteLine("[cmp-f-mul] {0}", F);

        // ---- direct (un-materialized) field operands ----
        D = 3.5;
        Console.WriteLine("[expr-d-div] {0}", D / 2);
        D = 3.5;
        Console.WriteLine("[expr-d-add] {0}", D + 1);
        F = 2.5f;
        Console.WriteLine("[expr-f-div] {0}", F / 2);
        D = 3.5;
        Console.WriteLine("[expr-d-cmp] {0} {1}", D > 2, D < 4);
        // A non-constant int operand is NOT constant-folded by the compiler
        // (ldsfld; ldloc; conv.r8; div), so the operand width must come from
        // the field's own decoded storage width. k derives from the static int
        // control field so no single-assignment constant fold can erase it.
        int k = I - 5;
        D = 3.5;
        Console.WriteLine("[expr-d-divv] {0}", D / k);
        F = 2.5f;
        Console.WriteLine("[expr-f-divv] {0}", F / k);
        SL = 5000000000L;
        Console.WriteLine("[expr-l-divv] {0}", SL / k);
        // ---- cross-method access ----
        s_acc = 0; AddD(3.5); AddD(1.0);
        Console.WriteLine("[cross-d] {0}", GetD());
        s_facc = 0; AddF(2.5f); AddF(0.25f);
        Console.WriteLine("[cross-f] {0}", GetF());

        // ---- argument / return ----
        Console.WriteLine("[arg-d] {0}", Twice(3.5));
        Console.WriteLine("[arg-f] {0}", Half(2.5f));

        // ---- exact bit images (storage width proof) ----
        D = 3.5;
        Console.WriteLine("[bits-d] {0}", BitConverter.DoubleToInt64Bits(D));
        F = 2.5f;
        Console.WriteLine("[bits-f] {0}", BitConverter.DoubleToInt64Bits((double)F));
        D = 1.25;
        Console.WriteLine("[bits-d2] {0}", BitConverter.DoubleToInt64Bits(DRO + D - 1.25));
    }
}
}
