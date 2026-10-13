//------------------------------------------------------------------------------
// rf-generic.cs -- RF-N4 e2e probe: generic-type reflection identification
// (net48 oracle, byte-for-byte). Covers the three acceptance queries on
// typeof(Box<int>) / typeof(int) / typeof(Box<>):
//
//   typeof(int)       non-generic: IsGenericType=False, IsGenericTypeDefinition=
//                     False, GetGenericArguments().Length == 0 (the empty
//                     array), GetGenericTypeDefinition throws
//                     InvalidOperationException (net48).
//   typeof(Box<>)     open generic DEFINITION: IsGenericType=True,
//                     IsGenericTypeDefinition=True (fold-time 1),
//                     GetGenericTypeDefinition().FullName == "Cases.Box`1"
//                     (the definition's own name -- identity).
//   typeof(Box<int>)  closed INSTANTIATION: IsGenericType=True (runtime
//                     genericArity), IsGenericTypeDefinition=False (fold-time
//                     0 -- the TypeSpec is an instance, not the definition),
//                     GetGenericTypeDefinition().FullName == "Cases.Box`1"
//                     (the definition's own name -- identity; the simplified
//                     Type model folds a closed instantiation to the
//                     definition's full name, so identity IS the definition).
//
// GN-18 P2/D1 (S3 compile-time fold): the closed instantiation
// typeof(Box<int>) now folds its GetGenericArguments() call result into a
// constant NAME-SEQUENCE array at compile time (GENERICINST TypeSpec blob
// decoding, TypeNameForReflection's 0x15 arm bypassed by a NEW read-only
// branch -- the existing fold semantics are untouched). Both consumers are
// compile-time constants: .Length -> 1 and [0].FullName -> "System.Int32"
// (the net48 oracle values, byte-identical). Element access is NOT a
// half-state: the same fold decision covers Length and the element read, so
// there is no "length known, element unread" split (the runtime LOUD path of
// MethodMap.Type.cs GetGenericArguments remains for the non-foldable forms).
//
// GetGenericArguments on arity>0 types was previously NOT probed: the runtime
// LOUDs (NotSupportedException -- the genericArgNames plan B is deferred, no
// argument fabrication), which did not match the net48 oracle. The empty-array
// non-generic path IS probed and byte-identical.
//
// GN-18 P2/D1 guard probes (must stay LOUD -- no fabrication, no wrong arity):
//   * open definition typeof(Box<>): the VAR argument has NO managed full
//     name (StackFold.IsOpenGenericDefToken) -> LOUD (caught below).
//   * a stloc'd (temp-passed) result: only the DIRECT chain GetTypeFromHandle
//     -> GetGenericArguments -> get_Length / ldelem folds; storing the array
//     in a temp and reading afterwards is NOT wired -> LOUD.
//------------------------------------------------------------------------------
using System;

namespace Cases {
    public class Box<T> {
        public T Value;
        public Box(T v) { Value = v; }
    }

    public static class RfGeneric {
        public static void Main() {
            // ---- typeof(int) -- non-generic ----
            Console.WriteLine("int_isgeneric=" + typeof(int).IsGenericType);
            Console.WriteLine("int_isgenericdef=" + typeof(int).IsGenericTypeDefinition);
            Console.WriteLine("int_args_len=" + typeof(int).GetGenericArguments().Length);
            bool intDefThrows = false;
            try { typeof(int).GetGenericTypeDefinition(); }
            catch (InvalidOperationException) { intDefThrows = true; }
            Console.WriteLine("int_def_throws=" + intDefThrows);

            // ---- typeof(Box<>) -- the open generic DEFINITION ----
            Console.WriteLine("boxdef_isgeneric=" + typeof(Box<>).IsGenericType);
            Console.WriteLine("boxdef_isgenericdef=" + typeof(Box<>).IsGenericTypeDefinition);
            Console.WriteLine("boxdef_defname=" + typeof(Box<>).GetGenericTypeDefinition().FullName);

            // ---- typeof(Box<int>) -- a closed INSTANTIATION ----
            Console.WriteLine("boxint_isgeneric=" + typeof(Box<int>).IsGenericType);
            Console.WriteLine("boxint_isgenericdef=" + typeof(Box<int>).IsGenericTypeDefinition);
            Console.WriteLine("boxint_defname=" + typeof(Box<int>).GetGenericTypeDefinition().FullName);

            // ---- GN-18 P2/D1: the closed instantiation's generic arguments ----
            // S3 compile-time fold: the GetGenericArguments() result of a
            // closed GENERICINST typeof chain is a constant name-sequence
            // array, so BOTH consumers are compile-time constants.
            Console.WriteLine("boxint_args_len=" + typeof(Box<int>).GetGenericArguments().Length);
            Console.WriteLine("boxint_arg0=" + typeof(Box<int>).GetGenericArguments()[0].FullName);
            Console.WriteLine("boxint_arg0_name=" + typeof(Box<int>).GetGenericArguments()[0].Name);

            // ---- GN-18 P2/D1 guard probes (must stay LOUD) ----
            bool openDefArgsLoud = false;
            try { typeof(Box<>).GetGenericArguments(); }
            catch (Exception) { openDefArgsLoud = true; }
            Console.WriteLine("boxdef_args_loud=" + openDefArgsLoud);

            bool tempPassLoud = false;
            try
            {
                Type[] tmp = typeof(Box<int>).GetGenericArguments();
                int n = tmp.Length;
                Console.WriteLine("temp_args_len=" + n);
            }
            catch (Exception) { tempPassLoud = true; }
            Console.WriteLine("temp_args_loud=" + tempPassLoud);
        }
    }
}
