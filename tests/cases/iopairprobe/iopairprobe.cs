// iopairprobe v2 -- I/O pairing matrix probe (NON-POOL, A1 2026-09-19).
// Per-pair = write -> read round-trip; per STEP isolation so unwired Flush/Close/Read
// do not hide the write-bytes measurement. Deterministic key=value; hex = raw bytes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal static class IoPairProbe
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string HD = "0123456789abcdef";
    private static string Hex(byte[] b, int n)
    {
        StringBuilder sb = new StringBuilder(n * 2);
        for (int i = 0; i < n; i++) { sb.Append(HD[(b[i] >> 4) & 15]); sb.Append(HD[b[i] & 15]); }
        return sb.ToString();
    }
    private static string Hex(byte[] b) { return Hex(b, b.Length); }
    private static string HexOf(string s) { return Hex(Encoding.UTF8.GetBytes(s)); }
    private static void W(string k, string v) { Console.WriteLine(k + "=" + v); }
    private static string F(double v) { return v.ToString("R", Inv); }
    private static string MsHex(MemoryStream ms)
    {
        long p = ms.Position; ms.Position = 0;
        byte[] b = new byte[(int)ms.Length];
        int n = ms.Read(b, 0, b.Length);
        ms.Position = p;
        return Hex(b, n);
    }
    private static string Join(List<string> items, string sep)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < items.Count; i++) { if (i > 0) { sb.Append(sep); } sb.Append(items[i]); }
        return sb.ToString();
    }

    // generic per-pair runner is impossible without delegates; use explicit typed helpers.

    private static void B_bool()
    {
        string k = "b_bool";
        MemoryStream ms = null;
        try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(true); W(k + "_in", "1"); W(k + "_bytes", MsHex(ms)); }
        catch (Exception) { W(k + "_bytes", "EX"); }
        try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadBoolean().ToString()); } }
        catch (Exception) { W(k + "_rt", "EX"); }
    }
    private static void B_byte()
    {
        byte[] vs = new byte[] { 0, 1, 0xAB, 0xFF };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_byte_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadByte().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_sbyte()
    {
        sbyte[] vs = new sbyte[] { 0, -1, 1, sbyte.MinValue, sbyte.MaxValue };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_sbyte_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadSByte().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_char()
    {
        char[] vs = new char[] { 'A', '\u00b5', '\u20ac' };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_char_" + i.ToString(Inv); W(k + "_in", ((int)vs[i]).ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", ((int)br.ReadChar()).ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_int16()
    {
        short[] vs = new short[] { 0, 1, -1, short.MinValue, short.MaxValue, -1234 };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_int16_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadInt16().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_uint16()
    {
        ushort[] vs = new ushort[] { 0, 1, 0xFFFF, 0x1234 };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_uint16_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadUInt16().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_int32()
    {
        int[] vs = new int[] { 0, 1, -1, int.MinValue, int.MaxValue, 0x01020304, -123456789 };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_int32_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadInt32().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_uint32()
    {
        uint[] vs = new uint[] { 0u, 1u, uint.MaxValue, 4000000000u };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_uint32_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadUInt32().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_int64()
    {
        long[] vs = new long[] { 0L, 1L, -1L, long.MinValue, long.MaxValue, -1234567890123L };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_int64_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadInt64().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_uint64()
    {
        ulong[] vs = new ulong[] { 0UL, 1UL, ulong.MaxValue, 12345678901234567890UL };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_uint64_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadUInt64().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_single()
    {
        float[] vs = new float[] { 0f, 1.5f, -2.5f, float.MinValue, float.MaxValue, float.NaN };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_single_" + i.ToString(Inv); W(k + "_in", F(vs[i]));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", F(br.ReadSingle())); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_double()
    {
        double[] vs = new double[] { 0d, 1.5d, -2.5d, double.MinValue, double.MaxValue, double.NaN };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_double_" + i.ToString(Inv); W(k + "_in", F(vs[i]));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", F(br.ReadDouble())); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_decimal()
    {
        decimal[] vs = new decimal[] { 0m, 1m, -1m, 123.456m, decimal.MinValue, decimal.MaxValue };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_decimal_" + i.ToString(Inv); W(k + "_in", vs[i].ToString(Inv));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", br.ReadDecimal().ToString(Inv)); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }
    private static void B_string()
    {
        string[] vs = new string[] { "", "a", "h\u00e9llo", "\u00b5\u20ac", "\u4e2d\u6587" };
        for (int i = 0; i < vs.Length; i++)
        {
            string k = "b_string_" + i.ToString(Inv); W(k + "_in", HexOf(vs[i]));
            MemoryStream ms = null;
            try { ms = new MemoryStream(); BinaryWriter bw = new BinaryWriter(ms); bw.Write(vs[i]); W(k + "_bytes", MsHex(ms)); }
            catch (Exception) { W(k + "_bytes", "EX"); }
            try { if (ms == null) { W(k + "_rt", "SKIP"); } else { ms.Position = 0; BinaryReader br = new BinaryReader(ms); W(k + "_rt", HexOf(br.ReadString())); } }
            catch (Exception) { W(k + "_rt", "EX"); }
        }
    }

    // ---- TextIO ----
    private static string FileHex(string path)
    {
        FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        byte[] b = new byte[512];
        int n = fs.Read(b, 0, 512);
        fs.Close();
        return Hex(b, n);
    }
    private static void T_case(string tag, int mode)
    {
        string path = "iopair_t_" + tag + ".txt";
        bool wrote = false;
        try
        {
            StreamWriter sw;
            if (mode == 0) { sw = new StreamWriter(path); }
            else if (mode == 1) { sw = new StreamWriter(path, false, Encoding.UTF8); }
            else if (mode == 2) { sw = new StreamWriter(path, false, Encoding.Unicode); }
            else { sw = new StreamWriter(path, false, Encoding.ASCII); }
            sw.Write("a\r\nb\nc");
            sw.WriteLine("x");
            sw.Write("\u00b5\u20ac");
            sw.Flush();
            sw.Close();
            wrote = true;
            W("t_" + tag + "_bytes", FileHex(path));
        }
        catch (Exception) { W("t_" + tag + "_bytes", "EX"); }
        try
        {
            if (!wrote) { W("t_" + tag + "_lines", "SKIP"); }
            else { StreamReader sr = new StreamReader(path); List<string> seq = new List<string>(); string l; while ((l = sr.ReadLine()) != null) { seq.Add(HexOf(l)); } W("t_" + tag + "_lines", Join(seq, "|")); try { W("t_" + tag + "_peek", sr.Peek().ToString(Inv)); } catch (Exception) { W("t_" + tag + "_peek", "EX"); } sr.Close(); }
        }
        catch (Exception) { W("t_" + tag + "_lines", "EX"); }
        try
        {
            if (!wrote) { W("t_" + tag + "_rte", "SKIP"); }
            else { StreamReader sr2 = new StreamReader(path); W("t_" + tag + "_rte", HexOf(sr2.ReadToEnd())); sr2.Close(); }
        }
        catch (Exception) { W("t_" + tag + "_rte", "EX"); }
        try { if (wrote) { File.Delete(path); } } catch (Exception) { }
    }
    private static void T_utf8nobom() { T_case("utf8nobom", 0); }
    private static void T_utf8bom() { T_case("utf8bom", 1); }
    private static void T_unicode() { T_case("unicode", 2); }
    private static void T_ascii() { T_case("ascii", 3); }

    // ---- Stream ops ----
    private static void S_memory()
    {
        MemoryStream ms = null;
        try { ms = new MemoryStream(); W("s_ms_capacity0", ms.Capacity.ToString(Inv)); W("s_ms_len0", ms.Length.ToString(Inv)); W("s_ms_pos0", ms.Position.ToString(Inv)); }
        catch (Exception) { W("s_ms_ctor", "EX"); }
        if (ms == null) { W("s_ms_after_write", "SKIP"); return; }
        try { byte[] w = new byte[] { 1, 2, 3, 4, 5 }; ms.Write(w, 0, 5); W("s_ms_after_write", ms.Length.ToString(Inv) + "/" + ms.Position.ToString(Inv)); }
        catch (Exception) { W("s_ms_after_write", "EX"); }
        try { ms.Seek(0, SeekOrigin.Begin); W("s_ms_seek_begin_byte", ms.ReadByte().ToString(Inv) + ",pos=" + ms.Position.ToString(Inv)); } catch (Exception) { W("s_ms_seek_begin_byte", "EX"); }
        try { ms.Seek(-1, SeekOrigin.End); W("s_ms_seek_end_byte", ms.ReadByte().ToString(Inv) + ",pos=" + ms.Position.ToString(Inv)); } catch (Exception) { W("s_ms_seek_end_byte", "EX"); }
        try { ms.Seek(1, SeekOrigin.Current); W("s_ms_seek_cur_pos", ms.Position.ToString(Inv)); } catch (Exception) { W("s_ms_seek_cur_pos", "EX"); }
        try { ms.SetLength(2); W("s_ms_setlen", ms.Length.ToString(Inv) + "/" + ms.Position.ToString(Inv)); } catch (Exception) { W("s_ms_setlen", "EX"); }
        try { ms.Position = 0; byte[] b = new byte[8]; int n = ms.Read(b, 0, 8); W("s_ms_read_eos", n.ToString(Inv) + ":" + Hex(b, n)); } catch (Exception) { W("s_ms_read_eos", "EX"); }
        try { W("s_ms_readbyte_eos", ms.ReadByte().ToString(Inv)); } catch (Exception) { W("s_ms_readbyte_eos", "EX"); }
        try { W("s_ms_canseek", ms.CanSeek.ToString() + "/" + ms.CanRead.ToString() + "/" + ms.CanWrite.ToString()); } catch (Exception) { W("s_ms_canseek", "EX"); }
    }
    private static void S_memory_intctor()
    {
        try { MemoryStream ms = new MemoryStream(16); ms.WriteByte(0xDE); ms.WriteByte(0xAD); W("s_ms16_len", ms.Length.ToString(Inv)); W("s_ms16_cap_ge", (ms.Capacity >= 2).ToString()); }
        catch (Exception) { W("s_ms16_err", "EX"); }
    }
    private static void S_memory_negativeseek()
    {
        try { MemoryStream ms = new MemoryStream(new byte[] { 1, 2, 3 }); ms.Seek(-5, SeekOrigin.Begin); W("s_ms_negseek", "noex"); }
        catch (Exception) { W("s_ms_negseek", "EX"); }
    }
    private static void S_filestream()
    {
        string fp = "iopair_s_fs.bin";
        try
        {
            byte[] all = new byte[64];
            for (int i = 0; i < 64; i++) { all[i] = (byte)(i + 1); }
            FileStream fs = new FileStream(fp, FileMode.Create, FileAccess.ReadWrite);
            fs.Write(all, 0, 64); fs.Flush();
            W("s_fs_pos_len", fs.Position.ToString(Inv) + "/" + fs.Length.ToString(Inv));
            fs.Seek(32, SeekOrigin.Begin);
            W("s_fs_seek32_byte", fs.ReadByte().ToString(Inv) + ",pos=" + fs.Position.ToString(Inv));
            fs.Seek(-2, SeekOrigin.End);
            W("s_fs_seekend_byte", fs.ReadByte().ToString(Inv));
            fs.SetLength(16);
            W("s_fs_setlen", fs.Length.ToString(Inv));
            fs.Close();
            FileStream fs2 = new FileStream(fp, FileMode.Open, FileAccess.Read);
            byte[] rd = new byte[64]; int k = fs2.Read(rd, 0, 64);
            W("s_fs_reopen", k.ToString(Inv) + ":" + Hex(rd, k));
            W("s_fs_eof", fs2.ReadByte().ToString(Inv));
            fs2.Close();
            File.Delete(fp);
        }
        catch (Exception) { W("s_filestream_err", "EX"); }
    }

    private static void Main()
    {
        try { B_bool(); } catch (Exception) { W("B_bool", "EX"); }
        try { B_byte(); } catch (Exception) { W("B_byte", "EX"); }
        try { B_sbyte(); } catch (Exception) { W("B_sbyte", "EX"); }
        try { B_char(); } catch (Exception) { W("B_char", "EX"); }
        try { B_int16(); } catch (Exception) { W("B_int16", "EX"); }
        try { B_uint16(); } catch (Exception) { W("B_uint16", "EX"); }
        try { B_int32(); } catch (Exception) { W("B_int32", "EX"); }
        try { B_uint32(); } catch (Exception) { W("B_uint32", "EX"); }
        try { B_int64(); } catch (Exception) { W("B_int64", "EX"); }
        try { B_uint64(); } catch (Exception) { W("B_uint64", "EX"); }
        try { B_single(); } catch (Exception) { W("B_single", "EX"); }
        try { B_double(); } catch (Exception) { W("B_double", "EX"); }
        try { B_decimal(); } catch (Exception) { W("B_decimal", "EX"); }
        try { B_string(); } catch (Exception) { W("B_string", "EX"); }
        try { T_utf8nobom(); } catch (Exception) { W("T_utf8nobom", "EX"); }
        try { T_utf8bom(); } catch (Exception) { W("T_utf8bom", "EX"); }
        try { T_unicode(); } catch (Exception) { W("T_unicode", "EX"); }
        try { T_ascii(); } catch (Exception) { W("T_ascii", "EX"); }
        try { S_memory(); } catch (Exception) { W("S_memory", "EX"); }
        try { S_memory_intctor(); } catch (Exception) { W("S_memory_intctor", "EX"); }
        try { S_memory_negativeseek(); } catch (Exception) { W("S_memory_negativeseek", "EX"); }
        try { S_filestream(); } catch (Exception) { W("S_filestream", "EX"); }
    }
}
