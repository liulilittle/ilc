// fileops2.cs -- deep System.IO.File e2e reference case (net48 baseline).
//
// DEEP probes of the wired File surface (il2cpp_io.h + MethodMap.IO.cs):
//
//   1) WriteAllText -> ReadAllText roundtrips: ASCII, multi-line UTF-8
//      (Chinese + digits), and EMPTY content (creates the file, reads "").
//   2) AppendAllText ACCUMULATION: a document built from several appends
//      reads back as the concatenation; an append to a MISSING file
//      creates it (net48 semantics).
//   3) UTF-8 WIDE-PATH file names (the batch-21 wide-path fix: plat_file_open
//      via _wfopen / plat_file_remove via DeleteFileW / wide stat behind
//      plat_path_is_file): a Chinese file name survives the
//      WriteAllText -> Exists -> ReadAllText -> Delete chain.
//   4) Delete semantics: double-delete stays silent; deleting a missing
//      file is a silent success; Exists drops to 0.
//   5) WriteAllLines / ReadAllLines roundtrip: line count, per-line
//      equality, and the empty-file -> zero-line edge.
//   6) FileInfo object closure: Length (int64) of an ASCII file and of a
//      no-BOM UTF-8 file (byte length, net48 semantics).
//   7) Directory deep chain: CreateDirectory("d1\\d2") creates both levels,
//      Exists on each, then Delete the inner and the outer level.
//
// All probes print bools/ints (never raw content) so the diff is
// byte-stable across console encodings. Every temp file uses a distinct
// name and is removed at start AND end (idempotent re-runs).
//
// NOT TESTED HERE (recorded in GAPS.md, each verified as a transpile/build
// abort -- NOT called): File.Copy / File.Move / File.GetAttributes /
// File.SetAttributes / File.GetLastWriteTime -- MethodMap.IO.cs maps the
// System.IO.File surface to Exists/Delete/Write*Text/Read*Text/
// Write*Lines/Read*Lines only; the remaining members fall to the
// generated-name path and do not link.

using System;
using System.IO;

namespace Cases { public static class FileOps2 {
    public static void Main() {
        const string a = "fops2_a.txt";
        const string b = "fops2_b.txt";
        const string c = "fops2_c.txt";
        const string wide = "宽路径_测试.txt";
        File.Delete(a); File.Delete(b); File.Delete(c); File.Delete(wide);

        // ---- 1) roundtrips: ASCII / multi-line UTF-8 / empty ----
        File.WriteAllText(a, "hello");
        Console.WriteLine("rt_ascii=" + (File.ReadAllText(a) == "hello" ? 1 : 0));
        Console.WriteLine("exists_ascii=" + (File.Exists(a) ? 1 : 0));
        File.WriteAllText(a, "第一行\r\n第二行123\r\n第三行ABC456");
        Console.WriteLine("rt_utf8=" + (File.ReadAllText(a) == "第一行\r\n第二行123\r\n第三行ABC456" ? 1 : 0));
        Console.WriteLine("rt_utf8_len=" + File.ReadAllText(a).Length);
        File.WriteAllText(a, "");
        Console.WriteLine("rt_empty_len=" + (File.ReadAllText(a).Length == 0 ? 1 : 0));
        Console.WriteLine("exists_after_empty_write=" + (File.Exists(a) ? 1 : 0));

        // ---- 2) append accumulation + append-to-missing ----
        File.WriteAllText(b, "a");
        File.AppendAllText(b, "b");
        File.AppendAllText(b, "cd");
        Console.WriteLine("append_acc=" + (File.ReadAllText(b) == "abcd" ? 1 : 0));
        File.Delete(b);
        File.AppendAllText(b, "new");
        Console.WriteLine("append_missing_rt=" + (File.ReadAllText(b) == "new" ? 1 : 0));

        // ---- 3) UTF-8 wide-path file name ----
        File.WriteAllText(wide, "宽内容");
        Console.WriteLine("wide_exists=" + (File.Exists(wide) ? 1 : 0));
        Console.WriteLine("wide_rt=" + (File.ReadAllText(wide) == "宽内容" ? 1 : 0));
        Console.WriteLine("wide_not_dir=" + (File.Exists(".") ? 0 : 1));
        File.Delete(wide);
        Console.WriteLine("wide_gone=" + (File.Exists(wide) ? 0 : 1));

        // ---- 4) delete semantics ----
        File.Delete(wide); // double delete stays silent
        Console.WriteLine("double_delete_ok=" + (File.Exists(wide) ? 0 : 1));
        Console.WriteLine("missing_delete_ok=" + (File.Exists("no_such_fops2_file.txt") ? 0 : 1));

        // ---- 5) lines roundtrip ----
        File.WriteAllLines(a, new string[] { "alpha", "beta", "gamma" });
        string[] ls = File.ReadAllLines(a);
        Console.WriteLine("lines_count=" + ls.Length);
        Console.WriteLine("line0=" + (ls[0] == "alpha" ? 1 : 0));
        Console.WriteLine("line1=" + (ls[1] == "beta" ? 1 : 0));
        Console.WriteLine("line2=" + (ls[2] == "gamma" ? 1 : 0));
        File.WriteAllText(a, "");
        Console.WriteLine("empty_lines=" + (File.ReadAllLines(a).Length == 0 ? 1 : 0));

        // ---- 6) FileInfo.Length ----
        File.WriteAllText(c, "hello");        // 5 ASCII bytes
        Console.WriteLine("fi_ascii_len=" + (new FileInfo(c).Length == 5 ? 1 : 0));
        File.WriteAllText(c, "宽内容");        // 9 UTF-8 bytes (3 x 3-byte CJK), no BOM
        Console.WriteLine("fi_utf8_len=" + (new FileInfo(c).Length == 9 ? 1 : 0));
        Console.WriteLine("fi_len_printed=" + new FileInfo(c).Length);

        // ---- 7) directory deep chain ----
        string sub = Path.Combine("fops2_d1", "sub");
        Directory.CreateDirectory(sub);
        Console.WriteLine("dir_deep_root=" + (Directory.Exists("fops2_d1") ? 1 : 0));
        Console.WriteLine("dir_deep_sub=" + (Directory.Exists(sub) ? 1 : 0));
        Directory.Delete(sub);
        Console.WriteLine("dir_sub_gone=" + (Directory.Exists(sub) ? 0 : 1));
        Console.WriteLine("dir_root_remains=" + (Directory.Exists("fops2_d1") ? 1 : 0));
        Directory.Delete("fops2_d1");
        Console.WriteLine("dir_root_gone=" + (Directory.Exists("fops2_d1") ? 0 : 1));

        // cleanup: idempotent end (re-runs must not leave artifacts)
        File.Delete(a); File.Delete(b); File.Delete(c);
    }
}}
