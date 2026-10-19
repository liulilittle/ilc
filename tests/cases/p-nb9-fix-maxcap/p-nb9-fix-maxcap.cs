using System;
using System.Text;

//------------------------------------------------------------------------------
// p-nb9-fix-maxcap -- nb9 fix batch M6/H4/H5: StringBuilder.MaxCapacity is
// per-instance state, and EVERY growth path (Insert / Replace as well as
// Append) must honour it.
//
// Before this fix:
//   * get_MaxCapacity returned the constant INT32_MAX, ignoring ctor(int,int);
//   * Insert grew past MaxCapacity (no check in the Insert growth policy);
//   * Replace grew past MaxCapacity (ReplaceBuffer called directly).
//
// net48 oracle (host .NET Framework 4.x):
//   [max_default]=2147483647
//   [max_1_5]=5
//   [max_1_100]=100
//   [insert_beyond_max]=AOORE:requiredLength
//   [replace_beyond_max]=AOORE:requiredLength
//   [replace_range]=AOORE:requiredLength
//   [insert_within]=ok:abXYZ
//   [replace_within]=ok:aXYc
//
// ParamName is asserted through the exception MESSAGE (both runtimes compose
// the same zh-CN text + parameter-name line).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixMaxcap
    {
        static string Probe(Action a, string expectedParam)
        {
            try
            {
                a();
                return "ok";
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return ex.Message.Contains(expectedParam) ? "AOORE:" + expectedParam : "AOORE:other";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }

        public static void Main()
        {
            Console.WriteLine("[max_default]=" + new StringBuilder().MaxCapacity);
            Console.WriteLine("[max_1_5]=" + new StringBuilder(1, 5).MaxCapacity);
            Console.WriteLine("[max_1_100]=" + new StringBuilder(1, 100).MaxCapacity);

            Console.WriteLine("[insert_beyond_max]=" + Probe(() =>
            {
                var sb = new StringBuilder(4, 4);
                sb.Append("ab");
                sb.Insert(0, "XYZ");
            }, "requiredLength"));

            Console.WriteLine("[replace_beyond_max]=" + Probe(() =>
            {
                var sb = new StringBuilder(4, 6);
                sb.Append("abc");
                sb.Replace("a", "abcdefgh");
            }, "requiredLength"));

            Console.WriteLine("[replace_range]=" + Probe(() =>
            {
                var sb = new StringBuilder(4, 6);
                sb.Append("abcdef");
                sb.Replace("a", "ZZZZ", 0, 6);
            }, "requiredLength"));

            var ok1 = new StringBuilder(4, 16);
            ok1.Append("ab");
            ok1.Insert(2, "XYZ");
            Console.WriteLine("[insert_within]=" + ok1.ToString());

            var ok2 = new StringBuilder(4, 16);
            ok2.Append("abc");
            ok2.Replace("b", "XY");
            Console.WriteLine("[replace_within]=" + ok2.ToString());
        }
    }
}
