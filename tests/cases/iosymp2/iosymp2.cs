// iosymp2 -- P2(3) probe (NON-POOL, A4): MemoryStream(byte[]) / MemoryStream(int) ctors.
using System;
using System.IO;
using System.Text;

internal static class P2MemStreamCtor
{
    private static string Hex(byte[] b)
    {
        StringBuilder sb = new StringBuilder();
        string d = "0123456789abcdef";
        for (int i = 0; i < b.Length; i++) { sb.Append(d[(b[i] >> 4) & 15]); sb.Append(d[b[i] & 15]); }
        return sb.ToString();
    }

    private static void Main()
    {
        MemoryStream a = new MemoryStream(new byte[] { 1, 2, 3 });
        Console.WriteLine("a_len=" + a.Length);
        Console.WriteLine("a_pos=" + a.Position);
        Console.WriteLine("a_caps=" + a.CanRead + "/" + a.CanWrite + "/" + a.CanSeek);
        Console.WriteLine("a_toarray=" + Hex(a.ToArray()));
        Console.WriteLine("a_read1=" + a.ReadByte());
        Console.WriteLine("a_pos2=" + a.Position);

        MemoryStream b = new MemoryStream(8);
        Console.WriteLine("b_len=" + b.Length);
        Console.WriteLine("b_pos=" + b.Position);
        byte[] one = new byte[] { 9 };
        b.Write(one, 0, 1);
        Console.WriteLine("b_len2=" + b.Length);
        Console.WriteLine("b_toarray=" + Hex(b.ToArray()));

        MemoryStream c = new MemoryStream(new byte[] { 0x0A, 0x0B, 0x0C, 0x0D });
        c.Position = 2;
        Console.WriteLine("c_read=" + c.ReadByte());
        Console.WriteLine("c_len=" + c.Length);
    }
}
