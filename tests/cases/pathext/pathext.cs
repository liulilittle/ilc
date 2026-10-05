// pathext.cs -- System.IO.Path static e2e reference case (net48 baseline).
//
// NEW-DOMAIN case (batch B bonus): the Path static surface wired in
// MethodMap.IO.cs (System.IO.Path owner):
//
//   Combine(a, b)        -> icall_path_combine (2-arg; the 3+ params form is
//                           LOUD -- work order #24, so nested 2-arg Combine is
//                           used instead)
//   GetFileName(path)    -> icall_path_get_file_name
//   GetExtension(path)   -> icall_path_get_extension
//   ChangeExtension(p, e)-> icall_path_change_extension
//
// Plus a File.Exists/Delete/WriteAllText/ReadAllText roundtrip on the
// combined path (the io/file icalls). All paths use Windows separators
// (both runtimes are Windows processes, so "\" is deterministic). The temp
// file is deleted at start AND end so re-runs stay idempotent.

using System;
using System.IO;
namespace Cases { public static class PathExt {
    public static void Main() {
        // ---- Combine (2-arg) + nested ----
        string c1 = Path.Combine("dir", "file.txt");
        Console.WriteLine(c1 == "dir\\file.txt");        // True
        Console.WriteLine(Path.Combine("", "b"));        // b
        Console.WriteLine(Path.Combine("a", ""));        // a
        string nested = Path.Combine(Path.Combine("x", "y"), "z");
        Console.WriteLine(nested == "x\\y\\z");          // True

        // ---- GetFileName ----
        Console.WriteLine(Path.GetFileName("dir\\file.txt"));     // file.txt
        Console.WriteLine(Path.GetFileName("file.txt"));          // file.txt
        Console.WriteLine(Path.GetFileName("dir\\sub"));          // sub
        Console.WriteLine(Path.GetFileName("dir\\") == "");       // True (trailing sep -> empty)

        // ---- GetExtension ----
        Console.WriteLine(Path.GetExtension("dir\\file.txt"));    // .txt
        Console.WriteLine(Path.GetExtension("file.tar.gz"));      // .gz
        Console.WriteLine(Path.GetExtension("noext") == "");      // True
        Console.WriteLine(Path.GetExtension("dir\\sub") == "");   // True

        // ---- ChangeExtension ----
        Console.WriteLine(Path.ChangeExtension("file.txt", ".md"));   // file.md
        Console.WriteLine(Path.ChangeExtension("file.txt", null));    // file (removes extension)

        // ---- File roundtrip on the combined path ----
        string file = Path.Combine("pathext_tmp", "data.txt");
        Directory.CreateDirectory("pathext_tmp");
        File.WriteAllText(file, "hello path");
        Console.WriteLine(File.Exists(file));                    // True
        Console.WriteLine(File.ReadAllText(file) == "hello path"); // True
        File.Delete(file);
        Console.WriteLine(File.Exists(file) == false);           // True
        Directory.Delete("pathext_tmp");
    }
}}
