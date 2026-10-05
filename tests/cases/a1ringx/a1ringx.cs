// a1ringx -- A1/A6 ring interaction probe (SCRATCH-ONLY): after a
// StreamReader ctor BOM peek pushes bytes into the shared pushback ring, a
// BinaryReader read and a later ReadToEnd must not lose/duplicate/reorder
// bytes. Scenario A: StreamReader ctor -> BinaryReader.ReadInt32 ->
// ReadToEnd. Scenario B (reverse): BinaryReader.ReadInt32 -> StreamReader
// ctor -> ReadToEnd. (ReadByte is an unmapped LOUD gap, ReadInt32 is wired.)
using System;
using System.IO;
using System.Text;

namespace Cases
{
    public static class A1RingX
    {
        private static void Dump(string label, int v, string rest)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(label).Append(".i32=").Append(v.ToString("X8")).Append(" rest=");
            for (int i = 0; i < rest.Length; i++)
            {
                sb.Append(((int)rest[i]).ToString("X4")).Append(' ');
            }
            Console.WriteLine(sb.ToString());
        }

        private static void ScenarioMemory(string label, bool readerFirst)
        {
            MemoryStream ms = new MemoryStream();
            StreamWriter w = new StreamWriter(ms);
            w.WriteLine("hello");
            w.WriteLine("world");
            w.Flush();
            ms.Position = 0;
            if (readerFirst)
            {
                StreamReader sr = new StreamReader(ms);
                BinaryReader br = new BinaryReader(ms);
                int v = br.ReadInt32();
                Dump(label, v, sr.ReadToEnd());
            }
            else
            {
                BinaryReader br = new BinaryReader(ms);
                int v = br.ReadInt32();
                StreamReader sr = new StreamReader(ms);
                Dump(label, v, sr.ReadToEnd());
            }
        }

        public static void Main()
        {
            ScenarioMemory("mem_srFirst", true);
            ScenarioMemory("mem_brFirst", false);
        }
    }
}
