using System;
using System.Globalization;
namespace Cases { public static class LoudRest1DtParse3 {
    public static void Main() {
        // DateTime.Parse(string, IFormatProvider, DateTimeStyles) -- the 3-arg
        // net48 overload (previously a LOUD rejection -- "the runtime has only
        // the (string) icall"). The provider is dropped; the styles enum rides
        // the invariant subset's documented behavior.
        var dt = DateTime.Parse("2020-01-02", CultureInfo.InvariantCulture, DateTimeStyles.None);
        Console.WriteLine(dt.ToString("yyyy-MM-dd"));
        var dt2 = DateTime.Parse("03/04/2021", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
        Console.WriteLine(dt2.ToString("yyyy-MM-dd"));
    }
}}
