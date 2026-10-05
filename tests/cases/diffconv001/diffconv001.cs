// diffconv001 -- C0 double->integer conversion probe (NON-POOL, A4).
// Verifies (long)/(int)/(uint)/(ulong) on double-typed VALUES (call results,
// locals, expressions) perform NUMERIC truncation, not bit reinterpretation.
using System;

internal static class ConvProbe
{
    private static double D1() { return 123456.7890123; }
    private static double DNeg() { return -3.9; }
    private static double DPos() { return 3.9; }
    private static double DBig() { return 9.2233720368547748e18; } // 2^63 - 1024
    private static double DNaN() { return double.NaN; }
    private static double DPosInf() { return double.PositiveInfinity; }
    private static double DNegInf() { return double.NegativeInfinity; }

    private static void L(string id, long v) { Console.WriteLine("[" + id + "] " + v); }
    private static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    private static void U(string id, uint v) { Console.WriteLine("[" + id + "] " + v); }
    private static void U8(string id, ulong v) { Console.WriteLine("[" + id + "] " + v); }

    private static void CallResult()
    {
        L("call.trunc", (long)D1());
        L("call.neg", (long)DNeg());
        L("call.pos", (long)DPos());
    }

    private static void Locals()
    {
        double d = D1();
        L("loc.trunc", (long)d);
        double n = -3.9;
        L("loc.neg", (long)n);
    }

    private static void Expr()
    {
        L("expr.mul", (long)(D1() * 2));
        L("expr.add", (long)(D1() + 0.5));
        L("expr.sqrt", (long)Math.Sqrt(15241578750190.0));
        L("expr.round", (long)Math.Round(3.9));
        L("expr.pow", (long)Math.Pow(2.0, 40.0));
    }

    private static void IntWidths()
    {
        I("i4.call", (int)D1());
        U("u4.call", (uint)D1());
        U8("u8.call", (ulong)D1());
        U8("u8.neg", (ulong)DNeg());
    }

    private static void Boundary()
    {
        L("big.max", (long)DBig());
        L("nan", (long)DNaN());
        L("posinf", (long)DPosInf());
        L("neginf", (long)DNegInf());
    }

    private static void ViaBitConverter()
    {
        byte[] b = BitConverter.GetBytes(123456.7890123);
        L("bc.rt", (long)BitConverter.ToDouble(b, 0));
    }

    private static void Main()
    {
        try { CallResult(); } catch (Exception e) { L("call.EX", -1); Console.WriteLine("call.ex:" + e.GetType().Name); }
        try { Locals(); } catch (Exception e) { Console.WriteLine("loc.ex:" + e.GetType().Name); }
        try { Expr(); } catch (Exception e) { Console.WriteLine("expr.ex:" + e.GetType().Name); }
        try { IntWidths(); } catch (Exception e) { Console.WriteLine("iw.ex:" + e.GetType().Name); }
        try { Boundary(); } catch (Exception e) { Console.WriteLine("bnd.ex:" + e.GetType().Name); }
        try { ViaBitConverter(); } catch (Exception e) { Console.WriteLine("bc.ex:" + e.GetType().Name); }
    }
}
