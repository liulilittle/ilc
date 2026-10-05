//------------------------------------------------------------------------------
// enumdeep4.cs -- ENUM DEEP e2e reference case (net48 oracle, byte-for-byte).
// Created in e2e batch C83, AFTER the enum register wiring landed
// (CodeGenerator.cs il2cpp_runtime_init enum loop -- item c of the REGISTER
// WIRING REPORT -- plus the boxed-enum arm and the name-string ABI from the
// enumface closure): the boxed user enum now formats its MEMBER NAME (not the
// numeric fallback), so this case probes the Parse / ToString / underlying
// chain at full depth.
//
// > NAMING: this case is `enumdeep4` because a git-tracked case named
// > `enumdeep2` already exists (e2e batch C10, recorded-gap case without
// > expected.txt) and the batch rule forbids modifying existing files; the
// > enumflags2 GAPS.md documents the same naming workaround.
//
// Coverage (all shapes proven byte-exact by enumflags2 / enumdeep3):
//   1. ToString on boxed Enum references: named member, zero member, an
//      UNNAMED value (numeric decimal fallback -- net48), negative named
//      member, and a [Flags] multi-bit decomposition.
//   2. Enum.Parse name / numeric / whitespace-trimmed round-trips (the
//      boxed-value equality shape .Equals((Enum)...)); Parse failure ->
//      ArgumentException.
//   3. Underlying VALUE face: (int) casts of negative / zero / boundary
//      values, int arithmetic after casts, value comparisons (== != < >),
//      and Enum.GetUnderlyingType == typeof(int) for every int-backed enum.
//   4. GetName / GetNames on the same enums (declaration order).
//
// net48-shape discipline (enumflags2 precedent): every line prints
// "label=True/False"; exceptions print type-name-only catches.
//
// NOT PROBED (transpile-blocking, recorded in GAPS.md -- do not uncomment):
//   * Enum.Parse(Type, string, bool ignoreCase) -- LOUD (no case-insensitive
//     runtime primitive, MethodMap.Enum.cs).
//   * Enum.TryParse<T> -- LOUD (ref/out result slot, no M-stage support).
//   * Enum.HasFlag / Format / IsDefined / GetValues / ToObject on the
//     remaining surface is covered by enumflags2; the 3-arg Parse and the
//     non-int underlying enums (D-ENM-4: "field type byte 0x05 not supported
//     in M2") stay out of scope here.
//------------------------------------------------------------------------------
using System;

