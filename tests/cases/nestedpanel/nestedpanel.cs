//------------------------------------------------------------------------------
// nestedpanel.cs -- end-to-end deep probe of NESTED WinForms-style container
// hierarchies and multi-hop event bubbling (the winpanel complement: winpanel
// covered one outer panel + one inner panel; this case stacks THREE panel
// levels and a sibling branch).
//
// Hierarchy under test (all raw user32 P/Invoke, same source on net48 and
// the transpiled runtime):
//   Form (top-level, WpFormClass)
//     +-- PanelA (WS_CHILD, WpPanelAClass)      MoveWindow(20,30,300,200)
//         +-- PanelB (WS_CHILD, WpPanelBClass)  MoveWindow(10,15,180,120)
//         |     +-- PanelC (WS_CHILD, WpPanelCClass) MoveWindow(8,8,140,90)
//         |     |     +-- ButtonC (BUTTON)      MoveWindow(4,4,90,28)
//         |     +-- PanelS (WS_CHILD, WpPanelSClass) MoveWindow(200,10,80,60)
//         +-- Label (STATIC)                    MoveWindow(5,160,200,25)
//
// Container chain: every panel/control is a CHILD of its container, so
// GetParent exposes the FULL 5-deep chain
// (button->C->B->A->form->null). GetWindowRect reports exact child sizes;
// ClientToScreen deltas between a child panel's client origin and its
// parent's equal its MoveWindow position (child windows have no border), so
// the output is byte-exact regardless of the screen origin.
//
// EVENT BUBBLING (the multi-hop probe): a message routed to the DEEPEST
// panel (C) is handled by C's OWN WndProc (its sequence + counter) and then
// BUBBLES up the chain -- C forwards to B (its parent), B forwards to A, A
// forwards to the FORM (3 hops, each appends its own token to its own
// sequence string). A button "click" (WM_COMMAND with the control id posted
// to panel C) follows the same 3-hop bubble. SIBLING ISOLATION: a posted
// message routed to the SIBLING panel S is handled by S's proc ONLY -- PanelA
// (their shared parent) must see NO forwarded notification, proving messages
// bubble up the OWNER chain, not to siblings.
//
// All output is deterministic: parent-chain bools, sizes, client-origin
// deltas, per-panel WM_CREATE counts, event-bubble sequences, and a
// ShowWindow / IsWindowVisible visibility probe on the deepest branch.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Nestedpanel
    {
        // --- style / message constants ---
        public const int CS_HREDRAW = 0x0002;
        public const int CS_VREDRAW = 0x0001;
        public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        public const int WS_CHILD = 0x40000000;
        public const int WS_VISIBLE = 0x10000000;
        public const int WM_CREATE = 0x0001;
        public const int WM_COMMAND = 0x0111;
        public const int WM_CLOSE = 0x0010;
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int WF_DEEP_MSG = 0x0411;
        public const int WF_BUBBLE1 = 0x0412;
        public const int WF_BUBBLE2 = 0x0413;
        public const int WF_FORM_NOTE = 0x0414;
        public const int WF_CHILD_CLICK = 0x0415;
        public const int WF_SIB_MSG = 0x0416;
        public const int ID_BTN_C = 201;
        public const int PM_REMOVE = 0x0001;

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

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        public delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        // --- P/Invoke ---
        [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);
        [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
        [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
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
        [DllImport("user32", EntryPoint = "GetParent")]
        public static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32", EntryPoint = "MoveWindow")]
        public static extern int MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
        [DllImport("user32", EntryPoint = "GetWindowRect")]
        public static extern int GetWindowRect(IntPtr hwnd, out RECT rc);
        [DllImport("user32", EntryPoint = "ClientToScreen")]
        public static extern int ClientToScreen(IntPtr hwnd, ref POINT pt);
        [DllImport("user32", EntryPoint = "ShowWindow")]
        public static extern int ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32", EntryPoint = "IsWindowVisible")]
        public static extern int IsWindowVisible(IntPtr hwnd);

        // --- observable state (one sequence + one counter per window) ---
        public static string FormSeq = "";
        public static string ASeq = "";
        public static string BSeq = "";
        public static string CSeq = "";
        public static string SSeq = "";
        public static int FormCreate = 0;
        public static int ACreate = 0;
        public static int BCreate = 0;
        public static int CCreate = 0;
        public static int SCreate = 0;
        public static int CMsgCount = 0;
        public static int SMsgCount = 0;
        public static int ANotifyCount = 0;
        public static int FormNotifyCount = 0;
        public static int CloseDestroyed = 0;

        // WndProc delegates -- ROOTED in static fields (native callback lifetime).
        public static WndProc FormProc;
        public static WndProc AProc;
        public static WndProc BProc;
        public static WndProc CProc;
        public static WndProc SProc;

        // The FORM: counts WM_CREATE, receives PanelA's bubbled notification,
        // destroys itself on WM_CLOSE.
        public static IntPtr FormWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { FormCreate = FormCreate + 1; return IntPtr.Zero; }
            if (msg == WF_FORM_NOTE)
            {
                FormNotifyCount = FormNotifyCount + 1;
                FormSeq = FormSeq + "form:note;";
                return IntPtr.Zero;
            }
            if (msg == WM_CLOSE)
            {
                CloseDestroyed = (DestroyWindow(hwnd) != 0) ? 1 : 0;
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // PanelA (child of the form, parent of B and S). Receives B's
        // bubbled WF_BUBBLE2 and S's bubbles, then forwards WF_FORM_NOTE to
        // the form.
        public static IntPtr AWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { ACreate = ACreate + 1; return IntPtr.Zero; }
            if (msg == WF_BUBBLE2 || msg == WF_CHILD_CLICK)
            {
                ANotifyCount = ANotifyCount + 1;
                ASeq = ASeq + "a:up;";
                PostMessageW(GetParent(hwnd), WF_FORM_NOTE, IntPtr.Zero, IntPtr.Zero);
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // PanelB (child of A, parent of C). Receives C's bubbled WF_BUBBLE1
        // and WF_CHILD_CLICK, then forwards WF_BUBBLE2 to A.
        public static IntPtr BWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { BCreate = BCreate + 1; return IntPtr.Zero; }
            if (msg == WF_BUBBLE1 || msg == WF_CHILD_CLICK)
            {
                BSeq = BSeq + "b:up;";
                PostMessageW(GetParent(hwnd), WF_BUBBLE2, IntPtr.Zero, IntPtr.Zero);
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // PanelC (the deepest panel). Handles WF_DEEP_MSG (its own routed
        // message) and WM_COMMAND (its button "clicked"); both bubble up one
        // hop to B as WF_BUBBLE1 / WF_CHILD_CLICK.
        public static IntPtr CWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { CCreate = CCreate + 1; return IntPtr.Zero; }
            if (msg == WF_DEEP_MSG)
            {
                CMsgCount = CMsgCount + 1;
                CSeq = CSeq + "c:msg;";
                PostMessageW(GetParent(hwnd), WF_BUBBLE1, IntPtr.Zero, IntPtr.Zero);
                return IntPtr.Zero;
            }
            if (msg == WM_COMMAND)
            {
                int id = (int)wParam.ToInt64() & 0xFFFF;
                if (id == ID_BTN_C)
                {
                    CSeq = CSeq + "c:btn;";
                    PostMessageW(GetParent(hwnd), WF_CHILD_CLICK, IntPtr.Zero, IntPtr.Zero);
                }
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // PanelS -- a SIBLING of B under A. Its own routed message appends
        // ONLY to S's sequence; it does NOT forward (isolation probe).
        public static IntPtr SWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { SCreate = SCreate + 1; return IntPtr.Zero; }
            if (msg == WF_SIB_MSG)
            {
                SMsgCount = SMsgCount + 1;
                SSeq = SSeq + "s:msg;";
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        public static void Main()
        {
            IntPtr hMod = GetModuleHandleW(null);
            Console.WriteLine("module_ok=" + (hMod != IntPtr.Zero));

            // Register five classes (form + four panels), each with its own proc.
            WNDCLASSEX wcForm = new WNDCLASSEX();
            wcForm.cbSize = 80;
            wcForm.style = CS_HREDRAW | CS_VREDRAW;
            FormProc = FormWndProcImpl;
            wcForm.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(FormProc);
            wcForm.hInstance = hMod;
            IntPtr cForm = Marshal.StringToHGlobalUni("WpFormClass");
            wcForm.lpszClassName = cForm;
            ushort aForm = RegisterClassExW(ref wcForm);
            Marshal.FreeHGlobal(cForm);
            Console.WriteLine("form_class=" + (aForm != 0));

            WNDCLASSEX wcA = new WNDCLASSEX();
            wcA.cbSize = 80;
            wcA.style = CS_HREDRAW | CS_VREDRAW;
            AProc = AWndProcImpl;
            wcA.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(AProc);
            wcA.hInstance = hMod;
            IntPtr cA = Marshal.StringToHGlobalUni("WpPanelAClass");
            wcA.lpszClassName = cA;
            ushort aA = RegisterClassExW(ref wcA);
            Marshal.FreeHGlobal(cA);
            Console.WriteLine("a_class=" + (aA != 0));

            WNDCLASSEX wcB = new WNDCLASSEX();
            wcB.cbSize = 80;
            wcB.style = CS_HREDRAW | CS_VREDRAW;
            BProc = BWndProcImpl;
            wcB.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(BProc);
            wcB.hInstance = hMod;
            IntPtr cB = Marshal.StringToHGlobalUni("WpPanelBClass");
            wcB.lpszClassName = cB;
            ushort aB = RegisterClassExW(ref wcB);
            Marshal.FreeHGlobal(cB);
            Console.WriteLine("b_class=" + (aB != 0));

            WNDCLASSEX wcC = new WNDCLASSEX();
            wcC.cbSize = 80;
            wcC.style = CS_HREDRAW | CS_VREDRAW;
            CProc = CWndProcImpl;
            wcC.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(CProc);
            wcC.hInstance = hMod;
            IntPtr cC = Marshal.StringToHGlobalUni("WpPanelCClass");
            wcC.lpszClassName = cC;
            ushort aC = RegisterClassExW(ref wcC);
            Marshal.FreeHGlobal(cC);
            Console.WriteLine("c_class=" + (aC != 0));

            WNDCLASSEX wcS = new WNDCLASSEX();
            wcS.cbSize = 80;
            wcS.style = CS_HREDRAW | CS_VREDRAW;
            SProc = SWndProcImpl;
            wcS.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(SProc);
            wcS.hInstance = hMod;
            IntPtr cS = Marshal.StringToHGlobalUni("WpPanelSClass");
            wcS.lpszClassName = cS;
            ushort aS = RegisterClassExW(ref wcS);
            Marshal.FreeHGlobal(cS);
            Console.WriteLine("s_class=" + (aS != 0));

            // Build the nested hierarchy (form -> A -> B -> C -> button).
            IntPtr hForm = CreateWindowExW(0, "WpFormClass", "WpForm",
                WS_OVERLAPPEDWINDOW | WS_VISIBLE, 10, 10, 500, 400,
                IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
            Console.WriteLine("form_created=" + (hForm != IntPtr.Zero));

            IntPtr hA = CreateWindowExW(0, "WpPanelAClass", "PanelA",
                WS_CHILD | WS_VISIBLE, 0, 0, 300, 200, hForm, IntPtr.Zero, hMod, IntPtr.Zero);
            Console.WriteLine("a_created=" + (hA != IntPtr.Zero));

            IntPtr hB = CreateWindowExW(0, "WpPanelBClass", "PanelB",
                WS_CHILD | WS_VISIBLE, 0, 0, 180, 120, hA, IntPtr.Zero, hMod, IntPtr.Zero);
            Console.WriteLine("b_created=" + (hB != IntPtr.Zero));

            IntPtr hC = CreateWindowExW(0, "WpPanelCClass", "PanelC",
                WS_CHILD | WS_VISIBLE, 0, 0, 140, 90, hB, IntPtr.Zero, hMod, IntPtr.Zero);
            Console.WriteLine("c_created=" + (hC != IntPtr.Zero));

            IntPtr hBtn = CreateWindowExW(0, "BUTTON", "BtnC",
                WS_CHILD | WS_VISIBLE, 0, 0, 90, 28, hC, new IntPtr(ID_BTN_C), hMod, IntPtr.Zero);
            Console.WriteLine("btn_created=" + (hBtn != IntPtr.Zero));

            IntPtr hS = CreateWindowExW(0, "WpPanelSClass", "PanelS",
                WS_CHILD | WS_VISIBLE, 0, 0, 80, 60, hA, IntPtr.Zero, hMod, IntPtr.Zero);
            Console.WriteLine("s_created=" + (hS != IntPtr.Zero));

            IntPtr hLbl = CreateWindowExW(0, "STATIC", "Lbl",
                WS_CHILD | WS_VISIBLE, 0, 0, 200, 25, hA, new IntPtr(202), hMod, IntPtr.Zero);
            Console.WriteLine("lbl_created=" + (hLbl != IntPtr.Zero));

            // Layout: each control inside its container (client coords).
            MoveWindow(hA, 20, 30, 300, 200, true);
            MoveWindow(hB, 10, 15, 180, 120, true);
            MoveWindow(hC, 8, 8, 140, 90, true);
            MoveWindow(hBtn, 4, 4, 90, 28, true);
            MoveWindow(hS, 200, 10, 80, 60, true);
            MoveWindow(hLbl, 5, 160, 200, 25, true);

            // ===== 1. Parent chain (the FULL 5-deep container chain) =====
            Console.WriteLine("gp_btn_is_c=" + (GetParent(hBtn) == hC));
            Console.WriteLine("gp_c_is_b=" + (GetParent(hC) == hB));
            Console.WriteLine("gp_b_is_a=" + (GetParent(hB) == hA));
            Console.WriteLine("gp_a_is_form=" + (GetParent(hA) == hForm));
            Console.WriteLine("gp_s_is_a=" + (GetParent(hS) == hA));
            Console.WriteLine("gp_lbl_is_a=" + (GetParent(hLbl) == hA));
            Console.WriteLine("gp_form_null=" + (GetParent(hForm) == IntPtr.Zero));
            Console.WriteLine("gp_c_not_form=" + (GetParent(hC) != hForm));
            Console.WriteLine("gp_s_is_b=" + (GetParent(hS) == hB));
            Console.WriteLine("gp_btn_not_b=" + (GetParent(hBtn) != hB));

            // ===== 2. Sizes (GetWindowRect) at every nesting depth =====
            RECT rA = new RECT();
            GetWindowRect(hA, out rA);
            Console.WriteLine("a_w=" + (rA.right - rA.left));
            Console.WriteLine("a_h=" + (rA.bottom - rA.top));
            RECT rB = new RECT();
            GetWindowRect(hB, out rB);
            Console.WriteLine("b_w=" + (rB.right - rB.left));
            Console.WriteLine("b_h=" + (rB.bottom - rB.top));
            RECT rC = new RECT();
            GetWindowRect(hC, out rC);
            Console.WriteLine("c_w=" + (rC.right - rC.left));
            Console.WriteLine("c_h=" + (rC.bottom - rC.top));
            RECT rBtn = new RECT();
            GetWindowRect(hBtn, out rBtn);
            Console.WriteLine("btn_w=" + (rBtn.right - rBtn.left));
            Console.WriteLine("btn_h=" + (rBtn.bottom - rBtn.top));
            RECT rS = new RECT();
            GetWindowRect(hS, out rS);
            Console.WriteLine("s_w=" + (rS.right - rS.left));
            Console.WriteLine("s_h=" + (rS.bottom - rS.top));

            // ===== 3. Client-origin deltas along the chain =====
            // Child client origin - parent client origin = MoveWindow pos.
            POINT pA = new POINT(); pA.x = 0; pA.y = 0;
            ClientToScreen(hA, ref pA);
            POINT pB = new POINT(); pB.x = 0; pB.y = 0;
            ClientToScreen(hB, ref pB);
            Console.WriteLine("cs_b_dx=" + (pB.x - pA.x));
            Console.WriteLine("cs_b_dy=" + (pB.y - pA.y));
            POINT pC = new POINT(); pC.x = 0; pC.y = 0;
            ClientToScreen(hC, ref pC);
            Console.WriteLine("cs_c_dx=" + (pC.x - pB.x));
            Console.WriteLine("cs_c_dy=" + (pC.y - pB.y));
            POINT pBtn = new POINT(); pBtn.x = 0; pBtn.y = 0;
            ClientToScreen(hBtn, ref pBtn);
            Console.WriteLine("cs_btn_dx=" + (pBtn.x - pC.x));
            Console.WriteLine("cs_btn_dy=" + (pBtn.y - pC.y));
            POINT pS = new POINT(); pS.x = 0; pS.y = 0;
            ClientToScreen(hS, ref pS);
            Console.WriteLine("cs_s_dx=" + (pS.x - pA.x));
            Console.WriteLine("cs_s_dy=" + (pS.y - pA.y));

            // ===== 4. Multi-hop event bubbling =====
            // 4a. WF_DEEP_MSG routed to the DEEPEST panel C: handled by C,
            //     bubbles C->B->A->form (3 hops).
            PostMessageW(hC, WF_DEEP_MSG, IntPtr.Zero, IntPtr.Zero);
            // 4b. The deepest button "clicked" (WM_COMMAND to C): bubbles
            //     C->B->A->form.
            PostMessageW(hC, WM_COMMAND, new IntPtr(ID_BTN_C), IntPtr.Zero);
            // 4c. SIBLING isolation: WF_SIB_MSG routed to PanelS -- handled by
            //     S only; A (the shared parent) must NOT see a notification.
            PostMessageW(hS, WF_SIB_MSG, IntPtr.Zero, IntPtr.Zero);

            MSG m;
            int guard = 0;
            while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref m);
                DispatchMessageW(ref m);
                guard++;
                if (guard >= 300) break;
            }

            Console.WriteLine("c_msg_count=" + CMsgCount);
            Console.WriteLine("s_msg_count=" + SMsgCount);
            Console.WriteLine("a_notify_count=" + ANotifyCount);
            Console.WriteLine("form_notify_count=" + FormNotifyCount);
            Console.WriteLine("c_seq=" + CSeq);
            Console.WriteLine("b_seq=" + BSeq);
            Console.WriteLine("a_seq=" + ASeq);
            Console.WriteLine("form_seq=" + FormSeq);
            Console.WriteLine("s_seq=" + SSeq);
            Console.WriteLine("form_create=" + FormCreate);
            Console.WriteLine("a_create=" + ACreate);
            Console.WriteLine("b_create=" + BCreate);
            Console.WriteLine("c_create=" + CCreate);
            Console.WriteLine("s_create=" + SCreate);

            // ===== 5. Visibility probe on the deepest branch =====
            Console.WriteLine("c_visible=" + (IsWindowVisible(hC) != 0));
            ShowWindow(hC, SW_HIDE);
            Console.WriteLine("c_hidden=" + (IsWindowVisible(hC) == 0));
            ShowWindow(hC, SW_SHOW);
            Console.WriteLine("c_reshown=" + (IsWindowVisible(hC) != 0));
            Console.WriteLine("form_visible=" + (IsWindowVisible(hForm) != 0));

            // Close the form (its proc destroys the window); children die with it.
            PostMessageW(hForm, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            guard = 0;
            while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref m);
                DispatchMessageW(ref m);
                guard++;
                if (guard >= 300) break;
            }
            Console.WriteLine("close_destroyed=" + (CloseDestroyed != 0));
        }
    }
}
