//------------------------------------------------------------------------------
// probe-entrypoint-suffix.cs -- CharSet A/W entry-point suffix probing
// (net48 oracle == transpiled runtime, byte-exact stdout).
//
// net48 never binds a [DllImport] at link time: it LoadLibrary's the module and
// walks a candidate list derived from the import attributes. Measured on
// Framework64 v4.0.30319 (dispatcher probe + this case):
//
//   ExactSpelling = true                 -> [EntryPoint]                only
//   CharSet in {None, Ansi}, Exact=false -> [EntryPoint, +"A", +"W"]
//   CharSet in {Unicode, Auto}, Exact=false -> [EntryPoint, +"W"]
//
//   * a miss throws EntryPointNotFoundException whose message quotes the
//     ORIGINAL EntryPoint ("FindWindow", never "FindWindowW");
//   * a missing MODULE throws DllNotFoundException (unchanged);
//   * the probe ORDER of "A" vs "W" is measured by this case on the self-built
//     `suffixprobe.dll` (which exports both, with different results): the
//     net48 oracle and the transpiled runtime must agree byte-exactly, so this
//     case is the order's falsifier -- not an assumption.
//
// Every system-DLL entry below is `only-W` in
// docs/audit/winforms-pinvoke-current.md section 2.2 and NON-BLOCKING (no
// MessageBox: a modal dialog would hang the harness).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class ProbeEntrypointSuffix
    {
        // -- only-W system-DLL entries (FindWindow is only-W; a class name that
        //    cannot exist returns NULL instead of opening a window) ------------
        [DllImport("user32.dll", EntryPoint = "FindWindow")]
        private static extern IntPtr FindWindowDefault(string cls, string title);

        [DllImport("user32.dll", EntryPoint = "FindWindow", CharSet = CharSet.Ansi)]
        private static extern IntPtr FindWindowAnsi(string cls, string title);

        [DllImport("user32.dll", EntryPoint = "FindWindow", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowUnicode(string cls, string title);

        // -- ExactSpelling disables probing: the bare name is not exported ----
        [DllImport("user32.dll", EntryPoint = "FindWindow",
                   CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr FindWindowExactSpelling(string cls, string title);

        [DllImport("user32.dll", EntryPoint = "FindWindow",
                   CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr FindWindowExactUnicode(string cls, string title);

        // -- second only-W entry, for the reverse-order cache-identity check --
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtrBare(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", ExactSpelling = true)]
        private static extern IntPtr GetWindowLongPtrExactSpelling(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        // -- missing module (DllNotFoundException path) -----------------------
        [DllImport("nosuchdll_xyz.dll")]
        private static extern int NoSuchApiXyz(int x);

        // -- SELF-BUILT probe DLL: exports ProbeA/ProbeW (11 / 22) and
        //    Probe2A/Probe2W (31 / 32) so the A-vs-W order is OBSERVABLE ------
        [DllImport("suffixprobe.dll", EntryPoint = "Probe", CharSet = CharSet.Ansi)]
        private static extern int SuffixProbeAnsi();

        [DllImport("suffixprobe.dll", EntryPoint = "Probe2", CharSet = CharSet.Unicode)]
        private static extern int SuffixProbeUnicode();

        [DllImport("suffixprobe.dll", EntryPoint = "Probe",
                   CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern int SuffixProbeExact();

        private const int GWL_STYLE = -16;

        public static void Main()
        {
            // 1. only-W resolution succeeds (net48 measures "FindWindow" -> "W").
            bool defaultZero = FindWindowDefault("NoSuchClassXyz", null) == IntPtr.Zero;
            Console.WriteLine("[sys.default.onlyw]=" + defaultZero);

            bool ansiZero = FindWindowAnsi("NoSuchClassXyz", null) == IntPtr.Zero;
            Console.WriteLine("[sys.ansi.onlyw]=" + ansiZero);

            bool unicodeZero = FindWindowUnicode("NoSuchClassXyz", null) == IntPtr.Zero;
            Console.WriteLine("[sys.unicode.onlyw]=" + unicodeZero);

            // Repeated call = cached symbol (no second probe).
            bool cachedZero = FindWindowAnsi("NoSuchClassXyz", null) == IntPtr.Zero;
            Console.WriteLine("[sys.ansi.onlyw.cached]=" + cachedZero);

            // 2. ExactSpelling = true disables probing -> EntryPointNotFoundException,
            //    and the message names the ORIGINAL entry point.
            string exactAnsi = CatchName(delegate { FindWindowExactSpelling("NoSuchClassXyz", null); });
            Console.WriteLine("[exact.ansi]=" + exactAnsi);
            string exactUnicode = CatchName(delegate { FindWindowExactUnicode("NoSuchClassXyz", null); });
            Console.WriteLine("[exact.unicode]=" + exactUnicode);

            // 3. Missing module -> DllNotFoundException.
            string missingDll = CatchName(delegate { NoSuchApiXyz(1); });
            Console.WriteLine("[dllmissing]=" + missingDll);

            // 4. Cache identity, order A-then-B: the exact import runs FIRST (it
            //    throws, storing nothing), then the probing import must still
            //    resolve. A (dll, entry)-only cache key would let either order
            //    leak into the other.
            Console.WriteLine("[cache.pair1.order]=exact-first");
            string pair1Exact = CatchName(delegate { FindWindowExactSpelling("NoSuchClassXyz", null); });
            Console.WriteLine("[cache.pair1.exact]=" + pair1Exact);
            bool pair1Bare = FindWindowDefault("NoSuchClassXyz", null) == IntPtr.Zero;
            Console.WriteLine("[cache.pair1.probing_ok]=" + pair1Bare);

            // 5. Cache identity, order B-then-A: the probing import runs FIRST
            //    (it stores the resolved "W" symbol), then the exact import must
            //    STILL throw -- it must never observe the probing import's
            //    cached symbol.
            Console.WriteLine("[cache.pair2.order]=probing-first");
            IntPtr hwndDesktop = GetDesktopWindow();
            long style = GetWindowLongPtrBare(hwndDesktop, GWL_STYLE).ToInt64();
            Console.WriteLine("[cache.pair2.probing_ok]=" + (style != 0));
            string pair2Exact = CatchName(delegate { GetWindowLongPtrExactSpelling(hwndDesktop, GWL_STYLE); });
            Console.WriteLine("[cache.pair2.exact]=" + pair2Exact);

            bool sameTerminalState = pair1Exact == pair2Exact;
            Console.WriteLine("[cache.same_terminal_state]=" + sameTerminalState);

            // 6. Probe order over a SELF-BUILT dll that exports both arms with
            //    DIFFERENT results: whatever the oracle prints here IS the net48
            //    order, and the runtime must print the same number.
            string suffixAnsi = CatchValue(delegate { return SuffixProbeAnsi(); });
            Console.WriteLine("[suffix.ansi]=" + suffixAnsi);
            string suffixUnicode = CatchValue(delegate { return SuffixProbeUnicode(); });
            Console.WriteLine("[suffix.unicode]=" + suffixUnicode);
            string suffixExact = CatchValue(delegate { return SuffixProbeExact(); });
            Console.WriteLine("[suffix.exact]=" + suffixExact);

            // 7. The thrown messages never name a probed symbol.
            string probeMsg = CatchMessage(delegate { FindWindowExactSpelling("NoSuchClassXyz", null); });
            Console.WriteLine("[msg.has_original]=" + probeMsg.Contains("FindWindow"));
            Console.WriteLine("[msg.has_suffixed]=" + probeMsg.Contains("FindWindowW"));
        }

        private static string CatchName(Action body)
        {
            try
            {
                body();
                return "NO_THROW";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }

        private static string CatchMessage(Action body)
        {
            try
            {
                body();
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message ?? "";
            }
        }

        private static string CatchValue(Func<int> body)
        {
            try
            {
                return body().ToString();
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }
    }
}
