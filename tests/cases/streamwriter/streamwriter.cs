//------------------------------------------------------------------------------
// streamwriter.cs -- StreamWriter DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C83, AFTER the stream domain is fully
// wired (streamcase/streamops/streamdeep2 cover the composition + timeout +
// ReadToEnd + Seek/SetLength surfaces; streamdeep2's S4 probes the
// StreamWriter(Stream) WriteLine chain).
//
// THIS case drives the StreamWriter-SPECIFIC WRITE / ENCODING / FLUSH faces
// to exhaustion:
//   1. WRITE FACE: the (Stream) ctor + WriteLine chain with byte-accurate
//      Length/Position verification, the (string path) ctor, blank-line /
//      multi-line accumulation, and content read-back (ReadLine chain to EOF).
//   2. ENCODING FACE: the default UTF-8 (no BOM) codec -- non-ASCII content
//      (CJK + a surrogate pair) written through WriteLine is verified against
//      Encoding.UTF8.GetBytes (the wired utf8 page) for the exact byte count,
//      and the underlying FileStream Read(byte[]) round-trip confirms the raw
//      bytes (UTF-8 encodes CJK as 3 bytes, the astral pair as 4; CRLF as
//      0x0D 0x0A) -- byte-exact vs net48.
//   3. FLUSH FACE: net48 StreamWriter BUFFERS internally (WriteLine does NOT
//      reach the file until Flush/Close -- fs.Length stays 0 pre-Flush), and
//      the runtime writes through directly; the byte-EXACT common ground is
//      the POST-Flush observation (Length/Position/read-back after Flush or
//      Close), so every Length/Position probe reads AFTER Flush/Close. Flush
//      idempotence (Flush twice, same Length) and Close auto-flush (data
//      visible to a fresh reader after Close) are also pinned. The pre-Flush
//      Length divergence is RECORDED, not probed (D-SW-2).
//   4. SINGLE-BYTE surface (D-STP-3): Stream.WriteByte / ReadByte round trip
//      (EOF -> -1), and MemoryStream WriteByte/ReadByte.
//
// Wired surface (MethodMap.StreamIO.cs -> streamio.cpp), all probes stay
// within it:
//   StreamWriter(Stream) / StreamWriter(string) ctor, WriteLine(string),
//   Flush/Close, FileStream.Write(byte[])/Read(byte[]), WriteByte/ReadByte,
//   Position/Length/CanWrite/CanRead, File.Delete/File.Exists,
//   Encoding.UTF8.GetBytes, StreamReader.ReadLine.
//
// RECORDED UNWIRED (NOT probed -- LOUD NotSupportedException, see GAPS.md):
//   * StreamWriter(string, Encoding) / (Stream, Encoding) -- encoding ctors
//     need extra icall variants (D-SW-1).
//   * StreamWriter.Write(string) / Write(char) / Write(char[]) /
//     WriteLine(int) -- the icall takes exactly (string) (D-SW-1).
//   * Dispose(bool) -- the runtime has only Close (no disposing flag).
//   * AutoFlush property -- no runtime icall.
//
// net48-shape discipline (streamdeep2 precedent): only ASCII labels + bools
// / byte values are printed (never raw content) so the diff is
// console-encoding stable; the temp file is deleted at start AND end.
//------------------------------------------------------------------------------
using System;
using System.IO;
using System.Text;

namespace Cases { public static class StreamWriterCase {
    const string file = "streamwriter_tmp.bin";

