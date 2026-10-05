// streamcase.cs -- System.IO stream object-chain e2e reference case (net48 baseline).
//
// HANDLE-GAP EXPLORATION CASE (2026-08-06): the runtime stream icalls
// (streamio.h) are handle FACTORIES (il2cpp_stream_t) with no managed-object
// association, and the transpiler maps stream ctors to LOUD NotSupportedException
// rejections (MethodMap.StreamIO.cs MapStreamCtor). This case therefore CANNOT
// transpile yet -- it is built FIRST as the net48 reference oracle (expected.txt)
// and as the precise gap list (GAPS.md) for the stream objectification task.
//
// Desired end-state surface exercised here:
//   FileStream(path, FileMode, FileAccess) -> StreamWriter.WriteLine chain
//   -> Flush/Position/Length -> StreamReader.ReadLine chain -> EOF -> Close
//   MemoryStream: Write/Read/Position/ToArray roundtrip.
// All probes print raw bools (True/False), never content, so the diff is
// byte-stable across console encodings. All paths are relative to the process
// working directory; the temp file is deleted at start AND end so re-runs
// stay idempotent.

using System;
using System.IO;

namespace Cases { public static class StreamCase {
    public static void Main() {
        const string file = "streamcase_tmp.txt";
        File.Delete(file); // idempotent start: silent on missing

        // ---- write chain: FileStream.Create -> StreamWriter.WriteLine ----
        FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write);
        StreamWriter sw = new StreamWriter(fs);
        Console.WriteLine(fs.CanWrite);            // True
        Console.WriteLine(fs.CanRead == false);    // True (write-only stream)
        sw.WriteLine("line one");
        sw.Flush();
        Console.WriteLine(fs.Position == 10);      // True ("line one" + CRLF = 10 bytes)
        sw.WriteLine("line two");
        sw.WriteLine("line three");
        sw.Close();                                // flushes + closes fs too (net48)

        // ---- read chain: FileStream.Open -> StreamReader.ReadLine ----
        FileStream fr = new FileStream(file, FileMode.Open, FileAccess.Read);
        StreamReader sr = new StreamReader(fr);
        Console.WriteLine(fr.CanRead);             // True
        Console.WriteLine(fr.CanWrite == false);   // True (read-only stream)
        Console.WriteLine(fr.Length == 32);        // True (10+10+12 bytes, UTF-8 no BOM)
        Console.WriteLine(sr.ReadLine() == "line one");    // True
        Console.WriteLine(sr.ReadLine() == "line two");    // True
        Console.WriteLine(sr.ReadLine() == "line three");  // True
        Console.WriteLine(sr.ReadLine() == null);  // True (EOF -> null)
        sr.Close();

        // ---- MemoryStream: Write/Read/Position/ToArray roundtrip ----
        MemoryStream ms = new MemoryStream();
        Console.WriteLine(ms.CanRead);             // True
        Console.WriteLine(ms.CanWrite);            // True
        Console.WriteLine(ms.Length == 0);         // True
        ms.Write(new byte[] { 1, 2, 3 }, 0, 3);
        Console.WriteLine(ms.Length == 3);         // True
        Console.WriteLine(ms.Position == 3);       // True
        ms.Position = 0;
        Console.WriteLine(ms.Position == 0);       // True
        byte[] buf = new byte[3];
        Console.WriteLine(ms.Read(buf, 0, 3) == 3);    // True
        Console.WriteLine(buf[0] == 1 && buf[1] == 2 && buf[2] == 3); // True
        byte[] arr = ms.ToArray();
        Console.WriteLine(arr.Length == 3);        // True
        Console.WriteLine(arr[0] == 1);            // True
        ms.Close();

        File.Delete(file); // idempotent end: the temp file must not linger
    }
}}
