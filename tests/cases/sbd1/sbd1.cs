using System;
using System.Text;

// sbd1 -- StringBuilder capacity/length/AppendFormat matrix/AppendJoin/
// ToString(int,int)/chaining identity.
class Prog
{
    static void Main()
    {
        StringBuilder sb = new StringBuilder();
        Console.WriteLine("cap0=" + sb.Capacity);
        Console.WriteLine("len0=" + sb.Length);
        sb.Append("hello");
        Console.WriteLine("len5=" + sb.Length);
        Console.WriteLine("cap_grow_ge=" + (sb.Capacity >= 5));

        sb.Capacity = 64;
        Console.WriteLine("cap64=" + sb.Capacity);
        try { sb.Capacity = 2; Console.WriteLine("cap_small=no_throw"); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("cap_small=aoore"); }

        StringBuilder len = new StringBuilder("abc");
        len.Length = 6;
        Console.WriteLine("len6=" + len.Length + " text=" + len.ToString());
        Console.WriteLine("char3=" + (int)len[3]);
        len.Length = 2;
        Console.WriteLine("len2=" + len.ToString());

        StringBuilder af = new StringBuilder();
        af.AppendFormat("{0}-{1}", 1, 2);
        Console.WriteLine("af2=" + af.ToString());
        af.Clear();
        af.AppendFormat("{0}", "solo");
        Console.WriteLine("af1=" + af.ToString());
        af.Clear();
        af.AppendFormat("|{0,5}|{1,-5}|", 42, "ab");
        Console.WriteLine("af_align=" + af.ToString());
        af.Clear();
        af.AppendFormat("{0:X4}", 255);
        Console.WriteLine("af_fmt=" + af.ToString());
        af.Clear();
        af.AppendFormat("{0:N2}", 1234.5);
        Console.WriteLine("af_num=" + af.ToString());

        StringBuilder ch = new StringBuilder();
        StringBuilder same = ch.Append("a").Append("b").AppendLine().Append("c");
        Console.WriteLine("chain_same=" + ReferenceEquals(ch, same));
        Console.WriteLine("chain_text=" + ch.ToString().Replace("\r\n", "\\n").Replace("\n", "\\n"));

        StringBuilder ins = new StringBuilder("abcdef");
        ins.Insert(2, "XY");
        Console.WriteLine("insert=" + ins.ToString());
        ins.Remove(1, 3);
        Console.WriteLine("remove=" + ins.ToString());
        ins.Replace("X", "Q");
        Console.WriteLine("replace=" + ins.ToString());

        StringBuilder ts = new StringBuilder("0123456789");
        Console.WriteLine("sub=" + ts.ToString(2, 3));

        StringBuilder ec = new StringBuilder();
        Console.WriteLine("ensure=" + ec.EnsureCapacity(100));
        Console.WriteLine("cap_after_ensure=" + ec.Capacity);
    }
}
