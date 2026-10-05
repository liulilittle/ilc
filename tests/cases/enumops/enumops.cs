//------------------------------------------------------------------------------
// enumops.cs -- end-to-end reference case for the ENUM BITWISE surface
// (net48 oracle): the full | & ^ ~ combination set, compound |= / &= forms,
// member/intersection tests, value-identity round-trips and comparisons --
// over int-backed [Flags] and plain enums.
//
// Coverage (active, all wired -- mirrors enumflags + extends with ^/~
// chains, named-zero members, mixed unnamed combos):
//   * combination construction (|) and the All alias
//   * |= / &= compound accumulation and bit-clear via ~, 0 reset
//   * member / intersection tests via & against None / 0-literal
//   * ^ xor and ~ complement (incl. double-complement round-trip)
//   * value comparisons == / != and relational < / > on the underlying int
//   * (int) casts and (Enum)(int) round-trips (incl. unnamed values)
//   * a second [Flags] enum with an explicit Zero member
//   * plain (non-Flags) enum: comparisons, relational ops, arithmetic after
//     (int) casts
//
// Every line prints True/False. net48 is the reference baseline.
//
// ACCEPTANCE SURFACE (NOT wired -- do not uncomment until fixed):
//   * Access.HasFlag(Access.A)   -- MethodMap.Enum.cs:227 LOUD ("no
//     flags-semantics primitive on the enumext.h surface").
//   * Enum.Parse / TryParse       -- MethodMap.Enum.cs:199 LOUD (the runtime
//     icall is TryParse-shaped with an int64_t* out result; no ref/out in
//     the M-stage transpiler).
//   * Access.All.ToString()       -- enum-VALUE ToString compiles to ldloca
//     + constrained. + callvirt System.Object::ToString -> TryMapObject has
//     no enum receiver entry (enumdeep gap). Printing a boxed enum value
//     via Console.WriteLine(object) takes the Int32 no-metadata path (a
//     numeric decimal), which DIFFS from the net48 member name.
//   * byte/long UNDERLYING types -- enum value__ field of type byte (0x05)
//     / int64 (0x0A) is rejected by MetadataLoader.DecodeFieldSignature
//     @MetadataLoader.cs:216 ("field type byte 0x05/0x0A not supported in
//     M2"); only the int-backed shape transpiles today.
//   * switch over an enum local    -- IL 0x45 switch rejected (M1, enumdeep
//     gap).
//------------------------------------------------------------------------------
using System;

namespace Cases {

[Flags]
enum Access : int { None = 0, A = 1, B = 2, C = 4, All = A | B | C }

[Flags]
enum Bits : int { Zero = 0, X = 1, Y = 2, Z = 4 }

enum Fruit : int { Apple = 1, Banana = 2, Cherry = 4 }

enum Level : int { Low = 0, Mid = 5, High = 10 }

public static class EnumOps {
    public static void Main() {
        // --- combination construction ---
        Access ab = Access.A | Access.B;
        Console.WriteLine("comb_ab=" + ((int)ab == 3));
        Access abc = Access.A | Access.B | Access.C;
        Console.WriteLine("comb_abc=" + ((int)abc == 7));
        Console.WriteLine("comb_all=" + (abc == Access.All));
        Console.WriteLine("comb_ne=" + (ab != Access.All));
        // --- |= accumulate / &= clear+reset ---
        Access p = Access.None;
        p |= Access.A;
        p |= Access.B;
        Console.WriteLine("acc_two=" + ((int)p == 3));
        p |= Access.C;
        Console.WriteLine("acc_all=" + (p == Access.All));
        p &= ~Access.A;
        Console.WriteLine("clear_a=" + ((int)p == 6));
        p &= Access.None;
        Console.WriteLine("reset=" + ((int)p == 0));
        // --- member / intersection tests via & ---
        Access q = Access.B | Access.C;
        Console.WriteLine("member_b=" + ((q & Access.B) != Access.None));
        Console.WriteLine("member_a_clear=" + ((q & Access.A) == Access.None));
        Console.WriteLine("member_0lit=" + ((q & Access.A) == 0));
        Console.WriteLine("union_all=" + ((q | Access.A) == Access.All));
        // --- ^ xor and ~ complement ---
        Console.WriteLine("xor_ac=" + ((Access.A ^ Access.C) == (Access)5));
        Console.WriteLine("xor_same=" + ((Access.A ^ Access.A) == Access.None));
        Console.WriteLine("comp_zero=" + ((int)~Access.None == -1));
        Console.WriteLine("comp_idem=" + ((Access)(~(int)~Access.None) == Access.None));   // double complement round-trip
        Console.WriteLine("comp_all=" + ((int)~Access.All == -8));
        // --- value comparisons / identity ---
        Console.WriteLine("cmp_7=" + (Access.All == (Access)7));
        Console.WriteLine("cmp_unnamed=" + ((Access)3 == (Access.A | Access.B)));
        Console.WriteLine("cmp_neq=" + (Access.All != Access.None));
        Console.WriteLine("cmp_zero=" + ((int)Access.None == 0));
        // --- (int) / (Enum)(int) round-trips ---
        int raw = (int)(Access.A | Access.C);
        Console.WriteLine("cast_raw=" + (raw == 5));
        Access back = (Access)raw;
        Console.WriteLine("cast_back=" + (back == (Access.A | Access.C)));
        Console.WriteLine("cast_9=" + ((int)(Access)9 == 9));
        Console.WriteLine("cast_ident=" + ((Access)9 == (Access)9));
        // --- second [Flags] enum with explicit Zero ---
        Bits z = Bits.Zero;
        Console.WriteLine("bits_zero=" + ((int)z == 0));
        Bits xy = Bits.X | Bits.Y;
        Console.WriteLine("bits_xy=" + ((int)xy == 3));
        Console.WriteLine("bits_clear=" + ((xy & Bits.Z) == Bits.Zero));
        Console.WriteLine("bits_eq=" + (z == Bits.Zero));
        Bits xyz = Bits.X | Bits.Y | Bits.Z;
        Console.WriteLine("bits_all=" + (xyz == (Bits)7));
        Console.WriteLine("bits_combo=" + ((Bits)((int)xy ^ (int)Bits.Z) == xyz));
        // --- plain (non-Flags) enum: relational / comparisons ---
        Fruit f = Fruit.Banana;
        Console.WriteLine("fruit_eq=" + (f == Fruit.Banana));
        Console.WriteLine("fruit_lt=" + (f < Fruit.Cherry));
        Console.WriteLine("fruit_gt=" + (Fruit.Cherry > Fruit.Apple));
        Console.WriteLine("fruit_cast=" + ((int)f == 2));
        Console.WriteLine("fruit_or=" + ((int)(Fruit.Apple | Fruit.Cherry) == 5));
        Console.WriteLine("fruit_unnamed=" + ((Fruit)5 == (Fruit)5));
        // --- relational on Level (non-contiguous values) ---
        Console.WriteLine("lvl_mid=" + (Level.Mid > Level.Low));
        Console.WriteLine("lvl_le=" + (Level.High <= Level.High));
        Console.WriteLine("lvl_cast=" + ((int)Level.High == 10));
        Console.WriteLine("lvl_arith=" + (((int)Level.Mid * 2) == (int)Level.High));
    }
}}
