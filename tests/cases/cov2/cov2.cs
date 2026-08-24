using System;
using System.IO;
using System.Numerics;
class P
{
    static void Main()
    {
        bool bb;
        Console.WriteLine("b1=" + (bool.TryParse("true", out bb) && bb));
        Console.WriteLine("b2=" + (!bool.TryParse("notabool", out bb)));
        Console.WriteLine("b3=" + (bool.Parse("False") == false));
        byte[] src = { 1, 2, 3, 4, 5 };
        byte[] dst = new byte[5];
        Buffer.BlockCopy(src, 0, dst, 0, 5);
        Console.WriteLine("bb1=" + string.Join(",", dst));
        Console.WriteLine("bb2=" + (Buffer.GetByte(src, 3) == 4));
        int[] ints = { 10, 20, 30, 40 };
        Console.WriteLine("i1=" + (Array.IndexOf(ints, 30) == 2));
        Console.WriteLine("i2=" + (Array.IndexOf(ints, 99) == -1));
        Array arr = ints;
        Console.WriteLine("i3=" + (arr.LongLength == 4L));
        BigInteger big = new BigInteger(123456789012345L);
        Console.WriteLine("bg1=" + (big.ToString() == "123456789012345"));
        Console.WriteLine("bg2=" + (big + 1 == new BigInteger(123456789012346L)));
Console.WriteLine("bx1=" + ((object)5).Equals((object)5));
        object strong = "str";
        Console.WriteLine("bx3=" + strong.Equals("str"));
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write(3.5f);
        bw.Write(42);
        ms.Position = 0;
        BinaryReader br = new BinaryReader(ms);
        Console.WriteLine("rw1=" + (br.ReadSingle() == 3.5f));
        Console.WriteLine("rw2=" + (br.ReadInt32() == 42));
    }
}
