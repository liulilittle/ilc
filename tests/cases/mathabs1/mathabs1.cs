using System;

internal static class MathAbs1
{
    private static void Show(string k, Func<object> f)
    {
        try { Console.WriteLine("[" + k + "] ok=" + f()); }
        catch (Exception e) { Console.WriteLine("[" + k + "] ex=" + e.GetType().FullName + " msg=" + e.Message); }
    }

    private static void Main()
    {
        Console.WriteLine("[abs_i_pos] " + Math.Abs(7));
        Console.WriteLine("[abs_i_neg] " + Math.Abs(-7));
        Console.WriteLine("[abs_i_zero] " + Math.Abs(0));
        Console.WriteLine("[abs_l_pos] " + Math.Abs(7L));
        Console.WriteLine("[abs_l_neg] " + Math.Abs(-7L));
        Console.WriteLine("[abs_sb_neg] " + Math.Abs((sbyte)-127));
        Console.WriteLine("[abs_sb_pos] " + Math.Abs((sbyte)127));
        Console.WriteLine("[abs_sh_neg] " + Math.Abs((short)-32767));
        Console.WriteLine("[abs_sh_pos] " + Math.Abs((short)32767));
        Console.WriteLine("[abs_f_neg] " + Math.Abs(-2.5f));
        Console.WriteLine("[abs_d_neg] " + Math.Abs(-2.5));
        Console.WriteLine("[abs_d_nzero] " + Math.Abs(-0.0));
        Console.WriteLine("[abs_d_nan] " + Math.Abs(double.NaN));
        Console.WriteLine("[abs_m_neg] " + Math.Abs(-2.5m));
        Show("abs_i_min", () => Math.Abs(int.MinValue));
        Show("abs_l_min", () => Math.Abs(long.MinValue));
        Show("abs_sb_min", () => Math.Abs(sbyte.MinValue));
        Show("abs_sh_min", () => Math.Abs(short.MinValue));
        Show("abs_m_min", () => Math.Abs(decimal.MinValue));
        Show("abs_m_max", () => Math.Abs(decimal.MaxValue));
    }
}