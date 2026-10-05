//------------------------------------------------------------------------------
// attrinherit.cs -- Attribute FILTER + INHERIT deep e2e reference case (net48
// oracle, byte-for-byte). Created in the R→G→C batch, R4 (B3).
//
// Probes the net48-semantics _ex_named query surface (attribute.cpp
// CollectAttributes): the attributeType assignability filter (registered name
// + base-class chain) and the baseType-chain inheritance walk with the
// explicit inherit arguments -- the 2-arg (element, bool) and 3-arg (element,
// Type, bool) overloads of Attribute.GetCustomAttribute(s). The 2-arg
// (element, Type) SINGULAR overload's default inherit is oracle-pinned here
// (net48: singular defaults to inherit = false, plural to inherit = true --
// the documented inconsistency between the two static forms).
//
// AttributeUsage: all attribute classes here use the net48 DEFAULTS
// (Inherited = true, AllowMultiple = false) -- no [AttributeUsage] declaration
// rides on them, so the transpiled side (which does not re-register usage)
// answers the same defaults. AllowMultiple = false exact-type shadowing is
// exercised (a more-derived [Tag] shadows the base [Tag]).
//
// Byte-stability: every line prints "label=True/False"; no attribute QUERY
// numeric outputs, no non-constant ctor args, no exotic VT values.
// net48 is the reference baseline; every line must be byte-identical.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases {

// Default usage on both sides: Inherited = true, AllowMultiple = false.
class Tag : Attribute { }
// An attribute class DERIVING Tag (the assignability filter key).
class DerivedTag : Tag { }

// Base carries [Tag] only; Derived (no own attribute) inherits it.
[Tag]
class Base { }
class Derived : Base { }

// DerivedTagged carries a [DerivedTag] OWN attribute (assignable to Tag).
[DerivedTag]
class DerivedTagged : Base { }

// AllowMultiple-shadowing pair: both levels carry the SAME exact attribute
// type [Tag]; the derived one shadows the base one (net48 AllowMultiple=false
// rule, keyed on the exact runtime type).
[Tag]
class BaseShadow { }
[Tag]
class DerivedShadow : BaseShadow { }

public static class AttrInherit {
    public static void Main() {
        // ---- 1. inheritance walk via the explicit bool overload (2-arg) ----
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(Base), true).Length == 1);     // own [Tag]
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(Derived), true).Length == 1);  // base [Tag] inherited
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(Derived), false).Length == 0); // own type only: none

        // ---- 2. attributeType filter + inherit (3-arg plural) ----
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(Derived), typeof(Tag), true).Length == 1);  // inherited [Tag]
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(Derived), typeof(Tag), false).Length == 0); // own only
        // Filter by the DERIVED attribute class: only the [DerivedTag] own
        // instance matches; the base [Tag] instance is not assignable.
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(DerivedTagged), typeof(DerivedTag), true).Length == 1);
        // Filter by the BASE attribute class: both the own [DerivedTag] and
        // the inherited [Tag] match (assignability base chain).
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(DerivedTagged), typeof(Tag), true).Length == 2);

        // ---- 3. singular overloads + explicit inherit ----
        Console.WriteLine(Attribute.GetCustomAttribute(typeof(Derived), typeof(Tag), true) != null);
        Console.WriteLine(Attribute.GetCustomAttribute(typeof(Derived), typeof(Tag), false) == null);
        // 2-arg (element, Type) singular: net48 default inherit = FALSE -> the
        // derived type's own attribute set has no [Tag] -> null. (The transpiler
        // must inject inherit = 0 on this arm.)
        Console.WriteLine(Attribute.GetCustomAttribute(typeof(Derived), typeof(Tag)) == null);
        Console.WriteLine(Attribute.GetCustomAttribute(typeof(DerivedTagged), typeof(DerivedTag)) != null);

        // ---- 4. AllowMultiple = false exact-type shadowing ----
        Console.WriteLine(Attribute.GetCustomAttributes(typeof(DerivedShadow), typeof(Tag), true).Length == 1);

        // ---- 5. null element -> ArgumentNullException (net48 element) ----
        try {
            Console.WriteLine(Attribute.GetCustomAttribute((MemberInfo)null, typeof(Tag)) == null);
            Console.WriteLine("no-throw");
        } catch (ArgumentNullException) {
            Console.WriteLine("ane");
        }
    }
}}