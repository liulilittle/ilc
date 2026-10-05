// iosymp4 -- P2(5) probe (NON-POOL, A4): StreamWriter path ctors --
// (string,bool append) / (string,bool,Encoding) / (string,bool,Encoding,int).
// Each API in its OWN method + try/catch so a mapping gap cannot mask others.
// Deterministic key=value output; byte results as lowercase hex (manual
// nibble table: byte.ToString(format) is not transpilable).
using System;
using System.IO;
using System.Text;

internal static class P2StreamWriterEncoding
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

    private static string ReadHex(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        return Hex(b);
    }

    // m1: StreamWriter(string) -- baseline (already wired).
    private static string M1_path()
    {
        string p = "w1.txt";
        using (StreamWriter w = new StreamWriter(p)) { w.Write("A"); }
        return "m1=" + ReadHex(p);
    }

    // m2: StreamWriter(string, bool append=false) -- 2-arg create.
    private static string M2_path_false()
    {
        string p = "w2.txt";
        File.WriteAllText(p, "OLD");
        using (StreamWriter w = new StreamWriter(p, false)) { w.Write("A"); }
        return "m2=" + ReadHex(p);
    }

    // m3: StreamWriter(string, bool append=true) -- 2-arg append.
    private static string M3_path_true()
    {
        string p = "w3.txt";
        File.WriteAllText(p, "OLD");
        using (StreamWriter w = new StreamWriter(p, true)) { w.Write("A"); }
        return "m3=" + ReadHex(p);
    }

    // m4: 3-arg, UTF8Encoding(false) -- no BOM.
    private static string M4_enc_nobom()
    {
        string p = "w4.txt";
        using (StreamWriter w = new StreamWriter(p, false, new UTF8Encoding(false))) { w.Write("A"); }
        return "m4=" + ReadHex(p);
    }

    // m5: 3-arg, UTF8Encoding(true) -- BOM expected.
    private static string M5_enc_bom()
    {
        string p = "w5.txt";
        using (StreamWriter w = new StreamWriter(p, false, new UTF8Encoding(true))) { w.Write("A"); }
        return "m5=" + ReadHex(p);
    }

    // m6: 4-arg -- bufferSize must not change bytes.
    private static string M6_enc_bufsize()
    {
        string p = "w6.txt";
        using (StreamWriter w = new StreamWriter(p, false, new UTF8Encoding(false), 512)) { w.Write("A"); }
        return "m6=" + ReadHex(p);
    }

    // m7: append + encoding.
    private static string M7_append_enc()
    {
        string p = "w7.txt";
        File.WriteAllText(p, "OLD");
        using (StreamWriter w = new StreamWriter(p, true, new UTF8Encoding(false))) { w.Write("A"); }
        return "m7=" + ReadHex(p);
    }

    // m8: Encoding.Unicode (UTF-16LE + BOM).
    private static string M8_unicode()
    {
        string p = "w8.txt";
        using (StreamWriter w = new StreamWriter(p, false, Encoding.Unicode)) { w.Write("A"); }
        return "m8=" + ReadHex(p);
    }

    // m9: Encoding.ASCII.
    private static string M9_ascii()
    {
        string p = "w9.txt";
        using (StreamWriter w = new StreamWriter(p, false, Encoding.ASCII)) { w.Write("A"); }
        return "m9=" + ReadHex(p);
    }

    private static void Main()
    {
        try { Console.WriteLine(M1_path()); } catch (Exception ex) { Console.WriteLine("m1=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M2_path_false()); } catch (Exception ex) { Console.WriteLine("m2=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M3_path_true()); } catch (Exception ex) { Console.WriteLine("m3=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M4_enc_nobom()); } catch (Exception ex) { Console.WriteLine("m4=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M5_enc_bom()); } catch (Exception ex) { Console.WriteLine("m5=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M6_enc_bufsize()); } catch (Exception ex) { Console.WriteLine("m6=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M7_append_enc()); } catch (Exception ex) { Console.WriteLine("m7=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M8_unicode()); } catch (Exception ex) { Console.WriteLine("m8=EX:" + ex.GetType().Name); }
        try { Console.WriteLine(M9_ascii()); } catch (Exception ex) { Console.WriteLine("m9=EX:" + ex.GetType().Name); }
    }
}