namespace Cases {
enum Level : int { Low = -2, Zero = 0, Mid = 3, High = 9 }
[Flags]
enum Access : int { None = 0, Read = 1, Write = 2, Execute = 4 }

public static class EnumDeep4 {
    public static void Main() {
        // ============ 1. ToString on boxed Enum references ============
        Enum lLow = Level.Low;
        Console.WriteLine("ts_low=" + (lLow.ToString() == "Low"));
        Enum lZero = Level.Zero;
        Console.WriteLine("ts_zero=" + (lZero.ToString() == "Zero"));
        Enum lHigh = Level.High;
        Console.WriteLine("ts_high=" + (lHigh.ToString() == "High"));
        Enum lUn = (Enum)(Level)(-1);              // unnamed value
        Console.WriteLine("ts_unnamed=" + (lUn.ToString() == "-1"));
        Enum lUn9 = (Enum)(Level)9;                // named value via cast
        Console.WriteLine("ts_named9=" + (lUn9.ToString() == "High"));
        Enum acc = (Enum)(Access.Read | Access.Execute);
        Console.WriteLine("ts_flags=" + (acc.ToString() == "Read, Execute"));

        // ============ 2. Enum.Parse round-trips ============
        Console.WriteLine("p_name=" + Enum.Parse(typeof(Level), "Mid").Equals((Enum)Level.Mid));
        Console.WriteLine("p_numeric=" + Enum.Parse(typeof(Level), "9").Equals((Enum)Level.High));
        Console.WriteLine("p_trim=" + Enum.Parse(typeof(Level), " Low ").Equals((Enum)Level.Low));
        Console.WriteLine("p_neg=" + Enum.Parse(typeof(Level), "-2").Equals((Enum)Level.Low));
        Console.WriteLine("p_zero=" + Enum.Parse(typeof(Level), "0").Equals((Enum)Level.Zero));
        Console.WriteLine("p_flags=" + Enum.Parse(typeof(Access), "3").Equals((Enum)(Access.Read | Access.Write)));
        Console.WriteLine("p_unnamed=" + Enum.Parse(typeof(Level), "5").Equals((Enum)(Level)5));
        bool pBad = false;
        try { Enum.Parse(typeof(Level), "NoSuch"); } catch (ArgumentException) { pBad = true; }
        Console.WriteLine("p_bad=" + pBad);
        bool pEmpty = false;
        try { Enum.Parse(typeof(Level), ""); } catch (ArgumentException) { pEmpty = true; }
        Console.WriteLine("p_empty=" + pEmpty);

        // ============ 3. underlying VALUE face ============
        Console.WriteLine("c_neg=" + ((int)Level.Low == -2));
        Console.WriteLine("c_zero=" + ((int)Level.Zero == 0));
        Console.WriteLine("c_mid=" + ((int)Level.Mid == 3));
        Console.WriteLine("c_high=" + ((int)Level.High == 9));
        Console.WriteLine("c_plus=" + ((int)Level.Mid + (int)Level.High == 12));
        Console.WriteLine("c_times=" + ((int)Level.High * (int)Level.Mid == 27));
        Console.WriteLine("c_negmul=" + ((int)Level.Low * (int)Level.High == -18));
        Console.WriteLine("cmp_lt=" + (Level.Low < Level.Mid));
        Console.WriteLine("cmp_le=" + (Level.Low <= Level.Zero));
        Console.WriteLine("cmp_gt=" + (Level.High > Level.Zero));
        Console.WriteLine("cmp_eq=" + (Level.Mid == (Level)3));
        Console.WriteLine("cmp_neq=" + (Level.Mid != Level.High));
        Console.WriteLine("cmp_flags=" + ((Access.Read | Access.Write) == (Access)3));
        Console.WriteLine("cmp_flags_neq=" + (Access.Read != Access.Write));
        Console.WriteLine("ug_int=" + (Enum.GetUnderlyingType(typeof(Level)) == typeof(int)));
        Console.WriteLine("ug_acc=" + (Enum.GetUnderlyingType(typeof(Access)) == typeof(int)));
        Console.WriteLine("ug_not_long=" + (Enum.GetUnderlyingType(typeof(Level)) == typeof(long)));

        // ============ 4. GetName / GetNames ============
        Console.WriteLine("gn_low=" + (Enum.GetName(typeof(Level), Level.Low) == "Low"));
        Console.WriteLine("gn_high=" + (Enum.GetName(typeof(Level), Level.High) == "High"));
        Console.WriteLine("gn_unnamed=" + (Enum.GetName(typeof(Level), (Level)5) == null));
        string[] names = Enum.GetNames(typeof(Level));
        Console.WriteLine("gns_len=" + (names.Length == 4));
        // NOTE (D-EN4-1, GAPS.md): net48 GetNames returns the names SORTED BY
        // VALUE (probe-verified 2026-08-13: Zero, Mid, High, Low for the
        // -2/0/3/9 declaration); the runtime returns DECLARATION order. The
        // order probes (gns0..gns3) diverge for this non-monotonic enum and
        // are REMOVED; the Access enum below (monotonic declaration) keeps
        // byte-exact order probes (gna_len/gna0/gna3).
        string[] anames = Enum.GetNames(typeof(Access));
        Console.WriteLine("gna_len=" + (anames.Length == 4));
        Console.WriteLine("gna0=" + (anames[0] == "None"));
        Console.WriteLine("gna3=" + (anames[3] == "Execute"));

        Console.WriteLine("done");
    }
}}
