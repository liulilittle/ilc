//------------------------------------------------------------------------------
// encodingconvcase.cs -- System.Text.Encoding UTF-16BE (BigEndianUnicode) e2e
// oracle. The transpiler's encodingconv extension (il2cpp_encoding.h
// unicodebe page) wires Encoding.BigEndianUnicode.GetBytes/GetString. Every
// output is a deterministic bool; expected.txt is generated from a net48
// reference run.
//------------------------------------------------------------------------------

using System;
using System.Text;

class EncodingConvCase
{
    static void Main()
    {
        // ---- 1. BigEndianUnicode GetBytes: high byte first ----
        byte[] ab = Encoding.BigEndianUnicode.GetBytes("AB");
        Console.WriteLine(ab.Length == 4);
        Console.WriteLine(ab[0] == 0x00 && ab[1] == 0x41 && ab[2] == 0x00 && ab[3] == 0x42);

        // ---- 2. GetString round trip ----
        Console.WriteLine(Encoding.BigEndianUnicode.GetString(ab) == "AB");

        // ---- 3. Chinese (U+4E2D) ----
        byte[] zh = Encoding.BigEndianUnicode.GetBytes("\u4E2D");
        Console.WriteLine(zh.Length == 2);
        Console.WriteLine(zh[0] == 0x4E && zh[1] == 0x2D);
        Console.WriteLine(Encoding.BigEndianUnicode.GetString(zh) == "\u4E2D");

        // ---- 4. BigEndianUnicode vs Unicode (LE) byte order ----
        byte[] le = Encoding.Unicode.GetBytes("AB");
        Console.WriteLine(le[0] == 0x41 && le[1] == 0x00);
        Console.WriteLine(ab[0] == le[1] && ab[1] == le[0]);
    }
}
