using System;
using System.Diagnostics;
using System.Text;

//------------------------------------------------------------------------------
// p-stacktrace.cs -- System.Diagnostics.StackTrace domain probe (GN-20 legacy,
// P1: StackTrace.GetStackFramesInternal is the one user-reachable internal-call
// gap). Purpose: measure what net48 actually RETURNS/produces for the icall so
// the ILC side can be judged byte-exact-feasible vs W7 (CLR-internal stack
// pointer semantics unreconstructible statically).
//
// Covers (all under the zh-CN host culture):
//   S1  plain new StackTrace() at Main depth       -- ToString() + frames
//   S2  new StackTrace(skip, needFileInfo)         -- file/line impact check
//   S3  new StackTrace(true)                       -- fNeedFileInfo=true shape
//   S4  exception capture (throw/catch -> new StackTrace(ex))
//   S5  exception path: ex.StackTrace after throw
//   S6  Environment.StackTrace (public getter, same underlying icall family)
//
// OUTPUT CONTRACT (byte-exact, net48 oracle = expected): every line is one
// self-describing record, no header/footer whitespace, no timestamps, no
// machine-specific absolute paths (paths are stripped to a normalized tail).
// The frame METHOD line is `TYPE.Method(params)` from the stack frame's
// MethodBase metadata; the file/line portions are separated so a W7 verdict
// can point precisely at which record depends on CLR-internal state.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PStackTrace
    {
        public static void Main()
        {
            Probe("S1", new StackTrace());
            Probe("S2", new StackTrace(0, false));
            Probe("S2b", new StackTrace(1, false));
            Probe("S3", new StackTrace(0, true));

            try { Throws(); }
            catch (Exception ex)
            {
                var stEx = new StackTrace(ex);
                Probe("S4", stEx);
                Console.WriteLine("[S5.ex] " + Strip(ex.ToString()));
            }

            Console.WriteLine("[S6.env] " + Strip(Environment.StackTrace));
            Console.WriteLine("[S7.now] " + new StackTrace().FrameCount);
            Console.WriteLine("[S8.onpect] " + OnPeek(new StackTrace()));
        }

        static void Throws() { ThrowInner(); }
        static void ThrowInner() { throw new InvalidOperationException("boom"); }

        static int OnPeek(StackTrace st)
        {
            return st.FrameCount;
        }

        static void Probe(string id, StackTrace st)
        {
            Console.WriteLine("[" + id + ".count] " + st.FrameCount);
            StackFrame[] frames = st.GetFrames();
            for (int i = 0; i < (frames == null ? 0 : frames.Length); i++)
            {
                StackFrame f = frames[i];
                var m = f.GetMethod();
                string who = m == null ? "<null-method>"
                    : (m.DeclaringType == null ? "" : m.DeclaringType.FullName) + "." + m.Name + "(" + Params(m) + ")";
                string file = f.GetFileName();
                if (file != null && file.Length > 0)
                {
                    int li = file.IndexOf(':');        // strip drive/colon prefix
                    string tail = li >= 0 ? file.Substring(Math.Max(0, file.Length - 24)) : file;
                    Console.WriteLine("[" + id + ".f" + i + ".m] " + who);
                    Console.WriteLine("[" + id + ".f" + i + ".file] " + tail + ":" + f.GetFileLineNumber() + ":" + f.GetFileColumnNumber());
                }
                else
                {
                    Console.WriteLine("[" + id + ".f" + i + ".m] " + who);
                }
            }
            string text = st.ToString();
            Console.WriteLine("[" + id + ".tostr] " + Strip(text));
        }

        static string Params(System.Reflection.MethodBase m)
        {
            var ps = m.GetParameters();
            var sb = new StringBuilder();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var pt = ps[i].ParameterType;
                sb.Append(pt == null ? "?" : pt.Name);
            }
            return sb.ToString();
        }

        static string Strip(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (var line in s.Replace("\r\n", "\n").Split('\n'))
            {
                string t = line;
                int inAt = t.IndexOf(" in ", StringComparison.Ordinal);
                if (inAt >= 0) t = t.Substring(0, inAt);  // drop " in X:\...\:line N"
                sb.AppendLine(t);
            }
            return sb.ToString().TrimEnd('\n');
        }
    }
}