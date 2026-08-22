using System;
using System.Runtime.InteropServices;
namespace Cases { public static class RadioProbe3 {
    [DllImport("user32.dll")] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr DefWindowProcW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll")] static extern int PeekMessageW(out MSG m, IntPtr h, int lo, int hi, int r);
    [DllImport("user32.dll")] static extern int TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct WNDCLASS { public int style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string lpszMenuName; public string lpszClassName; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam; public IntPtr lParam; public int time; public int ptX; public int ptY; }
    public delegate IntPtr WndProc(IntPtr h, int m, IntPtr w, IntPtr l);
    public const int BS_AUTORADIOBUTTON = 9;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_VISIBLE = unchecked((int)0x10000000);
    public const int WS_GROUP = 0x00020000;
    public const int BM_CLICK = 0xF5;
    public const int BM_SETCHECK = 0xF1;
    public const int BM_GETCHECK = 0xF0;
    public const int WM_COMMAND = 0x111;
    public const int PM_REMOVE = 1;
    public const int BN_CLICKED = 0;
    static WndProc proc = new WndProc(H);
    static int cmdCount = 0;
    static IntPtr H(IntPtr h, int m, IntPtr w, IntPtr l) {
        if (m == WM_COMMAND) { cmdCount++; Console.WriteLine("WM_COMMAND id=" + (w.ToInt64() & 0xFFFF) + " notif=" + ((w.ToInt64() >> 16) & 0xFFFF)); }
        return DefWindowProcW(h, m, w, l);
    }
    public static void Main() {
        WNDCLASS wc = new WNDCLASS(); wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc); wc.lpszClassName = "RP3"; wc.hInstance = GetModuleHandleW(null);
        RegisterClassW(ref wc);
        IntPtr f = CreateWindowExW(0, "RP3", "F", WS_VISIBLE, 0, 0, 200, 200, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        IntPtr r1 = CreateWindowExW(0, "BUTTON", "R1", WS_CHILD | WS_VISIBLE | WS_GROUP | BS_AUTORADIOBUTTON, 20, 20, 100, 20, f, new IntPtr(501), wc.hInstance, IntPtr.Zero);
        IntPtr r2 = CreateWindowExW(0, "BUTTON", "R2", WS_CHILD | WS_VISIBLE | BS_AUTORADIOBUTTON, 20, 44, 100, 20, f, new IntPtr(502), wc.hInstance, IntPtr.Zero);
        SendMessageW(r2, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        MSG m; while (PeekMessageW(out m, IntPtr.Zero, 0, 0, PM_REMOVE) != 0) { TranslateMessage(ref m); DispatchMessageW(ref m); }
        Console.WriteLine("cmdCount=" + cmdCount);
        Console.WriteLine("states=" + SendMessageW(r1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
        // managed-group pattern (WinForms RadioButton.Checked): set one checked via BM_SETCHECK, uncheck others in code
        SendMessageW(r1, BM_SETCHECK, new IntPtr(1), IntPtr.Zero);
        SendMessageW(r2, BM_SETCHECK, new IntPtr(0), IntPtr.Zero);
        Console.WriteLine("managedGroup=" + SendMessageW(r1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
    }
}}
