//------------------------------------------------------------------------------
// uimulti.cs -- MULTI-THREADED UI deep probe 2 (e2e batch C30, "多线程 UI 深测
// 2: 跨线程计数/状态一致性 -- 阶段 13 后"). The WinForms stage-13 surface
// (commit 8f7b6cd, winforms1 Phase 13) wired ONE worker thread posting four
// WF_WORKER_MSG cross-thread messages to the main form and proved posted count
// == processed count 1:1. THIS case goes DEEPER on the same wired channel
// (user32 PostMessageW cross-thread -> main-thread PeekMessageW pump ->
// form WndProc, all on the exact phase-13 icall/PInvoke surface):
//
//   * MULTI-WORKER cross-thread posting: THREE worker threads post to the SAME
//     form concurrently (the "N background workers report progress to one UI"
//     shape). Each worker posts a distinct tag RANGE (A: 0..7, B: 100..107,
//     C: 200..207) and Interlocked-counts its OWN posts.
//   * CROSS-THREAD COUNTING (1:1 pairing, D1/D9): per-worker posted counter
//     (incremented by that worker, allocation-free) vs per-worker PROCESSED
//     counter (incremented by the WndProc on the MAIN thread) MUST agree
//     exactly -- posted == processed for EVERY worker (no lost message, no
//     double dispatch, no ghost delivery).
//   * STATE CONSISTENCY: the WndProc accumulates a monotonic UI counter AND a
//     running check-sum of every accepted wParam; after all workers join, the
//     drained total MUST equal the sum of posted wParams (every cross-thread
//     message that left a worker arrived and was counted exactly once).
//   * DETERMINISM: workers never print and never allocate (the M-stage
//     managed allocator is single-threaded by contract -- they only
//     PostMessageW + Interlocked.Increment, both allocation-free). The main
//     thread Joins ALL workers (happens-before) BEFORE draining, so every
//     cross-thread message is already queued when the pump runs; the FIFO
//     dispatch order inside each worker's OWN range is preserved, and the
//     per-worker counters + the final check-sum are scheduling-independent
//     (D2: order NOT guaranteed, but the FINAL state converges to the same
//     values for every interleaving -- posted==processed==check-sum).
//   * The WndProc is the SAME registered-class delegate-thunk channel as
//     winforms1 (RegisterClassExW + Marshal.GetFunctionPointerForDelegate +
//     CreateWindowExW), so the dual-compiler byte-exact guarantee carries over.
//
// net48 ground truth: each worker's posted counter is a plain Interlocked
// int32 that increments exactly once per PostMessageW; the WndProc increments
// its own counters once per dispatched message; Join establishes happens-
// before, so the main thread reads exact final values. Every observable is
// deterministic on BOTH sides.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cases
{
    public static class Uimulti
    {
        // ---- Win32 constants (minimal surface, winforms1 stage-13 set) ----
        public const int WM_CREATE = 0x0001;
        public const int PM_REMOVE = 0x0001;
        public const int CS_HREDRAW = 0x0002;
        public const int CS_VREDRAW = 0x0001;
        public const int WS_OVERLAPPED = 0x00000000;
        public const int WF_WORKER_MSG = 0x8009; // the cross-thread UI message

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
        public static WndProc RootWndProc;

        [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

        [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance,
            IntPtr param);

        [DllImport("user32", EntryPoint = "PeekMessageW")]
        public static extern int PeekMessageW(out MSG msg, IntPtr hwnd, int min, int max, int remove);

        [DllImport("user32", EntryPoint = "TranslateMessage")]
        public static extern int TranslateMessage(ref MSG msg);

        [DllImport("user32", EntryPoint = "DispatchMessageW")]
        public static extern IntPtr DispatchMessageW(ref MSG msg);

        [DllImport("user32", EntryPoint = "DefWindowProcW")]
        public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", EntryPoint = "PostMessageW")]
        public static extern int PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", EntryPoint = "DestroyWindow")]
        public static extern int DestroyWindow(IntPtr hwnd);

        // ---- cross-thread UI state (WndProc is the ONLY writer on the main
        // thread; workers Interlocked their OWN posted counters) ----
        public static IntPtr UiHwnd;

        public static int PostedA;      // worker A (Interlocked by A)
        public static int PostedB;      // worker B (Interlocked by B)
        public static int PostedC;      // worker C (Interlocked by C)
        public static int ProcessedA;   // WndProc (Interlocked, main thread)
        public static int ProcessedB;
        public static int ProcessedC;
        public static int UiCount;      // monotonic accepted-message counter
        public static int UiCheck;      // running check-sum of accepted wParams (int32: tags fit easily)
        public static string UiSeq = ""; // dispatch-order sequence (worker A range)

        // Worker A: posts tags 0..7 (8 messages). Allocation-free.
        public static void WorkerA()
        {
            for (int i = 0; i < 8; i++)
            {
                PostMessageW(UiHwnd, WF_WORKER_MSG, new IntPtr(i), IntPtr.Zero);
                Interlocked.Increment(ref PostedA);
            }
        }

        // Worker B: posts tags 100..107 (8 messages). Allocation-free.
        public static void WorkerB()
        {
            for (int i = 0; i < 8; i++)
            {
                PostMessageW(UiHwnd, WF_WORKER_MSG, new IntPtr(100 + i), IntPtr.Zero);
                Interlocked.Increment(ref PostedB);
            }
        }

        // Worker C: posts tags 200..207 (8 messages). Allocation-free.
        public static void WorkerC()
        {
            for (int i = 0; i < 8; i++)
            {
                PostMessageW(UiHwnd, WF_WORKER_MSG, new IntPtr(200 + i), IntPtr.Zero);
                Interlocked.Increment(ref PostedC);
            }
        }

        // The main-thread WndProc: EVERY dispatched cross-thread message is
        // counted exactly once (1:1 with the posting side) and its tag
        // accumulates into the check-sum.
        public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WF_WORKER_MSG)
            {
                int tag = (int)wParam.ToInt64();
                if (tag >= 0 && tag < 100) { Interlocked.Increment(ref ProcessedA); UiSeq = UiSeq + "w" + tag + ";"; }
                else if (tag >= 100 && tag < 200) { Interlocked.Increment(ref ProcessedB); }
                else { Interlocked.Increment(ref ProcessedC); }
                Interlocked.Increment(ref UiCount);
                Interlocked.Add(ref UiCheck, tag);                return IntPtr.Zero;
            }
            if (msg == WM_CREATE) { return IntPtr.Zero; }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        public static void Main()
        {
            IntPtr hModule = GetModuleHandleW(null);

            // Register the "UiMultiForm" window class (WndProc delegate thunk).
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = 80;
            wc.style = CS_HREDRAW | CS_VREDRAW;
            RootWndProc = WndProcImpl;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
            wc.hInstance = hModule;
            IntPtr clsName = Marshal.StringToHGlobalUni("UiMultiFormClass");
            wc.lpszClassName = clsName;
            ushort atom = RegisterClassExW(ref wc);
            Console.WriteLine("class_registered=" + (atom != 0));
            Marshal.FreeHGlobal(clsName);

            // Create the hidden form (WS_OVERLAPPED, no WS_VISIBLE).
            IntPtr hForm = CreateWindowExW(0, "UiMultiFormClass", "UiMultiForm",
                WS_OVERLAPPED, 0, 0, 320, 200, IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("form_created=" + (hForm != IntPtr.Zero));
            UiHwnd = hForm;

            // ---- start THREE workers concurrently; each posts its own range ----
            PostedA = 0; PostedB = 0; PostedC = 0;
            ProcessedA = 0; ProcessedB = 0; ProcessedC = 0;
            UiCount = 0; UiCheck = 0; UiSeq = "";

            Thread wa = new Thread(WorkerA); wa.IsBackground = true; wa.Start();
            Thread wb = new Thread(WorkerB); wb.IsBackground = true; wb.Start();
            Thread wc_ = new Thread(WorkerC); wc_.IsBackground = true; wc_.Start();
            wa.Join(); wb.Join(); wc_.Join(); // happens-before: all posts queued

            Console.WriteLine("joined=" + (!wa.IsAlive && !wb.IsAlive && !wc_.IsAlive));
            Console.WriteLine("posted_a=" + PostedA);
            Console.WriteLine("posted_b=" + PostedB);
            Console.WriteLine("posted_c=" + PostedC);

            // ---- drain the queue on the MAIN thread (bounded pump) ----
            int dispatched = 0;
            MSG msg;
            while (PeekMessageW(out msg, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
                dispatched++;
                if (dispatched >= 200) break;
            }

            Console.WriteLine("processed_a=" + ProcessedA);
            Console.WriteLine("processed_b=" + ProcessedB);
            Console.WriteLine("processed_c=" + ProcessedC);
            Console.WriteLine("ui_count=" + UiCount);
            Console.WriteLine("ui_check=" + UiCheck);
            Console.WriteLine("seq=" + UiSeq);

            // ---- 1:1 pairing: every posted message was processed once ----
            Console.WriteLine("pair_a=" + (PostedA == ProcessedA));
            Console.WriteLine("pair_b=" + (PostedB == ProcessedB));
            Console.WriteLine("pair_c=" + (PostedC == ProcessedC));
            Console.WriteLine("pair_total=" + (UiCount == PostedA + PostedB + PostedC));

            // ---- state consistency: check-sum == sum of all accepted tags ----
            // worker A tags 0..7 -> 28; B 100..107 -> 828; C 200..207 -> 1628
            long expect = 0;
            for (int i = 0; i < 8; i++) expect += i;
            for (int i = 0; i < 8; i++) expect += 100 + i;
            for (int i = 0; i < 8; i++) expect += 200 + i;
            Console.WriteLine("checksum_ok=" + (UiCheck == expect)); // 28+828+1628 == 2484

            // ---- second drain: nothing left (no ghost delivery) ----
            int leftover = 0;
            while (PeekMessageW(out msg, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
                leftover++;
                if (leftover >= 200) break;
            }
            Console.WriteLine("leftover=" + leftover);
            Console.WriteLine("destroyed=" + (DestroyWindow(hForm) != 0));
        }
    }
}
