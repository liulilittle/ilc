//------------------------------------------------------------------------------
// p-refl4-3aqn.cs -- refl4 item 3 net48 oracle probe: Type.AssemblyQualifiedName
// + Type.get_Assembly + System.Reflection.Assembly op_Equality/op_Inequality.
//
// net48 semantics pinned by the explore-4 report:
//   * AssemblyQualifiedName = "<FullName>, <assemblySimpleName>, Version=1.0.0.0,
//     Culture=neutral, PublicKeyToken=null" (version fixed in the AssemblyDef
//     header, so a FULL string-equality record is byte-stable).
//   * get_Assembly returns a non-null Assembly (the simplified model carries an
//     Assembly VALUE as the assembly SIMPLE-NAME string, like the Type VALUE).
//   * Assembly op_Equality/op_Inequality -- the real operator; two Assembly
//     values from the same (single) assembly are equal (net48 same object).
//     C7: an Assembly VALUE compared to null is False; the pre-refl4 NullMarker
//     fold made `asm == null` True (COMPILE-BROKEN -- the fix here).
//   * typeof(X).Assembly == typeof(Y).Assembly -- both are the same string.
//
// All records are bool records (byte-stable). The AQN record compares the FULL
// string with == (string equality -- wired via icall_string_equals_static).
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class AqnClass { }

    public static class PRefl4Aqn
    {
        public static void Main()
        {
            // ---- Type.AssemblyQualifiedName: FULL string equality ----
            var aqn = typeof(AqnClass).AssemblyQualifiedName;
            Console.WriteLine("[aqn.full]=" +
                (aqn == "Cases.AqnClass, p-refl4-3aqn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));

            // ---- Type.get_Assembly: non-null + same-assembly identity ----
            var asm1 = typeof(AqnClass).Assembly;
            var asm2 = typeof(PRefl4Aqn).Assembly;
            Console.WriteLine("[asmb.nonnull]=" + (asm1 != null));
            Console.WriteLine("[asmb.same]=" + (asm1 == asm2));

            // ---- C7: Assembly op_Equality / op_Inequality incl. null ----
            Console.WriteLine("[asmb.ne]=" + (!(asm1 == null)));
            Console.WriteLine("[asmb.self]=" + (asm1 == asm1));
            Console.WriteLine("[asmb.ineqnull]=" + (asm1 != null));
            Console.WriteLine("[asmb.ineqself]=" + (!(asm1 != asm1)));
        }
    }
}