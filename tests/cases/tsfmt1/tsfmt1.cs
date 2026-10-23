//------------------------------------------------------------------------------
// tsfmt1.cs -- TimeSpan.ToString with the standard specifiers c / g / G
// (plus the t / T aliases and the invalid ones), pinned against net48.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Tsfmt1 {
    static void P(string tag, TimeSpan v, string fmt) {
        string r;
        try { r = v.ToString(fmt); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    public static void Main() {
        TimeSpan a = new TimeSpan(1, 2, 3, 4, 5);
        TimeSpan b = new TimeSpan(0, 2, 3, 4, 5);
        TimeSpan c = new TimeSpan(0, 0, 3, 4, 5);
        TimeSpan h25 = new TimeSpan(25, 0, 0);
        TimeSpan f = new TimeSpan(1, 2, 3, 4, 5).Add(TimeSpan.FromTicks(1234567));
        TimeSpan q = new TimeSpan(1, 2, 3, 4, 0);
        TimeSpan n = new TimeSpan(-1, -2, -3, -4, -5);
        TimeSpan nm = TimeSpan.FromTicks(-5000000);
        TimeSpan z = TimeSpan.Zero;

        // Parameterless (already wired -- guards the shared formatter).
        Console.WriteLine("p_a=" + a.ToString());
        Console.WriteLine("p_z=" + z.ToString());

        // "c".
        P("c_a", a, "c");
        P("c_b", b, "c");
        P("c_c", c, "c");
        P("c_f", f, "c");
        P("c_q", q, "c");
        P("c_n", n, "c");
        P("c_nm", nm, "c");
        P("c_z", z, "c");
        P("c_h25", h25, "c");
        P("c_max", TimeSpan.MaxValue, "c");
        P("c_min", TimeSpan.MinValue, "c");
        P("c_empty", a, "");

        // "g".
        P("g_a", a, "g");
        P("g_b", b, "g");
        P("g_c", c, "g");
        P("g_f", f, "g");
        P("g_q", q, "g");
        P("g_n", n, "g");
        P("g_nm", nm, "g");
        P("g_z", z, "g");
        P("g_h25", h25, "g");
        P("g_max", TimeSpan.MaxValue, "g");
        P("g_min", TimeSpan.MinValue, "g");

        // "G".
        P("G_a", a, "G");
        P("G_b", b, "G");
        P("G_c", c, "G");
        P("G_f", f, "G");
        P("G_q", q, "G");
        P("G_n", n, "G");
        P("G_nm", nm, "G");
        P("G_z", z, "G");
        P("G_h25", h25, "G");
        P("G_max", TimeSpan.MaxValue, "G");
        P("G_min", TimeSpan.MinValue, "G");

        // t / T are the "c" aliases.
        P("t_a", a, "t");
        P("T_a", a, "T");

        // Invalid single characters (standard-specifier lookup).
        P("x_C", a, "C");
        P("x_d", a, "d");
        P("x_f", a, "f");
        P("x_s", a, "s");
        P("x_colon", a, ":");
        P("x_dot", a, ".");
        P("x_digit", a, "1");
        P("x_space", a, " ");
        P("x_Q", a, "Q");
        P("x_K", a, "K");
        P("x_z", a, "z");
    }
}}
