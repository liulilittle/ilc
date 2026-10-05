//------------------------------------------------------------------------------
// enumdeep3.cs -- ENUM DEEP e2e reference case (net48 oracle, byte-for-byte).
// Created in e2e batch C11 (2026-08-09).
//
// Goes DEEPER than enumdeep/enumflags/enumdeep2 on the enum surface, with
// FOUR focus areas:
//   A. Enum.Parse ALL FORMS (name/numeric/trim/ignoreCase/malformed +
//      TryParse<T>) -- RECORDED GAP, NOT ACTIVE (see D-ENM-1 below; the
//      net48 oracle values are shown in comments).
//   B. ToString on the boxed-enum reference shape (Enum-typed reference ->
//      il2cpp_enum_to_string): net48 prints the member NAME; ours prints
//      the numeric decimal (D-ENM-3 -- the transpiler's enum:l cast still
//      passes a NULL class descriptor, and the null-descriptor fallback
//      SUPPRESSES the builtin name for any value owned by a registered USER
//      enum -- enumext.cpp il2cpp_enum_to_string, IsValueOfRegisteredNonBuiltin).
//   C. Enum.GetUnderlyingType for EACH backing -- RECORDED GAP for non-int
//      backings (D-ENM-4): the metadata loader CANNOT even load an enum
//      whose value__ field is byte/sbyte/short/ushort/uint/long/ulong
//      ("field type byte 0x05 not supported in M2", MetadataLoader.cs:615),
//      so the whole assembly aborts at TRANSPILE -- the byte/long/uint
//      probes below are commented with their net48 oracle values. The
//      int-backed probes are ACTIVE (the ABI fix makes them exact).
//   D. Equals CROSS-TYPE (il2cpp_enum_equals, boxed references): same value
//      + same type -> True; same value + different type -> False; null ->
//      False. ACTIVE (int-backed enums only, since non-int backings cannot
//      load -- D-ENM-4).
//
// RECORDED GAPS (see the batch C11 report):
//   D-ENM-1 (transpile-BLOCKING): Enum.Parse / TryParse<T> are LOUD
//     NotSupportedException in MethodMap.Enum.cs:212-214 (TryParse-shaped
//     il2cpp_enum_parse with an int64_t* out slot; no ref/out support in the
//     M-stage transpiler). Probe lines below are commented; the net48 oracle
//     (p_*/tp_* values) is in ref_out.txt / expected.txt.
//   D-ENM-4 (metadata-load, transpile-BLOCKING): a NON-INT enum backing
//     (byte/sbyte/short/ushort/uint/long/ulong value__ field) aborts
//     MetadataLoader.DecodeFieldSignature ("field type byte 0x05 not
//     supported in M2") -- the assembly cannot load at all. This is the
//     reason every non-int backing probe (ug_byte/ug_long/...) is commented.
//   D-ENM-3 (ToString): boxed user enum ToString prints the numeric decimal
//     (net48: the member NAME) -- the null-descriptor interim ABI, same
//     family as enumdeep2's ts_red gap.
//
// Byte-stability: every ACTIVE line prints "label=True/False" or a label+int.
//------------------------------------------------------------------------------
using System;

namespace Cases {
enum EInt : int { A = 1, B = 2 }
[Flags]
enum EFlags : int { None = 0, F1 = 1, F2 = 2, F3 = 4 }

// RECORDED GAP D-ENM-4 -- non-int backings cannot LOAD in the transpiler
// ("field type byte 0x05 not supported in M2"). Kept for the net48 oracle
// (comment out to re-enable when D-ENM-4 lands):
//   enum EByte  : byte   { X = 1 }
//   enum ELong  : long   { P1 = 1L }
//   enum EUInt  : uint   { Q1 = 1 }
//   enum ESByte : sbyte  { S1 = -1 }
//   enum EShort : short  { H1 = 1 }

public static class EnumDeep3 {
    public static void Main() {
        // ============ A. Enum.Parse ALL FORMS (D-ENM-1 gap, net48 oracle) ============
        //   p_name:        Enum.Parse(typeof(EInt), "A") == EInt.A            -> True
        //   p_numeric:     Enum.Parse(typeof(EInt), "1") == EInt.A            -> True
        //   p_trim:        Enum.Parse(typeof(EInt), " 2 ") == EInt.B          -> True
        //   p_ignorecase:  Enum.Parse(typeof(EInt), "a", true) == EInt.A      -> True
        //   p_badname:     Enum.Parse(typeof(EInt), "NoSuch") throws ArgumentException
        //   p_badnumeric:  Enum.Parse(typeof(EInt), "99") throws ArgumentException
        //   tp_success:    Enum.TryParse<EInt>("A", out v) && v == EInt.A     -> True
        //   tp_fail:      !Enum.TryParse<EInt>("NoSuch", out v) && v == EInt.A-> True

        // ============ B. ToString (boxed Enum reference; D-ENM-3) ============
        Enum ts1 = EInt.A;
        Console.WriteLine("ts_a=" + (ts1.ToString() == "A"));   // net48 True; ours False (numeric "1")
        Enum ts2 = EInt.B;
        Console.WriteLine("ts_b=" + (ts2.ToString() == "B"));   // net48 True; ours False (numeric "2")
        Enum ts3 = (Enum)(EFlags.F1 | EFlags.F2);
        Console.WriteLine("ts_flags=" + (ts3.ToString() == "F1, F2")); // net48 True; ours False (numeric "3")

        // ============ C. Enum.GetUnderlyingType (int ACTIVE; non-int = D-ENM-4) ============
        Console.WriteLine("ug_int=" + (Enum.GetUnderlyingType(typeof(EInt)) == typeof(int)));      // True both
        Console.WriteLine("ug_flags=" + (Enum.GetUnderlyingType(typeof(EFlags)) == typeof(int)));  // True both
        Console.WriteLine("ug_neq_long=" + (Enum.GetUnderlyingType(typeof(EInt)) == typeof(long))); // False both
        //   net48 oracle for the non-int backings (D-ENM-4 blocked):
        //   ug_byte:   Enum.GetUnderlyingType(typeof(EByte))  == typeof(byte)  -> True
        //   ug_long:   Enum.GetUnderlyingType(typeof(ELong))  == typeof(long)  -> True
        //   ug_uint:   Enum.GetUnderlyingType(typeof(EUInt))  == typeof(uint)  -> True
        //   ug_sbyte:  Enum.GetUnderlyingType(typeof(ESByte)) == typeof(sbyte) -> True
        //   ug_short:  Enum.GetUnderlyingType(typeof(EShort)) == typeof(short) -> True

        // ============ D. Equals CROSS-TYPE (il2cpp_enum_equals) ============
        Enum i1 = EInt.A;
        Enum i2 = EInt.A;
        Enum f1 = EFlags.F1;
        Enum f2 = (Enum)(EFlags.F2 | EFlags.F1);
        Console.WriteLine("eq_same=" + i1.Equals(i2));                          // True both
        Console.WriteLine("eq_diffval=" + i1.Equals(EInt.B));                   // False both
        Console.WriteLine("eq_cross=" + i1.Equals(f1));                         // False both (type gate)
        Console.WriteLine("eq_cross_sameval=" + i1.Equals((object)1));         // False (EInt vs boxed Int32 1)
        Console.WriteLine("eq_null=" + (i1.Equals(null) ? 1 : 0));              // 0 both
        Console.WriteLine("eq_flags_cross=" + f1.Equals(f2));                   // False (same type, values 1 vs 3)
    }
}}
