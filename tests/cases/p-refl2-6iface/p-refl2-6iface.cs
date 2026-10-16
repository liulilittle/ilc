//------------------------------------------------------------------------------
// p-refl2-6iface.cs -- refl2 net48 oracle probe for Type.GetInterface(string)
// / (string, bool). net48 matches the FULL or SIMPLE interface name across the
// type's own + base chain; ignoreCase=true uses ordinal-ignore-case compare.
// null -> null (not found); absent -> null. The RESULT of a successful query
// is a Type VALUE; the net48 records compare its Name / identity.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public interface IBase6 { }
    public interface IDerived6 : IBase6 { }
    public class Root6 { }
    public class Mid6 : Root6, IDerived6 { }
    public class Leaf6 : Mid6 { }
    public class Unrelated6 { }

    public static class PRefl2Iface
    {
        public static void Main()
        {
            var t = typeof(Leaf6);
            // Full name (own + inherited: IDerived6 is on Mid6's table).
            Console.WriteLine("[iface.full.derived]=" + (t.GetInterface("Cases.IDerived6") != null));
            Console.WriteLine("[iface.full.base]=" + (t.GetInterface("Cases.IBase6") != null));
            // Simple name.
            Console.WriteLine("[iface.simple.derived]=" + (t.GetInterface("IDerived6") != null));
            // Absent / unrelated.
            Console.WriteLine("[iface.missing]=" + (t.GetInterface("Nope") == null));
            Console.WriteLine("[iface.unrelated]=" + (typeof(Unrelated6).GetInterface("Cases.IDerived6") == null));
            // ignoreCase variant.
            Console.WriteLine("[iface.ignore.false]=" + (t.GetInterface("cAsEs.IDerived6", false) == null));
            Console.WriteLine("[iface.ignore.true]=" + (t.GetInterface("cAsEs.IDerived6", true) != null));
            Console.WriteLine("[iface.simple.ignore.true]=" + (t.GetInterface("iDeRiVeD6", true) != null));
            Console.WriteLine("[iface.simple.ignore.false]=" + (t.GetInterface("iDeRiVeD6", false) == null));
            // The result is a Type value: its Name (short) === "IDerived6".
            Console.WriteLine("[iface.name]=" + (t.GetInterface("Cases.IDerived6").Name == "IDerived6"));
        }
    }
}