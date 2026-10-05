//------------------------------------------------------------------------------
// streamdeep2.cs -- System.IO stream DOMAIN deep probe 2 (e2e batch C45):
// read/write byte[] round-trip, Position seeking, Length, partial reads, and
// the MemoryStream / StreamReader/StreamWriter composition faces -- byte-exact
// vs the net48 oracle.
//
// Wired expression surface exercised (MethodMap.StreamIO.cs, streamio.h):
//   FileStream(string, FileMode, FileAccess) ctor
//   StreamWriter(Stream) / StreamReader(Stream) ctor
//   MemoryStream() ctor + ToArray()
//   Write(byte[],int,int) / Read(byte[],int,int) -- managed-array icalls
//   get/set_Position, get_Length, get_CanRead, get_CanWrite
//   Flush / Close / WriteLine(string) / ReadLine()
//
// net48-shape discipline (streamops/streamcase precedent):
//   * Only ASCII lines + byte values are written; the diff prints bools and
//     positions/lengths, never raw content -> console-encoding stable.
//   * The temp file is deleted at start AND end (idempotent re-runs).
//   * Bytes are compared via == on indexed elements (byte[] indexer) -- the
//     wired array surface; no LINQ.
//
// RECORDED WIRING GAPS (defects, NOT probed -- probing would fall through to
// the generated-name path and abort the C++ build):
//   * Seek(long, SeekOrigin) / get_CanSeek / SetLength(long) -- NO runtime
//     icall exists (grep-confirmed: streamio.cpp exposes only get/set_position,
//     get_length, can_read/can_write, timeouts, read_line/read_to_end) -- the
//     transpiler map REJECTS them LOUDLY (D-STO-1) rather than emitting a
//     mangled symbol.
//   * ReadByte() / WriteByte(byte) -- no single-byte icall (LOUD).
//   * Dispose(bool) / StreamWriter(string, Encoding) / FileStream(string,
//     FileMode) 2-arg ctor -- LOUD.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class StreamDeep2 {
    const string file = "streamdeep2_tmp.bin";

    public static void Main()
    {
        File.Delete(file);

        // ================= S1: write chain + Position/Length =================
        FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write);
        Console.WriteLine("w_canwrite=" + fs.CanWrite);
        Console.WriteLine("w_canread=" + (fs.CanRead == false));
        byte[] w1 = { 1, 2, 3, 4, 5, 6, 7, 8 };
        fs.Write(w1, 0, 8);
        Console.WriteLine("w_pos8=" + (fs.Position == 8));
        Console.WriteLine("w_len8=" + (fs.Length == 8));
        fs.Position = 3;                          // seek within the stream
        Console.WriteLine("w_seek3=" + (fs.Position == 3));
        byte[] w2 = { 9, 9 };
        fs.Write(w2, 0, 2);                       // overwrite bytes 3..4
        Console.WriteLine("w_pos5=" + (fs.Position == 5));
        fs.Flush();
        fs.Close();

        // ================= S2: read chain + partial reads + seek =================
        FileStream fr = new FileStream(file, FileMode.Open, FileAccess.Read);
        Console.WriteLine("r_canread=" + fr.CanRead);
        Console.WriteLine("r_canwrite=" + (fr.CanWrite == false));
        Console.WriteLine("r_len8=" + (fr.Length == 8));
        byte[] rb = new byte[8];
        Console.WriteLine("r_read8=" + (fr.Read(rb, 0, 8) == 8));
        // file bytes after overwrite at pos 3 with {9,9}: [1,2,3,9,9,6,7,8]
        Console.WriteLine("r_b0=" + (rb[0] == 1 && rb[1] == 2 && rb[2] == 3 && rb[3] == 9 && rb[4] == 9 && rb[7] == 8));
        Console.WriteLine("r_pos8=" + (fr.Position == 8));
        // partial read after seek: pos 1..3 -> bytes [2,3,9]
        fr.Position = 1;
        byte[] small = new byte[3];
        Console.WriteLine("r_small3=" + (fr.Read(small, 0, 3) == 3));
        Console.WriteLine("r_small=" + (small[0] == 2 && small[1] == 3 && small[2] == 9));
        Console.WriteLine("r_pos4=" + (fr.Position == 4));
        fr.Close();

        // ================= S3: MemoryStream round-trip =================
        MemoryStream ms = new MemoryStream();
        Console.WriteLine("m_rw=" + (ms.CanRead && ms.CanWrite));
        ms.Write(new byte[] { 10, 20, 30 }, 0, 3);
        ms.Position = 0;
        byte[] m = new byte[3];
        Console.WriteLine("m_read3=" + (ms.Read(m, 0, 3) == 3));
        Console.WriteLine("m_data=" + (m[0] == 10 && m[1] == 20 && m[2] == 30));
        Console.WriteLine("m_pos3=" + (ms.Position == 3));
        ms.Write(new byte[] { 40 }, 0, 1);        // extend past previous end
        Console.WriteLine("m_len4=" + (ms.Length == 4));
        byte[] arr = ms.ToArray();
        Console.WriteLine("m_arr4=" + (arr.Length == 4 && arr[0] == 10 && arr[3] == 40));
        ms.Position = 2;
        byte[] tail = new byte[3];
        Console.WriteLine("m_tail2=" + (ms.Read(tail, 0, 3) == 2));
        Console.WriteLine("m_tail=" + (tail[0] == 30 && tail[1] == 40));
        ms.Close();

        // ================= S4: reader/writer composition (blank line + EOF) =================
        FileStream fw2 = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter sw = new StreamWriter(fw2);
        sw.WriteLine("alpha");
        sw.WriteLine("");
        sw.WriteLine("beta");
        sw.Flush();
        sw.Close();
        FileStream fr2 = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr = new StreamReader(fr2);
        Console.WriteLine("rl_alpha=" + (sr.ReadLine() == "alpha"));
        Console.WriteLine("rl_blank=" + (sr.ReadLine() == ""));
        Console.WriteLine("rl_beta=" + (sr.ReadLine() == "beta"));
        Console.WriteLine("rl_eof=" + (sr.ReadLine() == null));
        sr.Close();

        // ================= S5: exception paths =================
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

        // ================= S6: timeout surface (D-STO-1) =================
        // FileStream/MemoryStream never support timeouts: CanTimeout is false,
        // and the ReadTimeout / WriteTimeout accessors (get AND set) throw
        // InvalidOperationException (net48 probe-verified). The runtime icalls
        // icall_stream_get_can_timeout / {get,set}_{read,write}_timeout were
        // wired in the transpiler map (D-STO-1) so this shape is now e2e-
        // reachable.
        FileStream tfs = new FileStream(file, FileMode.Create, FileAccess.ReadWrite);
        Console.WriteLine("t_cantimeout=" + (tfs.CanTimeout == false));
        threw = false;
        try { int rt = tfs.ReadTimeout; }
        catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("t_rt_get=" + threw);
        threw = false;
        try { tfs.ReadTimeout = 5; }
        catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("t_rt_set=" + threw);
        threw = false;
        try { int wt = tfs.WriteTimeout; }
        catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("t_wt_get=" + threw);
        threw = false;
        try { tfs.WriteTimeout = 5; }
        catch (InvalidOperationException) { threw = true; }
        Console.WriteLine("t_wt_set=" + threw);
        tfs.Close();

        // ================= S7: ReadToEnd (D-STO-1) =================
        // StreamReader.ReadToEnd(): the remainder from the current position to
        // EOF as one string ("" at EOF, NOT null -- net48). The runtime icall
        // icall_stream_read_to_end was wired in the transpiler map (D-STO-1).
        FileStream fte = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter swte = new StreamWriter(fte);
        swte.WriteLine("gamma");
        swte.WriteLine("delta");
        swte.Flush();
        swte.Close();
        FileStream fre = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader srte = new StreamReader(fre);
        Console.WriteLine("rte_line=" + (srte.ReadLine() == "gamma"));
        Console.WriteLine("rte_rest=" + (srte.ReadToEnd() == "delta" + Environment.NewLine));
        Console.WriteLine("rte_eof=" + (srte.ReadToEnd() == ""));
        srte.Close();

        File.Delete(file); // idempotent end
    }
}}
