// wmresize.cs -- window LAYOUT deep probe (e2e batch C21, "window layout deep probe
// -- MoveWindow/GetWindowRect/parent-child hierarchy, after stage 5"). winforms1 Phase 5
// established the baseline (MoveWindow + GetWindowRect on a button/child,
// deltas + sizes, GetParent chain). THIS case goes deeper on the layout
// surface that a WinForms LayoutEngine depends on:
//
//   * exact CLIENT-AREA positioning: MoveWindow places a child relative to
//     its PARENT'S CLIENT AREA; GetClientRect + ClientToScreen recover the
//     client origin in screen space, so `child.left - origin.x == cx` is an
//     exact, machine-independent probe (the screen origin differs per
//     window station, but the delta does not).
//   * bRepaint true/false both succeed and produce identical geometry.
//   * SIBLING ISOLATION: moving one child never moves another child of the
//     same parent.
//   * SetWindowPos with SWP_NOMOVE / SWP_NOSIZE: size-only and position-only
//     updates leave the untouched axis exactly as-is.
//   * SEQUENTIAL moves on one window (last-write-wins geometry, no drift
//     across repeated GetWindowRect calls).
//   * PARENT-MOVE PROPAGATION: moving the parent window shifts every
//     descendant by the parent's exact delta (the layout contract WinForms
//     relies on).
//   * NEGATIVE coordinates (off-window-station placement) -- the delta math
//     stays exact.
//   * THREE-LEVEL hierarchy (form -> child form -> grandchild control):
//     GetParent chain, and SET-PARENT REPARENTING (SetParent returns the old
//     parent; the window moves to the new parent's client space).
//   * IsWindow before/after DestroyWindow (destroying the parent destroys
//     its descendants).
//
// Determinism: only DELTAS / sizes / booleans are printed (absolute screen
// coordinates are machine-dependent); the net48 oracle and the ILC side run
// on the SAME window station so every observation is byte-identical.
// Headless-safe: all windows are created HIDDEN (no WS_VISIBLE) -- layout
// math does not require visibility.
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Wmresize
    {
        public const int WS_OVERLAPPED = 0x00000000;
        public const int WS_CHILD = 0x40000000;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;

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
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        public delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string name);

        [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

        [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance,
            IntPtr param);

        [DllImport("user32", EntryPoint = "MoveWindow")]
        public static extern int MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);

        [DllImport("user32", EntryPoint = "GetWindowRect")]
        public static extern int GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32", EntryPoint = "GetClientRect")]
        public static extern int GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32", EntryPoint = "ClientToScreen")]
        public static extern int ClientToScreen(IntPtr hwnd, ref POINT pt);

        [DllImport("user32", EntryPoint = "SetWindowPos")]
        public static extern int SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, int flags);

        [DllImport("user32", EntryPoint = "SetParent")]
        public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32", EntryPoint = "GetParent")]
        public static extern IntPtr GetParent(IntPtr hwnd);

        [DllImport("user32", EntryPoint = "IsWindow")]
        public static extern int IsWindow(IntPtr hwnd);

        [DllImport("user32", EntryPoint = "DefWindowProcW")]
        public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", EntryPoint = "DestroyWindow")]
        public static extern int DestroyWindow(IntPtr hwnd);

        public static WndProc RootWndProc;

        public static IntPtr WndProcImpl(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // Client origin of a window in SCREEN coordinates: (0,0) in client
        // space mapped to screen. For a borderless WS_CHILD window the client
        // origin equals the window's top-left; for a framed top-level window
        // it lies inside the frame. The origin is published to static fields
        // -- user-method out/ref params and user-struct return types are both
        // unsupported by the M-stage emitter (return type 17 LOUD).
        public static int g_ox;
        public static int g_oy;

        public static void ComputeOrigin(IntPtr hwnd)
        {
            RECT cr;
            GetClientRect(hwnd, out cr);
            POINT p;
            p.x = 0; p.y = 0;
            ClientToScreen(hwnd, ref p);
            g_ox = p.x;
            g_oy = p.y;
        }

        public static void Main()
        {
            IntPtr hModule = GetModuleHandleW(null);
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = 80;
            wc.style = 0;
            RootWndProc = WndProcImpl;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RootWndProc);
            wc.hInstance = hModule;
            IntPtr cls = Marshal.StringToHGlobalUni("WmrClass");
            wc.lpszClassName = cls;
            ushort atom = RegisterClassExW(ref wc);
            Console.WriteLine("class_registered=" + (atom != 0));
            Marshal.FreeHGlobal(cls);

            // ---- three-level hierarchy: form -> child form -> grandchild ----
            // All windows HIDDEN (no WS_VISIBLE); explicit coordinates.
            IntPtr hParent = CreateWindowExW(0, "WmrClass", "WmrParent",
                WS_OVERLAPPED, 80, 90, 400, 300, IntPtr.Zero, IntPtr.Zero, hModule, IntPtr.Zero);
            IntPtr hChild = CreateWindowExW(0, "WmrClass", "WmrChild",
                WS_CHILD, 10, 20, 100, 60, hParent, IntPtr.Zero, hModule, IntPtr.Zero);
            IntPtr hSibling = CreateWindowExW(0, "WmrClass", "WmrSibling",
                WS_CHILD, 210, 120, 90, 45, hParent, IntPtr.Zero, hModule, IntPtr.Zero);
            IntPtr hGrand = CreateWindowExW(0, "WmrClass", "WmrGrand",
                WS_CHILD, 4, 5, 40, 30, hChild, IntPtr.Zero, hModule, IntPtr.Zero);
            Console.WriteLine("parent_created=" + (hParent != IntPtr.Zero));
            Console.WriteLine("child_created=" + (hChild != IntPtr.Zero));
            Console.WriteLine("sibling_created=" + (hSibling != IntPtr.Zero));
            Console.WriteLine("grand_created=" + (hGrand != IntPtr.Zero));

            // ---- hierarchy chain (stage-5 GetParent surface, one level deeper) ----
            Console.WriteLine("grand_parent_is_child=" + (GetParent(hGrand) == hChild));
            Console.WriteLine("child_parent_is_form=" + (GetParent(hChild) == hParent));
            Console.WriteLine("sibling_parent_is_form=" + (GetParent(hSibling) == hParent));
            Console.WriteLine("form_parent_null=" + (GetParent(hParent) == IntPtr.Zero));

            // ---- 1. exact client-area positioning (MoveWindow + GetWindowRect) ----
            ComputeOrigin(hParent);
            int mv1 = MoveWindow(hChild, 30, 40, 120, 60, true);
            Console.WriteLine("mv_child_ok=" + (mv1 != 0));
            RECT r1;
            GetWindowRect(hChild, out r1);
            Console.WriteLine("child_size_exact=" + (r1.right - r1.left == 120 && r1.bottom - r1.top == 60));
            Console.WriteLine("child_pos_exact=" + (r1.left - g_ox == 30 && r1.top - g_oy == 40));

            // ---- 2. bRepaint=false produces identical geometry ----
            int mv2 = MoveWindow(hSibling, 7, 9, 77, 33, false);
            Console.WriteLine("mv_sibling_ok=" + (mv2 != 0));
            RECT r2;
            GetWindowRect(hSibling, out r2);
            Console.WriteLine("sibling_size_exact=" + (r2.right - r2.left == 77 && r2.bottom - r2.top == 33));
            Console.WriteLine("sibling_pos_exact=" + (r2.left - g_ox == 7 && r2.top - g_oy == 9));

            // ---- 3. sibling isolation: moving one child leaves the other alone ----
            RECT rBefore;
            GetWindowRect(hChild, out rBefore);
            MoveWindow(hSibling, 50, 60, 88, 44, true);
            RECT rAfter;
            GetWindowRect(hChild, out rAfter);
            Console.WriteLine("sibling_isolated=" + (rAfter.left == rBefore.left && rAfter.top == rBefore.top
                && rAfter.right == rBefore.right && rAfter.bottom == rBefore.bottom));

            // ---- 4. SetWindowPos SWP_NOMOVE: size-only update ----
            RECT rS0b;
            GetWindowRect(hSibling, out rS0b);
            int sp1 = SetWindowPos(hSibling, IntPtr.Zero, 0, 0, 150, 60, SWP_NOMOVE);
            Console.WriteLine("setpos_nomove_ok=" + (sp1 != 0));
            RECT rS1;
            GetWindowRect(hSibling, out rS1);
            Console.WriteLine("nomove_pos_kept=" + (rS1.left == rS0b.left && rS1.top == rS0b.top));
            Console.WriteLine("nomove_size_changed=" + (rS1.right - rS1.left == 150 && rS1.bottom - rS1.top == 60));

            // ---- 5. SetWindowPos SWP_NOSIZE: position-only update ----
            int sp2 = SetWindowPos(hChild, IntPtr.Zero, 55, 66, 0, 0, SWP_NOSIZE);
            Console.WriteLine("setpos_nosize_ok=" + (sp2 != 0));
            RECT rC1;
            GetWindowRect(hChild, out rC1);
            Console.WriteLine("nosize_size_kept=" + (rC1.right - rC1.left == 120 && rC1.bottom - rC1.top == 60));
            Console.WriteLine("nosize_pos_changed=" + (rC1.left - g_ox == 55 && rC1.top - g_oy == 66));

            // ---- 6. sequential moves: last-write-wins, no drift ----
            RECT rG0;
            GetWindowRect(hGrand, out rG0);
            MoveWindow(hGrand, 20, 30, 50, 40, true);
            MoveWindow(hGrand, 5, 6, 25, 20, true);
            MoveWindow(hGrand, 11, 12, 33, 22, true);
            RECT rG1;
            GetWindowRect(hGrand, out rG1);
            RECT rG2;
            GetWindowRect(hGrand, out rG2);
            Console.WriteLine("seq_move_final_size=" + (rG1.right - rG1.left == 33 && rG1.bottom - rG1.top == 22));
            ComputeOrigin(hChild);
            Console.WriteLine("seq_move_final_pos=" + (rG1.left - g_ox == 11 && rG1.top - g_oy == 12));
            Console.WriteLine("getrect_stable=" + (rG1.left == rG2.left && rG1.top == rG2.top
                && rG1.right == rG2.right && rG1.bottom == rG2.bottom));

            // ---- 7. parent-move propagation: children track the parent's delta ----
            RECT rP0;
            GetWindowRect(hParent, out rP0);
            RECT rC0;
            GetWindowRect(hChild, out rC0);
            RECT rG0b;
            GetWindowRect(hGrand, out rG0b);
            int mvP = MoveWindow(hParent, 150, 130, 400, 300, true);
            Console.WriteLine("mv_parent_ok=" + (mvP != 0));
            RECT rP1;
            GetWindowRect(hParent, out rP1);
            RECT rC1b;
            GetWindowRect(hChild, out rC1b);
            RECT rG1b;
            GetWindowRect(hGrand, out rG1b);
            int pdx = rP1.left - rP0.left;
            int pdy = rP1.top - rP0.top;
            Console.WriteLine("parent_dx=" + pdx);
            Console.WriteLine("parent_dy=" + pdy);
            Console.WriteLine("child_tracks_parent=" + (rC1b.left - rC0.left == pdx && rC1b.top - rC0.top == pdy));
            Console.WriteLine("grand_tracks_parent=" + (rG1b.left - rG0b.left == pdx && rG1b.top - rG0b.top == pdy));
            Console.WriteLine("parent_size_kept=" + (rP1.right - rP1.left == 400 && rP1.bottom - rP1.top == 300));

            // ---- 8. negative coordinates (off-origin placement) ----
            int mvN = MoveWindow(hGrand, -20, -10, 30, 20, true);
            Console.WriteLine("mv_negative_ok=" + (mvN != 0));
            ComputeOrigin(hChild);
            RECT rN;
            GetWindowRect(hGrand, out rN);
            Console.WriteLine("neg_pos_exact=" + (rN.left - g_ox == -20 && rN.top - g_oy == -10));
            Console.WriteLine("neg_size_exact=" + (rN.right - rN.left == 30 && rN.bottom - rN.top == 20));

            // ---- 9. SetParent reparenting (SetParent returns the OLD parent) ----
            IntPtr oldP1 = SetParent(hGrand, hSibling);
            Console.WriteLine("reparent_old_is_child=" + (oldP1 == hChild));
            Console.WriteLine("reparent_new_is_sibling=" + (GetParent(hGrand) == hSibling));
            IntPtr oldP2 = SetParent(hGrand, hChild);
            Console.WriteLine("reparent_back_old_is_sibling=" + (oldP2 == hSibling));
            Console.WriteLine("reparent_back_new_is_child=" + (GetParent(hGrand) == hChild));

            // ---- 10. IsWindow before/after DestroyWindow ----
            Console.WriteLine("iswindow_before=" + (IsWindow(hChild) != 0));
            Console.WriteLine("iswindow_grand_before=" + (IsWindow(hGrand) != 0));
            int d1 = DestroyWindow(hChild);   // destroys the child AND its descendant grandchild
            Console.WriteLine("destroy_child_ok=" + (d1 != 0));
            Console.WriteLine("iswindow_after=" + (IsWindow(hChild) == 0));
            Console.WriteLine("iswindow_grand_after=" + (IsWindow(hGrand) == 0));
            Console.WriteLine("iswindow_sibling_still=" + (IsWindow(hSibling) != 0));
            int d2 = DestroyWindow(hSibling);
            Console.WriteLine("destroy_sibling_ok=" + (d2 != 0));
            int d3 = DestroyWindow(hParent);
            Console.WriteLine("destroy_parent_ok=" + (d3 != 0));
        }
    }
}
