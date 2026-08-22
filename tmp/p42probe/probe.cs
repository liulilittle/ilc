using System;
using System.Runtime.InteropServices;

public static class P42Probe
{
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryW(string name);
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

    public const string RICHEDIT_CLASS = "RichEdit20W";
    public const int WS_CHILD = 0x40000000;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int ES_MULTILINE = 0x0004;
    public const int WM_SETTEXT = 0x000C;
    public const int WM_GETTEXT = 0x000D;
    public const int WM_GETTEXTLENGTH = 0x000E;
    public const int WM_COMMAND = 0x0111;
    public const int WM_CHAR = 0x0102;
    public const int EM_GETSEL = 0x00B0;
    public const int EM_SETSEL = 0x00B1;
    public const int EM_GETLINECOUNT = 0x00BA;
    public const int EM_GETLINE = 0x00C4;
    public const int EM_LINEFROMCHAR = 0x00C9;
    public const int EM_LINEINDEX = 0x00BB;
    public const int EM_LINELENGTH = 0x00C1;
    public const int EM_SETEVENTMASK = 0x0445;
    public const int ENM_NONE = 0x0000;
    public const int ENM_CHANGE = 0x0001;
    public const int EN_CHANGE = 0x0300;
    public const int ID_RICH42 = 0x290;

