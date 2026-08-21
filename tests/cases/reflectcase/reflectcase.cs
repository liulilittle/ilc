using System;
namespace Cases { public static class ReflectCase {
    public static void Main() {
        // Reflection chain e2e: typeof keyword (passing precedent: typechain)
        // + Type.GetType(string) through the typeinfo registry (reflect_base.h
        // R-REFL-07/08). All probes bool.
        //
        // RECORDED GAPS:
        //   GAP-REFL-1 (registry): the typeinfo registry (typeinfo.cpp) is
        //     populated ONLY by generated startup code emitting
        //     il2cpp_typeinfo_register per reflected type (TypeInfo emission,
        //     reflection-runtime-design.md). That emission has NOT landed in
        //     the transpiler, so in this build every registry lookup misses:
        //     Type.GetType(name) -> null (the identity model -- a Type VALUE
        //     is its full-name string -- is never reached). The GetType
        //     probes below print net48 reference values and FAIL in this
        //     build until TypeInfo emission lands.
        //   GAP-REFL-2 (build include chain): generated.h does not include
        //     reflect_base.h/typeinfo.h, so a transpiled case calling
        //     il2cpp_type_get_type fails to COMPILE ("no member named
        //     'il2cpp_type_get_type' in namespace 'il2cpp'") -- same family
        //     as the crypto_obj.h include gap fixed in matrix5.
        //   GAP-REFL-3 (API surface, e2e-unreachable): Activator
        //     .CreateInstance(string) -- the single-arg string overload
        //     mapped in MethodMap.Reflect -- DOES NOT EXIST in .NET
        //     Framework 4.8 (verified against v4.0.30319 mscorlib: only
        //     (Type,...)/(string,string)/(ActivationContext) overloads).
        //     It is .NET Core-only (removed in Core 2.0), so NO net48
        //     reference-run probe can express it; any net48-expressible
        //     CreateInstance form (Type/2-arg) hits the LOUD
        //     NotSupportedException in MethodMap.Reflect and kills the
        //     transpile. The icall itself is covered by the runtime unit
        //     suite test_reflect_base (register-first fixture). An e2e
        //     probe becomes possible only with a non-net48 input assembly.
        //
        // --- typeof chain (passes; typechain precedent) ---
        Console.WriteLine(typeof(ReflectA).Name == "ReflectA");            // True
        Console.WriteLine(typeof(ReflectB).Name == "ReflectB");            // True
        Console.WriteLine(typeof(string).Name == "String");                // True
        // ReferenceEquals form: `typeof(X) == typeof(Y)` compiles to
        // Type.op_Equality, which is NOT wired in the transpiler (matrix5
        // typechain family: resolved as INSTANCE, "pops this but the stack
        // is empty"). ReferenceEquals -> ::refeq:: pointer compare (folded).
        Console.WriteLine(ReferenceEquals(typeof(int), typeof(int)));      // True
        Console.WriteLine(ReferenceEquals(typeof(ReflectA), typeof(ReflectB))); // False
        // --- Type.GetType(string) chain (identity model: hit -> the input
        // name string itself; miss -> null, never throws). ReferenceEquals
        // form: `Type == Type` compiles to Type.op_Equality, which the
        // transpiler misclassifies as INSTANCE (matrix5 typechain family,
        // "pops this but the stack is empty") -- the ReferenceEquals
        // overload is wired (internchain precedent). ---
        Console.WriteLine(!ReferenceEquals(Type.GetType("System.Int32"), null));           // True  (GAP-REFL-1: registry empty -> null)
        Console.WriteLine(!ReferenceEquals(Type.GetType("Cases.ReflectA"), null));         // False (net48: calling-assembly lookup miss; ours: registry miss)
        Console.WriteLine(ReferenceEquals(Type.GetType("System.Int32"), Type.GetType("System.Int32"))); // True (net48 cached Type; ours null==null)
        Console.WriteLine(ReferenceEquals(Type.GetType("No.Such.Type"), null));            // True  (not-found -> null contract, both)
    }
}}

public class ReflectA { }
public class ReflectB { }
