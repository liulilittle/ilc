//------------------------------------------------------------------------------
// filecopy.cs -- System.IO.File.Copy / File.Move DEEP e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C19 (2026-08-10).
//
// EXTENDS fileio2 (whose copy/move EXCEPTION probes were RECORDED GAPS at C10
// -- "the M3 icall returns a status silently") on the post-mapping surface:
// fileio2 (m7m) wired io.cpp icall_file_copy / icall_file_copy_overwrite /
// icall_file_move to THROW through the DotNetException carrier (io.cpp:636
// ThrowFileOperationFailure + the factories in exceptionext.cpp). This case
// is the exception-mapping acceptance surface:
//
//   * Copy(missingSrc, dst)      -> FileNotFoundException (源缺失); dst NOT
//     created (net48 throws before any write).
//   * Copy(src, existingDst)     -> IOException (2-arg never overwrites); the
//     src stays in place, the dst content is untouched.
//   * Copy(src, dst, false)      -> IOException (覆盖抛: overwrite=false on an
//     existing destination); overwrite=true REPLACES.
//   * Copy(src, src)             -> IOException (the destination exists).
//   * Move(missingSrc, dst)      -> FileNotFoundException.
//   * Move(src, existingDst)     -> IOException (目标存在); the SOURCE is KEPT
//     (a failed Move leaves the source in place -- net48 + io.cpp).
//   * BASE CATCH: FileNotFoundException derives from IOException -- a
//     `catch (IOException)` catches the missing-source failure on both
//     runtimes (il2cpp_exception_matches walks the baked base chain).
//   * SUCCESS paths re-verified with the mapping active (copy content
//     roundtrip, overwrite replace, move source-gone / dest-exists).
//
// Byte-stability rules:
//   * Only catch FLAGS + ex.GetType().Name are probed on the failure paths:
//     net48 LOCALIZES the IOException / FileNotFoundException message (this
//     machine is Chinese-locale), the ILC carriers are hardcoded English --
//     raw message text would diverge (recorded D-EX2-3 in exops2's GAPS).
//   * File state is probed as content / existence flags (locale-invariant).
//   * Every temp file uses a distinct fcopy_* name and is removed at start
//     AND end (idempotent re-runs); both runtimes share the harness CWD, so
//     the probes see the same file state.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class FileCopy {
    public static void Main() {
        const string a = "fcopy_a.txt";
        const string b = "fcopy_b.txt";
        const string c = "fcopy_c.txt";
        const string d = "fcopy_d.txt";
        const string m = "fcopy_missing.txt";
        File.Delete(a); File.Delete(b); File.Delete(c); File.Delete(d);

        // ============ 1) Copy success (mapping does not regress it) =====
        File.WriteAllText(a, "one");
        File.Copy(a, b);
        Console.WriteLine("f1_copy_dst=" + (File.Exists(b) ? 1 : 0));
        Console.WriteLine("f1_copy_content=" + (File.ReadAllText(b) == "one" ? 1 : 0));
        Console.WriteLine("f1_copy_src_kept=" + (File.Exists(a) ? 1 : 0));

        // ============ 2) Copy(src, dst, false) on an existing dst =======
        // net48 + io.cpp: IOException (覆盖抛); src intact, dst untouched.
        File.WriteAllText(b, "old");
        bool f2 = false;
        try { File.Copy(a, b, false); }
        catch (IOException ex) { f2 = ex.GetType().Name == "IOException"; }
        Console.WriteLine("f2_noow_throws=" + (f2 ? 1 : 0));
        Console.WriteLine("f2_noow_src_kept=" + (File.Exists(a) ? 1 : 0));
        Console.WriteLine("f2_noow_dst_unchanged=" + (File.ReadAllText(b) == "old" ? 1 : 0));

        // ============ 3) Copy(src, dst) 2-arg on an existing dst ========
        // the 2-arg form NEVER overwrites -> IOException.
        bool f3 = false;
        try { File.Copy(a, b); } catch (IOException) { f3 = true; }
        Console.WriteLine("f3_2arg_existing_throws=" + (f3 ? 1 : 0));

        // ============ 4) Copy(src, dst, true) overwrite REPLACES ========
        File.Copy(a, b, true);
        Console.WriteLine("f4_ow_content=" + (File.ReadAllText(b) == "one" ? 1 : 0));

        // ============ 5) Copy(missingSrc, dst) -> FileNotFoundException =
        // 源缺失: the failure type is exact, the dst is NOT created.
        bool f5 = false;
        try { File.Copy(m, c); }
        catch (FileNotFoundException ex) { f5 = ex.GetType().Name == "FileNotFoundException"; }
        Console.WriteLine("f5_missing_throws_fnf=" + (f5 ? 1 : 0));
        Console.WriteLine("f5_missing_dst_not_created=" + (File.Exists(c) ? 0 : 1));

        // ============ 6) missing source caught by the IOException BASE ===
        // FileNotFoundException : IOException -- the base catch matches.
        bool f6 = false;
        try { File.Copy(m, c); } catch (IOException) { f6 = true; }
        Console.WriteLine("f6_missing_base_catch=" + (f6 ? 1 : 0));

        // ============ 7) Move success ============
        File.WriteAllText(c, "move");
        File.Move(c, d);
        Console.WriteLine("f7_move_src_gone=" + (File.Exists(c) ? 0 : 1));
        Console.WriteLine("f7_move_dst_exists=" + (File.Exists(d) ? 1 : 0));
        Console.WriteLine("f7_move_content=" + (File.ReadAllText(d) == "move" ? 1 : 0));

        // ============ 8) Move(missingSrc, dst) -> FileNotFoundException ==
        bool f8 = false;
        try { File.Move(m, c); } catch (FileNotFoundException) { f8 = true; }
        Console.WriteLine("f8_move_missing_throws=" + (f8 ? 1 : 0));

        // ============ 9) Move(src, existingDst) -> IOException; src kept ==
        // 目标存在: the source must survive a failed move on BOTH runtimes.
        File.WriteAllText(a, "src");
        File.WriteAllText(d, "dst");
        bool f9 = false;
        try { File.Move(a, d); } catch (IOException) { f9 = true; }
        Console.WriteLine("f9_move_dest_exists_throws=" + (f9 ? 1 : 0));
        Console.WriteLine("f9_move_src_kept=" + (File.Exists(a) ? 1 : 0));
        Console.WriteLine("f9_move_dst_unchanged=" + (File.ReadAllText(d) == "dst" ? 1 : 0));

        // ============ 10) Copy(src, src) -> IOException (dst exists) =====
        bool f10 = false;
        try { File.Copy(a, a); } catch (IOException) { f10 = true; }
        Console.WriteLine("f10_self_throws=" + (f10 ? 1 : 0));

        // cleanup (idempotent end)
        File.Delete(a); File.Delete(b); File.Delete(d);
    }
}}
