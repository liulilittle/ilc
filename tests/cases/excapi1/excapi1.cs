//------------------------------------------------------------------------------
// excapi1 -- System.Exception base-class property surface on CONSTRUCTED
// instances (never thrown): Message / InnerException / HelpLink / Source /
// StackTrace / TargetSite / HResult / Data.
//
// Every probe prints a LABEL=value record. Reference-typed values print
// <null> or their text; HResult prints as 0x%08X (absolute value, so a wrong
// implementation cannot stay self-consistent).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ExcApi1
    {
        private static void S(string k, string v) { Console.WriteLine(k + "=[" + (v == null ? "<null>" : v) + "]"); }
        private static void H(string k, int v) { Console.WriteLine(k + "=0x" + v.ToString("X8")); }
        private static void B(string k, bool v) { Console.WriteLine(k + "=[" + (v ? "T" : "F") + "]"); }

        public static void Main()
        {
            var e0 = new Exception();
            var e1 = new Exception("m");
            var e2 = new Exception("m", new Exception("in"));

            S("e0.msg", e0.Message);
            S("e1.msg", e1.Message);
            B("e0.inner.null", e0.InnerException == null);
            S("e2.inner.msg", e2.InnerException == null ? "<null>" : e2.InnerException.Message);
            B("e2.inner.inner.null", e2.InnerException.InnerException == null);

            H("e0.hr", e0.HResult);
            H("e1.hr", e1.HResult);
            H("e2.hr", e2.HResult);

            S("e1.help", e1.HelpLink);
            S("e1.source", e1.Source);
            S("e1.stack", e1.StackTrace);
            B("e1.target.null", e1.TargetSite == null);

            Console.WriteLine("e0.data.count=" + e0.Data.Count);
            e1.Data["k"] = 1;
            Console.WriteLine("e1.data.count=" + e1.Data.Count);
            S("e1.data.k", e1.Data["k"] == null ? "<null>" : e1.Data["k"].ToString());
            B("e1.data.same", object.ReferenceEquals(e1.Data, e1.Data));

            S("e1.tostr", e1.ToString());
            S("e0.tostr", e0.ToString());

            var a = new ArgumentException("bad", "p");
            S("arg.msg", a.Message);
            H("arg.hr", a.HResult);
            S("arg.help", a.HelpLink);
            S("arg.stack", a.StackTrace);
            S("arg.tostr", a.ToString());
        }
    }
}
