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
// GetGenericArguments on arity>0 types is deliberately NOT probed: the
// runtime LOUDs (NotSupportedException -- the genericArgNames plan B is
// deferred, no argument fabrication), which would not match the net48 oracle
// (Length==1). The empty-array non-generic path IS probed and byte-identical.
// The LOUD arity>0 path is pinned by the MethodMapTests.Type.cs icall mapping
// assert + the runtime's own arity check (typeinfo.cpp).
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
        }
    }
}
