using System;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-sb -- nb9 fix batch: StringBuilder maxCapacity, EnsureCapacity,
// range validation order, ctor(string,int)/(string,int,int,int),
// Append(char,int) and Equals(object).
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
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixSb
    {
        static string Probe(Action a)
        {
            try
            {
                a();
                return "ok";
            }
            catch (ArgumentNullException ex)
            {
                return "ANE:" + ex.ParamName;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return "AOORE:" + ex.ParamName;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }

        static string Text(StringBuilder sb)
        {
            return sb.ToString();
        }

        public static void Main()
        {
            Console.WriteLine("[append_s_2_2]=" + Probe(() =>
            {
                var sb = new StringBuilder("ab");
                sb.Append("cdef", 2, 2);
                Console.Write(Text(sb) == "abcd" ? "ok:" : "bad:");
                Console.WriteLine(Text(sb));
            }));

            Console.WriteLine("[append_s_1_3]=" + Probe(() => new StringBuilder().Append("abc", 1, 3)));
            Console.WriteLine("[append_s_neg_0]=" + Probe(() => new StringBuilder().Append("abc", -1, 0)));
            Console.WriteLine("[append_s_0_neg]=" + Probe(() => new StringBuilder().Append("abc", 0, -1)));
            Console.WriteLine("[append_s_null_neg]=" + Probe(() => new StringBuilder().Append((string)null, -1, -1)));
            Console.WriteLine("[append_s_null_0_0]=" + Probe(() =>
            {
                var sb = new StringBuilder("ab");
                sb.Append((string)null, 0, 0);
                Console.Write("ok:" + Text(sb));
            }));
            Console.WriteLine("[append_s_null_1_0]=" + Probe(() => new StringBuilder().Append((string)null, 1, 0)));

            Console.WriteLine("[append_c_null_neg]=" + Probe(() => new StringBuilder().Append((char[])null, -1, -1)));
            Console.WriteLine("[append_c_null_0_0]=" + Probe(() =>
            {
                var sb = new StringBuilder("ab");
                sb.Append((char[])null, 0, 0);
                Console.Write("ok:" + Text(sb));
            }));
            Console.WriteLine("[append_c_2_2]=" + Probe(() =>
            {
                var sb = new StringBuilder("ab");
                sb.Append(new[] { 'a', 'b', 'c', 'd' }, 2, 2);
                Console.Write("ok:" + Text(sb));
            }));

            Console.WriteLine("[replace_null_neg]=" + Probe(() => new StringBuilder("ab").Replace((string)null, "x", -1, -1)));
            Console.WriteLine("[replace_null_0_0]=" + Probe(() => new StringBuilder("ab").Replace((string)null, "x", 0, 0)));
            Console.WriteLine("[replace_null_1_0]=" + Probe(() => new StringBuilder("ab").Replace((string)null, "x", 1, 0)));
            Console.WriteLine("[replace_1_9]=" + Probe(() => new StringBuilder("ab").Replace("a", "x", 1, 9)));

            Console.WriteLine("[ctor_max_1_1]=" + Probe(() =>
            {
                var sb = new StringBuilder(1, 1);
                sb.Append("abcdef");
            }));

            Console.WriteLine("[ctor_str_int]=" + Probe(() =>
            {
                var sb = new StringBuilder("abcdef", 2);
                Console.Write("ok:" + Text(sb));
            }));

            Console.WriteLine("[ctor_str_4]=" + Probe(() =>
            {
                var sb = new StringBuilder("abcdef", 1, 2, 4);
                Console.Write("ok:" + Text(sb));
            }));

            Console.WriteLine("[ensure_20]=" + Probe(() =>
            {
                var sb = new StringBuilder(1, 100);
                Console.Write("ok:" + sb.EnsureCapacity(20));
            }));

            Console.WriteLine("[ensure_100_cap1max1]=" + Probe(() =>
            {
                var sb = new StringBuilder(1, 1);
                sb.EnsureCapacity(100);
            }));

            Console.WriteLine("[append_c_count]=" + Probe(() =>
            {
                var sb = new StringBuilder();
                sb.Append('x', 3);
                Console.Write("ok:" + Text(sb));
            }));

            Console.WriteLine("[append_c_count_neg]=" + Probe(() => new StringBuilder().Append('x', -1)));

            Console.WriteLine("[equals_obj]=" + new StringBuilder("a").Equals((object)new StringBuilder("a")));
            Console.WriteLine("[equals_obj_null]=" + new StringBuilder("a").Equals((object)null));
            var same = new StringBuilder("a");
            Console.WriteLine("[equals_obj_same]=" + same.Equals((object)same));
        }
    }
}
