//------------------------------------------------------------------------------
// streamloud.cs -- the statically-analyzable StreamWriter Write/WriteLine value
// family + BinaryReader/Writer.BaseStream (net48 oracle, byte-for-byte).
//
// Every overload the M5 resolver used to reject LOUD (MethodMap.StreamIO.cs
// WriteLine/Write non-string arms, MethodMap.BinaryIO.cs get_BaseStream):
//   * WriteLine() / WriteLine(bool|char|int|uint|long|ulong|float|double|
//     decimal|string|object|char[]|char[]-range) and the composite
//     WriteLine(string, object...) forms.
//   * Write(bool|char|int|uint|long|ulong|float|double|decimal|string|object|
//     char[]|char[]-range) and the composite Write(string, object...) forms.
//   * BinaryReader.BaseStream / BinaryWriter.BaseStream.
//
// The WriteLine family is read back line-by-line through a StreamReader (the
// terminator is the net48 CRLF); the Write family is compared byte-for-byte
// against the expected UTF-8 concatenation (no terminator). Only ASCII labels
// + bools are printed, so the diff is console-encoding stable.
//------------------------------------------------------------------------------
using System;
using System.IO;
using System.Text;

namespace Cases { public static class StreamLoud {
    const string file = "streamloud_tmp.bin";

    public static void Main()
    {
        File.Delete(file);

        // ============ 1. WriteLine value family (read back line-by-line) ========
        FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter sw = new StreamWriter(fs);
        sw.WriteLine();
        sw.WriteLine(true);
        sw.WriteLine(false);
        sw.WriteLine('x');
        sw.WriteLine(42);
        sw.WriteLine(42u);
        sw.WriteLine(42L);
        sw.WriteLine(42ul);
        sw.WriteLine(1.5f);
        sw.WriteLine(1.5d);
        sw.WriteLine(1.23456789012345m);
        sw.WriteLine("text");
        sw.WriteLine((object)"obj");
        sw.WriteLine((object)null);
        char[] cs = { 'a', 'b', 'c' };
        sw.WriteLine(cs);
        sw.WriteLine(cs, 1, 2);
        sw.WriteLine("v={0}", 5);
        sw.Flush();
        sw.Close();

        FileStream fr = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr = new StreamReader(fr);
        Console.WriteLine("wl0=" + (sr.ReadLine() == ""));
        Console.WriteLine("wl1=" + (sr.ReadLine() == "True"));
        Console.WriteLine("wl2=" + (sr.ReadLine() == "False"));
        Console.WriteLine("wl3=" + (sr.ReadLine() == "x"));
        Console.WriteLine("wl4=" + (sr.ReadLine() == "42"));
        Console.WriteLine("wl5=" + (sr.ReadLine() == "42"));
        Console.WriteLine("wl6=" + (sr.ReadLine() == "42"));
        Console.WriteLine("wl7=" + (sr.ReadLine() == "42"));
        Console.WriteLine("wl8=" + (sr.ReadLine() == "1.5"));
        Console.WriteLine("wl9=" + (sr.ReadLine() == "1.5"));
        Console.WriteLine("wl10=" + (sr.ReadLine() == "1.23456789012345"));
        Console.WriteLine("wl11=" + (sr.ReadLine() == "text"));
        Console.WriteLine("wl12=" + (sr.ReadLine() == "obj"));
        Console.WriteLine("wl13=" + (sr.ReadLine() == ""));
        Console.WriteLine("wl14=" + (sr.ReadLine() == "abc"));
        Console.WriteLine("wl15=" + (sr.ReadLine() == "bc"));
        Console.WriteLine("wl16=" + (sr.ReadLine() == "v=5"));
        Console.WriteLine("wl_eof=" + (sr.ReadLine() == null));
        sr.Close();

        // ============ 2. Write value family (byte-for-byte) =====================
        MemoryStream ms = new MemoryStream();
        StreamWriter mw = new StreamWriter(ms);
        mw.Write(true);
        mw.Write('|');
        mw.Write(false);
        mw.Write('y');
        mw.Write(7);
        mw.Write(7u);
        mw.Write(7L);
        mw.Write(7ul);
        mw.Write(2.5f);
        mw.Write(2.5d);
        mw.Write(2.5m);
        mw.Write("no-newline");
        mw.Write((object)"obj");
        mw.Write(cs);
        mw.Write(cs, 1, 2);
        mw.Write("{0}+{1}", 1, 2);
        mw.Flush();
        byte[] got = ms.ToArray();
        mw.Close();

        string exp = "True" + "|" + "False" + "y" + "7" + "7" + "7" + "7"
            + "2.5" + "2.5" + "2.5" + "no-newline" + "obj" + "abc" + "bc" + "1+2";
        byte[] want = Encoding.UTF8.GetBytes(exp);
        bool same = got.Length == want.Length;
        for (int i = 0; same && i < got.Length; i++) { if (got[i] != want[i]) { same = false; } }
        Console.WriteLine("w_bytes=" + (got.Length == want.Length));
        Console.WriteLine("w_same=" + same);

        // ============ 3. BinaryReader/Writer.BaseStream =========================
        // net48 ownership: closing the wrapper closes the shared stream, so the
        // reader gets a FRESH MemoryStream seeded with the writer's bytes.
        MemoryStream bms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(bms);
        Console.WriteLine("bw_bs_canwrite=" + bw.BaseStream.CanWrite);
        bw.Write(123);
        bw.Flush();
        Console.WriteLine("bw_bs_len4=" + (bw.BaseStream.Length == 4));
        byte[] bdata = bms.ToArray();
        bw.Close();

        MemoryStream rms = new MemoryStream(bdata);
        BinaryReader br = new BinaryReader(rms);
        Console.WriteLine("br_bs_canread=" + br.BaseStream.CanRead);
        Console.WriteLine("br_read123=" + (br.ReadInt32() == 123));
        br.Close();

        // ============ 4. ctor arity arms ======================================
        // FileStream(string, FileMode, FileAccess, FileShare, int bufferSize)
        // and StreamReader(Stream, bool) -- the extra args are dropped (they
        // never change the bytes on this runtime).
        File.Delete(file);
        FileStream fs5 = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 4096);
        fs5.Close();
        Console.WriteLine("ctor5_exists=" + File.Exists(file));

        FileStream fsr5 = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 128);
        StreamReader sr5 = new StreamReader(fsr5, true);
        Console.WriteLine("ctor5_eof=" + (sr5.ReadLine() == null));
        sr5.Close();

        // StreamWriter(Stream, Encoding) -- the encoder is selected from the
        // encoding object (UTF-8 here: "enc" is 3 bytes, no BOM).
        MemoryStream ems = new MemoryStream();
        StreamWriter ew = new StreamWriter(ems, Encoding.UTF8);
        ew.Write("enc");
        ew.Flush();
        byte[] ebytes = ems.ToArray();
        ew.Close();
        Console.WriteLine("enc_len3=" + (ebytes.Length == 3));

        File.Delete(file);
    }
}}
