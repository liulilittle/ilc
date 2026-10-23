//------------------------------------------------------------------------------
// dtz2.cs -- DateTime.ToString with an INVALID format must throw
// FormatException (net48), never echo the specifier back:
//   * a single non-standard character (letter or not) is a standard-specifier
//     lookup -> FormatException;
//   * a multi-character custom format is accepted (unknown characters are
//     literals) EXCEPT the malformed '%' escape and unterminated quotes /
//     trailing backslash.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class Dtz2 {
    static void P(string tag, DateTime v, string fmt) {
        string r;
        try { r = v.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    public static void Main() {
        DateTime d = new DateTime(2020, 6, 15, 13, 45, 30);

        // Single unknown letters / non-letters -> FormatException.
        P("s_Q", d, "Q");
        P("s_q", d, "q");
        P("s_z", d, "z");
        P("s_K", d, "K");
        P("s_g", d, "g");
        P("s_colon", d, ":");
        P("s_space", d, " ");
        P("s_digit", d, "1");
        P("s_dot", d, ".");
        P("s_slash", d, "/");
        P("s_dash", d, "-");
        P("s_quote", d, "'");
        P("s_dquote", d, "\"");
        P("s_bslash", d, "\\");
        P("s_pct", d, "%");

        // Multi-character custom formats: unknown characters are LITERALS.
        P("m_yQ", d, "yQ");
        P("m_QQ", d, "QQ");
        P("m_abc", d, "abc");
        P("m_Q1", d, "Q1");
        P("m_colons", d, "::");
        P("m_slashes", d, "//");
        P("m_y1", d, "y1");
        P("m_dd1", d, "dd1");

        // Malformed '%' escapes -> FormatException.
        P("p_end", d, "d%");
        P("p_pct", d, "%%");
        P("p_pctd", d, "%%d");
        P("p_quote", d, "%'");
        P("p_dquote", d, "%\"");
        P("p_bslash", d, "%\\");
        P("p_space", d, "% ");
        P("p_digit", d, "%1");
        P("p_colon", d, "%:");
        P("p_Q", d, "%Q");

        // Malformed quotes / escapes -> FormatException.
        P("q_unterminated", d, "'ab");
        P("q_dq_unterm", d, "\"ab");
        P("q_trailing_bs", d, "dd\\");
    }
}}
