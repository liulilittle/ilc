using System;
using System.Globalization;
namespace Cases { public static class LoudRest1CultLcid {
    public static void Main() {
        // CultureInfo.GetCultureInfo(int culture) -- the net48 LCID overload
        // (previously a LOUD rejection -- "expected (string)"). The handle-gap
        // carrier is the culture NAME string, so the chained get_Name resolves.
        Console.WriteLine(CultureInfo.GetCultureInfo(1033).Name);
        Console.WriteLine(CultureInfo.GetCultureInfo(2052).Name);
        Console.WriteLine(CultureInfo.GetCultureInfo(127).Name.Length == 0 ? "invariant" : "?");
        // CultureInfo.GetCultureInfo(string name, string altName) -- the net48
        // alias overload.
        Console.WriteLine(CultureInfo.GetCultureInfo("en-US", "en-US").Name);
    }
}}
