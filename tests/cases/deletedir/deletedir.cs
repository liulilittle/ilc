//------------------------------------------------------------------------------
// deletedir.cs -- System.IO.Directory.Delete DEEP e2e reference case (net48
// oracle, byte-for-byte).
//
// Exercises the wired Directory.Delete surface (MethodMap.IO.cs at HEAD
// 8572fa6, rtdel-b23):
//   1-arg Delete(path)       -> icall_directory_delete       (non-recursive)
//   2-arg Delete(path, bool) -> icall_directory_delete_recursive
//
// net48 semantics (csc-probe-pinned): Delete(empty,false) succeeds; a
// NON-empty dir with recursive==false throws IOException and leaves the tree
// intact; recursive==true removes the whole tree post-order; a MISSING path
// throws DirectoryNotFoundException (both modes); deleting a FILE path throws
// IOException; a null path throws ArgumentNullException.
//
// EXCEPTION-TRANSPORT NOTE (M3 convention): the runtime icalls do NOT throw
// the .NET exceptions -- they signal the failure with a non-zero status int
// (see io.cpp icall_directory_delete / icall_directory_delete_recursive).
// The probes below therefore print BOTH the caught-exception flag (the true
// net48 observable, which DIVERGES on the transpiled build) AND the
// POST-CONDITION state (Directory.Exists / File.Exists after the call, which
// IS byte-identical -- both runtimes leave the filesystem in the same final
// state). The flag lines are the recorded defect surface (see the batch
// report); the post-condition lines are the byte-stable contract.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases { public static class DeleteDir {
    public static void Main() {
        const string root = "deletedir_tmp";
        // idempotent start (re-runs must not leave artifacts)
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        // ===== 1. empty directory, recursive=false =====
        string empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        Console.WriteLine("empty_before=" + (Directory.Exists(empty) ? 1 : 0));
        try { Directory.Delete(empty, false); } catch (IOException) { }
        Console.WriteLine("empty_gone=" + (Directory.Exists(empty) ? 0 : 1));

        // ===== 2. empty directory, recursive=true =====
        Directory.CreateDirectory(empty);
        Directory.Delete(empty, true);
        Console.WriteLine("empty_true_gone=" + (Directory.Exists(empty) ? 0 : 1));

        // ===== 3. non-empty + recursive=false: net48 IOException, tree intact =====
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "f.txt"), "x");
        bool io = false;
        try { Directory.Delete(empty, false); } catch (IOException) { io = true; }
        Console.WriteLine("ne_false_io=" + io);          // net48 True / runtime False (divergence)
        Console.WriteLine("ne_false_intact=" + (Directory.Exists(empty) ? 1 : 0));
        Console.WriteLine("ne_false_file=" + (File.Exists(Path.Combine(empty, "f.txt")) ? 1 : 0));

        // ===== 4. non-empty + recursive=true: whole tree removed =====
        string deep = Path.Combine(empty, "d1", "d2");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "g.txt"), "y");
        Directory.Delete(empty, true);
        Console.WriteLine("ne_true_gone=" + (Directory.Exists(empty) ? 0 : 1));

        // ===== 5. missing path: net48 DirectoryNotFoundException (both modes) =====
        string missing = Path.Combine(root, "missing");
        bool dnfe = false;
        try { Directory.Delete(missing, false); } catch (DirectoryNotFoundException) { dnfe = true; }
        Console.WriteLine("missing_dnfe=" + dnfe);       // net48 True / runtime False (divergence)
        bool dnfe2 = false;
        try { Directory.Delete(missing, true); } catch (DirectoryNotFoundException) { dnfe2 = true; }
        Console.WriteLine("missing_true_dnfe=" + dnfe2); // net48 True / runtime False (divergence)
        Console.WriteLine("missing_still_absent=" + (Directory.Exists(missing) ? 0 : 1));

        // ===== 6. deleting a FILE path: net48 IOException, file stays =====
        string fpath = Path.Combine(root, "afile.txt");
        File.WriteAllText(fpath, "z");
        bool fio = false;
        try { Directory.Delete(fpath, false); } catch (IOException) { fio = true; }
        Console.WriteLine("filepath_io=" + fio);         // net48 True / runtime False (divergence)
        Console.WriteLine("filepath_remains=" + (File.Exists(fpath) ? 1 : 0));
        File.Delete(fpath);

        // ===== 7. null path: net48 ArgumentNullException =====
        bool ane = false;
        try { Directory.Delete((string)null, false); } catch (ArgumentNullException) { ane = true; }
        Console.WriteLine("nullpath_ane=" + ane);        // net48 True / runtime False (divergence)

        // ===== 8. nested tree with files at every level, recursive=true =====
        Directory.CreateDirectory(Path.Combine(root, "t", "a"));
        Directory.CreateDirectory(Path.Combine(root, "t", "b"));
        string tx = Path.Combine(Path.Combine(root, "t"), "x.txt");
        string tya = Path.Combine(Path.Combine(root, "t"), "a");
        File.WriteAllText(tx, "1");
        File.WriteAllText(Path.Combine(tya, "y.txt"), "2");
        Console.WriteLine("tree_built=" + (Directory.Exists(Path.Combine(root, "t", "a")) ? 1 : 0));
        Directory.Delete(Path.Combine(root, "t"), true);
        Console.WriteLine("tree_gone=" + (Directory.Exists(Path.Combine(root, "t")) ? 0 : 1));

        // ===== 9. root cleanup (idempotent end) =====
        Directory.Delete(root, true);
        Console.WriteLine("root_gone=" + (Directory.Exists(root) ? 0 : 1));
    }
}}
