// ioaudit5.cs -- G6 static-audit suspicious point #5: Path.GetFileName /
// GetExtension with a DRIVE PREFIX. net48 (reference source) treats ':' as a
// volume separator: "C:file.txt" -> "file.txt". The runtime only splits on
// '/' and '\\' (io.cpp FileNameSegment).
//
// Deterministic output: bools only.
using System;
using System.IO;

namespace Cases { public static class IoAudit5 {
    public static void Main() {
        Console.WriteLine("gfn_drive=" + (Path.GetFileName("C:file.txt") == "file.txt"));
        Console.WriteLine("gfn_drive_only=" + (Path.GetFileName("C:") == ""));
        Console.WriteLine("gfn_rel_drive=" + (Path.GetFileName("a:b.txt") == "b.txt"));
        Console.WriteLine("gext_drive=" + (Path.GetExtension("C:file.txt") == ".txt"));
        Console.WriteLine("gext_drive_only=" + (Path.GetExtension("C:") == ""));
    }
}}
