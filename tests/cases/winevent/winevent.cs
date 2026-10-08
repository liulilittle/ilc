// winevent.cs -- WINDOW EVENT deep probe (e2e batch C22, "window event deep probe: event
// sequence / unsubscribe / multiple handlers -- after the WinForms event system"). Builds on winformsext (C20,
// the 8-section sequence/unsubscribe matrix) and winforms1 Phase 4/7. The
// net48 oracle runs the SAME source; the ILC side must produce byte-identical
// sequence strings and subscription counts.
//
// Coverage (deeper than winformsext -- sequence/unsubscribe/multi-handler):
//   * W1 ORDER + MIDDLE REMOVAL: 6-handler multicast fires in subscription
//     order; removing two middle handlers keeps the survivor order.
//   * W2 REMOVE-DURING-RAISE (snapshot semantics): a handler removes ANOTHER
//     handler while the event is firing. .NET snapshots the invocation list,
//     so the CURRENT raise still fires the removed handler and the NEXT raise
//     does not (the field-read-then-Invoke pattern captures the old delegate).
//   * W3 MULTI-HANDLER scale: an 8-handler list, then remove every other one.
//   * W4 MULTI-EVENT independence: FOUR events on one control (Click/Key/
//     DoubleClick/TextChanged), each with its own handler list -- raised in
//     an interleaved order, per-event sequences never cross-contaminate.
//   * W5 NESTED RAISE (re-entrancy): a Click handler raises the SAME control's
//     Key event from inside the Click invocation -- the outer sequence
//     resumes after the nested raise completes.
//   * W6 SAME HANDLER ON FOUR EVENTS: one handler subscribed to all four
//     events; unsubscribing from ONE event leaves the other three live.
//   * W7 REMOVE-ALL then RE-ADD different handlers.
//   * W8 REAL WINDOW dispatch: WM_COMMAND (by control id) + WM_KEYDOWN
//     (VK_F2 sentinel) through a real hidden window, PeekMessageW-drained in
//     FIFO order; unsubscribe a control mid-stream between drain batches.
//   * W9 MANY CONTROLS: four controls x two handlers each; raise in id
//     order; unsubscribe one handler on one control; re-raise.
//
// Determinism: every raise happens on the MAIN thread (the WinForms single-
// UI-thread model); message dispatch is FIFO-drained. All sequence strings
// and counts are exact.
//
// Every expected value is spelled in a trailing comment.
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Winevent
    {
        public const int WM_COMMAND = 0x0111;
        public const int WM_KEYDOWN = 0x0100;
        public const int PM_REMOVE = 0x0001;
        public const int VK_F2 = 0x0071;
        public const int WS_OVERLAPPED = 0x00000000;
        public const int CW_USEDEFAULT = unchecked((int)0x80000000);

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
        public delegate void H();

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

        [DllImport("user32", EntryPoint = "PostQuitMessage")]
        public static extern void PostQuitMessage(int exitCode);

        // ---- observable sequence + handlers (each appends its tag) ----
        public static string Seq = "";
        public static void ResetSeq() { Seq = ""; }
        public static void OnA() { Seq = Seq + "A;"; }
        public static void OnB() { Seq = Seq + "B;"; }
        public static void OnC() { Seq = Seq + "C;"; }
        public static void OnD() { Seq = Seq + "D;"; }
        public static void OnE() { Seq = Seq + "E;"; }
        public static void OnF() { Seq = Seq + "F;"; }
        public static void OnG() { Seq = Seq + "G;"; }
        public static void OnH() { Seq = Seq + "H;"; }
        public static void OnK1() { Seq = Seq + "K1;"; }
        public static void OnK2() { Seq = Seq + "K2;"; }
        public static void OnDb() { Seq = Seq + "Db;"; }
        public static void OnT1() { Seq = Seq + "T1;"; }
        public static void OnT2() { Seq = Seq + "T2;"; }
        public static void OnX() { Seq = Seq + "X;"; }
        public static void OnK5() { Seq = Seq + "K5;"; }
        public static void OnA5() { Seq = Seq + "A5;"; if (W5 != null) W5.RaiseKey(); }
        public static void OnB5() { Seq = Seq + "B5;"; }

        // W2: the control whose handler is being removed mid-raise.
        public static Ctrl Current;
        // OnB removes OnC from the CURRENT control during a raise.
        public static void OnBRemoveC() { Seq = Seq + "B;"; if (Current != null) Current.Click -= OnC; }

        // W5: the control whose Key event a Click handler raises re-entrantly.
        public static Ctrl W5;

        // ---- dispatch targets for the WinForms section ----
        public static Ctrl P1;
        public static Ctrl P2;
        public static Ctrl P3;
        public static WndProc RootWndProc;

        public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_COMMAND)
            {
                int cid = (int)(wParam.ToInt64() & 0xFFFFL);
                if (P1 != null && cid == P1.Id) P1.RaiseClick();
                else if (P2 != null && cid == P2.Id) P2.RaiseClick();
                return IntPtr.Zero;
            }
            if (msg == WM_KEYDOWN)
            {
                if (wParam.ToInt64() == VK_F2 && P3 != null) P3.RaiseKey();
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        public static void Main()
        {
            // ============ W1: 6-handler order + middle removal ============
            Ctrl c1 = new Ctrl(); c1.Id = 1; c1.Name = "c1";
            c1.Click += OnA; c1.Click += OnB; c1.Click += OnC;
            c1.Click += OnD; c1.Click += OnE; c1.Click += OnF;
            Console.WriteLine("w1_subs=" + c1.ClickCount);      // 6
            ResetSeq(); c1.RaiseClick();
            Console.WriteLine("w1_seq=" + Seq);                 // A;B;C;D;E;F;
            c1.Click -= OnB; c1.Click -= OnE;                   // remove two middles
            Console.WriteLine("w1_subs_after=" + c1.ClickCount);// 4
            ResetSeq(); c1.RaiseClick();
            Console.WriteLine("w1_seq_after=" + Seq);           // A;C;D;F;

            // ============ W2: remove-during-raise (snapshot semantics) ============
            Ctrl c2 = new Ctrl(); c2.Id = 2; c2.Name = "c2";
            c2.Click += OnA; c2.Click += OnBRemoveC; c2.Click += OnC;
            Current = c2;
            ResetSeq(); c2.RaiseClick();                        // OnBRemoveC removes OnC mid-raise
            Console.WriteLine("w2_seq1=" + Seq);                // A;B;C;  (snapshot still fires C)
            Console.WriteLine("w2_subs_after=" + c2.ClickCount);// 2
            ResetSeq(); c2.RaiseClick();
            Console.WriteLine("w2_seq2=" + Seq);                 // A;B;  (C removed)
            Current = null;

            // ============ W3: 8-handler list, remove every other ============
            Ctrl c3 = new Ctrl(); c3.Id = 3; c3.Name = "c3";
            c3.Click += OnA; c3.Click += OnB; c3.Click += OnC; c3.Click += OnD;
            c3.Click += OnE; c3.Click += OnF; c3.Click += OnG; c3.Click += OnH;
            Console.WriteLine("w3_subs=" + c3.ClickCount);      // 8
            ResetSeq(); c3.RaiseClick();
            Console.WriteLine("w3_seq=" + Seq);                 // A;B;C;D;E;F;G;H;
            c3.Click -= OnB; c3.Click -= OnD; c3.Click -= OnF; c3.Click -= OnH;
            Console.WriteLine("w3_subs_after=" + c3.ClickCount);// 4
            ResetSeq(); c3.RaiseClick();
            Console.WriteLine("w3_seq_after=" + Seq);           // A;C;E;G;

            // ============ W4: four events on one control, interleaved ============
            Ctrl c4 = new Ctrl(); c4.Id = 4; c4.Name = "c4";
            c4.Click += OnA; c4.Click += OnB;
            c4.Key += OnK1; c4.Key += OnK2;
            c4.Dbl += OnDb;
            c4.Text += OnT1; c4.Text += OnT2;
            Console.WriteLine("w4_subs=" + (c4.ClickCount == 2 && c4.KeyCount == 2
                && c4.DblCount == 1 && c4.TextCount == 2));     // True
            ResetSeq();
            c4.RaiseKey(); c4.RaiseClick(); c4.RaiseKey(); c4.RaiseDbl();
            c4.RaiseText(); c4.RaiseClick(); c4.RaiseText();
            Console.WriteLine("w4_seq=" + Seq);                 // K1;K2;A;B;K1;K2;Db;T1;T2;A;B;T1;T2;

            // ============ W5: nested raise (Click handler raises Key) ============
            Ctrl c5 = new Ctrl(); c5.Id = 5; c5.Name = "c5";
            c5.Click += OnA5; c5.Click += OnB5;
            c5.Key += OnK5;
            W5 = c5;
            ResetSeq(); c5.RaiseClick();
            Console.WriteLine("w5_seq=" + Seq);                 // A5;K5;B5;
            W5 = null;

            // ============ W6: same handler on four events, unsubscribe one ============
            Ctrl c6 = new Ctrl(); c6.Id = 6; c6.Name = "c6";
            c6.Click += OnX; c6.Key += OnX; c6.Dbl += OnX; c6.Text += OnX;
            c6.Click -= OnX;
            Console.WriteLine("w6_subs=" + (c6.ClickCount == 0 && c6.KeyCount == 1
                && c6.DblCount == 1 && c6.TextCount == 1));     // True
            ResetSeq();
            c6.RaiseClick(); c6.RaiseKey(); c6.RaiseDbl(); c6.RaiseText();
            Console.WriteLine("w6_seq=" + Seq);                 // X;X;X;  (click no-op)

            // ============ W7: remove-all then re-add different handlers ============
            Ctrl c7 = new Ctrl(); c7.Id = 7; c7.Name = "c7";
            c7.Click += OnA; c7.Click += OnB;
            c7.Click -= OnA; c7.Click -= OnB;
            Console.WriteLine("w7_subs0=" + c7.ClickCount);     // 0
            ResetSeq(); c7.RaiseClick();
            Console.WriteLine("w7_seq0=" + Seq);                // (empty)
            c7.Click += OnC; c7.Click += OnD;
            Console.WriteLine("w7_subs1=" + c7.ClickCount);     // 2
            ResetSeq(); c7.RaiseClick();
            Console.WriteLine("w7_seq1=" + Seq);                // C;D;

            // ============ W8: real window dispatch + mid-stream unsubscribe ============
            IntPtr hModule = GetModuleHandleW(null);
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = 80;
            wc.style = 0;
            RootWndProc = WndProcImpl;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
            wc.hInstance = hModule;
            IntPtr cls = Marshal.StringToHGlobalUni("WEvClass");
            wc.lpszClassName = cls;
            ushort atom = RegisterClassExW(ref wc);
            Console.WriteLine("w_class_registered=" + (atom != 0));
            Marshal.FreeHGlobal(cls);

            IntPtr hForm = CreateWindowExW(0, "WEvClass", "WEv",
                WS_OVERLAPPED, CW_USEDEFAULT, CW_USEDEFAULT, 200, 120,
                IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("w_form_created=" + (hForm != IntPtr.Zero));

            P1 = new Ctrl(); P1.Id = 0x301; P1.Name = "P1";
            P2 = new Ctrl(); P2.Id = 0x302; P2.Name = "P2";
            P3 = new Ctrl(); P3.Id = 0x303; P3.Name = "P3";
            P1.Click += OnA; P1.Click += OnB;
            P2.Click += OnC;
            P3.Key += OnK1;

            int ok1 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P1.Id), IntPtr.Zero);
            int ok2 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P2.Id), IntPtr.Zero);
            int ok3 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P1.Id), IntPtr.Zero);
            int ok4 = PostMessageW(hForm, WM_KEYDOWN, new IntPtr(VK_F2), IntPtr.Zero);
            Console.WriteLine("w_inject=" + (ok1 != 0 && ok2 != 0 && ok3 != 0 && ok4 != 0));

            ResetSeq();
            MSG m;
            int drained = 0;
            while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref m);
                DispatchMessageW(ref m);
                drained++;
                if (drained >= 200) break;
            }
            Console.WriteLine("w_seq=" + Seq);   // A;B;C;A;B;K1;

            // Unsubscribe P2 (C) while the window is still alive; re-dispatch.
            P2.Click -= OnC;
            ResetSeq();
            int ok5 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P2.Id), IntPtr.Zero);
            int ok6 = PostMessageW(hForm, WM_COMMAND, new IntPtr(P1.Id), IntPtr.Zero);
            Console.WriteLine("w_inject2=" + (ok5 != 0 && ok6 != 0));
            int drained2 = 0;
            while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref m);
                DispatchMessageW(ref m);
                drained2++;
                if (drained2 >= 200) break;
            }
            Console.WriteLine("w_seq2=" + Seq);  // A;B;  (P2's C handler removed)

            PostQuitMessage(0);
            DestroyWindow(hForm);

            // ============ W9: many controls, selective unsubscribe ============
            Ctrl d1 = new Ctrl(); d1.Id = 0x401; d1.Name = "d1";
            Ctrl d2 = new Ctrl(); d2.Id = 0x402; d2.Name = "d2";
            Ctrl d3 = new Ctrl(); d3.Id = 0x403; d3.Name = "d3";
            Ctrl d4 = new Ctrl(); d4.Id = 0x404; d4.Name = "d4";
            d1.Click += OnA; d1.Click += OnB;
            d2.Click += OnC; d2.Click += OnD;
            d3.Click += OnE; d3.Click += OnF;
            d4.Click += OnG; d4.Click += OnH;
            ResetSeq();
            d1.RaiseClick(); d2.RaiseClick(); d3.RaiseClick(); d4.RaiseClick();
            Console.WriteLine("w9_seq=" + Seq);                 // A;B;C;D;E;F;G;H;
            d3.Click -= OnE;                                    // remove first of d3
            ResetSeq();
            d1.RaiseClick(); d3.RaiseClick(); d4.RaiseClick(); d2.RaiseClick();
            Console.WriteLine("w9_seq2=" + Seq);                // A;B;F;G;H;C;D;
        }
    }
}

