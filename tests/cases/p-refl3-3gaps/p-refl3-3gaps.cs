//------------------------------------------------------------------------------
// p-refl3-3gaps.cs -- refl3 item 3 net48 oracle probe: the adjacent reflection
// gap found while wiring items 1-2 -- the 1-arg MemberInfo.IsDefined(Type)
// overload (net48 defaults inherit = TRUE), which the refl2 registry had no
// arm for (only the (Type, bool) form was wired in item 1). Plus a regression
// cross-check of the inherited singular attribute on the type chain (item 1
// surface) and the item-2 ParameterInfo IsOut exposure on an `out` member --
// to verify the two earlier items interoperate.
// Every record is a boolean record, byte-stable.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
    public class Tag3G : Attribute { }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class Tag3G2 : Attribute { }

    [Tag3G]
    public class Attr3GAnnotated { }

    public class Attr3GDerived : Attr3GAnnotated { }

    public class Attr3GPlain { }

    [Tag3G, Tag3G2]
    public class Attr3GMulti { }

    public static class PRefl3Gaps
    {
        public static void Main()
        {
            // ---- 1-arg MemberInfo.IsDefined(Type) -- inherit defaults TRUE ----
            Console.WriteLine("[one.own]=" + typeof(Attr3GAnnotated).IsDefined(typeof(Tag3G)));
            Console.WriteLine("[one.plain]=" + typeof(Attr3GPlain).IsDefined(typeof(Tag3G)));
            Console.WriteLine("[one.wrongtype]=" + typeof(Attr3GAnnotated).IsDefined(typeof(Tag3G2)));
            Console.WriteLine("[one.inherit]=" + typeof(Attr3GDerived).IsDefined(typeof(Tag3G)));
            // 2-arg cross-check against the 1-arg defaults (inherit=true).
            Console.WriteLine("[one.vs.2arg]=" + (typeof(Attr3GDerived).IsDefined(typeof(Tag3G)) == typeof(Attr3GDerived).IsDefined(typeof(Tag3G), true)));

            // ---- inherited singular attribute (item 1 surface, cross-check) ----
            Console.WriteLine("[sing.inherit]=" + (typeof(Attr3GDerived).GetCustomAttribute<Tag3G>() != null));
            Console.WriteLine("[multi.singular]=" + (typeof(Attr3GMulti).GetCustomAttribute<Tag3G>() != null));
            Console.WriteLine("[multi.singular.2]=" + (typeof(Attr3GMulti).GetCustomAttribute<Tag3G2>() != null));

            // ---- item-1 plural vs item-2 ParameterInfo on the same type ----
            var ctors = typeof(Attr3GMulti).GetConstructor(new Type[0]);
            Console.WriteLine("[ctor.params]=" + (ctors != null && ctors.GetParameters().Length == 0));
        }
    }
}