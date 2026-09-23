// ioaudit3.cs -- G6 static-audit suspicious point #3: Encoding.ASCII.GetString
// of high bytes. net48 (MS docs): "Any byte greater than hexadecimal 0x7F is
// decoded as the Unicode question mark ('?')". The runtime decodes them to
// U+FFFD (encoding.cpp ascii branches).
//
// Deterministic output: bool + int code units, no raw chars.
using System;
using System.Text;

namespace Cases { public static class IoAudit3 {
    public static void Main() {
        string s = Encoding.ASCII.GetString(new byte[] { 0x80, 0xFF });
        Console.WriteLine("ascii_qm=" + (s == "??"));
        Console.WriteLine("ascii_len=" + s.Length);
        Console.WriteLine("ascii_c0=" + (int)s[0]);
        Console.WriteLine("ascii_c1=" + (int)s[1]);
    }
}}
