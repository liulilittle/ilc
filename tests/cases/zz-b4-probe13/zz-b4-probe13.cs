using System;

namespace Cases
{
    public static class Probe13
    {
        static void L(string n, Func<object> f)
        {
            try { f(); Console.WriteLine(n + "=OK"); }
            catch (Exception e)
            {
                string m = (e.Message ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
                string pn = e is ArgumentException ae ? ae.ParamName : null;
                Console.WriteLine(n + "=" + e.GetType().FullName + "|PN=" + (pn ?? "null") + "|" + m);
            }
        }

        public static void Main()
        {
            L("clr_idx", () => { var a = new[] { 1, 2, 3 }; Array.Clear(a, -1, 1); return null; });
            L("clr_len", () => { var a = new[] { 1, 2, 3 }; Array.Clear(a, 0, -1); return null; });
            L("cp_idx_src", () => { var a = new[] { 1, 2, 3 }; Array.Copy(a, -1, a, 0, 1); return null; });
            L("cp_idx_dst", () => { var a = new[] { 1, 2, 3 }; Array.Copy(a, 0, a, -1, 1); return null; });
            L("cp_len", () => { var a = new[] { 1, 2, 3 }; Array.Copy(a, a, -1); return null; });
            L("rev_idx", () => { var a = new[] { 1, 2, 3 }; Array.Reverse(a, -1, 1); return null; });
            L("rev_len", () => { var a = new[] { 1, 2, 3 }; Array.Reverse(a, 0, -1); return null; });
            L("srt_idx", () => { var a = new[] { 3, 1, 2 }; Array.Sort(a, -1, 1); return null; });
            L("srt_len", () => { var a = new[] { 3, 1, 2 }; Array.Sort(a, 0, -1); return null; });
            L("bs_idx", () => { var a = new[] { 1, 2, 3 }; Array.BinarySearch(a, -1, 1, 1); return null; });
            L("bs_len", () => { var a = new[] { 1, 2, 3 }; Array.BinarySearch(a, 0, -1, 1); return null; });
            L("idx_negstart", () => { var a = new[] { 1, 2, 3 }; Array.IndexOf(a, 1, -1, 1); return null; });
            L("lio_negstart", () => { var a = new[] { 1, 2, 3 }; Array.LastIndexOf(a, 1, -1, 1); return null; });
            L("clr_tail", () => { var a = new[] { 1, 2, 3 }; Array.Clear(a, 1, 3); return null; });
            L("cp_src_tail", () => { var a = new[] { 1, 2, 3 }; Array.Copy(a, 2, a, 0, 2); return null; });
            L("cp_dst_tail", () => { var a = new[] { 1, 2, 3 }; var b = new[] { 4, 5 }; Array.Copy(a, 0, b, 0, 3); return null; });
            L("rev_tail", () => { var a = new[] { 1, 2, 3 }; Array.Reverse(a, 1, 3); return null; });
            L("srt_tail", () => { var a = new[] { 3, 1, 2 }; Array.Sort(a, 1, 3); return null; });
            L("bs_tail", () => { var a = new[] { 1, 2, 3 }; Array.BinarySearch(a, 1, 3, 1); return null; });
        }
    }
}
