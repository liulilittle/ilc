// ioaudit1.cs -- G6 static-audit suspicious point #1: NON-ASCII directory
// names through Directory.CreateDirectory / Exists / Delete (net48 baseline).
//
// Static finding: the Windows CreateDirectory path uses narrow CRT
// ::_mkdir with UTF-8 bytes (io.cpp IL2CPP_IO_MKDIR), while Directory.Exists
// probes with the wide stat, so a CJK directory name cannot round-trip.
//
// Deterministic output: bools only, no path echo, no timing.
using System;
using System.IO;

namespace Cases { public static class IoAudit1 {
    public static void Main() {
        const string dir = "ioaudit1_\u4E2D\u6587\u76EE\u5F55";
        // best-effort cleanup of the ASCII-visible name (idempotent re-runs)
        if (Directory.Exists(dir)) Directory.Delete(dir);

        Directory.CreateDirectory(dir);
        Console.WriteLine("cn_exists=" + Directory.Exists(dir));

        bool del = false;
        try {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir);
                del = !Directory.Exists(dir);
            }
        } catch (IOException) { del = false; }
        Console.WriteLine("cn_delete_ok=" + del);
    }
}}
