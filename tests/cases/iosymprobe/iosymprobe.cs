// iosymprobe v2 -- I/O symmetry probe (NON-POOL, A4 2026-09-19).
// Two goals:
//   (1) WIRED-CORE symmetry: round-trips using only mapped APIs, net48 vs ILC byte-compared.
//   (2) GAP ISOLATION: one method per unwired overload/API, so a single missing mapping does not
//       abort the whole program; our side prints "EX" (unhandled/not-transpiled) where net48 runs.
// Deterministic output: key=value lines, hex = lowercase UTF8 of value bytes (no byte.ToString(format),
// which is not transpiled).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal static class IoSymProbe
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string HexDigits = "0123456789abcdef";

    private static string Hex(byte[] b, int n)
    {
        StringBuilder sb = new StringBuilder(n * 2);
        for (int i = 0; i < n; i++)
        {
            sb.Append(HexDigits[(b[i] >> 4) & 0x0F]);
            sb.Append(HexDigits[b[i] & 0x0F]);
        }
        return sb.ToString();
    }
    private static string Hex(byte[] b) { return Hex(b, b.Length); }
    private static string HexOf(string s) { return Hex(Encoding.UTF8.GetBytes(s)); }
    private static void W(string k, string v) { Console.WriteLine(k + "=" + v); }
    private static string F(double v) { return v.ToString("R", Inv); }
    private static string Join(List<string> items, string sep)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(sep);
            sb.Append(items[i]);
        }
        return sb.ToString();
    }

    // File.ReadAllBytes/WriteAllBytes are NOT wired (link error) -> manual FileStream IO helpers.
    // NOTE: MemoryStream.ToArray() also breaks the C++ build (returns Il2CppObject* where
    // Il2CppArray* is required) -> read back via Position/Read instead.
    private static void WriteFile(string path, byte[] data)
    {
        FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(data, 0, data.Length);
        fs.Close();
    }
    private static string ReadFileHex(string path)
    {
        FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        byte[] buf = new byte[256];
        int n = fs.Read(buf, 0, 256);
        fs.Close();
        return Hex(buf, n);
    }
    private static string MsHex(MemoryStream ms)
    {
        ms.Position = 0;
        byte[] b = new byte[(int)ms.Length];
        int n = ms.Read(b, 0, b.Length);
        return Hex(b, n);
    }

    // ---------------- wired core ----------------

    private static void M1_ms_write()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write(0x01020304);
        bw.Write(-1234567890123L);
        bw.Write(4000000000u);
        bw.Write(1.5d);
        bw.Write(2.5f);
        bw.Write(true);
        bw.Write("h\u00e9llo");
        bw.Flush();
        W("m1_bytes_hex", MsHex(ms));
        W("m1_pos_len", ms.Position.ToString(Inv) + "/" + ms.Length.ToString(Inv));
        bw.Close();
    }

    private static void M2_ms_read()
    {
        byte[] payload = new byte[] { 4, 3, 2, 1, 255, 255, 255, 255, 6, 5, 4, 3, 2, 1, 0, 0 };
        MemoryStream ms = new MemoryStream(payload);
        BinaryReader br = new BinaryReader(ms);
        W("m2_rt", br.ReadInt32().ToString(Inv) + "," + br.ReadUInt32().ToString(Inv));
        br.Close();
        MemoryStream ms2 = new MemoryStream(new byte[] { 0xAB, 0x00, 0x02 });
        BinaryReader br2 = new BinaryReader(ms2);
        W("m2_bytebool", br2.ReadByte().ToString(Inv) + "," + br2.ReadByte().ToString(Inv) + "," + br2.ReadBoolean().ToString());
        br2.Close();
    }

    private static void M3_ms_seek()
    {
        MemoryStream ms = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        ms.Seek(3, SeekOrigin.Begin);
        W("m3_seek3", ms.ReadByte().ToString(Inv) + ",pos=" + ms.Position.ToString(Inv));
        ms.Seek(-2, SeekOrigin.End);
        W("m3_seekend", ms.ReadByte().ToString(Inv) + ",pos=" + ms.Position.ToString(Inv));
        W("m3_len_canseek", ms.Length.ToString(Inv) + "/" + ms.CanSeek.ToString());
        ms.Position = 0;
        byte[] buf = new byte[4];
        int n = ms.Read(buf, 0, 4);
        W("m3_read4", n.ToString(Inv) + ":" + Hex(buf, n));
        W("m3_read_eof", ms.CanRead.ToString() + "/" + ms.CanWrite.ToString());
    }

    private static void M4_ms_bytearray()
    {
        MemoryStream ms = new MemoryStream(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        byte[] all = new byte[8];
        int n = ms.Read(all, 0, 8);
        W("m4_bytearray", n.ToString(Inv) + ":" + Hex(all, n));
        W("m4_len", ms.Length.ToString(Inv));
    }

    private static void M5_fs_roundtrip()
    {
        string fp = "iosym_v2_fs.bin";
        byte[] all = new byte[64];
        for (int i = 0; i < 64; i++) all[i] = (byte)(i + 1);
        FileStream fs = new FileStream(fp, FileMode.Create, FileAccess.ReadWrite);
        fs.Write(all, 0, 64);
        fs.Flush();
        W("m5_pos_len", fs.Position.ToString(Inv) + "/" + fs.Length.ToString(Inv));
        fs.Seek(32, SeekOrigin.Begin);
        W("m5_seek32", fs.ReadByte().ToString(Inv) + ",pos=" + fs.Position.ToString(Inv));
        fs.Close();

        FileStream fs2 = new FileStream(fp, FileMode.Open, FileAccess.Read);
        byte[] rd = new byte[128];
        int k = fs2.Read(rd, 0, 128);
        W("m5_reopen", k.ToString(Inv) + ":" + Hex(rd, k));
        W("m5_read_eof", fs2.ReadByte().ToString(Inv));
        fs2.Close();
        File.Delete(fp);
    }

    private static void M6_fs_trunc()
    {
        string fp = "iosym_v2_fs2.bin";
        WriteFile(fp, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        FileStream fs = new FileStream(fp, FileMode.Open, FileAccess.ReadWrite);
        fs.SetLength(4);
        W("m6_setlen", fs.Length.ToString(Inv) + ",pos=" + fs.Position.ToString(Inv));
        fs.Close();
        FileStream fs2 = new FileStream(fp, FileMode.Open, FileAccess.Read);
        byte[] rd = new byte[16];
        int k = fs2.Read(rd, 0, 16);
        W("m6_after_trunc", k.ToString(Inv) + ":" + Hex(rd, k));
        fs2.Close();
        File.Delete(fp);
    }

    private static void M7_text_write()
    {
        string tp = "iosym_v2_t.txt";
        StreamWriter sw = new StreamWriter(tp);
        sw.Write("a\r\nb\nc");
        sw.WriteLine("x");
        sw.Write("\u03bc\u20ac");
        sw.Flush();
        sw.Close();
        W("m7_text_hex", ReadFileHex(tp));
        File.Delete(tp);
    }

    private static void M8_text_read()
    {
        string tp = "iosym_v2_t2.txt";
        WriteFile(tp, Encoding.UTF8.GetBytes("a\r\nb\nc"));
        StreamReader sr = new StreamReader(tp);
        List<string> seq = new List<string>();
        string l;
        while ((l = sr.ReadLine()) != null) seq.Add(HexOf(l));
        W("m8_readline", Join(seq, "|"));
        W("m8_eof_peek", sr.Peek().ToString(Inv));
        sr.Close();
        StreamReader sr2 = new StreamReader(tp);
        W("m8_readtoend", HexOf(sr2.ReadToEnd()));
        sr2.Close();
        File.Delete(tp);
    }

    private static void P1_peek()
    {
        StreamReader sr = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("ab")));
        W("p1_peek_seq", sr.Peek().ToString(Inv) + "," + sr.Peek().ToString(Inv) + "," +
            ((int)sr.Read()).ToString(Inv) + "," + sr.Peek().ToString(Inv));
        sr.Close();
    }

    private static void D1_rb_eof()
    {
        MemoryStream em = new MemoryStream(new byte[] { 1 });
        BinaryReader ebr = new BinaryReader(em);
        ebr.ReadByte();
        try { ebr.ReadByte(); W("d1_rb_eof", "noex"); }
        catch (Exception) { W("d1_rb_eof", "EX"); }
        W("d1_rb_eof_pos", em.Position.ToString(Inv));
        ebr.Close();
    }

    private static void D2_ms_read_eof()
    {
        MemoryStream em = new MemoryStream(new byte[] { 1 });
        byte[] b = new byte[4];
        int n = em.Read(b, 0, 4);
        W("d2_ms_read_eof", n.ToString(Inv) + ",pos=" + em.Position.ToString(Inv));
    }

    private static void D3_sr_closed()
    {
        string tp = "iosym_v2_d.txt";
        WriteFile(tp, Encoding.UTF8.GetBytes("abc"));
        StreamReader sr = new StreamReader(tp);
        sr.Close();
        try { sr.Read(); W("d3_sr_closed_read", "noex"); }
        catch (Exception) { W("d3_sr_closed_read", "EX"); }
        File.Delete(tp);
    }

    private static void D4_tw_after_close_owning()
    {
        string tp = "iosym_v2_d2.txt";
        StreamWriter sw = new StreamWriter(tp);
        sw.Write("hello");
        sw.Close();
        try { sw.Write("x"); W("d4_tw_owning_write", "noex"); }
        catch (Exception) { W("d4_tw_owning_write", "EX"); }
        try { sw.Flush(); W("d4_tw_owning_flush", "noex"); }
        catch (Exception) { W("d4_tw_owning_flush", "EX"); }
        try { File.Delete(tp); } catch { }
    }

    // ---------------- gap isolation (each in its own method) ----------------

    private static void G1_bw_byte()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write((byte)0xAB);
        bw.Close();
        W("g1_bw_byte_hex", MsHex(ms));
    }

    private static void G2_bw_sbyte()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write((sbyte)-5);
        bw.Close();
        W("g2_bw_sbyte_hex", MsHex(ms));
    }

    private static void G3_bw_int16()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write((short)-1234);
        bw.Close();
        W("g3_bw_int16_hex", MsHex(ms));
    }

    private static void G4_bw_uint16()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write((ushort)65535);
        bw.Close();
        W("g4_bw_uint16_hex", MsHex(ms));
    }

    private static void G5_bw_char()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write('\u00b5');
        bw.Close();
        W("g5_bw_char_hex", MsHex(ms));
    }

    private static void G6_bw_decimal()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write(123.456m);
        bw.Close();
        W("g6_bw_decimal_hex", MsHex(ms));
    }

    private static void G7_bw_bytearray()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms);
        bw.Write(new byte[] { 9, 8, 7 });
        bw.Close();
        W("g7_bw_bytearray_hex", MsHex(ms));
    }

    private static void G8_br_sbyte_int16()
    {
        MemoryStream ms = new MemoryStream(new byte[] { 0xFB, 0x2E, 0xFB, 0xFF });
        BinaryReader br = new BinaryReader(ms);
        W("g8_br_sbyte", br.ReadSByte().ToString(Inv));
        W("g8_br_int16", br.ReadInt16().ToString(Inv));
        br.Close();
    }

    private static void G9_br_decimal_char()
    {
        MemoryStream ms = new MemoryStream(new byte[] { 0x02, 0xBC });
        BinaryReader br = new BinaryReader(ms);
        W("g9_br_char", ((int)br.ReadChar()).ToString(Inv));
        br.Close();
    }

    private static void G10_ms_int_ctor()
    {
        MemoryStream ms = new MemoryStream(16);
        ms.WriteByte(1);
        W("g10_ms_int_ctor_len", ms.Length.ToString(Inv));
    }

    private static void G11_fs_4arg()
    {
        string fp = "iosym_v2_g11.bin";
        WriteFile(fp, new byte[] { 1, 2, 3, 4 });
        FileStream f = new FileStream(fp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        W("g11_fs4_len", f.Length.ToString(Inv));
        f.Close();
        try { File.Delete(fp); } catch { }
    }

    private static void G12_sw_3arg()
    {
        string tp = "iosym_v2_g12.txt";
        StreamWriter s = new StreamWriter(tp, false, new UTF8Encoding(false));
        s.Write("q");
        s.Close();
        W("g12_sw3_hex", ReadFileHex(tp));
        try { File.Delete(tp); } catch { }
    }

    private static void G13_sw_4arg()
    {
        string tp = "iosym_v2_g13.txt";
        StreamWriter s = new StreamWriter(tp, false, new UTF8Encoding(false), 1024);
        s.Write("q");
        s.Close();
        W("g13_sw4_hex", ReadFileHex(tp));
        try { File.Delete(tp); } catch { }
    }

    private static void G14_tw_base_write()
    {
        string tp = "iosym_v2_g14.txt";
        StreamWriter inner = new StreamWriter(tp);
        TextWriter tw = inner;
        tw.Write("Q");
        tw.Flush();
        tw.Close();
        W("g14_tw_base_hex", ReadFileHex(tp));
        try { File.Delete(tp); } catch { }
    }

    private static void G15_tr_base_read()
    {
        TextReader tr = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Z")));
        W("g15_tr_base_read", ((int)tr.Read()).ToString(Inv));
        tr.Close();
    }

    private static void G16_bw_encoding()
    {
        MemoryStream ms = new MemoryStream();
        BinaryWriter bw = new BinaryWriter(ms, Encoding.Unicode);
        bw.Write("A");
        bw.Close();
        W("g16_bw_unicode_hex", MsHex(ms));
    }

    private static void G17_ms_tobuffer()
    {
        MemoryStream ms = new MemoryStream();
        ms.WriteByte(0xDE);
        ms.WriteByte(0xAD);
        W("g17_tobuffer_hex", MsHex(ms));
        W("g17_capacity_ge", (ms.Capacity >= 2).ToString());
    }

    private static void Main()
    {
        try { M1_ms_write(); W("M1", "ok"); } catch (Exception) { W("M1", "EX"); }
        try { M2_ms_read(); W("M2", "ok"); } catch (Exception) { W("M2", "EX"); }
        try { M3_ms_seek(); W("M3", "ok"); } catch (Exception) { W("M3", "EX"); }
        try { M4_ms_bytearray(); W("M4", "ok"); } catch (Exception) { W("M4", "EX"); }
        try { M5_fs_roundtrip(); W("M5", "ok"); } catch (Exception) { W("M5", "EX"); }
        try { M6_fs_trunc(); W("M6", "ok"); } catch (Exception) { W("M6", "EX"); }
        try { M7_text_write(); W("M7", "ok"); } catch (Exception) { W("M7", "EX"); }
        try { M8_text_read(); W("M8", "ok"); } catch (Exception) { W("M8", "EX"); }
        try { P1_peek(); W("P1", "ok"); } catch (Exception) { W("P1", "EX"); }
        try { D1_rb_eof(); W("D1", "ok"); } catch (Exception) { W("D1", "EX"); }
        try { D2_ms_read_eof(); W("D2", "ok"); } catch (Exception) { W("D2", "EX"); }
        try { D3_sr_closed(); W("D3", "ok"); } catch (Exception) { W("D3", "EX"); }
        try { D4_tw_after_close_owning(); W("D4", "ok"); } catch (Exception) { W("D4", "EX"); }
        try { G1_bw_byte(); W("G1", "ok"); } catch (Exception) { W("G1", "EX"); }
        try { G2_bw_sbyte(); W("G2", "ok"); } catch (Exception) { W("G2", "EX"); }
        try { G3_bw_int16(); W("G3", "ok"); } catch (Exception) { W("G3", "EX"); }
        try { G4_bw_uint16(); W("G4", "ok"); } catch (Exception) { W("G4", "EX"); }
        try { G5_bw_char(); W("G5", "ok"); } catch (Exception) { W("G5", "EX"); }
        try { G6_bw_decimal(); W("G6", "ok"); } catch (Exception) { W("G6", "EX"); }
        try { G7_bw_bytearray(); W("G7", "ok"); } catch (Exception) { W("G7", "EX"); }
        try { G8_br_sbyte_int16(); W("G8", "ok"); } catch (Exception) { W("G8", "EX"); }
        try { G9_br_decimal_char(); W("G9", "ok"); } catch (Exception) { W("G9", "EX"); }
        try { G10_ms_int_ctor(); W("G10", "ok"); } catch (Exception) { W("G10", "EX"); }
        try { G11_fs_4arg(); W("G11", "ok"); } catch (Exception) { W("G11", "EX"); }
        try { G12_sw_3arg(); W("G12", "ok"); } catch (Exception) { W("G12", "EX"); }
        try { G13_sw_4arg(); W("G13", "ok"); } catch (Exception) { W("G13", "EX"); }
        try { G14_tw_base_write(); W("G14", "ok"); } catch (Exception) { W("G14", "EX"); }
        try { G15_tr_base_read(); W("G15", "ok"); } catch (Exception) { W("G15", "EX"); }
        try { G16_bw_encoding(); W("G16", "ok"); } catch (Exception) { W("G16", "EX"); }
        try { G17_ms_tobuffer(); W("G17", "ok"); } catch (Exception) { W("G17", "EX"); }
    }
}
