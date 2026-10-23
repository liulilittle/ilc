//------------------------------------------------------------------------------
// tsfmt2.cs -- TimeSpan.ToString(custom format): the d/h/m/s/f/F token set,
// escapes, quoted literals, the '%' escape and the invalid patterns.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Tsfmt2 {
    static void P(string tag, TimeSpan v, string fmt) {
        string r;
        try { r = v.ToString(fmt); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    public static void Main() {
        TimeSpan a = new TimeSpan(1, 2, 3, 4, 5);
        TimeSpan q = new TimeSpan(1, 2, 3, 4, 0);
        TimeSpan r = new TimeSpan(0, 0, 0, 0, 123);
        TimeSpan n = new TimeSpan(-1, -2, -3, -4, -5);
        TimeSpan z = TimeSpan.Zero;

        // The canonical "c"-like custom pattern.
        P("full_a", a, "dd\\.hh\\:mm\\:ss\\.fff");
        P("full_n", n, "dd\\.hh\\:mm\\:ss\\.fff");
        P("full_z", z, "dd\\.hh\\:mm\\:ss\\.fff");
        P("full_q", q, "dd\\.hh\\:mm\\:ss\\.fff");
        P("hhmm", a, "hh\\:mm");
        P("hhmmss", a, "hh\\:mm\\:ss");
        P("dh", a, "d\\.hh");
        P("dh2", a, "dd\\.hh");

        // Token widths.
        P("d1", a, "%d");
        P("d2", a, "dd");
        P("d3", a, "ddd");
        P("d4", a, "dddd");
        P("d8", a, "dddddddd");
        P("h1", a, "%h");
        P("h2", a, "hh");
        P("m1", a, "%m");
        P("m2", a, "mm");
        P("s1", a, "%s");
        P("s2", a, "ss");
        P("h_pct", a, "%hh");

        // Fraction tokens.
        P("f1", a, "%f");
        P("f3", a, "fff");
        P("f7", a, "fffffff");
        P("fF1", a, "%F");
        P("fF3", a, "FFF");
        P("fF7", a, "FFFFFFF");
        P("frac_dot_f", a, "ss\\.fff");
        P("frac_dot_F", a, "ss\\.FFF");
        P("frac_dot_F0", q, "ss\\.FFF");
        P("frac_dot_f0", q, "ss\\.fff");
        P("F_alone0", q, "FFF");
        P("f_alone_r", r, "%f");
        P("F_alone_r", r, "FFF");
        P("F_short_r", r, "FF");

        // Escapes / literals.
        P("q_sq", a, "hh':'mm");
        P("q_dq", a, "hh\"x\"mm");
        P("q_empty", a, "''");
        P("q_text", a, "'ab'");
        P("esc_a", a, "\\a");
        P("esc_bs", a, "\\\\dd");
        P("esc_pct", a, "\\%dd");
        P("q_pct", a, "'%'dd");

        // Invalid custom patterns (no literal passthrough in TimeSpan).
        P("bad_lit", a, "abc");
        P("bad_colon", a, "hh:mm");
        P("bad_dot", a, "dd.dd");
        P("bad_space", a, "hh mm");
        P("bad_digit", a, "dd1");
        P("bad_d", a, "d");
        P("bad_hhh", a, "hhh");
        P("bad_d9", a, "ddddddddd");
        P("bad_f8", a, "ffffffff");
        P("bad_F8", a, "FFFFFFFF");
        P("bad_sect", a, "dd;hh");
        P("bad_pct", a, "dd%");
        P("bad_pctpct", a, "dd%%");
        P("bad_pct_space", a, "dd% d");
        P("bad_trailq", a, "dd'");
        P("bad_trailbs", a, "dd\\");
        P("bad_pct_q", a, "dd%'");
        P("bad_pct_bs", a, "dd%\\");
    }
}}
