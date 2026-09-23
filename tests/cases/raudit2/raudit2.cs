//------------------------------------------------------------------------------
// raudit2.cs -- REFLECTION AUDIT PROBE 2 (G6 static audit, suspicious #4/#5):
// Activator.CreateInstance(Type) for BCL primitives and user classes.
//
// Probes:
//   * CreateInstance(typeof(int))  -> net48: boxed 0 (non-null), no exception.
//     Static audit (reflect_base.cpp:225-232): the runtime only resolves the
//     typeinfo registry (no BCL class_by_name fallback) -> expected
//     MissingMethodException.
//   * CreateInstance(typeof(C)) with C { public int x = 5; public C() {} }
//     -> net48: ((C)o).x == 5 (the parameterless ctor runs).
//     Static audit: the runtime primitive only allocates (il2cpp_object_new,
//     zero-init) and never runs the .ctor -> suspected x == 0.
//
// Each probe is wrapped in try/catch so the output line is either the value
// label or "label_ex=<ExceptionTypeName>" -- both sides print one line per
// probe and the diff stays informative.
//------------------------------------------------------------------------------
using System;

namespace Cases {
public class C { public int x = 5; public C() {} }

public static class Raudit2 {
    public static void Main()
    {
        try {
            object o = Activator.CreateInstance(typeof(int));
            Console.WriteLine("ci_int_null=" + (o == null));
        } catch (Exception e) {
            Console.WriteLine("ci_int_ex=" + e.GetType().Name);
        }

        try {
            object o = Activator.CreateInstance(typeof(C));
            Console.WriteLine("ci_c_x=" + ((C)o).x);
        } catch (Exception e) {
            Console.WriteLine("ci_c_ex=" + e.GetType().Name);
        }
    }
}}
