using System;
using System.Text;

// p-culture-case -- FULL-BMP regression for the zh-CN culture case map of
// string.ToUpper()/ToLower() (stringext.cpp -> stringcase.cpp, generated
// from the net48 oracle). For every BMP code unit, prints
//   up|XXXX|=HEXUNITS   when string.ToUpper() differs from the input
//   lo|XXXX|=HEXUNITS   when string.ToLower() differs from the input
// net48 zh-CN: every changed unit maps 1:1 (no multi-unit expansions on the
// BMP -- U+00DF stays U+00DF as an uppercase). The expected.txt is the exact
// net48 oracle; the transpiled runtime must reproduce it byte-for-byte.
namespace Cases
{
    public static class PCultureCase
    {
        public static void Main()
        {
            var sb = new StringBuilder();
            char[] one = new char[1];
            for (int cp = 0; cp <= 0xFFFF; cp++)
            {
                one[0] = (char)cp;
                string s = new string(one);
                string up = s.ToUpper();
                string lo = s.ToLower();
                if (up != s)
                    sb.Append("up|").Append(cp.ToString("X4")).Append("|=").Append(H(up)).Append('\n');
                if (lo != s)
                    sb.Append("lo|").Append(cp.ToString("X4")).Append("|=").Append(H(lo)).Append('\n');
            }
            Console.Write(sb.ToString());
        }

        static string H(string s)
        {
            var hb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) hb.Append(' ');
                hb.Append(((int)s[i]).ToString("X4"));
            }
            return hb.ToString();
        }
    }
}