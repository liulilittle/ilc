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
// typeof(Box<int>) now folds its GetGenericArguments() call result at the
// CONSUMPTION point -- the immediate next instruction must be the consumer:
//   ldtoken <封闭GENERICINST TypeSpec> ; call GetTypeFromHandle ; call
//   GetGenericArguments ; ldlen            -> IntConstExpr(arity)
// and the same fold decision covers the element read
//   ... ; ldc.i4.i ; ldelem               -> StringConstExpr(managed full name)
// so both consumers are compile-time constants from ONE fold decision: there
// is no "length known, element unread" half-state. .Length -> 1 and
// [0].FullName -> "System.Int32" (the net48 oracle values, byte-identical).
// The runtime LOUD path of MethodMap.Type.cs GetGenericArguments remains for
// the non-foldable forms.
//
// GN-18 P2/D1 deliberately NOT probed (net48 does not throw for these, so an
// oracle probe could never byte-match -- the LOUD paths are pinned by the
// tests/unit icall-mapping asserts and the runtime's own arity check in
// typeinfo.cpp, the same convention as this file's original RF-N4 probe):
//   * the open definition typeof(Box<>): its argument is a VAR with no
//     managed full name (StackFold.IsOpenGenericDefToken) -> LOUD.
//   * a result stored in a temp / read afterwards: only the DIRECT chain
//     (ldlen / Ldelem with a CONSTANT in-range index) folds -> LOUD otherwise.
//   * a runtime receiver (obj.GetType() / Type.GetType(string)), a generic
//     interface or struct (no clone, CodeGenerator.cs:577) -> LOUD.
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
            // S3 compile-time fold (Plan B -- consumption-point interception, no
            // new IR node): ldtoken <closed GENERICINST> -> GetTypeFromHandle ->
            // GetGenericArguments, consumed DIRECTLY by
            //   ldlen                        -> IntConstExpr(arity)          = 1
            //   ldc.i4.<const> + Ldelem_ref  -> StringConstExpr(arg's name)  = "System.Int32"
            // ONE fold decision covers both consumers, so there is no
            // "length known, element unread" half-state. The element string
            // then rides the EXISTING Type-domain consumer folds
            // (get_FullName -> identity "System.Int32"; get_Name -> "Int32").
            // The argument names are decoded from the TypeSpec GENERICINST
            // blob (metadata side) -- never from the emit-side clone suffix.
            //
            // Deliberately NOT probed (net48 does not throw, so an oracle probe
            // could never byte-match; the LOUD paths are pinned by the
            // tests/unit icall-mapping asserts + the runtime arity check in
            // typeinfo.cpp -- the same convention as the RF-N4 probe above):
            //   * the OPEN definition typeof(Box<>) -- its argument is a VAR
            //     with no managed full name (StackFold.IsOpenGenericDefToken);
            //   * a result stored in a temp / read afterwards -- only the
            //     DIRECT chain with a CONSTANT in-range index folds;
            //   * a variable / out-of-range index;
            //   * a runtime receiver (obj.GetType() / Type.GetType(string));
            //   * a generic interface or struct (no clone, CodeGenerator.cs:577).
            Console.WriteLine("boxint_args_len=" + typeof(Box<int>).GetGenericArguments().Length);
            Console.WriteLine("boxint_arg0=" + typeof(Box<int>).GetGenericArguments()[0].FullName);
            Console.WriteLine("boxint_arg0_name=" + typeof(Box<int>).GetGenericArguments()[0].Name);
        }
    }
}
