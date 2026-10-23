//------------------------------------------------------------------------------
// strgen1.cs -- System.String LOUD-matrix probe.
//
// Every call below is a net48 System.String member the M5 string mapping is
// expected to reject (transpile-time NotSupportedException -> the whole Main
// is stubbed). net48 is the byte oracle.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases
{
    public static class StrGen1
    {
        public static void Main()
        {
            string s = "  Hello World  ";

            // ---- IndexOf family: (char,int,int) / (string,int,int) ----
            Console.WriteLine("idx_c2=" + s.IndexOf('o', 5));
            Console.WriteLine("idx_c3=" + s.IndexOf('o', 5, 2));
            Console.WriteLine("idx_s2=" + s.IndexOf("o", 5));
            Console.WriteLine("idx_s3=" + s.IndexOf("o", 5, 3));

            // ---- IndexOfAny / LastIndexOfAny arities ----
            char[] any = { 'o', 'W' };
            Console.WriteLine("ianya2=" + s.IndexOfAny(any));
            Console.WriteLine("ianya3=" + s.IndexOfAny(any, 5));
            Console.WriteLine("ianya4=" + s.IndexOfAny(any, 5, 3));
            Console.WriteLine("lianya2=" + s.LastIndexOfAny(any));
            Console.WriteLine("lianya3=" + s.LastIndexOfAny(any, 6));
            Console.WriteLine("lianya4=" + s.LastIndexOfAny(any, 6, 3));

            // ---- Compare with ignoreCase (bool) ----
            Console.WriteLine("cmp_ic=" + string.Compare("abc", "ABC", true));
            Console.WriteLine("eq_ic=" + string.Equals("abc", "ABC", StringComparison.OrdinalIgnoreCase));

            // ---- Equals 2-arg instance (string, StringComparison) ----
            Console.WriteLine("ieq_sc=" + "abc".Equals("ABC", StringComparison.OrdinalIgnoreCase));

            // ---- ToUpper/ToLower with CultureInfo ----
            Console.WriteLine("up_ci=" + "abc".ToUpper(CultureInfo.InvariantCulture));
            Console.WriteLine("lo_ci=" + "ABC".ToLower(CultureInfo.InvariantCulture));

            // ---- ToString(IFormatProvider) ----
            Console.WriteLine("tostr_ci=" + "abc".ToString(CultureInfo.InvariantCulture));

            // ---- Split full matrix ----
            string csv = "a,b,,c";
            Console.WriteLine("sp_ca=" + csv.Split(new char[] { ',' }).Length);
            Console.WriteLine("sp_ca_opt=" + csv.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length);
            Console.WriteLine("sp_sa=" + csv.Split(new string[] { "," }, StringSplitOptions.None).Length);
            Console.WriteLine("sp_n=" + csv.Split(new char[] { ',' }, 2).Length);
            Console.WriteLine("sp_nopt=" + csv.Split(new char[] { ',' }, 2, StringSplitOptions.None).Length);

            // ---- Remove / Insert ----
            Console.WriteLine("rm=" + "abcdef".Remove(2));
            Console.WriteLine("rm2=" + "abcdef".Remove(2, 2));
            Console.WriteLine("ins=" + "abcdef".Insert(2, "XY"));

            // ---- PadLeft / PadRight ----
            Console.WriteLine("padl=" + "7".PadLeft(4));
            Console.WriteLine("padl_c=" + "7".PadLeft(4, '.'));
            Console.WriteLine("padr=" + "7".PadRight(4));
            Console.WriteLine("padr_c=" + "7".PadRight(4, '.'));

            // ---- Trim family: char[] set ----
            Console.WriteLine("trim_set=[" + "xxabcxx".Trim(new char[] { 'x' }) + "]");
            Console.WriteLine("trims_set=[" + "xxabcxx".TrimStart(new char[] { 'x' }) + "]");
            Console.WriteLine("trime_set=[" + "xxabcxx".TrimEnd(new char[] { 'x' }) + "]");
            Console.WriteLine("trim_c=[" + "xxabcxx".Trim('x') + "]");

            // ---- Replace char/char ----
            Console.WriteLine("rep_cc=" + "a-b-c".Replace('-', '+'));
            Console.WriteLine("rep_ss=" + "a-b-c".Replace("-", "+"));

            // ---- Substring / ToCharArray ----
            Console.WriteLine("sub=" + "abcdef".Substring(2));
            Console.WriteLine("sub2=" + "abcdef".Substring(2, 3));
            Console.WriteLine("tca=" + "abc".ToCharArray().Length);
            Console.WriteLine("tca2=" + "abcdef".ToCharArray(2, 3).Length);

            // ---- Normalize / IsNormalized ----
            Console.WriteLine("isnorm=" + "abc".IsNormalized());
            Console.WriteLine("norm=" + "abc".Normalize());

            // ---- GetHashCode ----
            Console.WriteLine("ghc_same=" + ("abc".GetHashCode() == "abc".GetHashCode()));

            // ---- Concat numeric pair ----
            Console.WriteLine("cc_ii=" + string.Concat((object)1, (object)2));

            Console.WriteLine("done");
        }
    }
}
