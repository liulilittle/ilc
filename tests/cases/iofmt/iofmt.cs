// iofmt.cs -- IO + formatting composite e2e reference case (net48 baseline).
//
// Two domains, all probes print bools (0/1) or a deterministic int so the
// diff stays byte-stable across console encodings:
//
// 1) StringWriter Write-variant widening (stringrwcase already covers the
//    basic Write/WriteLine/ToString): chained Write(string), Write(null) /
//    Write("") no-ops, WriteLine(string) (+CRLF), WriteLine(null) (bare
//    CRLF), ToString snapshot semantics (a copy -- repeatable, not a live
//    view) and a writer -> reader roundtrip. NOTE: the non-string Write
//    overloads (int/double/bool/char/char[]) are NOT wired -- LOUD
//    rejections in MethodMap.StringIO.cs, see GAPS.md.
//
// 2) Numeric formatting written to a file: File.WriteAllText with the
//    Concat(string, object) numeric conveniences (int/double/bool -- the
//    filestreamcase-proven 2-arg shape), plus a multi-line formatted file
//    built from WriteAllText + AppendAllText and read back byte-identically.
//
// All paths are relative to the process working directory; the temp FILE is
// removed at start AND end so re-runs stay idempotent.

using System;
using System.IO;

namespace Cases { public static class IofmtCase {
    public static void Main() {
        // ---- StringWriter Write variants (wired string surface only) ----
        StringWriter w = new StringWriter();
        Console.WriteLine("sw_empty=" + (w.ToString() == "" ? 1 : 0));
        w.Write("a"); w.Write("b"); w.Write("c");
        Console.WriteLine("sw_chain=" + (w.ToString() == "abc" ? 1 : 0));
        w.Write((string)null);
        Console.WriteLine("sw_null_noop=" + (w.ToString() == "abc" ? 1 : 0));
        w.Write("");
        Console.WriteLine("sw_empty_noop=" + (w.ToString() == "abc" ? 1 : 0));
        w.WriteLine("d");
        Console.WriteLine("sw_line=" + (w.ToString() == "abc" + Environment.NewLine + "d" ? 1 : 0));
        w.WriteLine((string)null);
        Console.WriteLine("sw_nullline=" + (w.ToString() == "abc" + Environment.NewLine + "d" + Environment.NewLine ? 1 : 0));
        string snap = w.ToString();
        w.Write("e");
        Console.WriteLine("sw_snapshot_copy=" + (snap == "abc" + Environment.NewLine + "d" + Environment.NewLine ? 1 : 0));
        Console.WriteLine("sw_after_e=" + (w.ToString() == "abc" + Environment.NewLine + "d" + Environment.NewLine + "e" ? 1 : 0));
        StringWriter w2 = new StringWriter();
        w2.WriteLine("x"); w2.Write("y"); w2.WriteLine("z");
        Console.WriteLine("sw_multi=" + (w2.ToString() == "x" + Environment.NewLine + "y" + Environment.NewLine + "z" + Environment.NewLine ? 1 : 0));
        StringReader r = new StringReader(w2.ToString());
        Console.WriteLine("sw_r_line1=" + (r.ReadLine() == "x" ? 1 : 0));
        Console.WriteLine("sw_r_rest=" + (r.ReadToEnd() == "y" + Environment.NewLine + "z" + Environment.NewLine ? 1 : 0));

        // ---- numeric formatting written to a file (WriteAllText) ----
        const string file = "iofmt_tmp.txt";
        File.Delete(file); // idempotent start: silent on missing
        int count = 42;
        int neg = -42;
        double ratio = 3.5;
        double negRatio = -2.25;
        double wide = 100.0;
        bool ok = true;
        File.WriteAllText(file, "count=" + count);
        Console.WriteLine("fmt_int_eq=" + (File.ReadAllText(file) == "count=42" ? 1 : 0));
        File.WriteAllText(file, "neg=" + neg);
        Console.WriteLine("fmt_neg_eq=" + (File.ReadAllText(file) == "neg=-42" ? 1 : 0));
        File.WriteAllText(file, "ratio=" + ratio);
        Console.WriteLine("fmt_double_eq=" + (File.ReadAllText(file) == "ratio=3.5" ? 1 : 0));
        File.WriteAllText(file, "negratio=" + negRatio);
        Console.WriteLine("fmt_negdouble_eq=" + (File.ReadAllText(file) == "negratio=-2.25" ? 1 : 0));
        File.WriteAllText(file, "wide=" + wide);
        Console.WriteLine("fmt_wide_eq=" + (File.ReadAllText(file) == "wide=100" ? 1 : 0));
        File.WriteAllText(file, "ok=" + ok);
        Console.WriteLine("fmt_bool_eq=" + (File.ReadAllText(file) == "ok=True" ? 1 : 0));
        // multi-line formatted file: WriteAllText + AppendAllText chain
        File.WriteAllText(file, "count=" + count);
        File.AppendAllText(file, "\nratio=" + ratio);
        File.AppendAllText(file, "\nok=" + ok);
        string all = File.ReadAllText(file);
        Console.WriteLine("fmt_multi_eq=" + (all == "count=42\nratio=3.5\nok=True" ? 1 : 0));
        Console.WriteLine("fmt_multi_len=" + all.Length);
        File.Delete(file); // idempotent end: the temp file must not linger
        Console.WriteLine("fmt_cleanup=" + (File.Exists(file) ? 0 : 1));
    }
}}
