//------------------------------------------------------------------------------
// rf-getname.cs -- MemberInfo::get_Name e2e probe (net48 oracle, byte-for-byte).
//
// RF-REFL gap closure: MemberInfo::get_Name on the simplified member model.
//   * a TRACKED Type receiver (typeof(X).Name) -> the short-name fold;
//   * a MethodInfo / FieldInfo / ConstructorInfo VALUE -> the member-name
//     decode (ConstructorInfo -> net48 ".ctor");
//   * an UNTRACKED Type VALUE stored in a local (the typeof-chain get_BaseType
//     fold stores the full-name string but the local loses the "System.Type"
//     hint) -> the generic get_Name arm must RE-RESOLVE so the short-name
//     primitive answers net48's SHORT name (not the phantom generated name).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class GnTarget
    {
        public int F;
        public GnTarget() { }
        public void M() { }
        public int P { get { return 1; } }
    }

    public static class RfGetName
    {
        public static void Main()
        {
            var t = typeof(GnTarget);
            Console.WriteLine("method=" + t.GetMethod("M").Name);
            Console.WriteLine("field=" + t.GetField("F").Name);
            Console.WriteLine("ctor=" + t.GetConstructor(new Type[0]).Name);
            Console.WriteLine("prop=" + t.GetProperty("P").Name);
            Console.WriteLine("self=" + t.Name);
            Console.WriteLine("obj=" + typeof(object).Name);
            // The UNTRACKED Type VALUE receiver: the base-type fold stores the
            // full-name string into a local whose declared type is not tracked.
            Type bt = t.BaseType;
            Console.WriteLine("base=" + bt.Name);
        }
    }
}
