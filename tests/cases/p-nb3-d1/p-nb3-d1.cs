//------------------------------------------------------------------------------
// p-nb3-d1.cs -- nb3 D1/D2/D3 difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins the three nb2 registered defects after the fixes:
//
//   D1 (io.cpp CollectDirectoryEntriesW): Directory.GetFiles(path, pattern,
//   SearchOption.AllDirectories) must DESCEND into every subdirectory even
//   when the subdirectory NAME does not match the pattern (net48: the pattern
//   filters the returned names only -- the recursion discovers subdirectories
//   with an UNFILTERED "*" search; the old Windows branch wove the pattern
//   into every per-level FindFirstFileW query, so "sub" / "zzz" never
//   matched "*.txt" and the descent stopped). Fixed-tree layout:
//   root = a.txt, b.bin; root/sub = d.txt; root/zzz = e.txt. Expected
//   result for *.txt AllDirectories: a.txt | d.txt | e.txt (FindFirstFileW
//   order on the same host -- byte-identical for oracle/runtime).
//
//   D2 (pathproc GetDirectoryName): a DRIVE ROOT ("C:\") and a BARE DRIVE
//   ("C:") both yield null (net48: a root is not a directory portion). The
//   old runtime returned "C:" for the former and "" for the latter. Control
//   records pin the neighbouring cases (drive subdir keeps "C:\",
//   "\\" -> null, "plain" -> "").
//
//   D3 (cultureinfo): GetCultureInfo("iw-IL") / GetCultureInfo("iw") must be
//   NON-NULL (a canonical table entry), and TwoLetterISOLanguageName must be
//   "iw" on both (the old legacy pin iw->he was removed because net48 keeps
//   "iw"). Controls pin the surviving legacy pins (no->nb) and the he-IL
//   two-letter code.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cases
{
    public static class PNb3D1
    {
        static string S(string v)
        {
            return v == null ? "<null>" : v;
        }

        static string Join(string[] full)
        {
            var parts = new string[full.Length];
            for (int i = 0; i < full.Length; i++)
                parts[i] = Path.GetFileName(full[i]);
            return string.Join("|", parts);
        }

        static string Root;

        static void Reset()
        {
            Root = Path.Combine(Path.GetTempPath(), "nb3d1probe");
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(Root, "b.bin"), "b");
            Directory.CreateDirectory(Path.Combine(Root, "sub"));
            File.WriteAllText(Path.Combine(Root, "sub", "d.txt"), "d");
            Directory.CreateDirectory(Path.Combine(Root, "zzz"));
            File.WriteAllText(Path.Combine(Root, "zzz", "e.txt"), "e");
        }

        static string CultureName(string name)
        {
            try { return "" + CultureInfo.GetCultureInfo(name).Name; }
            catch (Exception e) { return "THREW:" + e.GetType().Name; }
        }

        static string CultureTwoLetter(string name)
        {
            try { return "" + CultureInfo.GetCultureInfo(name).TwoLetterISOLanguageName; }
            catch (Exception e) { return "THREW:" + e.GetType().Name; }
        }

        public static void Main()
        {
            Reset();

            // D1: *.txt AllDirectories must descend past non-matching dirs.
            Console.WriteLine("[d1.gf.txt.ro]=" + Join(Directory.GetFiles(Root, "*.txt", SearchOption.TopDirectoryOnly)));
            Console.WriteLine("[d1.gf.txt.all]=" + Join(Directory.GetFiles(Root, "*.txt", SearchOption.AllDirectories)));
            // Cross-checks: "*" all-directories (4 files) and dir-name pattern
            // reached via a non-matching intermediate dir.
            Console.WriteLine("[d1.gf.star.all]=" + Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length);

            // D2: drive root / bare drive -> null; control cases.
            Console.WriteLine("[d2.root]=" + S(Path.GetDirectoryName(@"C:\")));
            Console.WriteLine("[d2.bare]=" + S(Path.GetDirectoryName(@"C:")));
            Console.WriteLine("[d2.drivedir]=" + S(Path.GetDirectoryName(@"C:\a")));
            Console.WriteLine("[d2.rootsep]=" + S(Path.GetDirectoryName(@"\")));
            Console.WriteLine("[d2.rel]=" + S(Path.GetDirectoryName(@"a\b")));
            Console.WriteLine("[d2.plain]=" + S(Path.GetDirectoryName("plain")));
            Console.WriteLine("[d2.uniroot]=" + S(Path.GetDirectoryName(@"\\")));

            // D3: iw canonical entries + two-letter pins.
            Console.WriteLine("[d3.iw.name]=" + CultureName("iw-IL"));
            Console.WriteLine("[d3.iw.2l]=" + CultureTwoLetter("iw-IL"));
            Console.WriteLine("[d3.iw2.name]=" + CultureName("iw"));
            Console.WriteLine("[d3.iw2.2l]=" + CultureTwoLetter("iw"));
            Console.WriteLine("[d3.he.2l]=" + CultureTwoLetter("he-IL"));
            Console.WriteLine("[d3.no.2l]=" + CultureTwoLetter("no"));
            Console.WriteLine("[d3.en.2l]=" + CultureTwoLetter("en-US"));

            // Best-effort cleanup so a stale dir never perturbs a later run.
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}