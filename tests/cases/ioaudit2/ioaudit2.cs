// ioaudit2.cs -- G6 static-audit suspicious point #2: the FileMode map
// (streamio.cpp StreamTranslateFileMode + the "r+b"->"w+b" fallback).
//
//   a) CreateNew on an EXISTING file  -> net48 IOException (file intact)
//   b) Truncate on a MISSING file     -> net48 FileNotFoundException
//   c) Open+Write on a MISSING file   -> net48 FileNotFoundException
//   d) OpenOrCreate+Read on a MISSING -> net48 creates an empty file
//
// Deterministic output: bools only; every stage cleans its file.
using System;
using System.IO;

namespace Cases { public static class IoAudit2 {
    public static void Main() {
        const string f = "ioaudit2_file.txt";
        File.Delete(f);

        // (a) CreateNew on an existing file
        File.WriteAllText(f, "ABCD");
        bool aThrew = false;
        try { FileStream fs = new FileStream(f, FileMode.CreateNew, FileAccess.Write); fs.Close(); }
        catch (IOException) { aThrew = true; }
        Console.WriteLine("a_createnew_threw=" + aThrew);
        Console.WriteLine("a_content_intact=" + (File.ReadAllText(f) == "ABCD"));
        File.Delete(f);

        // (b) Truncate on a missing file
        bool bThrew = false;
        try { FileStream fs = new FileStream(f, FileMode.Truncate, FileAccess.Write); fs.Close(); }
        catch (IOException) { bThrew = true; }
        Console.WriteLine("b_truncate_threw=" + bThrew);
        Console.WriteLine("b_file_created=" + File.Exists(f));
        File.Delete(f);

        // (c) Open+Write on a missing file
        bool cThrew = false;
        try { FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Write); fs.Close(); }
        catch (IOException) { cThrew = true; }
        Console.WriteLine("c_openwrite_threw=" + cThrew);
        Console.WriteLine("c_file_created=" + File.Exists(f));
        File.Delete(f);

        // (d) OpenOrCreate+Read on a missing file
        bool dThrew = false;
        try { FileStream fs = new FileStream(f, FileMode.OpenOrCreate, FileAccess.Read); fs.Close(); }
        catch (IOException) { dThrew = true; }
        Console.WriteLine("d_openorcreate_threw=" + dThrew);
        Console.WriteLine("d_file_created=" + File.Exists(f));
        File.Delete(f);
    }
}}
