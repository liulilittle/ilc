using System;
using System.Runtime.InteropServices;

public static class P43Probe
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
    [DllImport("user32", EntryPoint = "ShowWindow")]
    public static extern int ShowWindow(IntPtr hwnd, int cmdShow);
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

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

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX
    {
        public int dwSize;
        public int dwICC;
    }

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    public static WndProcDel procDel = new WndProcDel(WndProc);

    public const string WC_LISTVIEW = "SysListView32";
    public const int WS_CHILD = 0x40000000;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int LVS_REPORT = 0x0001;
    public const int ICC_LISTVIEW_CLASSES = 0x00000001;
    public const int LVM_FIRST = 0x1000;
    public const int LVM_GETCOLUMNW = LVM_FIRST + 95;
    public const int LVM_INSERTCOLUMNW = LVM_FIRST + 97;
    public const int LVM_GETHEADER = LVM_FIRST + 31;
    public const int LVM_INSERTITEMW = LVM_FIRST + 77;
    public const int LVM_SETITEMTEXTW = LVM_FIRST + 116;
    public const int LVM_GETSUBITEMRECT = LVM_FIRST + 56;
    public const int LVM_SETCOLUMNORDERARRAY = LVM_FIRST + 58;   // 0x103A
    public const int LVM_GETCOLUMNORDERARRAY = LVM_FIRST + 59;   // 0x103B
    public const int HDM_FIRST = 0x1200;
    public const int HDM_GETITEM = HDM_FIRST + 1;               // 0x1201
    public const int HDM_GETORDERARRAY = HDM_FIRST + 17;        // 0x1211
    public const int HDI_ORDER = 0x0080;
    public const int LVCF_TEXT = 0x0004;
    public const int LVCF_WIDTH = 0x0002;
    public const int LVCF_FMT = 0x0001;
    public const int LVCF_SUBITEM = 0x0008;
    public const int LVIF_TEXT = 0x0001;
    public const int ID_LIST43 = 0x2A0;
    public const int SW_SHOWNOACTIVATE = 4;

    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        return DefProc(hwnd, msg, wParam, lParam);
    }

    public static int AddColumn(IntPtr h, string text, int cx)
    {
        int index = ColumnCount(h);
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] col = new int[13];
        col[0] = LVCF_TEXT | LVCF_WIDTH | LVCF_FMT | LVCF_SUBITEM;
        col[1] = 0;
        col[2] = cx;
        long pt = p.ToInt64();
        col[4] = (int)(pt & 0xFFFFFFFFL);
        col[5] = (int)((pt >> 32) & 0xFFFFFFFFL);
        col[6] = 128;
        col[7] = index;
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_INSERTCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public static int ColumnCount(IntPtr h)
    {
        IntPtr hHead = SendMessageW(h, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        IntPtr r = SendMessageW(hHead, HDM_FIRST + 0, IntPtr.Zero, IntPtr.Zero); // HDM_GETITEMCOUNT
        return (int)r.ToInt64();
    }

    public static int Insert(IntPtr h, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = LVIF_TEXT;
        lvi[1] = 0;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public static void SetCell(IntPtr h, int col, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] lvi = new int[22];
        lvi[0] = LVIF_TEXT;
        lvi[1] = 0;
        lvi[2] = col;
        long pt = p.ToInt64();
        lvi[6] = (int)(pt & 0xFFFFFFFFL);
        lvi[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        SendMessageW(h, LVM_SETITEMTEXTW, new IntPtr(0), pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
    }

    public static int[] GetOrderArr(IntPtr h, int count)
    {
        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = 99;
        GCHandle pin = GCHandle.Alloc(order, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_GETCOLUMNORDERARRAY, new IntPtr(count), pin.AddrOfPinnedObject());
        pin.Free();
        int[] outArr = new int[count + 1];
        outArr[0] = (r.ToInt64() != 0) ? 1 : 0;
        for (int i = 0; i < count; i++) outArr[i + 1] = order[i];
        return outArr;
    }

    public static int[] SetOrder(IntPtr h, int[] order)
    {
        GCHandle pin = GCHandle.Alloc(order, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_SETCOLUMNORDERARRAY, new IntPtr(order.Length), pin.AddrOfPinnedObject());
        pin.Free();
        return new int[] { (r.ToInt64() != 0) ? 1 : 0 };
    }

    public static string GetColText(IntPtr h, int index)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] col = new int[13];
        col[0] = LVCF_TEXT;
        col[6] = 128;
        long bp = buf.ToInt64();
        col[4] = (int)(bp & 0xFFFFFFFFL);
        col[5] = (int)((bp >> 32) & 0xFFFFFFFFL);
        GCHandle pin = GCHandle.Alloc(col, GCHandleType.Pinned);
        SendMessageW(h, LVM_GETCOLUMNW, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        if (s == null) { s = ""; }
        return s;
    }

    public static int[] GetSubItemRectLeft(IntPtr h, int subitem)
    {
        // LVM_GETSUBITEMRECT: lParam = RECT; the TOP field carries the subitem index.
        int[] rc = new int[4];
        rc[1] = subitem;
        GCHandle pin = GCHandle.Alloc(rc, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_GETSUBITEMRECT, new IntPtr(0), pin.AddrOfPinnedObject());
        pin.Free();
        return new int[] { (r.ToInt64() != 0) ? 1 : 0, rc[0], rc[1], rc[2], rc[3] };
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procDel);
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P43ParentF");
        RegisterClassExW(ref wc);
        IntPtr hParent = CreateWindowExW(0, "P43ParentF", "P43PF",
            WS_OVERLAPPED, 0, 0, 400, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = Marshal.SizeOf(typeof(INITCOMMONCONTROLSEX));
        icc.dwICC = ICC_LISTVIEW_CLASSES;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("p43_icc=" + (iccRet != 0));

        IntPtr hList = CreateWindowExW(0, WC_LISTVIEW, "",
            WS_CHILD | LVS_REPORT, 0, 0, 320, 120, hParent, new IntPtr(ID_LIST43), hInst, IntPtr.Zero);
        Console.WriteLine("p43_list_created=" + (hList != IntPtr.Zero));
        Console.WriteLine("p43_id=" + ID_LIST43);

        int c0 = AddColumn(hList, "Name", 90);
        int c1 = AddColumn(hList, "Value", 70);
        int c2 = AddColumn(hList, "Kind", 70);
        Console.WriteLine("p43_col0_add=" + c0);
        Console.WriteLine("p43_col1_add=" + c1);
        Console.WriteLine("p43_col2_add=" + c2);
        Console.WriteLine("p43_colcount=" + ColumnCount(hList));

        int[] f = GetOrderArr(hList, 3);
        Console.WriteLine("p43_fresh=" + f[0] + ";" + f[1] + ";" + f[2] + ";" + f[3]);

        // ===== item 2/3: SET returns BOOL + readback after reorder =====
        int[] sr1 = SetOrder(hList, new int[] { 2, 0, 1 });
        int[] g1 = GetOrderArr(hList, 3);
        Console.WriteLine("p43_set1=" + (sr1[0] != 0));
        Console.WriteLine("p43_after1=" + g1[0] + ";" + g1[1] + ";" + g1[2] + ";" + g1[3]);

        // ===== item 4: GETCOLUMNW(0) text still by index =====
        Console.WriteLine("p43_col0_text=" + GetColText(hList, 0));
        Console.WriteLine("p43_col1_text=" + GetColText(hList, 1));
        Console.WriteLine("p43_col2_text=" + GetColText(hList, 2));

        // ===== item 6: DIRECTION via physical subitem rect =====
        Insert(hList, "Alpha");
        SetCell(hList, 0, "Alpha");
        SetCell(hList, 1, "Beta");
        SetCell(hList, 2, "Gamma");

        // Show the windows so layout happens (headless but a real desktop session).
        ShowWindow(hParent, SW_SHOWNOACTIVATE);
        ShowWindow(hList, SW_SHOWNOACTIVATE);

        int[] sr0 = GetSubItemRectLeft(hList, 0);
        int[] sr1v = GetSubItemRectLeft(hList, 1);
        int[] sr2 = GetSubItemRectLeft(hList, 2);
        Console.WriteLine("p43_rect0_after_reorder=ok" + sr0[0] + ";l=" + sr0[1] + ";t=" + sr0[2] + ";r=" + sr0[3] + ";b=" + sr0[4]);
        Console.WriteLine("p43_rect1_after_reorder=ok" + sr1v[0] + ";l=" + sr1v[1] + ";t=" + sr1v[2] + ";r=" + sr1v[3] + ";b=" + sr1v[4]);
        Console.WriteLine("p43_rect2_after_reorder=ok" + sr2[0] + ";l=" + sr2[1] + ";t=" + sr2[2] + ";r=" + sr2[3] + ";b=" + sr2[4]);

        // header order array (the header control mirrors the listview's display order)
        IntPtr hHead = SendMessageW(hList, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        int[] ho = new int[3];
        GCHandle hpin = GCHandle.Alloc(ho, GCHandleType.Pinned);
        IntPtr hret = SendMessageW(hHead, HDM_GETORDERARRAY, new IntPtr(3), hpin.AddrOfPinnedObject());
        hpin.Free();
        Console.WriteLine("p43_hdr_order_ret=" + (hret.ToInt64() != 0));
        Console.WriteLine("p43_hdr_order=" + ho[0] + ";" + ho[1] + ";" + ho[2]);

        // header HDI_ORDER per item
        for (int i = 0; i < 3; i++)
        {
            int[] hd = new int[13];
            hd[0] = HDI_ORDER;
            GCHandle pin = GCHandle.Alloc(hd, GCHandleType.Pinned);
            SendMessageW(hHead, HDM_GETITEM, new IntPtr(i), pin.AddrOfPinnedObject());
            pin.Free();
            Console.WriteLine("p43_hdr_item" + i + "_iOrder=" + hd[11]);
        }

        // ===== item 5: reset to identity, does the SECOND set succeed? =====
        int[] sr2x = SetOrder(hList, new int[] { 0, 1, 2 });
        Console.WriteLine("p43_set2=" + (sr2x[0] != 0));
        int[] g2 = GetOrderArr(hList, 3);
        Console.WriteLine("p43_after2=" + g2[0] + ";" + g2[1] + ";" + g2[2] + ";" + g2[3]);

        // re-set a non-identity permutation after reset
        int[] sr3 = SetOrder(hList, new int[] { 1, 2, 0 });
        Console.WriteLine("p43_set3=" + (sr3[0] != 0));
        int[] g3 = GetOrderArr(hList, 3);
        Console.WriteLine("p43_after3=" + g3[0] + ";" + g3[1] + ";" + g3[2] + ";" + g3[3]);

        Console.WriteLine("p43_destroyed=" + (DestroyWindow(hList) != 0));
        Console.WriteLine("p43_parent_destroyed=" + (DestroyWindow(hParent) != 0));
    }
}
