//------------------------------------------------------------------------------
// excmsg1 -- CONSTRUCTED exception instances (never thrown).
//
// Theme (one topic per case): Message / ParamName / GetType().Name / isinst on
// `new Argument*Exception(...)` objects. The net48 Argument* ctors COMPOSE the
// message (default text + CRLF + the localized "parameter name: <p>" line) and
// store ParamName in an INSTANCE field -- neither is thread-local. Expected
// values are asserted against source literals, so the probe is culture-proof
// and byte-exact on both runtimes.
//
// The catch-variable (carrier) path lives in excmsg2 -- kept separate so a
// fold failure on one shape cannot mask the other.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ExcMsg1
    {
        private const string ParamLine = "\r\n\u53C2\u6570\u540D: ";

        private static void B(string k, bool v) { Console.WriteLine(k + "=[" + (v ? "T" : "F") + "]"); }
        private static void S(string k, string v) { Console.WriteLine(k + "=[" + (v == null ? "<null>" : v) + "]"); }

        public static void Main()
        {
            // ---- ArgumentException ----
            var ae = new ArgumentException("bad", "p");
            B("c1.msg", ae.Message == "bad" + ParamLine + "p");
            B("c1.pn", ae.ParamName == "p");
            S("c1.ty", ae.GetType().Name);
            B("c1.isae", ae is ArgumentException);
            B("c1.isex", ae is Exception);
            B("c1.isane", ae is ArgumentNullException);

            var ae1 = new ArgumentException("only-msg");
            B("c2.msg", ae1.Message == "only-msg");
            B("c2.pn", ae1.ParamName == null);

            var ae0 = new ArgumentException();
            B("c3.msg", ae0.Message == "\u503C\u4E0D\u5728\u9884\u671F\u7684\u8303\u56F4\u5185\u3002");
            B("c3.pn", ae0.ParamName == null);

            var aeInner = new ArgumentException("bad", new Exception("in"));
            B("c4.msg", aeInner.Message == "bad");
            B("c4.pn", aeInner.ParamName == null);

            var ae3 = new ArgumentException("bad", "p", new Exception("in"));
            B("c5.msg", ae3.Message == "bad" + ParamLine + "p");
            B("c5.pn", ae3.ParamName == "p");

            // ---- ArgumentNullException ----
            var an1 = new ArgumentNullException("q");
            B("n1.msg", an1.Message == "\u503C\u4E0D\u80FD\u4E3A null\u3002" + ParamLine + "q");
            B("n1.pn", an1.ParamName == "q");
            S("n1.ty", an1.GetType().Name);
            B("n1.isae", an1 is ArgumentException);

            var an2 = new ArgumentNullException("q", "msg");
            B("n2.msg", an2.Message == "msg" + ParamLine + "q");
            B("n2.pn", an2.ParamName == "q");

            var an0 = new ArgumentNullException();
            B("n3.msg", an0.Message == "\u503C\u4E0D\u80FD\u4E3A null\u3002");
            B("n3.pn", an0.ParamName == null);

            var anInner = new ArgumentNullException("q", new Exception("in"));
            B("n4.msg", anInner.Message == "q");
            B("n4.pn", anInner.ParamName == null);

            // ---- ArgumentOutOfRangeException ----
            var ar1 = new ArgumentOutOfRangeException("r");
            B("r1.msg", ar1.Message == "\u6307\u5B9A\u7684\u53C2\u6570\u5DF2\u8D85\u51FA\u6709\u6548\u503C\u7684\u8303\u56F4\u3002" + ParamLine + "r");
            B("r1.pn", ar1.ParamName == "r");
            S("r1.ty", ar1.GetType().Name);

            var ar2 = new ArgumentOutOfRangeException("r", "msg");
            B("r2.msg", ar2.Message == "msg" + ParamLine + "r");
            B("r2.pn", ar2.ParamName == "r");

            var ar0 = new ArgumentOutOfRangeException();
            B("r3.msg", ar0.Message == "\u6307\u5B9A\u7684\u53C2\u6570\u5DF2\u8D85\u51FA\u6709\u6548\u503C\u7684\u8303\u56F4\u3002");
            B("r3.pn", ar0.ParamName == null);

            var arInner = new ArgumentOutOfRangeException("r", new Exception("in"));
            B("r4.msg", arInner.Message == "r");
            B("r4.pn", arInner.ParamName == null);

            // ---- System.Exception (the message-carrier baseline) ----
            var ex = new Exception("plain");
            B("x1.msg", ex.Message == "plain");
            S("x1.ty", ex.GetType().Name);

            // ---- instances survive an unrelated throw (no thread-local leak) ----
            try { throw new ArgumentException("other", "zz"); }
            catch (ArgumentException) { }
            B("s1.msg", ae.Message == "bad" + ParamLine + "p");
            B("s1.pn", ae.ParamName == "p");
            B("s2.msg", ae0.Message == "\u503C\u4E0D\u5728\u9884\u671F\u7684\u8303\u56F4\u5185\u3002");
            B("s2.pn", ae0.ParamName == null);
        }
    }
}