// WinForms-style managed Control: field-backed Click/Key/Dbl/Text events with
// custom add/remove accessors -> Delegate.Combine/Remove. Counts come from
// GetInvocationList().Length (0 = null).
public class Ctrl
{
    public int Id;
    public string Name;
    private Cases.Winevent.H _click;
    private Cases.Winevent.H _key;
    private Cases.Winevent.H _dbl;
    private Cases.Winevent.H _text;

    public event Cases.Winevent.H Click
    {
        add { _click = _click + value; }
        remove { _click = _click - value; }
    }

    public event Cases.Winevent.H Key
    {
        add { _key = _key + value; }
        remove { _key = _key - value; }
    }

    public event Cases.Winevent.H Dbl
    {
        add { _dbl = _dbl + value; }
        remove { _dbl = _dbl - value; }
    }

    public event Cases.Winevent.H Text
    {
        add { _text = _text + value; }
        remove { _text = _text - value; }
    }

    public int ClickCount
    {
        get { return _click == null ? 0 : _click.GetInvocationList().Length; }
    }

    public int KeyCount
    {
        get { return _key == null ? 0 : _key.GetInvocationList().Length; }
    }

    public int DblCount
    {
        get { return _dbl == null ? 0 : _dbl.GetInvocationList().Length; }
    }

    public int TextCount
    {
        get { return _text == null ? 0 : _text.GetInvocationList().Length; }
    }

    public void RaiseClick() { if (_click != null) _click(); }
    public void RaiseKey() { if (_key != null) _key(); }
    public void RaiseDbl() { if (_dbl != null) _dbl(); }
    public void RaiseText() { if (_text != null) _text(); }
}
