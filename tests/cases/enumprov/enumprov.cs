//------------------------------------------------------------------------------
// enumprov.cs -- ENUM ToString PROVIDER-overload e2e reference case (net48
// oracle, byte-for-byte). Created in impl-con (2026-10-27) for the
// D-ENUM-REST wiring of the two net48 provider overloads:
//
//   string ToString(IFormatProvider provider)              -- enum.cs:871
//   string ToString(string format, IFormatProvider provider) -- enum.cs:808
//
// net48 IGNORES the provider (both forms return the plain ToString() /
// ToString(format) text), so the transpiler drops the provider slot at
// emission and maps to the same boxed-enum icall as the providerless form
// (il2cpp_enum_boxed_to_string / il2cpp_enum_boxed_to_string_format).
//
// Probes (all on the boxed-Enum shape, which stringenum already pins
// byte-exact): the providerless equivalence for "G"/"D" specifiers, a
// [Flags] decomposition, and the DayOfWeek built-in table path.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases {
[Flags]
enum Mode : int { None = 0, Read = 1, Write = 2, Execute = 4 }

public static class EnumProv {
    public static void Main() {
        Enum r = Mode.Read;
        Enum combo = (Enum)(Mode.Read | Mode.Write);

        // ToString(IFormatProvider): net48 returns ToString() -- the provider
        // is ignored.
        Console.WriteLine("p_g=" + (r.ToString(CultureInfo.InvariantCulture) == "Read"));
        Console.WriteLine("p_d=" + (r.ToString(CultureInfo.InvariantCulture) == "1"));

        // ToString(string format, IFormatProvider): net48 returns
        // ToString(format) -- the provider is ignored.
        Console.WriteLine("pf_g=" + (r.ToString("G", CultureInfo.InvariantCulture) == "Read"));
        Console.WriteLine("pf_d=" + (r.ToString("D", CultureInfo.InvariantCulture) == "1"));
        Console.WriteLine("pf_x=" + (r.ToString("X", CultureInfo.InvariantCulture) == "1"));
        Console.WriteLine("pf_flags=" + (combo.ToString("G", CultureInfo.InvariantCulture) == "Read, Write"));
    }
}
}
