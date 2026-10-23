// mathminmax1 -- Math.Min/Max/Sign/Abs special-value lattice over all net48
// overloads. Bit patterns (long) keep the output culture-proof.
using System;

internal static class MathMinMax1
{
    private static long B(double v) { return BitConverter.DoubleToInt64Bits(v); }
    private static string S(string k, double v) { return "[" + k + "] " + B(v); }
    private static string Sf(string k, float v) { return "[" + k + "] " + BitConverter.DoubleToInt64Bits(v); }

    private static void Main()
    {
        double nan = double.NaN;
        double pinf = double.PositiveInfinity;
        double ninf = double.NegativeInfinity;
        double pz = 0.0;
        double nz = -0.0;

        // Max/Min NaN: net48 = (a >= b) ? a : b
        Console.WriteLine(S("max_nan_5", Math.Max(nan, 5.0)));
        Console.WriteLine(S("max_5_nan", Math.Max(5.0, nan)));
        Console.WriteLine(S("min_nan_5", Math.Min(nan, 5.0)));
        Console.WriteLine(S("min_5_nan", Math.Min(5.0, nan)));
        Console.WriteLine(S("max_nan_nan", Math.Max(nan, nan)));
        // signed zeros: ties -> first operand
        Console.WriteLine(S("max_pz_nz", Math.Max(pz, nz)));
        Console.WriteLine(S("max_nz_pz", Math.Max(nz, pz)));
        Console.WriteLine(S("min_pz_nz", Math.Min(pz, nz)));
        Console.WriteLine(S("min_nz_pz", Math.Min(nz, pz)));
        // infinities
        Console.WriteLine(S("max_pinf_5", Math.Max(pinf, 5.0)));
        Console.WriteLine(S("max_ninf_5", Math.Max(ninf, 5.0)));
        Console.WriteLine(S("min_pinf_5", Math.Min(pinf, 5.0)));
        Console.WriteLine(S("min_ninf_5", Math.Min(ninf, 5.0)));
        Console.WriteLine(S("max_pinf_nan", Math.Max(pinf, nan)));
        Console.WriteLine(S("min_ninf_nan", Math.Min(ninf, nan)));

        // float overloads ride the double slot; net48 Max(float,float)
        Console.WriteLine(Sf("maxf_nan_5", Math.Max(float.NaN, 5.0f)));
        Console.WriteLine(Sf("maxf_5_nan", Math.Max(5.0f, float.NaN)));
        Console.WriteLine(Sf("minf_nan_5", Math.Min(float.NaN, 5.0f)));
        Console.WriteLine(Sf("minf_nz_pz", Math.Min(-0.0f, 0.0f)));
        Console.WriteLine(Sf("maxf_nz_pz", Math.Max(-0.0f, 0.0f)));

        // integer Min/Max incl. extremes
        Console.WriteLine("[max_i] " + Math.Max(int.MinValue, int.MaxValue));
        Console.WriteLine("[min_i] " + Math.Min(int.MinValue, int.MaxValue));
        Console.WriteLine("[max_l] " + Math.Max(long.MinValue, long.MaxValue));
        Console.WriteLine("[min_l] " + Math.Min(long.MinValue, long.MaxValue));
        Console.WriteLine("[max_sb] " + Math.Max((sbyte)-128, (sbyte)127));
        Console.WriteLine("[min_sb] " + Math.Min((sbyte)-128, (sbyte)127));
        Console.WriteLine("[max_sh] " + Math.Max((short)-32768, (short)32767));
        Console.WriteLine("[min_sh] " + Math.Min((short)-32768, (short)32767));
        Console.WriteLine("[max_by] " + Math.Max((byte)0, (byte)255));
        Console.WriteLine("[min_by] " + Math.Min((byte)0, (byte)255));
        Console.WriteLine("[max_us] " + Math.Max((ushort)0, (ushort)65535));
        Console.WriteLine("[min_us] " + Math.Min((ushort)0, (ushort)65535));
        Console.WriteLine("[max_ui] " + Math.Max(0u, uint.MaxValue));
        Console.WriteLine("[min_ui] " + Math.Min(0u, uint.MaxValue));
        Console.WriteLine("[max_ul] " + Math.Max(0UL, ulong.MaxValue));
        Console.WriteLine("[min_ul] " + Math.Min(0UL, ulong.MaxValue));

        // decimal Min/Max
        Console.WriteLine("[max_m] " + Math.Max(-1.5m, 2.5m));
        Console.WriteLine("[min_m] " + Math.Min(-1.5m, 2.5m));

        // Sign
        Console.WriteLine("[sign_i_neg] " + Math.Sign(-9));
        Console.WriteLine("[sign_i_zero] " + Math.Sign(0));
        Console.WriteLine("[sign_i_pos] " + Math.Sign(9));
        Console.WriteLine("[sign_i_min] " + Math.Sign(int.MinValue));
        Console.WriteLine("[sign_l_min] " + Math.Sign(long.MinValue));
        Console.WriteLine("[sign_sb_min] " + Math.Sign((sbyte)-128));
        Console.WriteLine("[sign_sh_min] " + Math.Sign((short)-32768));
        Console.WriteLine("[sign_d_neg] " + Math.Sign(-2.5));
        Console.WriteLine("[sign_d_pzero] " + Math.Sign(0.0));
        Console.WriteLine("[sign_d_nzero] " + Math.Sign(-0.0));
        Console.WriteLine("[sign_d_pos] " + Math.Sign(2.5));
        Console.WriteLine("[sign_d_tiny] " + Math.Sign(-double.Epsilon));
        Console.WriteLine("[sign_f_neg] " + Math.Sign(-0.5f));
        Console.WriteLine("[sign_f_nzero] " + Math.Sign(-0.0f));
        try { Console.WriteLine("[sign_d_nan] " + Math.Sign(double.NaN)); }
        catch (Exception e) { Console.WriteLine("[sign_d_nan] " + e.GetType().FullName); }
        try { Console.WriteLine("[sign_f_nan] " + Math.Sign(float.NaN)); }
        catch (Exception e) { Console.WriteLine("[sign_f_nan] " + e.GetType().FullName); }
        try { Console.WriteLine("[sign_d_ninf] " + Math.Sign(double.NegativeInfinity)); }
        catch (Exception e) { Console.WriteLine("[sign_d_ninf] " + e.GetType().FullName); }
        try { Console.WriteLine("[sign_d_pinf] " + Math.Sign(double.PositiveInfinity)); }
        catch (Exception e) { Console.WriteLine("[sign_d_pinf] " + e.GetType().FullName); }
        Console.WriteLine("[sign_m_neg] " + Math.Sign(-1.5m));
        Console.WriteLine("[sign_m_zero] " + Math.Sign(0m));
        Console.WriteLine("[sign_m_pos] " + Math.Sign(1.5m));

        // Abs on the remaining overloads
        Console.WriteLine("[abs_f_nzero] " + Sf("x", Math.Abs(-0.0f)));
        Console.WriteLine("[abs_f_nan] " + Sf("x", Math.Abs(float.NaN)));
        Console.WriteLine("[abs_f_ninf] " + Sf("x", Math.Abs(float.NegativeInfinity)));
        Console.WriteLine("[abs_d_ninf] " + S("x", Math.Abs(double.NegativeInfinity)));
        Console.WriteLine("[abs_d_pinf] " + S("x", Math.Abs(double.PositiveInfinity)));
        Console.WriteLine("[abs_d_max] " + S("x", Math.Abs(double.MaxValue)));
        Console.WriteLine("[abs_d_min] " + S("x", Math.Abs(double.MinValue)));
    }
}