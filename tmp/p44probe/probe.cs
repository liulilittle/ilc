using System;
using System.Runtime.InteropServices;

public static class P44Probe
{
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
        int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);
    [DllImport("user32", EntryPoint = "MoveWindow")]
    public static extern int MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32", EntryPoint = "GetWindowRect")]
    public static extern int GetWindowRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    public static WndProcDel procDel = new WndProcDel(WndProc);

    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WS_OVERLAPPED = 0x00000000;

    // The Phase-44 state fields, verbatim.
    public static bool P44Active;
    public static int[] P44Write = new int[10] { 0, 0, 0, 0, 0, 0, 200, 150, 640, 480 };
    public static int P44Track;

    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            if (!P44Active) return DefProc(hwnd, msg, wParam, lParam);
            P44Track++;
            Marshal.Copy(P44Write, 0, lParam, 10);
            return IntPtr.Zero;
        }
        return DefProc(hwnd, msg, wParam, lParam);
    }

    public static string Fields(int[] b)
    {
        return b[0] + ";" + b[1] + ";" + b[2] + ";" + b[3] + ";" + b[4] + ";" + b[5] + ";" +
               b[6] + ";" + b[7] + ";" + b[8] + ";" + b[9];
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procDel);
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P44ParentF");
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("p44_class=" + (atom != 0));

        // Create with P44Active=false: natural WM_GETMINMAXINFO (create/move)
        // arrives with lParam = the OS internal buffer; the guard routes it to
        // DefWindowProcW. The window must survive (no corruption).
        IntPtr hParent = CreateWindowExW(0, "P44ParentF", "P44PF",
            WS_OVERLAPPED, 0, 0, 400, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        Console.WriteLine("p44_created=" + (hParent != IntPtr.Zero));

        // Probe 1: Marshal.Copy(int[] src, 0, IntPtr, 10) -- 40 bytes.
        // Write into an AllocHGlobal buffer, then read back with a fresh
        // Marshal.Copy from-unmanaged into another int[10].
        IntPtr buf = Marshal.AllocHGlobal(40);
        Marshal.Copy(P44Write, 0, buf, 10);
        int[] back = new int[10];
        Marshal.Copy(buf, back, 0, 10);
        Marshal.FreeHGlobal(buf);
        Console.WriteLine("p44_mc10=" + Fields(back));

        // Probe 2: WM_GETMINMAXINFO loopback via SendMessageW with OUR pinned
        // buffer. P44Active=true -> the WndProc writes P44Write (only [6..9]
        // are self-set; [0..5] stay 0) and returns IntPtr.Zero. No
        // DefWindowProc, so the caller's buffer is untouched by the OS.
        int[] p44buf = new int[10];
        GCHandle pin = GCHandle.Alloc(p44buf, GCHandleType.Pinned);
        P44Active = true;
        IntPtr ret = SendMessageW(hParent, WM_GETMINMAXINFO, IntPtr.Zero, pin.AddrOfPinnedObject());
        P44Active = false;
        Console.WriteLine("p44_ret=" + (ret == IntPtr.Zero));
        Console.WriteLine("p44_loop=" + Fields(p44buf));
        pin.Free();
        Console.WriteLine("p44_track=" + P44Track);

        // Guard: with P44Active=false, the same loopback routes to
        // DefWindowProcW, which fills resolution-dependent fields -- these are
        // NEVER asserted in the test (only self-set fields are).
        int[] p44buf2 = new int[10];
        GCHandle pin2 = GCHandle.Alloc(p44buf2, GCHandleType.Pinned);
        SendMessageW(hParent, WM_GETMINMAXINFO, IntPtr.Zero, pin2.AddrOfPinnedObject());
        pin2.Free();
        Console.WriteLine("p44_guard0=" + p44buf2[0] + ";" + p44buf2[1] + ";" + p44buf2[6] + ";" + p44buf2[8]);
        Console.WriteLine("p44_track_after_guard=" + P44Track);

        // Probe 3: relative layout reflow (Phase-5 delta discipline). The
        // parent is resized via MoveWindow with the guard OFF -- any natural
        // WM_GETMINMAXINFO the OS sends during the resize goes to
        // DefWindowProcW (the lParam is the OS internal buffer). GetWindowRect
        // reads the new size back; positions are RELATIVE DELTAS so the output
        // is deterministic (screen origin differs by window station).
        RECT rF0 = new RECT();
        GetWindowRect(hParent, out rF0);
        int mvF = MoveWindow(hParent, rF0.left, rF0.top, 400, 280, true);
        RECT rF1 = new RECT();
        GetWindowRect(hParent, out rF1);
        Console.WriteLine("p44_mv_ok=" + (mvF != 0));
        Console.WriteLine("p44_form_dx=" + (rF1.left - rF0.left));
        Console.WriteLine("p44_form_dy=" + (rF1.top - rF0.top));
        Console.WriteLine("p44_form_size=" + (rF1.right - rF1.left) + "x" + (rF1.bottom - rF1.top));
        Console.WriteLine("p44_track_after_move=" + P44Track);

        Console.WriteLine("p44_destroyed=" + (DestroyWindow(hParent) != 0));
    }
}
