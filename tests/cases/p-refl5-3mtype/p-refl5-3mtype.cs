//------------------------------------------------------------------------------
// p-refl5-3mtype.cs -- refl5 item 3 net48 oracle probe: the C2 member-type
// property family + the flag-derived MethodBase/FieldInfo predicates:
//   * FieldInfo.get_FieldType / PropertyInfo.get_PropertyType /
//     MethodInfo.get_ReturnType / EventInfo.get_EventHandlerType -> a TYPE
//     VALUE (the full-name string): printed via .FullName (the Type-domain
//     identity fold on a full-name receiver) and via == typeof(...) bools.
//   * MemberInfo.get_ReflectedType -- the type the member was obtained from =
//     the DECLARING type here (own members only, W7: the member name string
//     carries no query-type).
//   * FieldInfo.get_IsAssembly -- net48 False for a PUBLIC field (the narrow
//     model cannot distinguish private from internal -- W7: only the
//     public-field False case is pinned).
//   * MethodBase.get_IsStatic / get_IsPublic / get_IsConstructor -- flag-bit
//     decode, including the disambiguated ctor VALUE "<FullName>::.ctor"
//     (GetConstructor receiver) -> IsConstructor True.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class CBT
    {
        public CBT() { }
        public int F;
        public static int SF;
        public int P { get; set; }
        public void VM() { }
        public int RM() { return 0; }
        public static int SM() { return 0; }
        public event EventHandler Ev;
    }

    public static class PRefl5Mtype
    {
        public static void Main()
        {
            var t = typeof(CBT);

            Console.WriteLine("[c.f.ft]=" + t.GetField("F").FieldType.FullName);
            Console.WriteLine("[c.f.ftp]=" + (t.GetField("F").FieldType == typeof(int)));
            Console.WriteLine("[c.p.pt]=" + t.GetProperty("P").PropertyType.FullName);
            Console.WriteLine("[c.p.ptp]=" + (t.GetProperty("P").PropertyType == typeof(string)));
            Console.WriteLine("[c.m.rt]=" + t.GetMethod("VM").ReturnType.FullName);
            Console.WriteLine("[c.m.rtp]=" + (t.GetMethod("VM").ReturnType == typeof(void)));
            Console.WriteLine("[c.m.rt2]=" + (t.GetMethod("RM").ReturnType == typeof(int)));

            Console.WriteLine("[c.e.eht]=" + t.GetEvent("Ev").EventHandlerType.FullName);
            Console.WriteLine("[c.e.ehtp]=" + (t.GetEvent("Ev").EventHandlerType == typeof(EventHandler)));

            Console.WriteLine("[c.f.reflected]=" + (t.GetField("F").ReflectedType == typeof(CBT)));
            Console.WriteLine("[c.m.reflected]=" + (t.GetMethod("VM").ReflectedType == typeof(CBT)));
            Console.WriteLine("[c.p.reflected]=" + (t.GetProperty("P").ReflectedType == typeof(CBT)));

            Console.WriteLine("[c.f.iass]=" + t.GetField("F").IsAssembly);
            Console.WriteLine("[c.sf.iass]=" + t.GetField("SF").IsAssembly);

            Console.WriteLine("[c.m.isstatic]=" + t.GetMethod("RM").IsStatic);
            Console.WriteLine("[c.sm.isstatic]=" + t.GetMethod("SM").IsStatic);
            Console.WriteLine("[c.sf.isstatic]=" + t.GetField("SF").IsStatic);
            Console.WriteLine("[c.m.ispub]=" + t.GetMethod("RM").IsPublic);
            Console.WriteLine("[c.sm.ispub]=" + t.GetMethod("SM").IsPublic);

            Console.WriteLine("[c.m.isctor]=" + t.GetMethod("RM").IsConstructor);
            Console.WriteLine("[c.ci.isctor]=" + t.GetConstructor(new Type[0]).IsConstructor);
        }
    }
}