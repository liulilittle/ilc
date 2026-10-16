//------------------------------------------------------------------------------
// p-refl2-5ns.cs -- refl2 net48 oracle probe for Type.get_Namespace. The Type
// VALUE is the full-name string; net48 Namespace is the full name with the
// FINAL short-name segment stripped:
//   * "Cases.Top5"          -> "Cases"
//   * "Cases.Sub.Deep"      -> "Cases.Sub"
//   * a GLOBAL-namespace type -> NULL (probe-pinned: net48 returns null, NOT
//     the empty string -- the == comparison targets null below)
// A NESTED type's reflection name is "Outer+Inner"; net48 Namespace keeps the
// namespace of the OUTERMOST type ("Cases.OuterTop+InnerTop" -> "Cases"; a
// nested type of a global type -> null). All records compare via ==.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases { namespace Sub {
    public class Deep { }
} }

public class GlobalRt { }

namespace Cases {
    public class Outer5 { public class Inner5 { } }
}

public static class PRefl2Ns
{
    public static void Main()
    {
        Console.WriteLine("[ns.main.global]=" + (typeof(PRefl2Ns).Namespace == null));
        Console.WriteLine("[ns.nestedcases]=" + (typeof(Cases.OuterTop.InnerTop).Namespace == "Cases"));
        Console.WriteLine("[ns.deep]=" + (typeof(Cases.Sub.Deep).Namespace == "Cases.Sub"));
        Console.WriteLine("[ns.global]=" + (typeof(GlobalRt).Namespace == null));
        Console.WriteLine("[ns.nested]=" + (typeof(Cases.Outer5.Inner5).Namespace == "Cases"));
        Console.WriteLine("[ns.nested.global]=" + (typeof(OuterGlobal5.InnerGlobal5).Namespace == null));
    }
}

public class OuterGlobal5 { public class InnerGlobal5 { } }

namespace Cases {
    public class OuterTop { public class InnerTop { } }
}