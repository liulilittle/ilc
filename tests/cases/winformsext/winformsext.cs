// winformsext.cs -- WinForms EXTENSION (e2e batch C20): deep managed event
// sequence / unsubscribe semantics on top of the stage-4 event model
// (winforms1.cs Phase 4 -- the WfControl-style field-backed Click/KeyDown
// events routed through Delegate.Combine/Remove). The net48 oracle runs the
// SAME source; the ILC side must produce byte-identical sequence strings and
// subscription counts.
//
// Coverage (the worklist's "event sequence/unsubscribe -- stage-4 post-fix verification"):
//   * Order preservation: 3-handler multicast fires in subscription order;
//     removing the MIDDLE handler keeps first/third order.
//   * Duplicate handler added twice fires TWICE; one -= removes the LAST
//     occurrence (net48 Delegate.Remove contract -- delegateext.cpp
//     icall_delegate_remove scans for the last sub-list match).
//   * Removing a handler that was never subscribed is a NO-OP (identity).
//   * Removing the only handler nulls the event; raising does nothing.
//   * Re-subscribe after full removal works again.
//   * The same handler on TWO controls; unsubscribing from one leaves the
//     other live.
//   * Click and KeyDown are independent events on one control.
//   * Remove-first / remove-last / remove-all of a 3-handler list.
//   * A 5-handler list (with a duplicate) -- full teardown one by one.
//   * WinForms flavor: WM_COMMAND (by control id) + WM_KEYDOWN dispatch into
//     the managed event system via a real (hidden) window + posted messages,
//     PeekMessageW-drained in FIFO order (deterministic).
//
// Every expected value is spelled in a trailing comment.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cases
{
    public static class Winformsext
    {
        public const int WM_COMMAND = 0x0111;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_QUIT = 0x0012;
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
        public static void OnKey() { Seq = Seq + "K;"; }

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
            // ============ S1: order preservation + middle removal ============
            Ctrl c1 = new Ctrl(); c1.Id = 1; c1.Name = "c1";
            c1.Click += OnA; c1.Click += OnB; c1.Click += OnC;
            Console.WriteLine("s1_subs=" + c1.ClickCount);      // 3
            ResetSeq(); c1.RaiseClick();
            Console.WriteLine("s1_seq=" + Seq);                 // A;B;C;
            c1.Click -= OnB;                                    // remove the middle
            Console.WriteLine("s1_subs_after=" + c1.ClickCount);// 2
            ResetSeq(); c1.RaiseClick();
            Console.WriteLine("s1_seq_after=" + Seq);           // A;C;

            // ============ S2: duplicate handler, last-occurrence remove ============
            Ctrl c2 = new Ctrl(); c2.Id = 2; c2.Name = "c2";
            c2.Click += OnA; c2.Click += OnA;
            Console.WriteLine("s2_subs=" + c2.ClickCount);      // 2 (dup counted twice)
            ResetSeq(); c2.RaiseClick();
            Console.WriteLine("s2_seq=" + Seq);                 // A;A;
            c2.Click -= OnA;                                    // removes the LAST A
            Console.WriteLine("s2_subs_rm1=" + c2.ClickCount);  // 1
            ResetSeq(); c2.RaiseClick();
            Console.WriteLine("s2_seq_rm1=" + Seq);             // A;
            c2.Click -= OnA;                                    // removes the last one -> null
            Console.WriteLine("s2_subs_rm2=" + c2.ClickCount);  // 0
            ResetSeq(); c2.RaiseClick();                        // null -> no-op
            Console.WriteLine("s2_seq_rm2=" + Seq);             // (empty)

            // ============ S3: removing a non-subscribed handler ============
            Ctrl c3 = new Ctrl(); c3.Id = 3; c3.Name = "c3";
            c3.Click += OnB;
            c3.Click -= OnA;                                    // absent -> no-op
            Console.WriteLine("s3_subs_noop=" + c3.ClickCount); // 1
            ResetSeq(); c3.RaiseClick();
            Console.WriteLine("s3_seq=" + Seq);                 // B;

            // ============ S4: re-subscribe after full removal ============
            Ctrl c4 = new Ctrl(); c4.Id = 4; c4.Name = "c4";
            c4.Click += OnC;
            c4.Click -= OnC;
            Console.WriteLine("s4_subs0=" + c4.ClickCount);     // 0
            ResetSeq(); c4.RaiseClick();                        // null -> no-op
            Console.WriteLine("s4_seq0=" + Seq);                // (empty)
            c4.Click += OnC;
            Console.WriteLine("s4_subs1=" + c4.ClickCount);     // 1
            ResetSeq(); c4.RaiseClick();
            Console.WriteLine("s4_seq1=" + Seq);                // C;

            // ============ S5: one handler, two controls, unsubscribe one ============
            Ctrl c5 = new Ctrl(); c5.Id = 5; c5.Name = "c5";
            Ctrl c6 = new Ctrl(); c6.Id = 6; c6.Name = "c6";
            c5.Click += OnB; c6.Click += OnB;
            c6.Click -= OnB;
            Console.WriteLine("s5_subs_left=" + c5.ClickCount); // 1 (still subscribed on c5)
            Console.WriteLine("s5_subs_rm=" + c6.ClickCount);   // 0 (removed from c6)
            ResetSeq(); c5.RaiseClick(); c6.RaiseClick();
            Console.WriteLine("s5_seq=" + Seq);                 // B;  (c6 no-op)

            // ============ S6: Click and Key independent on one control ============
            Ctrl c7 = new Ctrl(); c7.Id = 7; c7.Name = "c7";
            c7.Click += OnA; c7.Key += OnKey;
            ResetSeq(); c7.RaiseClick(); c7.RaiseKey(); c7.RaiseClick();
            Console.WriteLine("s6_seq=" + Seq);                 // A;K;A;
            c7.Key -= OnKey;
            Console.WriteLine("s6_key_subs=" + c7.KeyCount);    // 0
            Console.WriteLine("s6_click_subs=" + c7.ClickCount);// 1
            ResetSeq(); c7.RaiseKey(); c7.RaiseClick();
            Console.WriteLine("s6_seq2=" + Seq);                // A;  (key no-op)

            // ============ S7: remove FIRST then LAST of a 3-list ============
            Ctrl c8 = new Ctrl(); c8.Id = 8; c8.Name = "c8";
            c8.Click += OnA; c8.Click += OnB; c8.Click += OnC;
            c8.Click -= OnA;                                    // remove first
            ResetSeq(); c8.RaiseClick();
            Console.WriteLine("s7_seq1=" + Seq);                // B;C;
            c8.Click -= OnC;                                    // remove last
            ResetSeq(); c8.RaiseClick();
            Console.WriteLine("s7_seq2=" + Seq);                // B;
            c8.Click -= OnB;
            Console.WriteLine("s7_subs=" + c8.ClickCount);      // 0
            ResetSeq(); c8.RaiseClick();
            Console.WriteLine("s7_seq3=" + Seq);                // (empty)

            // ============ S8: 5-handler list with a duplicate, teardown ============
            Ctrl c9 = new Ctrl(); c9.Id = 9; c9.Name = "c9";
            c9.Click += OnA; c9.Click += OnB; c9.Click += OnC; c9.Click += OnD; c9.Click += OnA;
            Console.WriteLine("s8_subs=" + c9.ClickCount);      // 5
            ResetSeq(); c9.RaiseClick();
            Console.WriteLine("s8_seq=" + Seq);                 // A;B;C;D;A;
            c9.Click -= OnA;                                    // removes the LAST A
            Console.WriteLine("s8_subs_rm=" + c9.ClickCount);   // 4
            ResetSeq(); c9.RaiseClick();
            Console.WriteLine("s8_seq_rm=" + Seq);              // A;B;C;D;
            c9.Click -= OnB; c9.Click -= OnC; c9.Click -= OnD;
            Console.WriteLine("s8_subs_2=" + c9.ClickCount);    // 1
            ResetSeq(); c9.RaiseClick();
            Console.WriteLine("s8_seq_2=" + Seq);               // A;
            c9.Click -= OnA;
            Console.WriteLine("s8_subs_3=" + c9.ClickCount);    // 0

            // ============ WinForms dispatch section (real window) ============
            IntPtr hModule = GetModuleHandleW(null);
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = 80;
            wc.style = 0;
            RootWndProc = WndProcImpl;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
            wc.hInstance = hModule;
            IntPtr cls = Marshal.StringToHGlobalUni("WfExtClass");
            wc.lpszClassName = cls;
            ushort atom = RegisterClassExW(ref wc);
            Console.WriteLine("w_class_registered=" + (atom != 0));
            Marshal.FreeHGlobal(cls);

            IntPtr hForm = CreateWindowExW(0, "WfExtClass", "WfExt",
                WS_OVERLAPPED, CW_USEDEFAULT, CW_USEDEFAULT, 200, 120,
                IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("w_form_created=" + (hForm != IntPtr.Zero));

            // Two Click controls + one Key control; P1/P2 share OnShared-style
            // duplicate coverage, P3 handles WM_KEYDOWN (VK_F2 sentinel).
            P1 = new Ctrl(); P1.Id = 0x201; P1.Name = "P1";
            P2 = new Ctrl(); P2.Id = 0x202; P2.Name = "P2";
            P3 = new Ctrl(); P3.Id = 0x203; P3.Name = "P3";
            P1.Click += OnA; P1.Click += OnB;
            P2.Click += OnC;
            P3.Key += OnKey;

            // Post: click P1, click P2, click P1 again, key (VK_F2) -> P3.
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
            Console.WriteLine("w_seq=" + Seq);   // A;B;C;A;B;K;

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
        }
    }
}

// Stage-4 style managed Control: field-backed Click/Key events with custom
// add/remove accessors -> Delegate.Combine/Remove (the delechain-proven
// multicast path). Counts come from GetInvocationList().Length (0 = null).
public class Ctrl
{
    public int Id;
    public string Name;
    private Cases.Winformsext.H _click;
    private Cases.Winformsext.H _key;

    public event Cases.Winformsext.H Click
    {
        add { _click = _click + value; }
        remove { _click = _click - value; }
    }

    public event Cases.Winformsext.H Key
    {
        add { _key = _key + value; }
        remove { _key = _key - value; }
    }

    public int ClickCount
    {
        get { return _click == null ? 0 : _click.GetInvocationList().Length; }
    }

    public int KeyCount
    {
        get { return _key == null ? 0 : _key.GetInvocationList().Length; }
    }

    public void RaiseClick() { if (_click != null) _click(); }
    public void RaiseKey() { if (_key != null) _key(); }
}
