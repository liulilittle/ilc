//------------------------------------------------------------------------------
// p-refl6-nested.cs -- refl6b item A net48 oracle probe: Type.GetNestedType
// (string) + GetNestedType(string, BindingFlags) + the GetNestedTypes() public
// default. net48 semantics pinned here (probe-verified on the live net48):
//   * GetNestedType(name) default binding is Public-only: a PUBLIC nested
//     type matches, private/protected/internal return NULL ([n.pub] found,
//     [n.priv]=NULL [n.prot]=NULL [n.int]=NULL);
//   * GetNestedType(name, NonPublic|Instance) finds every private/protected/
//     internal nested type ([n.priv.flags] etc. -- the binding's NonPublic bit
//     admits non-public nested entries);
//   * a nonexistent name returns NULL (no throw) -- [n.none]=NULL;
//   * an EMPTY name returns NULL without throwing -- [n.empty.throw]=none;
//   * GetNestedTypes() (no flags) is PUBLIC-ONLY -- [n.all]=1 for a type with
//     four nested classes where only N6Pub is public.
// The value returned is the nested type's net48 '+' reflection full name
// ("Cases.N6Outer+N6Pub"), i.e. a Type VALUE (full-name string) -- the same
// carrier GetNestedTypes returns.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class N6Outer
    {
        public class N6Pub { }
        private class N6Priv { }
        protected class N6Prot { }
        internal class N6Int { }
    }

    public static class PRefl6Nested
    {
        public static void Main()
        {
            var t = typeof(N6Outer);

            // ---- GetNestedType(name) default binding = Public-only ----
            var pub = t.GetNestedType("N6Pub");
            Console.WriteLine("[n.pub]=" + (pub == null ? "NULL" : pub.FullName));
            Console.WriteLine("[n.priv]=" + (t.GetNestedType("N6Priv") == null ? "NULL" : t.GetNestedType("N6Priv").FullName));
            Console.WriteLine("[n.prot]=" + (t.GetNestedType("N6Prot") == null ? "NULL" : t.GetNestedType("N6Prot").FullName));
            Console.WriteLine("[n.int]=" + (t.GetNestedType("N6Int") == null ? "NULL" : t.GetNestedType("N6Int").FullName));

            // ---- GetNestedType(name, NonPublic|Instance) finds all ----
            var npriv = t.GetNestedType("N6Priv", BindingFlags.NonPublic | BindingFlags.Instance);
            Console.WriteLine("[n.priv.flags]=" + (npriv == null ? "NULL" : npriv.FullName));
            var nprot = t.GetNestedType("N6Prot", BindingFlags.NonPublic | BindingFlags.Instance);
            Console.WriteLine("[n.prot.flags]=" + (nprot == null ? "NULL" : nprot.FullName));
            var nint = t.GetNestedType("N6Int", BindingFlags.NonPublic | BindingFlags.Instance);
            Console.WriteLine("[n.int.flags]=" + (nint == null ? "NULL" : nint.FullName));

            // ---- nonexistent -> NULL, no throw ----
            Console.WriteLine("[n.none]=" + (t.GetNestedType("NoSuch") == null ? "NULL" : t.GetNestedType("NoSuch").FullName));

            // ---- GetNestedTypes() default = Public only ----
            var nts = t.GetNestedTypes();
            Console.WriteLine("[n.all]=" + nts.Length);
            if (nts.Length > 0)
                Console.WriteLine("[n.all.name]=" + nts[0].FullName);

            // ---- empty name -> NULL (throws nothing) ----
            try
            {
                var e = t.GetNestedType("");
                Console.WriteLine("[n.empty.throw]=none val=" + (e == null ? "NULL" : e.FullName));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[n.empty.throw]=" + ex.GetType().Name);
            }
        }
    }
}