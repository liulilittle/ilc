// ioaudit6.cs -- G6 static-audit suspicious point #6: StreamReader.ReadLine on
// a lone-CR terminator ("a\rb"). net48 treats '\r' as a line terminator; the
// runtime read_line only breaks on '\n' and strips a trailing '\r'
// (streamio.cpp il2cpp_stream_read_line).
//
// Deterministic output: bools only.
using System;
using System.IO;

namespace Cases { public static class IoAudit6 {
    public static void Main() {
        MemoryStream ms = new MemoryStream();
        ms.Write(new byte[] { 0x61, 0x0D, 0x62 }, 0, 3); // a CR b
        ms.Position = 0;
        StreamReader sr = new StreamReader(ms);
        string l1 = sr.ReadLine();
        string l2 = sr.ReadLine();
        string l3 = sr.ReadLine();
        Console.WriteLine("cr_l1_a=" + (l1 == "a"));
        Console.WriteLine("cr_l2_b=" + (l2 == "b"));
        Console.WriteLine("cr_l3_null=" + (l3 == null));
        sr.Close();
    }
}}
