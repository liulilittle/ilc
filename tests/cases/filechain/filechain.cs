// filechain.cs -- file IO chain e2e reference case (net48 baseline).
//
// Exercises the full write -> read-back -> compare -> delete chain plus
// Path.Combine/GetFileName/GetExtension assertions, with UTF-8 multi-line
// content (Chinese + digits). The output is fully deterministic: all probes
// print bools (0/1) or ints, never raw content, so the diff is byte-stable
// across console encodings. All paths are relative to the process working
// directory; the temp file is removed at start AND end so re-runs (and
// leftover files from a crashed run) stay idempotent.

using System;
using System.IO;

namespace Cases { public static class FileChainCase {
    public static void Main() {
        const string file = "filechain_tmp.txt";
        File.Delete(file); // idempotent start: silent on missing
        Console.WriteLine("exists_before=" + (File.Exists(file) ? 1 : 0));

        // Multi-line UTF-8 content: Chinese + digits, CRLF line endings.
        const string content = "第一行\r\n第二行123\r\n第三行ABC456";
        File.WriteAllText(file, content);
        Console.WriteLine("exists_after_write=" + (File.Exists(file) ? 1 : 0));

        // Read back and compare (string equality -> bool).
        string read = File.ReadAllText(file);
        Console.WriteLine("roundtrip_equal=" + (read == content ? 1 : 0));
        Console.WriteLine("read_length=" + read.Length);

        // Line split: ReadAllLines must see the CRLF-bound lines.
        string[] lines = File.ReadAllLines(file);
        Console.WriteLine("line_count=" + lines.Length);
        Console.WriteLine("line0_equal=" + (lines[0] == "第一行" ? 1 : 0));
        Console.WriteLine("line1_equal=" + (lines[1] == "第二行123" ? 1 : 0));
        Console.WriteLine("line2_equal=" + (lines[2] == "第三行ABC456" ? 1 : 0));

        // Path.Combine bool assertions (Windows separators).
        Console.WriteLine("combine_eq=" + (Path.Combine("a", "b") == "a\\b" ? 1 : 0));
        Console.WriteLine("combine_empty_eq=" + (Path.Combine("", "b") == "b" ? 1 : 0));

        // Path.GetFileName bool assertions.
        Console.WriteLine("name_eq=" + (Path.GetFileName("dir\\data.txt") == "data.txt" ? 1 : 0));
        Console.WriteLine("name_trailing_empty=" + (Path.GetFileName("dir\\").Length == 0 ? 1 : 0));

        // Path.GetExtension bool assertions.
        Console.WriteLine("ext_eq=" + (Path.GetExtension("data.txt") == ".txt" ? 1 : 0));
        Console.WriteLine("ext_none_empty=" + (Path.GetExtension("noext").Length == 0 ? 1 : 0));

        // Delete: exists -> gone; a second delete stays silent.
        File.Delete(file);
        Console.WriteLine("exists_after_delete=" + (File.Exists(file) ? 1 : 0));
        File.Delete(file);
        Console.WriteLine("exists_double_delete=" + (File.Exists(file) ? 1 : 0));
    }
}}
