//------------------------------------------------------------------------------
// p-refl2-4ambig.cs -- refl2 net48 oracle probe for AmbiguousMatchException on
// Type.GetMethod(name) / (name, BindingFlags) when a type exposes MULTIPLE
// same-name PUBLIC methods (overloads). net48 semantics: a single exact-arity
// match resolves; multiple overloads on the queried type + base chain make
// GetMethod(name) ambiguous -> AmbiguousMatchException (probe-pinned). A
// UNIQUE public name stays a plain hit; an absent name is null. The
// (string, BindingFlags) form asserts the same behaviour for programs that
// pass an explicit visibility.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class AmbBase
    {
        public void Shared() { }
        public void Multi(int x) { }
    }
    public class AmbMid : AmbBase
    {
        public void Multi() { }
        public void Only() { }
        public void PrivateOne() { }
        private void Hidden() { }
        public static void StatOnly() { }
    }

    public static class PRefl2Ambig
    {
        public static void Main()
        {
            var t = typeof(AmbMid);
            // Unique names stay hits (byte-exact).
            Console.WriteLine("[amb.unique]=" + (t.GetMethod("Only") != null));
            Console.WriteLine("[amb.unique.bf]=" + (t.GetMethod("Only", BindingFlags.Public | BindingFlags.Instance) != null));
            // Absent name -> null.
            Console.WriteLine("[amb.absent]=" + (t.GetMethod("Nope") == null));
            // Shared (base-only) unique method -> hit, not ambiguous.
            Console.WriteLine("[amb.base.unique]=" + (t.GetMethod("Shared") != null));
            // Multi has two public overloads (own 0-arg + base 1-arg) ->
            // AmbiguousMatchException on the no-types query.
            bool ambThrew = false;
            try { t.GetMethod("Multi"); } catch (AmbiguousMatchException) { ambThrew = true; }
            Console.WriteLine("[amb.multi.throws]=" + ambThrew);
            // The (string, BindingFlags) form behaves the same.
            bool ambThrewBf = false;
            try { t.GetMethod("Multi", BindingFlags.Public | BindingFlags.Instance); } catch (AmbiguousMatchException) { ambThrewBf = true; }
            Console.WriteLine("[amb.multi.bf.throws]=" + ambThrewBf);
            // A private method with the same name does NOT make a public query
            // ambiguous (net48 binding hides it) -- "PrivateOne" resolves.
            Console.WriteLine("[amb.priv.unique]=" + (t.GetMethod("PrivateOne") != null));
        }
    }
}