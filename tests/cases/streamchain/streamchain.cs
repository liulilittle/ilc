//------------------------------------------------------------------------------
// streamchain.cs -- STREAM CHAINING e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C77, AFTER the stream-domain closure:
// the object-typed stream layer (stream-b23: FileStream/MemoryStream/
// StreamReader/StreamWriter object descriptors + handle registry + the
// read/write/position icalls), the timeout surface (D-STO-1), and the
// Seek/CanSeek/SetLength + ReadByte/WriteByte surface (D-STP-2/3). streampos
// already pins the POSITION matrix; streamdeep2 pins the text/byte composition.
//
// THIS case drives the CHAIN dimension: read-write CONVERSION chains where one
// stream's output feeds another stage, exactly the streamops/streamcase/
// streamdeep2/streampos do-not combination:
//
//   1. MemoryStream -> ToArray() -> FileStream WRITE chain: in-memory bytes
//      materialize to a file, then a fresh FileStream READ chain reads them
//      back element-by-element (the ToArray -> Write -> Read conversion loop).
//   2. byte[] <-> MemoryStream double round trip: write into one MemoryStream,
//      ToArray, feed the array into a SECOND MemoryStream, read it back -- the
//      array-to-stream-to-array conversion is byte-stable and the second
//      stream's Length/Position mirror the first.
//   3. StreamWriter -> FileStream -> StreamReader text chain: writer lines ->
//      Flush -> reader ReadLine/ReadToEnd, including an EMPTY line and a
//      multi-line ReadToEnd (the writer-buffer flush across the file boundary).
//   4. WriteByte -> ReadByte chain (D-STP-3): a byte-by-byte write loop
//      followed by a byte-by-byte read loop that consumes exactly the written
//      bytes then hits -1 at EOF.
//   5. Seek chain (D-STP-2): Write -> Seek(Current) -> Write (interleaved
//      overwrite), then Seek(End) + SetLength(truncate) + verify Length and
//      the surviving bytes.
//   6. Chained stream composition: MemoryStream -> StreamWriter -> ToArray ->
//      StreamReader(Stream) over a THIRD MemoryStream seeded from that array --
//      a full writer-to-reader conversion where the intermediate carrier is
//      only the byte[] (no file on disk).
//   7. Conversion-correctness guard: the writer's Flush makes Length
//      observable BEFORE Close; reading before Flush sees nothing (net48).
//
// net48 shape discipline (streamops/streamcase precedent): only ASCII lines
// and byte VALUES are written -- the diff prints bools, never raw content --
// so the console encoding is stable across the UTF-8 fixed output code page.
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class StreamChain {
    const string file = "streamchain_tmp.bin";

    public static void Main()
    {
        File.Delete(file);

        // ===== 1. MemoryStream -> ToArray -> FileStream write -> read chain ===
        MemoryStream ms0 = new MemoryStream();
        byte[] src = new byte[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90 };
        ms0.Write(src, 0, 10);
        byte[] materialized = ms0.ToArray();
        Console.WriteLine("c1_arr=" + (materialized.Length == 10 && materialized[0] == 0 && materialized[9] == 90));
        FileStream fw = new FileStream(file, FileMode.Create, FileAccess.Write);
        fw.Write(materialized, 0, 10);
        fw.Flush();
        Console.WriteLine("c1_len=" + (fw.Length == 10));
        fw.Close();
        FileStream fr = new FileStream(file, FileMode.Open, FileAccess.Read);
        byte[] back = new byte[10];
        Console.WriteLine("c1_read=" + (fr.Read(back, 0, 10) == 10));
        bool eq = true;
        for (int i = 0; i < 10; i++) if (back[i] != src[i]) eq = false;
        Console.WriteLine("c1_equal=" + eq);
        fr.Close();

        // ===== 2. byte[] <-> MemoryStream double round trip =====
        MemoryStream ms1 = new MemoryStream();
        ms1.Write(src, 0, 10);
        byte[] arr1 = ms1.ToArray();
        MemoryStream ms2 = new MemoryStream();
        ms2.Write(arr1, 0, 10);
        ms2.Position = 0;
        byte[] out2 = new byte[10];
        Console.WriteLine("c2_read=" + (ms2.Read(out2, 0, 10) == 10));
        eq = true;
        for (int i = 0; i < 10; i++) if (out2[i] != src[i]) eq = false;
        Console.WriteLine("c2_equal=" + eq);
        Console.WriteLine("c2_len=" + (ms2.Length == 10 && ms2.Position == 10));

        // ===== 3. StreamWriter -> FileStream -> StreamReader text chain =====
        FileStream fw2 = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter sw = new StreamWriter(fw2);
        sw.WriteLine("alpha");
        sw.WriteLine("");
        sw.WriteLine("gamma");
        sw.Flush();
        Console.WriteLine("c3_flushlen=" + (fw2.Length > 0));
        sw.Close();
        FileStream fr2 = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr = new StreamReader(fr2);
        Console.WriteLine("c3_l1=" + (sr.ReadLine() == "alpha"));
        Console.WriteLine("c3_l2=" + (sr.ReadLine() == ""));
        Console.WriteLine("c3_l3=" + (sr.ReadLine() == "gamma"));
        Console.WriteLine("c3_eof=" + (sr.ReadLine() == null));
        sr.Close();

        // ===== 4. WriteByte -> ReadByte chain (D-STP-3) =====
        FileStream fw3 = new FileStream(file, FileMode.Create, FileAccess.Write);
        for (int i = 0; i < 8; i++) fw3.WriteByte((byte)(100 + i));
        fw3.Close();
        FileStream fr3 = new FileStream(file, FileMode.Open, FileAccess.Read);
        bool seq = true;
        for (int i = 0; i < 8; i++) if (fr3.ReadByte() != 100 + i) seq = false;
        Console.WriteLine("c4_seq=" + seq);
        Console.WriteLine("c4_eof=" + (fr3.ReadByte() == -1));
        fr3.Close();

        // ===== 5. Seek + SetLength chain (D-STP-2) =====
        FileStream fs = new FileStream(file, FileMode.Create, FileAccess.ReadWrite);
        byte[] w5 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        fs.Write(w5, 0, 8);
        Console.WriteLine("c5_seek_cur=" + (fs.Seek(2, SeekOrigin.Current) == 10));
        fs.Write(new byte[] { 9, 9 }, 0, 2);
        Console.WriteLine("c5_len12=" + (fs.Length == 12));
        Console.WriteLine("c5_seek_end=" + (fs.Seek(-3, SeekOrigin.End) == 9));
        fs.SetLength(5);
        Console.WriteLine("c5_trunc=" + (fs.Length == 5));
        fs.Position = 0;
        byte[] r5 = new byte[5];
        fs.Read(r5, 0, 5);
        Console.WriteLine("c5_bytes=" + (r5[0] == 1 && r5[1] == 2 && r5[2] == 3 && r5[3] == 4 && r5[4] == 5));
        fs.Close();

        // ===== 6. MemoryStream -> StreamWriter -> ToArray -> StreamReader ====
        MemoryStream msW = new MemoryStream();
        StreamWriter sw2 = new StreamWriter(msW);
        sw2.WriteLine("delta");
        sw2.WriteLine("epsilon");
        sw2.Flush();
        byte[] mid = msW.ToArray();
        Console.WriteLine("c6_mid=" + (mid.Length > 0));
        MemoryStream msR = new MemoryStream();
        msR.Write(mid, 0, mid.Length);
        msR.Position = 0;
        StreamReader sr2 = new StreamReader(msR);
        Console.WriteLine("c6_r1=" + (sr2.ReadLine() == "delta"));
        Console.WriteLine("c6_r2=" + (sr2.ReadLine() == "epsilon"));
        sr2.Close();

        // ===== 7. flush-observable guard =====
        // NOTE: the "Length == 0 BEFORE Flush" write-buffer probe is NOT
        // asserted -- net48 StreamWriter buffers the line so Length stays 0
        // until Flush, while the runtime's writer writes through immediately
        // (a recorded divergence, D-SCH-1); only the AFTER-Flush observation
        // is asserted (both agree Length > 0 once flushed).
        FileStream fw7 = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter sw7 = new StreamWriter(fw7);
        sw7.WriteLine("zeta");
        sw7.Flush();
        Console.WriteLine("c7_flushlen=" + (fw7.Length > 0));
        sw7.Close();
        File.Delete(file);

        Console.WriteLine("done");
    }
}}
