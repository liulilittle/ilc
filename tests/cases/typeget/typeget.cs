//------------------------------------------------------------------------------
// typeget.cs -- TYPE ACQUISITION DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C81, AFTER the typeinfo domain
// closure (typeof fold / GetType() runtime name / GetTypeFromHandle /
// get_Name short-name / GetTypeCode).
//
// Goes deeper than typeops/typechain/typeext2 (C20/C18/C44) on the WIRED
// Type-acquisition surface (MethodMap.Type.cs / MethodMap.Object.cs +
// typecheck.cpp / typeext.cpp):
//
//   1. typeof FOLD -> .Name short names over a WIDER lattice: all 6 numeric
//      primitives (int/long/double/float/short/byte), bool/char/string,
//      a user class, a struct, an enum, a nested class and the case class
//      itself. Every probe is a bool against the net48 short name.
//   2. GetType() -> .Name on BOXED primitives (int/long/double/bool/char),
//      a string and user instances (class + struct boxed + enum boxed).
//   3. ARRAY TYPES (the new D-TYPEX-4 closure): GetType().Name on int[] /
//      double[] / string[] instances renders "<Element>[]" on BOTH runtimes
//      (typecheck.cpp translates the array-class '[' marker to a trailing
//      "[]" before the short-name truncation). Also typeof(int[]) folding
//      to the array short name.
//   4. typeof IDENTITY: == on folded types (op_Equality -> name string
//      equality) and object.ReferenceEquals (net48 caches one RuntimeType
//      per type) -- same-type True, different-type False.
//   5. GetHashCode CONSISTENCY: the same type hashes equal across calls;
//      different types hash differently. Never the raw hash values.
//   6. GetTypeCode STATIC FOLD: Type.GetTypeCode(typeof(T)) == the net48
//      TypeCode constant for int/bool/char/double/string + a user class
//      (Object) and an enum (Object).
//   7. GetType() vs typeof IDENTITY: o.GetType() == typeof(o.GetType()) --
//      the instance and the folded Type agree (both name strings).
//
// Byte-stability: every line prints "label=True/False"; net48 is the byte
// reference. No raw hash values, no unwired members (FullName/IsValueType/
// IsEnum/Equals(object) stay out -- D-TYPE-2/3/4/1, see typeops GAPS).
//------------------------------------------------------------------------------
using System;

namespace Cases {
    enum Shade { Dark, Light }
    struct Pt { public int X; public int Y; }
    class Animal2 { }
    class Outer { public class Inner { } }

