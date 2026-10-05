//------------------------------------------------------------------------------
// iochain.cs -- SYSTEM.IO CHAIN DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C40, AFTER the full IO chain is wired
// at HEAD (MethodMap.IO.cs -> io.cpp / il2cpp_io.h): File.Exists/Delete/
// WriteAllText/ReadAllText/AppendAllText/WriteAllLines/ReadAllLines/Copy/
// Move/GetAttributes/SetAttributes, Directory.CreateDirectory/Exists/Delete
// (recursive), Path.Combine (2/3-arg)/GetFileName/GetExtension/
// ChangeExtension, FileInfo ctor/get_Length. THIS case drives the WRITE ->
// READ-BACK -> APPEND -> VERIFY -> DIRECTORY round-trip chains with state
// carried across operations:
//
//   1. WriteAllText + ReadAllText round-trips: fresh, overwrite-truncate,
//      empty content, length checks (UTF-8 without BOM both sides -- the
//      .NET Framework default, io.cpp:16-22).
//   2. AppendAllText: append-to-existing accumulates, append-to-missing
//      creates the file, repeated appends, length checks.
//   3. WriteAllLines/ReadAllLines round-trip (3-line content).
//   4. DIRECTORY chain: CreateDirectory -> Exists -> nested 3-deep create ->
//      write inside -> read back -> Delete(recursive) -> gone, root kept.
//   5. Path.Combine 2/3-arg, ChangeExtension (add/remove/null), GetFileName /
//      GetExtension on the chained paths.
//   6. FileInfo.Length on an existing file before/after append.
//   7. GetAttributes/SetAttributes read-only round-trip (masked probe --
//      robust to platform extra bits like Archive).
//   8. Copy carry chain: write -> Copy -> content -> Delete source.
//
// All paths are relative to the process working directory; the root temp
// directory is removed at start AND end so re-runs (and leftover files from
// a crashed run) stay idempotent. Every probe prints "label=True/False"
// (never raw content) -- a byte-stable diff. net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases
{
    public static class IoChain
    {
        public static void Main()
        {
            const string root = "iochain_tmp";
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);

            // ===== 1. WriteAllText + ReadAllText round-trips =====
            string file = Path.Combine(root, "round.txt");
            Console.WriteLine("ex_before=" + (File.Exists(file) == false));
            File.WriteAllText(file, "line one");
            Console.WriteLine("ex_after=" + (File.Exists(file)));
            Console.WriteLine("rt_equal=" + (File.ReadAllText(file) == "line one"));
            Console.WriteLine("rt_len=" + (File.ReadAllText(file).Length == 8));
            File.WriteAllText(file, "12345");
            Console.WriteLine("ow_val=" + (File.ReadAllText(file) == "12345"));
            File.WriteAllText(file, "ab");
            Console.WriteLine("ow_trunc=" + (File.ReadAllText(file) == "ab" && File.ReadAllText(file).Length == 2));
            File.WriteAllText(file, "");
            Console.WriteLine("ow_empty=" + (File.ReadAllText(file).Length == 0));

            // ===== 2. AppendAllText chains =====
            File.WriteAllText(file, "A");
            File.AppendAllText(file, "B");
            Console.WriteLine("app_ab=" + (File.ReadAllText(file) == "AB"));
            File.AppendAllText(file, "CD");
            Console.WriteLine("app_abcd=" + (File.ReadAllText(file) == "ABCD"));
            Console.WriteLine("app_len=" + (File.ReadAllText(file).Length == 4));
            string fresh = Path.Combine(root, "fresh.txt");
            File.AppendAllText(fresh, "x");
            Console.WriteLine("app_missing=" + (File.ReadAllText(fresh) == "x"));

            // ===== 3. WriteAllLines / ReadAllLines round-trip =====
            string[] lines = { "alpha", "beta", "gamma" };
            File.WriteAllLines(file, lines);
            string[] back = File.ReadAllLines(file);
            Console.WriteLine("ln_len=" + (back.Length == 3));
            Console.WriteLine("ln_0=" + (back[0] == "alpha"));
            Console.WriteLine("ln_1=" + (back[1] == "beta"));
            Console.WriteLine("ln_2=" + (back[2] == "gamma"));

            // ===== 4. directory chain =====
            string sub = Path.Combine(root, "sub");
            Console.WriteLine("dir_before=" + (Directory.Exists(sub) == false));
            Directory.CreateDirectory(sub);
            Console.WriteLine("dir_after=" + (Directory.Exists(sub)));
            string deep = Path.Combine(sub, "a", "b");
            Directory.CreateDirectory(deep);
            Console.WriteLine("dir_nested=" + (Directory.Exists(Path.Combine(sub, "a"))));
            string nfile = Path.Combine(deep, "d.txt");
            File.WriteAllText(nfile, "deep content");
            Console.WriteLine("dir_roundtrip=" + (File.ReadAllText(nfile) == "deep content"));
            Directory.Delete(sub, true);
            Console.WriteLine("dir_gone=" + (Directory.Exists(sub) == false));
            Console.WriteLine("root_kept=" + (Directory.Exists(root)));

            // ===== 5. path surface on the chained paths =====
            Console.WriteLine("pc2=" + (Path.Combine("a", "b") == "a\\b"));
            Console.WriteLine("pc3=" + (Path.Combine("a", "b", "c") == "a\\b\\c"));
            Console.WriteLine("pc_empty=" + (Path.Combine("", "b") == "b"));
            Console.WriteLine("ce_log=" + (Path.ChangeExtension("data.txt", ".log") == "data.log"));
            Console.WriteLine("ce_add=" + (Path.ChangeExtension("data", ".log") == "data.log"));
            Console.WriteLine("ce_rem=" + (Path.ChangeExtension("data.txt", "") == "data."));
            Console.WriteLine("ce_null=" + (Path.ChangeExtension("data.txt", null) == "data"));
            Console.WriteLine("gfn=" + (Path.GetFileName(deep) == "b"));
            Console.WriteLine("gfn_file=" + (Path.GetFileName(nfile) == "d.txt"));
            Console.WriteLine("ge=" + (Path.GetExtension(nfile) == ".txt"));

            // ===== 6. FileInfo.Length on an existing file =====
            File.WriteAllText(file, "abcde");
            Console.WriteLine("fi_len=" + (new FileInfo(file).Length == 5));
            File.AppendAllText(file, "xy");
            Console.WriteLine("fi_len_append=" + (new FileInfo(file).Length == 7));

            // ===== 7. attributes read-only round-trip (masked probe) =====
            File.SetAttributes(file, FileAttributes.ReadOnly);
            Console.WriteLine("attr_ro=" + ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0));
            File.SetAttributes(file, FileAttributes.Normal);
            Console.WriteLine("attr_clear=" + ((File.GetAttributes(file) & FileAttributes.ReadOnly) == 0));

            // ===== 8. copy carry chain =====
            string f1 = Path.Combine(root, "one.txt");
            string f2 = Path.Combine(root, "two.txt");
            File.WriteAllText(f1, "chain");
            File.Copy(f1, f2);
            Console.WriteLine("copy_content=" + (File.ReadAllText(f2) == "chain"));
            File.Delete(f1);
            Console.WriteLine("copy_src_gone=" + (File.Exists(f1) == false));
            Console.WriteLine("copy_dst_kept=" + (File.Exists(f2)));

            // cleanup
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Console.WriteLine("cleanup=" + (Directory.Exists(root) == false));
        }
    }
}
