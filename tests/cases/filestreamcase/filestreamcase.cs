// filestreamcase.cs -- file stream deep-chain e2e reference case (net48 baseline).
//
// Exercises the WIRED System.IO surface: File.WriteAllText -> File.ReadAllText
// roundtrip (Chinese + digits), File.Exists/Delete (double delete stays
// silent), Path.GetFileName/GetExtension/Combine, Directory.CreateDirectory/
// Exists plus a nested file roundtrip inside the created directory.
//
// All probes print bools (0/1) or a deterministic int, never raw content, so
// the diff is byte-stable across console encodings. All paths are relative to
// the process working directory; the temp FILE is removed at start AND end so
// re-runs stay idempotent.
//
// GAPS.md wiring closures (2026-08-05): File.AppendAllText, Path.ChangeExtension,
// Directory.Delete and FileInfo.Length are wired -- their probes below are
// active (was: "deliberately NOT called here so the pipeline can run").
// Directory.Delete removes filestreamcase_dir at the end, so the temp
// directory is no longer left behind.

using System;
using System.IO;

namespace Cases { public static class FileStreamCase {
    public static void Main() {
        const string file = "filestreamcase_tmp.txt";
        const string dir = "filestreamcase_dir";
        File.Delete(file); // idempotent start: silent on missing
        Console.WriteLine("file_exists_before=" + (File.Exists(file) ? 1 : 0));

        // File.WriteAllText -> File.ReadAllText roundtrip (Chinese + digits).
        const string content = "文件IO深链123";
        File.WriteAllText(file, content);
        Console.WriteLine("file_exists_after_write=" + (File.Exists(file) ? 1 : 0));
        string read = File.ReadAllText(file);
        Console.WriteLine("roundtrip_equal=" + (read == content ? 1 : 0));
        Console.WriteLine("read_length=" + read.Length);

        // File.AppendAllText: writes "A", appends "B", reads back "AB".
        File.WriteAllText(file, "A");
        File.AppendAllText(file, "B");
        Console.WriteLine("append_roundtrip_equal=" + (File.ReadAllText(file) == "AB" ? 1 : 0));

        // File.Delete: exists -> gone; a second delete stays silent.
        File.Delete(file);
        Console.WriteLine("file_exists_after_delete=" + (File.Exists(file) ? 1 : 0));
        File.Delete(file);
        Console.WriteLine("file_exists_double_delete=" + (File.Exists(file) ? 1 : 0));

        // Path.GetFileName / GetExtension / Combine (wired 2-arg overload).
        Console.WriteLine("name_eq=" + (Path.GetFileName("dir\\data.txt") == "data.txt" ? 1 : 0));
        Console.WriteLine("name_trailing_empty=" + (Path.GetFileName("dir\\").Length == 0 ? 1 : 0));
        Console.WriteLine("ext_eq=" + (Path.GetExtension("data.txt") == ".txt" ? 1 : 0));
        Console.WriteLine("ext_none_empty=" + (Path.GetExtension("noext").Length == 0 ? 1 : 0));
        Console.WriteLine("combine_eq=" + (Path.Combine("a", "b") == "a\\b" ? 1 : 0));
        Console.WriteLine("combine_empty_eq=" + (Path.Combine("", "b") == "b" ? 1 : 0));

        // Path.ChangeExtension: .txt -> .dat replacement + null strip.
        Console.WriteLine("change_ext_eq=" + (Path.ChangeExtension("data.txt", ".dat") == "data.dat" ? 1 : 0));
        Console.WriteLine("change_ext_strip_eq=" + (Path.ChangeExtension("data.txt", null) == "data" ? 1 : 0));

        // Directory.CreateDirectory / Exists (wired) + nested file roundtrip.
        Directory.CreateDirectory(dir);
        Console.WriteLine("dir_exists_after_create=" + (Directory.Exists(dir) ? 1 : 0));
        string nested = Path.Combine(dir, "nested.txt");
        File.WriteAllText(nested, "嵌套文件");
        Console.WriteLine("nested_roundtrip_equal=" + (File.ReadAllText(nested) == "嵌套文件" ? 1 : 0));
        File.Delete(nested);
        Console.WriteLine("nested_deleted=" + (File.Exists(nested) ? 1 : 0));
        Console.WriteLine("dir_exists_final=" + (Directory.Exists(dir) ? 1 : 0));

        // Directory.Delete: create -> delete -> exists=false chain.
        Directory.Delete(dir);
        Console.WriteLine("dir_exists_after_delete=" + (Directory.Exists(dir) ? 1 : 0));

        // FileInfo.Length: size-after-write probe (5 ASCII bytes).
        File.WriteAllText(file, "12345");
        Console.WriteLine("fileinfo_length_eq=" + (new FileInfo(file).Length == 5 ? 1 : 0));
        Console.WriteLine("fileinfo_length=" + new FileInfo(file).Length);
        File.Delete(file); // idempotent end: the temp file must not linger
    }
}}
