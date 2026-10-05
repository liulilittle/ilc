//------------------------------------------------------------------------------
// fileio2.cs -- System.IO.File FULL-API deep e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C10 (2026-08-09).
//
// Exercises the M7 io3 surface (MethodMap.IO.cs + io.cpp, landed 2026-08-08
// -- the File.Copy/Move/GetAttributes/SetAttributes/GetLastWriteTime gap
// closures that fileops2 could NOT probe, recorded there in DEFECT-FILE-1..5):
//   * File.Copy(src, dst)           -> icall_file_copy (never overwrites)
//   * File.Copy(src, dst, overwrite)-> icall_file_copy_overwrite
//   * File.Move(src, dst)           -> icall_file_move
//   * File.GetAttributes            -> icall_file_get_attributes (mask int)
//   * File.SetAttributes            -> icall_file_set_attributes (REPLACES)
//   * File.GetLastWriteTime         -> icall_file_get_last_write_time (dt)
//   * UTF-8 WIDE-path (Chinese) file names through Copy/Move/Attributes/
//     LastWriteTime (the wide-path fix: every icall transcodes via CsToWide
//     / CopyFileW / MoveFileW / GetFileAttributesW / GetFileAttributesExW).
//
// Byte-stability rules:
//   * Probe CONTENT/Existence/attribute bits, never raw DateTime text: the
//     write-time of a just-written file is the CURRENT year on both runtimes
//     (probe .Year > 2000 for existing, .Year == 1601 for the missing-path
//     1601-01-01 contract), and raw attribute MASKS are Windows-dependent --
//     the SetAttributes(ReadOnly/Normal) round-trip pins the mask exactly
//     (net48 SetFileAttributesW semantics, io.cpp probed: ReadOnly clears
//     Archive -> mask 0x1; Normal -> exactly 0x80).
//   * Exception-path probes (net48 throws IOException/FileNotFoundException
//     where the M3 icall returns a status silently) are RECORDED GAPS: the
//     try/catch flag prints True on net48, False on ours (no throw) -- see
//     the labels. They are kept in the file (documented defects), the
//     success paths are the io3 acceptance surface.
//   * Every temp file uses a distinct fio2_* name and is removed at start
//     AND end (idempotent re-runs); attributes are reset to Normal before
//     delete (a ReadOnly file cannot be deleted on Windows).
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class FileIo2 {
    public static void Main() {
        const string a = "fio2_a.txt";
        const string b = "fio2_b.txt";
        const string c = "fio2_c.txt";
        const string d = "fio2_d.txt";
        const string wide = "宽_fio2.txt";
        const string wide2 = "宽_fio2_副本.txt";
        File.Delete(a); File.Delete(b); File.Delete(c); File.Delete(d);
        File.Delete(wide); File.Delete(wide2);

        // ================= 1) File.Copy basic =================
        File.WriteAllText(a, "copy-me");
        File.Copy(a, b);
        Console.WriteLine("copy_exists=" + (File.Exists(b) ? 1 : 0));
        Console.WriteLine("copy_content=" + (File.ReadAllText(b) == "copy-me" ? 1 : 0));
        Console.WriteLine("copy_src_kept=" + (File.Exists(a) ? 1 : 0));

        // ============ 2) File.Copy(src, dst, overwrite) ============
        File.WriteAllText(b, "old");
        File.Copy(a, b, true);                       // overwrite=true replaces
        Console.WriteLine("copy_ow_content=" + (File.ReadAllText(b) == "copy-me" ? 1 : 0));
        File.WriteAllText(b, "old2");
        bool noowCaught = false;
        try { File.Copy(a, b, false); } catch (IOException) { noowCaught = true; }
        // net48 throws IOException on an existing dest with overwrite=false;
        // the M3 icall returns a status silently (no throw) -- RECORDED GAP
        // (flag prints 1 on net48, 0 on ours). The content is untouched on
        // BOTH (net48 throws before writing; ours never overwrites).
        Console.WriteLine("copy_noow_throw=" + (noowCaught ? 1 : 0));
        Console.WriteLine("copy_noow_content=" + (File.ReadAllText(b) == "old2" ? 1 : 0));

        // ============ 3) File.Move ============
        File.WriteAllText(c, "move-me");
        File.Move(c, d);
        Console.WriteLine("move_src_gone=" + (File.Exists(c) ? 0 : 1));
        Console.WriteLine("move_dst_exists=" + (File.Exists(d) ? 1 : 0));
        Console.WriteLine("move_content=" + (File.ReadAllText(d) == "move-me" ? 1 : 0));

        // ============ 4) File.GetAttributes / SetAttributes ============
        File.WriteAllText(a, "attr");
        File.SetAttributes(a, FileAttributes.ReadOnly);
        Console.WriteLine("attr_readonly=" + (File.GetAttributes(a) == FileAttributes.ReadOnly ? 1 : 0));
        Console.WriteLine("attr_readonly_int=" + ((int)File.GetAttributes(a) == 1 ? 1 : 0));
        File.SetAttributes(a, FileAttributes.Normal);
        Console.WriteLine("attr_normal=" + (File.GetAttributes(a) == FileAttributes.Normal ? 1 : 0));
        Console.WriteLine("attr_normal_int=" + ((int)File.GetAttributes(a) == 128 ? 1 : 0));
        Console.WriteLine("attr_notreadonly=" + ((File.GetAttributes(a) & FileAttributes.ReadOnly) == 0 ? 1 : 0));

        // ============ 5) File.GetLastWriteTime ============
        File.WriteAllText(a, "lwt");
        Console.WriteLine("lwt_existing=" + (File.GetLastWriteTime(a).Year > 2000 ? 1 : 0));
        Console.WriteLine("lwt_missing_1601=" + (File.GetLastWriteTime("fio2_missing.txt").Year == 1601 ? 1 : 0));

        // ============ 6) wide (Chinese) path names ============
        File.WriteAllText(wide, "宽内容");
        File.Copy(wide, wide2);
        Console.WriteLine("wide_copy_exists=" + (File.Exists(wide2) ? 1 : 0));
        Console.WriteLine("wide_copy_content=" + (File.ReadAllText(wide2) == "宽内容" ? 1 : 0));
        File.Delete(b);                                  // free the Move destination
        File.Move(wide2, b);
        Console.WriteLine("wide_move_content=" + (File.ReadAllText(b) == "宽内容" ? 1 : 0));
        File.SetAttributes(b, FileAttributes.ReadOnly);
        Console.WriteLine("wide_attr_readonly=" + ((File.GetAttributes(b) & FileAttributes.ReadOnly) != 0 ? 1 : 0));
        File.SetAttributes(b, FileAttributes.Normal);
        Console.WriteLine("wide_lwt_existing=" + (File.GetLastWriteTime(b).Year > 2000 ? 1 : 0));
        File.Delete(wide);
        Console.WriteLine("wide_gone=" + (File.Exists(wide) ? 0 : 1));

        // ============ 7) RECORDED GAP: exception semantics ============
        // net48 throws IOException when Copy overwrite=false targets an
        // existing file; the M3 icall returns a status the .NET method
        // discards (no throw). The try/catch flag is True on net48, False
        // on ours (documented deviation -- exception transport milestone).
        bool c1 = false;
        try { File.Copy(a, b, false); } catch (IOException) { c1 = true; }
        Console.WriteLine("exc_copy_noow=" + (c1 ? 1 : 0));
        bool c2 = false;
        try { File.Move("fio2_none.txt", a); } catch (FileNotFoundException) { c2 = true; }
        Console.WriteLine("exc_move_missing=" + (c2 ? 1 : 0));

        // cleanup: reset attrs then remove (idempotent end)
        File.SetAttributes(b, FileAttributes.Normal);
        File.Delete(a); File.Delete(b); File.Delete(d);
    }
}}
