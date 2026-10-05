using System;
namespace Cases { public static class ConvertCase {
    // Empirically pin the .NET Framework 4.8 System.Convert semantics that the
    // runtime icalls must match (net48 is the project's reference baseline):
    //   * Convert.ToInt32(null)/ToInt64(null)/ToDouble(null) == 0 -- the
    //     "null means zero" rule that DIFFERS from int.Parse/Int64.Parse (which
    //     throw ArgumentNullException) -- the Convert family is documented to
    //     return 0 for a null input.
    //   * Convert.ToInt32(double)/ToInt32(long)/ToChar(int) are CHECKED
    //     conversions: out-of-range input throws OverflowException (raw C++
    //     casts would truncate silently -- the icalls must range-check).
    //   * Convert.ToBoolean(int) is "non-zero -> true".
    //   * Convert.ToString family mirrors the formatting icalls.
    // Exceptions are printed as their TYPE NAME only (messages are
    // OS-localized and not stable for byte-diffing).
    public static void Main() {
        Console.WriteLine(Convert.ToString(Convert.ToInt32(null)));                // 0
        Console.WriteLine(Convert.ToString(Convert.ToInt64(null)));                // 0
        Console.WriteLine(Convert.ToString(Convert.ToDouble(null)));               // 0
        Console.WriteLine(Convert.ToString(Convert.ToInt32("42")));                // 42
        Console.WriteLine(Convert.ToString(Convert.ToInt32("  -7  ")));            // -7
        Console.WriteLine(Convert.ToString(Convert.ToInt32("2147483647")));        // 2147483647
        Console.WriteLine(Convert.ToString(Convert.ToInt64("9223372036854775807"))); // 9223372036854775807
        Console.WriteLine(Convert.ToString(Convert.ToInt64("-9223372036854775808"))); // -9223372036854775808
        Console.WriteLine(Convert.ToString(Convert.ToDouble("3.5")));              // 3.5
        Console.WriteLine(Convert.ToString(Convert.ToDouble("  -1.25e2  ")));      // -125
        Console.WriteLine(Convert.ToString(Convert.ToInt32(2.9)));                 // 2
        Console.WriteLine(Convert.ToString(Convert.ToInt32(-2.9)));                // -2
        Console.WriteLine(Convert.ToString(Convert.ToDouble(42)));                 // 42
        Console.WriteLine(Convert.ToString(123));                                  // 123
        Console.WriteLine(Convert.ToString(-45L));                                 // -45
        Console.WriteLine(Convert.ToString(1.5));                                  // 1.5
        Console.WriteLine(Convert.ToString(true));                                 // True
        Console.WriteLine(Convert.ToString(false));                                // False
        Console.WriteLine(Convert.ToString(Convert.ToBoolean(0)));                 // False
        Console.WriteLine(Convert.ToString(Convert.ToBoolean(1)));                 // True
        Console.WriteLine(Convert.ToString(Convert.ToBoolean(42)));                // True
        Console.WriteLine(Convert.ToString(Convert.ToChar(65)));                   // A
        Console.WriteLine(Convert.ToString(Convert.ToInt32('A')));                 // 65
        try { Console.WriteLine(Convert.ToString(Convert.ToInt32("abc"))); }
        catch (Exception e) { Console.WriteLine("ToInt32(abc)=" + e.GetType().Name); }   // FormatException
        try { Console.WriteLine(Convert.ToString(Convert.ToInt64("9223372036854775808"))); }
        catch (Exception e) { Console.WriteLine("ToInt64(overflow)=" + e.GetType().Name); } // OverflowException
        try { Console.WriteLine(Convert.ToString(Convert.ToInt32(2147483648L))); }
        catch (Exception e) { Console.WriteLine("ToInt32(2147483648L)=" + e.GetType().Name); } // OverflowException
        try { Console.WriteLine(Convert.ToString(Convert.ToInt32(2147483648.0))); }
        catch (Exception e) { Console.WriteLine("ToInt32(2147483648.0)=" + e.GetType().Name); } // OverflowException
        try { Console.WriteLine(Convert.ToString(Convert.ToInt32(double.NaN))); }
        catch (Exception e) { Console.WriteLine("ToInt32(NaN)=" + e.GetType().Name); } // OverflowException
        try { Console.WriteLine(Convert.ToString(Convert.ToChar(0x1F600))); }
        catch (Exception e) { Console.WriteLine("ToChar(0x1F600)=" + e.GetType().Name); } // checked: OverflowException
        try { Console.WriteLine(Convert.ToString(Convert.ToChar(-1))); }
        catch (Exception e) { Console.WriteLine("ToChar(-1)=" + e.GetType().Name); } // OverflowException
        // Rounding pins (net48 Convert.ToInt32(double) = round half-to-even,
        // Math.Round semantics -- NOT truncation, NOT away-from-zero):
        Console.WriteLine(Convert.ToString(Convert.ToInt32(2.5)));                 // 2 (even neighbor)
        Console.WriteLine(Convert.ToString(Convert.ToInt32(-2.5)));                // -2 (even neighbor)
        Console.WriteLine(Convert.ToString(Convert.ToInt32(3.5)));                 // 4 (even neighbor)
        Console.WriteLine(Convert.ToString(Convert.ToInt32(-3.5)));                // -4 (even neighbor)
        Console.WriteLine(Convert.ToString(Convert.ToInt32(2147483646.5)));        // 2147483646 (even)
        // Boundary pins: the ROUNDED value must lie in [INT32_MIN, 2^31):
        try { Console.WriteLine(Convert.ToString(Convert.ToInt32(2147483647.0))); }
        catch (Exception e) { Console.WriteLine("ToInt32(2147483647.0)=" + e.GetType().Name); } // legal -> 2147483647
        try { Console.WriteLine(Convert.ToString(Convert.ToInt32(-2147483648.0))); }
        catch (Exception e) { Console.WriteLine("ToInt32(-2147483648.0)=" + e.GetType().Name); } // legal -> -2147483648
        try { Console.WriteLine(Convert.ToString(Convert.ToInt32(2147483647.5))); }
        catch (Exception e) { Console.WriteLine("ToInt32(2147483647.5)=" + e.GetType().Name); } // rounds to 2^31: OverflowException
        try { Console.WriteLine(Convert.ToString(Convert.ToInt32(-2147483648.5))); }
        catch (Exception e) { Console.WriteLine("ToInt32(-2147483648.5)=" + e.GetType().Name); } // rounds to -2^31: legal
    }
}}
