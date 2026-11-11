//------------------------------------------------------------------------------
// isvalue.cs -- A2: typeof(T).IsValueType for a generic parameter T (VAR)
// inside a clone body. net48 oracle (byte-for-byte):
//
//   typeof(System.Int32).IsValueType == true
//   typeof(Cases.Color).IsValueType  == true   (enum is a value type)
//   typeof(System.String).IsValueType == false
//
// For a generic T (VAR, et=0x13) inside Box<T>.GetVT(), the TypeKindFlag
// valuetype branch previously returned false WITHOUT reading the
// InstantiationSuffixHint (StackFold.cs:8446-8462), so Box<int>.GetVT()
// answered false instead of true -- a silent wrong value.
//------------------------------------------------------------------------------
using System;

namespace Cases {
    public enum Color { Red, Green, Blue }

    public class Box<T> {
        T item;
        public Box(T v) { item = v; }
        // GetVT returns typeof(T).IsValueType -- the VAR path under test.
        public static bool GetVT() => typeof(T).IsValueType;
        public T Get() => item;
    }

    public static class IsValue {
        public static void Main() {
            // BCL direct probes (sanity: these already worked via TypeRef).
            Console.WriteLine("int32_vt=" + typeof(System.Int32).IsValueType);
            Console.WriteLine("string_vt=" + typeof(System.String).IsValueType);
            Console.WriteLine("color_vt=" + typeof(Cases.Color).IsValueType);

            // The VAR path: Box<int>.GetVT() must be true, Box<string>.GetVT()
            // must be false (a reference generic argument is NOT a value type).
            Console.WriteLine("boxint_getvt=" + Box<int>.GetVT());
            Console.WriteLine("boxstr_getvt=" + Box<string>.GetVT());
        }
    }
}
