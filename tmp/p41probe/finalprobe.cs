using System;
using System.Runtime.InteropServices;

public static class P41FinalProbe
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
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

    [StructLayout(LayoutKind.Sequential)]
    public struct INITCOMMONCONTROLSEX { public int dwSize; public int dwICC; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize; public int style; public IntPtr lpfnWndProc; public int cbClsExtra;
        public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor;
        public IntPtr hbrBackground; public IntPtr lpszMenuName; public IntPtr lpszClassName; public IntPtr hIconSm;
    }

    public const int WS_CHILD = 0x40000000;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int ICC_TREEVIEW_CLASSES = 0x00000002;
    public const int TVM_FIRST = 0x1100;
    public const int TVM_GETITEMW = TVM_FIRST + 62;
    public const int TVM_SETITEMW = TVM_FIRST + 63;
    public const int TVM_GETITEMSTATE = TVM_FIRST + 39;
    public const int TVM_SELECTITEM = TVM_FIRST + 11;
    public const int TVM_INSERTITEMW = TVM_FIRST + 50;
    public const int TVM_GETNEXTITEM = TVM_FIRST + 10;
    public const int TVM_EXPAND = TVM_FIRST + 2;
    public const int TVM_GETCOUNT = TVM_FIRST + 5;
    public const int TVIF_TEXT = 0x0001;
    public const int TVIF_PARAM = 0x0004;
    public const int TVIF_STATE = 0x0008;
    public const int TVIF_HANDLE = 0x0010;
    public const int TVGN_ROOT = 0x0000;
    public const int TVGN_CHILD = 0x0004;
    public const int TVGN_CARET = 0x0009;
    public const int TVIS_BOLD = 0x0010;
    public const int TVIS_EXPANDED = 0x0020;
    public const int TVE_EXPAND = 0x0002;
    public const int TVE_COLLAPSE = 0x0001;

    public static IntPtr hTree;

    public static void Insert(IntPtr hParent, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] tvi = new int[18];
        long hp = hParent.ToInt64();
        tvi[0] = (int)(hp & 0xFFFFFFFFL);
        tvi[1] = (int)((hp >> 32) & 0xFFFFFFFFL);
        tvi[2] = unchecked((int)0xFFFF0002);
        tvi[4] = TVIF_TEXT | TVIF_PARAM;
        long pt = p.ToInt64();
        tvi[10] = (int)(pt & 0xFFFFFFFFL);
        tvi[11] = (int)((pt >> 32) & 0xFFFFFFFFL);
        tvi[12] = 128;
        tvi[16] = 42; tvi[17] = 0;
        GCHandle pin = GCHandle.Alloc(tvi, GCHandleType.Pinned);
        SendMessageW(hTree, TVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
    }

    public static string GetNodeText(IntPtr hItem)
    {
        IntPtr buf = Marshal.AllocHGlobal(256);
        int[] item = new int[14];
        item[0] = TVIF_TEXT | TVIF_HANDLE;
        long hi = hItem.ToInt64();
        item[2] = (int)(hi & 0xFFFFFFFFL);
        item[3] = (int)((hi >> 32) & 0xFFFFFFFFL);
        long bp = buf.ToInt64();
        item[6] = (int)(bp & 0xFFFFFFFFL);
        item[7] = (int)((bp >> 32) & 0xFFFFFFFFL);
        item[8] = 128;
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        SendMessageW(hTree, TVM_GETITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        return s;
    }

    public static int GetNodeLParam(IntPtr hItem)
    {
        int[] item = new int[14];
        item[0] = TVIF_PARAM | TVIF_HANDLE;
        long hi = hItem.ToInt64();
        item[2] = (int)(hi & 0xFFFFFFFFL);
        item[3] = (int)((hi >> 32) & 0xFFFFFFFFL);
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        SendMessageW(hTree, TVM_GETITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        long lo = item[12];
        long lp = lo & 0xFFFFFFFFL;
        return (int)lp;
    }

    public static bool SetNodeText(IntPtr hItem, string text)
    {
        IntPtr p = Marshal.StringToHGlobalUni(text);
        int[] item = new int[14];
        item[0] = TVIF_TEXT | TVIF_HANDLE;
        long hi = hItem.ToInt64();
        item[2] = (int)(hi & 0xFFFFFFFFL);
        item[3] = (int)((hi >> 32) & 0xFFFFFFFFL);
        long pt = p.ToInt64();
        item[6] = (int)(pt & 0xFFFFFFFFL);
        item[7] = (int)((pt >> 32) & 0xFFFFFFFFL);
        item[8] = 128;
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        IntPtr r = SendMessageW(hTree, TVM_SETITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Marshal.FreeHGlobal(p);
        return r.ToInt64() != 0;
    }

    public static int GetNodeState(IntPtr hItem, int stateMask)
    {
        IntPtr r = SendMessageW(hTree, TVM_GETITEMSTATE, hItem, new IntPtr(stateMask));
        return (int)r.ToInt64() & stateMask;
    }

    public static bool SetNodeState(IntPtr hItem, int stateMask, int state)
    {
        int[] item = new int[14];
        item[0] = TVIF_HANDLE | TVIF_STATE;
        long hi = hItem.ToInt64();
        item[2] = (int)(hi & 0xFFFFFFFFL);
        item[3] = (int)((hi >> 32) & 0xFFFFFFFFL);
        item[4] = state;
        item[5] = stateMask;
        GCHandle pin = GCHandle.Alloc(item, GCHandleType.Pinned);
        IntPtr r = SendMessageW(hTree, TVM_SETITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        return r.ToInt64() != 0;
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(new WndProcDel(DefProc));
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P41ParentF");
        RegisterClassExW(ref wc);
        IntPtr hParent = CreateWindowExW(0, "P41ParentF", "P41PF",
            WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_TREEVIEW_CLASSES;
        InitCommonControlsEx(ref icc);

        hTree = CreateWindowExW(0, "SysTreeView32", "",
            WS_CHILD, 10, 10, 280, 160, hParent, new IntPtr(0x280), hInst, IntPtr.Zero);
        Console.WriteLine("p41_tree_created=" + (hTree != IntPtr.Zero));
        Console.WriteLine("p41_id=" + 0x280);

        int cnt0 = (int)SendMessageW(hTree, TVM_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p41_count0=" + cnt0);

        Insert(IntPtr.Zero, "Root");
        IntPtr root = SendMessageW(hTree, TVM_GETNEXTITEM, new IntPtr(TVGN_ROOT), IntPtr.Zero);
        Console.WriteLine("p41_root_ok=" + (root != IntPtr.Zero));

        Insert(root, "Child");
        IntPtr child = SendMessageW(hTree, TVM_GETNEXTITEM, new IntPtr(TVGN_CHILD), root);
        Console.WriteLine("p41_child_ok=" + (child != IntPtr.Zero));

        int cnt2 = (int)SendMessageW(hTree, TVM_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p41_count2=" + cnt2);

        Console.WriteLine("p41_root_text=" + GetNodeText(root));
        Console.WriteLine("p41_child_text=" + GetNodeText(child));
        Console.WriteLine("p41_root_param=" + GetNodeLParam(root));

        bool settext = SetNodeText(child, "Child2");
        Console.WriteLine("p41_settext_ok=" + settext);
        Console.WriteLine("p41_child_text2=" + GetNodeText(child));

        Console.WriteLine("p41_state_fresh=" + GetNodeState(root, TVIS_BOLD));
        bool setbold = SetNodeState(root, TVIS_BOLD, TVIS_BOLD);
        Console.WriteLine("p41_state_set_ok=" + setbold);
        Console.WriteLine("p41_state_bold=" + GetNodeState(root, TVIS_BOLD));
        bool clearbold = SetNodeState(root, TVIS_BOLD, 0);
        Console.WriteLine("p41_state_clear_ok=" + clearbold);
        Console.WriteLine("p41_state_cleared=" + GetNodeState(root, TVIS_BOLD));

        bool sel = SendMessageW(hTree, TVM_SELECTITEM, new IntPtr(TVGN_CARET), child).ToInt64() != 0;
        Console.WriteLine("p41_select_ok=" + sel);
        IntPtr caret = SendMessageW(hTree, TVM_GETNEXTITEM, new IntPtr(TVGN_CARET), IntPtr.Zero);
        Console.WriteLine("p41_caret_ok=" + (caret != IntPtr.Zero));
        Console.WriteLine("p41_caret_text=" + GetNodeText(caret));

        bool exp = SendMessageW(hTree, TVM_EXPAND, new IntPtr(TVE_EXPAND), root).ToInt64() != 0;
        Console.WriteLine("p41_expand_ok=" + exp);
        Console.WriteLine("p41_state_expanded=" + GetNodeState(root, TVIS_EXPANDED));

        bool col = SendMessageW(hTree, TVM_EXPAND, new IntPtr(TVE_COLLAPSE), root).ToInt64() != 0;
        Console.WriteLine("p41_collapse_ok=" + col);
        Console.WriteLine("p41_state_collapsed=" + GetNodeState(root, TVIS_EXPANDED));

        int cntF = (int)SendMessageW(hTree, TVM_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p41_count_after=" + cntF);

        Console.WriteLine("p41_tree_destroyed=" + (DestroyWindow(hTree) != 0));
        Console.WriteLine("p41_parent_destroyed=" + (DestroyWindow(hParent) != 0));
    }

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
