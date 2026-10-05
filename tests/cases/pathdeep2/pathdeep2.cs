//------------------------------------------------------------------------------
// pathdeep2.cs -- System.IO.Path domain DEEP probe 2 (e2e batch C47):
// Combine multi-arg + relative paths + extension edges -- byte-exact vs the
// net48 oracle. Created after the path domain full chain landed (the 3-arg
// Path.Combine now resolves through the NestedCombineMarker in
// MethodMap.IO.cs + StackFold.cs -- work order #24 closed, where pathext's
// GAPS.md D-PATH-1 recorded it as LOUD).
//
// Wired surface (MethodMap.IO.cs System.IO.Path owner), all probes stay
// within it:
//   Combine(a, b)          -> icall_path_combine (2-arg)
//   Combine(a, b, c)       -> NestedCombineMarker -> nested 2-arg combine
//   GetFileName(path)      -> icall_path_get_file_name
//   GetExtension(path)     -> icall_path_get_extension
//   ChangeExtension(p, ext)-> icall_path_change_extension
//
// Combine semantics are the net48 concat rules (io.cpp): empty side wins,
// separator-led second part wins unchanged (rooted), trailing separator in
// the first part is reused, else "\" join. Combine performs NO normalization
// (".."/"." segments survive verbatim). GetExtension drops a trailing dot
// ("file." -> ""), keeps a dotfile's leading dot (".gitignore" -> 
// ".gitignore"), and the last dot wins ("file.tar.gz" -> ".gz").
// ChangeExtension splits at the LAST dot before any separator and re-adds a
// leading dot when the new extension lacks one; the "a\b.txt\" + "c" and
// "file." + null corners are the net48-pinned outputs recorded in
// pathproc.cpp (probe-verified 2026-08-03).
//
// net48 discipline: both runtimes are Windows processes, so "\" separators
// are deterministic. All paths stay relative (no cwd dependency). Every line
// of output is byte-exact.
//------------------------------------------------------------------------------
using System;
using System.IO;
namespace Cases { public static class PathDeep2 {
    public static void Main() {
        // =============== 1. Combine 3-arg (multi-arg overload, nested fold) ===============
        Console.WriteLine(Path.Combine("a", "b", "c") == "a\\b\\c");          // True
        Console.WriteLine(Path.Combine("dir", "sub", "file.txt") == "dir\\sub\\file.txt"); // True
        Console.WriteLine(Path.Combine("", "b", "c") == "b\\c");               // True (empty first)
        Console.WriteLine(Path.Combine("a", "b", "\\root") == "\\root");       // True (rooted third wins)
        Console.WriteLine(Path.Combine("a\\", "b", "c") == "a\\b\\c");         // True (trailing sep first)
        // 3-arg == nested 2-arg equivalence
        Console.WriteLine(Path.Combine("x", "y", "z") == Path.Combine(Path.Combine("x", "y"), "z")); // True
        // =============== 2. Combine 2-arg: relative paths + separators ===============
        Console.WriteLine(Path.Combine("a", "") == "a");                       // True (empty second)
        Console.WriteLine(Path.Combine("", "b") == "b");                       // True (empty first)
        Console.WriteLine(Path.Combine("a\\", "b") == "a\\b");                 // True (trailing sep reused)
        Console.WriteLine(Path.Combine("a", "\\b") == "\\b");                  // True (separator-led second)
        Console.WriteLine(Path.Combine("a", "b\\") == "a\\b\\");               // True (trailing sep second kept)
        Console.WriteLine(Path.Combine("a\\b\\", "c") == "a\\b\\c");           // True
        Console.WriteLine(Path.Combine("a", "b/c") == "a\\b/c");               // True (mixed separators kept)
        // relative dot segments are NOT normalized by Combine
        Console.WriteLine(Path.Combine("a", "..\\b") == "a\\..\\b");           // True
        Console.WriteLine(Path.Combine("a\\b", "..") == "a\\b\\..");           // True
        Console.WriteLine(Path.Combine(".", "file") == ".\\file");             // True
        Console.WriteLine(Path.Combine("a", ".\\b") == "a\\.\\b");             // True
        // =============== 3. GetFileName: segment extraction ===============
        Console.WriteLine(Path.GetFileName("dir\\file.txt") == "file.txt");    // True
        Console.WriteLine(Path.GetFileName("file.txt") == "file.txt");         // True
        Console.WriteLine(Path.GetFileName("a\\b\\c") == "c");                 // True
        Console.WriteLine(Path.GetFileName("dir\\") == "");                    // True (trailing sep -> empty)
        Console.WriteLine(Path.GetFileName("dir\\\\file.txt") == "file.txt");  // True (double sep)
        Console.WriteLine(Path.GetFileName("dir\\sub\\file.tar.gz") == "file.tar.gz"); // True
        Console.WriteLine(Path.GetFileName("dir\\file.") == "file.");          // True (trailing dot kept by GetFileName)
        // =============== 4. GetExtension: dot edge cases ===============
        Console.WriteLine(Path.GetExtension("dir\\file.txt") == ".txt");       // True
        Console.WriteLine(Path.GetExtension("file.tar.gz") == ".gz");          // True (last dot wins)
        Console.WriteLine(Path.GetExtension("file.") == "");                   // True (trailing dot is NOT an extension)
        Console.WriteLine(Path.GetExtension(".gitignore") == ".gitignore");    // True (dotfile keeps its leading dot)
        Console.WriteLine(Path.GetExtension("dir\\.hidden") == ".hidden");     // True
        Console.WriteLine(Path.GetExtension("dir\\file.TXT") == ".TXT");       // True (case preserved)
        Console.WriteLine(Path.GetExtension("noext") == "");                   // True
        Console.WriteLine(Path.GetExtension("dir\\sub") == "");                // True
        // =============== 5. ChangeExtension: replace / add / remove ===============
        Console.WriteLine(Path.ChangeExtension("file.txt", ".md") == "file.md");  // True (replace)
        Console.WriteLine(Path.ChangeExtension("file.txt", "md") == "file.md");  // True (dot auto-added)
        Console.WriteLine(Path.ChangeExtension("file.txt", null) == "file");     // True (remove)
        Console.WriteLine(Path.ChangeExtension("file.txt", "") == "file.");      // True (empty -> trailing dot)
        Console.WriteLine(Path.ChangeExtension("file.tar.gz", ".zip") == "file.tar.zip"); // True (last dot only)
        Console.WriteLine(Path.ChangeExtension("dir\\file", ".log") == "dir\\file.log"); // True
        Console.WriteLine(Path.ChangeExtension(".gitignore", "txt") == ".txt");  // True (net48-pinned)
        Console.WriteLine(Path.ChangeExtension("a\\b.txt\\", "c") == "a\\b.txt\\.c"); // True (net48-pinned)
        Console.WriteLine(Path.ChangeExtension("file.", null) == "file");        // True (net48-pinned)
        Console.WriteLine(Path.ChangeExtension("file.txt", ".txt") == "file.txt"); // True (same ext)
    }
}}
