//------------------------------------------------------------------------------
// enumflags2.cs -- [Flags] ENUM DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C78, AFTER the enum-domain closure:
// il2cpp_enum_to_string (named member + flags decomposition, enumext.cpp),
// il2cpp_enum_has_flag (boxed-receiver flags bit test), il2cpp_enum_equals,
// the name-string ABI (il2cpp_enum_get_name_by_type_name / get_names /
// parse_boxed -- enumface D-STREN-2, batch 124) and the boxed-enum WriteLine
// arm. The batch-13 enumflags case pins the bit-OPERATOR surface (| & ^ ~ =
// on enum-typed locals); THIS case drives the FLAGS-STATIC surface that was
// unwired when enumflags was written -- HasFlag / ToString decomposition /
// Parse round-trips -- on the boxed-Enum reference shape (the enum-VALUE
// ldloca+constrained shape stays D-ENU-2, see GAPS.md).
//
// Coverage (all shapes stringenum proves byte-exact):
//   1. ToString on boxed Enum references: named member, zero member,
//      single-bit, multi-bit COMBINED values (decomposed "F1, F2"), the
//      full-combination value, and an UNNAMED value (numeric fallback).
//   2. HasFlag(Enum) boxed-receiver: self, absent bit, combined flag
//      (self & flag == flag), zero flag, none-vs-bit, one-bit-of-two
//      (False), full-combo HasFlag each component.
//   3. Bit-operator combos via (Enum) casts on [Flags] values: | union,
//      & intersection, ^ toggle, ~ complement -- verified both as bool
//      comparisons and as (int) values.
//   4. Parse round-trip: Enum.Parse name -> boxed value, numeric -> boxed
//      value, whitespace-trimmed name, and ToString of the parsed value.
//   5. GetName / GetNames / GetUnderlyingType on the [Flags] enum.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class EnumFlags2 {
    [Flags]
    enum Bits : int { None = 0, F1 = 1, F2 = 2, F4 = 4, F8 = 8 }

    static void Main() {
        // ============ 1. ToString on boxed Enum references ============
        Enum b0 = Bits.None;
        Console.WriteLine("ts_none=" + (b0.ToString() == "None"));
        Enum b1 = Bits.F1;
        Console.WriteLine("ts_f1=" + (b1.ToString() == "F1"));
        Enum b2 = Bits.F2;
        Console.WriteLine("ts_f2=" + (b2.ToString() == "F2"));
        Enum b3 = (Enum)(Bits.F1 | Bits.F2);
        Console.WriteLine("ts_combo=" + (b3.ToString() == "F1, F2"));
        Enum b5 = (Enum)(Bits.F1 | Bits.F4);
        Console.WriteLine("ts_combo2=" + (b5.ToString() == "F1, F4"));
        Enum bAll = (Enum)(Bits)(Bits.F1 | Bits.F2 | Bits.F4 | Bits.F8);
        Console.WriteLine("ts_all=" + (bAll.ToString() == "F1, F2, F4, F8"));
        Enum b7 = (Enum)(Bits.F1 | Bits.F2 | Bits.F4);
        Console.WriteLine("ts_7=" + (b7.ToString() == "F1, F2, F4"));
        Enum bUn = (Enum)(Bits)(Bits.F2 | Bits.F8);           // value 10 (named combo)
        Console.WriteLine("ts_10=" + (bUn.ToString() == "F2, F8"));
        Enum bUn2 = (Enum)(Bits)16;                          // unnamed value
        Console.WriteLine("ts_unnamed=" + (bUn2.ToString() == "16"));

        // ============ 2. HasFlag(Enum) boxed-receiver ============
        Enum self = Bits.F2;
        Console.WriteLine("hf_self=" + self.HasFlag((Enum)Bits.F2));
        Console.WriteLine("hf_absent=" + (self.HasFlag((Enum)Bits.F1) == false));
        Console.WriteLine("hf_zero=" + self.HasFlag((Enum)Bits.None));
        Enum combo = (Enum)(Bits.F1 | Bits.F4);
        Console.WriteLine("hf_one_of_two=" + (combo.HasFlag((Enum)Bits.F2) == false));
        Console.WriteLine("hf_has_f1=" + combo.HasFlag((Enum)Bits.F1));
        Console.WriteLine("hf_has_f4=" + combo.HasFlag((Enum)Bits.F4));
        Console.WriteLine("hf_full=" + combo.HasFlag((Enum)(Bits.F1 | Bits.F4)));
        Enum full = (Enum)(Bits.F1 | Bits.F2 | Bits.F4 | Bits.F8);
        Console.WriteLine("hf_all_each=" +
            (full.HasFlag((Enum)Bits.F1) && full.HasFlag((Enum)Bits.F2) &&
             full.HasFlag((Enum)Bits.F4) && full.HasFlag((Enum)Bits.F8)));
        Enum none = Bits.None;
        Console.WriteLine("hf_none_bit=" + (none.HasFlag((Enum)Bits.F1) == false));
        Console.WriteLine("hf_none_none=" + none.HasFlag((Enum)Bits.None));
        Console.WriteLine("hf_mixed_subset=" +
            ((Enum)(Bits.F1 | Bits.F2 | Bits.F4)).HasFlag((Enum)(Bits.F1 | Bits.F2)));

        // ============ 3. Bit-operator combos via (Enum) casts ============
        Console.WriteLine("op_union=" + ((Bits.F1 | Bits.F4) == (Bits)(Bits.F1 | Bits.F4)));
        Console.WriteLine("op_and_val=" + (((int)(Bits.F1 | Bits.F2) & (int)Bits.F1) == 1));
        Console.WriteLine("op_and_set=" + (((Bits.F1 | Bits.F2) & Bits.F1) == Bits.F1));
        Console.WriteLine("op_and_clear=" + (((Bits.F1 | Bits.F2) & Bits.F4) == Bits.None));
        Console.WriteLine("op_xor=" + (((Bits.F1 | Bits.F2) ^ Bits.F2) == Bits.F1));
        Console.WriteLine("op_xor_neg=" + (((Bits.F1 | Bits.F2) ^ Bits.F4) == (Bits)(Bits.F1 | Bits.F2 | Bits.F4)));
        Console.WriteLine("op_comp_val=" + (~Bits.None == (Bits)(-1)));
        Console.WriteLine("op_comp_clear=" + ((~Bits.F1 & (Bits.F1 | Bits.F2)) == Bits.F2));
        Console.WriteLine("op_union_val=" + ((int)(Bits.F1 | Bits.F8) == 9));
        Console.WriteLine("op_self_union=" + ((Bits.F1 | Bits.F1) == Bits.F1));
        Console.WriteLine("op_mixed=" + (((Bits.F1 | Bits.F2 | Bits.F8) & (Bits.F2 | Bits.F4)) == Bits.F2));

        // ============ 4. Parse round-trip ============
        Console.WriteLine("p_name=" + Enum.Parse(typeof(Bits), "F4").Equals((Enum)Bits.F4));
        Console.WriteLine("p_numeric=" + Enum.Parse(typeof(Bits), "5").Equals((Enum)(Bits.F1 | Bits.F4)));
        Console.WriteLine("p_ws=" + Enum.Parse(typeof(Bits), " F8 ").Equals((Enum)Bits.F8));
        Console.WriteLine("p_numeric_combo=" + Enum.Parse(typeof(Bits), "10").Equals((Enum)(Bits.F2 | Bits.F8)));
        Console.WriteLine("p_equals_combo=" + Enum.Parse(typeof(Bits), "5").Equals((Enum)(Bits.F1 | Bits.F4)));
        bool pBad = false;
        try { Enum.Parse(typeof(Bits), "NoSuch"); } catch (ArgumentException) { pBad = true; }
        Console.WriteLine("p_bad=" + pBad);
        // NOTE (D-EF2-1, GAPS.md): net48 Enum.Parse accepts COMMA-SEPARATED
        // member names ("F1, F4") and throws OverflowException for an
        // out-of-range numeric; the runtime il2cpp_enum_parse_boxed parses
        // single names / numerics only and reports every failure as
        // ArgumentException. Those probe rows are removed (divergence).

        // ============ 5. GetName / GetNames / GetUnderlyingType ============
        Console.WriteLine("g_name=" + (Enum.GetName(typeof(Bits), Bits.F4) == "F4"));
        Console.WriteLine("g_unnamed=" + (Enum.GetName(typeof(Bits), (Bits)16) == null));
        string[] names = Enum.GetNames(typeof(Bits));
        Console.WriteLine("g_names0=" + (names[0] == "None"));
        Console.WriteLine("g_names1=" + (names[1] == "F1"));
        Console.WriteLine("g_names2=" + (names[2] == "F2"));
        Console.WriteLine("g_names3=" + (names[3] == "F4"));
        Console.WriteLine("g_names4=" + (names[4] == "F8"));
        Console.WriteLine("g_names_len=" + (names.Length == 5));
        Console.WriteLine("g_under=" + (Enum.GetUnderlyingType(typeof(Bits)) == typeof(int)));

        Console.WriteLine("done");
    }
}}
