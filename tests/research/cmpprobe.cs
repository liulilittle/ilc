// cmpprobe.cs — 比较族（ceq/cgt/clt/cgt.un/clt.un）net48 实证。
// 覆盖 int32 / int64 / float / 引用 各操作数类型 + NaN 行为 + 无符号语义。
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
        // ---- int32 有符号 ----
        Pb("ceq(1,1)", 1 == I1);
        Pb("cgt(2,1)", I2 > I1);
        Pb("clt(1,2)", I1 < I2);
        Pb("cgt(-1,1) [有符号]", IM1 > I1);   // 有符号：-1 > 1 为假
        Pb("clt(-1,1) [有符号]", IM1 < I1);   // 有符号：-1 < 1 为真

        // ---- int32 无符号（cgt.un/clt.un = uint 比较）----
        Pb("cgt.un(0xFFFFFFFF, 1) [无符号]", (uint)IM1 > U1);   // 4294967295 > 1 为真
        Pb("cgt(0xFFFFFFFF, 1) [有符号]", IM1 > I1);            // 对照：有符号为假
        Pb("clt.un(0, 0xFFFFFFFF)", 0u < (uint)IM1);
        Pb("clt.un(0xFFFFFFFF, 1)", (uint)IM1 < U1);

        // ---- int64 ----
        Pb("ceq(long.MaxValue, long.MaxValue)", LMAX == LMAX);
        Pb("cgt(1L, -1L)", L1 > LM1);
        Pb("clt(-1L, 1L)", LM1 < L1);
        Pb("cgt.un(0xFFFFFFFFFFFFFFFF, 1L) [无符号]", (ulong)LM1 > (ulong)L1);
        Pb("cgt(0xFFFFFFFFFFFFFFFF, 1L) [有符号]", LM1 > L1);

        // ---- float/double（有符号比较对 NaN = 全假）----
        Pb("cgt(NaN, 0) [有符号]", NaN > 0.0);
        Pb("clt(NaN, 0) [有符号]", NaN < 0.0);
        Pb("ceq(NaN, NaN)", NaN == NaN);
        Pb("cgt(2.0, 1.0)", 2.0 > 1.0);
        Pb("clt(1.0, 2.0)", 1.0 < 2.0);
        Pb("cgt.un(NaN, 0) [C# 不可达：clt.un 浮点 NaN→真 仅 ECMA]",
            NaN != 0.0); // != 走 ceq+neg；clt.un/cgt.un 浮点 NaN 语义 C# 编译器不发射

        // ---- 引用（ceq/cgt.un 指针比较；字符串 = 引用恒等非内容）----
        object o1 = new string('x', 1);
        object o2 = new string('x', 1);
        object o3 = o1;
        Pb("ceq(ref, ref) [内容同实例异]", o1 == o2);
        Pb("ceq(ref, ref) [同实例]", o1 == o3);
        Pb("cgt.un(ref, null) [x != null]", o1 != null);
        Pb("ceq(ref, null)", o1 == null);
        Pb("ceq(null, null)", (object)null == null);

        // ---- 比较结果类型：int32 0/1 ----
        Pi("cgt(2,1) 值", I2 > I1 ? 1 : 0);
        Pi("ceq(2,1) 值", I2 == I1 ? 1 : 0);
    }
}
