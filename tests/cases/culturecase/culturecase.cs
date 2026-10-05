using System;
using System.Globalization;

namespace Cases { public static class CultureCase {
    public static void Main() {
        // --- current culture does not crash (runtime pins "en-US" / host net48 zh-CN) ---
        Console.WriteLine(CultureInfo.CurrentCulture != null);                 // net48: true
        Console.WriteLine(CultureInfo.CurrentCulture.Name != null);            // net48: true
        // --- number separators (getters wired: folded to invariant literals "." / ",") ---
        Console.WriteLine(NumberFormatInfo.CurrentInfo.NumberDecimalSeparator == "."); // net48: true (zh-CN)
        Console.WriteLine(NumberFormatInfo.CurrentInfo.NumberGroupSeparator == ",");   // net48: true (zh-CN)
        Console.WriteLine(NumberFormatInfo.InvariantInfo.NumberDecimalSeparator == "."); // net48: true
        Console.WriteLine(NumberFormatInfo.InvariantInfo.NumberGroupSeparator == ",");   // net48: true
        // --- ToString is culture-independent (fixed values; the IFormatProvider
        //     argument is dropped at the call site) ---
        Console.WriteLine((1234.5).ToString("F2", CultureInfo.InvariantCulture) == "1234.50"); // net48: true
        Console.WriteLine((1234.5).ToString("F2", CultureInfo.CurrentCulture) == "1234.50");   // net48: true (zh-CN)
        Console.WriteLine(1234567.ToString("N0", CultureInfo.InvariantCulture) == "1,234,567"); // net48: true
        // GAP (not asserted, see docs/audit): CultureInfo.InvariantCulture.Name == ""
        //   -- transpile fails "callvirt on non-instance method": get_InvariantCulture
        //   folds to null (no object model), the .Name chain is not wired; the
        //   runtime icall il2cpp_culture_get_name exists but this path is
        //   unreachable. InvariantCulture == null is also a design divergence
        //   (net48: false).
    }
}}
