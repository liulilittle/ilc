//------------------------------------------------------------------------------
// p-refl2-3assign.cs -- refl2 net48 oracle probe for name-string Type
// IsAssignableFrom / IsInstanceOfType / IsSubclassOf. net48 semantics:
//   IsAssignableFrom(Type c)  -- typeof(D).IsAssignableFrom(typeof(B)) == True
//     (the derived is assignable to the base); identity True; interface impl
//     True (D implements J -> typeof(J).IsAssignableFrom(typeof(D)) True);
//     unrelated False; the source (argument) being null returns FALSE (no throw).
//   IsInstanceOfType(object)  -- typeof(B).IsInstanceOfType(new D()) True;
//     null object False (no throw).
//   IsSubclassOf(Type c)      -- typeof(D).IsSubclassOf(typeof(B)) True;
//     identity (typeof(B).IsSubclassOf(typeof(B))) False (net48: a type is
//     NOT a subclass of itself); interface(arg)/unrelated False; instance
//     (typeof(B).IsSubclassOf(typeof(D))) False.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public interface IJ { }
    public class B3 { }
    public class D3 : B3, IJ { }
    public class X3 { }

    public static class PRefl2Assign
    {
        public static void Main()
        {
            var tb = typeof(B3);
            var td = typeof(D3);
            var tx = typeof(X3);
            var tj = typeof(IJ);
            // IsAssignableFrom
            Console.WriteLine("[assign.base.frm.d]=" + tb.IsAssignableFrom(td));
            Console.WriteLine("[assign.d.frm.base]=" + td.IsAssignableFrom(tb));
            Console.WriteLine("[assign.self]=" + tb.IsAssignableFrom(tb));
            Console.WriteLine("[assign.iface.frm.d]=" + tj.IsAssignableFrom(td));
            Console.WriteLine("[assign.d.frm.iface]=" + td.IsAssignableFrom(tj));
            Console.WriteLine("[assign.unrelated]=" + tb.IsAssignableFrom(tx));
            Console.WriteLine("[assign.null]=" + tb.IsAssignableFrom(null));
            // IsInstanceOfType
            Console.WriteLine("[inst.base.ond]=" + tb.IsInstanceOfType(new D3()));
            Console.WriteLine("[inst.d.onb]=" + td.IsInstanceOfType(new B3()));
            Console.WriteLine("[inst.self]=" + td.IsInstanceOfType(new D3()));
            Console.WriteLine("[inst.iface.ond]=" + tj.IsInstanceOfType(new D3()));
            Console.WriteLine("[inst.unrelated]=" + tb.IsInstanceOfType(new X3()));
            Console.WriteLine("[inst.null]=" + tb.IsInstanceOfType(null));
            // IsSubclassOf
            Console.WriteLine("[sub.d.base]=" + td.IsSubclassOf(tb));
            Console.WriteLine("[sub.base.d]=" + tb.IsSubclassOf(td));
            Console.WriteLine("[sub.self]=" + td.IsSubclassOf(td));
            Console.WriteLine("[sub.d.iface]=" + td.IsSubclassOf(tj));
            Console.WriteLine("[sub.unrelated]=" + td.IsSubclassOf(tx));
            // net48: IsSubclassOf(null) throws ArgumentNullException (unlike
            // IsAssignableFrom(null) which returns False -- the documented
            // per-primitive asymmetry). Probed as a catch bool.
            bool subNullThrew = false;
            try { td.IsSubclassOf(null); } catch (ArgumentNullException) { subNullThrew = true; }
            Console.WriteLine("[sub.null.throws]=" + subNullThrew);
        }
    }
}