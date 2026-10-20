using System;

namespace Cases
{
    public static class Probe12
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
            L("rev_neg_len", () => { Array a = new[] { 1, 2, 3 }; Array.Reverse(a, 0, -1); return null; });
            L("rev_str_neg", () => { Array a = new[] { "a", "b" }; Array.Reverse(a, 0, -2); return null; });
            L("lio_neg_start", () => { Array a = new[] { 1, 2, 3 }; Array.LastIndexOf(a, 1, -1, 1); return null; });
            L("lio_g_neg_start", () => { var a = new[] { 1, 2, 3 }; Array.LastIndexOf<int>(a, 1, -1, 1); return null; });
            L("clear_neg_len", () => { Array a = new[] { 1, 2, 3 }; Array.Clear(a, 0, -1); return null; });
            L("copy_neg_len", () => { Array a = new[] { 1, 2, 3 }; Array.Copy(a, a, -1); return null; });
        }
    }
}

