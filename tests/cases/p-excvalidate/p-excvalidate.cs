//------------------------------------------------------------------------------
// p-excvalidate.cs -- MSCORLIB exception-type alignment probe (GN-20 legacy
// batch, P1 "net48 exception types the String/StringBuilder API surface throws
// on invalid arguments"). net48 oracle == transpiled runtime byte-exact on the
// CATCHABLE exception TYPE NAME -- the semantic surface the audit flags (a
// user can catch ArgumentNullException vs ArgumentException vs
// ArgumentOutOfRangeException vs NotSupportedException and branch on it).
//
// Scope: the default zh-CN console process, no App.config. EVERY candidate
// call is wrapped; the record carries the exception TYPE NAME on a throw
// (`[id]=THREW:ArgumentNullException`) or `OK` when the call returns. The
// localized Message FIRST LINE is captured SEPARATELY (env-gated:
// P_EXC_MSGS=1) so the zh-CN net48 message text never pollutes the byte-exact
// diff gate -- the diff compares the type names (locale-independent) only.
//
// Candidates (three domains, the audit P1 suspects for
// ArgumentNullException/ArgumentException/ArgumentOutOfRangeException/
// NotSupportedException divergence):
//   String:        Substring(start<0), Substring(range overflow),
//                  Insert(index out of range), Insert(null value),
//                  Remove(start<0), Remove(count overflow), IndexOf(null),
//                  IndexOf neg-start, StartsWith(null), TrimStart(null char[]),
//                  Concat(null string[]), Join(null separator).
//   StringBuilder: ctor(capacity<0), Append(null), ToString(0, len) overflow,
//                  ToString(-1, 1).
//   Other:         String.Format(null format), Format index overflow.
//
// Encoding discipline: every record prints the exception TYPE NAME only; the
// messages (localized, host-dependent) go to the P_EXC_MSGS side capture, so
// expected.txt is reproducible on any host.
//------------------------------------------------------------------------------
using System;
using System.Text;

namespace Cases
{
    public static class PExcvalidate
    {
        private static readonly bool DumpMsgs =
            Environment.GetEnvironmentVariable("P_EXC_MSGS") == "1";

        private static void R(string id, string type, string msg)
        {
            Console.WriteLine("[" + id + "]=" + type);
            if (DumpMsgs)
            {
                string m = msg;
                int nl = m.IndexOf('\n');
                if (nl >= 0) m = m.Substring(0, nl);
                Console.WriteLine("[MSG:" + id + "]=" + Escape(m));
            }
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\') { sb.Append("\\x5C"); continue; }
                if (c == '\n') { sb.Append("\\n"); continue; }
                if (c == '\r') { sb.Append("\\r"); continue; }
                if (c >= 0x20 && c < 0x7F) { sb.Append(c); continue; }
                sb.Append("\\u");
                sb.Append(((int)c).ToString("X4"));
            }
            return sb.ToString();
        }

        private static void TryThrows(string id, Action act)
        {
            try
            {
                act();
                R(id, "OK", "");
            }
            catch (Exception e)
            {
                R(id, "THREW:" + e.GetType().Name, e.Message);
            }
        }

        public static void Main()
        {
            string s = "abc";

            // --- String.Substring bounds ---
            TryThrows("sub.start.neg", () => { string x = s.Substring(-1); });
            TryThrows("sub.range.over", () => { string x = s.Substring(1, 100); });

            // --- String.Insert: index vs null value ---
            TryThrows("ins.idx.over", () => { string x = s.Insert(5, "x"); });
            TryThrows("ins.null.val", () => { string x = s.Insert(1, null); });

            // --- String.Remove: start<0 / count overflow ---
            TryThrows("rem.start.neg", () => { string x = s.Remove(-1); });
            TryThrows("rem.count.over", () => { string x = s.Remove(1, 100); });

            // --- String.IndexOf: null / negative start ---
            TryThrows("iof.null.val", () => { int x = s.IndexOf((string)null); });
            TryThrows("iof.neg.start", () => { int x = s.IndexOf("b", -1); });

            // --- String.StartsWith(null) ---
            TryThrows("sw.null.val", () => { bool x = s.StartsWith((string)null); });

            // --- String.TrimStart(null char[]) -> net48? ---
            TryThrows("trimstart.null", () => { string x = " x ".TrimStart((char[])null); });

            // --- String.Concat(null string[]) ---
            TryThrows("concat.null.arr", () => { string x = string.Concat((string[])null); });

            // --- String.Join(null separator) legal ---
            TryThrows("join.null.sep", () => { string x = string.Join(null, new[] { "a", "b" }); });

            // --- StringBuilder ctor negative capacity ---
            TryThrows("sb.ctor.negcap", () => { var sb0 = new StringBuilder(-1); });

            // --- StringBuilder.Append(null) -> legal? ---
            TryThrows("sb.append.null", () => { var sb1 = new StringBuilder(); StringBuilder r = sb1.Append((string)null); });

            // --- StringBuilder set_Length: negative -> AOORE; grow pads \0 ---
            // --- StringBuilder set_Length: negative -> AOORE; grow pads \0 ---
            var sbNeg = new StringBuilder("abc");
            TryThrows("sb.len.neg", () => { sbNeg.Length = -1; });

            var sbGrow = new StringBuilder("abc");
            sbGrow.Length = 5;
            R("sb.len.grow", "content=" + Escape(sbGrow.ToString()), "");
            sbGrow.Length = 1;
            R("sb.len.trunc", "content=" + Escape(sbGrow.ToString()), "");

            // --- StringBuilder ToString(int,int): range validation ---
            var sb3 = new StringBuilder("abcd");
            TryThrows("sb.tostr.range", () => { string x = sb3.ToString(0, 100); });
            TryThrows("sb.tostr.negstart", () => { string x = sb3.ToString(-1, 1); });
            TryThrows("sb.tostr.neglen", () => { string x = sb3.ToString(0, -1); });
            TryThrows("sb.tostr.ok", () => { string x = sb3.ToString(1, 2); });
            TryThrows("sb.tostr.over", () => { string x = sb3.ToString(2, 3); });

            // --- String.Format: null format / index overflow ---
            TryThrows("fmt.null.fmt", () => { string x = string.Format(null, new object[] { 1 }); });
            TryThrows("fmt.idx.over", () => { string x = string.Format("{0} {3}", 1, 2); });
        }
    }
}