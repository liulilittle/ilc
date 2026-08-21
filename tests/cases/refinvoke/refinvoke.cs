using System;
namespace Cases { public static class RefInvoke {
    public static void Main() {
        // Reflection MethodInfo/Invoke chain e2e (sibling of reflectcase:
        // reflectcase covers typeof + Type.GetType; THIS case covers the
        // Type.GetMethod -> MethodInfo -> Invoke chain).
        //
        // RECORDED GAPS:
        //   GAP-REFINVOKE-1 (Invoke wiring, taskcard T308 NOT landed):
        //     System.Reflection MethodInfo::Invoke has NO icall mapping in
        //     the transpiler (MethodMap*.cs has no MethodBase/MethodInfo/
        //     "Invoke" entry; the only "Invoke" family wired is the DELEGATE
        //     one, DelegateInvokeExpr). Writing a real mi.Invoke(...) call
        //     would hit the LOUD NotSupportedException in MethodRefResolver
        //     and KILL the transpile (reflectcase GAP-REFL-3 precedent), so
        //     NO Invoke call is written -- the gap is recorded here instead.
        //     The runtime side (invoker.cpp) IS built, but the transpiled
        //     simplified member model (typeinfo.h: GetMethod returns the
        //     member-NAME string, not a MethodInfo object) has no fnPtr to
        //     invoke through anyway.
        //   GAP-REFINVOKE-2 (typeinfo registry, inherited from reflectcase
        //     GAP-REFL-1): the typeinfo registry (typeinfo.cpp) is populated
        //     ONLY by generated startup code emitting il2cpp_typeinfo_register
        //     per reflected type (TypeInfo emission, reflection-runtime-
        //     design.md). That emission has NOT landed in the transpiler, so
        //     EVERY registry lookup misses: typeof(Calc).GetMethod("Add") ->
        //     null in this build. The GetMethod probes below print net48
        //     reference values and FAIL in this build until TypeInfo emission
        //     lands.
        //   GAP-REFINVOKE-3 (build include chain, inherited from reflectcase
        //     GAP-REFL-2): generated.h includes only il2cpp_runtime.h, which
        //     includes neither reflect_base.h nor typeinfo.h, so the wired
        //     il2cpp_typeinfo_get_method call fails to COMPILE ("no member
        //     named 'il2cpp_typeinfo_get_method'"). Same family as the
        //     crypto_obj.h include gap fixed in matrix5. reflectcase's own
        //     build_mingw has no il2cpp_gen_exe.exe -- the include gap has
        //     never been fixed.
        //   GAP-REFINVOKE-4 (op_Equality static/instance classification,
        //     transpiler, NOT this task's scope): `m == null` on a MethodInfo
        //     compiles (net48 csc, IL-verified) to a STATIC
        //     MethodInfo::op_Equality call, but MethodRefResolver's default
        //     op_* heuristic classifies it INSTANCE -> StackFold dies with
        //     "pops this but the stack is empty" (the reflectcase
        //     Type.op_Equality family, recorded there at lines 40-43). Only
        //     ReferenceEquals probes are written below -- the == null probe
        //     was removed after the transpile hit this wall (10:4x).
        //
        // --- typeof chain (passes; typechain/reflectcase precedent) ---
        Console.WriteLine(typeof(Calc).Name == "Calc");            // True
        Console.WriteLine(typeof(Calc).Name == "Add");             // False
        Console.WriteLine(typeof(string).Name == "String");        // True
        // --- custom class method registration, DIRECT call (reflection-free
        //     proof the method is registered and callable) ---
        Calc c = new Calc();
        Console.WriteLine(c.Add(2, 3) == 5);                       // True
        Console.WriteLine(c.Mul(4, 5) == 20);                      // True
        // --- Type.GetMethod(string) chain (wired M5.5,
        //     MethodMap.Type.cs:99-103 -> il2cpp_typeinfo_get_method;
        //     simplified member model: a MethodInfo VALUE is the member-name
        //     string, so ReferenceEquals / == null are the probe forms) ---
        Console.WriteLine(!ReferenceEquals(typeof(Calc).GetMethod("Add"), null));    // True (net48) / False (ours: GAP-REFINVOKE-2)
        Console.WriteLine(ReferenceEquals(typeof(Calc).GetMethod("Add"), null));     // False (net48) / True (ours)
        Console.WriteLine(ReferenceEquals(typeof(Calc).GetMethod("NoSuch"), null));  // True (both: not-found -> null)
    }
}}

public class Calc {
    public int Add(int a, int b) { return a + b; }
    public int Mul(int a, int b) { return a * b; }
}
