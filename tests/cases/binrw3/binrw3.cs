using System;
using System.IO;

// binrw3 -- T4b parity probe: BinaryWriter/Reader decimal + char +
// byte[]/char[] round-trips over MemoryStream, byte-exact vs net48 oracle.
namespace Cases
{
    public static class BinRw3
    {
        public static void Main()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(42);
            w.Write(123.45m);
            w.Write('A');
            w.Write('中');
            byte[] raw = new byte[] { 1, 2, 255 };
            char[] chs = new char[] { 'x', 'y' };
            byte[] sub = new byte[] { 9, 8, 7 };
            char[] sq = new char[] { 'p', 'q' };
            w.Write(raw);
            w.Write(chs);
            w.Write(sub, 1, 2);
            w.Write(sq, 1, 1);
            ms.Position = 0;
            BinaryReader r = new BinaryReader(ms);
            Console.WriteLine("i=" + r.ReadInt32());
            Console.WriteLine("d=" + r.ReadDecimal());
            Console.WriteLine("c1=" + r.ReadChar());
            Console.WriteLine("c2=" + r.ReadChar());
            byte[] all = ms.ToArray();
            Console.WriteLine("len=" + all.Length);
            Console.WriteLine("b4=" + all[4]);
            Console.WriteLine("blast=" + all[all.Length - 1]);
        }
    }
}
