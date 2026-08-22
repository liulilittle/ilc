using System;
using System.Runtime.InteropServices;

public static class P40StepProbe
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

    public const int WS_CHILD = 0x40000000;
    public const int WS_OVERLAPPED = 0x00000000;
    public const int ES_MULTILINE = 0x0004;
    public const int ES_AUTOVSCROLL = 0x0040;
    public const int ES_AUTOHSCROLL = 0x0080;
    public const int WM_SETTEXT = 0x000C;
    public const int WM_COMMAND = 0x0111;
    public const int WM_CHAR = 0x0102;
    public const int EM_SETSEL = 0x00B1;
    public const int EM_SETLIMITTEXT = 0x00C5;
    public const int EM_SETMODIFY = 0x00B9;
    public const int EM_UNDO = 0x00C7;
    public const int EM_EMPTYUNDOBUFFER = 0x00CD;
    public const int ID_EDIT40 = 0x270;

    public static int wmCommandCount;
    public static string cmdSeq = "";

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

    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_COMMAND)
        {
            wmCommandCount++;
            int cid = (int)(wParam.ToInt64() & 0xFFFFL);
            int notif = (int)((wParam.ToInt64() >> 16) & 0xFFFFL);
            cmdSeq = cmdSeq + "id" + cid + "/n" + notif + ";";
            return IntPtr.Zero;
        }
        return DefProc(hwnd, msg, wParam, lParam);
    }

    public static void Main()
    {
        IntPtr hInst = GetModuleHandleW(null);
        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procDel);
        wc.hInstance = hInst;
        wc.lpszClassName = Marshal.StringToHGlobalUni("P40StepParent");
        RegisterClassExW(ref wc);
        IntPtr hParent = CreateWindowExW(0, "P40StepParent", "P40StepP",
            WS_OVERLAPPED, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        IntPtr hEdit = CreateWindowExW(0, "EDIT", "",
            WS_CHILD | ES_MULTILINE | ES_AUTOVSCROLL | ES_AUTOHSCROLL, 0, 0, 200, 100,
            hParent, new IntPtr(ID_EDIT40), hInst, IntPtr.Zero);

        IntPtr t = Marshal.StringToHGlobalUni("LineA\r\nLineB\r\nLineC");
        SendMessageW(hEdit, WM_SETTEXT, IntPtr.Zero, t);
        Marshal.FreeHGlobal(t);
        cmdSeq = ""; wmCommandCount = 0;

        // Probe each step's EN_CHANGE contribution.
        SendMessageW(hEdit, EM_SETLIMITTEXT, new IntPtr(20), IntPtr.Zero);
        Console.WriteLine("after_setlimit=" + wmCommandCount);
        SendMessageW(hEdit, EM_SETMODIFY, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("after_setmodify1=" + wmCommandCount);
        SendMessageW(hEdit, EM_SETMODIFY, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("after_setmodify0=" + wmCommandCount);
        SendMessageW(hEdit, EM_SETSEL, new IntPtr(0), new IntPtr(-1));
        Console.WriteLine("after_setsel=" + wmCommandCount);
        SendMessageW(hEdit, WM_CHAR, new IntPtr('Z'), new IntPtr(1));
        Console.WriteLine("after_char=" + wmCommandCount);
        SendMessageW(hEdit, EM_UNDO, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("after_undo1=" + wmCommandCount);
        SendMessageW(hEdit, EM_UNDO, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("after_undo2=" + wmCommandCount);
        SendMessageW(hEdit, EM_EMPTYUNDOBUFFER, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("after_empty=" + wmCommandCount);
        Console.WriteLine("cmd_seq=" + cmdSeq);

        Console.WriteLine("destroy=" + (DestroyWindow(hEdit) != 0));
        Console.WriteLine("destroy_parent=" + (DestroyWindow(hParent) != 0));
    }
}
