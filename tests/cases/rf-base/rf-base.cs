//------------------------------------------------------------------------------
// rf-base.cs -- RF-N2 audit-A-③ (observation D) probe: Type.BaseType semantics
// for a USER class whose base chain ends in a BCL NON-Object base
// (System.Exception). The descriptor's baseClass link is only set for
// RESOLVED USER bases (BaseType != null); a BCL base stays null. net48's
// typeof(CustomEx).BaseType is NON-null (System.Exception) -- the probe pins
// what the ILC runtime must answer byte-for-byte.
//
//   CustomEx : Exception          (terminal BCL base -- the audit edge)
//   CustomMid : CustomEx          (user chain on top of a BCL base)
//
// net48 truth (expected): both report their immediate base type name; the
// top-level root still reports null (typeof(object).BaseType == null).
// Receivers use typeof() FOLDED forms (ex.GetType() on an Exception-derived
// BCL-owner receiver hits an unrelated M5 gap -- see transpile log).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class CustomEx : Exception
    {
        public CustomEx(string m) : base(m) { }
    }

    public class CustomMid : CustomEx
    {
        public CustomMid() : base("mid") { }
    }

    public static class RfBase
    {
        public static void Main()
        {
            Type mid = typeof(CustomMid);
            Type bt = mid.BaseType;
            Console.WriteLine("base_type=" + (bt != null ? bt.Name : "<null>"));
            Type bbt = bt != null ? bt.BaseType : null;
            Console.WriteLine("base_base_type=" + (bbt != null ? bbt.Name : "<null>"));
            Console.WriteLine("root_base=" + (typeof(object).BaseType != null ? typeof(object).BaseType.Name : "<null>"));
            Console.WriteLine("top_base=" + (typeof(CustomEx).BaseType != null ? typeof(CustomEx).BaseType.Name : "<null>"));
        }
    }
}