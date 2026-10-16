//------------------------------------------------------------------------------
// p-refl3-1attrs.cs -- refl3 item 1 net48 oracle probe: the generic
// CustomAttributeExtensions surface (GetCustomAttribute<T> /
// GetCustomAttributes<T>) + MemberInfo.IsDefined(Type, bool) +
// Attribute.IsDefined(...). The simplified attribute model answers through
// the attribute.cpp registry (type-name keyed); this probe pins the net48
// semantics the new MethodMap arms must reproduce byte-exact:
//   * CustomAttributeExtensions.GetCustomAttribute<T>(Type element) -- a Type
//     VALUE receiver; net48: a present attribute -> the attribute INSTANCE
//     (non-null), absent / other-typed -> null.
//   * CustomAttributeExtensions.GetCustomAttributes<T>(Type element) -- the
//     plural; net48 returns an IEnumerable<T> whose runtime object is the
//     T[] the registry returns, so an (object[]) cast exposes the SAME length
//     (byte-exact count).
//   * memberInfo.IsDefined(typeof(T), bool) -- the receiver is the MemberInfo
//     VALUE (full-name string); existence query with attributeType filter +
//     inherit.
//   * Attribute.IsDefined(element, typeof(T)[, bool]) -- the static form.
// Every record is a boolean / small-int / short-name comparison record,
// byte-stable across the net48 oracle and the transpiled runtime.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Cases
{
    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
    public class Tag3A : Attribute { }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
    public class Tag3B : Attribute { }

    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = true)]
    public class Tag3C : Attribute { }

    // Derived attribute class (the assignability filter key for <T> routing).
    public class Derived3B : Tag3B { }

    [Tag3A]
    public class Attr3Annotated { }

    // Inherits [Tag3A] through the base chain (inherit semantics).
    public class Attr3Derived : Attr3Annotated { }

    public class Attr3Plain { }

    [Derived3B]
    public class Attr3WithDerived { }

    public static class PRefl3Attrs
    {
        // The plural result's runtime object is the attribute T[]; an
        // (object[]) cast via object exposes the SAME array on net48 (array
        // covariance), so the count is byte-comparable.
        private static int PluralLen<T>(IEnumerable<T> e) where T : Attribute
        {
            var arr = (object[])(object)e;
            return arr.Length;
        }

        public static void Main()
        {
            // ---- singular generic: CustomAttributeExtensions.GetCustomAttribute<T> ----
            Console.WriteLine("[ga.present]=" + (typeof(Attr3Annotated).GetCustomAttribute<Tag3A>() != null));
            Console.WriteLine("[ga.absent]=" + (typeof(Attr3Plain).GetCustomAttribute<Tag3A>() == null));
            Console.WriteLine("[ga.wrongtype]=" + (typeof(Attr3Annotated).GetCustomAttribute<Tag3B>() == null));
            Console.WriteLine("[ga.basefilter]=" + (typeof(Attr3WithDerived).GetCustomAttribute<Tag3B>() != null));
            Console.WriteLine("[ga.basefilter.derived]=" + (typeof(Attr3WithDerived).GetCustomAttribute<Derived3B>() != null));
            // inherit default for the generic singular (probe-pinned): the
            // attribute is on the BASE; the extension generic singular finds it
            // (inherit=true) -- contrasted with the STATIC 2-arg singular
            // below (net48 static singular defaults inherit=FALSE, attrinherit).
            Console.WriteLine("[ga.inherit.derived]=" + (typeof(Attr3Derived).GetCustomAttribute<Tag3A>() != null));

            // ---- plural generic: GetCustomAttributes<T> ----
            Console.WriteLine("[gb.present.len]=" + PluralLen<Tag3A>(typeof(Attr3Annotated).GetCustomAttributes<Tag3A>()));
            Console.WriteLine("[gb.absent.len]=" + PluralLen<Tag3A>(typeof(Attr3Plain).GetCustomAttributes<Tag3A>()));
            Console.WriteLine("[gb.wrongtype.len]=" + PluralLen<Tag3B>(typeof(Attr3Annotated).GetCustomAttributes<Tag3B>()));
            Console.WriteLine("[gb.inherit.len]=" + PluralLen<Tag3A>(typeof(Attr3Derived).GetCustomAttributes<Tag3A>()));
            Console.WriteLine("[gb.basefilter.len]=" + PluralLen<Tag3B>(typeof(Attr3WithDerived).GetCustomAttributes<Tag3B>()));
            // strict-T filtering on the plural (the [Derived3B] does NOT match
            // a <Tag3B> query when the assignability walk is absent -- it MUST
            // appear, because Derived3B : Tag3B).
            Console.WriteLine("[gb.basefilter.derived.len]=" + PluralLen<Derived3B>(typeof(Attr3WithDerived).GetCustomAttributes<Derived3B>()));

            // ---- MemberInfo.IsDefined(Type, bool) ----
            Console.WriteLine("[def.own.true]=" + typeof(Attr3Annotated).IsDefined(typeof(Tag3A), true));
            Console.WriteLine("[def.own.false]=" + typeof(Attr3Annotated).IsDefined(typeof(Tag3A), false));
            Console.WriteLine("[def.plain]=" + typeof(Attr3Plain).IsDefined(typeof(Tag3A), true));
            Console.WriteLine("[def.wrongtype]=" + typeof(Attr3Annotated).IsDefined(typeof(Tag3B), true));
            Console.WriteLine("[def.inherit.true]=" + typeof(Attr3Derived).IsDefined(typeof(Tag3A), true));
            Console.WriteLine("[def.inherit.false]=" + typeof(Attr3Derived).IsDefined(typeof(Tag3A), false));
            Console.WriteLine("[def.basefilter]=" + typeof(Attr3WithDerived).IsDefined(typeof(Tag3B), true));

            // ---- Attribute.IsDefined(element, attributeType[, bool]) ----
            Console.WriteLine("[astat.2arg]=" + Attribute.IsDefined(typeof(Attr3Annotated), typeof(Tag3A)));
            Console.WriteLine("[astat.2arg.wrong]=" + Attribute.IsDefined(typeof(Attr3Annotated), typeof(Tag3B)));
            Console.WriteLine("[astat.2arg.inherit]=" + Attribute.IsDefined(typeof(Attr3Derived), typeof(Tag3A)));
            Console.WriteLine("[astat.3arg.true]=" + Attribute.IsDefined(typeof(Attr3Annotated), typeof(Tag3A), true));
            Console.WriteLine("[astat.3arg.false]=" + Attribute.IsDefined(typeof(Attr3Derived), typeof(Tag3A), false));
            Console.WriteLine("[astat.3arg.false.own]=" + Attribute.IsDefined(typeof(Attr3Annotated), typeof(Tag3A), false));
            Console.WriteLine("[astat.plain]=" + Attribute.IsDefined(typeof(Attr3Plain), typeof(Tag3A), true));

            // Static 2-arg singular: net48 default inherit=FALSE (attrinherit
            // pin) -- the base-only [Tag3A] is NOT found on Attr3Derived.
            Console.WriteLine("[gastat.singular.inherit]=" + (Attribute.GetCustomAttribute(typeof(Attr3Derived), typeof(Tag3A)) == null));
        }
    }
}