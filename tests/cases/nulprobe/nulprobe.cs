// nulprobe -- A1NUL e2e probe (NON-POOL: manual run only, not registered in
// pool-final-570). Literals with embedded NULs must keep their exact length
// and code units (net48 oracle) after string-pool interning.
using System;
using System.Text;

namespace Cases
{
    public static class NulProbe
    {
        private static void Lit(string label, string s)
        {
            Console.WriteLine(label + ".len=" + s.Length);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(((int)s[i]).ToString("X4")).Append(' ');
            }
            Console.WriteLine(label + ".cs=" + sb.ToString());
        }

        public static void Main()
        {
            Lit("A0B", "A\0B");
            Lit("z0", "\0");
            Lit("A0", "A\0");
            Lit("0B", "\0B");
            Lit("A0B0C", "A\0B\0C");
            Lit("cn0cn", "\u4E2D\0\u6587");
            Lit("ascii", "AB");
            Console.WriteLine("ident=" + ReferenceEquals("dup\0x", "dup\0x"));
        }
    }
}
