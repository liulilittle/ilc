//------------------------------------------------------------------------------
// rf-attr-filter.cs -- RF-N6 e2e probe (net48 oracle, byte-for-byte): the
// MEMBER-level GetCustomAttributes(Type attributeType, bool inherit) filter
// (il2cpp_attribute_member_get_custom_named).
//
// Same simplified member model / declaring-type scan as rf-attr.cs: the
// receiver is the member-name string; attributeType rides as a managed-string
// full name; the runtime applies the attributeType.IsAssignableFrom filter
// (an attribute matches when its runtime class equals or derives from the
// filter type) on top of the own+base-chain walk.
//
//   VM : AttrDeriv.VM [TagB] overrides AttrBase.VM [TagA]
//   F  : inherited field declared on AttrBase [TagB]
//   P  : inherited property declared on AttrBase [TagC]
//   F2 : own field on AttrDeriv [TagA]
//   P2 : own property on AttrDeriv [TagA]
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

    public static class RfAttrFilter
    {
        private static void Dump(string label, object[] attrs)
        {
            Console.WriteLine(label + "_len=" + attrs.Length);
            foreach (var a in attrs)
                Console.WriteLine(label + "_attr=" + a.GetType().Name);
        }

        public static void Main()
        {
            // MethodInfo VM: own [TagB], base [TagA].
            Dump("vm_tagb_false", typeof(AttrDeriv).GetMethod("VM").GetCustomAttributes(typeof(TagB), false));
            Dump("vm_tagb_true", typeof(AttrDeriv).GetMethod("VM").GetCustomAttributes(typeof(TagB), true));
            Dump("vm_taga_false", typeof(AttrDeriv).GetMethod("VM").GetCustomAttributes(typeof(TagA), false));
            Dump("vm_taga_true", typeof(AttrDeriv).GetMethod("VM").GetCustomAttributes(typeof(TagA), true));
            // Field F [TagB].
            Dump("f_tagb", typeof(AttrDeriv).GetField("F").GetCustomAttributes(typeof(TagB), true));
            Dump("f_taga", typeof(AttrDeriv).GetField("F").GetCustomAttributes(typeof(TagA), true));
            // Property P [TagC].
            Dump("p_tagc", typeof(AttrDeriv).GetProperty("P").GetCustomAttributes(typeof(TagC), false));
            Dump("p_taga", typeof(AttrDeriv).GetProperty("P").GetCustomAttributes(typeof(TagA), false));
            // Own members.
            Dump("f2_taga", typeof(AttrDeriv).GetField("F2").GetCustomAttributes(typeof(TagA), true));
            Dump("f2_tagb", typeof(AttrDeriv).GetField("F2").GetCustomAttributes(typeof(TagB), true));
            Dump("p2_taga", typeof(AttrDeriv).GetProperty("P2").GetCustomAttributes(typeof(TagA), false));
        }
    }
}
