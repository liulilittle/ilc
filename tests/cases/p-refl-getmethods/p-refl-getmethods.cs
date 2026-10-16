//------------------------------------------------------------------------------
// p-refl-getmethods.cs -- Type.GetMethods() ctor-exclusion probe (GAP-4 /
// P1). net48 GetMethods() returns the type's PUBLIC NON-CONSTRUCTOR methods:
// ".ctor"/".cctor" are EXCLUDED (they surface only through
// GetConstructors()). The runtime's simplified PublicMethod filter previously
// enumerated ctors too (the R2 "includes ctors" documented deviation); this
// probe pins the net48 exclusion. Every record is a containment/absence bool
// over the returned member-name set, byte-stable (rf-members foreach shape).
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class Gm
    {
        public Gm() { }                       // .ctor (must NOT be in GetMethods)
        public int A() { return 1; }
        public void B() { }
        static Gm() { }                       // .cctor (must NOT be in GetMethods)
    }

    public static class PReflGetmethods
    {
        // The typeinfo chain never models the inherited System.Object virtuals
        // (ToString/GetHashCode/Equals/GetType work through the BCL, not the
        // reflected method table) -- rf-members' SkipObjectMember convention:
        // both sides drop the same four names so the printed set/count agree.
        private static bool SkipObjectMember(string name)
        {
            return name == "ToString" || name == "GetHashCode"
                || name == "Equals" || name == "GetType";
        }

        public static void Main()
        {
            bool hasA = false, hasB = false, noCtor = true, noCctor = true;
            int count = 0;
            foreach (var m in typeof(Gm).GetMethods())
            {
                if (SkipObjectMember(m.Name)) continue;
                count++;
                if (m.Name == "A") hasA = true;
                if (m.Name == "B") hasB = true;
                if (m.Name == ".ctor") noCtor = false;
                if (m.Name == ".cctor") noCctor = false;
            }
            Console.WriteLine("[has.A]=" + hasA);
            Console.WriteLine("[has.B]=" + hasB);
            Console.WriteLine("[no.ctor]=" + noCtor);
            Console.WriteLine("[no.cctor]=" + noCctor);
            Console.WriteLine("[count]=" + count);
        }
    }
}