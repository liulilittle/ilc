//------------------------------------------------------------------------------
// p-refl5-1flags.cs -- refl5 item 1 net48 oracle probe: the (BindingFlags)
// arity-1 member enumeration overloads (GetMethods/GetFields/GetProperties/
// GetEvents/GetConstructors/GetMembers). net48 semantics pinned here (all
// probe-verified on the live oracle):
//   * visibility: a PUBLIC member is listed ONLY when the Public (16) bit is
//     set; a NONPUBLIC member ONLY when the NonPublic (32) bit is set
//     ("NonPublic excludes public");
//   * static/instance: Instance (4) matches non-static members, Static (8)
//     matches static -- WITH NEITHER staticness bit set NOTHING matches
//     (GetMethods(Public) without Instance/Static is EMPTY); INHERITED static
//     members are never listed (own statics only, no FlattenHierarchy);
//   * base-chain: public + protected + internal base members are inherited,
//     PRIVATE base members are never listed, even under NonPublic;
//   * ordering = metadata declaration order for the own table, then the base
//     chain (the existing ()-overload chain order -- rf-members convention);
//   * GetMethods NEVER lists ctors (GAP-4), any flags combo; GetConstructors
//     lists ONLY the queried type's OWN ctors (no inherited ctor), including
//     a private static .cctor under NonPublic|Static.
// SkipObjectMember / SkipBackingField drop the names the typeinfo chain never
// models (System.Object virtuals, auto-property backing fields) on BOTH sides.
// Every loop iterates the ENUMERATION directly (not a MemberInfo[] helper --
// the array-element receiver of a reflected-typed array is not a member-name
// string in this model); the member-name receivers that reach get_Name are the
// "System.String"-tracked enumeration elements the rf-members convention uses.
// Byte-stable records: one joined name-list + one filtered count per combo.
//------------------------------------------------------------------------------
using System;
using System.Reflection;
using System.Text;

namespace Cases
{
    public class FBase
    {
        public static int FBasePubS;
        public int FBasePub;
        internal int FBaseInt;
        protected int FBaseProt;
        private static int FBasePrivS;
        private int FBasePriv;
        public static void MBaseS() { }
        public void MBase() { }
        internal void MBaseInt() { }
        protected void MBaseProt() { }
        private static void MBasePrivS() { }
        private void MBasePriv() { }
        public int PBase { get; set; }
        private int PBasePriv { get; set; }
    }

    public class FDerived : FBase
    {
        public static int FDerPubS;
        public int FDerPub;
        private static int FDerPrivS;
        private int FDerPriv;
        public static void MDerS() { }
        public void MDer() { }
        private static void MDerPrivS() { }
        private void MDerPriv() { }
        public int PDer { get; set; }
        private int PDerPriv { get; set; }
        public event EventHandler EvPub;
    }

    public class FPrivCtor
    {
        private FPrivCtor() { }
        static FPrivCtor() { }
        public void MPub() { }
        private void MPriv() { }
        public static void MPubS() { }
        private static void MPrivS() { }
    }

    public static class PRefl5Flags
    {
        // The typeinfo chain never models the inherited System.Object virtuals
        // (public ToString/GetHashCode/Equals/GetType + nonpublic Finalize/
        // MemberwiseClone) nor the auto-property backing fields -- both sides
        // drop the same names so the printed set/count agree (rf-members).
        private static bool SkipObj(string n)
        {
            return n == "ToString" || n == "GetHashCode" || n == "Equals"
                || n == "GetType" || n == "Finalize" || n == "MemberwiseClone";
        }

        private static bool SkipBack(string n) { return n.EndsWith("k__BackingField"); }

        public static void Main()
        {
            var t = typeof(FDerived);
            int pi = (int)(BindingFlags.Public | BindingFlags.Instance);
            int npi = (int)(BindingFlags.NonPublic | BindingFlags.Instance);
            int ps = (int)(BindingFlags.Public | BindingFlags.Static);
            int nps = (int)(BindingFlags.NonPublic | BindingFlags.Static);
            int all = (int)(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static);

            var sb = new StringBuilder();
            int cnt = 0;
            bool first = true;

            // ---- methods ----
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMethods((BindingFlags)pi)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[m.pi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMethods((BindingFlags)npi)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[m.npi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMethods((BindingFlags)ps)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[m.ps]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMethods((BindingFlags)nps)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[m.nps]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMethods((BindingFlags)(pi | ps))) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[m.pips]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMethods((BindingFlags)all)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[m.all]=" + sb.ToString() + "|" + cnt);

            // ---- fields ----
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetFields((BindingFlags)pi)) { if (SkipBack(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[f.pi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetFields((BindingFlags)npi)) { if (SkipBack(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[f.npi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetFields((BindingFlags)ps)) { if (SkipBack(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[f.ps]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetFields((BindingFlags)nps)) { if (SkipBack(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[f.nps]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetFields((BindingFlags)all)) { if (SkipBack(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[f.all]=" + sb.ToString() + "|" + cnt);

            // ---- properties ----
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetProperties((BindingFlags)pi)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[p.pi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetProperties((BindingFlags)npi)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[p.npi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetProperties((BindingFlags)ps)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[p.ps]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetProperties((BindingFlags)all)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[p.all]=" + sb.ToString() + "|" + cnt);

            // ---- events ----
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetEvents((BindingFlags)pi)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.pi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetEvents((BindingFlags)npi)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.npi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetEvents((BindingFlags)all)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.all]=" + sb.ToString() + "|" + cnt);

            // ---- constructors ----
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetConstructors((BindingFlags)pi)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[c.pi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetConstructors((BindingFlags)npi)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[c.npi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetConstructors((BindingFlags)ps)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[c.ps]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetConstructors((BindingFlags)nps)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[c.nps]=" + sb.ToString() + "|" + cnt);

            // ---- GetMembers ----
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMembers((BindingFlags)pi)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[mbr.pi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMembers((BindingFlags)npi)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[mbr.npi]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMembers((BindingFlags)ps)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[mbr.ps]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t.GetMembers((BindingFlags)all)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[mbr.all]=" + sb.ToString() + "|" + cnt);

            // -- edge behaviors (no Instance/Static bits; private/static ctors) --
            var t2 = typeof(FPrivCtor);
            int pubonly2 = (int)BindingFlags.Public;
            int nopubonly = (int)BindingFlags.NonPublic;
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetMethods((BindingFlags)pubonly2)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.m.pub]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetMethods((BindingFlags)nopubonly)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.m.nopub]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetMethods((BindingFlags)((int)BindingFlags.Public | (int)BindingFlags.Static))) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.m.pubs]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetFields((BindingFlags)pubonly2)) { if (SkipBack(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.f.pub]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetProperties((BindingFlags)pubonly2)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.p.pub]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetConstructors((BindingFlags)nopubonly)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.c.np]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetConstructors((BindingFlags)(nopubonly | pi))) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.c.npis]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetConstructors((BindingFlags)(nopubonly | ps))) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.c.nps]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetConstructors((BindingFlags)pubonly2)) { if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.c.pub]=" + sb.ToString() + "|" + cnt);
            cnt = 0; first = true; sb = new StringBuilder();
            foreach (var m in t2.GetMembers((BindingFlags)pubonly2)) { if (SkipObj(m.Name)) continue; if (!first) sb.Append(','); sb.Append(m.Name); first = false; cnt++; }
            Console.WriteLine("[e.mbr.pub]=" + sb.ToString() + "|" + cnt);
        }
    }
}