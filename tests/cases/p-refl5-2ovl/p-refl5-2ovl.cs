//------------------------------------------------------------------------------
// p-refl5-2ovl.cs -- refl5 item 2 net48 oracle probe: the reachable exact-
// overload reflection queries net48 actually has:
//   * Type.GetMethod(string, Type[], ParameterModifier[]) -- signature match
//     by arity + param-type identity; the ParameterModifier[] is IGNORED by
//     net48 for the match (it only carries by-ref modifiers).
//   * Type.GetProperty(string, Type returnType) -- NO arity-2 indexer-list
//     overload exists in net48; this is the returnType-AMBIGUITY disambiguator
//     (there is NO (string, Type[], Type[]) 3-param overload -- refl4 note).
//   * Type.GetProperty(string, Type returnType, Type[] types) -- the 3-param
//     form: match by return type AND the indexer params.
// Each record is a null/not-null bool + the matched member name, byte-stable.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class Ovl
    {
        public Ovl() { }
        public int M0() { return 1; }
        public int M1(int x) { return x; }
        public string M1(string s) { return s; }
        public int P1 { get; set; }
        public string P2 { get; set; }
        public string this[int i, string s] { get { return ""; } set { } }
    }

    public static class PRefl5Ovl
    {
        public static void Main()
        {
            var t = typeof(Ovl);

            // GetMethod(name, Type[], ParameterModifier[]) -- net48 exists.
            Console.WriteLine("[m3.pm.0]=" + (t.GetMethod("M0", new Type[0], new ParameterModifier[0]) != null));
            Console.WriteLine("[m3.pm.0n]=" + (t.GetMethod("M0", new Type[0], new[] { new ParameterModifier() }) != null));
            Console.WriteLine("[m3.pm.i]=" + (t.GetMethod("M1", new[] { typeof(int) }, new ParameterModifier[0]) != null));
            Console.WriteLine("[m3.pm.s]=" + (t.GetMethod("M1", new[] { typeof(string) }, new ParameterModifier[0]) != null));
            Console.WriteLine("[m3.pm.miss]=" + (t.GetMethod("M1", new[] { typeof(long) }, new ParameterModifier[0]) == null));
            Console.WriteLine("[m3.pm.name]=" + (t.GetMethod("M1", new[] { typeof(int) }, new ParameterModifier[0]).Name));
            Console.WriteLine("[m3.pm.arity]=" + (t.GetMethod("M0", new[] { typeof(int) }, new ParameterModifier[0]) == null));

            // GetProperty(string, Type returnType) -- arity-2, return-type
            // disambiguation. No (string, Type[], Type[]) overload exists.
            Console.WriteLine("[p2.ret.p1]=" + (t.GetProperty("P1", typeof(int)) != null));
            Console.WriteLine("[p2.ret.p1n]=" + (t.GetProperty("P1", typeof(string)) == null));
            Console.WriteLine("[p2.ret.p2]=" + (t.GetProperty("P2", typeof(string)) != null));
            Console.WriteLine("[p2.ret.p2n]=" + (t.GetProperty("P2", typeof(int)) == null));
            Console.WriteLine("[p2.ret.miss]=" + (t.GetProperty("Nope", typeof(int)) == null));

            // GetProperty(string, Type returnType, Type[] types) -- the indexer
            // exact match with return-type check.
            Console.WriteLine("[p3.ret.itm]=" + (t.GetProperty("Item", typeof(string), new[] { typeof(int), typeof(string) }) != null));
            Console.WriteLine("[p3.ret.wrong]=" + (t.GetProperty("Item", typeof(int), new[] { typeof(int), typeof(string) }) == null));
            Console.WriteLine("[p3.ret.arity]=" + (t.GetProperty("Item", typeof(string), new[] { typeof(int) }) == null));
            Console.WriteLine("[p3.ret.nonidx]=" + (t.GetProperty("P1", typeof(int), new Type[0]) != null));

            // ParameterModifier[] arity forms: passing a single-element works.
            Console.WriteLine("[m3.pm.single]=" + (t.GetMethod("M1", new[] { typeof(string) }, new[] { new ParameterModifier(1) }) != null));
        }
    }
}