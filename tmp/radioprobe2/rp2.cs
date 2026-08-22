using System;
using System.Runtime.InteropServices;
namespace Cases { public static class RadioProbe2 {
    [DllImport("user32.dll")] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr DefWindowProcW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll")] static extern int ShowWindow(IntPtr h, int c);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [StructLayout(LayoutKind.Sequential)] struct WNDCLASS { public int style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string lpszMenuName; public string lpszClassName; }
    public delegate IntPtr WndProc(IntPtr h, int m, IntPtr w, IntPtr l);
    public const int BS_AUTORADIOBUTTON = 9;
    public const int BS_AUTOCHECKBOX = 3;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_VISIBLE = unchecked((int)0x10000000);
    public const int WS_GROUP = 0x00020000;
    public const int BM_CLICK = 0xF5;
    public const int BM_SETCHECK = 0xF1;
    public const int BM_GETCHECK = 0xF0;
    static WndProc proc = new WndProc(H);
    static IntPtr H(IntPtr h, int m, IntPtr w, IntPtr l) { return DefWindowProcW(h, m, w, l); }
    public static void Main() {
        WNDCLASS wc = new WNDCLASS(); wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc); wc.lpszClassName = "RP2"; wc.hInstance = GetModuleHandleW(null);
        RegisterClassW(ref wc);
        IntPtr f = CreateWindowExW(0, "RP2", "F", WS_VISIBLE, 0, 0, 200, 200, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        IntPtr r1 = CreateWindowExW(0, "BUTTON", "R1", WS_CHILD | WS_VISIBLE | WS_GROUP | BS_AUTORADIOBUTTON, 20, 20, 100, 20, f, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        IntPtr r2 = CreateWindowExW(0, "BUTTON", "R2", WS_CHILD | WS_VISIBLE | BS_AUTORADIOBUTTON, 20, 44, 100, 20, f, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        IntPtr r3 = CreateWindowExW(0, "BUTTON", "R3", WS_CHILD | WS_VISIBLE | BS_AUTORADIOBUTTON, 20, 68, 100, 20, f, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        IntPtr cb = CreateWindowExW(0, "BUTTON", "CB", WS_CHILD | WS_VISIBLE | BS_AUTOCHECKBOX, 20, 100, 100, 20, f, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        Console.WriteLine("before=" + SendMessageW(r1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r3, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(r2, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("afterClickR2=" + SendMessageW(r1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r3, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(r3, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("afterClickR3=" + SendMessageW(r1, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r2, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64() + "," + SendMessageW(r3, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(cb, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("cbAfterClick=" + SendMessageW(cb, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
        SendMessageW(cb, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("cbAfterClick2=" + SendMessageW(cb, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt64());
    }
}}
