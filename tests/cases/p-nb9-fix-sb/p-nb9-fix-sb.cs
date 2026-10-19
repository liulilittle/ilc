using System;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-sb -- nb9 fix batch: StringBuilder maxCapacity, EnsureCapacity,
// range validation order, ctor(string,int)/(string,int,int,int),
// Append(char,int) and Equals.
//
// net48 oracle (host .NET Framework 4.x, Add-Type):
//   append_s_2_2=ok:abcd              Append("abcdef",2,2)
//   append_s_1_3=AOORE:startIndex     Append("abc",1,3)
//   append_s_neg_0=AOORE:startIndex
//   append_s_0_neg=AOORE:count
//   append_s_null_neg=AOORE:startIndex
//   append_s_null_0_0=ok:ab
//   append_s_null_1_0=ANE:value
//   append_c_null_neg=AOORE:startIndex
//   append_c_null_0_0=ok:ab
//   append_c_2_2=ok:abcd
//   replace_null_neg=AOORE:startIndex
//   replace_null_0_0=ANE:oldValue
//   replace_null_1_0=ANE:oldValue
//   replace_1_9=AOORE:count
//   ctor_max_1_1=AOORE:requiredLength
//   ctor_str_int=ok:abcdef
//   ctor_str_4=ok:bc
//   ensure_20=ok:20
//   ensure_100_cap1max1=AOORE:value
//   append_c_count=ok:xxx
//   append_c_count_neg=AOORE:repeatCount
//
// ParamName is asserted through the exception MESSAGE (both runtimes compose
// the same zh-CN text + "parameter name:" line; ArgumentException.ParamName
// itself is not part of the stage-1 BCL surface).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixSb
    {
        static string Probe(Action a, string expectedParam)
        {
            try
            {
                a();
                return "ok";
            }
            catch (ArgumentNullException ex)
            {
                // The catch variable carries the message (the stage-1 carrier
                // model); the param name is the composed suffix.
                return "ANE:" + (ex.Message.Contains(expectedParam) ? expectedParam : "other");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return "AOORE:" + (ex.Message.Contains(expectedParam) ? expectedParam : "other");
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }

        public static void Main()
        {
            Console.WriteLine("[append_s_2_2]=" + Probe(() =>
            {
                var sb = new StringBuilder("ab");
                sb.Append("cdef", 2, 2);
                Console.Write(sb.ToString() == "abcd" ? "ok:" : "bad:");
                Console.WriteLine(sb.ToString());
            }, "startIndex"));

            Console.WriteLine("[append_s_1_3]=" + Probe(() => new StringBuilder().Append("abc", 1, 3), "startIndex"));
            Console.WriteLine("[append_s_neg_0]=" + Probe(() => new StringBuilder().Append("abc", -1, 0), "startIndex"));
            Console.WriteLine("[append_s_0_neg]=" + Probe(() => new StringBuilder().Append("abc", 0, -1), "count"));
            Console.WriteLine("[append_s_null_neg]=" + Probe(() => new StringBuilder().Append((string)null, -1, -1), "startIndex"));
            Console.WriteLine("[append_s_null_0_0]=" + Probe(() =>
            {
                var sb = new StringBuilder("ab");
                sb.Append((string)null, 0, 0);
                Console.Write("ok:" + sb.ToString());
            }, "value"));
            Console.WriteLine("[append_s_null_1_0]=" + Probe(() => new StringBuilder().Append((string)null, 1, 0), "value"));

            Console.WriteLine("[append_c_null_neg]=" + Probe(() => new StringBuilder().Append((char[])null, -1, -1), "startIndex"));
            Console.WriteLine("[append_c_null_0_0]=" + Probe(() =>
            {
                var sb = new StringBuilder("ab");
                sb.Append((char[])null, 0, 0);
                Console.Write("ok:" + sb.ToString());
            }, "value"));
            Console.WriteLine("[append_c_2_2]=" + Probe(() =>
            {
                var sb = new StringBuilder("ab");
                sb.Append(new[] { 'a', 'b', 'c', 'd' }, 2, 2);
                Console.Write("ok:" + sb.ToString());
            }, "startIndex"));

            Console.WriteLine("[replace_null_neg]=" + Probe(() => new StringBuilder("ab").Replace((string)null, "x", -1, -1), "startIndex"));
            Console.WriteLine("[replace_null_0_0]=" + Probe(() => new StringBuilder("ab").Replace((string)null, "x", 0, 0), "oldValue"));
            Console.WriteLine("[replace_null_1_0]=" + Probe(() => new StringBuilder("ab").Replace((string)null, "x", 1, 0), "oldValue"));
            Console.WriteLine("[replace_1_9]=" + Probe(() => new StringBuilder("ab").Replace("a", "x", 1, 9), "count"));

            Console.WriteLine("[ctor_max_1_1]=" + Probe(() =>
            {
                var sb = new StringBuilder(1, 1);
                sb.Append("abcdef");
            }, "requiredLength"));

            Console.WriteLine("[ctor_str_int]=" + Probe(() =>
            {
                var sb = new StringBuilder("abcdef", 2);
                Console.Write("ok:" + sb.ToString());
            }, "capacity"));

            Console.WriteLine("[ctor_str_4]=" + Probe(() =>
            {
                var sb = new StringBuilder("abcdef", 1, 2, 4);
                Console.Write("ok:" + sb.ToString());
            }, "capacity"));

            Console.WriteLine("[ensure_20]=" + Probe(() =>
            {
                var sb = new StringBuilder(1, 100);
                Console.Write("ok:" + sb.EnsureCapacity(20));
            }, "value"));

            Console.WriteLine("[ensure_100_cap1max1]=" + Probe(() =>
            {
                var sb = new StringBuilder(1, 1);
                sb.EnsureCapacity(100);
            }, "value"));

            Console.WriteLine("[append_c_count]=" + Probe(() =>
            {
                var sb = new StringBuilder();
                sb.Append('x', 3);
                Console.Write("ok:" + sb.ToString());
            }, "repeatCount"));

            Console.WriteLine("[append_c_count_neg]=" + Probe(() => new StringBuilder().Append('x', -1), "repeatCount"));

            // M17: Equals(StringBuilder) is VALUE equality (Length + Capacity +
            // MaxCapacity + units); Equals(object) stays reference equality.
            var a = new StringBuilder("abc");
            var b = new StringBuilder("abc");
            var c = new StringBuilder("abc", 100);
            var same = a;
            Console.WriteLine("[eq_same_content]=" + a.Equals(b));
            Console.WriteLine("[eq_same_obj]=" + a.Equals(same));
            Console.WriteLine("[eq_diff_capacity]=" + a.Equals(c));
            Console.WriteLine("[eq_diff_content]=" + a.Equals(new StringBuilder("abd")));
            Console.WriteLine("[eq_null_sb]=" + a.Equals((StringBuilder)null));
            Console.WriteLine("[eq_obj_null]=" + a.Equals((object)null));
            Console.WriteLine("[eq_obj_same_ref]=" + a.Equals((object)b));
            Console.WriteLine("[eq_obj_self]=" + a.Equals((object)a));
        }
    }
}
