//------------------------------------------------------------------------------
// ioext.cs -- System.IO EXTENSION deep e2e case (batch C48), net48 oracle,
// byte-for-byte. Extends iochain/filechain/streamdeep2 with three DEEPER axes
// over the fully-wired IO surface (MethodMap.IO.cs -> io.cpp/il2cpp_io.h +
// MethodMap.StreamIO.cs -> streamio.cpp):
//
//   1. DIRECTORY TRAVERSAL (遍历): a nested 3-level tree (root/a/b/c) with a
//      file written at the deepest level, Exists checks at every level,
//      Directory.Exists(false) on a FILE path and File.Exists(false) on a
//      DIRECTORY path, then recursive Delete(true) of the whole tree with the
//      root kept -- plus the non-recursive delete of an EMPTY dir (1-arg and
//      2-arg false both succeed on an empty directory, both runtimes).
//   2. FileInfo PROPERTIES (属性): FileInfo.Length across write / append /
//      copy / move chains (the Length of the same physical file re-stated
//      through a fresh FileInfo after each mutation), plus the D-IOEXT-1
//      exception semantics: Length on a MISSING path or a DIRECTORY path
//      throws FileNotFoundException (net48-probed; the runtime matches after
//      the fix -- the try/catch flags print True on both runtimes).
//   3. STREAM WRAPPING (流包装): StreamWriter(Stream) wrapping a FileStream,
//      StreamReader(Stream) wrapping a FileStream (ReadLine chain + EOF null),
//      MemoryStream + StreamWriter + ToArray byte-exact, MemoryStream +
//      StreamReader + ReadToEnd, StreamWriter(path) direct-write + read-back,
//      Position/Length/CanRead/CanWrite on the wrapped FileStream, and the
//      wrapped-writer ContentCheck via File.ReadAllText.
//
// net48-shape discipline: all probes print bools (never raw content -- the
// byte-stable convention); paths are relative to the process working
// directory; the root temp directory is removed at start AND end so re-runs
// stay idempotent. Every line is deterministic on both runtimes.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class IoExt {
    public static void Main() {
        const string root = "ioext_tmp";
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // ================= 1. Directory traversal (nested 3-level tree) =================
        string a = Path.Combine(root, "a");
        string b = Path.Combine(a, "b");
        string c = Path.Combine(b, "c");
        Directory.CreateDirectory(c);                       // creates a/b/c at once
        Console.WriteLine("t_lvl1=" + Directory.Exists(a));       // True
        Console.WriteLine("t_lvl2=" + Directory.Exists(b));       // True
        Console.WriteLine("t_lvl3=" + Directory.Exists(c));       // True
        string deepFile = Path.Combine(c, "deep.txt");
        File.WriteAllText(deepFile, "deep content");
        Console.WriteLine("t_file=" + File.Exists(deepFile));     // True
        Console.WriteLine("t_file_len=" + (new FileInfo(deepFile).Length == 12)); // True
        // Exists type discrimination: a file is NOT a directory, and vice versa.
        Console.WriteLine("t_file_is_dir=" + Directory.Exists(deepFile));   // False
        Console.WriteLine("t_dir_is_file=" + File.Exists(c));               // False
        // recursive delete removes the whole tree, root kept.
        Directory.Delete(a, true);
        Console.WriteLine("t_a_gone=" + (Directory.Exists(a) == false));    // True
        Console.WriteLine("t_root_kept=" + Directory.Exists(root));         // True

        // ================= 2. non-recursive delete of an EMPTY dir =================
        string empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        Directory.Delete(empty);                            // 1-arg on empty: OK
        Console.WriteLine("e1_gone=" + (Directory.Exists(empty) == false)); // True
        Directory.CreateDirectory(empty);
        Directory.Delete(empty, false);                     // 2-arg false on empty: OK
        Console.WriteLine("e2_gone=" + (Directory.Exists(empty) == false)); // True

        // ================= 3. FileInfo.Length property chains =================
        string f = Path.Combine(root, "len.txt");
        File.WriteAllText(f, "12345");                      // 5 bytes
        Console.WriteLine("l_w5=" + (new FileInfo(f).Length == 5));        // True
        File.AppendAllText(f, "67");                        // 5 + 2 = 7
        Console.WriteLine("l_ap7=" + (new FileInfo(f).Length == 7));       // True
        string g = Path.Combine(root, "len2.txt");
        File.Copy(f, g);
        Console.WriteLine("l_copy7=" + (new FileInfo(g).Length == 7));     // True
        string h = Path.Combine(root, "len3.txt");
        File.Move(g, h);
        Console.WriteLine("l_move7=" + (new FileInfo(h).Length == 7));     // True
        Console.WriteLine("l_src_gone=" + (File.Exists(g) == false));      // True
        // D-IOEXT-1: FileInfo.Length on a MISSING file or a DIRECTORY path
        // throws FileNotFoundException on net48 (probed). The runtime now
        // matches (previously returned 0). try/catch flags print True.
        bool lfMissing = false;
        try { long unused = new FileInfo(Path.Combine(root, "missing.txt")).Length; }
        catch (FileNotFoundException) { lfMissing = true; }
        Console.WriteLine("l_missing_throw=" + lfMissing);                 // True
        bool lfDir = false;
        try { long unused = new FileInfo(root).Length; }    // root is a directory
        catch (FileNotFoundException) { lfDir = true; }
        Console.WriteLine("l_dir_throw=" + lfDir);                          // True
        File.Delete(f);
        File.Delete(h);

        // ================= 4. Stream wrapping (writer over FileStream) =================
        string wf = Path.Combine(root, "wrap.txt");
        FileStream wfs = new FileStream(wf, FileMode.Create, FileAccess.Write);
        Console.WriteLine("w_canwrite=" + wfs.CanWrite);                  // True
        Console.WriteLine("w_canread=" + (wfs.CanRead == false));         // True
        StreamWriter sw = new StreamWriter(wfs);
        sw.WriteLine("alpha");
        sw.WriteLine("beta");
        sw.WriteLine("gamma");
        sw.Flush();
        Console.WriteLine("w_pos=" + (wfs.Position == 21));  // 3 lines * 7 bytes ("alpha"+CRLF)
        sw.Close();                                         // flushes + closes the file once
        Console.WriteLine("w_content=" + (File.ReadAllText(wf) == "alpha\r\nbeta\r\ngamma\r\n")); // True

        // ================= 5. Stream wrapping (reader over FileStream) =================
        FileStream rfs = new FileStream(wf, FileMode.Open, FileAccess.Read);
        Console.WriteLine("r_canread=" + rfs.CanRead);                    // True
        Console.WriteLine("r_canwrite=" + (rfs.CanWrite == false));       // True
        StreamReader sr = new StreamReader(rfs);
        Console.WriteLine("r_line0=" + (sr.ReadLine() == "alpha"));       // True
        Console.WriteLine("r_line1=" + (sr.ReadLine() == "beta"));        // True
        Console.WriteLine("r_line2=" + (sr.ReadLine() == "gamma"));       // True
        Console.WriteLine("r_eof=" + (sr.ReadLine() == null));            // True
        sr.Close();
        File.Delete(wf);

        // ================= 6. MemoryStream wrapping (writer + ToArray) =================
        MemoryStream ms = new MemoryStream();
        StreamWriter msw = new StreamWriter(ms);
        msw.WriteLine("xy");                            // "xy" + CRLF
        msw.WriteLine("z");                             // "z" + CRLF
        msw.Flush();
        byte[] arr = ms.ToArray();
        Console.WriteLine("m_len=" + (arr.Length == 7));  // 4 + 3 = 7
        Console.WriteLine("m_b0=" + (arr[0] == (byte)'x'));                // True
        Console.WriteLine("m_b1=" + (arr[1] == (byte)'y'));                // True
        Console.WriteLine("m_b2=" + (arr[2] == (byte)'\r'));               // True
        Console.WriteLine("m_b3=" + (arr[3] == (byte)'\n'));               // True
        Console.WriteLine("m_b4=" + (arr[4] == (byte)'z'));                // True
        msw.Close();

        // ================= 7. MemoryStream wrapping (reader + ReadToEnd) =================
        MemoryStream ms2 = new MemoryStream();
        StreamWriter w2 = new StreamWriter(ms2);
        w2.WriteLine("hello");
        w2.WriteLine("world");
        w2.Flush();
        ms2.Position = 0;
        StreamReader sr2 = new StreamReader(ms2);
        Console.WriteLine("rte_read=" + (sr2.ReadToEnd() == "hello\r\nworld\r\n")); // True
        sr2.Close();
        w2.Close();

        // ================= 8. StreamWriter(path) direct write + read-back =================
        string pf = Path.Combine(root, "pathwriter.txt");
        StreamWriter psw = new StreamWriter(pf);            // FileMode.Create
        psw.WriteLine("direct");
        psw.Close();
        Console.WriteLine("pw_content=" + (File.ReadAllText(pf) == "direct\r\n")); // True
        File.Delete(pf);

        // cleanup
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Console.WriteLine("cleanup=" + (Directory.Exists(root) == false)); // True
    }
}}
