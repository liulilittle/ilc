// iocase.cs -- System.IO base e2e reference case (net48 baseline).
//
// Exercises the System.IO foundation that the runtime icalls in io.cpp
// implement: File.Exists/Delete/ReadAllText/WriteAllText/ReadAllLines/
// WriteAllLines, Path.GetFileName/GetExtension/Combine, and Directory.
// Exists/CreateDirectory. The output is fully deterministic (fixed relative
// directory + fixed file names, relative to the process working directory),
// so the reference run can be diffed byte-for-byte against the transpiled
// output once the BCL mapping lands.
//
// Path semantics note: every path is built with Path.Combine (backslash
// separators -- the Windows/.NET Framework 4.8 convention). The case writes
// files, reads them back, lists file names via Path.GetFileName, prints the
// Path function results, then cleans up its artifacts (including the
// temporary directory) so re-runs are idempotent.

using System;
using System.IO;

namespace Cases { public static class IoCase {
    public static void Main() {
        const string dir = "iocase_tmp";
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "data.txt");

        // --- File.Exists / WriteAllText / ReadAllText round-trip ---
        Console.WriteLine("exists_before=" + (File.Exists(file) ? 1 : 0));
        File.WriteAllText(file, "hello file");
        Console.WriteLine("exists_after=" + (File.Exists(file) ? 1 : 0));
        Console.WriteLine("read=" + File.ReadAllText(file));

        // --- Overwrite truncates + empty content ---
        File.WriteAllText(file, "second");
        Console.WriteLine("read_overwrite=" + File.ReadAllText(file));
        File.WriteAllText(file, "");
        Console.WriteLine("read_empty=" + (File.ReadAllText(file).Length == 0 ? 1 : 0));

        // --- WriteAllLines / ReadAllLines ---
        string[] lines = new string[] { "one", "two", "three" };
        File.WriteAllLines(file, lines);
        string[] back = File.ReadAllLines(file);
        Console.WriteLine("line_count=" + back.Length);
        for (int i = 0; i < back.Length; ++i) {
            Console.WriteLine("line_" + i + "=" + back[i]);
        }

        // --- Path.GetFileName (list file names from paths) ---
        Console.WriteLine("name=" + Path.GetFileName(file));
        Console.WriteLine("name_nested=" + Path.GetFileName("x\\y\\z.txt"));
        Console.WriteLine("name_trailing=" + Path.GetFileName("x\\y\\"));
        Console.WriteLine("name_plain=" + Path.GetFileName("plain"));

        // --- Path.GetExtension ---
        Console.WriteLine("ext=" + Path.GetExtension(file));
        Console.WriteLine("ext_none=" + (Path.GetExtension("plain").Length == 0 ? 1 : 0));
        Console.WriteLine("ext_hidden=" + (Path.GetExtension(".gitignore").Length == 0 ? 1 : 0));
        Console.WriteLine("ext_dot=" + Path.GetExtension("file."));

        // --- Path.Combine ---
        Console.WriteLine("combine=" + Path.Combine("a", "b"));
        Console.WriteLine("combine_lead=" + Path.Combine("a", "\\b"));
        Console.WriteLine("combine_trail=" + Path.Combine("a\\", "b"));
        Console.WriteLine("combine_empty1=" + Path.Combine("", "b"));
        Console.WriteLine("combine_empty2=" + Path.Combine("a", ""));

        // --- Directory.Exists / CreateDirectory (recursive) ---
        Console.WriteLine("dir_exists=" + (Directory.Exists(dir) ? 1 : 0));
        Console.WriteLine("nested_before=" + (Directory.Exists(Path.Combine(dir, "nested")) ? 1 : 0));
        Directory.CreateDirectory(Path.Combine(dir, "nested", "deep"));
        Console.WriteLine("nested_after=" + (Directory.Exists(Path.Combine(dir, "nested")) ? 1 : 0));
        Console.WriteLine("deep_after=" + (Directory.Exists(Path.Combine(dir, "nested", "deep")) ? 1 : 0));
        Console.WriteLine("missing_dir=" + (Directory.Exists(Path.Combine(dir, "nothere")) ? 1 : 0));

        // --- File.Delete: success + silent success on missing files ---
        File.Delete(file);
        Console.WriteLine("exists_deleted=" + (File.Exists(file) ? 1 : 0));
        File.Delete(file);
        Console.WriteLine("exists_double_delete=" + (File.Exists(file) ? 1 : 0));

        // --- Cleanup: remove the whole tree so re-runs are idempotent ---
        Directory.Delete(dir, true);
        Console.WriteLine("dir_exists_final=" + (Directory.Exists(dir) ? 1 : 0));
    }
}}
