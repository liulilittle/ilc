// iosymp3 -- P2(4) probe (NON-POOL, A4): StreamReader(string path) + BOM detection (+ Peek isolated).
using System;
using System.IO;

internal static class P2StreamReaderPath
{
    private static void Main()
    {
        string p = "iosymp3.txt";
        File.WriteAllText(p, "l1\r\nl2\nl3");

        StreamReader sr = new StreamReader(p);
        Console.WriteLine("r1=" + sr.ReadLine());
        Console.WriteLine("r2=" + sr.ReadLine());
        Console.WriteLine("r3=" + sr.ReadLine());
        Console.WriteLine("r4null=" + (sr.ReadLine() == null));
        sr.Close();

        StreamReader sr2 = new StreamReader(p);
        string all = sr2.ReadToEnd();
        Console.WriteLine("rte=" + all.Replace("\r", "<CR>").Replace("\n", "<LF>"));
        sr2.Close();

        // Peek (W-02) is measured by the dedicated iosympw02 probe -- NOT here
        // (an unwired mapping skips the whole method).

        // UTF-8 BOM at the start: net48 skips it
        string pb = "iosymp3b.txt";
        File.WriteAllBytes(pb, new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0x0A, 0x42 });
        StreamReader sr3 = new StreamReader(pb);
        Console.WriteLine("bom1=" + sr3.ReadLine());
        Console.WriteLine("bom2=" + sr3.ReadLine());
        Console.WriteLine("bom3null=" + (sr3.ReadLine() == null));
        sr3.Close();

        // missing file: net48 throws FileNotFoundException
        try
        {
            StreamReader miss = new StreamReader("iosymp3_missing.txt");
            Console.WriteLine("missing=noex");
            miss.Close();
        }
        catch (Exception)
        {
            Console.WriteLine("missing=EX");
        }

        File.Delete(p);
        File.Delete(pb);
    }
}
