//------------------------------------------------------------------------------
// p-refl6-mprops.cs -- refl6b item B/C net48 oracle probe: the member/MODULE
// property surface + ContainsGenericParameters + the visibility-predicate
// precision.
//   * get_Module (Type + member owners) -- Module.Name == the MODULE name
//     (the output file name, e.g. "p-refl6-mprops.exe" -- net48 Module.Name
//     for the single-module assembly). Printed on a Type VALUE and on a
//     FieldInfo VALUE: both go to the same fold.
//   * Type.get_ContainsGenericParameters -- the pure-fold predicate:
//       typeof(Box<>) open-definition -> True
//       typeof(Box<int>) closed       -> False
//       typeof(int)/int[]             -> False
//       typeof(Outer<>) open          -> True
//       typeof(Outer<>.Inner) nested-in-open -> True
//       typeof(Outer<int>.Inner) nested-in-closed -> False
//   * IsFamilyOrAssembly (new) + IsAssembly (precision fix) -- the net48
//     exact-member-access decode:
//       IsAssembly = vis == Internal(0x3) ONLY (a protected / protected-
//       internal / public field is False -- the batch fixes the old
//       "non-public AND non-private = internal" conflation).
//       IsFamilyOrAssembly = vis == FamORAssem(0x5) ONLY (protected-internal;
//       a plain protected / internal / public member is False).
//   * IsStatic (a field is STATIC -> True) -- unchanged (regression guard).
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class Box<T> { }
    public class Outer<T> { public class Inner { } }

    public class MProps
    {
        public int FPub;
        private int FPriv;
        protected int FProt;
        internal int FInt;
        protected internal int FPI;
        private int FPriv2;
        public static int FS;
        public void MPub() { }
        protected void MProt() { }
        protected internal void MPI() { }
    }

    public static class PRefl6Mprops
    {
        public static void Main()
        {
            var t = typeof(MProps);

            // ---- get_Module (Type receiver + member receiver) ----
            Console.WriteLine("[mod.t]=" + t.Module.Name);
            Console.WriteLine("[mod.f]=" + t.GetField("FPub").Module.Name);

            // ---- ContainsGenericParameters (pure fold on typeof chains) ----
            Console.WriteLine("[cg.def]=" + typeof(Box<>).ContainsGenericParameters);
            Console.WriteLine("[cg.closed]=" + typeof(Box<int>).ContainsGenericParameters);
            Console.WriteLine("[cg.int]=" + typeof(int).ContainsGenericParameters);
            Console.WriteLine("[cg.arr]=" + typeof(int[]).ContainsGenericParameters);
            Console.WriteLine("[cg.ouropen]=" + typeof(Outer<>).ContainsGenericParameters);
            Console.WriteLine("[cg.inneropen]=" + typeof(Outer<>.Inner).ContainsGenericParameters);
            Console.WriteLine("[cg.innerclosed]=" + typeof(Outer<int>.Inner).ContainsGenericParameters);

            // ---- IsAssembly (exact Internal(0x3) decode) ----
            Console.WriteLine("[fpub.iass]=" + t.GetField("FPub").IsAssembly);
            Console.WriteLine("[fpriv.iass]=" + t.GetField("FPriv", BindingFlags.NonPublic | BindingFlags.Instance).IsAssembly);
            Console.WriteLine("[fprot.iass]=" + t.GetField("FProt", BindingFlags.NonPublic | BindingFlags.Instance).IsAssembly);
            Console.WriteLine("[fint.iass]=" + t.GetField("FInt", BindingFlags.NonPublic | BindingFlags.Instance).IsAssembly);
            Console.WriteLine("[fpi.iass]=" + t.GetField("FPI", BindingFlags.NonPublic | BindingFlags.Instance).IsAssembly);

            // ---- IsFamilyOrAssembly (exact FamORAssem(0x5) decode) ----
            Console.WriteLine("[fpub.fa]=" + t.GetField("FPub").IsFamilyOrAssembly);
            Console.WriteLine("[fpriv.fa]=" + t.GetField("FPriv", BindingFlags.NonPublic | BindingFlags.Instance).IsFamilyOrAssembly);
            Console.WriteLine("[fprot.fa]=" + t.GetField("FProt", BindingFlags.NonPublic | BindingFlags.Instance).IsFamilyOrAssembly);
            Console.WriteLine("[fint.fa]=" + t.GetField("FInt", BindingFlags.NonPublic | BindingFlags.Instance).IsFamilyOrAssembly);
            Console.WriteLine("[fpi.fa]=" + t.GetField("FPI", BindingFlags.NonPublic | BindingFlags.Instance).IsFamilyOrAssembly);
            Console.WriteLine("[mprot.fa]=" + t.GetMethod("MProt", BindingFlags.NonPublic | BindingFlags.Instance).IsFamilyOrAssembly);
            Console.WriteLine("[mpi.fa]=" + t.GetMethod("MPI", BindingFlags.NonPublic | BindingFlags.Instance).IsFamilyOrAssembly);

            // ---- IsStatic (unchanged bit decode -- regression guard) ----
            Console.WriteLine("[fs.isstatic]=" + t.GetField("FS").IsStatic);
            Console.WriteLine("[fpub.isstatic]=" + t.GetField("FPub").IsStatic);
            Console.WriteLine("[mpub.isstatic]=" + t.GetMethod("MPub").IsStatic);
        }
    }
}