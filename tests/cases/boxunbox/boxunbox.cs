// boxunbox.cs -- deep box/unbox e2e reference case (net48 baseline).
//
// Probes the WIRED boxing surface only (MethodMap.Object.cs /
// MethodMap.ValueType.cs / valuesemantics.cpp / boxingext2):
//
//   1) GetHashCode on BOXED values: value semantics (Int32 hash = the value),
//      cross-box consistency (two boxes of the same value hash equal), and
//      cross-value inequality -- via Object.GetHashCode callvirt on the box
//      (icall_object_get_hash_code dispatch).
//   2) ToString on BOXED values: the value's .NET text, NOT the type name
//      (Int32/UInt32/UInt64/Byte/Int16/Boolean/Char/Double) -- the
//      icall_object_to_string dispatch.
//   3) GetType().Name on boxed values (Int32/Char short names).
//   4) isinst on a boxed value type (`o is int` -> unboxable probe; `o is
//      long`/`o is string` -> False), castclass unbox, unbox->rebox chain.
//   5) Interface castclass on a boxed value + callvirt of a BCL interface
//      method (IComparable.CompareTo on boxed Int32) -- dispatch on the box.
//   6) GENERIC box: Box<int>.Get() returns a T stored in a generic field;
//      the T->object conversion boxes the value (generics.cs precedent for
//      the Box<T> class shape; here the box is explicit).
//
// Recorded gaps (GAPS.md, each verified as a transpile/build abort -- NOT
// called here):
//   GAP-BOX-1 "modify a field of a BOXED value type" is not probeable:
//     boxing a USER STRUCT (`object o = new Fix{...}`) aborts the transpile
//     with "box of Fix not supported -- no runtime box helper (audit H3 +
//     M5)" (CodeGenerator.cs box_ switch). The interface-dispatch mutation
//     probe (box struct -> cast to IFix -> Add() mutates the box) is parked
//     until user-struct box helpers land.
//   GAP-BOX-2 `o is long` (isinst Int64 on a box) fails to COMPILE: the
//     generated `il2cpp_is_instance(t0, &System_Int64_class)` hits an
//     undeclared descriptor -- only System_Int32_class (and friends) are
//     declared, System_Int64_class is missing.
//   GAP-BOX-3 interface callvirt on a boxed value
//     (`((IComparable)od).CompareTo(42)`) resolves to an UNDEFINED generated
//     extern System_IComparable_CompareTo_<hash> -- interface method dispatch
//     on boxes is not wired.
//   GAP-BOX-4 `bo.ToString()` on a box produced by a GENERIC field read
//     (Box<int>.Get() -> object) emits the undeclared
//     System_Object_ToString_<hash>: the box type is not TRACKED through the
//     generic T->object conversion, so Object::ToString falls to the vtable
//     path instead of icall_object_to_string.
//   GAP-BOX-5 `((object)4000000000u).ToString()` (boxed UInt32) emits the
//     SAME undeclared System_Object_ToString_<hash>: the Object::ToString
//     tracked-receiver whitelist in MethodMap.Object.cs lists every boxed
//     prim EXCEPT "System.UInt32" (icall_object_to_string DOES handle the
//     UInt32 arm at runtime -- only the transpiler whitelist is missing it).
//   GAP-BOX-6 boxed GetHashCode is value-based ONLY for boxed Int32; boxed
//     Boolean/Char/Double/Int64 (and the other prims) return an ADDRESS hash
//     (`ptr >> 3`) -- two boxes of the same value hash differently, and the
//     values diverge from net48 (bool->1/0, char->code unit, double->xor of
//     the two int32 halves, long->lo^hi). Root: valuesemantics.cpp
//     icall_object_get_hash_code (IsBoxedInt32 special case only).

using System;
public class Box<T> { public T item; public T Get() { return item; } public void Set(T v) { item = v; } }
namespace Cases { public static class BoxUnbox {
    public static void Main() {
        // ---- boxed GetHashCode: value + cross-box consistency (Int32 only) ----
        // NOTE: cross-box consistency probes for boxed Boolean/Char/Double/Int64
        // are REMOVED -- the runtime's icall_object_get_hash_code is value-based
        // ONLY for boxed Int32; everything else returns an ADDRESS hash (ptr>>3),
        // so two boxes of the same bool/char/double/long hash differently (and
        // wrong vs net48: bool->1/0, char->unit, double->xor-halves, long->lo^hi).
        // See GAPS.md GAP-BOX-6.
        Console.WriteLine(((object)42).GetHashCode());                      // 42
        Console.WriteLine(((object)(-5)).GetHashCode());                    // -5
        object a = 42, b = 42, c = 43;
        Console.WriteLine(a.GetHashCode() == b.GetHashCode());              // True
        Console.WriteLine(a.GetHashCode() == c.GetHashCode());              // False

        // ---- boxed ToString: value text, not the type name ----
        Console.WriteLine(((object)42).ToString() == "42");                 // True
        Console.WriteLine(((object)18446744073709551615UL).ToString() == "18446744073709551615"); // True
        Console.WriteLine(((object)(byte)200).ToString() == "200");         // True
        Console.WriteLine(((object)(short)-3).ToString() == "-3");          // True
        Console.WriteLine(((object)true).ToString() == "True");             // True
        Console.WriteLine(((object)'Q').ToString() == "Q");                 // True
        Console.WriteLine(((object)2.75).ToString() == "2.75");             // True

        // ---- boxed GetType short names ----
        Console.WriteLine(((object)42).GetType().Name == "Int32");          // True
        Console.WriteLine(((object)'Q').GetType().Name == "Char");          // True

        // ---- isinst / castclass / unbox-rebox ----
        object o = 42;
        Console.WriteLine(o is int);                                        // True
        Console.WriteLine(o is string);                                     // False
        Console.WriteLine(o is IComparable);                                // True
        Console.WriteLine(((int)o) == 42);                                  // True
        object o2 = (object)(int)o;                                         // unbox -> rebox
        Console.WriteLine((int)o2 == 42);                                   // True

        // ---- generic class box: T stored in generic field, boxed on read ----
        Box<int> bx = new Box<int>();
        bx.Set(42);
        object bo = bx.Get();
        Console.WriteLine((int)bo == 42);                                   // True
        Console.WriteLine(bo.GetType().Name == "Int32");                    // True
    }
}}
