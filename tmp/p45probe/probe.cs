using System;
using System.Runtime.InteropServices;

public static class P45Probe
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
    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    [DllImport("user32", EntryPoint = "GetClientRect")]
    public static extern int GetClientRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX
    {
        public int dwSize;
        public int dwICC;
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

    public const string WC_TABCONTROL = "SysTabControl32";
    public const int TCM_FIRST = 0x1300;
    public const int TCM_GETITEMCOUNT = TCM_FIRST + 4;
    public const int TCM_GETITEMRECT = TCM_FIRST + 10;
    public const int TCM_SETITEMSIZE = TCM_FIRST + 41;
    public const int TCM_GETROWCOUNT = TCM_FIRST + 44;
    public const int TCM_INSERTITEMW = TCM_FIRST + 62;
    public const int TCIF_TEXT = 0x0001;
    public const int TCIF_PARAM = 0x0008;
    public const int ICC_TAB_CLASSES = 0x00000008;
    public const int TCS_FIXEDWIDTH = 0x0400;
    public const int TCS_MULTILINE = 0x0200;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_CHILD = 0x40000000;

    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        return DefProc(hwnd, msg, wParam, lParam);
    }

    public static void InsertTab(IntPtr hwnd, int index, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] tci = new int[12];
        tci[0] = TCIF_TEXT | TCIF_PARAM;
        long pt = p.ToInt64();
        tci[4] = (int)(pt & 0xFFFFFFFFL);
        tci[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        tci[6] = 128;
        tci[8] = index;
        GCHandle pin = GCHandle.Alloc(tci, GCHandleType.Pinned);
        SendMessageW(hwnd, TCM_INSERTITEMW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
    }

    public static int GetCount(IntPtr hwnd)
    {
        return (int)SendMessageW(hwnd, TCM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
    }

    public static int GetRows(IntPtr hwnd)
    {
        return (int)SendMessageW(hwnd, TCM_GETROWCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
    }

    public static long SetItemSize(IntPtr hwnd, int cx, int cy)
    {
        return SendMessageW(hwnd, TCM_SETITEMSIZE, IntPtr.Zero, new IntPtr((long)(cx | (cy << 16)))).ToInt64();
    }

    public static string RectBool(IntPtr hwnd, int idx)
    {
        int[] rc = new int[4];
        GCHandle pin = GCHandle.Alloc(rc, GCHandleType.Pinned);
        SendMessageW(hwnd, TCM_GETITEMRECT, new IntPtr(idx), pin.AddrOfPinnedObject());
        pin.Free();
        bool lr = rc[0] < rc[2];
        bool tb = rc[1] < rc[3];
        return "w=" + (rc[2] - rc[0]) + ";lr=" + lr + ";tb=" + tb + ";ne=" + (lr && tb) + ";pos=" + (rc[0] >= 0 && rc[1] >= 0);
    }

    public static IntPtr NewTab(IntPtr hParent, IntPtr hInst, int style, int w)
    {
        return CreateWindowExW(0, WC_TABCONTROL, "",
            WS_CHILD | style, 10, 10, w, 120, hParent, IntPtr.Zero, hInst, IntPtr.Zero);
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procDel);
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P45ParentF");
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("p45_class=" + (atom != 0));

        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_TAB_CLASSES;
        Console.WriteLine("p45_icc=" + (InitCommonControlsEx(ref icc) != 0));

        IntPtr hParent = CreateWindowExW(0, "P45ParentF", "P45PF",
            WS_OVERLAPPED, 0, 0, 400, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        Console.WriteLine("p45_parent=" + (hParent != IntPtr.Zero));

        // Probe 1+4: TCS_FIXEDWIDTH 320-wide, two-step SetItemSize readback + rows.
        IntPtr hTab1 = NewTab(hParent, hInst, TCS_FIXEDWIDTH, 320);
        Console.WriteLine("p45_tab1_created=" + (hTab1 != IntPtr.Zero));
        InsertTab(hTab1, 0, "Tab0");
        InsertTab(hTab1, 1, "Tab1");
        InsertTab(hTab1, 2, "Tab2");
        Console.WriteLine("p45_count=" + GetCount(hTab1));
        long sz1 = SetItemSize(hTab1, 60, 24);
        Console.WriteLine("p45_step1_old_w=" + (sz1 & 0xFFFF));
        Console.WriteLine("p45_step1_old_h=" + ((sz1 >> 16) & 0xFFFF));
        long sz2 = SetItemSize(hTab1, 80, 28);
        Console.WriteLine("p45_prev_w=" + (sz2 & 0xFFFF));
        Console.WriteLine("p45_prev_h=" + ((sz2 >> 16) & 0xFFFF));
        long sz3 = SetItemSize(hTab1, 50, 20);
        Console.WriteLine("p45_prev2_w=" + (sz3 & 0xFFFF));
        Console.WriteLine("p45_prev2_h=" + ((sz3 >> 16) & 0xFFFF));
        Console.WriteLine("p45_rect0=" + RectBool(hTab1, 0));
        Console.WriteLine("p45_rows1=" + GetRows(hTab1));

        // Probe 2: TCS_FIXEDWIDTH necessity -- a plain control (no FIXEDWIDTH).
        IntPtr hTabPlain = NewTab(hParent, hInst, 0, 320);
        Console.WriteLine("p45_plain_created=" + (hTabPlain != IntPtr.Zero));
        InsertTab(hTabPlain, 0, "Tab0");
        InsertTab(hTabPlain, 1, "Tab1");
        InsertTab(hTabPlain, 2, "Tab2");
        Console.WriteLine("p45_plain_rect_before=" + RectBool(hTabPlain, 0));
        long szp1 = SetItemSize(hTabPlain, 60, 24);
        Console.WriteLine("p45_plain_set1_old_w=" + (szp1 & 0xFFFF));
        Console.WriteLine("p45_plain_rect_after=" + RectBool(hTabPlain, 0));
        long szp2 = SetItemSize(hTabPlain, 80, 28);
        Console.WriteLine("p45_plain_prev_w=" + (szp2 & 0xFFFF));
        Console.WriteLine("p45_plain_rows=" + GetRows(hTabPlain));

        // Probe 3: multiline rows = control width / item width.
        // Narrow ~100px control, item size 50x20.
        IntPtr hTabML = NewTab(hParent, hInst, TCS_MULTILINE | TCS_FIXEDWIDTH, 100);
        Console.WriteLine("p45_ml_created=" + (hTabML != IntPtr.Zero));
        InsertTab(hTabML, 0, "Tab0");
        InsertTab(hTabML, 1, "Tab1");
        InsertTab(hTabML, 2, "Tab2");
        SetItemSize(hTabML, 50, 20);
        RECT crML = new RECT();
        GetClientRect(hTabML, out crML);
        Console.WriteLine("p45_ml_client_w=" + (crML.right - crML.left));
        Console.WriteLine("p45_ml_rows_100w_50i=" + GetRows(hTabML));

        // Wider 320 control, item size 50 -> 6 tabs (320/50=6), expect rows < tabs.
        IntPtr hTabML2 = NewTab(hParent, hInst, TCS_MULTILINE | TCS_FIXEDWIDTH, 320);
        Console.WriteLine("p45_ml2_created=" + (hTabML2 != IntPtr.Zero));
        InsertTab(hTabML2, 0, "Tab0");
        InsertTab(hTabML2, 1, "Tab1");
        InsertTab(hTabML2, 2, "Tab2");
        InsertTab(hTabML2, 3, "Tab3");
        InsertTab(hTabML2, 4, "Tab4");
        InsertTab(hTabML2, 5, "Tab5");
        SetItemSize(hTabML2, 50, 20);
        RECT crML2 = new RECT();
        GetClientRect(hTabML2, out crML2);
        Console.WriteLine("p45_ml2_client_w=" + (crML2.right - crML2.left));
        Console.WriteLine("p45_ml2_rows_320w_50i_6tabs=" + GetRows(hTabML2));

        // Row formula sweep: item size 50x20, 3 tabs, widths 90..200.
        int[] widths = new int[] { 90, 100, 110, 120, 140, 160, 180, 200 };
        foreach (int w in widths)
        {
            IntPtr hw = NewTab(hParent, hInst, TCS_MULTILINE | TCS_FIXEDWIDTH, w);
            InsertTab(hw, 0, "Tab0");
            InsertTab(hw, 1, "Tab1");
            InsertTab(hw, 2, "Tab2");
            SetItemSize(hw, 50, 20);
            RECT cr = new RECT();
            GetClientRect(hw, out cr);
            Console.WriteLine("p45_sweep_w=" + w + ";client=" + (cr.right - cr.left) + ";rows=" + GetRows(hw));
            DestroyWindow(hw);
        }

        // Threshold refinement: 104..112 (3->2 band) and 154..162 (2->1 band).
        int[] fine = new int[] { 104, 105, 106, 107, 108, 109, 154, 155, 156, 157, 158, 159 };
        foreach (int w in fine)
        {
            IntPtr hw = NewTab(hParent, hInst, TCS_MULTILINE | TCS_FIXEDWIDTH, w);
            InsertTab(hw, 0, "Tab0");
            InsertTab(hw, 1, "Tab1");
            InsertTab(hw, 2, "Tab2");
            SetItemSize(hw, 50, 20);
            Console.WriteLine("p45_fine_w=" + w + ";rows=" + GetRows(hw));
            DestroyWindow(hw);
        }

        Console.WriteLine("p45_tab1_destroyed=" + (DestroyWindow(hTab1) != 0));
        Console.WriteLine("p45_plain_destroyed=" + (DestroyWindow(hTabPlain) != 0));
        Console.WriteLine("p45_ml_destroyed=" + (DestroyWindow(hTabML) != 0));
        Console.WriteLine("p45_ml2_destroyed=" + (DestroyWindow(hTabML2) != 0));
        Console.WriteLine("p45_parent_destroyed=" + (DestroyWindow(hParent) != 0));
    }
}
