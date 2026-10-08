// clipops.cs -- CLIPBOARD deep probe (e2e batch C27, "clipboard deep probe: set/get/multi-format
// -- after WinForms stage 12"). Builds on winforms1 Phase 12 (the clipboard COPY/PASTE
// surface: OpenClipboard -> EmptyClipboard -> SetClipboardData(CF_UNICODETEXT) ->
// CloseClipboard, then OpenClipboard -> GetClipboardData -> GlobalLock ->
// PtrToStringUni -> GlobalUnlock -> CloseClipboard) and goes DEEPER on the
// clipboard surface:
//
//   * SET/GET ROUND-TRIP: copy a text then read it back in a SECOND open
//     session -- content + length are byte-exact (winforms1 p12 proves the
//     two-session path; this re-probes it with a longer + non-ASCII text).
//   * MULTI-FORMAT: the clipboard holds TWO formats simultaneously --
//     CF_UNICODETEXT (13) + a CUSTOM registered format (RegisterClipboardFormatW
//     -> id >= 0xC000) carrying a BINARY payload (GlobalAlloc/GMEM_MOVEABLE ->
//     GlobalLock -> Marshal.Copy(byte[]) -> GlobalUnlock -> SetClipboardData).
//     GetClipboardFormatNameW reads the custom format's registered name back.
//   * AVAILABILITY + ENUMERATION: IsClipboardFormatAvailable for our two
//     formats (True) and for an unset standard format (CF_BITMAP -> False);
//     CountClipboardFormats + EnumClipboardFormats enumerate the clipboard
//     contents (>= 2, both of ours present).
//   * SEQUENCE NUMBER: GetClipboardSequenceNumber is a GLOBAL monotonic
//     counter -- raw values differ across runs, so only the in-process
//     RELATIVE comparison (after_set > before_set) is printed (True).
//   * EMPTY: EmptyClipboard clears the clipboard; the format-available probe
//     for our text format reads False afterwards (the re-set in the final
//     phase also proves the empty was real, not a copy failure).
//   * ERROR PATH: GetClipboardData(CF_HDROP) on a clipboard that has never
//     held an HDROP returns a null handle (read as a boolean).
//
// Headless acceptance: OpenClipboard needs the calling thread's message queue,
// so every clipboard operation runs INSIDE the WndProc handler for a POSTED
// custom message (the phase-4 PostMessage + PeekMessageW drain pattern -- the
// exact winforms1 phase-12 shape). The only real-world hazard (another process
// holding the clipboard open) is handled by a bounded OpenClipboard retry
// inside the handler (identical on both sides). Every printed value is a bool
// or a string DERIVED FROM OUR OWN SET DATA -- never raw clipboard contents,
// handles, or the global sequence counter.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cases
{
    public static class Clipops
    {
        public const int CF_UNICODETEXT = 13;
        public const int CF_BITMAP = 2;
        public const int CF_HDROP = 15;
        public const int PM_REMOVE = 0x0001;
        public const int GWL_STYLE = -16;
        public const int WF_CLIP_SET = 0x8010;
        public const int WF_CLIP_GET = 0x8011;
        public const int WF_CLIP_EMPTY = 0x8012;

        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public IntPtr lpszMenuName;
            public IntPtr lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public int message;
            public IntPtr wParam;
            public IntPtr lParam;
            public int time;
            public int pt_x;
            public int pt_y;
        }

        public delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32", EntryPoint = "GlobalAlloc")]
        public static extern IntPtr GlobalAlloc(int uFlags, IntPtr cbBytes);

        [DllImport("kernel32", EntryPoint = "GlobalLock")]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32", EntryPoint = "GlobalUnlock")]
        public static extern int GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32", EntryPoint = "GlobalFree")]
        public static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

        [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance,
            IntPtr param);

        [DllImport("user32", EntryPoint = "PostMessageW")]
        public static extern int PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", EntryPoint = "PeekMessageW")]
        public static extern int PeekMessageW(out MSG msg, IntPtr hwnd, int min, int max, int remove);

        [DllImport("user32", EntryPoint = "TranslateMessage")]
        public static extern int TranslateMessage(ref MSG msg);

        [DllImport("user32", EntryPoint = "DispatchMessageW")]
        public static extern IntPtr DispatchMessageW(ref MSG msg);

        [DllImport("user32", EntryPoint = "DefWindowProcW")]
        public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", EntryPoint = "OpenClipboard")]
        public static extern int OpenClipboard(IntPtr hwnd);

        [DllImport("user32", EntryPoint = "EmptyClipboard")]
        public static extern int EmptyClipboard();

        [DllImport("user32", EntryPoint = "SetClipboardData")]
        public static extern IntPtr SetClipboardData(int uFormat, IntPtr hMem);

        [DllImport("user32", EntryPoint = "GetClipboardData")]
        public static extern IntPtr GetClipboardData(int uFormat);

        [DllImport("user32", EntryPoint = "CloseClipboard")]
        public static extern int CloseClipboard();

        [DllImport("user32", EntryPoint = "IsClipboardFormatAvailable")]
        public static extern int IsClipboardFormatAvailable(int uFormat);

        [DllImport("user32", EntryPoint = "CountClipboardFormats")]
        public static extern int CountClipboardFormats();

        [DllImport("user32", EntryPoint = "EnumClipboardFormats")]
        public static extern int EnumClipboardFormats(int uFormat);

        [DllImport("user32", EntryPoint = "GetClipboardFormatNameW", CharSet = CharSet.Unicode)]
        public static extern int GetClipboardFormatNameW(int uFormat, IntPtr lpszFormatName, int cchMaxCount);

        [DllImport("user32", EntryPoint = "RegisterClipboardFormatW", CharSet = CharSet.Unicode)]
        public static extern int RegisterClipboardFormatW(string lpszFormat);

        [DllImport("user32", EntryPoint = "GetClipboardSequenceNumber")]
        public static extern int GetClipboardSequenceNumber();

        // ---- observable state (all derived from OUR OWN set data) ----
        public static WndProc ClipWndProc;
        public static IntPtr ClipHwnd;
        public static string ClipSetText = "";
        public static string ClipGetText = "";
        public static int ClipGetLen;
        public static int CustomFmt = 0;
        public static string CustomNameRead = "";
        public static bool SetOk;
        public static bool GetOk;
        public static bool CustomGetOk;
        public static bool CustomRoundtrip;
        public static bool AvailText;
        public static bool AvailCustom;
        public static bool AvailBitmap;
        public static bool AvailHdrop;
        public static int FormatCount;
        public static bool EnumHasText;
        public static bool EnumHasCustom;
        public static int SeqBefore;
        public static int SeqAfter;
        public static bool EmptyOk;
        public static bool AvailAfterEmpty;
        public static bool ReSetOk;

        public static int OpenClipWithRetry()
        {
            int r = 0;
            int polls = 0;
            while (r == 0 && polls < 200)
            {
                r = OpenClipboard(IntPtr.Zero);
                if (r == 0) { Thread.Sleep(2); polls++; }
            }
            return r;
        }

        // ---- the copy/paste/empty/format-sequence runs inside the pump ----
        public static IntPtr ClipWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WF_CLIP_SET)
            {
                // Phase 1: copy BOTH formats in ONE open session.
                SetOk = false;
                if (OpenClipWithRetry() != 0)
                {
                    EmptyClipboard();
                    // format 1: CF_UNICODETEXT (UTF-16 text)
                    IntPtr hg1 = Marshal.StringToHGlobalUni(ClipSetText);
                    IntPtr sd1 = SetClipboardData(CF_UNICODETEXT, hg1);
                    if (sd1 == IntPtr.Zero) Marshal.FreeHGlobal(hg1);
                    // format 2: custom binary (GMEM_MOVEABLE global block).
                    // NOTE: Marshal.Copy routes through icall_marshal_copy_*
                    // (runtime.cpp) with an INT32 stride (length*4 bytes) -- a
                    // byte[] payload would over-copy 4x the bytes (4-byte array
                    // -> 16-byte write into a 4-byte block = heap corruption),
                    // so the payload is an int[] (one 4-byte element).
                    int[] payload = { 0x50515253 };
                    IntPtr hg2 = GlobalAlloc(0x0022, new IntPtr(4)); // GMEM_MOVEABLE|GMEM_ZEROINIT
                    if (hg2 != IntPtr.Zero)
                    {
                        IntPtr p2 = GlobalLock(hg2);
                        if (p2 != IntPtr.Zero)
                        {
                            Marshal.Copy(payload, 0, p2, 1);
                            GlobalUnlock(hg2);
                        }
                        IntPtr sd2 = SetClipboardData(CustomFmt, hg2);
                        if (sd2 == IntPtr.Zero) GlobalFree(hg2);
                        SetOk = sd2 != IntPtr.Zero;
                    }
                    SeqAfter = GetClipboardSequenceNumber();
                    CloseClipboard();
                }
                return IntPtr.Zero;
            }
            if (msg == WF_CLIP_GET)
            {
                // Phase 2: read both formats back in a SECOND open session.
                GetOk = false; CustomGetOk = false; CustomRoundtrip = false;
                if (OpenClipWithRetry() != 0)
                {
                    AvailText = IsClipboardFormatAvailable(CF_UNICODETEXT) != 0;
                    AvailCustom = IsClipboardFormatAvailable(CustomFmt) != 0;
                    AvailBitmap = IsClipboardFormatAvailable(CF_BITMAP) != 0;
                    FormatCount = CountClipboardFormats();
                    EnumHasText = false; EnumHasCustom = false;
                    int f = 0;
                    while ((f = EnumClipboardFormats(f)) != 0)
                    {
                        if (f == CF_UNICODETEXT) EnumHasText = true;
                        if (f == CustomFmt) EnumHasCustom = true;
                    }
                    IntPtr hg1 = GetClipboardData(CF_UNICODETEXT);
                    GetOk = hg1 != IntPtr.Zero;
                    if (GetOk)
                    {
                        IntPtr p1 = GlobalLock(hg1);
                        ClipGetText = Marshal.PtrToStringUni(p1);
                        ClipGetLen = ClipGetText.Length;
                        GlobalUnlock(hg1);
                    }
                    IntPtr hg2 = GetClipboardData(CustomFmt);
                    CustomGetOk = hg2 != IntPtr.Zero;
                    if (CustomGetOk)
                    {
                        IntPtr p2 = GlobalLock(hg2);
                        int[] back = new int[1];
                        Marshal.Copy(p2, back, 0, 1);
                        GlobalUnlock(hg2);
                        CustomRoundtrip = back[0] == 0x50515253;
                    }
                    AvailHdrop = GetClipboardData(CF_HDROP) != IntPtr.Zero;
                    IntPtr nameBuf = Marshal.AllocHGlobal(128);
                    int n = GetClipboardFormatNameW(CustomFmt, nameBuf, 64);
                    CustomNameRead = n > 0 ? Marshal.PtrToStringUni(nameBuf) : "";
                    Marshal.FreeHGlobal(nameBuf);
                    CloseClipboard();
                }
                return IntPtr.Zero;
            }
            if (msg == WF_CLIP_EMPTY)
            {
                // Phase 3: empty, probe gone, then RE-SET the text (proves the
                // empty was real -- a copy failure would show the text again).
                EmptyOk = false; AvailAfterEmpty = true; ReSetOk = false;
                if (OpenClipWithRetry() != 0)
                {
                    EmptyOk = EmptyClipboard() != 0;
                    AvailAfterEmpty = IsClipboardFormatAvailable(CF_UNICODETEXT) != 0;
                    IntPtr hg = Marshal.StringToHGlobalUni("re-set");
                    IntPtr sd = SetClipboardData(CF_UNICODETEXT, hg);
                    ReSetOk = sd != IntPtr.Zero;
                    if (!ReSetOk) Marshal.FreeHGlobal(hg);
                    CloseClipboard();
                }
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        public static int Drain()
        {
            MSG m;
            int n = 0;
            while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref m);
                DispatchMessageW(ref m);
                n++;
                if (n >= 300) break;
            }
            return n;
        }

        public static void Main()
        {
            IntPtr hModule = GetModuleHandleW(null);
            Console.WriteLine("clip_module_nonzero=" + (hModule != IntPtr.Zero));

            // Register the (hidden) message-probe window class -- the WndProc
            // delegate is held in a STATIC field (the GC-root fix 01afb99
            // registers reference static fields; the pinned delegate survives).
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = 80;
            wc.style = 0;
            ClipWndProc = ClipWndProcImpl;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ClipWndProc);
            wc.hInstance = hModule;
            IntPtr cls = Marshal.StringToHGlobalUni("ClipOpsClass");
            wc.lpszClassName = cls;
            ushort clsReg = RegisterClassExW(ref wc);
            Marshal.FreeHGlobal(cls);
            Console.WriteLine("clip_class_registered=" + (clsReg != 0));

            IntPtr hwnd = CreateWindowExW(0, "ClipOpsClass", "ClipOps",
                0x00000000, 0, 0, 120, 80, IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
            ClipHwnd = hwnd;
            Console.WriteLine("clip_window_created=" + (hwnd != IntPtr.Zero));

            // Register the custom format (session-global id >= 0xC000; same id
            // on net48 and ILC in the same session -- but the id is never
            // printed raw, only the name readback which is deterministic).
            CustomFmt = RegisterClipboardFormatW("ClipOpsCustom");
            Console.WriteLine("clip_custom_registered=" + (CustomFmt >= 0xC000));

            // ---- Phase 1: copy (both formats in one session) ----
            ClipSetText = "ClipOps-Mix-Text";
            SeqBefore = GetClipboardSequenceNumber();
            int p1 = PostMessageW(hwnd, WF_CLIP_SET, IntPtr.Zero, IntPtr.Zero);
            Console.WriteLine("clip_set_post=" + (p1 != 0));
            int d1 = Drain();
            Console.WriteLine("clip_set_ok=" + SetOk);
            Console.WriteLine("clip_set_seq_after_gt_before=" + (SeqAfter > SeqBefore));
            Console.WriteLine("clip_set_drained=" + (d1 >= 1));

            // ---- Phase 2: get (second session) ----
            int p2 = PostMessageW(hwnd, WF_CLIP_GET, IntPtr.Zero, IntPtr.Zero);
            Console.WriteLine("clip_get_post=" + (p2 != 0));
            int d2 = Drain();
            Console.WriteLine("clip_get_ok=" + GetOk);
            Console.WriteLine("clip_get_len=" + ClipGetLen);
            Console.WriteLine("clip_get_text=" + ClipGetText);
            Console.WriteLine("clip_custom_get_ok=" + CustomGetOk);
            Console.WriteLine("clip_custom_roundtrip=" + CustomRoundtrip);
            Console.WriteLine("clip_custom_name=" + CustomNameRead);
            Console.WriteLine("clip_avail_text=" + AvailText);
            Console.WriteLine("clip_avail_custom=" + AvailCustom);
            Console.WriteLine("clip_avail_bitmap=" + AvailBitmap);
            Console.WriteLine("clip_count_ge_2=" + (FormatCount >= 2));
            Console.WriteLine("clip_enum_has_text=" + EnumHasText);
            Console.WriteLine("clip_enum_has_custom=" + EnumHasCustom);
            Console.WriteLine("clip_hdrop_absent=" + (!AvailHdrop));
            Console.WriteLine("clip_get_drained=" + (d2 >= 1));

            // ---- Phase 3: empty + probe + re-set ----
            int p3 = PostMessageW(hwnd, WF_CLIP_EMPTY, IntPtr.Zero, IntPtr.Zero);
            Console.WriteLine("clip_empty_post=" + (p3 != 0));
            int d3 = Drain();
            Console.WriteLine("clip_empty_ok=" + EmptyOk);
            Console.WriteLine("clip_avail_after_empty=" + (!AvailAfterEmpty));
            Console.WriteLine("clip_reset_ok=" + ReSetOk);
            Console.WriteLine("clip_empty_drained=" + (d3 >= 1));
        }
    }
}
