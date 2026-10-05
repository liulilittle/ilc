//------------------------------------------------------------------------------
// winpanel.cs -- deep probe of the WinForms Panel layout surface (the
// "after-stage-15" container complement). A Panel is a CONTAINER child
// window that holds child controls; nested panels nest containers.
//
// Hierarchy under test (all raw user32 P/Invoke, same source on net48 and
// the transpiled runtime):
//   Form (top-level, WpFormClass)
//     +-- OuterPanel (WS_CHILD, WpPanelClass)      MoveWindow(20,30,300,200)
//         +-- InnerPanel (WS_CHILD, WpPanelClass)  MoveWindow(10,10,150,80)
//         |     +-- InnerButton (BUTTON)           MoveWindow(5,5,100,30)
//         +-- OuterButton (BUTTON)                 MoveWindow(5,5,120,40)
//         +-- Label (STATIC)                       MoveWindow(5,50,200,30)
//
// Layout: every panel/control is a CHILD of its container, so GetParent
// exposes the FULL container chain (panel->form, innerPanel->panel,
// button->its panel). GetWindowRect reports exact child sizes;
// GetClientRect reports the client area; ClientToScreen(ScreenToClient)
// converts a container's client origin to screen space -- the DELTAS
// between a child panel's origin and its parent's origin equal its
// MoveWindow position (child windows have no border), so the output is
// byte-exact regardless of the screen origin.
//
// Nested events: each panel has its OWN WndProc delegate. A "button click"
// inside a panel is simulated by posting WM_COMMAND to the PANEL (LOWORD =
// the control id); the panel's proc raises its managed Click (appends to
// the sequence) and FORWARDS a notification message to its PARENT panel
// (event bubbling); the outer panel bubbles to the form. A posted
// WF_INNER_MSG routed to the INNER panel's own proc proves each panel
// handles its own messages. All output is a deterministic sequence string.
//
// Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Winpanel
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
        public const int WM_DESTROY = 0x0002;
        public const int WF_INNER_MSG = 0x0401;
        public const int WF_BUBBLE_MSG = 0x0402;
        public const int WF_CHILD_CLICK = 0x0403;
        public const int WF_OUTER_BTN = 0x0404;
        public const int ID_BTN_OUTER = 101;
        public const int ID_BTN_INNER = 102;
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
        [DllImport("user32", EntryPoint = "GetClientRect")]
        public static extern int GetClientRect(IntPtr hwnd, out RECT rc);
        [DllImport("user32", EntryPoint = "ClientToScreen")]
        public static extern int ClientToScreen(IntPtr hwnd, ref POINT pt);
        [DllImport("user32", EntryPoint = "ScreenToClient")]
        public static extern int ScreenToClient(IntPtr hwnd, ref POINT pt);

        // --- observable state ---
        public static string InnerSeq = "";
        public static string OuterSeq = "";
        public static string FormSeq = "";
        public static int InnerMsgCount = 0;
        public static int OuterCmdCount = 0;
        public static int InnerCmdCount = 0;
        public static int FormNotifyCount = 0;
        public static int FormCreateCount = 0;
        public static int OuterCreateCount = 0;
        public static int InnerCreateCount = 0;
        public static int CloseDestroyed = 0;

        // WndProc delegates -- ROOTED in static fields (native callback lifetime).
        public static WndProc FormProc;
        public static WndProc OuterProc;
        public static WndProc InnerProc;
        // The FORM's window proc: top-level frame. It counts its WM_CREATE,
        // receives the outer panel's bubbled button notification
        // (WF_OUTER_BTN), and destroys itself on WM_CLOSE.
        public static IntPtr FormWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { FormCreateCount = FormCreateCount + 1; return IntPtr.Zero; }
            if (msg == WF_OUTER_BTN)
            {
                FormNotifyCount = FormNotifyCount + 1;
                FormSeq = FormSeq + "form:outerbtn;";
                return IntPtr.Zero;
            }
            if (msg == WM_CLOSE)
            {
                CloseDestroyed = (DestroyWindow(hwnd) != 0) ? 1 : 0;
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // The OUTER panel's window proc (a WS_CHILD of the form). Handles:
        //   * WF_BUBBLE_MSG  -- the inner panel's forwarded message
        //   * WF_CHILD_CLICK -- the inner panel's bubbled button click
        //   * WM_COMMAND     -- the outer panel's OWN button (ID_BTN_OUTER)
        //     was "clicked"; raises its click and bubbles WF_OUTER_BTN to the
        //     form.
        public static IntPtr OuterWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { OuterCreateCount = OuterCreateCount + 1; return IntPtr.Zero; }
            if (msg == WF_BUBBLE_MSG) { OuterSeq = OuterSeq + "outer:bubble;"; return IntPtr.Zero; }
            if (msg == WF_CHILD_CLICK) { OuterSeq = OuterSeq + "outer:childclick;"; return IntPtr.Zero; }
            if (msg == WM_COMMAND)
            {
                int id = (int)wParam.ToInt64() & 0xFFFF;
                if (id == ID_BTN_OUTER)
                {
                    OuterCmdCount = OuterCmdCount + 1;
                    OuterSeq = OuterSeq + "outer:btn;";
                    PostMessageW(GetParent(hwnd), WF_OUTER_BTN, IntPtr.Zero, IntPtr.Zero);
                }
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // The INNER panel's window proc (a WS_CHILD of the outer panel):
        //   * WF_INNER_MSG  -- a posted message routed to ITS OWN proc
        //   * WM_COMMAND    -- the inner panel's own button (ID_BTN_INNER)
        //     was "clicked"; raises its click and bubbles WF_CHILD_CLICK to
        //     the outer panel (the container).
        public static IntPtr InnerWndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_CREATE) { InnerCreateCount = InnerCreateCount + 1; return IntPtr.Zero; }
            if (msg == WF_INNER_MSG)
            {
                InnerMsgCount = InnerMsgCount + 1;
                InnerSeq = InnerSeq + "inner:msg;";
                PostMessageW(GetParent(hwnd), WF_BUBBLE_MSG, IntPtr.Zero, IntPtr.Zero);
                return IntPtr.Zero;
            }
            if (msg == WM_COMMAND)
            {
                int id = (int)wParam.ToInt64() & 0xFFFF;
                if (id == ID_BTN_INNER)
                {
                    InnerCmdCount = InnerCmdCount + 1;
                    InnerSeq = InnerSeq + "inner:btn;";
                    PostMessageW(GetParent(hwnd), WF_CHILD_CLICK, IntPtr.Zero, IntPtr.Zero);
                }
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        public static void Main()
        {
            IntPtr hMod = GetModuleHandleW(null);
            Console.WriteLine("module_ok=" + (hMod != IntPtr.Zero));

            // Register the three window classes (form + outer panel + inner
            // panel) -- each with its OWN WndProc delegate.
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

            WNDCLASSEX wcOuter = new WNDCLASSEX();
            wcOuter.cbSize = 80;
            wcOuter.style = CS_HREDRAW | CS_VREDRAW;
            OuterProc = OuterWndProcImpl;
            wcOuter.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(OuterProc);
            wcOuter.hInstance = hMod;
            IntPtr cOuter = Marshal.StringToHGlobalUni("WpPanelClass");
            wcOuter.lpszClassName = cOuter;
            ushort aOuter = RegisterClassExW(ref wcOuter);
            Marshal.FreeHGlobal(cOuter);
            Console.WriteLine("panel_class=" + (aOuter != 0));

            WNDCLASSEX wcInner = new WNDCLASSEX();
            wcInner.cbSize = 80;
            wcInner.style = CS_HREDRAW | CS_VREDRAW;
            InnerProc = InnerWndProcImpl;
            wcInner.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(InnerProc);
            wcInner.hInstance = hMod;
            IntPtr cInner = Marshal.StringToHGlobalUni("WpInnerPanelClass");
            wcInner.lpszClassName = cInner;
            ushort aInner = RegisterClassExW(ref wcInner);
            Marshal.FreeHGlobal(cInner);
            Console.WriteLine("inner_class=" + (aInner != 0));

            // Build the nested hierarchy.
            IntPtr hForm = CreateWindowExW(0, "WpFormClass", "WpForm",
                WS_OVERLAPPEDWINDOW | WS_VISIBLE, 10, 10, 500, 400,
                IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
            Console.WriteLine("form_created=" + (hForm != IntPtr.Zero));

            IntPtr hOuter = CreateWindowExW(0, "WpPanelClass", "OuterPanel",
                WS_CHILD | WS_VISIBLE, 0, 0, 300, 200, hForm, IntPtr.Zero, hMod, IntPtr.Zero);
            Console.WriteLine("outer_created=" + (hOuter != IntPtr.Zero));

            IntPtr hInner = CreateWindowExW(0, "WpInnerPanelClass", "InnerPanel",
                WS_CHILD | WS_VISIBLE, 0, 0, 150, 80, hOuter, IntPtr.Zero, hMod, IntPtr.Zero);
            Console.WriteLine("inner_created=" + (hInner != IntPtr.Zero));

            IntPtr hBtnOuter = CreateWindowExW(0, "BUTTON", "B1",
                WS_CHILD | WS_VISIBLE, 0, 0, 120, 40, hOuter, new IntPtr(ID_BTN_OUTER), hMod, IntPtr.Zero);
            Console.WriteLine("btn_created=" + (hBtnOuter != IntPtr.Zero));

            IntPtr hLbl = CreateWindowExW(0, "STATIC", "Lbl",
                WS_CHILD | WS_VISIBLE, 0, 0, 200, 30, hOuter, new IntPtr(103), hMod, IntPtr.Zero);
            Console.WriteLine("lbl_created=" + (hLbl != IntPtr.Zero));

            IntPtr hBtnInner = CreateWindowExW(0, "BUTTON", "B2",
                WS_CHILD | WS_VISIBLE, 0, 0, 100, 30, hInner, new IntPtr(ID_BTN_INNER), hMod, IntPtr.Zero);
            Console.WriteLine("btn_inner_created=" + (hBtnInner != IntPtr.Zero));

            // Layout: position each control INSIDE its container (client coords).
            MoveWindow(hOuter, 20, 30, 300, 200, true);
            MoveWindow(hInner, 10, 10, 150, 80, true);
            MoveWindow(hBtnOuter, 5, 5, 120, 40, true);
            MoveWindow(hLbl, 5, 50, 200, 30, true);
            MoveWindow(hBtnInner, 5, 5, 100, 30, true);

            // ===== 1. Parent chain (the container hierarchy) =====
            Console.WriteLine("gp_outer_is_form=" + (GetParent(hOuter) == hForm));
            Console.WriteLine("gp_inner_is_outer=" + (GetParent(hInner) == hOuter));
            Console.WriteLine("gp_btn_is_outer=" + (GetParent(hBtnOuter) == hOuter));
            Console.WriteLine("gp_lbl_is_outer=" + (GetParent(hLbl) == hOuter));
            Console.WriteLine("gp_btninner_is_inner=" + (GetParent(hBtnInner) == hInner));
            Console.WriteLine("gp_form_null=" + (GetParent(hForm) == IntPtr.Zero));
            Console.WriteLine("gp_inner_is_not_form=" + (GetParent(hInner) != hForm));
            Console.WriteLine("gp_btninner_is_not_outer=" + (GetParent(hBtnInner) != hOuter));

            // ===== 2. Sizes: GetWindowRect (exact child sizes) =====
            RECT rOuter = new RECT();
            GetWindowRect(hOuter, out rOuter);
            int ow = rOuter.right - rOuter.left;
            int oh = rOuter.bottom - rOuter.top;
            Console.WriteLine("outer_w=" + ow);
            Console.WriteLine("outer_h=" + oh);

            RECT rInner = new RECT();
            GetWindowRect(hInner, out rInner);
            int iw = rInner.right - rInner.left;
            int ih = rInner.bottom - rInner.top;
            Console.WriteLine("inner_w=" + iw);
            Console.WriteLine("inner_h=" + ih);

            RECT rBtn = new RECT();
            GetWindowRect(hBtnOuter, out rBtn);
            int bw = rBtn.right - rBtn.left;
            int bh = rBtn.bottom - rBtn.top;
            Console.WriteLine("btn_w=" + bw);
            Console.WriteLine("btn_h=" + bh);

            RECT rBIn = new RECT();
            GetWindowRect(hBtnInner, out rBIn);
            int binw = rBIn.right - rBIn.left;
            int binh = rBIn.bottom - rBIn.top;
            Console.WriteLine("btninner_w=" + binw);
            Console.WriteLine("btninner_h=" + binh);

            // ===== 3. Client area sizes =====
            RECT cf = new RECT();
            GetClientRect(hForm, out cf);
            Console.WriteLine("client_form_w=" + (cf.right - cf.left));
            Console.WriteLine("client_form_h=" + (cf.bottom - cf.top));
            RECT co = new RECT();
            GetClientRect(hOuter, out co);
            Console.WriteLine("client_outer_w=" + (co.right - co.left));
            Console.WriteLine("client_outer_h=" + (co.bottom - co.top));

            // ===== 4. Client-origin deltas via ClientToScreen =====
            // A child's client origin in screen space MINUS its parent's
            // client origin = its MoveWindow position (child windows have no
            // border) -- deterministic across window stations.
            POINT pForm = new POINT(); pForm.x = 0; pForm.y = 0;
            ClientToScreen(hForm, ref pForm);
            POINT pOuter = new POINT(); pOuter.x = 0; pOuter.y = 0;
            ClientToScreen(hOuter, ref pOuter);
            Console.WriteLine("cs_outer_dx=" + (pOuter.x - pForm.x));
            Console.WriteLine("cs_outer_dy=" + (pOuter.y - pForm.y));

            POINT pInner = new POINT(); pInner.x = 0; pInner.y = 0;
            ClientToScreen(hInner, ref pInner);
            Console.WriteLine("cs_inner_dx=" + (pInner.x - pOuter.x));
            Console.WriteLine("cs_inner_dy=" + (pInner.y - pOuter.y));

            POINT pBtn = new POINT(); pBtn.x = 0; pBtn.y = 0;
            ClientToScreen(hBtnOuter, ref pBtn);
            Console.WriteLine("cs_btn_dx=" + (pBtn.x - pOuter.x));
            Console.WriteLine("cs_btn_dy=" + (pBtn.y - pOuter.y));

            // ScreenToClient round-trip: the button's screen origin, converted
            // back into the OUTER panel's client space -> its MoveWindow pos.
            POINT sc = new POINT(); sc.x = pBtn.x; sc.y = pBtn.y;
            ScreenToClient(hOuter, ref sc);
            int sx = sc.x;
            Console.WriteLine("s2c_btn_dx=" + sx);
            int sy = sc.y;
            Console.WriteLine("s2c_btn_dy=" + sy);

            // ===== 5. Nested events (posted, drained, sequence strings) =====
            // 5a. WF_INNER_MSG routed to the INNER panel's own proc; it
            //     bubbles WF_BUBBLE_MSG to the OUTER panel.
            PostMessageW(hInner, WF_INNER_MSG, IntPtr.Zero, IntPtr.Zero);
            // 5b. The inner panel's button "clicked" (WM_COMMAND to the inner
            //     panel); it bubbles WF_CHILD_CLICK to the outer panel.
            PostMessageW(hInner, WM_COMMAND, new IntPtr(ID_BTN_INNER), IntPtr.Zero);
            // 5c. The outer panel's button "clicked" (WM_COMMAND to the outer
            //     panel); it bubbles WF_OUTER_BTN to the FORM.
            PostMessageW(hOuter, WM_COMMAND, new IntPtr(ID_BTN_OUTER), IntPtr.Zero);

            MSG m;
            int guard = 0;
            while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref m);
                DispatchMessageW(ref m);
                guard++;
                if (guard >= 200) break;
            }

            Console.WriteLine("inner_msg_count=" + InnerMsgCount);
            Console.WriteLine("inner_cmd_count=" + InnerCmdCount);
            Console.WriteLine("outer_cmd_count=" + OuterCmdCount);
            Console.WriteLine("form_notify_count=" + FormNotifyCount);
            Console.WriteLine("inner_seq=" + InnerSeq);
            Console.WriteLine("outer_seq=" + OuterSeq);
            Console.WriteLine("form_seq=" + FormSeq);
            Console.WriteLine("form_create=" + FormCreateCount);
            Console.WriteLine("outer_create=" + OuterCreateCount);
            Console.WriteLine("inner_create=" + InnerCreateCount);

            // Close the form (its proc destroys the window); the child panels
            // die with it.
            PostMessageW(hForm, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            guard = 0;
            while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0)
            {
                TranslateMessage(ref m);
                DispatchMessageW(ref m);
                guard++;
                if (guard >= 200) break;
            }
            Console.WriteLine("close_destroyed=" + (CloseDestroyed != 0));
        }
    }
}
