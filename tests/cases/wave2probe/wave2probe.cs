// wave2probe -- WAVE-2 e2e probe (SCRATCH-ONLY, non-pool): net48 byte-EQ
// coverage for the three newly wired members. Points chosen so the runtime
// M3 conventions (truncated read = 0, write-after-close = no-op) are NOT
// exercised -- those divergences are documented in WAVE2-PLAN.md §6.
//   1. BinaryReader.ReadByte: raw 0xAB -> 171 and 0x02 -> 2 (NOT normalized);
//      ReadBoolean on 0x01 -> True (normalization preserved).
//   2. StreamWriter/TextWriter.Write(string): no line terminator, null is a
//      no-op (mixed with WriteLine to pin the byte order).
//   3. FileStream(string, FileMode) 2-arg: access defaults (Create/Open/
//      OpenOrCreate/Truncate -> ReadWrite; Append -> Write-only), length
//      behavior, missing file -> FileNotFoundException.
using System;
using System.IO;

namespace Cases
{
    public static class Wave2Probe
    {
        // Byte values as a decimal list ("5,65,66,...") -- byte.ToString(format)
        // is not wired in the transpiler face; (int) casts are.
        private static string Ints(byte[] b)
        {
            string s = "" + b.Length;
            for (int i = 0; i < b.Length; i++) s = s + "," + (int)b[i];
            return s;
        }

        private static void ReadBytePoints()
        {
            MemoryStream ms = new MemoryStream();
            ms.WriteByte(0xAB);
            ms.WriteByte(0x02);
            ms.WriteByte(0x01);
            ms.Position = 0;
            BinaryReader br = new BinaryReader(ms);
            Console.WriteLine("readbyte=" + br.ReadByte());
            BinaryReader br2 = new BinaryReader(ms);
            Console.WriteLine("readbyte2=" + br2.ReadByte());
            BinaryReader br3 = new BinaryReader(ms);
            Console.WriteLine("readbool=" + (br3.ReadBoolean() ? 1 : 0));
        }

        private static void WritePoints()
        {
            MemoryStream ms = new MemoryStream();
            StreamWriter w = new StreamWriter(ms);
            w.Write("AB");
            w.Write((string)null);
            w.WriteLine("C");
            w.Flush();
            Console.WriteLine("write=" + Ints(ms.ToArray()));
            w.Close();

            MemoryStream m2 = new MemoryStream();
            TextWriter tw = new StreamWriter(m2);
            tw.Write("Q");
            tw.Flush();
            Console.WriteLine("tw_base_write=" + Ints(m2.ToArray()));
            tw.Close();
        }

        private static void Fs2Body(int label, int modeValue)
        {
            string p = "w2probe_tmp/m_" + label.ToString() + ".txt";
            File.WriteAllText(p, "seed");
            FileStream fs = null;
            if (modeValue == 2)
            {
                fs = new FileStream(p, FileMode.Create);
            }
            else if (modeValue == 3)
            {
                fs = new FileStream(p, FileMode.Open);
            }
            else if (modeValue == 4)
            {
                fs = new FileStream(p, FileMode.OpenOrCreate);
            }
            else if (modeValue == 5)
            {
                fs = new FileStream(p, FileMode.Truncate);
            }
            else
            {
                fs = new FileStream(p, FileMode.Append);
            }
            Console.WriteLine("fs2_" + label.ToString() + "="
                + (fs.CanRead ? 1 : 0) + "," + (fs.CanWrite ? 1 : 0) + "," + fs.Length);
            fs.Close();
        }

        private static void FileStreamTwoArgPoints()
        {
            Directory.CreateDirectory("w2probe_tmp");
            Fs2Body(2, 2);
            Fs2Body(3, 3);
            Fs2Body(4, 4);
            Fs2Body(5, 5);
            Fs2Body(6, 6);
            try
            {
                FileStream missing = new FileStream("w2probe_tmp/missing.txt", FileMode.Open);
                Console.WriteLine("fs2_missing=none");
                missing.Close();
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("fs2_missing=FileNotFoundException");
            }
            catch (IOException)
            {
                Console.WriteLine("fs2_missing=IOException");
            }
        }

        public static void Main()
        {
            ReadBytePoints();
            WritePoints();
            FileStreamTwoArgPoints();
        }
    }
}
