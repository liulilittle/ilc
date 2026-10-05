using System;

// -----------------------------------------------------------------------------
// strfmt.cs -- string formatting deep-chain probe (formatext domain).
//
// Design: EVERY output line is a bool produced by comparing the formatted
// value against a net48-known literal IN-PROCESS. The diff therefore compares
// only "True"/"False" lines -- immune to the encoding/culture oracle
// artifacts that break byte-diffs of raw formatter output (formatext's U+00A5
// currency symbol, U+221E infinity, exponent padding, culture decimal
// separators).
//
// Wired subset exercised (MethodMap.Format.cs -- icall_format_int32_custom /
// icall_format_double_custom, see il2cpp_format_ext.h):
//   int32:    ToString("D5") / ToString("X4") / ToString("N2")
//   double:   ToString("F2") / ToString("E2")
//   default:  parameterless ToString() of int/long/double (ValueType icalls)
//   edge:     negative values, zero, MinValue/MaxValue, rounding probes
//
// CLOSED 2026-08-06 (strfmt workorder): string.Format is WIRED -- the ps
// dispatch in MethodMap.String.cs maps Format(string[, object...]) to
// icall_string_format_0/1/2/3 and Format(string, params object[]) to
// icall_string_format (stringbuilder.cpp, AppendFormatCore kernel, net48
// semantics). The 3 Format probes below print True.
// -----------------------------------------------------------------------------
class Program
{
    static void Main()
    {
        // --- int32 ToString("D5") (wired: icall_format_int32_custom) ---
        Console.WriteLine((42).ToString("D5") == "00042");
        Console.WriteLine((-42).ToString("D5") == "-00042");
        Console.WriteLine(0.ToString("D5") == "00000");
        Console.WriteLine(int.MaxValue.ToString("D5") == "2147483647");
        Console.WriteLine(int.MinValue.ToString("D5") == "-2147483648");
        Console.WriteLine((-123).ToString("D5") == "-00123");

        // --- int32 ToString("X4") (wired) ---
        Console.WriteLine(255.ToString("X4") == "00FF");
        Console.WriteLine(0.ToString("X4") == "0000");
        Console.WriteLine((-1).ToString("X4") == "FFFFFFFF");
        Console.WriteLine((-123).ToString("X4") == "FFFFFF85");

        // --- int32 ToString("N2") (wired) ---
        Console.WriteLine(12345.ToString("N2") == "12,345.00");
        Console.WriteLine((-12345).ToString("N2") == "-12,345.00");
        Console.WriteLine(0.ToString("N2") == "0.00");
        Console.WriteLine(123.ToString("N2") == "123.00");

        // --- double ToString("F2") (wired: icall_format_double_custom) ---
        Console.WriteLine(1234.5678.ToString("F2") == "1234.57");
        Console.WriteLine((-1234.5678).ToString("F2") == "-1234.57");
        Console.WriteLine(0.0.ToString("F2") == "0.00");
        Console.WriteLine(0.001234.ToString("F2") == "0.00");
        Console.WriteLine((9.99).ToString("F1") == "10.0");
        Console.WriteLine((2.5).ToString("F0") == "3");
        Console.WriteLine((-2.5).ToString("F0") == "-3");
        Console.WriteLine((0.005).ToString("F2") == "0.01");
        Console.WriteLine(double.NaN.ToString("F2") == "NaN");
        Console.WriteLine((-0.0).ToString("F2") == "0.00");

        // --- double ToString("E2") (wired) ---
        Console.WriteLine(1234.5678.ToString("E2") == "1.23E+003");
        Console.WriteLine((-1234.5678).ToString("E2") == "-1.23E+003");
        Console.WriteLine(0.001234.ToString("E2") == "1.23E-003");
        Console.WriteLine(0.0.ToString("E2") == "0.00E+000");
        Console.WriteLine((1e100).ToString("E2") == "1.00E+100");

        // --- default ToString() (wired: icall_int32_to_string / icall_int64 /
        //     icall_double_to_string -- G15 for double) ---
        Console.WriteLine(42.ToString() == "42");
        Console.WriteLine((-42).ToString() == "-42");
        Console.WriteLine(0.ToString() == "0");
        Console.WriteLine(int.MaxValue.ToString() == "2147483647");
        Console.WriteLine(int.MinValue.ToString() == "-2147483648");
        Console.WriteLine(1234567890123456789L.ToString() == "1234567890123456789");
        Console.WriteLine(long.MinValue.ToString() == "-9223372036854775808");
        Console.WriteLine(3.5.ToString() == "3.5");
        Console.WriteLine((1.0 / 3.0).ToString() == "0.333333333333333");
        Console.WriteLine((0.1 + 0.2).ToString() == "0.3");
        Console.WriteLine(1e-4.ToString() == "0.0001");
        Console.WriteLine((-0.0).ToString() == "0");

        // --- string.Format (wired 2026-08-06: MethodMap.String.cs ps
        //     dispatch -> icall_string_format_0/1/2/3 + params form,
        //     stringbuilder.cpp AppendFormatCore kernel, net48 semantics) ---
        Console.WriteLine(string.Format("{0}-{1}", 42, 7) == "42-7");
        Console.WriteLine(string.Format("v={0}", 3.5) == "v=3.5");
        Console.WriteLine(string.Format("{0}", "abc") == "abc");
    }
}
