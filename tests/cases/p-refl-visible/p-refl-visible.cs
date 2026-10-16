//------------------------------------------------------------------------------
// p-refl-visible.cs -- net48 oracle probe for the SINGLE-MEMBER visibility
// filters (GAP-1 / P0). net48 default binding for GetMethod(name) /
// GetProperty(name) / GetField(name) is Public|Instance|Static -- a PRIVATE
// member is INVISIBLE (returns null), while an EXPLICIT BindingFlags
// NonPublic|Instance query finds it. The typeinfo runtime previously returned
// private methods/properties for the no-flags name queries (the pre-P0 off-
// by-one: get_field already filtered flags&1, get_method/get_property did
// NOT). Every record is a shape decision (non-null vs null) -> stable bool.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class VisPair
    {
        public int PublicPub;            // public field
        private int PrivatePriv;         // private field
        public int PublicProp { get; set; }
        private int PrivateProp { get; set; }
        public void PublicMethod() { }
        private void PrivateMethod() { }
        public static int StaticPub;
        private static int StaticPriv;
        public static void StaticPubMethod() { }
    }

    public static class PReflVisible
    {
        public static void Main()
        {
            var t = typeof(VisPair);

            Console.WriteLine("[pub.field]=" + (t.GetField("PublicPub") != null));
            Console.WriteLine("[priv.field.none]=" + (t.GetField("PrivatePriv") == null));
            Console.WriteLine("[priv.field.np]=" + (t.GetField("PrivatePriv", BindingFlags.NonPublic | BindingFlags.Instance) != null));
            Console.WriteLine("[pub.prop]=" + (t.GetProperty("PublicProp") != null));
            Console.WriteLine("[priv.prop.none]=" + (t.GetProperty("PrivateProp") == null));
            Console.WriteLine("[priv.prop.np]=" + (t.GetProperty("PrivateProp", BindingFlags.NonPublic | BindingFlags.Instance) != null));
            Console.WriteLine("[pub.method]=" + (t.GetMethod("PublicMethod") != null));
            Console.WriteLine("[priv.method.none]=" + (t.GetMethod("PrivateMethod") == null));
            Console.WriteLine("[priv.method.np]=" + (t.GetMethod("PrivateMethod", BindingFlags.NonPublic | BindingFlags.Instance) != null));
            // Static public field/method still visible under the default binding.
            Console.WriteLine("[static.pub.field]=" + (t.GetField("StaticPub") != null));
            Console.WriteLine("[static.pub.method]=" + (t.GetMethod("StaticPubMethod") != null));
            Console.WriteLine("[static.priv.none]=" + (t.GetField("StaticPriv") == null));
            Console.WriteLine("[static.priv.np]=" + (t.GetField("StaticPriv", BindingFlags.NonPublic | BindingFlags.Static) != null));
        }
    }
}