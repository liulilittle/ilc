using System;

namespace Cases
{
    public static class Probe9
    {
        static void L(string n, Func<object> f)
        {
            try { Console.WriteLine(n + "=OK:" + f()); }
            catch (Exception e)
            {
                string m = (e.Message ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
                string pn = null;
                if (e is ArgumentException ae) pn = ae.ParamName;
                Console.WriteLine(n + "=" + e.GetType().FullName + "|PN=" + (pn ?? "null") + "|" + m);
            }
        }

        static string HelperPn(ArgumentException e) { return e.ParamName; }

        public static void Main()
        {
            L("lio_end", () => { Array a = new int[3]; Array.LastIndexOf(a, 1, 0, 2); return "no"; });
            L("lio_count", () => { Array a = new int[3]; Array.LastIndexOf(a, 1, 2, -1); return "no"; });
            L("lio_start", () => { Array a = new int[3]; Array.LastIndexOf(a, 1, 9, 1); return "no"; });
            L("lio_g_end", () => { var a = new int[3]; Array.LastIndexOf<int>(a, 1, 0, 2); return "no"; }); L("lio_g_start", () => { var a = new int[3]; Array.LastIndexOf<int>(a, 1, 9, 1); return "no"; });
            L("lio_g_count", () => { var a = new int[3]; Array.LastIndexOf<int>(a, 1, 2, -1); return "no"; });
            L("idx_count", () => { Array a = new int[3]; Array.IndexOf(a, 1, 2, -1); return "no"; });
            L("sort_len", () => { var a = new int[3]; Array.Sort(a, 0, -1); return "no"; });
            L("bs_len", () => { var a = new int[3]; Array.BinarySearch(a, 0, -1, 1); return "no"; });
            L("bs_type", () => { Array a = new int[3]; Array.BinarySearch(a, 0, 3, "x"); return "no"; });
            L("sb_cap", () => { var sb = new System.Text.StringBuilder("ab"); sb.Capacity = 1; return "no"; });
            L("pn_helper", () => { try { Array.Sort(new int[3], 0, -1); return "no"; } catch (ArgumentException e) { return HelperPn(e) ?? "null"; } });
            L("pn_helper2", () => { try { Array.LastIndexOf((Array)new int[3], 1, 0, 2); return "no"; } catch (ArgumentException e) { var ae = (ArgumentException)e; return HelperPn(ae) ?? "null"; } });
        }
    }
}

