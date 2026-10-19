using System;
using System.Text;

// Audit-B round4 probe: StringBuilder maxCapacity semantics vs net48 (host
// oracle). Focus: the growth-path exception type/ParamName and the observable
// Capacity after a growth that exceeds MaxCapacity.
namespace Cases
{
    public static class ZZB4Sbcap
    {
        static string Probe(Action a)
        {
            try { a(); return "ok"; }
            catch (OutOfMemoryException) { return "OOM"; }
            catch (ArgumentOutOfRangeException ex)
            {
                string p = ex.Message.Contains("requiredLength") ? "requiredLength"
                    : ex.Message.Contains("value") ? "value"
                    : ex.Message.Contains("count") ? "count"
                    : ex.Message.Contains("startIndex") ? "startIndex"
                    : ex.Message.Contains("repeatCount") ? "repeatCount" : "other";
                return "AOORE:" + p;
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        public static void Main()
        {
            Console.WriteLine("[ins_str_cnt_beyond]=" + Probe(() =>
            {
                var sb = new StringBuilder(4, 4);
                sb.Append("ab");
                sb.Insert(0, "X", 3);
            }));
            Console.WriteLine("[length_beyond]=" + Probe(() =>
            {
                var sb = new StringBuilder(4, 4);
                sb.Length = 10;
            }));
            Console.WriteLine("[capacity_beyond]=" + Probe(() =>
            {
                var sb = new StringBuilder(4, 4);
                sb.Capacity = 5;
            }));
            Console.WriteLine("[replace_at_max_cap]=" + Probe(() =>
            {
                var sb = new StringBuilder(6, 6);
                sb.Append("abcd");
                sb.Replace("a", "XY");
                Console.Write("cap=" + sb.Capacity + ";len=" + sb.Length + ";");
            }));
            Console.WriteLine("[appendchar_at_max_cap]=" + Probe(() =>
            {
                var sb = new StringBuilder(5, 6);
                sb.Append("abcd");
                sb.Append('X');
                sb.Append('Y');
                Console.Write("cap=" + sb.Capacity + ";len=" + sb.Length + ";");
            }));
            Console.WriteLine("[insert_at_max_cap]=" + Probe(() =>
            {
                var sb = new StringBuilder(6, 6);
                sb.Append("abcd");
                sb.Insert(0, "XY");
                Console.Write("cap=" + sb.Capacity + ";len=" + sb.Length + ";");
            }));
        }
    }
}
