// ioaudit7.cs -- G6 static-audit suspicious point #7: Append-mode FileStream on
// an EXISTING file. net48: opens at EOF (Position == old length) and rejects
// Seek/SetLength before the append start with IOException; the runtime opens
// "ab" at position 0 and allows both (streamio.cpp).
//
// Deterministic output: bool / long only; the file is deleted at the end.
using System;
using System.IO;

namespace Cases { public static class IoAudit7 {
    public static void Main() {
        const string f = "ioaudit7_file.txt";
        File.Delete(f);
        File.WriteAllText(f, "abc");

        FileStream fs = new FileStream(f, FileMode.Append, FileAccess.Write);
        Console.WriteLine("app_pos3=" + (fs.Position == 3));

        bool seekThrew = false;
        try { fs.Seek(0, SeekOrigin.Begin); } catch (IOException) { seekThrew = true; }
        Console.WriteLine("app_seek0_threw=" + seekThrew);

        bool setLenThrew = false;
        try { fs.SetLength(0); } catch (IOException) { setLenThrew = true; }
        Console.WriteLine("app_setlength0_threw=" + setLenThrew);

        Console.WriteLine("app_len_after=" + fs.Length);
        fs.Close();
        File.Delete(f);
    }
}}
