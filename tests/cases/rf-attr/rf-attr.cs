//------------------------------------------------------------------------------
// rf-attr.cs -- RF-N6 e2e probe (net48 oracle, byte-for-byte): MEMBER-level
// GetCustomAttributes(bool inherit) on MethodInfo / FieldInfo / PropertyInfo.
//
// The simplified member model represents a member VALUE as its name string,
// so the receiver of GetCustomAttributes is just the member name; the runtime
// scans the typeinfo registry for the declaring type (unique names in this
// probe), then collects the member's registered attribute objects. inherit
// walks the declaring type's own + base chain collecting the same-named
// member's attributes, most-derived first (net48: an overridden virtual's
// GetCustomAttributes(true) returns the derived + base declarations' attrs).
//
//   VM : AttrDeriv.VM [TagB] overrides AttrBase.VM [TagA] -> inherit=false
//       [TagB], inherit=true [TagB, TagA].
//   F  : inherited field declared on AttrBase [TagB] -> inherit any [TagB]
//       (net48 fields have no inheritance chain -- the probe's base walk finds
//       no same-named field above the declaring type, so both agree).
//   P  : inherited property declared on AttrBase [TagC] -> [TagC].
//   P2 : own property on AttrDeriv [TagA] -> [TagA].
//   F2 : own field on AttrDeriv [TagA] -> [TagA].
// Every attribute object's runtime class name is printed (o.GetType().Name ->
// the short class name, e.g. "TagB").
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    [AttributeUsage(AttributeTargets.All)]
    public class TagA : Attribute { }

    [AttributeUsage(AttributeTargets.All)]
    public class TagB : Attribute { }

    [AttributeUsage(AttributeTargets.All)]
    public class TagC : Attribute { }

    public class AttrBase
    {
        [TagA] public virtual void VM() { }
        [TagB] public int F;
        [TagC] public int P { get { return 1; } set { } }
    }

    public class AttrDeriv : AttrBase
    {
        [TagB] public override void VM() { }
        [TagA] public int F2;
        [TagA] public string P2 { get { return ""; } set { } }
    }

    public static class RfAttr
    {
        private static void Dump(string label, object[] attrs)
        {
            Console.WriteLine(label + "_len=" + attrs.Length);
            foreach (var a in attrs)
                Console.WriteLine(label + "_attr=" + a.GetType().Name);
        }

        public static void Main()
        {
            // ---- MethodInfo: overridden virtual, own + base chain ----
            var vm = typeof(AttrDeriv).GetMethod("VM");
            Dump("vm_false", vm.GetCustomAttributes(false));
            Dump("vm_true", vm.GetCustomAttributes(true));
            // ---- FieldInfo: inherited field (declared on the base) ----
            Dump("f_true", typeof(AttrDeriv).GetField("F").GetCustomAttributes(true));
            // ---- PropertyInfo: inherited property ----
            Dump("p_false", typeof(AttrDeriv).GetProperty("P").GetCustomAttributes(false));
            // ---- own members ----
            Dump("f2_true", typeof(AttrDeriv).GetField("F2").GetCustomAttributes(true));
            Dump("p2_true", typeof(AttrDeriv).GetProperty("P2").GetCustomAttributes(true));
        }
    }
}