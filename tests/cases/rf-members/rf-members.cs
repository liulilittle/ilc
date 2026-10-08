//------------------------------------------------------------------------------
// rf-members.cs -- RF-N5 e2e probe (net48 oracle, byte-for-byte):
//
//   GetNestedTypes() -- TypeInfo nested sub-table enumeration. The nested
//   types are expanded to independent top-level TypeDefs
//   (TypeEmitter.ExpandNestedTypes) keyed in the typeinfo registry by the
//   net48 '+' reflection name ("Cases.Outer+Inner"); GetNestedTypes returns
//   those FULL names in metadata declaration order, own only (net48: a
//   derived type does NOT inherit nested types).
//
//   GetMembers() -- the five-enumeration union (methods + fields +
//   properties + events + constructors, own + base chain, public methods /
//   fields AND constructors as the doctored simplification, properties and
//   events all). Accessor methods (get_/set_/add_/remove_/op_*) are a
//   documented DIFFERENCE from net48 (which folds them into their owning
//   property/event members), so the probe's oracle filter drops the object
//   virtuals (ToString/GetHashCode/Equals/GetType -- inherited from
//   System.Object, which the typeinfo chain never models) and the probe
//   relies on net48 GetMembers' own member-kinds + declaration order to pin
//   the union order.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class Outer
    {
        public class Inner
        {
            public int X;
        }
        public class Inner2
        {
        }
    }

    public class MembersBase
    {
        public int BaseField;
        public void BaseMethod() { }
        public int BaseProp { get { return 1; } set { } }
    }

    public class MembersDerived : MembersBase
    {
        public MembersDerived() { }
        public int Field1;
        public void Method1() { }
        public string Prop1 { get { return ""; } set { } }
        public event EventHandler Evt1;
    }

    public static class RfMembers
    {
        private static bool SkipObjectMember(string n)
        {
            return n == "ToString" || n == "GetHashCode" || n == "Equals" || n == "GetType";
        }

        public static void Main()
        {
            // ---- GetNestedTypes: own nested types, metadata order ----
            var nts = typeof(Outer).GetNestedTypes();
            foreach (var nt in nts)
                Console.WriteLine("nested=" + nt.FullName);
            Console.WriteLine("nested_count=" + nts.Length);

            // ---- GetMembers: five-enumeration union, own + base chain ----
            // The member_count line counts the FILTERED view (net48's raw
            // GetMembers includes the inherited System.Object virtuals
            // ToString/GetHashCode/Equals/GetType, which the typeinfo chain
            // never models -- the SkipObjectMember filter drops the same four
            // names on both sides so the printed set and count agree).
            int n = 0;
            foreach (var m in typeof(MembersDerived).GetMembers())
            {
                if (SkipObjectMember(m.Name)) continue;
                n++;
                Console.WriteLine("member=" + m.Name);
            }
            Console.WriteLine("member_count=" + n);
        }
    }
}