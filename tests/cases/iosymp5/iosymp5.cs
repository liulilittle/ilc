// iosymp5 -- P2(5a) probe (NON-POOL, A4): StreamWriter(string, bool append)
// 2-arg path ctor. 3/4-arg (Encoding) forms need a runtime Encoding object
// model + arg2 type tracking -- tracked separately.
using System;
using System.IO;

internal static class P2StreamWriterAppend
{
    private static string Hex(byte[] b)
    {
        const string T = "0123456789abcdef";
        string s = "";
        for (int i = 0; i < b.Length; i++)
        {
            s = s + T[(b[i] >> 4) & 15];
            s = s + T[b[i] & 15];
        }
        return s;
    }

    private static string ReadHex(string path) { return Hex(File.ReadAllBytes(path)); }

    // m1: StreamWriter(string) -- 1-arg baseline (already wired).
    private static string M1()
    {
        string p = "w1.txt";
        File.WriteAllText(p, "OLD");
        using (StreamWriter w = new StreamWriter(p)) { w.Write("A"); }
        return "m1=" + ReadHex(p);
    }

    // m2: StreamWriter(string, false) -- truncate.
    private static string M2()
    {
        string p = "w2.txt";
        File.WriteAllText(p, "OLD");
        using (StreamWriter w = new StreamWriter(p, false)) { w.Write("A"); }
        return "m2=" + ReadHex(p);
    }

    // m3: StreamWriter(string, true) -- append.
    private static string M3()
    {
        string p = "w3.txt";
        File.WriteAllText(p, "OLD");
        using (StreamWriter w = new StreamWriter(p, true)) { w.Write("A"); }
        return "m3=" + ReadHex(p);
    }

    // m3b: append twice.
    private static string M3b()
    {
        string p = "w3b.txt";
        File.WriteAllText(p, "X");
        using (StreamWriter w = new StreamWriter(p, true)) { w.Write("A"); }
        using (StreamWriter w2 = new StreamWriter(p, true)) { w2.Write("B"); }
        return "m3b=" + ReadHex(p);
    }

    private static void Main()
    {
        try { Console.WriteLine(M1()); } catch (Exception ex) { Console.WriteLine("m1=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M2()); } catch (Exception ex) { Console.WriteLine("m2=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M3()); } catch (Exception ex) { Console.WriteLine("m3=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M3b()); } catch (Exception ex) { Console.WriteLine("m3b=EX:" + ex.GetType().Name); }
    }
}
