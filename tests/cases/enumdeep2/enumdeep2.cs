//------------------------------------------------------------------------------
// enumdeep2.cs -- ENUM DEEP e2e reference case (net48 oracle, byte-for-byte).
// Created in e2e batch C10 (2026-08-09).
//
// Goes DEEPER than enumdeep/enumflags on the enum2-fixed surface (the
// 2026-08-08 runtime audit: Parse trim/width/Equals i64 -- enumext.cpp):
//   * Enum.GetUnderlyingType(typeof(T)) == typeof(int): the ABI FIX
//     (MethodMap.Enum.cs) drops the folded typeof arg and the null
//     descriptor takes the Int32 no-metadata path -> "System.Int32";
//     typeof(int) folds to the same string -> Type.op_Equality (string
//     equality) -> True. EXACT for every int-backed enum.
//   * Enum.Equals via a BOXED enum reference (il2cpp_enum_equals, the
//     enum2 i64-payload fix): same value -> True, different -> False,
//     cross-type -> False.
//   * Bitwise combination deep: |, &, ^, ~, |=, compound masks, unnamed
//     combos, zero/All round-trips, int casts (the enumflags surface,
//     verified PASS -- deeper mask arithmetic here).
//   * ToString format probes (Console.WriteLine(boxed enum)): RECORDED GAP
//     -- the enum register emission (il2cpp_enum_register in generated
//     startup, MethodMap.Enum.cs REGISTER WIRING REPORT item c) has NOT
//     landed, so a boxed USER enum formats NUMERIC (the Int32 no-metadata
//     path) instead of the member NAME; net48 prints the name. Labeled
//     below, the byte diff documents the gap.
//
// NOT PROBED (transpile-blocking, recorded as gaps -- do not uncomment):
//   * Enum.Parse / TryParse -- LOUD NotSupportedException in MethodMap.
//     Enum.cs (the runtime il2cpp_enum_parse is TryParse-shaped with an
//     int64_t* out slot; no ref/out support in the M-stage transpiler).
//     The enum2 Parse trim/width/Equals fixes live on the RUNTIME side and
//     are exercised by tests/runtime/test_enum* (unit), not reachable e2e.
//   * Enum.ToString(string format) / HasFlag / GetNames / GetValues --
//     LOUD (MethodMap.Enum.cs).
//   * `Color.Red.ToString()` VALUE-shape -- build-aborting bad cast in
//     generated C++ (enumdeep's known failure, unchanged).
//------------------------------------------------------------------------------
using System;

namespace Cases {
enum Color : int { Red = 1, Green = 2, Blue = 4 }
[Flags]
enum Access : int { None = 0, Read = 1, Write = 2, Execute = 4, All = Read | Write | Execute }

public static class EnumDeep2 {
    public static void Main() {
        // ============ A. Enum.GetUnderlyingType (ABI-fixed) ============
        Console.WriteLine("ug_int=" + (Enum.GetUnderlyingType(typeof(Color)) == typeof(int)));
        Console.WriteLine("ug_access=" + (Enum.GetUnderlyingType(typeof(Access)) == typeof(int)));
        Console.WriteLine("ug_neq=" + (Enum.GetUnderlyingType(typeof(Color)) == typeof(long)));

        // ============ B. Enum.Equals via boxed references ============
        Enum r1 = Color.Red;
        Enum r2 = Color.Red;
        Enum g1 = Color.Green;
        Enum a1 = Access.Read;
        Console.WriteLine("eq_same=" + r1.Equals(r2));
        Console.WriteLine("eq_diff=" + r1.Equals(g1));
        Console.WriteLine("eq_crosstype=" + r1.Equals(a1));
        Console.WriteLine("eq_null=" + (r1.Equals(null) ? 1 : 0));

        // ============ C. bitwise combination deep ============
        Access p = Access.None;
        p |= Access.Read | Access.Write;
        Console.WriteLine("bw_or=" + ((int)p == 3));
        p |= Access.Execute;
        Console.WriteLine("bw_accum=" + (p == Access.All));
        p &= ~Access.Read;
        Console.WriteLine("bw_clear=" + ((int)p == 6));
        Console.WriteLine("bw_and=" + ((Access.Read | Access.Write) == (Access)3));
        Console.WriteLine("bw_xor=" + ((int)(Access.Read ^ Access.Write) == 3));
        Console.WriteLine("bw_not=" + ((int)~Access.None == -1));
        Console.WriteLine("bw_unnamed=" + ((Access)5 == (Access.Read | Access.Execute)));
        Console.WriteLine("bw_zero=" + ((int)(Access.None & Access.All) == 0));
        Console.WriteLine("bw_all=" + ((int)(Access.Read | Access.All) == 7));
        Console.WriteLine("bw_member=" + (((Access.Write | Access.Execute) & Access.Write) != Access.None));
        Console.WriteLine("bw_nomember=" + (((Access.Write | Access.Execute) & Access.Read) == Access.None));
        Console.WriteLine("bw_intcast=" + ((int)(Access.All ^ Access.Execute) == 3));
        Console.WriteLine("bw_color=" + ((Color.Red | Color.Blue) == (Color)5));
        Console.WriteLine("bw_colornot=" + ((int)~Color.Red == -2));

        // ============ D. RECORDED GAP: boxed-enum ToString ============
        // A boxed user enum (Enum-typed reference) routes to
        // il2cpp_enum_to_string (the wired System.Enum owner shape). net48
        // prints the member NAME ("Red"); the transpiled build prints the
        // numeric decimal ("1") -- the enum register emission has not
        // landed (MethodMap.Enum.cs REGISTER WIRING REPORT item c; the
        // runtime null-descriptor path formats numeric). The byte diff
        // below is the gap documentation. (The VALUE-shape
        // `Color.Red.ToString()` is build-broken -- enumdeep's known bad
        // cast -- so this reference shape is the reachable form.)
        Enum ts = Color.Red;
        Console.WriteLine("ts_red=" + ts.ToString());
    }
}}
