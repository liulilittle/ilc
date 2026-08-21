//------------------------------------------------------------------------------
// enumflags.cs -- end-to-end reference case for the [Flags] ENUM domain:
// Flags combination values, |= / &= compound bit operators, member/intersection
// tests via &, value-combination comparisons, (int) conversions, and plain
// non-Flags enum operations. Runs on .NET Framework 4.8 (the reference
// runtime); every line prints True/False (a bool assertion).
//
// Coverage:
//   * [Flags] enum (None/A/B/C) -- combination construction (|) and All value
//   * |= / &= compound assignments on enum-typed locals (incl. ~ complement
//     bit-clear, 0 reset, repeated |= idempotence)
//   * member tests via & (bit set / bit clear / 0-literal comparison)
//   * ^ xor and ~ complement on Flags values
//   * value combination comparisons (== / !=, incl. unnamed combos)
//   * (int) casts and (Access)(int) round-trips
//   * second [Flags] enum with an explicit Zero member
//   * plain (non-Flags) enum: comparisons, relational ops, casts, int | after
//     casts
//   * NO switch statements anywhere -- the IL 0x45 switch opcode is rejected
//     by il2cppc M1 (IlDecoder.cs:265); this case must stay switch-free so it
//     exercises the enum domain without that unrelated gap.
//
// KNOWN GAPS (not exercised at runtime; acceptance surface for the enum
// milestone, see e2e-sprint-worklist.md and enumdeep.cs):
//   * Access.HasFlag(Access.A) -- NOT wired: transpiler throws
//     NotSupportedException ("Enum.HasFlag not wired", MethodMap.Enum.cs:206)
//     because the runtime has no flags-semantics primitive on the enumext.h
//     surface.
//   * Access.A.ToString() -- NOT wired for the enum-VALUE shape: Roslyn
//     compiles it to ldloca + constrained. + callvirt System.Object::ToString,
//     which routes to TryMapObject (MethodMap.Object.cs) -- the receiver-typed
//     ToString list has no enum entry (enumdeep gap, verified 2026-08-05).
//     (A System.Enum-typed receiver DOES map to il2cpp_enum_to_string,
//     MethodMap.Enum.cs:180-192; the direct value shape does not.)
//------------------------------------------------------------------------------
using System;

namespace Cases {

[Flags]
enum Access : int { None = 0, A = 1, B = 2, C = 4, All = A | B | C }

[Flags]
enum Bits : int { Zero = 0, X = 1, Y = 2, Z = 4 }

enum Fruit : int { Apple = 1, Banana = 2, Cherry = 4 }

public static class EnumFlags {
    public static void Main() {
        // --- [Flags] combination construction ---
        Access ab = Access.A | Access.B;
        Console.WriteLine((int)ab == 3);                    // True
        Access abc = Access.A | Access.B | Access.C;
        Console.WriteLine((int)abc == 7);                   // True
        Console.WriteLine(abc == Access.All);               // True
        Console.WriteLine(ab != Access.All);                // True

        // --- |= compound assignment (accumulate) ---
        Access p = Access.None;
        p |= Access.A;
        Console.WriteLine((int)p == 1);                     // True
        p |= Access.B;
        Console.WriteLine((int)p == 3);                     // True
        p |= Access.C;
        Console.WriteLine(p == Access.All);                 // True

        // --- &= compound assignment (bit-clear via ~, then 0 reset) ---
        p &= ~Access.A;
        Console.WriteLine((int)p == 6);                     // True (B|C)
        p &= Access.None;
        Console.WriteLine((int)p == 0);                     // True (reset)

        // --- member / intersection tests via & ---
        Access q = Access.B | Access.C;
        Console.WriteLine((q & Access.B) != Access.None);   // True (bit set)
        Console.WriteLine((q & Access.A) == Access.None);   // True (bit clear)
        Console.WriteLine((q & Access.A) == 0);             // True (0-literal enum compare)
        Console.WriteLine((q | Access.A) == Access.All);    // True (union -> All)

        // --- ^ xor and ~ complement ---
        Console.WriteLine((Access.A ^ Access.C) == (Access)5);  // True
        Console.WriteLine((int)~Access.None == -1);             // True (~0)

        // --- value combination comparisons ---
        Console.WriteLine(Access.All == (Access)7);         // True
        Console.WriteLine((Access)3 == (Access.A | Access.B)); // True (unnamed combo)
        Console.WriteLine(Access.All != Access.None);       // True
        Console.WriteLine((int)Access.None == 0);           // True

        // --- (int) conversion round-trips ---
        int raw = (int)(Access.A | Access.C);
        Console.WriteLine(raw == 5);                        // True
        Access back = (Access)raw;
        Console.WriteLine(back == (Access.A | Access.C));   // True
        Console.WriteLine((int)(Access)7 == 7);             // True
        Console.WriteLine((Access)9 == (Access)9);          // True (unnamed value identity)

        // --- repeated |= on the same bit (idempotence) ---
        Access acc = Access.None;
        acc |= Access.A;
        acc |= Access.A;
        Console.WriteLine((int)acc == 1);                   // True

        // --- second [Flags] enum with explicit Zero member ---
        Bits z = Bits.Zero;
        Console.WriteLine((int)z == 0);                     // True
        Bits xy = Bits.X | Bits.Y;
        Console.WriteLine((int)xy == 3);                    // True
        Console.WriteLine((xy & Bits.Z) == Bits.Zero);      // True (bit clear vs Zero)
        Console.WriteLine(z == Bits.Zero);                  // True

        // --- plain (non-Flags) enum: normal operations ---
        Fruit f = Fruit.Banana;
        Console.WriteLine(f == Fruit.Banana);               // True
        Console.WriteLine(f != Fruit.Apple);                // True
        Console.WriteLine(f < Fruit.Cherry);                // True (2 < 4)
        Console.WriteLine((int)f == 2);                     // True
        Console.WriteLine((int)(Fruit.Apple | Fruit.Cherry) == 5); // True (int semantics)
        Console.WriteLine((Fruit)5 == (Fruit)5);            // True (unnamed identity)
        Console.WriteLine(Fruit.Cherry > Fruit.Apple);      // True (4 > 1)

        // KNOWN GAPS (acceptance surface -- do not uncomment until wired):
        //   * Access.HasFlag(Access.A)        -- MethodMap.Enum.cs:206 throws
        //   * Access.All.ToString() == "All"  -- Object::ToString enum shape unwired
    }
}}
