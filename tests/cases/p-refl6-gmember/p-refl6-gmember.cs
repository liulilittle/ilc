//------------------------------------------------------------------------------
// p-refl6-gmember.cs -- refl6 item B net48 oracle probe: Type.GetMember(string)
// + GetMember(string, BindingFlags). net48 semantics pinned here (all
// probe-verified on the live oracle):
//   * GetMember(name) default binding is Public|Instance|Static: a PUBLIC
//     member (method / field / property / event / accessor) matches, a
//     PRIVATE member does NOT ([gmn.throw] aside, FPriv needs NonPublic);
//   * inherited members are returned with their BASE declaring type
//     ([gmi.decl]=M6Base), metadata/own-then-base order;
//   * name matching is by SIMPLE member name -- accessors hit by their
//     method name ("get_PP" -> a Method member);
//   * "" returns an EMPTY array (no throw): [gme.throw]=NOnothing cnt=0;
//   * null returns ArgumentNullException ([gmn.throw]);
//   * member kinds print as the net48 MemberTypes value (Method=8,
//     Property=16, Event=2) or the MemberTypes NAME for the accessor
//     record ([gmacc.kind]=Method -- deterministic from the numeric).
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class M6Base
    {
        public void MInh() { }          // inherited method (declared in base)
        public void MPub() { }          // inherited method (the gm/gms target)
    }

    public class M6 : M6Base
    {
        public int FPub;                // public field
        private int FPriv;              // private field (NonPublic|Instance)
        public int PP { get; set; }     // property (get_PP / set_PP accessors)
        public int PB { get; set; }     // second property
        public event EventHandler EvPub; // event
    }

    public static class PRefl6Gmember
    {
        // Deterministic MemberTypes name from the NUMERIC value (byte-stable
        // on both sides; MemberTypes.Method == 8).
        private static string KindName(MemberInfo m)
        {
            return (int)m.MemberType == (int)MemberTypes.Method ? "Method"
                : (int)m.MemberType == (int)MemberTypes.Property ? "Property"
                : (int)m.MemberType == (int)MemberTypes.Event ? "Event"
                : (int)m.MemberType == (int)MemberTypes.Field ? "Field" : "?";
        }

        public static void Main()
        {
            var t = typeof(M6);

            // ---- GetMember(string) default binding (Public|Instance|Static) ----
            var gm = t.GetMember("MPub");
            Console.WriteLine("[gm.cnt]=" + gm.Length);
            Console.WriteLine("[gm.kind]=" + (int)gm[0].MemberType);

            var gm2 = t.GetMember("FPub");
            Console.WriteLine("[gm2.cnt]=" + gm2.Length);
            Console.WriteLine("[gm2.name]=" + gm2[0].Name);

            var gm3 = t.GetMember("PP");
            Console.WriteLine("[gm3.cnt]=" + gm3.Length);
            Console.WriteLine("[gm3.kind]=" + (int)gm3[0].MemberType);

            // ---- inherited member -> declaring base type ----
            var gmi = t.GetMember("MInh");
            Console.WriteLine("[gmi.cnt]=" + gmi.Length);
            Console.WriteLine("[gmi.decl]=" + gmi[0].DeclaringType.Name);

            // ---- not found -> empty ----
            Console.WriteLine("[gmn.cnt]=" + t.GetMember("NoSuchMember").Length);

            // ---- empty name -> empty array, NO throw ----
            Console.WriteLine("[gme.throw]=NOnothing cnt=" + t.GetMember("").Length);

            // ---- null name -> ArgumentNullException ----
            try
            {
                t.GetMember((string)null);
                Console.WriteLine("[gmn.throw]=none");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[gmn.throw]=" + ex.GetType().Name);
            }

            // ---- NonPublic|Instance finds the private field ----
            var gmpr = t.GetMember("FPriv", BindingFlags.NonPublic | BindingFlags.Instance);
            Console.WriteLine("[gmpr.cnt]=" + gmpr.Length);
            Console.WriteLine("[gmpr.name]=" + gmpr[0].Name);

            // ---- explicit Public|Instance|Static ----
            var gms = t.GetMember("MPub", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Console.WriteLine("[gms.cnt]=" + gms.Length);

            // ---- accessors surface as methods ----
            var gmacc = t.GetMember("get_PP");
            Console.WriteLine("[gmacc.cnt]=" + gmacc.Length);
            Console.WriteLine("[gmacc.kind]=" + KindName(gmacc[0]));

            Console.WriteLine("[gmacc2.cnt]=" + t.GetMember("set_PP").Length);

            // ---- event ----
            var gmev = t.GetMember("EvPub");
            Console.WriteLine("[gmev.cnt]=" + gmev.Length);
            Console.WriteLine("[gmev.kind]=" + (int)gmev[0].MemberType);

            // ---- second property ----
            var gm3b = t.GetMember("PB");
            Console.WriteLine("[gm3b.cnt]=" + gm3b.Length);
            Console.WriteLine("[gm3b.kind]=" + (int)gm3b[0].MemberType);
        }
    }
}