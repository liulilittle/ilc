using System;
namespace Cases { public static class ParseCase {
    public static void Main() {
        int i; long l; double d; bool b; byte y;

        // int.TryParse: valid forms
        Console.WriteLine(int.TryParse("42", out i) ? i.ToString() : "fail");          // 42
        Console.WriteLine(int.TryParse("  -42  ", out i) ? i.ToString() : "fail");     // -42
        Console.WriteLine(int.TryParse("+42", out i) ? i.ToString() : "fail");         // 42
        Console.WriteLine(int.TryParse("00042", out i) ? i.ToString() : "fail");       // 42
        Console.WriteLine(int.TryParse("2147483647", out i) ? i.ToString() : "fail"); // 2147483647
        Console.WriteLine(int.TryParse("-2147483648", out i) ? i.ToString() : "fail");// -2147483648
        // int.TryParse: invalid forms -> fail
        Console.WriteLine(int.TryParse("2147483648", out i) ? i.ToString() : "fail"); // fail
        Console.WriteLine(int.TryParse("", out i) ? i.ToString() : "fail");           // fail
        Console.WriteLine(int.TryParse("  ", out i) ? i.ToString() : "fail");         // fail
        Console.WriteLine(int.TryParse("12a", out i) ? i.ToString() : "fail");        // fail
        Console.WriteLine(int.TryParse(null, out i) ? i.ToString() : "fail");         // fail

        // long.TryParse
        Console.WriteLine(long.TryParse("9223372036854775807", out l) ? l.ToString() : "fail");  // 9223372036854775807
        Console.WriteLine(long.TryParse("-9223372036854775808", out l) ? l.ToString() : "fail"); // -9223372036854775808
        Console.WriteLine(long.TryParse("99999999999999999999", out l) ? l.ToString() : "fail"); // fail

        // byte.TryParse
        Console.WriteLine(byte.TryParse("255", out y) ? y.ToString() : "fail");  // 255
        Console.WriteLine(byte.TryParse("256", out y) ? y.ToString() : "fail");  // fail
        Console.WriteLine(byte.TryParse("-1", out y) ? y.ToString() : "fail");   // fail

        // bool.TryParse (case-insensitive)
        Console.WriteLine(bool.TryParse("true", out b) ? b.ToString() : "fail");  // True
        Console.WriteLine(bool.TryParse("FALSE", out b) ? b.ToString() : "fail"); // False
        Console.WriteLine(bool.TryParse(" TruE ", out b) ? b.ToString() : "fail");// True
        Console.WriteLine(bool.TryParse("1", out b) ? b.ToString() : "fail");     // fail

        // double.TryParse ("NaN" works on net48 zh-CN; the Infinity family
        // FAILS on this machine because zh-CN's infinity symbol is U+221E --
        // the ASCII "Infinity" word is culture-dependent on net48. The
        // runtime follows the InvariantCulture simplification (accepts
        // "Infinity"/"-Infinity"), matching il2cpp_format.h's Parse family
        // and en-US net48 behaviour. Rows below show the zh-CN net48
        // reference output; documented deviation for the ASCII spelling.)
        Console.WriteLine(double.TryParse("NaN", out d) ? d.ToString() : "fail");       // NaN
        Console.WriteLine(double.TryParse(" NaN ", out d) ? d.ToString() : "fail");     // NaN
        Console.WriteLine(double.TryParse("Infinity", out d) ? "inf-ok" : "fail");      // fail (zh-CN culture)
        Console.WriteLine(double.TryParse("-Infinity", out d) ? "ninf-ok" : "fail");    // fail (zh-CN culture)
        Console.WriteLine(double.TryParse("+Infinity", out d) ? "pinf-ok" : "fail");    // fail
        Console.WriteLine(double.TryParse("1e999", out d) ? "ok" : "fail");             // fail (overflow)
        Console.WriteLine(double.TryParse("1.5e-3", out d) ? d.ToString() : "fail");    // 0.0015
        Console.WriteLine(double.TryParse("  -2.25  ", out d) ? d.ToString() : "fail"); // -2.25

        // Convert.ToInt32(string, fromBase) -- net48 semantics (ParseNumbers.
        // StringToInt with IsTight|NoSpace): NO whitespace for any radix,
        // sign only for radix 10, radix 2/8/16 accumulate in uint32 with
        // wraparound (no range check -- "80000000",16 wraps to int.MinValue),
        // radix 10 does range-check (overflow -> OverflowException).
        Console.WriteLine(Convert.ToInt32("1010", 2).ToString());          // 10
        try { Console.WriteLine(Convert.ToInt32("-101", 2).ToString()); } catch (ArgumentException) { Console.WriteLine("threw"); }  // threw
        Console.WriteLine(Convert.ToInt32("7777777777", 8).ToString());    // 1073741823
        Console.WriteLine(Convert.ToInt32("FF", 16).ToString());           // 255
        try { Console.WriteLine(Convert.ToInt32("-80000000", 16).ToString()); } catch (ArgumentException) { Console.WriteLine("threw"); }  // threw
        Console.WriteLine(Convert.ToInt32("80000000", 16).ToString());     // -2147483648 (wraps)
        Console.WriteLine(Convert.ToInt32("FFFFFFFF", 16).ToString());     // -1 (wraps)
        try { Console.WriteLine(Convert.ToInt32(" 1F ", 16).ToString()); } catch (FormatException) { Console.WriteLine("threw"); }   // threw (no space)
        Console.WriteLine(Convert.ToInt32("-42", 10).ToString());          // -42
        Console.WriteLine(Convert.ToInt32("+42", 10).ToString());          // 42
        try { Console.WriteLine(Convert.ToInt32(" 42 ", 10).ToString()); } catch (FormatException) { Console.WriteLine("threw"); }   // threw (no space)
        try { Console.WriteLine(Convert.ToInt32("2147483648", 10).ToString()); } catch (OverflowException) { Console.WriteLine("threw"); }  // threw (overflow)
    }
}}
