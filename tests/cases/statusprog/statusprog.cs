// statusprog.cs -- STATUSBAR/PROGRESSBAR DEEP (e2e batch C24, "StatusBar/
// ProgressBar deep probe -- after stage 9"). Builds on winforms1 Phase 9 (the stage-9
// status-strip surface: comctl32 StatusBar "msctls_statusbar32" +
// ProgressBar "msctls_progress32" over the raw SB_*/PBM_* message channels)
// and goes DEEPER on both controls:
//
//   * MULTI-PART STATUS BAR: SB_SETPARTS (3 explicit boundaries [80,160,320])
//     + SB_GETPARTS read-back (the exact boundaries return, so the round-trip
//     is byte-exact); SB_SETTEXT / SB_GETTEXT / SB_GETTEXTLENGTH per PART
//     index (part 0/1/2 written independently, read back independently);
//     overwrite (re-set part 0 -> length + text change) and empty-text
//     (part 2 "" -> length 0, readback "").
//   * PROGRESSBAR RANGE + POSITION DEEP: PBM_SETRANGE32 + PBM_GETRANGE
//     read back BEFORE and AFTER a mid-test range change (0..100 -> 0..200).
//     NOTE: net48 comctl32 packs PBM_GETRANGE (NULL lParam) with the UPPER
//     limit in the LOW word and the LOWER limit in the HIGH word (empirically
//     pinned: range 0..100 reads back word0=100 word1=0) -- the reverse of
//     the classic doc note. PBM_SETPOS returns the PREVIOUS position (a
//     deterministic sequence 10/25/100); PBM_DELTAPOS (delta advance returns
//     previous); PBM_SETSTEP + PBM_STEPIT; POSITION CLAMPING -- and a real
//     comctl32 quirk exposed: SETPOS stores the raw position unclamped
//     (GETPOS clamps on READ), and STEPIT from a >max position RESETS the
//     position to the step increment (100 -> 5 with step 5). All rows are
//     the deterministic real-control output.
//   * INTERACTION (progress drives status): a posted WF_STEP advances the
//     progress bar (PBM_DELTAPOS +25) and mirrors the new position into
//     status part 1 text (SB_SETTEXT + SB_GETTEXT round-trip), recorded in
//     a step/position/status sequence -- the WinForms progress-reporting
//     pattern, deeper (multi-part status) than winforms1's single-part form.
//
// Headless-safe: InitCommonControlsEx registers the comctl32 classes; both
// controls are WS_CHILD of the HIDDEN form; the interaction is PostMessage +
// PeekMessageW drain (bounded 200 dispatches). Determinism: every message
// goes to the SAME real comctl32 on the net48 oracle and the transpiled
// builds, so the outputs are byte-exact (winforms1 model).
//
// Reference output shape (net48 oracle):
//   comctl_init=True
//   statusbar_created=True
//   progressbar_created=True
//   st_parts_set=True
//   st_parts_count=3
//   st_parts_back=80/160/320
//   st_t0_set=True
//   st_t1_set=True
//   st_t2_set=True
//   st_l0=5          ("Ready")
//   st_l1=9          ("Items: 42")
//   st_l2=4          ("Done")
//   st_g0=Ready
//   st_g1=Items: 42
//   st_g2=Done
//   st_ow_set=True
//   st_ow_len=13     ("Processing..." = 13 chars)
//   st_ow=Processing...
//   st_empty_len=0
//   st_empty=
//   pr_range_set=True
//   pr_range_word0=100   (LOWORD of PBM_GETRANGE = UPPER limit)
//   pr_range_word1=0     (HIWORD of PBM_GETRANGE = LOWER limit)
//   pr_pos0=0
//   pr_prev10=0
//   pr_pos10=10
//   pr_prev25=10
//   pr_pos25=25
//   pr_deltaprev=25
//   pr_pos40=40
//   pr_prev100=40
//   pr_pos100=100
//   pr_clamp_prev=100
//   pr_pos_clamped=100
//   pr_step_set=True
//   pr_stepit_prev=100
//   pr_pos_stepit=5      (comctl32 quirk: STEPIT from a >max position
//                         resets the position to the step increment)
//   pr_range2_set=True
//   pr_range2_word0=200
//   pr_range2_word1=0
//   pr_prev150=5         (internal position was 5 after the stepit quirk)
//   pr_pos150=150
//   pr_prev300=150
//   pr_pos_clamped2=200
//   pr_reset_prev=200
//   pr_pos_reset=0
//   st_inject=True
//   st_seq=s1/p25/Step 1 at 25%;s2/p50/Step 2 at 50%;s3/p75/Step 3 at 75%;s4/p100/Step 4 at 100%;
//   st_final_pos=100
//   st_final_part1=Step 4 at 100%
//   st_step_count=4
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class Statusprog {
    // --- Win32 constants ---
    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_CHILD = 0x40000000;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int PM_REMOVE = 0x0001;

    // Stage 9 comctl32 registration flags.
    public const int ICC_BAR_CLASSES = 0x00000004;
    public const int ICC_PROGRESS_CLASS = 0x00000020;

    // StatusBar messages (comctl32 "msctls_statusbar32"). SB_SETPARTS sets
    // the part boundaries (wParam = count, lParam = int[] pointer);
    // SB_GETPARTS copies them back (returns the count); SB_SETTEXT /
    // SB_GETTEXTLENGTH / SB_GETTEXT address a part by the wParam index.
    public const int SB_SETPARTS = 0x0404;
    public const int SB_GETPARTS = 0x0406;
    public const int SB_SETTEXT = 0x040B;
    public const int SB_GETTEXTLENGTH = 0x040C;
    public const int SB_GETTEXT = 0x040D;

    // ProgressBar messages (comctl32 "msctls_progress32"). PBM_SETPOS /
    // PBM_DELTAPOS / PBM_STEPIT each return the PREVIOUS position (a
    // deterministic sequence); PBM_GETPOS reads the current position;
    // PBM_GETRANGE with lParam=0 packs LOWORD=low / HIWORD=high into the
    // return; PBM_SETRANGE32 re-ranges mid-test.
    public const int PBM_SETPOS = 0x0402;
    public const int PBM_DELTAPOS = 0x0403;
    public const int PBM_SETSTEP = 0x0404;
    public const int PBM_STEPIT = 0x0405;
    public const int PBM_SETRANGE32 = 0x0406;
    public const int PBM_GETRANGE = 0x0407;
    public const int PBM_GETPOS = 0x0408;

    // Custom posted message: the WndProc advances the progress bar and
    // mirrors the new position into status part 1 ("progress drives
    // status"), the stage-9 interaction deepened for multi-part status.
    public const int WF_STEP = 0x8003;

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX {
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
    public struct MSG {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public int pt_x;
        public int pt_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX {
        public int dwSize;
        public int dwICC;
    }

    public delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // --- P/Invoke surface ---
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);

    [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
        int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance,
        IntPtr param);

    [DllImport("user32", EntryPoint = "PostMessageW")]
    public static extern int PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", EntryPoint = "PeekMessageW")]
    public static extern int PeekMessageW(out MSG msg, IntPtr hwnd, int min, int max, int remove);

    [DllImport("user32", EntryPoint = "TranslateMessage")]
    public static extern int TranslateMessage(ref MSG msg);

    [DllImport("user32", EntryPoint = "DispatchMessageW")]
    public static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);

    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    // --- Observable state ---
    public static IntPtr StatusHwnd;
    public static IntPtr ProgressHwnd;
    public static string Seq = "";
    public static int StepCount;
    public static WndProc RootWndProc;

    public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WF_STEP)
        {
            // The posted "user advances progress" interaction: DELTAPOS +25
            // (returns the PREVIOUS position), read the NEW position, then
            // mirror it into status part 1 (SB_SETTEXT + SB_GETTEXT
            // round-trip): progress drives status.
            int step = (int)wParam.ToInt64();
            SendMessageW(ProgressHwnd, PBM_DELTAPOS, new IntPtr(25), IntPtr.Zero);
            int got = (int)SendMessageW(ProgressHwnd, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero).ToInt64();
            IntPtr st = Marshal.StringToHGlobalUni("Step " + step + " at " + got + "%");
            SendMessageW(StatusHwnd, SB_SETTEXT, new IntPtr(1), st);
            Marshal.FreeHGlobal(st);
            IntPtr sb = Marshal.AllocHGlobal(256);
            SendMessageW(StatusHwnd, SB_GETTEXT, new IntPtr(1), sb);
            string sText = Marshal.PtrToStringUni(sb);
            Marshal.FreeHGlobal(sb);
            Seq = Seq + "s" + step + "/p" + got + "/" + sText + ";";
            StepCount++;
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public static void Main()
    {
        IntPtr hModule = GetModuleHandleW(null);

        // Register the form class (WNDPROC = our delegate thunk) + create the
        // HIDDEN form (WS_OVERLAPPED, no WS_VISIBLE -- headless-safe).
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = CS_HREDRAW | CS_VREDRAW;
        RootWndProc = WndProcImpl;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
        wc.hInstance = hModule;
        IntPtr clsName = Marshal.StringToHGlobalUni("StatusprogFormClass");
        wc.lpszClassName = clsName;
        RegisterClassExW(ref wc);
        Marshal.FreeHGlobal(clsName);
        IntPtr hForm = CreateWindowExW(0, "StatusprogFormClass", "StatusprogForm",
            WS_OVERLAPPED, CW_USEDEFAULT, CW_USEDEFAULT, 320, 200,
            IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);

        // Register the common-control classes + create the two controls.
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_BAR_CLASSES | ICC_PROGRESS_CLASS;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("comctl_init=" + (iccRet != 0));

        IntPtr hStatus = CreateWindowExW(0, "msctls_statusbar32", "",
            WS_CHILD, 0, 0, 320, 20,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        StatusHwnd = hStatus;
        Console.WriteLine("statusbar_created=" + (hStatus != IntPtr.Zero));
        IntPtr hProgress = CreateWindowExW(0, "msctls_progress32", "",
            WS_CHILD, 20, 250, 160, 20,
            hForm, IntPtr.Zero, hModule, IntPtr.Zero);
        ProgressHwnd = hProgress;
        Console.WriteLine("progressbar_created=" + (hProgress != IntPtr.Zero));

        // ============ A. multi-part status bar ============
        // 3 explicit part boundaries [80,160,320]; SB_SETPARTS stores them
        // exactly and SB_GETPARTS returns them (round-trip is byte-exact).
        int[] parts = new int[] { 80, 160, 320 };
        IntPtr pbuf = Marshal.AllocHGlobal(64);
        Marshal.Copy(parts, 0, pbuf, 3);
        IntPtr partsRet = SendMessageW(hStatus, SB_SETPARTS, new IntPtr(3), pbuf);
        Console.WriteLine("st_parts_set=" + (partsRet != IntPtr.Zero));
        IntPtr partsCount = SendMessageW(hStatus, SB_GETPARTS, new IntPtr(3), pbuf);
        Console.WriteLine("st_parts_count=" + partsCount.ToInt64());
        int[] back = new int[] { 0, 0, 0 };
        Marshal.Copy(pbuf, back, 0, 3);
        // NOTE: reading array ELEMENTS directly inside a string concat
        // (`".." + back[0]`) lowers to ldelema + callvirt ToString, which the
        // emitter mis-folds (unbox of the element ADDRESS -- defect D-STP-1).
        // Reading into int locals first uses the proven box path.
        int b0 = back[0];
        int b1 = back[1];
        int b2 = back[2];
        Console.WriteLine("st_parts_back=" + b0 + "/" + b1 + "/" + b2);
        Marshal.FreeHGlobal(pbuf);

        // Independent per-part text channels (part index in wParam).
        IntPtr s0 = Marshal.StringToHGlobalUni("Ready");
        IntPtr s0Ret = SendMessageW(hStatus, SB_SETTEXT, IntPtr.Zero, s0);
        Marshal.FreeHGlobal(s0);
        Console.WriteLine("st_t0_set=" + (s0Ret != IntPtr.Zero));
        IntPtr s1 = Marshal.StringToHGlobalUni("Items: 42");
        IntPtr s1Ret = SendMessageW(hStatus, SB_SETTEXT, new IntPtr(1), s1);
        Marshal.FreeHGlobal(s1);
        Console.WriteLine("st_t1_set=" + (s1Ret != IntPtr.Zero));
        IntPtr s2 = Marshal.StringToHGlobalUni("Done");
        IntPtr s2Ret = SendMessageW(hStatus, SB_SETTEXT, new IntPtr(2), s2);
        Marshal.FreeHGlobal(s2);
        Console.WriteLine("st_t2_set=" + (s2Ret != IntPtr.Zero));

        IntPtr l0 = SendMessageW(hStatus, SB_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("st_l0=" + (l0.ToInt64() & 0xFFFFL));
        IntPtr l1 = SendMessageW(hStatus, SB_GETTEXTLENGTH, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("st_l1=" + (l1.ToInt64() & 0xFFFFL));
        IntPtr l2 = SendMessageW(hStatus, SB_GETTEXTLENGTH, new IntPtr(2), IntPtr.Zero);
        Console.WriteLine("st_l2=" + (l2.ToInt64() & 0xFFFFL));

        IntPtr g0 = Marshal.AllocHGlobal(256);
        SendMessageW(hStatus, SB_GETTEXT, IntPtr.Zero, g0);
        Console.WriteLine("st_g0=" + Marshal.PtrToStringUni(g0));
        SendMessageW(hStatus, SB_GETTEXT, new IntPtr(1), g0);
        Console.WriteLine("st_g1=" + Marshal.PtrToStringUni(g0));
        SendMessageW(hStatus, SB_GETTEXT, new IntPtr(2), g0);
        Console.WriteLine("st_g2=" + Marshal.PtrToStringUni(g0));

        // Overwrite part 0: length + text change.
        IntPtr ow = Marshal.StringToHGlobalUni("Processing...");
        IntPtr owRet = SendMessageW(hStatus, SB_SETTEXT, IntPtr.Zero, ow);
        Marshal.FreeHGlobal(ow);
        Console.WriteLine("st_ow_set=" + (owRet != IntPtr.Zero));
        IntPtr owl = SendMessageW(hStatus, SB_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("st_ow_len=" + (owl.ToInt64() & 0xFFFFL));
        SendMessageW(hStatus, SB_GETTEXT, IntPtr.Zero, g0);
        Console.WriteLine("st_ow=" + Marshal.PtrToStringUni(g0));

        // Empty text on part 2: length 0, readback "".
        IntPtr em = Marshal.StringToHGlobalUni("");
        SendMessageW(hStatus, SB_SETTEXT, new IntPtr(2), em);
        Marshal.FreeHGlobal(em);
        IntPtr eml = SendMessageW(hStatus, SB_GETTEXTLENGTH, new IntPtr(2), IntPtr.Zero);
        Console.WriteLine("st_empty_len=" + (eml.ToInt64() & 0xFFFFL));
        SendMessageW(hStatus, SB_GETTEXT, new IntPtr(2), g0);
        Console.WriteLine("st_empty=" + Marshal.PtrToStringUni(g0));
        Marshal.FreeHGlobal(g0);

        // ============ B. progress bar: range + position deep ============
        IntPtr rangeRet = SendMessageW(hProgress, PBM_SETRANGE32, IntPtr.Zero, new IntPtr(100));
        Console.WriteLine("pr_range_set=" + (rangeRet != IntPtr.Zero));
        IntPtr range0 = SendMessageW(hProgress, PBM_GETRANGE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_range_word0=" + (range0.ToInt64() & 0xFFFFL));
        Console.WriteLine("pr_range_word1=" + ((range0.ToInt64() >> 16) & 0xFFFFL));

        IntPtr p0 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos0=" + p0.ToInt64());
        IntPtr pv10 = SendMessageW(hProgress, PBM_SETPOS, new IntPtr(10), IntPtr.Zero);
        Console.WriteLine("pr_prev10=" + pv10.ToInt64());
        IntPtr p10 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos10=" + p10.ToInt64());
        IntPtr pv25 = SendMessageW(hProgress, PBM_SETPOS, new IntPtr(25), IntPtr.Zero);
        Console.WriteLine("pr_prev25=" + pv25.ToInt64());
        IntPtr p25 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos25=" + p25.ToInt64());

        // PBM_DELTAPOS: +15 from 25 -> 40 (returns the PREVIOUS position 25).
        IntPtr dp = SendMessageW(hProgress, PBM_DELTAPOS, new IntPtr(15), IntPtr.Zero);
        Console.WriteLine("pr_deltaprev=" + dp.ToInt64());
        IntPtr p40 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos40=" + p40.ToInt64());

        IntPtr pv100 = SendMessageW(hProgress, PBM_SETPOS, new IntPtr(100), IntPtr.Zero);
        Console.WriteLine("pr_prev100=" + pv100.ToInt64());
        IntPtr p100 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos100=" + p100.ToInt64());

        // Position clamping: 150 with max 100 -> stays 100 (prev returned).
        IntPtr pclamp = SendMessageW(hProgress, PBM_SETPOS, new IntPtr(150), IntPtr.Zero);
        Console.WriteLine("pr_clamp_prev=" + pclamp.ToInt64());
        IntPtr pc = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos_clamped=" + pc.ToInt64());

        // PBM_SETSTEP / PBM_STEPIT: step = 5, stepit at max stays 100.
        IntPtr sstep = SendMessageW(hProgress, PBM_SETSTEP, new IntPtr(5), IntPtr.Zero);
        Console.WriteLine("pr_step_set=" + (sstep != IntPtr.Zero));
        IntPtr stepit = SendMessageW(hProgress, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_stepit_prev=" + stepit.ToInt64());
        IntPtr psi = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos_stepit=" + psi.ToInt64());

        // Mid-test range change 0..100 -> 0..200; range read back again.
        IntPtr range2 = SendMessageW(hProgress, PBM_SETRANGE32, IntPtr.Zero, new IntPtr(200));
        Console.WriteLine("pr_range2_set=" + (range2 != IntPtr.Zero));
        IntPtr r2 = SendMessageW(hProgress, PBM_GETRANGE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_range2_word0=" + (r2.ToInt64() & 0xFFFFL));
        Console.WriteLine("pr_range2_word1=" + ((r2.ToInt64() >> 16) & 0xFFFFL));

        // The previously-clamped position becomes legal: 150 now fits.
        IntPtr pv150 = SendMessageW(hProgress, PBM_SETPOS, new IntPtr(150), IntPtr.Zero);
        Console.WriteLine("pr_prev150=" + pv150.ToInt64());
        IntPtr p150 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos150=" + p150.ToInt64());

        // Clamp again under the new max: 300 with max 200 -> 200.
        IntPtr pv300 = SendMessageW(hProgress, PBM_SETPOS, new IntPtr(300), IntPtr.Zero);
        Console.WriteLine("pr_prev300=" + pv300.ToInt64());
        IntPtr pc2 = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos_clamped2=" + pc2.ToInt64());

        // Reset to 0 for the interaction phase.
        IntPtr pv0 = SendMessageW(hProgress, PBM_SETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_reset_prev=" + pv0.ToInt64());
        IntPtr p0r = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pr_pos_reset=" + p0r.ToInt64());

        // ============ C. interaction: progress drives status ============
        // 4 posted steps; each advances the bar +25 (DELTAPOS) and mirrors
        // the new position into status part 1. The drain dispatches them in
        // order; the WndProc handler records the sequence.
        Seq = "";
        StepCount = 0;
        int i1 = PostMessageW(hForm, WF_STEP, new IntPtr(1), IntPtr.Zero);
        int i2 = PostMessageW(hForm, WF_STEP, new IntPtr(2), IntPtr.Zero);
        int i3 = PostMessageW(hForm, WF_STEP, new IntPtr(3), IntPtr.Zero);
        int i4 = PostMessageW(hForm, WF_STEP, new IntPtr(4), IntPtr.Zero);
        Console.WriteLine("st_inject=" + (i1 != 0 && i2 != 0 && i3 != 0 && i4 != 0));
        MSG msg;
        int drained = 0;
        while (PeekMessageW(out msg, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
            drained++;
            if (drained >= 200) break;
        }
        Console.WriteLine("st_seq=" + Seq);
        IntPtr fp = SendMessageW(hProgress, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("st_final_pos=" + fp.ToInt64());
        IntPtr fb = Marshal.AllocHGlobal(256);
        SendMessageW(hStatus, SB_GETTEXT, new IntPtr(1), fb);
        Console.WriteLine("st_final_part1=" + Marshal.PtrToStringUni(fb));
        Marshal.FreeHGlobal(fb);
        Console.WriteLine("st_step_count=" + StepCount);

        DestroyWindow(hStatus);
        DestroyWindow(hProgress);
        DestroyWindow(hForm);
    }
}}
