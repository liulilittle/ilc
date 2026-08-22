using System;
using System.Runtime.InteropServices;

public static class P35Probe
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
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX
    {
        public int cbSize; public int style; public IntPtr lpfnWndProc; public int cbClsExtra;
        public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor;
        public IntPtr hbrBackground; public IntPtr lpszMenuName; public IntPtr lpszClassName; public IntPtr hIconSm;
    }

    public const int WS_CHILD = 0x40000000;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const string TAB_CLASS = "SysTabControl32";
    public const int ICC_TAB_CLASSES = 0x00000008;
    public const int TCM_FIRST = 0x1300;
    public const int TCM_GETITEMW = TCM_FIRST + 60;
    public const int TCM_SETITEMW = TCM_FIRST + 61;
    public const int TCM_GETITEMRECT = TCM_FIRST + 10;
    public const int TCM_GETROWCOUNT = TCM_FIRST + 44;
    public const int TCM_GETITEMCOUNT = TCM_FIRST + 4;
    public const int TCM_INSERTITEMW = TCM_FIRST + 62;
    public const int TCIF_TEXT = 0x0001;
    public const int TCIF_PARAM = 0x0008;
    public const int TCIF_RTLREADING = 0x0004;

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    public static WndProcDel ParentProcDel;

    public static IntPtr ParentProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public static string GetTabText(IntPtr hTab, int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] tci = new int[12];
        tci[0] = TCIF_TEXT;
        long bp = buf.ToInt64();
        tci[4] = (int)(bp & 0xFFFFFFFFL);
        tci[5] = (int)((bp >> 32) & 0xFFFFFFFFL);
        tci[6] = 128;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        SendMessageW(hTab, TCM_GETITEMW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s == null ? "<null>" : s;
    }

    public static int GetTabParam(IntPtr hTab, int index)
    {
        int[] tci = new int[12];
        tci[0] = TCIF_PARAM;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        SendMessageW(hTab, TCM_GETITEMW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        return tci[8];
    }

    public static int InsertTabText(IntPtr hTab, IntPtr wParam, string text, int pageIndex)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] tci = new int[12];
        tci[0] = TCIF_TEXT | TCIF_PARAM;
        long pt = p.ToInt64();
        tci[4] = (int)(pt & 0xFFFFFFFFL);
        tci[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        tci[6] = 128;
        tci[8] = pageIndex;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        IntPtr r = SendMessageW(hTab, TCM_INSERTITEMW, wParam, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public static bool SetTabText(IntPtr hTab, int index, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] tci = new int[12];
        tci[0] = TCIF_TEXT;
        long pt = p.ToInt64();
        tci[4] = (int)(pt & 0xFFFFFFFFL);
        tci[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        tci[6] = 128;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        IntPtr r = SendMessageW(hTab, TCM_SETITEMW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return r.ToInt64() != 0;
    }

    public static void Main()
    {
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_TAB_CLASSES;
        Console.WriteLine("icc=" + (InitCommonControlsEx(ref icc) != 0));

        IntPtr hMod = GetModuleHandleW(null);

        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = CS_HREDRAW | CS_VREDRAW;
        ParentProcDel = ParentProc;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ParentProcDel);
        wc.hInstance = hMod;
        IntPtr clsName = Marshal.StringToHGlobalUni("P35ProbeParent");
        wc.lpszClassName = clsName;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("parent_class=" + (atom != 0));
        Marshal.FreeHGlobal(clsName);

        IntPtr hParent = CreateWindowExW(0, "P35ProbeParent", "parent", 0, 0, 0, 320, 200, IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("parent=" + (hParent != IntPtr.Zero));

        // --- Insert-order probe: front-insert (wParam=0) vs append (wParam=count).
        IntPtr hFront = CreateWindowExW(0, TAB_CLASS, "", WS_CHILD, 0, 0, 320, 120, hParent, IntPtr.Zero, hMod, IntPtr.Zero);
        int r1 = InsertTabText(hFront, IntPtr.Zero, "TabA", 0);
        int r2 = InsertTabText(hFront, IntPtr.Zero, "TabB", 1);
        int r3 = InsertTabText(hFront, IntPtr.Zero, "TabC", 2);
        Console.WriteLine("front_insert_ret=" + r1 + "/" + r2 + "/" + r3);
        Console.WriteLine("front_count=" + SendMessageW(hFront, TCM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("front_text0=" + GetTabText(hFront, 0));
        Console.WriteLine("front_text1=" + GetTabText(hFront, 1));
        Console.WriteLine("front_text2=" + GetTabText(hFront, 2));
        Console.WriteLine("front_param0=" + GetTabParam(hFront, 0));
        Console.WriteLine("front_param1=" + GetTabParam(hFront, 1));
        Console.WriteLine("front_param2=" + GetTabParam(hFront, 2));
        Console.WriteLine("front_rows=" + SendMessageW(hFront, TCM_GETROWCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());

        IntPtr hAppend = CreateWindowExW(0, TAB_CLASS, "", WS_CHILD, 0, 0, 320, 120, hParent, IntPtr.Zero, hMod, IntPtr.Zero);
        int a1 = InsertTabText(hAppend, new IntPtr(0), "TabA", 0);
        int a2 = InsertTabText(hAppend, new IntPtr(1), "TabB", 1);
        int a3 = InsertTabText(hAppend, new IntPtr(2), "TabC", 2);
        Console.WriteLine("append_insert_ret=" + a1 + "/" + a2 + "/" + a3);
        Console.WriteLine("append_count=" + SendMessageW(hAppend, TCM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("append_text0=" + GetTabText(hAppend, 0));
        Console.WriteLine("append_text1=" + GetTabText(hAppend, 1));
        Console.WriteLine("append_text2=" + GetTabText(hAppend, 2));
        Console.WriteLine("append_param0=" + GetTabParam(hAppend, 0));
        Console.WriteLine("append_param1=" + GetTabParam(hAppend, 1));
        Console.WriteLine("append_param2=" + GetTabParam(hAppend, 2));

        // --- SetTabText rename + re-read.
        Console.WriteLine("settext_ret=" + SetTabText(hAppend, 1, "Renamed"));
        Console.WriteLine("rename_text1=" + GetTabText(hAppend, 1));

        // --- TCM_GETITEMRECT raw values (pinned int[4]: L,T,R,B).
        int[] rect = new int[4];
        GCHandle rpin = GCHandle.Alloc(rect, GCHandleType.Pinned);
        IntPtr rr = SendMessageW(hAppend, TCM_GETITEMRECT, new IntPtr(0), rpin.AddrOfPinnedObject());
        rpin.Free();
        Console.WriteLine("itemrect_ret=" + rr.ToInt64());
        Console.WriteLine("itemrect0=" + rect[0] + "," + rect[1] + "," + rect[2] + "," + rect[3]);

        // --- TCM_GETROWCOUNT.
        Console.WriteLine("append_rows=" + SendMessageW(hAppend, TCM_GETROWCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());

        Console.WriteLine("destroy_front=" + (DestroyWindow(hFront) != 0));
        Console.WriteLine("destroy_append=" + (DestroyWindow(hAppend) != 0));
        Console.WriteLine("destroy_parent=" + (DestroyWindow(hParent) != 0));
    }
}
