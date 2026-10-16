//------------------------------------------------------------------------------
// p-refl3-2params.cs -- refl3 item 2 net48 oracle probe: MethodBase.
// GetParameters() + the ParameterInfo property surface (Name / ParameterType /
// Position / IsOut). The simplified member model represents a MethodInfo VALUE
// as its method-name string; GetParameters decodes the reflected method's
// parameter table into a ParameterInfo[] whose ELEMENTS carry the per-parameter
// data. Every record is a boolean / small-int / short-name comparison record,
// byte-stable across the net48 oracle and the transpiled runtime.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class PairParams
    {
        public PairParams(int a, string b) { }
        public void Mix(int a, string b) { }
        public void Refs(ref int x, out string y, bool z) { y = ""; }
        public void Many(int q1, long q2, double q3, int[] q4, object q5) { }
        public void None() { }
    }

    public static class PRefl3Params
    {
        public static void Main()
        {
            // ---- GetParameters length arities ----
            Console.WriteLine("[gp.ctor.n]=" + typeof(PairParams).GetConstructor(new Type[] { typeof(int), typeof(string) }).GetParameters().Length);
            Console.WriteLine("[gp.mix.n]=" + typeof(PairParams).GetMethod("Mix").GetParameters().Length);
            Console.WriteLine("[gp.refs.n]=" + typeof(PairParams).GetMethod("Refs").GetParameters().Length);
            Console.WriteLine("[gp.many.n]=" + typeof(PairParams).GetMethod("Many").GetParameters().Length);
            Console.WriteLine("[gp.none.n]=" + typeof(PairParams).GetMethod("None").GetParameters().Length);

            // ---- ParameterInfo.Name / Position / IsOut on Mix(a, b) ----
            var mix = typeof(PairParams).GetMethod("Mix").GetParameters();
            Console.WriteLine("[mix.p0.name]=" + (mix[0].Name == "a"));
            Console.WriteLine("[mix.p0.pos]=" + (mix[0].Position == 0));
            Console.WriteLine("[mix.p0.out]=" + mix[0].IsOut);
            Console.WriteLine("[mix.p1.name]=" + (mix[1].Name == "b"));
            Console.WriteLine("[mix.p1.pos]=" + (mix[1].Position == 1));
            Console.WriteLine("[mix.p1.out]=" + mix[1].IsOut);
            Console.WriteLine("[mix.p1.type.isstr]=" + (mix[1].ParameterType == typeof(string)));
            Console.WriteLine("[mix.p1.type.str]=" + mix[1].ParameterType.Name);

            // ---- Refs(ref int x, out string y, bool z): IsOut flags ----
            var refs = typeof(PairParams).GetMethod("Refs").GetParameters();
            Console.WriteLine("[refs.p0.out]=" + refs[0].IsOut);
            Console.WriteLine("[refs.p0.name]=" + (refs[0].Name == "x"));
            Console.WriteLine("[refs.p1.out]=" + refs[1].IsOut);
            Console.WriteLine("[refs.p1.name]=" + (refs[1].Name == "y"));
            Console.WriteLine("[refs.p2.out]=" + refs[2].IsOut);
            Console.WriteLine("[refs.p2.name]=" + (refs[2].Name == "z"));

            // ---- Many(q1..q5): types + positions, parameter ordering ----
            var many = typeof(PairParams).GetMethod("Many").GetParameters();
            Console.WriteLine("[many.p0.type]=" + many[0].ParameterType.Name);
            Console.WriteLine("[many.p1.type]=" + many[1].ParameterType.FullName);
            Console.WriteLine("[many.p2.type]=" + many[2].ParameterType.Name);
            Console.WriteLine("[many.p3.type]=" + (many[3].ParameterType.IsArray && many[3].ParameterType.GetElementType() == typeof(int)));
            Console.WriteLine("[many.p4.type]=" + (many[4].ParameterType == typeof(object)));
            Console.WriteLine("[many.p4.pos]=" + (many[4].Position == 4));
        }
    }
}