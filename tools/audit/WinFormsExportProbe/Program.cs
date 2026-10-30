// WinFormsExportProbe -- entry-point existence evidence for the net48
// System.Windows.Forms / System.Drawing P/Invoke audit.
//
// For every (module, entrypoint) row of the graph TSV this tool asks the real
// Windows loader which spellings exist:
//   bare   -- GetProcAddress(module, name)             (what the transpiler resolves)
//   W      -- GetProcAddress(module, name + "W")
//   A      -- GetProcAddress(module, name + "A")
// That is what .NET's DllImport binder does when ExactSpelling=false: try the
// CharSet suffix first, then the bare name. A row with bare=missing but
// W=found is a P/Invoke that transpiles cleanly and then throws
// EntryPointNotFoundException at runtime.
//
// usage: WinFormsExportProbe <pinvokes.tsv> <out.tsv>

using System.Reflection.PortableExecutable;
using System.Text;

namespace WinFormsExportProbe;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string path);

    [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: WinFormsExportProbe <pinvokes.tsv> <out.tsv>");
            return 2;
        }

        var lines = File.ReadAllLines(args[0]);
        var header = lines[0].Split('\t');
        var moduleCol = Array.IndexOf(header, "module");
        var entryCol = Array.IndexOf(header, "entrypoint");
        var charsetCol = Array.IndexOf(header, "charset");
        var exactCol = Array.IndexOf(header, "exactSpelling");
        var ownerCol = Array.IndexOf(header, "owner");

        var handles = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("module\tentrypoint\tcharset\texactSpelling\towner\tbare\tW\tA\tverdict");

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = lines[i].Split('\t');
            var module = f[moduleCol];
            var entry = f[entryCol];
            var charset = f[charsetCol];
            var exact = f[exactCol];
            var owner = ownerCol >= 0 ? f[ownerCol] : "";

            if (!handles.TryGetValue(module, out var h))
            {
                h = LoadLibraryW(module);
                handles[module] = h;
            }

            var bare = h != IntPtr.Zero && GetProcAddress(h, entry) != IntPtr.Zero;
            var w = h != IntPtr.Zero && GetProcAddress(h, entry + "W") != IntPtr.Zero;
            var a = h != IntPtr.Zero && GetProcAddress(h, entry + "A") != IntPtr.Zero;

            string verdict;
            if (bare) verdict = "bare";
            else if (w) verdict = "only-W";
            else if (a) verdict = "only-A";
            else verdict = "missing";
            if (h == IntPtr.Zero) verdict = "no-module";

            counts[verdict] = counts.GetValueOrDefault(verdict) + 1;
            sb.AppendLine(string.Join('\t', module, entry, charset, exact, owner,
                bare ? "1" : "0", w ? "1" : "0", a ? "1" : "0", verdict));
        }

        File.WriteAllText(args[1], sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("rows=" + (lines.Length - 1));
        foreach (var kv in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Console.WriteLine("  " + kv.Key + "=" + kv.Value);
        return 0;
    }
}
