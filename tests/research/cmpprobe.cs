// cmpprobe.cs — comparison family (ceq/cgt/clt/cgt.un/clt.un) net48 evidence.
// Covers int32 / int64 / float / reference operand types + NaN behavior + unsigned semantics.
using System;

class CmpProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pb(string label, bool v) => P(label + " = " + (v ? 1 : 0));
    static void Pi(string label, int v) => P(label + " = " + v);

    static int I1 = 1, I2 = 2, IM1 = -1;
    static long LM1 = -1L, L1 = 1L, LMAX = long.MaxValue;
    static uint U1 = 1u, UMAX = uint.MaxValue;
    static double NaN = double.NaN;

    static void Main()
    {
        // ---- int32 signed ----
        Pb("ceq(1,1)", 1 == I1);
        Pb("cgt(2,1)", I2 > I1);
        Pb("clt(1,2)", I1 < I2);
        Pb("cgt(-1,1) [signed]", IM1 > I1);   // signed: -1 > 1 is false
        Pb("clt(-1,1) [signed]", IM1 < I1);   // signed: -1 < 1 is true

        // ---- int32 unsigned (cgt.un/clt.un = uint comparison) ----
        Pb("cgt.un(0xFFFFFFFF, 1) [unsigned]", (uint)IM1 > U1);   // 4294967295 > 1 is true
        Pb("cgt(0xFFFFFFFF, 1) [signed]", IM1 > I1);            // contrast: signed is false
        Pb("clt.un(0, 0xFFFFFFFF)", 0u < (uint)IM1);
        Pb("clt.un(0xFFFFFFFF, 1)", (uint)IM1 < U1);

        // ---- int64 ----
        Pb("ceq(long.MaxValue, long.MaxValue)", LMAX == LMAX);
        Pb("cgt(1L, -1L)", L1 > LM1);
        Pb("clt(-1L, 1L)", LM1 < L1);
        Pb("cgt.un(0xFFFFFFFFFFFFFFFF, 1L) [unsigned]", (ulong)LM1 > (ulong)L1);
        Pb("cgt(0xFFFFFFFFFFFFFFFF, 1L) [signed]", LM1 > L1);

        // ---- float/double (signed comparison against NaN = all false) ----
        Pb("cgt(NaN, 0) [signed]", NaN > 0.0);
        Pb("clt(NaN, 0) [signed]", NaN < 0.0);
        Pb("ceq(NaN, NaN)", NaN == NaN);
        Pb("cgt(2.0, 1.0)", 2.0 > 1.0);
        Pb("clt(1.0, 2.0)", 1.0 < 2.0);
        Pb("cgt.un(NaN, 0) [unreachable from C#: clt.un float NaN->true is ECMA-only]",
            NaN != 0.0); // != uses ceq+neg; clt.un/cgt.un float NaN semantics are not emitted by the C# compiler

        // ---- reference (ceq/cgt.un pointer comparison; strings = reference identity, not content) ----
        object o1 = new string('x', 1);
        object o2 = new string('x', 1);
        object o3 = o1;
        Pb("ceq(ref, ref) [same content, different instance]", o1 == o2);
        Pb("ceq(ref, ref) [same instance]", o1 == o3);
        Pb("cgt.un(ref, null) [x != null]", o1 != null);
        Pb("ceq(ref, null)", o1 == null);
        Pb("ceq(null, null)", (object)null == null);

        // ---- comparison result type: int32 0/1 ----
        Pi("cgt(2,1) value", I2 > I1 ? 1 : 0);
        Pi("ceq(2,1) value", I2 == I1 ? 1 : 0);
    }
}
