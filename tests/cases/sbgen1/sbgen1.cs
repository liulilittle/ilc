//------------------------------------------------------------------------------
// sbgen1.cs -- System.Text.StringBuilder LOUD-matrix probe.
//
// Every call below is a net48 StringBuilder member the M5 mapping is expected
// to reject. net48 is the byte oracle.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Text;

namespace Cases
{
    public static class SbGen1
    {
        public static void Main()
        {
            // ---- Append(char[], int, int) ----
            char[] buf = { 'a', 'b', 'c', 'd' };
            StringBuilder a = new StringBuilder();
            a.Append(buf, 1, 2);
            Console.WriteLine("ap_ca=" + a.ToString());

            // ---- Append(char, int) ----
            a = new StringBuilder();
            a.Append('x', 4);
            Console.WriteLine("ap_cc=" + a.ToString());

            // ---- Append(StringBuilder) ----
            a = new StringBuilder("ab");
            StringBuilder b = new StringBuilder("cd");
            a.Append(b);
            Console.WriteLine("ap_sb=" + a.ToString());

            // ---- Append(object) ----
            a = new StringBuilder();
            a.Append((object)42);
            a.Append((object)null);
            Console.WriteLine("ap_o=[" + a.ToString() + "]");

            // ---- Append(byte/sbyte/short/ushort/uint/ulong/float) ----
            a = new StringBuilder();
            a.Append(1.5f);
            a.Append((byte)7);
            a.Append((sbyte)-3);
            a.Append((short)-300);
            a.Append((ushort)60000);
            a.Append(4u);
            a.Append(5ul);
            Console.WriteLine("ap_num=" + a.ToString());

            // ---- AppendFormat matrix ----
            a = new StringBuilder();
            a.AppendFormat("{0}-{1}", 1, 2);
            Console.WriteLine("af2=" + a.ToString());
            a = new StringBuilder();
            a.AppendFormat("{0}-{1}-{2}", 1, 2, 3);
            Console.WriteLine("af3=" + a.ToString());
            a = new StringBuilder();
            a.AppendFormat("{0}-{1}-{2}-{3}", 1, 2, 3, 4);
            Console.WriteLine("af4=" + a.ToString());
            a = new StringBuilder();
            a.AppendFormat("{0}-{1}-{2}-{3}-{4}", new object[] { 1, 2, 3, 4, 5 });
            Console.WriteLine("afarr=" + a.ToString());
            a = new StringBuilder();
            a.AppendFormat(CultureInfo.InvariantCulture, "{0}-{1}", 1, 2);
            Console.WriteLine("afprov=" + a.ToString());

            // ---- Insert full matrix ----
            a = new StringBuilder("ac");
            a.Insert(1, "b");
            Console.WriteLine("ins_s=" + a.ToString());
            a = new StringBuilder("ac");
            a.Insert(1, 'b');
            Console.WriteLine("ins_c=" + a.ToString());
            a = new StringBuilder("13");
            a.Insert(1, 2);
            Console.WriteLine("ins_i=" + a.ToString());
            a = new StringBuilder("13");
            a.Insert(1, 2L);
            Console.WriteLine("ins_l=" + a.ToString());
            a = new StringBuilder("13");
            a.Insert(1, 2.5);
            Console.WriteLine("ins_d=" + a.ToString());
            a = new StringBuilder("1");
            a.Insert(1, new char[] { 'x', 'y' }, 0, 2);
            Console.WriteLine("ins_car=" + a.ToString());

            // ---- Replace matrix ----
            a = new StringBuilder("a-b-c");
            a.Replace('-', '+');
            Console.WriteLine("rp_cc=" + a.ToString());
            a = new StringBuilder("a-b-c");
            a.Replace("-", "+");
            Console.WriteLine("rp_ss=" + a.ToString());

            // ---- Remove(int,int) ----
            a = new StringBuilder("abcdef");
            a.Remove(1, 2);
            Console.WriteLine("rm=" + a.ToString());

            // ---- ToString(int,int) ----
            a = new StringBuilder("abcdef");
            Console.WriteLine("ts2=" + a.ToString(1, 3));

            // ---- CopyTo / EnsureCapacity / Equals ----
            a = new StringBuilder("abc");
            char[] dest = new char[3];
            a.CopyTo(0, dest, 0, 3);
            Console.WriteLine("ct=" + new string(dest));
            Console.WriteLine("ec=" + a.EnsureCapacity(100));
            Console.WriteLine("eq_self=" + a.Equals(a));
            StringBuilder other = new StringBuilder("abc");
            Console.WriteLine("eq_other=" + a.Equals(other));

            // ---- Length / Capacity / indexer ----
            a = new StringBuilder("abc");
            a.Length = 2;
            Console.WriteLine("len=" + a.Length);
            Console.WriteLine("cap_ok=" + (a.Capacity >= a.Length));
            a[0] = 'Z';
            Console.WriteLine("chr=" + a[0]);
            Console.WriteLine("done");
        }
    }
}
