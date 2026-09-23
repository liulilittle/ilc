using System;
using System.IO;
using System.Text;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + (v == null ? "<null>" : v)); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static string Hex(byte[] b) {
        const string H = "0123456789abcdef";
        var sb = new StringBuilder(b.Length * 2);
        for (int i = 0; i < b.Length; i++) { sb.Append(H[b[i] >> 4]); sb.Append(H[b[i] & 15]); }
        return sb.ToString();
    }
    static void EncApi() {
        byte[] u8 = Encoding.UTF8.GetBytes("h\u00e9\u4e2d");
        S("enc.utf8.getbytes.hex", Hex(u8));
        S("enc.utf8.getstring", Encoding.UTF8.GetString(u8));
        S("enc.ascii.getbytes.hex", Hex(Encoding.ASCII.GetBytes("h\u00e9")));
        S("enc.ascii.getstring", Encoding.ASCII.GetString(new byte[] { 0x68, 0xe9 }));
        S("enc.unicode.getbytes.hex", Hex(Encoding.Unicode.GetBytes("a\u4e2d")));
        S("enc.bigendian.getbytes.hex", Hex(Encoding.BigEndianUnicode.GetBytes("a\u4e2d")));
        S("enc.utf8.getchars.hex", Hex(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(new byte[] { 0x41, 0x42 }))));
        S("enc.utf8.supplementary", Hex(Encoding.UTF8.GetBytes("\uD83D\uDE00")));
        I("enc.utf8.empty", Encoding.UTF8.GetBytes("").Length);
    }
    static void MsApi() {
        var ms = new MemoryStream();
        I("ms.canread", ms.CanRead ? 1 : 0);
        I("ms.canseek", ms.CanSeek ? 1 : 0);
        I("ms.canwrite", ms.CanWrite ? 1 : 0);
        I("ms.cantimeout", ms.CanTimeout ? 1 : 0);
        I("ms.len.initial", (int)ms.Length);
        I("ms.pos.initial", (int)ms.Position);
        ms.WriteByte(0x41);
        ms.Write(new byte[] { 0x42, 0x43 }, 0, 2);
        I("ms.len.afterwrite", (int)ms.Length);
        I("ms.pos.afterwrite", (int)ms.Position);
        S("ms.toarray.hex", Hex(ms.ToArray()));
        ms.Position = 1;
        I("ms.pos.set", (int)ms.Position);
        I("ms.readbyte", ms.ReadByte());
        var buf = new byte[4];
        int n = ms.Read(buf, 0, 4);
        I("ms.read.count", n);
        S("ms.read.hex", Hex(buf));
        I("ms.readbyte.eof", ms.ReadByte());
        long sk = ms.Seek(0, SeekOrigin.Begin);
        I("ms.seek.pos", (int)sk);
        ms.SetLength(2);
        I("ms.setlength.len", (int)ms.Length);
        S("ms.toarray.aftertruncate", Hex(ms.ToArray()));
        ms.Flush();
        I("ms.flush.ok", 1);
        ms.Dispose();
        I("ms.dispose.ok", 1);
    }
    public static void Main() {
        Console.WriteLine("-- shard io002 start --");
        EncApi();
        MsApi();
        Console.WriteLine("-- shard io002 end --");
    }
}}
