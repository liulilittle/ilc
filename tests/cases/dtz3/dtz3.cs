//------------------------------------------------------------------------------
// dtz3.cs -- DateTime custom-format escaping and literals:
//   * backslash escapes ('\x' -> literal x, '\\' -> '\')
//   * single- and double-quoted literal groups (quote characters stripped,
//     backslash still escapes inside, unterminated -> FormatException)
//   * '%' forces the next character to be a SINGLE custom specifier
//   * every other character is a literal
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class Dtz3 {
    static void P(string tag, DateTime v, string fmt) {
        string r;
        try { r = v.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    public static void Main() {
        DateTime d = new DateTime(2020, 6, 15, 13, 45, 30);

        // Backslash escapes.
        P("e_T", d, "\\T");
        P("e_bslash", d, "\\\\");
        P("e_dash", d, "yyyy\\-MM\\-dd");
        P("e_colon", d, "HH\\:mm");
        P("e_d", d, "\\d");
        P("e_quote", d, "\\'");
        P("e_dquote", d, "\\\"");
        P("e_pct", d, "\\%d");
        P("e_space", d, "\\ ");
        P("e_digit", d, "\\1");

        // Single-quoted literal groups.
        P("q_T", d, "yyyy-MM-dd'T'HH:mm:ss");
        P("q_at", d, "dddd, dd MMMM yyyy 'at' HH:mm");
        P("q_group", d, "d'dd'd");
        P("q_yy", d, "yyyy'yy'");
        P("q_empty", d, "''");
        P("q_bslash", d, "'a\\b'");
        P("q_dq_in_sq", d, "'a\"b'");
        P("q_sq_in_dq", d, "\"a'b\"");

        // Double-quoted literal groups.
        P("dq_T", d, "yyyy-MM-dd\"T\"HH:mm:ss");
        P("dq_empty", d, "\"\"");
        P("dq_group", d, "d\"dd\"d");

        // '%' single-specifier escape.
        P("p_d", d, "%d");
        P("p_dd", d, "%dd");
        P("p_yyyy", d, "%yyyy");
        P("p_yy", d, "%yy");
        P("p_M", d, "%M");
        P("p_H", d, "%H");
        P("p_s", d, "%s");
        P("p_f", d, "%f");
        P("p_F", d, "%F");
        P("p_t", d, "%t");
        P("p_z", d, "%z");
        P("p_zz", d, "%zz");
        P("p_zzz", d, "%zzz");
        P("p_K", d, "%K");
        P("p_two", d, "%d%y");
        P("p_embed", d, "a%b");
        P("p_mid_token", d, "x%dy");

        // Plain literals inside a longer format.
        P("l_dash", d, "yyyy-MM-dd");
        P("l_space", d, "yyyy MM dd");
        P("l_text", d, "yyyy'年'MM'月'");
        P("l_dot", d, "HH.mm.ss");
    }
}}
