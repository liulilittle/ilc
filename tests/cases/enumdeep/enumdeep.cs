//------------------------------------------------------------------------------
// enumdeep.cs -- end-to-end reference case for the ENUM domain (enumext):
// custom int-backed enums under deep usage. Runs on .NET Framework 4.8
// (the reference runtime); every line prints True/False (a bool assertion).
//
// Coverage:
//   * value comparisons (==/!=/</>) -- pure underlying-int semantics
//   * (int) casts and (Enum)(int) round-trips (incl. zero + unnamed values)
//   * int arithmetic / bitwise ops after casts, plus enum-typed | & ~
//   * switch over an enum local (full match / default arm / no-default
//     fall-through)
//   * [Flags] combined values (A|B|C, intersection tests, 0-literal
//     comparison, unnamed combo round-trip)
//   * enum ToString / Enum.GetUnderlyingType -- wired in the transpiler map
//     (MethodMap.Enum.cs) but end-to-end UNREACHABLE today (see KNOWN GAPS)
//
// TRANSPILE STATUS (verified 2026-08-05, il2cppc rebuilt): the three enum
// switch blocks below CANNOT transpile today -- il2cppc M1 rejects switch
// (IL 0x45) with "not supported in M1" (IlDecoder.cs:265). This case is the
// switch-milestone acceptance surface: the non-switch lines (comparisons,
// casts, arithmetic, [Flags] ops) are expected to pass once switch lands;
// the two gaps below are the enum-milestone acceptance surface (unchanged).
//
// KNOWN GAPS (see e2e-sprint-worklist.md §9.5):
//   * enum-value ToString() compiles to ldloca + constrained. + callvirt
//     instance string System.Object::ToString() -- the MemberRef owner is
//     System.Object, NOT System.Enum -- so the call routes to TryMapObject
//     (MethodMap.Object.cs), whose receiver-typed ToString list has no enum
//     entry (and cannot distinguish enum receivers without knownTypes).
//   * Enum.GetUnderlyingType(typeof(Color)) needs ldtoken/typeof emission
//     for enum types (REGISTER WIRING REPORT e.1 -- value types have no
//     class descriptor, so the typeof milestone must emit a name-bearing
//     descriptor or the icalls must take the name string).
//   The runtime primitives EXIST (il2cpp_enum_to_string /
//   il2cpp_enum_get_underlying_type, enumext.h:137-148) and the transpiler
//   mapping IS wired (MethodMap.Enum.cs:124-155); the gap is descriptor /
//   ldtoken emission + the enum box helper (CodeGenerator.cs:2089). The
//   gap lines below stay in the file as the enum-milestone acceptance
//   surface; the rest of the case exercises what already works.
//------------------------------------------------------------------------------
using System;

namespace Cases {

enum Color : int { Red = 1, Green = 2, Blue = 4 }
enum Level : int { Low = 0, Mid = 5, High = 10 }

[Flags]
enum Perms : int { None = 0, Read = 1, Write = 2, Execute = 4, All = Read | Write | Execute }

public static class EnumDeep {
    public static void Main() {
        Color c = Color.Green;

        // --- value comparisons (underlying-int semantics) ---
        Console.WriteLine(c == Color.Green);                    // True
        Console.WriteLine(c != Color.Red);                      // True
        Console.WriteLine(c == Color.Red);                      // False
        Console.WriteLine((int)c == 2);                         // True
        Console.WriteLine(c < Color.Blue);                      // True (2 < 4)
        Console.WriteLine(Level.Mid > Level.Low);               // True (5 > 0)
        Console.WriteLine(Level.High <= Level.High);            // True

        // --- (int) casts and (Enum)(int) round-trips ---
        Console.WriteLine((int)Color.Red == 1);                 // True
        Console.WriteLine((int)Color.Blue == 4);                // True
        Console.WriteLine((int)Level.Mid == 5);                 // True
        int raw = (int)c;
        Console.WriteLine(raw == 2);                            // True
        Color back = (Color)raw;
        Console.WriteLine(back == Color.Green);                 // True
        Console.WriteLine((Color)0 == (Color)0);                // True (zero value identity)
        Console.WriteLine((Color)9 == (Color)9);                // True (unnamed value identity)
        Console.WriteLine((int)(Color)0 == 0);                  // True

        // --- int arithmetic / bitwise after casts, enum-typed ops ---
        Console.WriteLine(((int)Color.Red | (int)Color.Blue) == 5);   // True
        Console.WriteLine(((int)Color.Red ^ (int)Color.Green) == 3);  // True
        Console.WriteLine(((int)Color.Blue >> 1) == 2);                // True
        Console.WriteLine(((int)Color.Green * (int)Color.Blue) == 8);  // True
        Console.WriteLine(((int)Color.Blue - (int)Color.Red) == 3);    // True
        Console.WriteLine((Color.Red | Color.Blue) == (Color)5);       // True (enum-typed |)
        Console.WriteLine(((Color)7 & Color.Blue) == Color.Blue);      // True
        Console.WriteLine((int)~Color.Red == -2);                      // True (~1)

        // --- switch over an enum local ---
        bool sw = false;
        switch (c) {
            case Color.Red:   sw = false; break;
            case Color.Green: sw = true;  break;
            case Color.Blue:  sw = false; break;
        }
        Console.WriteLine(sw);                                  // True (Green arm)
        sw = false;
        switch (c) {
            case Color.Red:  sw = false; break;
            case Color.Blue: sw = false; break;
            default:         sw = true;  break;
        }
        Console.WriteLine(sw);                                  // True (default arm)
        sw = true;
        switch (Level.High) {
            case Level.Low: sw = false; break;
            case Level.Mid: sw = false; break;
        }
        Console.WriteLine(sw);                                  // True (no arm, no default)

        // --- [Flags] combined values ---
        Perms p = Perms.Read | Perms.Write;
        Console.WriteLine((int)p == 3);                         // True
        Console.WriteLine(p == (Perms.Read | Perms.Write));     // True
        Console.WriteLine((p & Perms.Read) != Perms.None);      // True
        Console.WriteLine((p & Perms.Execute) == Perms.None);   // True
        Console.WriteLine((p | Perms.Execute) == Perms.All);    // True
        Console.WriteLine((int)(p | Perms.Execute) == 7);       // True
        Console.WriteLine((p & Perms.Read) != 0);               // True (0-literal enum compare)
        Console.WriteLine((int)Perms.None == 0);                // True
        Console.WriteLine((Perms)6 == (Perms.Write | Perms.Execute)); // True (unnamed combo)
        Console.WriteLine((int)~Perms.None == -1);              // True (~0)
        Console.WriteLine(((int)Perms.All & 4) != 0);           // True (Execute bit set)
        Console.WriteLine(((int)Perms.All & 8) == 0);           // True (bit 8 clear)

        // --- enum ToString (KNOWN GAP: constrained/callvirt System.Object shape) ---
        Console.WriteLine(Color.Red.ToString() == "Red");       // True (net48)
        Console.WriteLine(c.ToString() == "Green");             // True (net48)
        Console.WriteLine(Level.Mid.ToString() == "Mid");       // True (net48)

        // --- Enum.GetUnderlyingType (KNOWN GAP: ldtoken/typeof of enum types) ---
        Console.WriteLine(Enum.GetUnderlyingType(typeof(Color)) == typeof(int));   // True (net48)
        Console.WriteLine(Enum.GetUnderlyingType(typeof(Perms)) == typeof(int));   // True (net48)
    }
}}
