//------------------------------------------------------------------------------
// rest2-refl.cs -- the statically-analyzable Type/Reflection LOUD items wired by
// the impl-rest2 batch (net48 reference oracle):
//
//   * Type.Equals(Type) -- net48's virtual (Type) overload (type.cs:1792); the
//     CLASS signature byte 0x12 identifies it (distinct from Equals(object)'s
//     0x1C) and maps to the same type-identity icall.
//   * Type.GetMember(string, MemberTypes, BindingFlags) -- net48's 3-param
//     form (type.cs:768); the runtime filters the five-slice union by BOTH the
//     BindingFlags and the MemberTypes bit set.
//   * Assembly.GetName(bool copiedName) -- net48's arity-1 form
//     (reflection_assembly.cs:720); identity (the AssemblyName VALUE is the
//     assembly simple-name string).
//   * Assembly.GetType(string, bool throwOnError, bool ignoreCase) -- net48's
//     3-param form (reflection_assembly.cs:758); the ignoreCase flag folds ASCII
//     case on the registry key comparison.
//
// Every line prints "label=value". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class R2Base
    {
        public void MInh() { }
    }

    public class R2 : R2Base
    {
        public int FPub;
        private int pp;
        // Computed accessors (non-foldable bodies) so the transpiler emits real
        // get_PP/set_PP functions -- a folded auto-property accessor carries a
        // null fnPtr (the runtime-unreachable boundary documented in rf-props).
        public int PP { get { return GetPp(); } set { SetPp(value); } }
        private int GetPp() { return pp; }
        private void SetPp(int v) { pp = v; }

        public void MDirect() { }
    }

    public static class Rest2Refl
    {
        public static void Main()
        {
            var t = typeof(R2);

            // ---- Type.Equals(Type) ----
            Console.WriteLine("[eq.type]=" + typeof(R2).Equals((Type)t));
            Console.WriteLine("[eq.other]=" + typeof(R2).Equals(typeof(R2Base)));
            Console.WriteLine("[eq.obj]=" + typeof(R2).Equals((object)t));
            Console.WriteLine("[eq.nonobj]=" + typeof(R2).Equals("x"));

            // ---- Type.GetMember(name, MemberTypes, BindingFlags) ----
            var gmM = t.GetMember("MInh", MemberTypes.Method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Console.WriteLine("[gmm.cnt]=" + gmM.Length);
            Console.WriteLine("[gmm.name]=" + gmM[0].Name);
            Console.WriteLine("[gmm.kind]=" + (int)gmM[0].MemberType);

            // MemberTypes.Method excludes the field / property / event slices.
            Console.WriteLine("[gmm.field]=" + t.GetMember("FPub", MemberTypes.Method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length);
            Console.WriteLine("[gmm.prop]=" + t.GetMember("PP", MemberTypes.Property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length);
            Console.WriteLine("[gmm.all]=" + t.GetMember("FPub", MemberTypes.All, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length);

            // ---- Assembly.GetName(bool) ----
            var asm = Assembly.GetExecutingAssembly();
            Console.WriteLine("[an.simple]=" + asm.GetName(false).Name);

            // ---- Assembly.GetType(name, throwOnError, ignoreCase) ----
            Console.WriteLine("[agt.hit]=" + (asm.GetType("Cases.R2", false, false) != null));
            Console.WriteLine("[agt.miss]=" + (asm.GetType("Cases.NoSuch", false, false) == null));
            Console.WriteLine("[agt.ignorecase]=" + (asm.GetType("cases.r2", false, true) != null));
            Console.WriteLine("[agt.ignorecase.miss]=" + (asm.GetType("cases.r2", false, false) == null));

            try
            {
                asm.GetType("Cases.NoSuch", true, false);
                Console.WriteLine("[agt.throw]=none");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[agt.throw]=" + ex.GetType().Name);
            }

            // ---- PropertyInfo.SetValue binder arity (6-param) ----
            // The accessor is materialized by a direct call first (the runtime's
            // reflection Invoke only reaches AOT-compiled methods).
            var target = new R2();
            target.PP = 7;
            var pi = typeof(R2).GetProperty("PP");
            pi.SetValue(target, 42, BindingFlags.SetProperty, null, null, null);
            Console.WriteLine("[psv.val]=" + target.PP);

            // ---- MethodInfo.Invoke binder arity (5-param) ----
            var mi = typeof(R2).GetMethod("MDirect");
            target.MDirect();
            mi.Invoke(target, BindingFlags.InvokeMethod, null, null, null);
            Console.WriteLine("[minv.ok]=done");
        }
    }
}
