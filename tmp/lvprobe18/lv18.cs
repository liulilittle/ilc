using System;
using System.Runtime.InteropServices;
namespace Cases { public static class LvProbe18 {
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("comctl32.dll")] static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryW(string n);
    [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr h, string n);
    [StructLayout(LayoutKind.Sequential)] struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }
    public const int ICC_LISTVIEW_CLASSES = 1;
    public const int ICC_WIN95_CLASSES = 255;
    public const int LVM_FIRST = 0x1000;
    public const int LVM_INSERTITEMW = LVM_FIRST + 77;
    public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    public const int LVIF_TEXT = 1;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_OVERLAPPED = 0;
    public static void Main() {
        IntPtr cc = LoadLibraryW("comctl32.dll");
        Console.WriteLine("comctl32_loaded=" + (cc != IntPtr.Zero));
        IntPtr init = GetProcAddress(cc, "InitCommonControlsEx");
        Console.WriteLine("init_addr=" + (init != IntPtr.Zero));
        // Force-init with ALL flags via the raw function
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX(); icc.dwSize = 8; icc.dwICC = unchecked((int)0xFFFFFFFF);
        int r1 = InitCommonControlsEx(ref icc);
        Console.WriteLine("icc_all=" + r1);
        IntPtr f = CreateWindowExW(0, "STATIC", "P", WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        IntPtr h = CreateWindowExW(0, "SysListView32", "", WS_CHILD, 0, 0, 200, 100, f, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        Console.WriteLine("created=" + (h != IntPtr.Zero));
        Console.WriteLine("count=" + SendMessageW(h, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        IntPtr tp = Marshal.StringToHGlobalUni("Alpha");
        int[] lvi = new int[22];
        long p = tp.ToInt64();
        lvi[0] = LVIF_TEXT; lvi[1] = -1; lvi[6] = (int)(p & 0xFFFFFFFFL); lvi[7] = (int)((p >> 32) & 0xFFFFFFFFL); lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Console.WriteLine("insert=" + r.ToInt64());
        Console.WriteLine("count2=" + SendMessageW(h, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
    }
}}
