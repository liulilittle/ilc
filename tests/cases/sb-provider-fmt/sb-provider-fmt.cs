using System;
using System.Globalization;
using System.Text;

//------------------------------------------------------------------------------
// sb-provider-fmt -- StringBuilder.AppendFormat(IFormatProvider, ...) explicit
// arities. net48 has the provider-prefixed 1/2/3-argument forms besides the
// params-object[] form; this pins the fixed-arity provider surface end to end
// against the net48 oracle (the invariant provider is threaded into the
// composite engine).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class SbProviderFmt
    {
        public static void Main()
        {
            var inv = CultureInfo.InvariantCulture;

            StringBuilder s1 = new StringBuilder();
            s1.AppendFormat(inv, "{0:N2}", 1234.5);
            Console.WriteLine("f1=" + s1.ToString());

            StringBuilder s2 = new StringBuilder();
            s2.AppendFormat(inv, "{0:X4} {1}", 255, 42);
            Console.WriteLine("f2=" + s2.ToString());

            StringBuilder s3 = new StringBuilder();
            s3.AppendFormat(inv, "{0} {1} {2}", "a", "b", "c");
            Console.WriteLine("f3=" + s3.ToString());

            // the params-object[] form (already wired) still agrees
            StringBuilder s4 = new StringBuilder();
            s4.AppendFormat(inv, "{0}-{1}", new object[] { 7, 8 });
            Console.WriteLine("f4=" + s4.ToString());

            // chained provider appends
            StringBuilder s5 = new StringBuilder("seed");
            s5.AppendFormat(inv, "[{0:D3}]", 7).AppendFormat(inv, " {1}:{0}", 1, 2);
            Console.WriteLine("f5=" + s5.ToString());
        }
    }
}
