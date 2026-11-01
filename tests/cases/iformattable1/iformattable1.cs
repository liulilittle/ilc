// iformattable1 -- System.IFormattable.ToString(format, provider) on the
// NON-NUMERIC receivers that really implement IFormattable in net48:
// System.DateTime, System.DateTimeOffset, System.TimeSpan and System.Guid.
// (Oracle probe, net48 4.8: typeof(X).GetInterfaces() carries
// System.IFormattable for exactly those four BCL value types; System.Char,
// System.Boolean and System.String do NOT -- a cast of char/bool to
// IFormattable does not even compile, CS0030.) The numeric receivers are
// covered by convtostr1.
//
// System.DateTimeOffset is oracle-verified IFormattable. Its rows live in the
// dedicated case dtoiformattable1 (the box/unbox arms and the formatter landed
// after this case was written), so they are not repeated here.
//
// The explicit interface call must transpile WITHOUT the former
// "System.IFormattable::ToString: not wired in M5" rejection, and every row
// must be byte-identical to the net48 oracle:
//   DateTime       "d"        -> "02/29/2024"
//   DateTime       "O"        -> "2024-02-29T13:45:06.7890000"
//   TimeSpan       null / "c" -> "1.02:03:04.0050000"
//   TimeSpan       "G"        -> "1:02:03:04.0050000"
//   Guid           "N"        -> 32 lowercase hex digits
//   Guid           "X"        -> "{0x00112233,0x4455,...,{...}}"
//   DateTimeOffset null       -> General pattern + " +02:00"
//   DateTimeOffset "G"        -> General pattern only
using System;
using System.Globalization;

internal static class IFormattable1
{
    private static void Main()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;

        DateTime dt = new DateTime(2024, 2, 29, 13, 45, 6, 789);
        Console.WriteLine("[dt.default.inv] " + ((IFormattable)dt).ToString(null, inv));
        Console.WriteLine("[dt.G.inv] " + ((IFormattable)dt).ToString("G", inv));
        Console.WriteLine("[dt.d.inv] " + ((IFormattable)dt).ToString("d", inv));
        Console.WriteLine("[dt.s.inv] " + ((IFormattable)dt).ToString("s", inv));
        Console.WriteLine("[dt.O.inv] " + ((IFormattable)dt).ToString("O", inv));
        Console.WriteLine("[dt.custom.inv] " + ((IFormattable)dt).ToString("yyyy-MM-dd HH:mm:ss", inv));
        Console.WriteLine("[dt.year.null] " + ((IFormattable)dt).ToString("yyyy", null));

        TimeSpan ts = new TimeSpan(1, 2, 3, 4, 5);
        Console.WriteLine("[ts.default.inv] " + ((IFormattable)ts).ToString(null, inv));
        Console.WriteLine("[ts.c.inv] " + ((IFormattable)ts).ToString("c", inv));
        Console.WriteLine("[ts.G.inv] " + ((IFormattable)ts).ToString("G", inv));
        Console.WriteLine("[ts.custom.inv] " + ((IFormattable)ts).ToString(@"dd\.hh\:mm\:ss", inv));
        TimeSpan neg = new TimeSpan(0, -1, -2, -3, -4);
        Console.WriteLine("[ts.neg.c.inv] " + ((IFormattable)neg).ToString("c", inv));

        Guid g = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        Console.WriteLine("[guid.default.inv] " + ((IFormattable)g).ToString(null, inv));
        Console.WriteLine("[guid.D.inv] " + ((IFormattable)g).ToString("D", inv));
        Console.WriteLine("[guid.N.inv] " + ((IFormattable)g).ToString("N", inv));
        Console.WriteLine("[guid.B.inv] " + ((IFormattable)g).ToString("B", inv));
        Console.WriteLine("[guid.P.inv] " + ((IFormattable)g).ToString("P", inv));
        Console.WriteLine("[guid.X.inv] " + ((IFormattable)g).ToString("X", inv));

        // System.DateTimeOffset also implements IFormattable in net48; those rows
        // are pinned by dtoiformattable1 (its box/unbox arms and formatter are
        // wired now), so this case intentionally stops at the four receivers it
        // was written for.

        int negi = -1;
        Console.WriteLine("[i32.X.inv] " + ((IFormattable)negi).ToString("X", inv));
        decimal dec = 1.5m;
        Console.WriteLine("[dec.F2.inv] " + ((IFormattable)dec).ToString("F2", inv));
    }
}