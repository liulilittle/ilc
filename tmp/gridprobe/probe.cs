using System;
using System.Runtime.InteropServices;

public static class GridProbe
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
    [DllImport("user32", EntryPoint = "GetParent")]
    public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEX
    {
        public int cbSize; public int style; public IntPtr lpfnWndProc; public int cbClsExtra;
        public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor;
        public IntPtr hbrBackground; public IntPtr lpszMenuName; public IntPtr lpszClassName; public IntPtr hIconSm;
    }

    // LVCOLUMNW mirror (mingw commctrl.h, NTDDI >= 0x06000000 tail).
    [StructLayout(LayoutKind.Sequential)]
    public struct LVCOLUMNW
    {
        public uint mask;
        public int fmt;
        public int cx;
        public IntPtr pszText;
        public int cchTextMax;
        public int iSubItem;
        public int iImage;
        public int iOrder;
        public int cxMin;
        public int cxDefault;
        public int cxIdeal;
    }

    // LVITEMW mirror (Phase-21 layout: mask/iItem/iSubItem @0..8).
    [StructLayout(LayoutKind.Sequential)]
    public struct LVITEMW
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public int state;
        public int stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public int lParam;
        public int iIndent;
        public int iGroupId;
        public int cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int iGroup;
    }

    public const int WS_CHILD = 0x40000000;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;

    public const string WC_LISTVIEW = "SysListView32";
    public const int ICC_LISTVIEW_CLASSES = 0x00000001;
    public const int ICC_TAB_CLASSES = 0x00000008;
    public const int ICC_BAR_CLASSES = 0x00000004;
    public const int ICC_PROGRESS_CLASS = 0x00000020;

    public const int LVS_REPORT = 0x0001;
    public const int LVS_EX_GRIDLINES = 0x0001;
    public const int LVS_EX_FULLROWSELECT = 0x0020;
    public const int LVM_FIRST = 0x1000;
    public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    public const int LVM_GETCOLUMNW = LVM_FIRST + 95;    // 0x105F
    public const int LVM_SETCOLUMNW = LVM_FIRST + 96;    // 0x1060
    public const int LVM_INSERTCOLUMNW = LVM_FIRST + 97; // 0x1061
    public const int LVM_GETCOLUMNWIDTH = LVM_FIRST + 29; // 0x101D
    public const int LVM_SETCOLUMNWIDTH = LVM_FIRST + 30; // 0x101E
    public const int LVM_GETHEADER = LVM_FIRST + 31;      // 0x101F
    public const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54; // 0x1036
    public const int LVM_GETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 55; // 0x1037
    public const int LVM_INSERTITEMW = LVM_FIRST + 77;
    public const int LVM_GETITEMTEXTW = LVM_FIRST + 115;
    public const int LVM_SETITEMTEXTW = LVM_FIRST + 116;
    public const int LVCF_FMT = 0x1;
    public const int LVCF_WIDTH = 0x2;
    public const int LVCF_TEXT = 0x4;
    public const int LVCF_SUBITEM = 0x8;
    public const int LVCFMT_LEFT = 0;
    public const int LVCFMT_RIGHT = 1;
    public const int LVCFMT_CENTER = 2;
    public const int LVIF_TEXT = 0x0001;
    public const int LVIF_PARAM = 0x0004;
    public const int HDM_FIRST = 0x1200;
    public const int HDM_GETITEMCOUNT = HDM_FIRST + 0;
    public const int LVSCW_AUTOSIZE = -1;
    public const int LVSCW_AUTOSIZE_USEHEADER = -2;

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    public static WndProcDel ParentProcDel;

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    public static IntPtr ParentProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public static void Main()
    {
        // Probe 0: LVCOLUMNW offsetof discipline.
        Console.WriteLine("sizeof_lvcolumn=" + Marshal.SizeOf(typeof(LVCOLUMNW)));
        Console.WriteLine("off_mask=" + Marshal.OffsetOf(typeof(LVCOLUMNW), "mask").ToInt64());
        Console.WriteLine("off_fmt=" + Marshal.OffsetOf(typeof(LVCOLUMNW), "fmt").ToInt64());
        Console.WriteLine("off_cx=" + Marshal.OffsetOf(typeof(LVCOLUMNW), "cx").ToInt64());
        Console.WriteLine("off_pszText=" + Marshal.OffsetOf(typeof(LVCOLUMNW), "pszText").ToInt64());
        Console.WriteLine("off_cchTextMax=" + Marshal.OffsetOf(typeof(LVCOLUMNW), "cchTextMax").ToInt64());
        Console.WriteLine("off_iSubItem=" + Marshal.OffsetOf(typeof(LVCOLUMNW), "iSubItem").ToInt64());
        Console.WriteLine("off_iImage=" + Marshal.OffsetOf(typeof(LVCOLUMNW), "iImage").ToInt64());
        Console.WriteLine("off_iOrder=" + Marshal.OffsetOf(typeof(LVCOLUMNW), "iOrder").ToInt64());

        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_LISTVIEW_CLASSES | ICC_TAB_CLASSES | ICC_BAR_CLASSES | ICC_PROGRESS_CLASS;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("icc=" + (iccRet != 0));

        IntPtr hMod = GetModuleHandleW(null);

        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = CS_HREDRAW | CS_VREDRAW;
        ParentProcDel = ParentProc;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ParentProcDel);
        wc.hInstance = hMod;
        IntPtr clsName = Marshal.StringToHGlobalUni("GridProbeParent");
        wc.lpszClassName = clsName;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("parent_class=" + (atom != 0));
        Marshal.FreeHGlobal(clsName);

        IntPtr hParent = CreateWindowExW(0, "GridProbeParent", "parent", 0, 0, 0, 320, 200, IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("parent=" + (hParent != IntPtr.Zero));

        IntPtr hList = CreateWindowExW(0, WC_LISTVIEW, "",
            WS_CHILD | LVS_REPORT, 0, 0, 320, 160, hParent, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("created=" + (hList != IntPtr.Zero));
        Console.WriteLine("parent_is_form=" + (GetParent(hList) == hParent));

        // Probe 1: extended style set + readback.
        IntPtr extPrev = SendMessageW(hList, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
        SendMessageW(hList, LVM_SETEXTENDEDLISTVIEWSTYLE,
            new IntPtr(LVS_EX_GRIDLINES | LVS_EX_FULLROWSELECT), new IntPtr(LVS_EX_GRIDLINES | LVS_EX_FULLROWSELECT));
        IntPtr extNow = SendMessageW(hList, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("ext_prev=" + extPrev.ToInt64());
        Console.WriteLine("ext_now=" + extNow.ToInt64());

        // Probe 2: insert 3 columns with LVCOLUMNW. LVM_INSERTCOLUMNW takes the
        // column INDEX in wParam (the explicit-index discipline -- same rule as
        // Phase-21 LVM_INSERTITEMW).
        IntPtr p0 = Marshal.StringToHGlobalUni("Name");
        IntPtr p1 = Marshal.StringToHGlobalUni("Value");
        IntPtr p2 = Marshal.StringToHGlobalUni("Kind");
        int[] col0 = BuildColumnPtr(LVCF_TEXT | LVCF_WIDTH | LVCF_FMT | LVCF_SUBITEM,
            LVCFMT_LEFT, 90, 0, p0);
        IntPtr ins0 = SendColumn(hList, 0, col0);
        int[] col1 = BuildColumnPtr(LVCF_TEXT | LVCF_WIDTH | LVCF_FMT | LVCF_SUBITEM,
            LVCFMT_CENTER, 70, 1, p1);
        IntPtr ins1 = SendColumn(hList, 1, col1);
        int[] col2 = BuildColumnPtr(LVCF_TEXT | LVCF_WIDTH | LVCF_FMT | LVCF_SUBITEM,
            LVCFMT_RIGHT, 70, 2, p2);
        IntPtr ins2 = SendColumn(hList, 2, col2);
        Marshal.FreeHGlobal(p0);
        Marshal.FreeHGlobal(p1);
        Marshal.FreeHGlobal(p2);
        Console.WriteLine("ins0=" + ins0.ToInt64() + " ins1=" + ins1.ToInt64() + " ins2=" + ins2.ToInt64());

        // Column width scalar readback.
        IntPtr w0 = SendMessageW(hList, LVM_GETCOLUMNWIDTH, IntPtr.Zero, IntPtr.Zero);
        IntPtr w1 = SendMessageW(hList, LVM_GETCOLUMNWIDTH, new IntPtr(1), IntPtr.Zero);
        IntPtr w2 = SendMessageW(hList, LVM_GETCOLUMNWIDTH, new IntPtr(2), IntPtr.Zero);
        Console.WriteLine("width0=" + w0.ToInt64() + " width1=" + w1.ToInt64() + " width2=" + w2.ToInt64());

        // Probe 3: column struct readback via LVM_GETCOLUMNW.
        IntPtr cbuf = Marshal.AllocHGlobal(256);
        int[] gc0 = new int[13];
        gc0[0] = LVCF_TEXT | LVCF_WIDTH | LVCF_FMT | LVCF_SUBITEM;
        gc0[6] = 128;
        long cbp = cbuf.ToInt64();
        gc0[4] = (int)(cbp & 0xFFFFFFFFL);
        gc0[5] = (int)((cbp >> 32) & 0xFFFFFFFFL);
        GCHandle g0 = GCHandle.Alloc(gc0, GCHandleType.Pinned);
        IntPtr gr = SendMessageW(hList, LVM_GETCOLUMNW, new IntPtr(0), g0.AddrOfPinnedObject());
        g0.Free();
        Console.WriteLine("getcol_ret=" + gr.ToInt64() + " fmt=" + gc0[1] + " cx=" + gc0[2] + " sub=" + gc0[7]);
        Console.WriteLine("getcol_text0=" + Marshal.PtrToStringUni(cbuf));
        Marshal.FreeHGlobal(cbuf);

        IntPtr cbuf1 = Marshal.AllocHGlobal(256);
        int[] gc1 = new int[13];
        gc1[0] = LVCF_TEXT | LVCF_WIDTH | LVCF_FMT | LVCF_SUBITEM;
        gc1[6] = 128;
        long cbp1 = cbuf1.ToInt64();
        gc1[4] = (int)(cbp1 & 0xFFFFFFFFL);
        gc1[5] = (int)((cbp1 >> 32) & 0xFFFFFFFFL);
        GCHandle g1 = GCHandle.Alloc(gc1, GCHandleType.Pinned);
        SendMessageW(hList, LVM_GETCOLUMNW, new IntPtr(1), g1.AddrOfPinnedObject());
        g1.Free();
        Console.WriteLine("getcol1_fmt=" + gc1[1] + " cx=" + gc1[2] + " text=" + Marshal.PtrToStringUni(cbuf1));
        Marshal.FreeHGlobal(cbuf1);

        IntPtr cbuf2 = Marshal.AllocHGlobal(256);
        int[] gc2 = new int[13];
        gc2[0] = LVCF_TEXT | LVCF_WIDTH | LVCF_FMT | LVCF_SUBITEM;
        gc2[6] = 128;
        long cbp2 = cbuf2.ToInt64();
        gc2[4] = (int)(cbp2 & 0xFFFFFFFFL);
        gc2[5] = (int)((cbp2 >> 32) & 0xFFFFFFFFL);
        GCHandle g2 = GCHandle.Alloc(gc2, GCHandleType.Pinned);
        SendMessageW(hList, LVM_GETCOLUMNW, new IntPtr(2), g2.AddrOfPinnedObject());
        g2.Free();
        Console.WriteLine("getcol2_fmt=" + gc2[1] + " cx=" + gc2[2] + " text=" + Marshal.PtrToStringUni(cbuf2));
        Marshal.FreeHGlobal(cbuf2);

        // Probe 4: column header count via LVM_GETHEADER -> HDM_GETITEMCOUNT.
        IntPtr hHead = SendMessageW(hList, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("header_nonzero=" + (hHead != IntPtr.Zero));
        IntPtr hCnt = SendMessageW(hHead, HDM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("headercount=" + hCnt.ToInt64());

        // Probe 5: set column width scalar.
        SendMessageW(hList, LVM_SETCOLUMNWIDTH, new IntPtr(0), new IntPtr(120));
        IntPtr w0b = SendMessageW(hList, LVM_GETCOLUMNWIDTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("width0_after=" + w0b.ToInt64());

        // Probe 6: insert 3 rows (explicit index, Phase-21 shape) + subitem cells.
        int r0 = InsertItem(hList, 0, "Alpha");
        int r1 = InsertItem(hList, 1, "Beta");
        int r2 = InsertItem(hList, 2, "Gamma");
        Console.WriteLine("row0=" + r0 + " row1=" + r1 + " row2=" + r2);
        IntPtr cnt = SendMessageW(hList, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("count=" + cnt.ToInt64());

        // Cell (1,1) = "Yes", (1,2) = "No".
        bool c11 = SetCellText(hList, 1, 1, "Yes");
        bool c12 = SetCellText(hList, 1, 2, "No");
        Console.WriteLine("cell11_set=" + c11 + " cell12_set=" + c12);
        Console.WriteLine("cell11=" + GetCellText(hList, 1, 1));
        Console.WriteLine("cell12=" + GetCellText(hList, 1, 2));
        Console.WriteLine("cell00=" + GetCellText(hList, 0, 0));

        DestroyWindow(hList);
        DestroyWindow(hParent);
        Console.WriteLine("done");
    }

    public static IntPtr SendColumn(IntPtr hList, int index, int[] c)
    {
        GCHandle pin = GCHandle.Alloc(c, GCHandleType.Pinned);
        IntPtr r = SendMessageW(hList, LVM_INSERTCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        return r;
    }

    public static int[] BuildColumnPtr(int mask, int fmt, int cx, int sub, IntPtr p)
    {
        int[] c = new int[13];
        c[0] = mask;
        c[1] = fmt;
        c[2] = cx;
        if (p != IntPtr.Zero)
        {
            long pt = p.ToInt64();
            c[4] = (int)(pt & 0xFFFFFFFFL);
            c[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
            c[6] = 128;
            c[7] = sub;
        }
        return c;
    }

    public static int[] BuildColumn(int mask, int fmt, int cx, int sub, string text)
    {
        int[] c = new int[13];
        c[0] = mask;
        c[1] = fmt;
        c[2] = cx;
        if (text != null && text.Length > 0)
        {
            IntPtr p = Marshal.StringToHGlobalUni(text);
            long pt = p.ToInt64();
            c[4] = (int)(pt & 0xFFFFFFFFL);
            c[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
            c[6] = 128;
            c[7] = sub;
        }
        return c;
    }

    public static int InsertItem(IntPtr hList, int iItem, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = LVIF_TEXT | LVIF_PARAM;
        lvi[1] = iItem;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        lvi[10] = 42; lvi[11] = 0;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = SendMessageW(hList, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public static bool SetCellText(IntPtr hList, int row, int col, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = LVIF_TEXT;
        lvi[1] = row;
        lvi[2] = col;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = SendMessageW(hList, LVM_SETITEMTEXTW, new IntPtr(row), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return r.ToInt64() != 0;
    }

    public static string GetCellText(IntPtr hList, int row, int col)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] lvi = new int[22];
        lvi[0] = LVIF_TEXT;
        lvi[1] = row;
        lvi[2] = col;
        long bp = buf.ToInt64();
        lvi[6] = (int)(bp & 0xFFFFFFFFL);
        lvi[7] = (int)((bp >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        SendMessageW(hList, LVM_GETITEMTEXTW, new IntPtr(row), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }
}
