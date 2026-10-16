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

    // refl2 (item 4 audit): same-SIGNATURE shadowing -- the most-derived
    // same-signature method hides its base-class twin (both `new` and
    // `override` shadow), so the no-types GetMethod stays a unique hit
    // instead of throwing AmbiguousMatchException. Different signatures
    // remain overloads and still throw.
    public class ShBase
    {
        public void F() { }
    }
    public class ShDer : ShBase
    {
        public new void F() { }
    }
    // Cross-layer static/instance interplay: the derived `new static`
    // hides the base INSTANCE F() (same signature, different layer).
    // net48 pins the verdict (oracle live) -- the ambiguity counter must
    // treat same-signature cross-layer entries as shadowed, NOT ambiguous.
    public class ShStatDer : ShBase
    {
        public new static void F() { }
    }
    public class OvBase
    {
        public virtual void F() { }
        public virtual void OnlyBase() { }
    }
    public class OvDer : OvBase
    {
        public override void F() { }
    }
    public class ObDer : OvBase
    {
        // inherits OvBase.F (virtual, not overridden) -- a base-only
        // same-signature method visible through the derived type.
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

            // refl2 (item 4 audit) same-signature shadowing:
            // 1. hide-by-new: derived F() (same signature) shadows base F() ->
            //    unique hit, no throw.
            var sh = typeof(ShDer);
            Console.WriteLine("[sh.hide.found]=" + (sh.GetMethod("F") != null));
            bool shHideThrew = false;
            try { sh.GetMethod("F"); } catch (AmbiguousMatchException) { shHideThrew = true; }
            Console.WriteLine("[sh.hide.throws]=" + shHideThrew);
            // 2. base virtual + derived override (same signature) -> unique hit.
            var ov = typeof(OvDer);
            Console.WriteLine("[ov.override.found]=" + (ov.GetMethod("F") != null));
            bool ovOverrideThrew = false;
            try { ov.GetMethod("F"); } catch (AmbiguousMatchException) { ovOverrideThrew = true; }
            Console.WriteLine("[ov.override.throws]=" + ovOverrideThrew);
            // 3. the base type alone exposes the same signature -> unique hit.
            var ob = typeof(OvBase);
            Console.WriteLine("[ov.baseonly.found]=" + (ob.GetMethod("F") != null));
            bool ovBaseOnlyThrew = false;
            try { ob.GetMethod("F"); } catch (AmbiguousMatchException) { ovBaseOnlyThrew = true; }
            Console.WriteLine("[ov.baseonly.throws]=" + ovBaseOnlyThrew);
            // 4. a base-only same-signature method inherited (not overridden)
            //    by the derived type -> unique hit through the derived type.
            var oi = typeof(ObDer);
            Console.WriteLine("[ob.inherit.found]=" + (oi.GetMethod("F") != null));
            bool obInheritThrew = false;
            try { oi.GetMethod("F"); } catch (AmbiguousMatchException) { obInheritThrew = true; }
            Console.WriteLine("[ob.inherit.throws]=" + obInheritThrew);
            // 5. a unique base-only method stays a hit (control).
            Console.WriteLine("[ov.base.only.unique]=" + (typeof(OvDer).GetMethod("OnlyBase") != null));
            // 6. cross-layer static/instance: derived `new static F()` hides the
            //    base INSTANCE F() -- net48 pins whether the default binding
            //    (Public|Instance|Static) treats them as one signature group
            //    (shadowed -> unique hit) or two (ambiguous).
            var ss = typeof(ShStatDer);
            Console.WriteLine("[sh.stat.new.found]=" + (ss.GetMethod("F") != null));
            bool shStatNewThrew = false;
            try { ss.GetMethod("F"); } catch (AmbiguousMatchException) { shStatNewThrew = true; }
            Console.WriteLine("[sh.stat.new.throws]=" + shStatNewThrew);
        }
    }
}