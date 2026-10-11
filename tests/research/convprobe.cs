// convprobe.cs — conversion family (conv.i/u/i1..i8/u1..u8/r4/r8/r.un + conv.ovf.*) net48 evidence.
// Golden rule: conversions must go through variables (Roslyn constant folding vs JIT runtime conversion diverge — see existing
// evidence in the conv domain), so all inputs come from static fields/method parameters, forcing JIT conversions on the runtime path.
using System;

class ConvProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pi(string label, int v) => P(label + " = " + v);
    static void Pl(string label, long v) => P(label + " = " + v);
    static void Pu(string label, ulong v) => P(label + " = " + v);

    // Variable inputs (prevent constant folding; checked constant overflow CS0221 forces the runtime path)
    static int I1 = -1;
    static int I128 = 128;
    static int I65535 = 65535;
    static long L4294967297 = 4294967297L;
    static long LM4294967297 = -4294967297L;
    static long L2147483648 = 2147483648L;
    static long Lm2147483649 = -2147483649L;
    static long L4294967296 = 4294967296L;
    static long Lm1 = -1L;
    static uint UMAX = uint.MaxValue;
    static ulong ULONG_MAX = ulong.MaxValue;
    static uint U256 = 256u;
    static uint U32768 = 32768u;
    static uint U65536 = 65536u;
    static uint Um1 = unchecked((uint)(-1));
    static double DNeg1 = -1.0;
    static double D29 = 2.9;
    static double DM29 = -2.9;
    static double D2147483647 = 2147483647.0;
    static double D2147483648 = 2147483648.0;
    static double Dm2147483649 = -2147483649.0;
    static double Dm2147483648 = -2147483648.0;
    static double D63m1024 = 9223372036854774784.0;   // 2^63 - 1024 (representable in double, within int64 range)
    static double D63 = 9223372036854775808.0;        // 2^63 (out of range)
    static double DNaN = double.NaN;
    static double DInf = double.PositiveInfinity;
    static double D01 = 0.1;
    static float F16777217 = 16777217f;
    static long L9007199254740993 = 9007199254740993L; // 2^53+1

    static void Main()
    {
        P("runtime=" + Environment.Version);
        P("ptrSize=" + IntPtr.Size);

        // ---- narrowing truncation (conv.i1/i2/u1/u2/u4) ----
        Pi("conv.i1: (sbyte)128", unchecked((sbyte)I128));
        Pi("conv.i2: (short)65535", unchecked((short)I65535));
        Pi("conv.u1: (byte)-1", unchecked((byte)I1));
        Pi("conv.u2: (ushort)-1", unchecked((ushort)I1));
        Pu("conv.u4: (uint)-1", unchecked((uint)I1));

        // ---- widening (conv.i8 sign extension / zero extension) ----
        Pl("conv.i8: (long)-1", (long)I1);
        Pl("conv.i8: (long)uint.MaxValue", (long)UMAX);
        Pi("conv.i: (int)(IntPtr)(-1) [when native int is 32-bit]",
            unchecked((int)(IntPtr)I1));

        // ---- int64 narrowing wraparound (conv.i4 keeps low 32 bits) ----
        Pi("conv.i4: unchecked((int)4294967297L)", unchecked((int)L4294967297));
        Pi("conv.i4: unchecked((int)-4294967297L)", unchecked((int)LM4294967297));

        // ---- float <-> int (conv.r4/r8/i4/i8/u8/r.un) ----
        P("conv.r8: (double)5 = " + (double)5);
        P("conv.r8: (double)9007199254740993L = " + (double)L9007199254740993);
        P("conv.r8: (double)(uint)uint.MaxValue = " + (double)UMAX);       // 2^32-1 exact in double
        P("conv.r4: (double)(float)0.1 = " + (double)(float)D01);
        P("conv.r4: (double)(float)16777217 = " + (double)F16777217);      // rounds to 2^24
        Pi("conv.i4: (int)2.9 [truncate toward zero]", unchecked((int)D29));
        Pi("conv.i4: (int)-2.9 [truncate toward zero]", unchecked((int)DM29));
        Pu("conv.u8: unchecked((ulong)(double)-1.0)", unchecked((ulong)DNeg1));
        Pu("conv.u8: (ulong)1.5", (ulong)1.5);
        P("conv.r_un: (double)(uint)4294967295 = " + (double)UMAX);

        // ---- checked overflow conversions (conv.ovf.*) ----
        // conv.ovf.i4 (int64 -> int32 checked)
        Pi("ovf.i4: checked((int)2147483647L)", checked((int)2147483647L));
        Pi("ovf.i4: checked((int)-2147483648L)", checked((int)-2147483648L));
        TryI(() => checked((int)L2147483648), "ovf.i4: checked((int)2147483648L)");
        TryI(() => checked((int)Lm2147483649), "ovf.i4: checked((int)-2147483649L)");
        // conv.ovf.i4 (double -> int32 checked)
        Pi("ovf.i4: checked((int)2147483647.0)", checked((int)D2147483647));
        Pi("ovf.i4: checked((int)-2147483648.0)", checked((int)Dm2147483648));
        TryI(() => checked((int)D2147483648), "ovf.i4: checked((int)2147483648.0)");
        TryI(() => checked((int)Dm2147483649), "ovf.i4: checked((int)-2147483649.0)");
        // conv.ovf.i8 (double -> int64 checked)
        Pl("ovf.i8: checked((long)9223372036854774784.0)", checked((long)D63m1024));
        TryL(() => checked((long)D63), "ovf.i8: checked((long)2^63)");
        TryL(() => checked((long)DNaN), "ovf.i8: checked((long)NaN)");
        TryL(() => checked((long)DInf), "ovf.i8: checked((long)+Inf)");
        // conv.ovf.u4
        Pu("ovf.u4: checked((uint)4294967295L)", checked((uint)4294967295L));
        TryU(() => checked((uint)L4294967296), "ovf.u4: checked((uint)4294967296L)");
        TryU(() => checked((uint)Lm1), "ovf.u4: checked((uint)(-1L))");
        // conv.ovf.i8.un (ulong -> long checked)
        Pl("ovf.i8.un: checked((long)9223372036854775807UL)", checked((long)9223372036854775807UL));
        TryL(() => checked((long)ULONG_MAX), "ovf.i8.un: checked((long)ulong.MaxValue)");
        // conv.ovf.u1.un / i2.un / u2.un (uint -> narrow types, checked)
        Pi("ovf.u1.un: checked((byte)255u)", checked((byte)255u));
        TryI(() => checked((byte)U256), "ovf.u1.un: checked((byte)256u)");
        TryI(() => checked((byte)Um1), "ovf.u1.un: checked((byte)(uint)-1)");
        Pi("ovf.i2.un: checked((short)32767u)", checked((short)32767u));
        TryI(() => checked((short)U32768), "ovf.i2.un: checked((short)32768u)");
        Pi("ovf.u2.un: checked((ushort)65535u)", checked((ushort)65535u));
        TryI(() => checked((ushort)U65536), "ovf.u2.un: checked((ushort)65536u)");
        // conv.ovf.u4.un (uint -> uint checked = identity)
        Pu("ovf.u4.un: checked((uint)uint.MaxValue)", checked((uint)UMAX));

        // ---- unchecked float overflow conversion = indeterminate int (cvttsd2si semantics) ----
        Pi("unchecked (int)(double)2147483648.0 [indeterminate]", unchecked((int)D2147483648));
        Pi("unchecked (int)(double)NaN [indeterminate]", unchecked((int)DNaN));
        Pi("unchecked (int)(double)+Inf [indeterminate]", unchecked((int)DInf));
        Pl("unchecked (long)(double)2^63 [indeterminate]", unchecked((long)D63));
    }

    // Exception catcher: prints "label -> OverflowException" or "label -> <type>"
    static void TryI(Func<int> f, string label)
    {
        try { Pi(label, f()); }
        catch (Exception e) { P(label + " -> " + e.GetType().Name); }
    }
    static void TryL(Func<long> f, string label)
    {
        try { Pl(label, f()); }
        catch (Exception e) { P(label + " -> " + e.GetType().Name); }
    }
    static void TryU(Func<uint> f, string label)
    {
        try { Pu(label, (ulong)f()); }
        catch (Exception e) { P(label + " -> " + e.GetType().Name); }
    }
}
