//------------------------------------------------------------------------------
// dtz1.cs -- DateTime custom-format timezone tokens z / zz / zzz / K across
// the three DateTimeKind values, pinned against the net48 oracle.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class Dtz1 {
    static void P(string tag, DateTime v, string fmt) {
        string r;
        try { r = v.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    public static void Main() {
        DateTime unspec = new DateTime(2020, 6, 15, 13, 45, 30);
        DateTime utc = DateTime.SpecifyKind(new DateTime(2020, 6, 15, 13, 45, 30), DateTimeKind.Utc);
        DateTime local = DateTime.SpecifyKind(new DateTime(2020, 6, 15, 13, 45, 30), DateTimeKind.Local);

        Console.WriteLine("tz_current=" + TimeZoneInfo.Local.GetUtcOffset(unspec).TotalMinutes);

        P("u_z", unspec, "z");
        P("u_zz", unspec, "zz");
        P("u_zzz", unspec, "zzz");
        P("u_K", unspec, "K");
        P("u_full", unspec, "yyyy-MM-ddTHH:mm:ss.fffK");
        P("u_full_zzz", unspec, "yyyy-MM-ddTHH:mm:ss.fff zzz");

        P("c_z", utc, "z");
        P("c_zz", utc, "zz");
        P("c_zzz", utc, "zzz");
        P("c_K", utc, "K");
        P("c_full", utc, "yyyy-MM-ddTHH:mm:ss.fffK");
        P("c_full_zzz", utc, "yyyy-MM-ddTHH:mm:ss.fff zzz");

        P("l_z", local, "z");
        P("l_zz", local, "zz");
        P("l_zzz", local, "zzz");
        P("l_K", local, "K");
        P("l_full", local, "yyyy-MM-ddTHH:mm:ss.fffK");
        P("l_full_zzz", local, "yyyy-MM-ddTHH:mm:ss.fff zzz");

        // Run-length behaviour beyond the documented 1..3 lengths.
        P("u_z4", unspec, "zzzz");
        P("u_K2", unspec, "KK");
        P("c_K2", utc, "KK");
        P("l_K2", local, "KK");
        P("l_Kzzz", local, "K zzz");

        // Embedded in a longer pattern + percent escape.
        P("u_embed", unspec, "HH:mm z");
        P("u_pctz", unspec, "%z");
        P("u_pctK", unspec, "%K");
        P("c_pctK", utc, "%K");
        P("u_quoted", unspec, "yyyy'zzz'");
    }
}}
