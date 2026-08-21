//------------------------------------------------------------------------------
// streamops.cs -- end-to-end reference case for StreamReader/StreamWriter
// COMPOSITION (net48 oracle): FileStream.Create -> StreamWriter.Write -> read
// back -> UTF-8 Chinese roundtrip -> using/dispose semantics -> exception
// paths.
//
// STATUS 2026-08-07: the net48 oracle below is byte-exact, but the case CANNOT
// transpile yet -- the transpiler's MethodMap.StreamIO.cs LOUD-rejects EVERY
// stream ctor (MapStreamCtor, lines 108-126) although the RUNTIME object layer
// already LANDED (il2cpp_streamio.h: object-typed icall_stream_file_ctor /
// reader_ctor / writer_ctor + the handle registry + Read/Write bytes icalls,
// runtime batch 17 commit 1066ad7). The transpiler wiring task is the missing
// hop. See GAPS.md D-STO-1..3 for the precise causal chains.
//
// Coverage (oracle surface):
//   * FileStream.Create -> StreamWriter.WriteLine chain -> Flush -> Length ->
//     Close; StreamReader.ReadLine chain -> EOF -> Close (Content NEVER
//     printed -- only bools/lengths -- so the diff is console-encoding stable).
//   * UTF-8 Chinese roundtrip: write a Chinese line, read it back, compare
//     equality + byte Length + EOF.
//   * using semantics: a using block's Dispose must flush + close so a
//     subsequent open+read sees the line.
//   * Exception paths: FileStream on a missing file -> FileNotFoundException;
//     Stream.Read on a write-only stream -> NotSupportedException.
//   * Temp file is deleted at start AND end (idempotent re-runs).
//
// Known net48 deviations of the RUNTIME model that are NOT probed (would
// diverge; streamio.h documents each): reading after Close returns
// -1/nullptr (net48 throws ObjectDisposedException); double Close is a no-op
// on both sides (net48 close is idempotent too).
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class StreamOps {
    const string file = "streamops_tmp.txt";

    public static void Main()
    {
        File.Delete(file); // idempotent start: silent on missing

        // ---- write chain: FileStream.Create -> StreamWriter.WriteLine ----
        FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter sw = new StreamWriter(fs);
        Console.WriteLine("w_canwrite=" + fs.CanWrite);
        Console.WriteLine("w_canread=" + (fs.CanRead == false));
        sw.WriteLine("line one");
        sw.WriteLine("line two");
        sw.Flush();
        Console.WriteLine("w_len=" + fs.Length);
        sw.Close();
        Console.WriteLine("w_exists=" + File.Exists(file));

        // ---- read chain: FileStream.Open -> StreamReader.ReadLine ----
        FileStream fr = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr = new StreamReader(fr);
        Console.WriteLine("r_canread=" + fr.CanRead);
        Console.WriteLine("r_canwrite=" + (fr.CanWrite == false));
        Console.WriteLine("r_len=" + fr.Length);
        Console.WriteLine("r_line1=" + (sr.ReadLine() == "line one"));
        Console.WriteLine("r_line2=" + (sr.ReadLine() == "line two"));
        Console.WriteLine("r_eof=" + (sr.ReadLine() == null));
        sr.Close();

        // ---- UTF-8 Chinese roundtrip ----
        const string zh = "中文往返123";
        FileStream fw = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter wzh = new StreamWriter(fw);
        wzh.WriteLine(zh);
        wzh.Close();
        FileStream fr2 = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader rzh = new StreamReader(fr2);
        string got = rzh.ReadLine();
        Console.WriteLine("zh_roundtrip=" + (got == zh));
        Console.WriteLine("zh_len=" + fr2.Length);
        Console.WriteLine("zh_eof=" + (rzh.ReadLine() == null));
        rzh.Close();

        // ---- using semantics: Dispose flushes + closes ----
        using (StreamWriter usw = new StreamWriter(file))
        {
            usw.WriteLine("using-line");
        }
        FileStream uf = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader usr = new StreamReader(uf);
        Console.WriteLine("using_flushed=" + (usr.ReadLine() == "using-line"));
        usr.Close();

        // ---- exception path: missing file -> FileNotFoundException ----
        File.Delete(file);
        bool threw = false;
        try { new FileStream(file, FileMode.Open, FileAccess.Read); }
        catch (FileNotFoundException) { threw = true; }
        Console.WriteLine("missing_throws=" + threw);

        // ---- exception path: Read on a write-only stream -> NotSupportedException ----
        FileStream wfs = new FileStream(file, FileMode.Create, FileAccess.Write);
        byte[] buf = new byte[4];
        threw = false;
        try { wfs.Read(buf, 0, 4); }
        catch (NotSupportedException) { threw = true; }
        Console.WriteLine("read_writeonly_throws=" + threw);
        wfs.Close();

        File.Delete(file); // idempotent end: the temp file must not linger
    }
}}
