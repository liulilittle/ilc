using System;
using System.Text;
namespace Cases { public static class Prog {
    static string Hex(byte[] b) {
        const string H = "0123456789abcdef";
        var sb = new StringBuilder(b.Length * 2);
        for (int i = 0; i < b.Length; i++) { sb.Append(H[b[i] >> 4]); sb.Append(H[b[i] & 15]); }
        return sb.ToString();
    }
    public static void Main() {
        try { Console.WriteLine("[bc.to.i32.negoff] " + BitConverter.ToInt32(new byte[] { 1, 2, 3, 4 }, -1)); }
        catch (ArgumentException) { Console.WriteLine("[bc.to.i32.negoff] EX ArgumentException"); }
        Console.WriteLine("[bc.to.i32.off2] " + BitConverter.ToInt32(new byte[] { 0x00, 0x00, 0x01, 0x02, 0x03, 0x04 }, 2));
        Console.WriteLine("[char.isdigit.ar] " + (Char.IsDigit('\u0660') ? 1 : 0));
        Console.WriteLine("[probe.extra.hex] " + Hex(new byte[] { 0xde, 0xad }));
    }
}}
