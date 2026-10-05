//------------------------------------------------------------------------------
// raudit4.cs -- REFLECTION AUDIT PROBE 4 (G6 static audit, suspicious
// #13/#14/#15): enum reflection over a long-backed enum.
//
// enum LE : long { A = 1, B = 0x100000000L }  (B = 4294967296, needs 64 bits)
//
// Probes:
//   * Enum.GetValues(typeof(LE)).Length -> net48: 2 (both sides same).
//   * ((Array)Enum.GetValues(typeof(LE))).GetValue(1) -> net48: 4294967296
//     (boxed Int64, no truncation). Static audit (enumext.cpp:1110-1128):
//     GetValues is hard-wired to an int32[] with static_cast<int32_t> ->
//     suspected 0 (truncated) and/or InvalidCastException on (Array) cast.
//   * Enum.GetUnderlyingType(typeof(LE)) == typeof(long) -> net48: True.
//     Static audit (MethodMap.Enum.cs:214-216 + 449): the descriptor is
//     dropped and the register table hard-codes Int32 -> suspected False.
//   * Enum.IsDefined(typeof(int), 1) -> net48: ArgumentException (the type
//     argument is not an enum). Static audit (enumext.cpp:1041-1056):
//     unregistered non-enum name -> returns 0, no throw -> suspected "False".
//
// try/catch prints "label_ex=<ExceptionTypeName>" when a side throws.
//------------------------------------------------------------------------------
using System;

namespace Cases {
enum LE : long { A = 1, B = 0x100000000L }

public static class Raudit4 {
    public static void Main()
    {
        Console.WriteLine("gv_len=" + Enum.GetValues(typeof(LE)).Length);
        Console.WriteLine("gv_b=" + ((Array)Enum.GetValues(typeof(LE))).GetValue(1));
        Console.WriteLine("ut_long=" + (Enum.GetUnderlyingType(typeof(LE)) == typeof(long)));
        try {
            Console.WriteLine("isdef_int=" + Enum.IsDefined(typeof(int), 1));
        } catch (Exception e) {
            Console.WriteLine("isdef_int_ex=" + e.GetType().Name);
        }
    }
}}
