//------------------------------------------------------------------------------
// p-refl4-1mt.cs -- refl4 item 1 net48 oracle probe: MemberInfo.get_MemberType
// + MemberInfo.get_DeclaringType (including the C5 array-ELEMENT forms --
// GetMethods()[i].MemberType / GetProperties()[i].DeclaringType, i.e. the
// member-name-string receivers the enumeration icalls produce).
//
// The simplified member model represents a MemberInfo VALUE as the member-NAME
// string; the runtime classifies the kind by scanning the typeinfo member
// tables. This probe pins the net48 MemberTypes values + DeclaringType rules
// the new icalls must reproduce:
//   * get_MemberType on method/field/property/event/ctor/TypeInfo/nested-type
//     receivers (net48 MemberTypes: Method=8 Field=4 Property=16 Constructor=1
//     Event=2 TypeInfo=32 NestedType=128). Probes print the NUMERIC value via
//     (int) cast -- byte-stable, no enum-name stringification needed.
//   * get_DeclaringType on own/inherited/override/nested receivers (net48:
//     own -> itself, inherited -> base, override -> most derived, nested type
//     -> its outer type, non-nested type receiver -> null).
//   * array-element coverage (C5): GetMethods()[i] / GetProperties()[i] /
//     GetFields()[i] / GetEvents()[i] / GetConstructors()[i] carry the NAME
//     string; MemberType on the element must answer by kind.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class MTBase
    {
        public int FieldB;
        public string PropB { get; set; }
        public event EventHandler EvtB;
        public void MethB() { }
        public virtual void OverB() { }
    }

    public class MTDerived : MTBase
    {
        public int FieldD;
        public int PropD { get; set; }
        public event EventHandler EvtD;
        public void MethD() { }
        public override void OverB() { }
    }

    public class OuterM
    {
        public class InnerM { }
    }

    public class MTPlain { public int X; }

    public class MTStatic
    {
        public static string SField;
    }

    public static class PRefl4Mt
    {
        public static void Main()
        {
            var t = typeof(MTDerived);

            // ---- get_MemberType: numeric MemberTypes on each kind ----
            Console.WriteLine("[mt.method]=" + (int)t.GetMethod("MethD").MemberType);
            Console.WriteLine("[mt.field]=" + (int)t.GetField("FieldD").MemberType);
            Console.WriteLine("[mt.prop]=" + (int)t.GetProperty("PropD").MemberType);
            Console.WriteLine("[mt.event]=" + (int)t.GetEvent("EvtD").MemberType);
            Console.WriteLine("[mt.ctor]=" + (int)t.GetConstructor(new Type[0]).MemberType);
            Console.WriteLine("[mt.nested]=" + (int)typeof(OuterM.InnerM).MemberType);
            Console.WriteLine("[mt.typeinfo]=" + (int)typeof(MTDerived).MemberType);

            // ---- C5 array-ELEMENT MemberType (name-string receivers) ----
            // Each enumeration element is the member-NAME string; MemberType
            // on the element must classify by kind. Printed per match (a
            // name-checked element is byte-stable regardless of enumeration
            // order/count -- net48 and the runtime may differ on the OTHER
            // elements, only the named element is compared).
            foreach (var m in t.GetMethods())
                if (m.Name == "MethD")
                    Console.WriteLine("[c5.method]=" + (int)m.MemberType);
            foreach (var f in typeof(MTPlain).GetFields())
                if (f.Name == "X")
                    Console.WriteLine("[c5.field]=" + (int)f.MemberType);
            foreach (var p in typeof(MTDerived).GetProperties())
            {
                if (p.Name == "PropD") Console.WriteLine("[c5.prop]=" + (int)p.MemberType);
                if (p.Name == "PropB") Console.WriteLine("[c5.propinh]=" + (int)p.MemberType);
            }
            foreach (var e in t.GetEvents())
                if (e.Name == "EvtD")
                    Console.WriteLine("[c5.event]=" + (int)e.MemberType);
            foreach (var c in typeof(MTPlain).GetConstructors())
                Console.WriteLine("[c5.ctor]=" + (int)c.MemberType);

            // ---- get_DeclaringType: own / inherited / override / nested ----
            Console.WriteLine("[dt.own.f]=" + (t.GetField("FieldD").DeclaringType == typeof(MTDerived)));
            Console.WriteLine("[dt.inh.f]=" + (t.GetField("FieldB").DeclaringType == typeof(MTBase)));
            Console.WriteLine("[dt.own.m]=" + (t.GetMethod("MethD").DeclaringType == typeof(MTDerived)));
            Console.WriteLine("[dt.inh.m]=" + (t.GetMethod("MethB").DeclaringType == typeof(MTBase)));
            Console.WriteLine("[dt.ovr.m]=" + (t.GetMethod("OverB").DeclaringType == typeof(MTDerived)));
            Console.WriteLine("[dt.own.p]=" + (t.GetProperty("PropD").DeclaringType == typeof(MTDerived)));
            Console.WriteLine("[dt.inh.p]=" + (t.GetProperty("PropB").DeclaringType == typeof(MTBase)));
            Console.WriteLine("[dt.own.e]=" + (t.GetEvent("EvtD").DeclaringType == typeof(MTDerived)));
            Console.WriteLine("[dt.inh.e]=" + (t.GetEvent("EvtB").DeclaringType == typeof(MTBase)));
            Console.WriteLine("[dt.nested]=" + (typeof(OuterM.InnerM).DeclaringType == typeof(OuterM)));
            Console.WriteLine("[dt.plainnull]=" + (typeof(MTPlain).DeclaringType == null));
            Console.WriteLine("[dt.ctor]=" + (t.GetConstructor(new Type[0]).DeclaringType == typeof(MTDerived)));
            Console.WriteLine("[dt.statfield]=" + (typeof(MTStatic).GetField("SField").DeclaringType == typeof(MTStatic)));
        }
    }
}