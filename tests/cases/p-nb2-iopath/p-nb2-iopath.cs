//------------------------------------------------------------------------------
// p-nb2-iopath.cs -- batch nb2 IO-PATH2 difftest probe
// (net48 oracle == transpiled runtime byte-exact).
//
// Pins the System.IO.Path holes closed by IO-PATH2:
//   * GetTempPath / GetFullPath / HasExtension -- runtime pathproc.h had
//     implementations but MethodMap.IO.cs had no arms (silent phantom ->
//     C++ compile error).
//   * GetDirectoryName / GetFileNameWithoutExtension -- NEW runtime icalls
//     (both ends were missing).
//   * Combine 4-parameter overload -- the 4-arg form was LOUD; the
//     params-array form had no arm.
//
// GetFullPath / GetCurrentDirectory-family records depend on the process
// working directory -- the diff harness runs BOTH executables from the SAME
// cwd (difftest Invoke-RawRun inherits the host PowerShell cwd), so a
// relative-input record like [io.full]=<cwd>\sub is byte-identical on both
// sides. GetDirectoryName edge cases follow net48: "" -> ArgumentException;
// "\\" -> null; a trailing separator -> the parent without it.
//------------------------------------------------------------------------------
using System;
using System.IO;

namespace Cases
{
    public static class PNb2IoPath
    {
        static string P(string s)
        {
            if (s == null) return "<null>";
            if (s.Length == 0) return "<empty>";
            return s;
        }

        public static void Main()
        {
            // GetTempPath: the OS temp dir with a trailing separator.
            Console.WriteLine("[io.temp]=" + Path.GetTempPath());
            // GetFullPath: relative input resolved against the cwd.
            Console.WriteLine("[io.full]=" + Path.GetFullPath("sub"));
            // HasExtension.
            Console.WriteLine("[io.hasxt]=" + (Path.HasExtension("a.txt") ? "Y" : "N"));
            Console.WriteLine("[io.hasxt0]=" + (Path.HasExtension("noext") ? "Y" : "N"));
            Console.WriteLine("[io.hasxtdot]=" + (Path.HasExtension(".git") ? "Y" : "N"));
            // GetDirectoryName edge cases.
            Console.WriteLine("[io.dir1]=" + P(Path.GetDirectoryName(@"a\b\c.txt")));
            Console.WriteLine("[io.dir2]=" + P(Path.GetDirectoryName(@"plain")));
            Console.WriteLine("[io.dir3]=" + P(Path.GetDirectoryName(@"\a\b")));
            Console.WriteLine("[io.dir5]=" + P(Path.GetDirectoryName(@"a\b")));
            Console.WriteLine("[io.dirsingle]=" + P(Path.GetDirectoryName(@"\a")));
            Console.WriteLine("[io.dirroot]=" + P(Path.GetDirectoryName(@"\")));
            Console.WriteLine("[io.dirtrail]=" + P(Path.GetDirectoryName(@"a\b\")));
            try
            {
                Console.WriteLine("[io.dirempty]=" + P(Path.GetDirectoryName("")));
            }
            catch (Exception e)
            {
                Console.WriteLine("[io.dirempty]=AE:" + e.GetType().Name);
            }
            // GetFileNameWithoutExtension.
            Console.WriteLine("[io.fnwo]=" + Path.GetFileNameWithoutExtension(@"a\b\c.txt"));
            Console.WriteLine("[io.fnwo.nodot]=" + Path.GetFileNameWithoutExtension("name"));
            Console.WriteLine("[io.fnwo.git]=" + P(Path.GetFileNameWithoutExtension(".git")));
            // Combine 4-parameter overload + params-array form.
            Console.WriteLine("[io.cmb4]=" + Path.Combine("a", "b", "c", "d"));
            Console.WriteLine("[io.cmbarr]=" + Path.Combine(new[] { "a", "b", "c" }));
            Console.WriteLine("[io.cmb2]=" + Path.Combine("a", "b"));
        }
    }
}