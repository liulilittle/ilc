using System;
using System.Text;

//------------------------------------------------------------------------------
// zz-b4-cap5 -- round-5 M-1 / M-3 oracle (net48 host measured):
//   Insert(int,int|long|bool) beyond MaxCapacity -> OutOfMemoryException
//   Insert(int,string) beyond MaxCapacity        -> AOORE("requiredLength")
//   Insert(int,char)                             -> succeeds
//   Append(string) beyond MaxCapacity            -> AOORE("requiredLength")
//   Capacity < Length (incl. negative)           -> AOORE("value")
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ZZB4Cap5
    {
        static string P(Action a)
        {
            try { a(); return "ok"; }
            catch (OutOfMemoryException) { return "OOM"; }
            catch (ArgumentOutOfRangeException ex)
            {
                return "AOORE:" + (ex.Message.Contains("requiredLength") ? "requiredLength" : "value");
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        public static void Main()
        {
            Console.WriteLine("[ins_i]=" + P(() => new StringBuilder(2, 3).Insert(0, 12345)));
            Console.WriteLine("[ins_l]=" + P(() => new StringBuilder(2, 3).Insert(0, 1234567890123L)));
            Console.WriteLine("[ins_b]=" + P(() => new StringBuilder(2, 3).Insert(0, true)));
            Console.WriteLine("[ins_s]=" + P(() => new StringBuilder(2, 3).Insert(0, "abc")));
            Console.WriteLine("[ins_c]=" + P(() => new StringBuilder(2, 3).Insert(0, 'x')));
            Console.WriteLine("[app_s]=" + P(() => { var sb = new StringBuilder(2, 3); sb.Append("ab"); sb.Append("cd"); }));

            var cap = new StringBuilder("abc");
            Console.WriteLine("[cap_below]=" + P(() => { cap.Capacity = 1; }));
            Console.WriteLine("[cap_neg]=" + P(() => { cap.Capacity = -1; }));
            Console.WriteLine("[cap_ok]=" + P(() => { cap.Capacity = 3; }));
        }
    }
}
