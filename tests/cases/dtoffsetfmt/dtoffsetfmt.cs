//------------------------------------------------------------------------------
// dtoffsetfmt.cs -- DateTimeOffset.ToString(format[, provider]) oracle probe.
//
// Pins net48 semantics on the zh-CN verification host for the unwired surface.
// Every call is exception-guarded (net48 throws on some specifiers); a throw is
// pinned as EXF=FormatException / EXO=other. Outputs are deterministic; the
// transpiled runtime must match this oracle byte-for-byte.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class DtoffsetFmt {
    static void P(string tag, DateTimeOffset v, string fmt, CultureInfo ci) {
        string r;
        try { r = v.ToString(fmt, ci); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception) { r = "EXO"; }
        Console.WriteLine(tag + "=" + r);
    }
    static void Q(string tag, DateTimeOffset v, string fmt) {
        string r;
        try { r = v.ToString(fmt); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception) { r = "EXO"; }
        Console.WriteLine(tag + "=" + r);
    }
    static void D(string tag, DateTime v, string fmt, CultureInfo ci) {
        string r;
        try { r = v.ToString(fmt, ci); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception) { r = "EXO"; }
        Console.WriteLine(tag + "=" + r);
    }

    public static void Main() {
        DateTimeOffset p8 = new DateTimeOffset(new DateTime(2020, 6, 15, 13, 45, 30), new TimeSpan(8, 0, 0));
        DateTimeOffset m530 = new DateTimeOffset(new DateTime(2020, 6, 15, 13, 45, 30), new TimeSpan(-5, -30, 0));
        DateTimeOffset z = new DateTimeOffset(new DateTime(2020, 6, 15, 13, 45, 30), new TimeSpan(0, 0, 0));
        DateTimeOffset p8m = new DateTimeOffset(new DateTime(2020, 6, 15, 13, 45, 30, 123), new TimeSpan(8, 0, 0));
        DateTimeOffset p14 = new DateTimeOffset(new DateTime(9999, 12, 31, 23, 59, 59), new TimeSpan(14, 0, 0));
        DateTime dt = new DateTime(2020, 6, 15, 13, 45, 30);
        CultureInfo inv = CultureInfo.InvariantCulture;
        CultureInfo cur = CultureInfo.CurrentCulture;

        // A. parameterless / null / empty format; zero-offset default.
        Console.WriteLine("A_default=" + p8.ToString());
        Console.WriteLine("A_null=[" + p8.ToString((string)null) + "]");
        Console.WriteLine("A_empty=[" + p8.ToString("") + "]");
        Console.WriteLine("A_zero_default=" + z.ToString());
        Console.WriteLine("A_m530_default=" + m530.ToString());

        // B/C. all standard specifiers, InvariantCulture / CurrentCulture.
        string[] specs = { "d", "D", "t", "T", "f", "F", "g", "G", "m", "M", "y", "Y", "s", "u", "U", "o", "O", "r", "R" };
        foreach (string s in specs) { P("B_" + s, p8, s, inv); }
        foreach (string s in specs) { P("C_" + s, p8, s, cur); }

        // D. U family and DateTime contrast.
        P("D_U_zero", z, "U", inv);
        P("D_U_cur", p8, "U", cur);
        Q("D_U_1arg", p8, "U");
        D("D_dt_U_inv", dt, "U", inv);
        D("D_dt_U_cur", dt, "U", cur);

        // E. G / format-null matrix.
        P("E_G_inv", p8, "G", inv);
        P("E_G_cur", p8, "G", cur);
        Q("E_G_1arg", p8, "G");
        P("E_nullfmt_inv", p8, null, inv);
        P("E_emptyfmt_inv", p8, "", inv);
        Q("E_p14_G", p14, "G");

        // F. offset tokens and custom formats.
        P("F_K_p8", p8, "K", inv);
        P("F_K_zero", z, "K", inv);
        P("F_K_m530", m530, "K", inv);
        P("F_zzz_p8", p8, "yyyy-MM-dd HH:mm:ss zzz", inv);
        P("F_zzz_m530", m530, "yyyy-MM-dd HH:mm:ss zzz", inv);
        P("F_zzz_zero", z, "yyyy-MM-dd HH:mm:ss zzz", inv);
        P("F_z_p8", p8, "z", inv);
        P("F_zz_p8", p8, "zz", inv);
        P("F_z_m530", m530, "z", inv);
        P("F_zz_m530", m530, "zz", inv);
        P("F_frac_o", p8m, "o", inv);
        P("F_frac_fff", p8m, "yyyy-MM-dd HH:mm:ss.fff zzz", inv);
        P("F_quote", p8, "yyyy-MM-dd'T'HH:mm:ss zzz", inv);

        // G. unknown single letter throws FormatException.
        P("G_q_inv", p8, "q", inv);
        P("G_q_cur", p8, "q", cur);

        // H. explicit null provider == current culture.
        P("H_nullprov_G", p8, "G", null);
        P("H_nullprov_d", p8, "d", null);

        // I. custom-token edge probes.
        P("I_z_in_p8", p8, "H:mm z", inv);
        P("I_z_in_m530", m530, "H:mm z", inv);
        P("I_z_in_zero", z, "H:mm z", inv);
        P("I_zz_in_zero", z, "H:mm zz", inv);
        P("I_K_in", p8, "yyyy K", inv);
        P("I_zzz_alone", p8, "zzz", inv);
        P("I_o_zero", z, "o", inv);
        P("I_f1", p8m, "s.f", inv);
        P("I_f2", p8m, "s.ff", inv);
        P("I_f7", p8m, "s.fffffff", inv);
        P("I_f9", p8m, "s.fffffffff", inv);
        P("I_lit", p8, "abc", inv);
        P("I_pctz", p8, "%z", inv);
        P("I_pctK", p8, "%K", inv);
        P("I_cjk", p8, "yyyy'年'M'月'", inv);
        P("I_K2", p8, "KK", inv);
        P("I_z4", p8, "zzzz", inv);
        P("I_Kzzz", p8, "yyyy K zzz", inv);
        P("I_Fms", p8m, "s.FFFFFFF", inv);
        P("I_Fzero", p8, "s.FFF", inv);

        // J. 'F' trimming vs the preceding separator (net48 edge).
        P("J_dotF0", p8, "ss.FFF", inv);
        P("J_dotF123", p8m, "ss.FFF", inv);
        P("J_dashF0", p8, "ss-FFF", inv);
        P("J_dotf0", p8, "ss.fff", inv);
        P("J_quoteF", p8, "ss.'F'", inv);
        P("J_colonF0", p8, "ss:FFF", inv);
        P("J_Fonly0", p8, "FFF", inv);
        P("J_FF", p8, "ss.FF", inv);
        P("J_F", p8, "ss.F", inv);
        D("J_dt_dotF0", dt, "ss.FFF", inv);
        D("J_dt_dotF0_cur", dt, "ss.FFF", cur);

        // K. '.' removal rule: output-adjacent vs format-adjacent.
        P("K_quoteddot", p8, "ss'.'FFF", inv);
        P("K_twodots", p8, "ss..FFF", inv);
        P("K_dotspace", p8, "ss. FFF", inv);
        P("K_xdot", p8, "ss'x'.FFF", inv);
        P("K_adot", p8, "'a'.FFF", inv);
        P("K_dotonly", p8, ".FFF", inv);
        P("K_spaceF", p8, "ss FFF", inv);
        P("K_cur_dot", p8, "ss.FFF", cur);
    }
}}
