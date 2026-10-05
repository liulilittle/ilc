// ioaudit4.cs -- G6 static-audit suspicious point #4: BOM handling of
// File.ReadAllText / ReadAllLines / StreamReader. net48 detects and strips
// the UTF-8 BOM and switches to UTF-16 on a UTF-16 BOM; the runtime always
// decodes UTF-8 and never strips a BOM (io.cpp / streamio.cpp).
//
// Deterministic output: bool / int only; content is never echoed.
using System;
using System.IO;

namespace Cases { public static class IoAudit4 {
    static void WriteBytes(string path, byte[] data) {
        FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(data, 0, data.Length);
        fs.Close();
    }

    static string ReadFirstLine(string path) {
        FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        StreamReader sr = new StreamReader(fs);
        string line = sr.ReadLine();
        sr.Close();
        return line;
    }

    public static void Main() {
        const string f8 = "ioaudit4_bom8.txt";
        const string f16 = "ioaudit4_bom16.txt";
        File.Delete(f8);
        File.Delete(f16);

        // ---- UTF-8 BOM + "AB" ----
        WriteBytes(f8, new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0x42 });
        string t = File.ReadAllText(f8);
        Console.WriteLine("b8_text_ab=" + (t == "AB"));
        Console.WriteLine("b8_text_len=" + t.Length);
        Console.WriteLine("b8_text_c0=" + (t.Length > 0 ? (int)t[0] : -1));
        string[] l = File.ReadAllLines(f8);
        Console.WriteLine("b8_lines_n=" + l.Length);
        Console.WriteLine("b8_lines_ab=" + (l.Length == 1 && l[0] == "AB"));
        string sr = ReadFirstLine(f8);
        Console.WriteLine("b8_sr_ab=" + (sr == "AB"));
        Console.WriteLine("b8_sr_len=" + (sr == null ? -1 : sr.Length));

        // ---- UTF-16LE BOM + "AB" ----
        WriteBytes(f16, new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00 });
        string u = File.ReadAllText(f16);
        Console.WriteLine("b16_text_ab=" + (u == "AB"));
        Console.WriteLine("b16_text_len=" + u.Length);
        Console.WriteLine("b16_text_c0=" + (u.Length > 0 ? (int)u[0] : -1));
        string[] ul = File.ReadAllLines(f16);
        Console.WriteLine("b16_lines_n=" + ul.Length);
        Console.WriteLine("b16_lines_ab=" + (ul.Length == 1 && ul[0] == "AB"));
        string usr = ReadFirstLine(f16);
        Console.WriteLine("b16_sr_ab=" + (usr == "AB"));
        Console.WriteLine("b16_sr_len=" + (usr == null ? -1 : usr.Length));

        File.Delete(f8);
        File.Delete(f16);
    }
}}
