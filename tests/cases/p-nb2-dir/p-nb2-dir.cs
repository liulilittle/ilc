//------------------------------------------------------------------------------
// p-nb2-dir.cs -- batch nb2 DIR-FILES difftest probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Pins System.IO.Directory.GetFiles / GetDirectories / GetCurrentDirectory
// (all four mapping arms were missing -- phantom symbols, nb2 bw_build.log:
// Directory_GetFiles_... not declared), over a FIXED temporary directory the
// probe recreates fresh at the start of every run so the enumeration order
// is deterministic: a.txt, b.bin, c.txt in the root and sub/d.txt in a
// subdirectory. Both executables enumerate the SAME directory contents via
// the SAME (FindFirstFileW) OS enumeration on the same host, so the
// returned order is byte-identical on both sides. The runtime icalls return
// full paths (net48 Directory.GetFiles returns the path + name); the probe
// prints them verbatim (the temp-path prefix is identical for both
// processes at diff time).
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases
{
    public static class PNb2Dir
    {
        static string Root;

        static string Join(string[] full)
        {
            var parts = new string[full.Length];
            for (int i = 0; i < full.Length; i++)
                parts[i] = Path.GetFileName(full[i]);
            return string.Join("|", parts);
        }

        static void Reset()
        {
            Root = Path.Combine(Path.GetTempPath(), "nb2dirprobe");
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(Root, "b.bin"), "b");
            File.WriteAllText(Path.Combine(Root, "c.txt"), "c");
            Directory.CreateDirectory(Path.Combine(Root, "sub"));
            File.WriteAllText(Path.Combine(Root, "sub", "d.txt"), "d");
        }

        public static void Main()
        {
            Reset();

            // GetFiles(path): all 3 root files.
            Console.WriteLine("[io.gf.all]=" + Directory.GetFiles(Root).Length);
            Console.WriteLine("[io.gf.joined]=" + Join(Directory.GetFiles(Root)));
            // GetFiles(path, pattern).
            Console.WriteLine("[io.gf.txt]=" + Join(Directory.GetFiles(Root, "*.txt")));
            // GetFiles(path, pattern, SearchOption.AllDirectories): 4 files.
            Console.WriteLine("[io.gf.ro]=" + Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length);
            // GetDirectories(path).
            Console.WriteLine("[io.gd.sub]=" + Join(Directory.GetDirectories(Root)));
            // GetDirectories(path, pattern, AllDirectories).
            Console.WriteLine("[io.gd.all]=" + Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Length);
            // GetCurrentDirectory: the process cwd (identical for both executables).
            Console.WriteLine("[io.cwd]=" + Directory.GetCurrentDirectory());

            // Best-effort cleanup so a stale dir never perturbs a later run.
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}