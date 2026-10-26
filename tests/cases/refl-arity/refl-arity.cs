//------------------------------------------------------------------------------
// refl-arity.cs -- REFL-ARITY e2e probe (net48 oracle, byte-for-byte):
//
//   The remaining reachable net48 overloads whose parameter shape is
//   compile-time known (arity + element/enum family) but had no mapping arm:
//
//     Type.GetNestedTypes(BindingFlags)                    -- the arity-1
//       filtered nested-type enumeration (the () overload is Public-only).
//     Type.GetEvent(string, BindingFlags)                  -- the flags-aware
//       single-event query.
//     Type.GetMethod(string, BindingFlags, Binder, Type[],
//                    ParameterModifier[])                   -- net48 ignores the
//       binder/modifiers (default binder; by-ref modifiers only); the explicit
//       BindingFlags DOES filter.
//     Type.GetConstructor(BindingFlags, Binder, Type[],
//                         ParameterModifier[])               -- same drop; the
//       BindingFlags filters the ctor scan.
//     Type.GetProperty(string, Type, Type[], ParameterModifier[])
//                                                          -- net48 ignores the
//       modifier array; the existing exact-signature primitive serves it.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class ArityHost
    {
        public class PubNested { }
        private class PrivNested { }

        public event EventHandler Evt;

        public void Method(int a, string b) { }

        public int this[int index] { get { return index; } set { } }

        public ArityHost() { }
        public ArityHost(int a) { }
    }

    public static class ReflArity
    {
        public static void Main()
        {
            var t = typeof(ArityHost);

            // ---- GetNestedTypes(BindingFlags.Public) ----
            foreach (var nt in t.GetNestedTypes(BindingFlags.Public))
                Console.WriteLine("pub_nested=" + nt.FullName);
            Console.WriteLine("pub_nested_count=" + t.GetNestedTypes(BindingFlags.Public).Length);

            // ---- GetNestedTypes(BindingFlags.NonPublic) ----
            foreach (var nt in t.GetNestedTypes(BindingFlags.NonPublic))
                Console.WriteLine("priv_nested=" + nt.FullName);
            Console.WriteLine("priv_nested_count=" + t.GetNestedTypes(BindingFlags.NonPublic).Length);

            // ---- GetEvent(string, BindingFlags) ----
            var ev = t.GetEvent("Evt", BindingFlags.Public | BindingFlags.Instance);
            Console.WriteLine("event=" + (ev != null ? ev.Name : "<null>"));

            // ---- GetMethod(string, BindingFlags, Binder, Type[], ParameterModifier[]) ----
            var m = t.GetMethod("Method", BindingFlags.Public | BindingFlags.Instance, null,
                new Type[] { typeof(int), typeof(string) }, null);
            Console.WriteLine("method=" + (m != null ? m.Name : "<null>"));

            // ---- GetConstructor(BindingFlags, Binder, Type[], ParameterModifier[]) ----
            var c = t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null,
                new Type[] { typeof(int) }, null);
            Console.WriteLine("ctor=" + (c != null ? c.Name : "<null>"));

            // ---- GetProperty(string, Type, Type[], ParameterModifier[]) ----
            var p = t.GetProperty("Item", typeof(int), new Type[] { typeof(int) }, null);
            Console.WriteLine("prop=" + (p != null ? p.Name : "<null>"));

            // ---- Assembly.GetType(string, bool throwOnError) ----
            var asm = typeof(ArityHost).Assembly;
            var a = asm.GetType("Cases.ArityHost", false);
            Console.WriteLine("asm_type=" + (a != null ? a.FullName : "<null>"));
            var miss = asm.GetType("Cases.DoesNotExist", false);
            Console.WriteLine("asm_type_missing=" + (miss != null ? miss.FullName : "<null>"));
        }
    }
}
