// dtoiformattable1 -- System.DateTimeOffset through IFormattable (the BOX path).
//
// DateTimeOffset really implements IFormattable in net48 and every runtime
// piece already existed (icall_dtoffset_to_string_format, and the
// icall_box_date_time_offset / icall_unbox_date_time_offset helpers in
// nullable.cpp) -- but the transpiler's `box` arm had no DateTimeOffset
// branch, so `((IFormattable)dto)` was refused at the BOX instruction
// ("box of System.DateTimeOffset not supported -- no runtime box helper").
// The transpiler now boxes through icall_box_date_time_offset and unboxes
// through icall_unbox_date_time_offset, and MethodMap.Object.IsFormattableReceiver
// lists the type.
//
// Every row must be byte-identical to the net48 oracle (tests/cases is the
// differential harness: net48 exe vs transpiled runtime, raw byte compare).
// The provider is always explicit InvariantCulture except the two rows that
// exercise the null-provider path; the non-zero offsets (+05:30 / -08:00) are
// the point of the case -- the offset must SURVIVE the box.
using System;
using System.Globalization;

internal static class DtoIFormattable1
{
    private static string Try(Func<string> f)
    {
        try { return f(); }
        catch (FormatException) { return "EXF"; }
        catch (Exception) { return "EXO"; }
    }

    private static void W(string tag, Func<string> f)
    {
        Console.WriteLine("[" + tag + "] " + Try(f));
    }

    private static void Main()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;

        DateTimeOffset z = new DateTimeOffset(new DateTime(2024, 2, 29, 13, 45, 6, 789), TimeSpan.Zero);
        DateTimeOffset p530 = new DateTimeOffset(new DateTime(2024, 2, 29, 13, 45, 6, 789), new TimeSpan(5, 30, 0));
        DateTimeOffset m8 = new DateTimeOffset(new DateTime(2024, 2, 29, 13, 45, 6, 789), new TimeSpan(-8, 0, 0));

        // 1. IFormattable.ToString(null, provider) and format + provider.
        W("iface.null.inv.zero", () => ((IFormattable)z).ToString(null, inv));
        W("iface.null.null.zero", () => ((IFormattable)z).ToString(null, null));
        W("iface.G.inv.zero", () => ((IFormattable)z).ToString("G", inv));
        W("iface.d.inv.zero", () => ((IFormattable)z).ToString("d", inv));
        W("iface.null.inv.p530", () => ((IFormattable)p530).ToString(null, inv));
        W("iface.custom.inv.p530", () => ((IFormattable)p530).ToString("yyyy-MM-dd HH:mm:ss zzz", inv));
        W("iface.null.inv.m8", () => ((IFormattable)m8).ToString(null, inv));
        W("iface.empty.inv.p530", () => ((IFormattable)p530).ToString("", inv));

        // 2. Round-trip "o" / "O".
        W("iface.o.inv.p530", () => ((IFormattable)p530).ToString("o", inv));
        W("iface.O.inv.p530", () => ((IFormattable)p530).ToString("O", inv));
        W("iface.o.inv.zero", () => ((IFormattable)z).ToString("o", inv));
        W("iface.o.inv.m8", () => ((IFormattable)m8).ToString("o", inv));

        // 3. The direct ToString must agree with the IFormattable path.
        W("direct.inv.p530", () => p530.ToString(inv));
        W("agree.p530", () => (p530.ToString(inv) == ((IFormattable)p530).ToString(null, inv)).ToString());
        W("agree.zero", () => (z.ToString(inv) == ((IFormattable)z).ToString(null, inv)).ToString());

        // 4. Boxing identity: GetType / is / Equals against the unboxed value.
        object boxed = p530;
        W("box.gettype", () => boxed.GetType().FullName);
        W("box.is", () => (boxed is DateTimeOffset).ToString());
        W("box.equals.unboxed", () => boxed.Equals(p530).ToString());
        W("box.equals.other", () => boxed.Equals(z).ToString());
        W("box.tostring", () => boxed.ToString());

        // 5. unbox.any / cast read back (Ticks + Offset are the payload halves).
        W("unbox.ticks", () => ((DateTimeOffset)boxed).Ticks.ToString(inv));
        W("unbox.offset", () => ((DateTimeOffset)boxed).Offset.ToString());
        W("unbox.roundtrip", () => (((DateTimeOffset)boxed) == p530).ToString());
        W("unbox.utc", () => ((DateTimeOffset)boxed).UtcDateTime.ToString("o", inv));

        // 6. Composite string.Format paths.
        W("fmt.0.p530", () => string.Format("{0}", p530));
        W("fmt.prov.0.p530", () => string.Format(inv, "{0}", p530));
        W("fmt.0O.p530", () => string.Format(inv, "{0:O}", p530));
        W("fmt.0o.p530", () => string.Format(inv, "{0:o}", p530));
        W("fmt.G.p530", () => string.Format(inv, "{0:G}", p530));
        W("fmt.custom.p530", () => string.Format(inv, "{0:yyyy-MM-dd HH:mm:ss zzz}", p530));
        W("fmt.align.p530", () => string.Format(inv, "[{0,40}]", p530));
        W("fmt.two", () => string.Format(inv, "{0}|{1}", z, p530));

        // 7. The non-zero offsets must survive the box.
        W("offset.p530", () => ((DateTimeOffset)boxed).Offset.TotalMinutes.ToString(inv));
        W("offset.m8", () => ((DateTimeOffset)(object)m8).Offset.ToString());
        W("offset.zzz.p530", () => ((IFormattable)p530).ToString("zzz", inv));
        W("offset.k.p530", () => ((IFormattable)p530).ToString("K", inv));
    }
}
