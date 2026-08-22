using System;
using System.Runtime.InteropServices;

public static class SbProbe
{
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
        int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }

    public const int WS_CHILD = 0x40000000;
    public const int ICC_BAR_CLASSES = 0x00000004;

    public const int SB_SETPARTS = 0x0404;   // WM_USER+4
    public const int SB_GETPARTS = 0x0406;   // WM_USER+6
    public const int SB_SETTEXT = 0x040B;    // WM_USER+11 (W variant)
    public const int SB_GETTEXTLENGTH = 0x040C; // WM_USER+12
    public const int SB_GETTEXT = 0x040D;    // WM_USER+13

    public static void Main()
    {
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_BAR_CLASSES;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("icc=" + (iccRet != 0));

        IntPtr hMod = GetModuleHandleW(null);
        IntPtr hParent = CreateWindowExW(0, "STATIC", "parent", 0, 0, 0, 320, 20, IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("parent=" + (hParent != IntPtr.Zero));
        IntPtr hStatus = CreateWindowExW(0, "msctls_statusbar32", "",
            WS_CHILD, 0, 0, 320, 20, hParent, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("created=" + (hStatus != IntPtr.Zero));

        // Probe 1: SB_GETPARTS on a fresh status bar (no parts set).
        int[] gp0 = new int[3];
        GCHandle g0 = GCHandle.Alloc(gp0, GCHandleType.Pinned);
        IntPtr ret0 = SendMessageW(hStatus, SB_GETPARTS, new IntPtr(3), g0.AddrOfPinnedObject());
        Console.WriteLine("getparts_fresh_ret=" + ret0.ToInt64());
        Console.WriteLine("getparts_fresh_arr=" + gp0[0] + "," + gp0[1] + "," + gp0[2]);
        g0.Free();

        // Probe 2: SB_SETPARTS with pinned int[] [150, 300, -1].
        int[] right = new int[3] { 150, 300, -1 };
        GCHandle gr = GCHandle.Alloc(right, GCHandleType.Pinned);
        IntPtr setRet = SendMessageW(hStatus, SB_SETPARTS, new IntPtr(3), gr.AddrOfPinnedObject());
        Console.WriteLine("setparts_ret=" + setRet.ToInt64());
        gr.Free();

        // Probe 3: SB_GETPARTS read-back after SETPARTS.
        int[] gp1 = new int[3];
        GCHandle g1 = GCHandle.Alloc(gp1, GCHandleType.Pinned);
        IntPtr ret1 = SendMessageW(hStatus, SB_GETPARTS, new IntPtr(3), g1.AddrOfPinnedObject());
        Console.WriteLine("getparts_ret=" + ret1.ToInt64());
        Console.WriteLine("getparts_arr=" + gp1[0] + "," + gp1[1] + "," + gp1[2]);
        g1.Free();

        // Probe 4: SB_GETPARTS with a too-small wParam (2 parts requested).
        int[] gp2 = new int[2];
        GCHandle g2 = GCHandle.Alloc(gp2, GCHandleType.Pinned);
        IntPtr ret2 = SendMessageW(hStatus, SB_GETPARTS, new IntPtr(2), g2.AddrOfPinnedObject());
        Console.WriteLine("getparts2_ret=" + ret2.ToInt64());
        Console.WriteLine("getparts2_arr=" + gp2[0] + "," + gp2[1]);
        g2.Free();

        // Probe 5: per-part text round-trips (wParam = part index, W channel).
        for (int part = 0; part < 3; part++)
        {
            IntPtr t = Marshal.StringToHGlobalUni("Status" + (char)('A' + part));
            IntPtr sRet = SendMessageW(hStatus, SB_SETTEXT, new IntPtr(part), t);
            Marshal.FreeHGlobal(t);
            Console.WriteLine("settext" + part + "_ret=" + sRet.ToInt64());
            IntPtr len = SendMessageW(hStatus, SB_GETTEXTLENGTH, new IntPtr(part), IntPtr.Zero);
            Console.WriteLine("len" + part + "=" + (len.ToInt64() & 0xFFFFL));
            IntPtr buf = Marshal.AllocHGlobal(256);
            IntPtr gRet = SendMessageW(hStatus, SB_GETTEXT, new IntPtr(part), buf);
            Console.WriteLine("gettext" + part + "_ret=" + gRet.ToInt64());
            Console.WriteLine("text" + part + "=" + Marshal.PtrToStringUni(buf));
            Marshal.FreeHGlobal(buf);
        }

        // Probe 6: fresh-status-bar text length (part 0 only, default).
        IntPtr lenFresh = SendMessageW(hStatus, SB_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("len_fresh=" + (lenFresh.ToInt64() & 0xFFFFL));

        DestroyWindow(hStatus);
        DestroyWindow(hParent);
        Console.WriteLine("done");
    }
}
