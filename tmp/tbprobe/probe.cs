using System;
using System.Runtime.InteropServices;

public static class TbProbe
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

    [StructLayout(LayoutKind.Sequential)]
    public struct TBBUTTON
    {
        public int iBitmap;
        public int idCommand;
        public byte fsState;
        public byte fsStyle;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] bReserved;
        public IntPtr dwData;
        public IntPtr iString;
    }

    public const int WS_CHILD = 0x40000000;
    public const int CCS_TOP = 0x00000001;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;

    public const string WC_TOOLBAR = "ToolbarWindow32";
    public const int ICC_BAR_CLASSES = 0x00000004;
    public const int TB_BUTTONSTRUCTSIZE = 0x041E;
    public const int TB_ENABLEBUTTON = 0x0401;
    public const int TB_CHECKBUTTON = 0x0402;
    public const int TB_ISBUTTONENABLED = 0x0409;
    public const int TB_ISBUTTONCHECKED = 0x040A;
    public const int TB_GETSTATE = 0x0412;
    public const int TB_ADDBUTTONSW = 0x0444;
    public const int TB_DELETEBUTTON = 0x0416;
    public const int TB_GETBUTTON = 0x0417;
    public const int TB_BUTTONCOUNT = 0x0418;
    public const int TB_COMMANDTOINDEX = 0x0419;
    public const int TB_GETITEMRECT = 0x041D;
    public const int TB_GETBUTTONTEXTW = 0x044B;
    public const int TB_AUTOSIZE = 0x041F;
    public const int CCM_SETUNICODEFORMAT = 0x2005;
    public const int CCM_GETUNICODEFORMAT = 0x2006;
    public const int TBSTATE_ENABLED = 0x0004;
    public const int TBSTYLE_BUTTON = 0x0000;
    public const int TBSTYLE_SEP = 0x0001;
    public const int TBSTYLE_CHECK = 0x0002;
    public const int I_IMAGENONE = -2;
    public const int WM_COMMAND = 0x0111;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int MK_LBUTTON = 0x0001;
    public const int WM_SETREDRAW = 0x000B;
    public const int ID_TB_NEW = 0x1F1;
    public const int ID_TB_OPEN = 0x1F2;
    public const int ID_TB_BOLD = 0x1F4;

    public delegate IntPtr WndProcDel(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    public static WndProcDel ParentProcDel;
    public static int CmdCount;
    public static int LastCmd;
    public static int LastCmdHi;

    public static IntPtr ParentProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_COMMAND)
        {
            CmdCount++;
            LastCmd = (int)(wParam.ToInt64() & 0xFFFFL);
            LastCmdHi = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    [DllImport("user32", EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void Main()
    {
        // Probe 0: TBBUTTON offsetof discipline.
        Console.WriteLine("sizeof_tbbutton=" + Marshal.SizeOf(typeof(TBBUTTON)));
        Console.WriteLine("off_iBitmap=" + Marshal.OffsetOf(typeof(TBBUTTON), "iBitmap").ToInt64());
        Console.WriteLine("off_idCommand=" + Marshal.OffsetOf(typeof(TBBUTTON), "idCommand").ToInt64());
        Console.WriteLine("off_fsState=" + Marshal.OffsetOf(typeof(TBBUTTON), "fsState").ToInt64());
        Console.WriteLine("off_fsStyle=" + Marshal.OffsetOf(typeof(TBBUTTON), "fsStyle").ToInt64());
        Console.WriteLine("off_dwData=" + Marshal.OffsetOf(typeof(TBBUTTON), "dwData").ToInt64());
        Console.WriteLine("off_iString=" + Marshal.OffsetOf(typeof(TBBUTTON), "iString").ToInt64());

        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_BAR_CLASSES;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("icc=" + (iccRet != 0));

        IntPtr hMod = GetModuleHandleW(null);

        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = CS_HREDRAW | CS_VREDRAW;
        ParentProcDel = ParentProc;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ParentProcDel);
        wc.hInstance = hMod;
        IntPtr clsName = Marshal.StringToHGlobalUni("TbProbeParent");
        wc.lpszClassName = clsName;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("parent_class=" + (atom != 0));
        Marshal.FreeHGlobal(clsName);

        IntPtr hParent = CreateWindowExW(0, "TbProbeParent", "parent", 0, 0, 0, 320, 200, IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("parent=" + (hParent != IntPtr.Zero));

        IntPtr hTool = CreateWindowExW(0, WC_TOOLBAR, "",
            WS_CHILD | CCS_TOP, 0, 0, 320, 24, hParent, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("created=" + (hTool != IntPtr.Zero));
        Console.WriteLine("parent_is_form=" + (GetParent(hTool) == hParent));

        // Probe 1: Unicode format channel.
        IntPtr unPrev = SendMessageW(hTool, CCM_SETUNICODEFORMAT, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("unicode_prev=" + unPrev.ToInt64());
        IntPtr unNow = SendMessageW(hTool, CCM_GETUNICODEFORMAT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("unicode_now=" + unNow.ToInt64());

        // Probe 2: TB_BUTTONSTRUCTSIZE return shape.
        IntPtr bss = SendMessageW(hTool, TB_BUTTONSTRUCTSIZE, new IntPtr(32), IntPtr.Zero);
        Console.WriteLine("bss_ret=" + bss.ToInt64());

        // Probe 3: build 2 buttons + a sep as pinned int[8] -> add.
        string tNew = "New";
        string tOpen = "Open";
        IntPtr pNew = Marshal.StringToHGlobalUni(tNew);
        IntPtr pOpen = Marshal.StringToHGlobalUni(tOpen);
        int[] btns = new int[24];
        // btn 0 (New): iBitmap=-2, id=0x1F1, state=4|style0<<8, reserved 0, dwData=42, iString=ptr
        btns[0] = I_IMAGENONE;
        btns[1] = ID_TB_NEW;
        btns[2] = TBSTATE_ENABLED | (TBSTYLE_BUTTON << 8);
        btns[3] = 0;
        btns[4] = 42; btns[5] = 0;
        long pn = pNew.ToInt64();
        btns[6] = (int)(pn & 0xFFFFFFFFL); btns[7] = (int)((pn >> 32) & 0xFFFFFFFFL);
        // btn 1 (Open)
        btns[8] = I_IMAGENONE;
        btns[9] = ID_TB_OPEN;
        btns[10] = TBSTATE_ENABLED | (TBSTYLE_BUTTON << 8);
        btns[11] = 0;
        btns[12] = 42; btns[13] = 0;
        long po = pOpen.ToInt64();
        btns[14] = (int)(po & 0xFFFFFFFFL); btns[15] = (int)((po >> 32) & 0xFFFFFFFFL);
        // btn 2 (sep)
        btns[16] = 0;
        btns[17] = 0;
        btns[18] = 0 | (TBSTYLE_SEP << 8);
        btns[19] = 0;
        btns[20] = 0; btns[21] = 0;
        btns[22] = 0; btns[23] = 0;

        GCHandle pin = GCHandle.Alloc(btns, GCHandleType.Pinned);
        IntPtr addRet = SendMessageW(hTool, TB_ADDBUTTONSW, new IntPtr(3), pin.AddrOfPinnedObject());
        pin.Free();
        Console.WriteLine("add_ret=" + addRet.ToInt64());
        IntPtr cnt = SendMessageW(hTool, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("count=" + cnt.ToInt64());

        // Probe 4: TB_GETBUTTON readback (fields except iString).
        int[] got = new int[8];
        GCHandle pin2 = GCHandle.Alloc(got, GCHandleType.Pinned);
        IntPtr gret = SendMessageW(hTool, TB_GETBUTTON, new IntPtr(0), pin2.AddrOfPinnedObject());
        pin2.Free();
        int gId = got[1];
        int gState = got[2] & 0xFF;
        int gStyle = (got[2] >> 8) & 0xFF;
        int gData = got[4];
        long gStr = ((long)got[7] << 32) | (long)(got[6] & 0xFFFFFFFFL);
        Console.WriteLine("get0_ret=" + gret.ToInt64() + " id=" + gId + " state=" + gState + " style=" + gStyle + " data=" + gData + " istr_nonzero=" + (gStr != 0));

        // Probe 5: text via TB_GETBUTTONTEXTW, then free iString buffers, read again.
        IntPtr tbuf = Marshal.AllocHGlobal(256);
        IntPtr lt0 = SendMessageW(hTool, TB_GETBUTTONTEXTW, new IntPtr(ID_TB_NEW), tbuf);
        Console.WriteLine("text_new_len=" + lt0.ToInt64() + " text_new=" + Marshal.PtrToStringUni(tbuf));
        IntPtr lt1 = SendMessageW(hTool, TB_GETBUTTONTEXTW, new IntPtr(ID_TB_OPEN), tbuf);
        Console.WriteLine("text_open_len=" + lt1.ToInt64() + " text_open=" + Marshal.PtrToStringUni(tbuf));
        Marshal.FreeHGlobal(tbuf);
        Marshal.FreeHGlobal(pNew);
        Marshal.FreeHGlobal(pOpen);
        tbuf = Marshal.AllocHGlobal(256);
        IntPtr lt0b = SendMessageW(hTool, TB_GETBUTTONTEXTW, new IntPtr(ID_TB_NEW), tbuf);
        Console.WriteLine("text_new_afterfree_len=" + lt0b.ToInt64() + " text_new_afterfree=" + Marshal.PtrToStringUni(tbuf));
        Marshal.FreeHGlobal(tbuf);

        // Probe 6: TB_GETITEMRECT.
        int[] rc = new int[4];
        GCHandle pin3 = GCHandle.Alloc(rc, GCHandleType.Pinned);
        IntPtr rcRet = SendMessageW(hTool, TB_GETITEMRECT, new IntPtr(0), pin3.AddrOfPinnedObject());
        pin3.Free();
        Console.WriteLine("itemrect_ret=" + rcRet.ToInt64() + " l=" + rc[0] + " t=" + rc[1] + " r=" + rc[2] + " b=" + rc[3] + " w=" + (rc[2] - rc[0]) + " h=" + (rc[3] - rc[1]));

        // Probe 7: real mouse click on button 0 (New) -> parent WM_COMMAND?
        int cx = (rc[0] + rc[2]) / 2;
        int cy = (rc[1] + rc[3]) / 2;
        int lparam = (cy << 16) | (cx & 0xFFFF);
        CmdCount = 0;
        SendMessageW(hTool, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), new IntPtr(lparam));
        SendMessageW(hTool, WM_LBUTTONUP, new IntPtr(0), new IntPtr(lparam));
        Console.WriteLine("click_cmd_count=" + CmdCount + " last_cmd=" + LastCmd + " hi=" + LastCmdHi);

        // Probe 8: add a 5th button (Bold, TBSTYLE_CHECK) then the check/enable
        // state channel + delete + commandtoindex.
        string tBold = "Bold";
        IntPtr pBold = Marshal.StringToHGlobalUni(tBold);
        int[] bold = new int[8];
        bold[0] = I_IMAGENONE;
        bold[1] = ID_TB_BOLD;
        bold[2] = TBSTATE_ENABLED | (TBSTYLE_CHECK << 8);
        bold[3] = 0;
        bold[4] = 42; bold[5] = 0;
        long pb = pBold.ToInt64();
        bold[6] = (int)(pb & 0xFFFFFFFFL); bold[7] = (int)((pb >> 32) & 0xFFFFFFFFL);
        GCHandle pin4 = GCHandle.Alloc(bold, GCHandleType.Pinned);
        IntPtr addRet2 = SendMessageW(hTool, TB_ADDBUTTONSW, new IntPtr(1), pin4.AddrOfPinnedObject());
        pin4.Free();
        Marshal.FreeHGlobal(pBold);
        Console.WriteLine("add_bold_ret=" + addRet2.ToInt64());
        IntPtr cntBold = SendMessageW(hTool, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("count_with_bold=" + cntBold.ToInt64());

        IntPtr stBefore = SendMessageW(hTool, TB_GETSTATE, new IntPtr(ID_TB_BOLD), IntPtr.Zero);
        SendMessageW(hTool, TB_CHECKBUTTON, new IntPtr(ID_TB_BOLD), new IntPtr(1));
        IntPtr isc = SendMessageW(hTool, TB_ISBUTTONCHECKED, new IntPtr(ID_TB_BOLD), IntPtr.Zero);
        IntPtr st = SendMessageW(hTool, TB_GETSTATE, new IntPtr(ID_TB_BOLD), IntPtr.Zero);
        Console.WriteLine("check_prev=" + stBefore.ToInt64() + " checked=" + isc.ToInt64() + " state=" + st.ToInt64());
        SendMessageW(hTool, TB_ENABLEBUTTON, new IntPtr(ID_TB_OPEN), IntPtr.Zero);
        IntPtr stD = SendMessageW(hTool, TB_GETSTATE, new IntPtr(ID_TB_OPEN), IntPtr.Zero);
        IntPtr isE = SendMessageW(hTool, TB_ISBUTTONENABLED, new IntPtr(ID_TB_OPEN), IntPtr.Zero);
        Console.WriteLine("disabled_state=" + stD.ToInt64() + " isenabled=" + isE.ToInt64());
        SendMessageW(hTool, TB_ENABLEBUTTON, new IntPtr(ID_TB_OPEN), new IntPtr(1));
        IntPtr stE = SendMessageW(hTool, TB_GETSTATE, new IntPtr(ID_TB_OPEN), IntPtr.Zero);
        Console.WriteLine("reenabled_state=" + stE.ToInt64());

        IntPtr delRet = SendMessageW(hTool, TB_DELETEBUTTON, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("del_ret=" + delRet.ToInt64());
        IntPtr cnt2 = SendMessageW(hTool, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("count_after=" + cnt2.ToInt64());
        IntPtr cti = SendMessageW(hTool, TB_COMMANDTOINDEX, new IntPtr(ID_TB_OPEN), IntPtr.Zero);
        Console.WriteLine("cmdtoindex_deleted=" + cti.ToInt64());

        DestroyWindow(hTool);
        DestroyWindow(hParent);
        Console.WriteLine("done");
    }
}
