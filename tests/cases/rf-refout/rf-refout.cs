//------------------------------------------------------------------------------
// rf-refout.cs -- RF-N8 e2e probe (net48 oracle, byte-for-byte): methods with
// REF/OUT (by-ref) PARAMETERS invoked through MethodInfo.Invoke must fail
// LOUDLY instead of silently returning nullptr (the pre-RF-N8 behavior -- the
// invoker cannot express the by-ref address ABI, and the argbuf builder's
// unknown-param-kind switch dropped the by-ref param silently).
//
//   void AddByRef(ref int x)     -- instance, ref VALUE-type param
//   int  GetViaOut(out int x)    -- instance, out VALUE-type param
//   static string StrByRef(ref string s) -- static, ref REFERENCE-type param
//
// net48 EXPLICITLY REJECTS a wrong-typed argument on a by-ref parameter
// (ArgumentException -- "Object of type 'System.String' cannot be converted
// to type 'System.Int32'"; probe-verified v4.0.30319). The ILC RF-N8 LOUD path
// rejects ANY by-ref parameter with NotSupportedException (with the wrong-typed
// argument, the by-ref reject fires before any value conversion). Both sides
// therefore print the SAME normalized bool: the call THREW an explicit
// exception. The RED is the pre-RF-N8 silent-null behavior (no throw -> False
// on the ILC side, mismatching the net48 True); the GREEN is the RF-N8 explicit
// NotSupportedException (True on both sides). The probe NEVER fabricates a
// by-ref write-back -- a by-ref param is rejected, never silently passed as
// null.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class RefOutHost
    {
        public void AddByRef(ref int x) { x = x + 10; }
        public int GetViaOut(out int x) { x = 7; return 99; }
        public static string StrByRef(ref string s) { s = s + "!"; return s; }
    }

    public static class RfRefOut
    {
        public static void Main()
        {
            var h = new RefOutHost();

            // ref VALUE-type param, wrong-typed arg: net48 ArgumentException;
            // ILC RF-N8 -> NotSupportedException. Normalized: loud=True.
            bool refInt = false;
            try { typeof(RefOutHost).GetMethod("AddByRef").Invoke(h, new object[] { "zz" }); }
            catch (Exception) { refInt = true; }
            Console.WriteLine("refint_loud=" + refInt);

            // out VALUE-type param, wrong-typed arg: same normalized contract.
            bool outInt = false;
            try { typeof(RefOutHost).GetMethod("GetViaOut").Invoke(h, new object[] { "zz" }); }
            catch (Exception) { outInt = true; }
            Console.WriteLine("outint_loud=" + outInt);

            // static ref REFERENCE-type param, wrong-typed arg (int where a
            // string ref is expected): net48 ignores the receiver for statics
            // (a non-null receiver keeps the ILC free-function path intact)
            // and rejects the wrong type with ArgumentException; ILC RF-N8 ->
            // NotSupportedException. Normalized: loud=True.
            bool refStr = false;
            try { typeof(RefOutHost).GetMethod("StrByRef").Invoke(h, new object[] { 42 }); }
            catch (Exception) { refStr = true; }
            Console.WriteLine("refstr_loud=" + refStr);
        }
    }
}