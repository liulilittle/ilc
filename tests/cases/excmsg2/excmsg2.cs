//------------------------------------------------------------------------------
// excmsg2 -- CATCH-VARIABLE path (the runtime exception carrier).
//
// Theme (one topic per case): Message / ParamName / GetType().Name observed on
// the variable bound by `catch (Argument*Exception e)` after the SAME case
// throws that exception. This exercises the throw-site composition (the
// runtime carrier) -- the constructed-instance (side-table) path lives in
// excmsg1. Kept separate so a carrier regression cannot hide a side-table one.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ExcMsg2
    {
        private const string ParamLine = "\r\n\u53C2\u6570\u540D: ";

        private static void B(string k, bool v) { Console.WriteLine(k + "=[" + (v ? "T" : "F") + "]"); }
        private static void S(string k, string v) { Console.WriteLine(k + "=[" + (v == null ? "<null>" : v) + "]"); }

        public static void Main()
        {
            // ---- ArgumentException(message, paramName) ----
            try { throw new ArgumentException("bad", "p"); }
            catch (ArgumentException e)
            {
                B("t1.msg", e.Message == "bad" + ParamLine + "p");
                B("t1.pn", e.ParamName == "p");
                S("t1.ty", e.GetType().Name);
                B("t1.isex", e is Exception);
            }

            // ---- ArgumentException(message) ----
            try { throw new ArgumentException("bad"); }
            catch (ArgumentException e)
            {
                B("t2.msg", e.Message == "bad");
                B("t2.pn", e.ParamName == null);
                S("t2.ty", e.GetType().Name);
            }

            // ---- ArgumentException() ----
            try { throw new ArgumentException(); }
            catch (ArgumentException e)
            {
                B("t3.msg", e.Message == "\u503C\u4E0D\u5728\u9884\u671F\u7684\u8303\u56F4\u5185\u3002");
                B("t3.pn", e.ParamName == null);
            }

            // ---- ArgumentNullException(paramName) ----
            try { throw new ArgumentNullException("q"); }
            catch (ArgumentNullException e)
            {
                B("t4.msg", e.Message == "\u503C\u4E0D\u80FD\u4E3A null\u3002" + ParamLine + "q");
                B("t4.pn", e.ParamName == "q");
                S("t4.ty", e.GetType().Name);
                B("t4.isae", e is ArgumentException);
            }

            // ---- ArgumentNullException(paramName, message) ----
            try { throw new ArgumentNullException("q", "msg"); }
            catch (ArgumentNullException e)
            {
                B("t5.msg", e.Message == "msg" + ParamLine + "q");
                B("t5.pn", e.ParamName == "q");
            }

            // ---- ArgumentOutOfRangeException(paramName) ----
            try { throw new ArgumentOutOfRangeException("r"); }
            catch (ArgumentOutOfRangeException e)
            {
                B("t6.msg", e.Message == "\u6307\u5B9A\u7684\u53C2\u6570\u5DF2\u8D85\u51FA\u6709\u6548\u503C\u7684\u8303\u56F4\u3002" + ParamLine + "r");
                B("t6.pn", e.ParamName == "r");
                S("t6.ty", e.GetType().Name);
            }

            // ---- ArgumentOutOfRangeException(paramName, message) ----
            try { throw new ArgumentOutOfRangeException("r", "msg"); }
            catch (ArgumentOutOfRangeException e)
            {
                B("t7.msg", e.Message == "msg" + ParamLine + "r");
                B("t7.pn", e.ParamName == "r");
            }

            // ---- catch through the base type: ParamName stays reachable ----
            try { throw new ArgumentException("bad", "p"); }
            catch (Exception e)
            {
                S("t8.ty", e.GetType().Name);
                B("t8.msg", e.Message == "bad" + ParamLine + "p");
                var ae = e as ArgumentException;
                B("t8.pn", ae != null && ae.ParamName == "p");
            }

            // ---- plain System.Exception message ----
            try { throw new Exception("plain"); }
            catch (Exception e)
            {
                B("t9.msg", e.Message == "plain");
                S("t9.ty", e.GetType().Name);
            }
        }
    }
}
