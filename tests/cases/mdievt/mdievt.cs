// mdievt.cs -- MDI EVENT deep probe (e2e batch C26, "MDI event deep probe: child activation order /
// per-child events / parent-child linkage -- after WinForms stage 11"). Builds on winforms1 Phase 11 (the
// simplified MDI surface: an MDI FRAME (top-level, WS_CLIPCHILDREN) -> its MDI
// CLIENT (a WS_CHILD, the single parent of all children) -> 3 MDI CHILD
// windows (WS_CHILD of the client, each with its OWN WfMdiChild managed model
// and its OWN Click event); WM_MDIACTIVATE posted to the CLIENT activates a
// child; WF_MDI_CHILD_MSG posted to a child fires ONLY that child's Click)
// and goes DEEPER on the MDI event model:
//
//   * ACTIVATION ORDER: post sequences A,B,C / B,A,C / A,A,B -- the client's
//     MdiActSeq proves the FIFO drain order; re-activating a child appends it
//     again (A,A,B -> "A;A;B;"). The active-child pointer and the PER-CHILD
//     ActivationCount (bumped by the client on each activation) are read back.
//   * PARENT->CHILD LINKAGE (frame drives child): WM_COMMAND to the FRAME with
//     a child's id in the low word routes into THAT child's OWN Click event --
//     the parent container dispatches to the right child by id.
//   * CHILD->PARENT LINKAGE (client notifies frame): on every WM_MDIACTIVATE
//     the CLIENT posts WF_MDI_FRAME_NOTIFY to the FRAME, whose WndProc records
//     the child tag -- the frame's notify sequence MIRRORS the activation
//     sequence. Every child Click also bumps a FRAME-LEVEL ChildClickTotal.
//   * PER-CHILD EVENT ISOLATION: child A carries TWO handlers (A1,A2), child B
//     ONE (B1), child C TWO (C1,C2); a WF_MDI_CHILD_MSG posted to child A
//     fires ONLY A's handlers, in subscription order (A1;A2;), never B's or
//     C's -- and vice versa.
//   * UNSUBSCRIBE ISOLATION: removing ONE handler from child A (A1) leaves
//     A's other handler AND every other child untouched; re-adding A1 restores
//     the full sequence.
//   * TREE / CREATE COUNTS: GetParent proves child->client->frame; the
//     WM_CREATE counters (frame/client/child) are read back; the frame's
//     WS_CLIPCHILDREN style is read back via GetWindowLongW.
//
// Headless acceptance: PostMessage + PeekMessageW bounded drain (phase-4
// pattern); all windows are hidden children of a hidden frame. Only booleans/
// counts/sequence strings derived from our own model are printed -- NEVER raw
// handle values or window-manager return values (SetActiveWindow's return
// depends on the prior active window, which differs run to run).
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Mdievt
    {
        public const int WM_COMMAND = 0x0111;
        public const int WM_MDIACTIVATE = 0x0222;
        public const int WM_CREATE = 0x0001;
        public const int PM_REMOVE = 0x0001;
        public const int WS_OVERLAPPED = 0x00000000;
        public const int WS_CHILD = 0x40000000;
        public const int WS_CLIPCHILDREN = 0x02000000;
        public const int WS_CLIPSIBLINGS = 0x04000000;
        public const int CS_HREDRAW = 0x0001;
        public const int CS_VREDRAW = 0x0002;
        public const int GWL_STYLE = -16;

        // Custom posted messages. WF_MDI_CHILD_MSG raises a child's OWN Click
        // (phase-11 channel); WF_MDI_FRAME_NOTIFY is the client -> frame
        // activation notification (the CHILD->PARENT linkage channel).
        public const int WF_MDI_CHILD_MSG = 0x8006;
        public const int WF_MDI_FRAME_NOTIFY = 0x8007;

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
        public delegate void ClickHandler();

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

        [DllImport("user32", EntryPoint = "GetParent")]
        public static extern IntPtr GetParent(IntPtr hwnd);

        [DllImport("user32", EntryPoint = "GetWindowLongW")]
        public static extern IntPtr GetWindowLongW(IntPtr hwnd, int index);

        [DllImport("user32", EntryPoint = "SetActiveWindow")]
        public static extern IntPtr SetActiveWindow(IntPtr hwnd);

        // ---- the MDI model (frame / client / children) ----
        public static IntPtr MdiFrameHwnd;
        public static IntPtr MdiClientHwnd;
        public static WfMdiChild MdiChildA;
        public static WfMdiChild MdiChildB;
        public static WfMdiChild MdiChildC;
        public static IntPtr MdiActiveChild;

        public static WndProc MdiFrameWndProc;
        public static WndProc MdiClientWndProc;
        public static WndProc MdiChildWndProc;

        // ---- observable state ----
        public static string MdiActSeq = "";
        public static string MdiNotifySeq = "";
        public static string MdiChildSeq = "";
        public static int MdiChildMsgCount;
        public static int MdiFrameCreateCount;
        public static int MdiClientCreateCount;
        public static int MdiChildCreateCount;
        public static int ChildClickTotal;

        public static void ResetAct() { MdiActSeq = ""; MdiNotifySeq = ""; }
        public static void ResetChild() { MdiChildSeq = ""; MdiChildMsgCount = 0; ChildClickTotal = 0; }

        // ---- per-child handlers (each appends its own tag + bumps the
        // frame-level ChildClickTotal -- the child->parent linkage) ----
        public static void OnA1() { MdiChildSeq = MdiChildSeq + "A1;"; ChildClickTotal++; }
        public static void OnA2() { MdiChildSeq = MdiChildSeq + "A2;"; ChildClickTotal++; }
        public static void OnB1() { MdiChildSeq = MdiChildSeq + "B1;"; ChildClickTotal++; }
        public static void OnC1() { MdiChildSeq = MdiChildSeq + "C1;"; ChildClickTotal++; }
        public static void OnC2() { MdiChildSeq = MdiChildSeq + "C2;"; ChildClickTotal++; }

        // ---- MDI FRAME WndProc: WM_CREATE count; WF_MDI_FRAME_NOTIFY records
        // the activated child (client -> frame linkage); WM_COMMAND routes by
        // child id to that child's OWN Click (frame -> child linkage). ----
        public static IntPtr MdiFrameWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { MdiFrameCreateCount++; return IntPtr.Zero; }
            if (msg == WF_MDI_FRAME_NOTIFY)
            {
                if (MdiChildA != null && wParam == MdiChildA.Handle) MdiNotifySeq = MdiNotifySeq + "A;";
                else if (MdiChildB != null && wParam == MdiChildB.Handle) MdiNotifySeq = MdiNotifySeq + "B;";
                else if (MdiChildC != null && wParam == MdiChildC.Handle) MdiNotifySeq = MdiNotifySeq + "C;";
                return IntPtr.Zero;
            }
            if (msg == WM_COMMAND)
            {
                int cid = (int)(wParam.ToInt64() & 0xFFFFL);
                if (MdiChildA != null && cid == MdiChildA.Id) MdiChildA.RaiseClick();
                else if (MdiChildB != null && cid == MdiChildB.Id) MdiChildB.RaiseClick();
                else if (MdiChildC != null && cid == MdiChildC.Id) MdiChildC.RaiseClick();
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // ---- MDI CLIENT WndProc: WM_CREATE count; WM_MDIACTIVATE records the
        // activation order + bumps the child's ActivationCount + activates the
        // child + NOTIFIES the frame (child->parent linkage). ----
        public static IntPtr MdiClientWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { MdiClientCreateCount++; return IntPtr.Zero; }
            if (msg == WM_MDIACTIVATE)
            {
                MdiActiveChild = wParam;
                if (MdiChildA != null && wParam == MdiChildA.Handle) { MdiActSeq = MdiActSeq + "A;"; MdiChildA.ActivationCount++; }
                else if (MdiChildB != null && wParam == MdiChildB.Handle) { MdiActSeq = MdiActSeq + "B;"; MdiChildB.ActivationCount++; }
                else if (MdiChildC != null && wParam == MdiChildC.Handle) { MdiActSeq = MdiActSeq + "C;"; MdiChildC.ActivationCount++; }
                SetActiveWindow(wParam);
                PostMessageW(MdiFrameHwnd, WF_MDI_FRAME_NOTIFY, wParam, IntPtr.Zero);
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // ---- MDI CHILD WndProc (shared class): WM_CREATE count; a posted
        // WF_MDI_CHILD_MSG raises ONLY the matching child's OWN Click. ----
        public static IntPtr MdiChildWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { MdiChildCreateCount++; return IntPtr.Zero; }
            if (msg == WF_MDI_CHILD_MSG)
            {
                MdiChildMsgCount++;
                if (MdiChildA != null && hwnd == MdiChildA.Handle) MdiChildA.RaiseClick();
                else if (MdiChildB != null && hwnd == MdiChildB.Handle) MdiChildB.RaiseClick();
                else if (MdiChildC != null && hwnd == MdiChildC.Handle) MdiChildC.RaiseClick();
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // ---- bounded FIFO drain (PeekMessageW PM_REMOVE) ----
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
            Console.WriteLine("m_module_nonzero=" + (hModule != IntPtr.Zero));

            // ---- register the three window classes ----
            WNDCLASSEX wcFrame = new WNDCLASSEX();
            wcFrame.cbSize = 80; wcFrame.style = CS_HREDRAW | CS_VREDRAW;
            MdiFrameWndProc = MdiFrameWndProcImpl;
            wcFrame.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(MdiFrameWndProc);
            wcFrame.hInstance = hModule;
            IntPtr clsFrame = Marshal.StringToHGlobalUni("MdievtFrameClass");
            wcFrame.lpszClassName = clsFrame;
            ushort aFrame = RegisterClassExW(ref wcFrame);
            Marshal.FreeHGlobal(clsFrame);

            WNDCLASSEX wcClient = new WNDCLASSEX();
            wcClient.cbSize = 80; wcClient.style = CS_HREDRAW | CS_VREDRAW;
            MdiClientWndProc = MdiClientWndProcImpl;
            wcClient.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(MdiClientWndProc);
            wcClient.hInstance = hModule;
            IntPtr clsClient = Marshal.StringToHGlobalUni("MdievtClientClass");
            wcClient.lpszClassName = clsClient;
            ushort aClient = RegisterClassExW(ref wcClient);
            Marshal.FreeHGlobal(clsClient);

            WNDCLASSEX wcChild = new WNDCLASSEX();
            wcChild.cbSize = 80; wcChild.style = CS_HREDRAW | CS_VREDRAW;
            MdiChildWndProc = MdiChildWndProcImpl;
            wcChild.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(MdiChildWndProc);
            wcChild.hInstance = hModule;
            IntPtr clsChild = Marshal.StringToHGlobalUni("MdievtChildClass");
            wcChild.lpszClassName = clsChild;
            ushort aChild = RegisterClassExW(ref wcChild);
            Marshal.FreeHGlobal(clsChild);

            Console.WriteLine("m_classes_registered=" + (aFrame != 0 && aClient != 0 && aChild != 0));

            // ---- create the MDI tree: frame -> client -> 3 children ----
            IntPtr hFrame = CreateWindowExW(0, "MdievtFrameClass", "MdievtFrame",
                WS_OVERLAPPED | WS_CLIPCHILDREN, 0, 0, 360, 260,
                IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
            MdiFrameHwnd = hFrame;
            Console.WriteLine("m_frame_created=" + (hFrame != IntPtr.Zero));
            IntPtr fStyle = GetWindowLongW(hFrame, GWL_STYLE);
            Console.WriteLine("m_frame_clipchildren=" + ((fStyle.ToInt64() & WS_CLIPCHILDREN) != 0));
            Console.WriteLine("m_frame_create_count=" + MdiFrameCreateCount);

            IntPtr hClient = CreateWindowExW(0, "MdievtClientClass", "",
                WS_CHILD | WS_CLIPSIBLINGS, 0, 0, 360, 260,
                hFrame, IntPtr.Zero, hModule, IntPtr.Zero);
            MdiClientHwnd = hClient;
            Console.WriteLine("m_client_created=" + (hClient != IntPtr.Zero));
            Console.WriteLine("m_client_parent_is_frame=" + (GetParent(hClient) == hFrame));

            IntPtr hA = CreateWindowExW(0, "MdievtChildClass", "MdievtDocA",
                WS_CHILD | WS_CLIPSIBLINGS, 10, 10, 160, 100,
                hClient, IntPtr.Zero, hModule, IntPtr.Zero);
            IntPtr hB = CreateWindowExW(0, "MdievtChildClass", "MdievtDocB",
                WS_CHILD | WS_CLIPSIBLINGS, 30, 30, 160, 100,
                hClient, IntPtr.Zero, hModule, IntPtr.Zero);
            IntPtr hC = CreateWindowExW(0, "MdievtChildClass", "MdievtDocC",
                WS_CHILD | WS_CLIPSIBLINGS, 50, 50, 160, 100,
                hClient, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("m_children_created=" + (hA != IntPtr.Zero && hB != IntPtr.Zero && hC != IntPtr.Zero));
            Console.WriteLine("m_childA_parent_is_client=" + (GetParent(hA) == hClient));
            Console.WriteLine("m_childB_parent_is_client=" + (GetParent(hB) == hClient));
            Console.WriteLine("m_childC_parent_is_client=" + (GetParent(hC) == hClient));
            Console.WriteLine("m_child_create_count=" + MdiChildCreateCount);

            // ---- per-child managed models (each with its OWN Click) ----
            MdiChildA = new WfMdiChild(); MdiChildA.Handle = hA; MdiChildA.Id = 1; MdiChildA.Name = "A";
            MdiChildB = new WfMdiChild(); MdiChildB.Handle = hB; MdiChildB.Id = 2; MdiChildB.Name = "B";
            MdiChildC = new WfMdiChild(); MdiChildC.Handle = hC; MdiChildC.Id = 3; MdiChildC.Name = "C";
            MdiChildA.Click += OnA1; MdiChildA.Click += OnA2;
            MdiChildB.Click += OnB1;
            MdiChildC.Click += OnC1; MdiChildC.Click += OnC2;

            // ============ S1: activation order A,B,C (phase-11 baseline) ============
            ResetAct();
            int p1 = PostMessageW(hClient, WM_MDIACTIVATE, hA, IntPtr.Zero);
            int p2 = PostMessageW(hClient, WM_MDIACTIVATE, hB, IntPtr.Zero);
            int p3 = PostMessageW(hClient, WM_MDIACTIVATE, hC, IntPtr.Zero);
            Console.WriteLine("s1_act_inject=" + (p1 != 0 && p2 != 0 && p3 != 0));
            int d1 = Drain();
            Console.WriteLine("s1_act_seq=" + MdiActSeq);          // A;B;C;
            Console.WriteLine("s1_notify_seq=" + MdiNotifySeq);    // A;B;C;
            Console.WriteLine("s1_active_is_C=" + (MdiActiveChild == hC));
            // NOTE (defect record): `".." + obj.IntField` boxes an int32 CLASS
            // FIELD read -- the transpiler emits `static_cast<Il2CppObject*>(
            // (&(obj->IntField)))` (int32* -> Il2CppObject*, C2440/C++ error) --
            // so the field is copied into a LOCAL int first (decops precedent).
            int a1 = MdiChildA.ActivationCount;
            int b1 = MdiChildB.ActivationCount;
            int c1 = MdiChildC.ActivationCount;
            Console.WriteLine("s1_act_A=" + a1); // 1
            Console.WriteLine("s1_act_B=" + b1); // 1
            Console.WriteLine("s1_act_C=" + c1); // 1
            Console.WriteLine("s1_drained=" + (d1 >= 3));

            // ============ S2: activation reorder B,A,C ============
            ResetAct();
            PostMessageW(hClient, WM_MDIACTIVATE, hB, IntPtr.Zero);
            PostMessageW(hClient, WM_MDIACTIVATE, hA, IntPtr.Zero);
            PostMessageW(hClient, WM_MDIACTIVATE, hC, IntPtr.Zero);
            Drain();
            Console.WriteLine("s2_act_seq=" + MdiActSeq);          // B;A;C;
            Console.WriteLine("s2_notify_seq=" + MdiNotifySeq);    // B;A;C;
            Console.WriteLine("s2_active_is_C=" + (MdiActiveChild == hC));
            int a2 = MdiChildA.ActivationCount;
            int b2 = MdiChildB.ActivationCount;
            int c2 = MdiChildC.ActivationCount;
            Console.WriteLine("s2_act_A=" + a2); // 2
            Console.WriteLine("s2_act_B=" + b2); // 2
            Console.WriteLine("s2_act_C=" + c2); // 2

            // ============ S3: re-activation appends (A,A,B) ============
            ResetAct();
            PostMessageW(hClient, WM_MDIACTIVATE, hA, IntPtr.Zero);
            PostMessageW(hClient, WM_MDIACTIVATE, hA, IntPtr.Zero);
            PostMessageW(hClient, WM_MDIACTIVATE, hB, IntPtr.Zero);
            Drain();
            Console.WriteLine("s3_act_seq=" + MdiActSeq);          // A;A;B;
            Console.WriteLine("s3_notify_seq=" + MdiNotifySeq);    // A;A;B;
            Console.WriteLine("s3_active_is_B=" + (MdiActiveChild == hB));
            int a3 = MdiChildA.ActivationCount;
            int b3 = MdiChildB.ActivationCount;
            int c3 = MdiChildC.ActivationCount;
            Console.WriteLine("s3_act_A=" + a3); // 4
            Console.WriteLine("s3_act_B=" + b3); // 3
            Console.WriteLine("s3_act_C=" + c3); // 2

            // ============ S4: per-child event isolation (WF_MDI_CHILD_MSG) ============
            ResetChild();
            int q1 = PostMessageW(hA, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
            int q2 = PostMessageW(hB, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
            int q3 = PostMessageW(hC, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
            Console.WriteLine("s4_child_inject=" + (q1 != 0 && q2 != 0 && q3 != 0));
            int d4 = Drain();
            Console.WriteLine("s4_child_seq=" + MdiChildSeq);      // A1;A2;B1;C1;C2;
            Console.WriteLine("s4_child_msg_count=" + MdiChildMsgCount); // 3
            Console.WriteLine("s4_child_total=" + ChildClickTotal);      // 5
            Console.WriteLine("s4_drained=" + (d4 >= 3));

            // ============ S5: parent->child linkage (WM_COMMAND to the FRAME) ============
            ResetChild();
            PostMessageW(hFrame, WM_COMMAND, new IntPtr(1), IntPtr.Zero);
            PostMessageW(hFrame, WM_COMMAND, new IntPtr(2), IntPtr.Zero);
            PostMessageW(hFrame, WM_COMMAND, new IntPtr(3), IntPtr.Zero);
            PostMessageW(hFrame, WM_COMMAND, new IntPtr(1), IntPtr.Zero);
            Drain();
            Console.WriteLine("s5_cmd_seq=" + MdiChildSeq);        // A1;A2;B1;C1;C2;A1;A2;
            Console.WriteLine("s5_cmd_total=" + ChildClickTotal);  // 7

            // ============ S6: unsubscribe isolation (remove A1 from child A) ============
            MdiChildA.Click -= OnA1;
            Console.WriteLine("s6_subsA=" + MdiChildA.ClickCount); // 1 (A2 only)
            Console.WriteLine("s6_subsB=" + MdiChildB.ClickCount); // 1 (untouched)
            Console.WriteLine("s6_subsC=" + MdiChildC.ClickCount); // 2 (untouched)
            ResetChild();
            PostMessageW(hA, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
            PostMessageW(hB, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
            PostMessageW(hC, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
            Drain();
            Console.WriteLine("s6_child_seq=" + MdiChildSeq);      // A2;B1;C1;C2;
            Console.WriteLine("s6_child_total=" + ChildClickTotal); // 4

            // ============ S7: re-add A1 restores the full sequence ============
            MdiChildA.Click += OnA1;
            Console.WriteLine("s7_subsA=" + MdiChildA.ClickCount); // 2
            ResetChild();
            PostMessageW(hA, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
            PostMessageW(hC, WF_MDI_CHILD_MSG, IntPtr.Zero, IntPtr.Zero);
            Drain();
            Console.WriteLine("s7_child_seq=" + MdiChildSeq);      // A2;A1;C1;C2; (A2 first: -= removed A1, += re-appends)
            Console.WriteLine("s7_child_total=" + ChildClickTotal); // 4

            // ============ S8: final counts (create + event totals) ============
            // ResetChild() in S6/S7 zeroed the message/click counters, so the
            // totals are the LAST section's values (S7: 2 messages, 4 clicks).
            Console.WriteLine("m_frame_create_count=" + MdiFrameCreateCount);   // 1
            Console.WriteLine("m_client_create_count=" + MdiClientCreateCount); // 1
            Console.WriteLine("m_child_create_count=" + MdiChildCreateCount);   // 3
            Console.WriteLine("m_child_msg_count_total=" + MdiChildMsgCount);   // 2
            Console.WriteLine("m_click_total=" + ChildClickTotal);              // 4
        }
    }

    // The managed MDI CHILD model: a native HWND + its OWN Click event (the
    // per-child event delegate). Same multicast event shape as WfControl/
    // WfMdiChild in winforms1 Phase 11; adds a child Id (for the frame's
    // WM_COMMAND routing) and an ActivationCount (bumped by the client on each
    // WM_MDIACTIVATE).
    public class WfMdiChild
    {
        public IntPtr Handle;
        public int Id;
        public string Name;
        public int ActivationCount;
        private Cases.Mdievt.ClickHandler _click;

        public event Cases.Mdievt.ClickHandler Click
        {
            add { _click = _click + value; }
            remove { _click = _click - value; }
        }

        public int ClickCount
        {
            get { return _click == null ? 0 : _click.GetInvocationList().Length; }
        }

        public void RaiseClick() { if (_click != null) _click(); }
    }
}