    public static int wmCommandCount;
    public static string cmdSeq = "";
    public static int notifCount;

    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_COMMAND)
        {
            wmCommandCount++;
            int cid = (int)(wParam.ToInt64() & 0xFFFFL);
            int notif = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            cmdSeq = cmdSeq + "id" + cid + "/n" + notif + ";";
            if (notif == EN_CHANGE) { notifCount++; }
            return IntPtr.Zero;
        }
        return DefProc(hwnd, msg, wParam, lParam);
    }

    public static void SetText(IntPtr h, string s)
    {
        IntPtr p = Marshal.StringToHGlobalUni(s);
        SendMessageW(h, WM_SETTEXT, IntPtr.Zero, p);
        Marshal.FreeHGlobal(p);
    }

    public static string GetText(IntPtr h)
    {
        IntPtr buf = Marshal.AllocHGlobal(512);
        SendMessageW(h, WM_GETTEXT, new IntPtr(256), buf);
        string s = Marshal.PtrToStringUni(buf);
        Marshal.FreeHGlobal(buf);
        if (s == null) { s = ""; }
        return s;
    }

    public static string Hex(int v) { return "0x" + v.ToString("X8"); }

    public static void ProbeGetLine(IntPtr h, int line)
    {
        int[] gl = new int[32];
        gl[0] = 0x4142;   // marker WORDs: 'A','B' -- if the text lands at +2 this survives
        GCHandle pin = GCHandle.Alloc(gl, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, EM_GETLINE, new IntPtr(line), pin.AddrOfPinnedObject());
        int copied = (int)r.ToInt64();
        string r0 = Marshal.PtrToStringUni(pin.AddrOfPinnedObject());
        string r2 = Marshal.PtrToStringUni(pin.AddrOfPinnedObject() + 2);
        pin.Free();
        if (r0 == null) { r0 = ""; }
        if (r2 == null) { r2 = ""; }
        Console.WriteLine("p42_gl" + line + "_copied=" + copied);
        Console.WriteLine("p42_gl" + line + "_first=" + Hex(gl[0]));
        Console.WriteLine("p42_gl" + line + "_read0=" + r0);
        Console.WriteLine("p42_gl" + line + "_read2=" + r2);
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procDel);
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P42ParentF");
        RegisterClassExW(ref wc);
        IntPtr hParent = CreateWindowExW(0, "P42ParentF", "P42PF",
            WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        IntPtr hLib = LoadLibraryW("riched20.dll");
        Console.WriteLine("p42_lib_loaded=" + (hLib != IntPtr.Zero));

        IntPtr hRich = CreateWindowExW(0, RICHEDIT_CLASS, "",
            WS_CHILD | ES_MULTILINE, 0, 0, 200, 100,
            hParent, new IntPtr(ID_RICH42), hInst, IntPtr.Zero);
        Console.WriteLine("p42_rich_created=" + (hRich != IntPtr.Zero));
        Console.WriteLine("p42_id=" + ID_RICH42);

        // ===== item 6: WM_GETTEXTLENGTH normalization + readback =====
        SetText(hRich, "LineA\r\nLineB\r\nLineC");
        IntPtr lenA = SendMessageW(hRich, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p42_len=" + lenA.ToInt32());
        string txtA = GetText(hRich);
        Console.WriteLine("p42_text_esc=" + txtA.Replace("\r", "\\r").Replace("\n", "\\n"));
        Console.WriteLine("p42_text_chars=" + txtA.Length);

        // ===== item 1: trailing empty line (CRLF-terminated text) =====
        SetText(hRich, "LineA\r\nLineB\r\nLineC\r\n");
        IntPtr lenB = SendMessageW(hRich, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p42_trail_len=" + lenB.ToInt32());
        IntPtr cntB = SendMessageW(hRich, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("p42_trail_linecount=" + cntB.ToInt32());
        string txtB = GetText(hRich);
        Console.WriteLine("p42_trail_text_esc=" + txtB.Replace("\r", "\\r").Replace("\n", "\\n"));

        // ===== back to the main text for the line family =====
        SetText(hRich, "LineA\r\nLineB\r\nLineC");
        Console.WriteLine("p42_linecount=" + SendMessageW(hRich, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32());
        Console.WriteLine("p42_li0=" + SendMessageW(hRich, EM_LINEINDEX, new IntPtr(0), IntPtr.Zero).ToInt32());
        Console.WriteLine("p42_li1=" + SendMessageW(hRich, EM_LINEINDEX, new IntPtr(1), IntPtr.Zero).ToInt32());
        Console.WriteLine("p42_li2=" + SendMessageW(hRich, EM_LINEINDEX, new IntPtr(2), IntPtr.Zero).ToInt32());

        // ===== item 3: EM_LINELENGTH -- does it include CRLF? =====
        Console.WriteLine("p42_ll0=" + SendMessageW(hRich, EM_LINELENGTH, new IntPtr(0), IntPtr.Zero).ToInt32());
        Console.WriteLine("p42_ll7=" + SendMessageW(hRich, EM_LINELENGTH, new IntPtr(7), IntPtr.Zero).ToInt32());
        Console.WriteLine("p42_ll14=" + SendMessageW(hRich, EM_LINELENGTH, new IntPtr(14), IntPtr.Zero).ToInt32());
        Console.WriteLine("p42_ll_end=" + SendMessageW(hRich, EM_LINELENGTH, new IntPtr(-1), IntPtr.Zero).ToInt32());

        // ===== item 2: EM_GETLINE write offset =====
        ProbeGetLine(hRich, 0);
        ProbeGetLine(hRich, 1);
        ProbeGetLine(hRich, 2);

        // ===== EM_LINEFROMCHAR reverse lookup =====
        Console.WriteLine("p42_lfc0=" + SendMessageW(hRich, EM_LINEFROMCHAR, new IntPtr(0), IntPtr.Zero).ToInt32());
        Console.WriteLine("p42_lfc5=" + SendMessageW(hRich, EM_LINEFROMCHAR, new IntPtr(5), IntPtr.Zero).ToInt32());
        Console.WriteLine("p42_lfc7=" + SendMessageW(hRich, EM_LINEFROMCHAR, new IntPtr(7), IntPtr.Zero).ToInt32());

        // ===== item 4: EM_GETSEL packing =====
        SendMessageW(hRich, EM_SETSEL, new IntPtr(0), new IntPtr(-1));
        long selv = SendMessageW(hRich, EM_GETSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
        Console.WriteLine("p42_sel_pack=" + Hex((int)selv));
        Console.WriteLine("p42_sel_start=" + (int)(selv & 0xFFFFL));
        Console.WriteLine("p42_sel_end=" + (int)((selv >> 16) & 0xFFFFL));

        // ===== item 5: WM_CHAR synchronous real EN_CHANGE (mask-gated) =====
        wmCommandCount = 0; cmdSeq = ""; notifCount = 0;

        // previous mask of a fresh RichEdit20W (two-step: ENM_CHANGE then ENM_NONE).
        IntPtr prev1 = SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_CHANGE));
        Console.WriteLine("p42_mask_prev0=" + prev1.ToInt32());
        IntPtr prev2 = SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_NONE));
        Console.WriteLine("p42_mask_prev1=" + prev2.ToInt32());

        // Now the real test: ENM_CHANGE on, WM_CHAR 'X', check the WndProc got
        // a SYNCHRONOUS WM_COMMAND/EN_CHANGE.
        SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_CHANGE));
        SendMessageW(hRich, WM_CHAR, new IntPtr((int)'X'), new IntPtr(1));
        Console.WriteLine("p42_char_cmdcount=" + wmCommandCount);
        Console.WriteLine("p42_char_notifcount=" + notifCount);
        Console.WriteLine("p42_char_seq=" + cmdSeq);

        // Negative gate: mask ENM_NONE -> WM_CHAR must NOT notify.
        SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_NONE));
        SendMessageW(hRich, WM_CHAR, new IntPtr((int)'Y'), new IntPtr(1));
        Console.WriteLine("p42_masked_cmdcount=" + wmCommandCount);
        Console.WriteLine("p42_masked_notifcount=" + notifCount);

        // Text after the two injected chars (caret position decides insertion point).
        string after = GetText(hRich);
        Console.WriteLine("p42_after_esc=" + after.Replace("\r", "\\r").Replace("\n", "\\n"));

        Console.WriteLine("p42_destroyed=" + (DestroyWindow(hRich) != 0));
        Console.WriteLine("p42_parent_destroyed=" + (DestroyWindow(hParent) != 0));
    }
}
