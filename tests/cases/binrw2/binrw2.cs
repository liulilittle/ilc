using System;
using System.IO;

namespace Cases
{
    public static class BinRw2
    {
        public static void Main()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(42);
            w.Write(123456789012345L);
            w.Write(3.5);
            w.Write(true);
            w.Write("hello");
            w.Write((uint)7);
            w.Write(-987654321);
            ms.Position = 0;
            BinaryReader r = new BinaryReader(ms);
            Console.WriteLine(r.ReadInt32());
            Console.WriteLine(r.ReadInt64());
            Console.WriteLine(r.ReadDouble());
            Console.WriteLine(r.ReadBoolean());
            Console.WriteLine(r.ReadString());
            Console.WriteLine(r.ReadUInt32());
            Console.WriteLine(r.ReadInt32());
            // to-array round trip length check
            Console.WriteLine(ms.ToArray().Length);
        }
    }
}
