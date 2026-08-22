using System;
using System.Runtime.InteropServices;

public static class P39Probe
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

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        return DefProc(hwnd, msg, wParam, lParam);
    }

    // ---- constants (winforms1 Phase 21/33 + Phase 39) ----
    public const int WS_CHILD = 0x40000000;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int LVS_REPORT = 0x0001;
    public const int LVS_EX_CHECKBOXES = 0x0004;
    public const int LVM_FIRST = 0x1000;
    public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    public const int LVM_INSERTITEMW = LVM_FIRST + 77;
    public const int LVM_GETITEMSTATE = LVM_FIRST + 44;
    public const int LVM_SETITEMSTATE = LVM_FIRST + 43;
    public const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
    public const int LVM_GETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 55;
    public const int LVM_GETSELECTEDCOUNT = LVM_FIRST + 50;
    public const int LVM_GETNEXTITEM = LVM_FIRST + 12;
    public const int LVIF_TEXT = 0x0001;
    public const int LVIF_PARAM = 0x0004;
    public const int LVIS_SELECTED = 0x0002;
    public const int LVIS_STATEIMAGEMASK = 0xF000;
    public const int LVNI_SELECTED = 0x0002;
    public const int LVM_SETCHECKSTATE = 0x105D;   // v6-only
    public const int LVM_GETCHECKSTATE = 0x105E;   // v6-only

    public static int InsertItem(IntPtr h, string text)
    {
        int iItem = (int)SendMessageW(h, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
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
        IntPtr r = SendMessageW(h, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return (int)r.ToInt64();
    }

    public static bool SetItemStateBits(IntPtr h, int index, int state, int mask)
    {
        int[] lvi = new int[22];
        lvi[3] = state;
        lvi[4] = mask;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_SETITEMSTATE, new IntPtr(index), pin.AddrOfPinnedObject());
        pin.Free();
        return r.ToInt64() != 0;
    }

    public static int GetCheckState(IntPtr h, int index)
    {
        IntPtr r = SendMessageW(h, LVM_GETITEMSTATE, new IntPtr(index), new IntPtr(LVIS_STATEIMAGEMASK));
        return ((int)r.ToInt64() >> 12) - 1;
    }

    public static string GetIndices(IntPtr h)
    {
        string s = "";
        int idx = -1;
        while (true)
        {
            IntPtr r = SendMessageW(h, LVM_GETNEXTITEM, new IntPtr(idx), new IntPtr(LVNI_SELECTED));
            idx = (int)r.ToInt64();
            if (idx < 0) break;
            s = s + idx + ";";
        }
        return s;
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        IntPtr pfn = Marshal.GetFunctionPointerForDelegate(procDel);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = 0;
        wc.lpfnWndProc = pfn;
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P39ProbeParent");
        ushort reg = RegisterClassExW(ref wc);
        Console.WriteLine("reg=" + (reg != 0));
        IntPtr hParent = CreateWindowExW(0, "P39ProbeParent", "P39Parent",
            WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        Console.WriteLine("parent_created=" + (hParent != IntPtr.Zero));

        // SysListView32 with LVS_REPORT (the Phase-33 shape).
        IntPtr hList = CreateWindowExW(0, "SysListView32", "",
            WS_CHILD | LVS_REPORT, 20, 300, 280, 120, hParent, IntPtr.Zero, hInst, IntPtr.Zero);
        Console.WriteLine("list_created=" + (hList != IntPtr.Zero));

        // Enable LVS_EX_CHECKBOXES -- the setter returns the PREVIOUS extended style.
        IntPtr extPrev = SendMessageW(hList, LVM_SETEXTENDEDLISTVIEWSTYLE, new IntPtr(LVS_EX_CHECKBOXES), new IntPtr(LVS_EX_CHECKBOXES));
        Console.WriteLine("checkbox_ext=" + (int)extPrev.ToInt64());
        IntPtr extNow = SendMessageW(hList, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("checkbox_readback=" + (int)extNow.ToInt64());

        // Insert 3 rows -- explicit-index append (0/1/2).
        Console.WriteLine("insert0=" + InsertItem(hList, "Alpha"));
        Console.WriteLine("insert1=" + InsertItem(hList, "Beta"));
        Console.WriteLine("insert2=" + InsertItem(hList, "Gamma"));
        Console.WriteLine("count3=" + (int)SendMessageW(hList, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());

        // FRESH state image on the newly-inserted rows (with checkboxes ON):
        // state image n is stored as n<<12; GetCheckState decodes (r>>12)-1.
        Console.WriteLine("fresh0=" + GetCheckState(hList, 0));
        Console.WriteLine("fresh1=" + GetCheckState(hList, 1));
        Console.WriteLine("fresh2=" + GetCheckState(hList, 2));

        // Checked: state image 2 (2<<12). Unchecked: state image 1 (1<<12).
        Console.WriteLine("setchk0=" + SetItemStateBits(hList, 0, 2 << 12, LVIS_STATEIMAGEMASK));
        Console.WriteLine("getchk0=" + GetCheckState(hList, 0));
        Console.WriteLine("setchk2=" + SetItemStateBits(hList, 2, 2 << 12, LVIS_STATEIMAGEMASK));
        Console.WriteLine("getchk2=" + GetCheckState(hList, 2));
        Console.WriteLine("setchk1f=" + SetItemStateBits(hList, 1, 1 << 12, LVIS_STATEIMAGEMASK));
        Console.WriteLine("getchk1=" + GetCheckState(hList, 1));

        // v6-only checkbox messages: the v5.82 control IGNORES them (dead reads).
        IntPtr v6get = SendMessageW(hList, LVM_GETCHECKSTATE, new IntPtr(0), IntPtr.Zero);
        Console.WriteLine("v6_get=" + (int)v6get.ToInt64());
        IntPtr v6set = SendMessageW(hList, LVM_SETCHECKSTATE, new IntPtr(0), new IntPtr(1));
        Console.WriteLine("v6_set=" + (int)v6set.ToInt64());
        Console.WriteLine("chk0_after_v6=" + GetCheckState(hList, 0));

        // Multi-select: two rows LVIS_SELECTED (independent of checkbox bits).
        Console.WriteLine("sel0=" + SetItemStateBits(hList, 0, LVIS_SELECTED, LVIS_SELECTED));
        Console.WriteLine("sel2=" + SetItemStateBits(hList, 2, LVIS_SELECTED, LVIS_SELECTED));
        Console.WriteLine("sel0_read=" + ((int)SendMessageW(hList, LVM_GETITEMSTATE, new IntPtr(0), new IntPtr(LVIS_SELECTED)).ToInt64() != 0));
        Console.WriteLine("sel1_read=" + ((int)SendMessageW(hList, LVM_GETITEMSTATE, new IntPtr(1), new IntPtr(LVIS_SELECTED)).ToInt64() != 0));
        Console.WriteLine("sel2_read=" + ((int)SendMessageW(hList, LVM_GETITEMSTATE, new IntPtr(2), new IntPtr(LVIS_SELECTED)).ToInt64() != 0));
        IntPtr sc = SendMessageW(hList, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("selcount=" + (int)sc.ToInt64());
        Console.WriteLine("indices=" + GetIndices(hList));

        // Cancel round trip: unselect row 0.
        Console.WriteLine("unsel0=" + SetItemStateBits(hList, 0, 0, LVIS_SELECTED));
        Console.WriteLine("selcount_after=" + (int)SendMessageW(hList, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("indices_after=" + GetIndices(hList));

        // Checkbox / selection bit independence: unselecting row 0 leaves its
        // checkbox checked; the checkbox bits were never touched by the
        // LVIS_SELECTED sets.
        Console.WriteLine("chk0_indep=" + GetCheckState(hList, 0));

        // Full clear: unselect row 2.
        Console.WriteLine("unsel2=" + SetItemStateBits(hList, 2, 0, LVIS_SELECTED));
        Console.WriteLine("selcount_clear=" + (int)SendMessageW(hList, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        Console.WriteLine("indices_clear=" + GetIndices(hList));
        Console.WriteLine("chk2_indep=" + GetCheckState(hList, 2));
        Console.WriteLine("chk1_indep=" + GetCheckState(hList, 1));

        Console.WriteLine("destroy=" + (DestroyWindow(hList) != 0));
        Console.WriteLine("destroy_parent=" + (DestroyWindow(hParent) != 0));
    }
}