    public static void Main()
    {
        File.Delete(file);

        // ============ 1. WRITE FACE: (Stream) ctor + WriteLine chain ============
        FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter sw = new StreamWriter(fs);
        Console.WriteLine("w_canwrite=" + fs.CanWrite);
        Console.WriteLine("w_canread=" + (fs.CanRead == false));
        sw.WriteLine("abc");
        sw.Flush();
        Console.WriteLine("w_len5=" + (fs.Length == 5));        // "abc" + CRLF
        Console.WriteLine("w_pos5=" + (fs.Position == 5));
        sw.WriteLine("de");
        sw.Flush();
        Console.WriteLine("w_len9=" + (fs.Length == 9));        // + "de" + CRLF
        Console.WriteLine("w_pos9=" + (fs.Position == 9));
        sw.WriteLine("");                                       // blank line
        sw.Flush();
        Console.WriteLine("w_len11=" + (fs.Length == 11));      // + CRLF only
        sw.Close();

        // read back: three lines + blank + EOF
        FileStream fr = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr = new StreamReader(fr);
        Console.WriteLine("r_abc=" + (sr.ReadLine() == "abc"));
        Console.WriteLine("r_de=" + (sr.ReadLine() == "de"));
        Console.WriteLine("r_blank=" + (sr.ReadLine() == ""));
        Console.WriteLine("r_eof=" + (sr.ReadLine() == null));
        Console.WriteLine("r_len11=" + (fr.Length == 11));
        sr.Close();

        // ============ 2. WRITE FACE: (string path) ctor ============
        File.Delete(file);
        StreamWriter swp = new StreamWriter(file);
        swp.WriteLine("alpha");
        swp.WriteLine("beta");
        swp.Flush();
        Console.WriteLine("p_exists=" + File.Exists(file));
        swp.Close();
        FileStream fpr = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader spr = new StreamReader(fpr);
        Console.WriteLine("p_alpha=" + (spr.ReadLine() == "alpha"));
        Console.WriteLine("p_beta=" + (spr.ReadLine() == "beta"));
        Console.WriteLine("p_eof=" + (spr.ReadLine() == null));
        spr.Close();

        // ============ 3. ENCODING FACE: UTF-8 no-BOM byte accuracy ============
        // "中" (U+4E2D) = E4 B8 AD, "文" (U+6587) = E6 96 87, the pair = 6 bytes;
        // CRLF = 0x0D 0x0A. GetBytes is the wired utf8 page -- the same codec
        // the runtime's write_line uses (streamio.cpp CsToUtf8).
        File.Delete(file);
        const string zh = "\u4E2D\u6587";
        FileStream fe = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter swe = new StreamWriter(fe);
        swe.WriteLine(zh);
        swe.Flush();
        byte[] enc = Encoding.UTF8.GetBytes(zh);
        Console.WriteLine("e_enc6=" + (enc.Length == 6));
        Console.WriteLine("e_len8=" + (fe.Length == enc.Length + 2));   // + CRLF
        swe.Close();

        // raw byte round-trip: the written bytes ARE the UTF-8 sequence
        FileStream fre = new FileStream(file, FileMode.Open, FileAccess.Read);
        byte[] raw = new byte[8];
        Console.WriteLine("e_read8=" + (fre.Read(raw, 0, 8) == 8));
        Console.WriteLine("e_b0=" + (raw[0] == 0xE4 && raw[1] == 0xB8 && raw[2] == 0xAD));
        Console.WriteLine("e_b3=" + (raw[3] == 0xE6 && raw[4] == 0x96 && raw[5] == 0x87));
        Console.WriteLine("e_crlf=" + (raw[6] == 0x0D && raw[7] == 0x0A));
        fre.Close();

        // surrogate pair: astral char = 4 UTF-8 bytes (F0 9F 98 80 for U+1F600)
        const string emoji = "\uD83D\uDE00";
        File.Delete(file);
        FileStream fsm = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter swm = new StreamWriter(fsm);
        swm.WriteLine(emoji);
        swm.Flush();
        byte[] em = Encoding.UTF8.GetBytes(emoji);
        Console.WriteLine("e_emoji4=" + (em.Length == 4));
        Console.WriteLine("e_emojilen=" + (fsm.Length == em.Length + 2));
        swm.Close();

        // ============ 4. FLUSH FACE: post-Flush observation + idempotence ============
        File.Delete(file);
        FileStream ff = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter sf = new StreamWriter(ff);
        sf.WriteLine("flush1");
        sf.Flush();
        long afterFirst = ff.Length;
        Console.WriteLine("f_len8=" + (afterFirst == 8));       // "flush1" + CRLF
        sf.Flush();                                             // idempotent
        Console.WriteLine("f_idem=" + (ff.Length == afterFirst));
        sf.Close();                                             // auto-flush
        // data visible to a fresh reader after Close (no explicit second flush)
        FileStream frr = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader srr = new StreamReader(frr);
        Console.WriteLine("f_visible=" + (srr.ReadLine() == "flush1"));
        srr.Close();

        // ============ 5. SINGLE-BYTE surface (D-STP-3) ============
        File.Delete(file);
        FileStream fb = new FileStream(file, FileMode.Create, FileAccess.ReadWrite);
        fb.WriteByte(0x00);
        fb.WriteByte(0x7F);
        fb.WriteByte(0xFF);
        fb.Flush();
        fb.Position = 0;
        Console.WriteLine("b_00=" + (fb.ReadByte() == 0x00));
        Console.WriteLine("b_7f=" + (fb.ReadByte() == 0x7F));
        Console.WriteLine("b_ff=" + (fb.ReadByte() == 0xFF));
        Console.WriteLine("b_eof=" + (fb.ReadByte() == -1));
        fb.Close();

        // MemoryStream single-byte
        MemoryStream ms = new MemoryStream();
        ms.WriteByte(10);
        ms.WriteByte(20);
        ms.Position = 0;
        Console.WriteLine("m_b10=" + (ms.ReadByte() == 10));
        Console.WriteLine("m_b20=" + (ms.ReadByte() == 20));
        Console.WriteLine("m_eof=" + (ms.ReadByte() == -1));
        ms.Close();

        // ============ 6. exception paths ============
        File.Delete(file);
        bool threw = false;
        try { new FileStream(file, FileMode.Open, FileAccess.Read); }
        catch (FileNotFoundException) { threw = true; }
        Console.WriteLine("ex_missing=" + threw);

        FileStream wfs = new FileStream(file, FileMode.Create, FileAccess.Write);
        byte[] b = new byte[2];
        threw = false;
        try { wfs.Read(b, 0, 2); }
        catch (NotSupportedException) { threw = true; }
        Console.WriteLine("ex_read_wo=" + threw);
        wfs.Close();

        File.Delete(file); // idempotent end
    }
}}
