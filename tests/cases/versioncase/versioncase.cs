using System;
namespace Cases { public static class VersionCase {
    public static void Main() {
        // Version domain: ctor -> segment accessors -> ToString -> CompareTo/==/Equals -> Parse errors.
        // All bool probes (culture-independent; no int ToString, per e2e convention).
        Version v = new Version("1.2.3.4");
        Console.WriteLine(v.Major == 1);                        // True
        Console.WriteLine(v.Minor == 2);                        // True
        Console.WriteLine(v.Build == 3);                        // True
        Console.WriteLine(v.Revision == 4);                     // True
        Console.WriteLine(v.ToString() == "1.2.3.4");           // True
        Console.WriteLine(new Version("10.20.30").ToString() == "10.20.30");   // True
        Console.WriteLine(new Version("2.0").ToString() == "2.0");             // True
        Console.WriteLine(new Version(2, 0).ToString() == "2.0");              // True
        Console.WriteLine(new Version(3, 1, 2).ToString() == "3.1.2");         // True
        Console.WriteLine(new Version(1, 2, 3, 4).ToString() == "1.2.3.4");    // True
        Console.WriteLine(new Version().ToString() == "0.0");   // True
        Console.WriteLine(new Version(2, 0).Build == -1);       // True (absent build)
        Console.WriteLine(new Version(2, 0).Revision == -1);    // True (absent revision)
        // CompareTo / == / != / Equals (same-shape comparisons only)
        Console.WriteLine(v.CompareTo(new Version("1.2.3.4")) == 0);   // True
        Console.WriteLine(v.CompareTo(new Version("1.2.3.5")) < 0);    // True
        Console.WriteLine(v.CompareTo(new Version("1.2.3.3")) > 0);    // True
        Console.WriteLine(new Version(2, 0).CompareTo(new Version(1, 9)) > 0);  // True
        Console.WriteLine(new Version(1, 9).CompareTo(new Version(2, 0)) < 0);  // True
        Console.WriteLine(v == new Version("1.2.3.4"));         // True
        Console.WriteLine(v != new Version("1.2.3.5"));         // True
        Console.WriteLine(v.Equals(new Version("1.2.3.4")));    // True
        Console.WriteLine(new Version("1.2.3.4").Equals(v));    // True
        // Parse invalid input caught (probe -> True on expected exception type)
        try { Version.Parse("abc"); Console.WriteLine(false); } catch (ArgumentException) { Console.WriteLine(true); }
        try { Version.Parse("1.2.3.4.5"); Console.WriteLine(false); } catch (ArgumentException) { Console.WriteLine(true); }
        try { new Version(1, -1); Console.WriteLine(false); } catch (ArgumentOutOfRangeException) { Console.WriteLine(true); }
    }
}}
