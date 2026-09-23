using System;
using System.Text;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static string Hex(byte[] b) {
        const string H = "0123456789abcdef";
        var sb = new StringBuilder(b.Length * 2);
        for (int i = 0; i < b.Length; i++) { sb.Append(H[b[i] >> 4]); sb.Append(H[b[i] & 15]); }
        return sb.ToString();
    }
    static void B64() {
        S("cnv.b64.empty", Convert.ToBase64String(new byte[0]));
        S("cnv.b64.1", Convert.ToBase64String(new byte[] { 0x66 }));
        S("cnv.b64.2", Convert.ToBase64String(new byte[] { 0x66, 0x6f }));
        S("cnv.b64.3", Convert.ToBase64String(new byte[] { 0x66, 0x6f, 0x6f }));
        S("cnv.b64.ff00807f", Convert.ToBase64String(new byte[] { 0xff, 0x00, 0x80, 0x7f }));
        S("cnv.unb64.ok", Hex(Convert.FromBase64String("Zm9v")));
        S("cnv.unb64.ws", Hex(Convert.FromBase64String(" Z m 9 v ")));
        try { S("cnv.unb64.bad", Hex(Convert.FromBase64String("!!!"))); }
        catch (FormatException) { X("cnv.unb64.bad", "FormatException"); }
    }
    static void BitConv() {
        S("bc.get.i32.neg2", Hex(BitConverter.GetBytes(-2)));
        S("bc.get.i32.max", Hex(BitConverter.GetBytes(int.MaxValue)));
        I("bc.to.i32.0", BitConverter.ToInt32(new byte[] { 0x01, 0x02, 0x03, 0x04 }, 0));
        I("bc.to.i32.off2", BitConverter.ToInt32(new byte[] { 0x00, 0x00, 0x01, 0x02, 0x03, 0x04 }, 2));
        try { I("bc.to.i32.negoff", BitConverter.ToInt32(new byte[] { 1, 2, 3, 4 }, -1)); }
        catch (ArgumentException) { X("bc.to.i32.negoff", "ArgumentException"); }
    }
    static void MathApi() {
        I("math.sign.neg", Math.Sign(-5));
        I("math.sign.zero", Math.Sign(0));
        I("math.sign.pos", Math.Sign(42));
        int rem; int quo = Math.DivRem(17, 5, out rem);
        I("math.divrem.q", quo);
        I("math.divrem.r", rem);
        I("math.bigmul.lo", (int)Math.BigMul(1000, 1000));
    }
    static void GuidApi() {
        var g = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        S("guid.str", g.ToString());
        I("guid.eq", (g == Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")) ? 1 : 0);
    }
    static void CharEnc() {
        I("char.isdigit7", Char.IsDigit('7') ? 1 : 0);
        I("char.isdigit.ar", Char.IsDigit('\u0660') ? 1 : 0);
        I("char.upinv_a", (Char.ToUpperInvariant('a') == 'A') ? 1 : 0);
        I("enc.utf8.bytecount", Encoding.UTF8.GetBytes("h\u00e9llo").Length);
        S("enc.utf8.getstring", Encoding.UTF8.GetString(new byte[] { 0x68, 0xc3, 0xa9 }));
    }
    static void SideEffects() {
        var src = new byte[] { 1, 2, 3, 4 }; var dst = new byte[4];
        Buffer.BlockCopy(src, 0, dst, 0, 4); dst[0] = 9;
        S("side.blockcopy", Hex(src) + "|" + Hex(dst));
    }
    public static void Main() {
        B64();
        BitConv();
        MathApi();
        GuidApi();
        CharEnc();
        SideEffects();
    }
}}
