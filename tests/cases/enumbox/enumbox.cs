//------------------------------------------------------------------------------
// enumbox.cs -- ENUM BOXING DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C12 (2026-08-09).
//
// Exercises the BOXED-enum surface after the M6 batch-35 "enum box klass"
// work. That batch landed the RUNTIME primitives (icall_box_enum,
// il2cpp_enum_boxed_to_string, il2cpp_enum_boxed_get_hash_code, the
// icall_boxed_equals_ext2 enum branch -- enumext.h/boxingext2.cpp, unit-
// tested in tests/runtime/test_enumext.cpp), but the TRANSPILER's box arm
// (CodeGenerator.cs box_ switch) STILL emits icall_box_long for enum values,
// so end-to-end a boxed enum keeps the System.Int64 carrier klass -- the
// klass-name-keyed enum registry (il2cpp_enum_register/FindInfo) never
// resolves it, and the user-enum register emission has no call site yet.
//
// Probes (all ASCII, all on the Enum-typed reference / boxed shape):
//   ts_*        ToString of a boxed enum: net48 prints the member NAME
//               ("A", "B", "F3", "F1, F2"); ours prints the numeric decimal
//               (il2cpp_enum_to_string null-descriptor path) -> D-ENM-3.
//   ts_neq      the same probe inverted (ours prints "1", net48 "A").
//   eq_same     same value + same enum type            -> True both.
//   eq_diffval  different value, same type             -> False both.
//   eq_cross    same value + DIFFERENT enum type       -> net48 False; ours
//               TRUE (both boxes carry the System.Int64 klass, so
//               il2cpp_enum_equals' klass-NAME type gate passes and the
//               payloads compare equal) -> D-ENM-5.
//   eq_cross_sameval  boxed enum vs boxed Int32        -> False both (Int64
//               vs Int32 klass names differ -> the gate rejects).
//   eq_null     Equals(null)                           -> False both.
//   eq_flags_*  Flags value equality on same/diff vals -> True/False both.
//   eq_obj_same EQUALITY VIA object.Equals on two boxes of the same value:
//               True both (verified 2026-08-09 -- the two Int64 carrier
//               boxes compare VALUE-equal through the boxed-equals dispatch;
//               net48 boxed Enum.Equals is value equality too).
//   eq_obj_cross  object.Equals across DIFFERENT enum types, same value 1:
//               net48 False (type gate); ours TRUE (both boxes carry the
//               System.Int64 klass -> value 1 == 1) -> D-ENM-5, the same
//               cross-type failure surfacing through the object-typed path.
//
// GetHashCode probes are COMMENTED (net48 oracle documented below): the
// Enum-typed receiver GetHashCode has no TryMapEnum arm (generated-name
// fallback -> undefined C++ symbol -> cmake fail), and the object-typed
// boxed receiver routes icall_object_get_hash_code to the ADDRESS hash for
// an Int64 box (nondeterministic -- impossible to pin byte-exact). The
// runtime primitive il2cpp_enum_boxed_get_hash_code is unreachable end-to-
// end until the box arm emits icall_box_enum.
//------------------------------------------------------------------------------
using System;

namespace Cases {
enum EInt : int { A = 1, B = 2 }
[Flags]
enum EFlags : int { None = 0, F1 = 1, F2 = 2, F3 = 4 }

public static class EnumBox {
    public static void Main() {
        Enum e1 = EInt.A;
        Enum e2 = EInt.A;
        Enum eb = EInt.B;
        Enum f1 = (Enum)EFlags.F1;
        Enum f12 = (Enum)(EFlags.F1 | EFlags.F2);
        Enum f3 = (Enum)EFlags.F3;

        // ============ A. ToString (boxed Enum; D-ENM-3) ============
        Console.WriteLine("ts_a=" + (e1.ToString() == "A"));
        Console.WriteLine("ts_b=" + (eb.ToString() == "B"));
        Console.WriteLine("ts_f_single=" + (f3.ToString() == "F3"));
        Console.WriteLine("ts_f_comb=" + (f12.ToString() == "F1, F2"));
        Console.WriteLine("ts_neq=" + (e1.ToString() == "1"));

        // ============ B. Equals (il2cpp_enum_equals, boxed refs) ============
        Console.WriteLine("eq_same=" + e1.Equals(e2));
        Console.WriteLine("eq_diffval=" + e1.Equals(eb));
        Console.WriteLine("eq_cross=" + e1.Equals(f1));
        Console.WriteLine("eq_cross_sameval=" + e1.Equals((object)1));
        Console.WriteLine("eq_null=" + (e1.Equals(null) ? 1 : 0));
        Console.WriteLine("eq_flags_same=" + f1.Equals((Enum)EFlags.F1));
        Console.WriteLine("eq_flags_diff=" + f1.Equals(f12));

        // ============ C. Equals via object (icall_boxed_equals_ext2) ============
        object o1 = EInt.A;
        object o2 = EInt.A;
        Console.WriteLine("eq_obj_same=" + o1.Equals(o2));
        Console.WriteLine("eq_obj_cross=" + o1.Equals((object)EFlags.F1));

        // ============ D. GetHashCode (commented -- net48 oracle documented) ============
        //   net48 oracle:
        //     Enum eh = EInt.A; eh.GetHashCode()   -> 1
        //     object ob = EInt.A; ob.GetHashCode() -> 1 (the underlying value)
        //   ours (both commented -- not byte-pinnable):
        //     Enum-typed receiver: TryMapEnum has no GetHashCode arm ->
        //       generated-name fallback -> undefined C++ symbol -> cmake fail
        //       (D-ENM-7).
        //     object-typed receiver: the Int64 carrier box (icall_box_long)
        //       routes icall_object_get_hash_code to the ADDRESS hash
        //       (ptr >> 3 -- valuesemantics.cpp) -> nondeterministic, not 1
        //       (D-ENM-6). il2cpp_enum_boxed_get_hash_code is unreachable
        //       until the box arm emits icall_box_enum.
    }
}}