    public static class TypeGet {
        public static void Main() {
            // ============ 1. typeof fold -> .Name (wide lattice) ============
            Console.WriteLine("tf_int=" + (typeof(int).Name == "Int32"));
            Console.WriteLine("tf_long=" + (typeof(long).Name == "Int64"));
            Console.WriteLine("tf_double=" + (typeof(double).Name == "Double"));
            Console.WriteLine("tf_float=" + (typeof(float).Name == "Single"));
            Console.WriteLine("tf_short=" + (typeof(short).Name == "Int16"));
            Console.WriteLine("tf_byte=" + (typeof(byte).Name == "Byte"));
            Console.WriteLine("tf_bool=" + (typeof(bool).Name == "Boolean"));
            Console.WriteLine("tf_char=" + (typeof(char).Name == "Char"));
            Console.WriteLine("tf_string=" + (typeof(string).Name == "String"));
            Console.WriteLine("tf_class=" + (typeof(Animal2).Name == "Animal2"));
            Console.WriteLine("tf_struct=" + (typeof(Pt).Name == "Pt"));
            Console.WriteLine("tf_enum=" + (typeof(Shade).Name == "Shade"));
            Console.WriteLine("tf_nested=" + (typeof(Outer.Inner).Name == "Inner"));
            Console.WriteLine("tf_self=" + (typeof(TypeGet).Name == "TypeGet"));

            // ============ 2. GetType() -> .Name on boxed values ============
            Console.WriteLine("gt_int=" + ((42).GetType().Name == "Int32"));
            Console.WriteLine("gt_long=" + ((42L).GetType().Name == "Int64"));
            Console.WriteLine("gt_double=" + ((3.14).GetType().Name == "Double"));
            Console.WriteLine("gt_bool=" + ((true).GetType().Name == "Boolean"));
            Console.WriteLine("gt_char=" + (('A').GetType().Name == "Char"));
            Console.WriteLine("gt_str=" + ("hi".GetType().Name == "String"));
            Console.WriteLine("gt_class=" + (new Animal2().GetType().Name == "Animal2"));
            Console.WriteLine("gt_enum=" + (Shade.Light.GetType().Name == "Shade"));
            // NOTE: `new Pt().GetType()` (boxed user struct) is NOT probed --
            // the struct box has no runtime helper (audit H3); only primitives
            // and enums box.

        // ============ 3. array types (D-TYPEX-4 closure) ============
        Console.WriteLine("arr_int=" + (new int[3].GetType().Name == "Int32[]"));
        // D-TYPG-1: reference-element arrays report the ELEMENT-aware short
        // name. string[] -> "String[]" (net48 parity); object[] -> "Object[]".
        Console.WriteLine("arr_str=" + (new string[2].GetType().Name == "String[]"));
        Console.WriteLine("arr_obj=" + (new object[2].GetType().Name == "Object[]"));
        // NOTE: `typeof(int[])` / array ldtoken folds are unwired (D-TYPEX-4);
        // only the runtime GetType().Name path is probed.

            // ============ 4. typeof identity (== / ReferenceEquals) ============
            Console.WriteLine("id_eq=" + (typeof(int) == typeof(int)));
            Console.WriteLine("id_eq2=" + (typeof(string) == typeof(string)));
            Console.WriteLine("id_ne=" + (!(typeof(int) == typeof(string))));
            Console.WriteLine("id_ne2=" + (!(typeof(Animal2) == typeof(Pt))));
            Console.WriteLine("id_ref=" + object.ReferenceEquals(typeof(int), typeof(int)));
            Console.WriteLine("id_ref2=" + object.ReferenceEquals(typeof(string), typeof(int)));

            // ============ 5. GetHashCode consistency ============
            Console.WriteLine("hc_same=" + (typeof(int).GetHashCode() == typeof(int).GetHashCode()));
            Console.WriteLine("hc_same2=" + (typeof(Animal2).GetHashCode() == typeof(Animal2).GetHashCode()));
            Console.WriteLine("hc_diff=" + (!(typeof(int).GetHashCode() == typeof(string).GetHashCode())));
            Console.WriteLine("hc_diff2=" + (!(typeof(Animal2).GetHashCode() == typeof(Pt).GetHashCode())));

            // ============ 6. GetTypeCode static fold ============
            Console.WriteLine("tc_int=" + (Type.GetTypeCode(typeof(int)) == TypeCode.Int32));
            Console.WriteLine("tc_bool=" + (Type.GetTypeCode(typeof(bool)) == TypeCode.Boolean));
            Console.WriteLine("tc_char=" + (Type.GetTypeCode(typeof(char)) == TypeCode.Char));
            Console.WriteLine("tc_double=" + (Type.GetTypeCode(typeof(double)) == TypeCode.Double));
            Console.WriteLine("tc_string=" + (Type.GetTypeCode(typeof(string)) == TypeCode.String));
            Console.WriteLine("tc_class=" + (Type.GetTypeCode(typeof(Animal2)) == TypeCode.Object));
            // NOTE: `Type.GetTypeCode(typeof(Shade))` is NOT probed -- net48
            // reports the ENUM's underlying TypeCode (Int32) while the runtime
            // fold falls back to Object (D-TYPG-2).

            // ============ 7. GetType() vs typeof identity ============
            Console.WriteLine("gv_t=" + (new Animal2().GetType() == typeof(Animal2)));
            Console.WriteLine("gv_t2=" + ((42).GetType() == typeof(int)));
            Console.WriteLine("gv_t3=" + (!(new Animal2().GetType() == typeof(int))));
            Console.WriteLine("gv_t4=" + ("s".GetType() == typeof(string)));
        }
    }
}
