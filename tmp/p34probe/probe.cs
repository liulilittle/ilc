using System;
using System.Runtime.InteropServices;

public static class P34Probe
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

    public const int WS_CHILD = 0x40000000;
    public const int CS_HREDRAW = 0x0002;
    public const int CS_VREDRAW = 0x0001;
    public const string PROGRESS_CLASS = "msctls_progress32";
    public const int ICC_PROGRESS_CLASS = 0x00000020;

    public const int PBM_SETPOS = 0x0402;
    public const int PBM_SETSTEP = 0x0404;
    public const int PBM_STEPIT = 0x0405;
    public const int PBM_SETRANGE32 = 0x0406;
    public const int PBM_GETRANGE = 0x0407;
    public const int PBM_GETPOS = 0x0408;
    public const int PBM_SETBARCOLOR = 0x0409;
    public const int PBM_GETSTEP = 0x040D;
    public const int PBM_GETBARCOLOR = 0x040F;
    public const int PBM_SETBKCOLOR = 0x2001;

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
        INITCOMMONCONTROLSEX icc = new INITCOMMONCONTROLSEX();
        icc.dwSize = 8;
        icc.dwICC = ICC_PROGRESS_CLASS;
        int iccRet = InitCommonControlsEx(ref icc);
        Console.WriteLine("icc=" + (iccRet != 0));

        IntPtr hMod = GetModuleHandleW(null);

        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = 80;
        wc.style = CS_HREDRAW | CS_VREDRAW;
        ParentProcDel = ParentProc;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ParentProcDel);
        wc.hInstance = hMod;
        IntPtr clsName = Marshal.StringToHGlobalUni("P34ProbeParent");
        wc.lpszClassName = clsName;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("parent_class=" + (atom != 0));
        Marshal.FreeHGlobal(clsName);

        IntPtr hParent = CreateWindowExW(0, "P34ProbeParent", "parent", 0, 0, 0, 320, 200, IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("parent=" + (hParent != IntPtr.Zero));

        IntPtr hBar = CreateWindowExW(0, PROGRESS_CLASS, "",
            WS_CHILD, 0, 0, 320, 20, hParent, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("created=" + (hBar != IntPtr.Zero));
        Console.WriteLine("parent_is_form=" + (GetParent(hBar) == hParent));

        // Probe 1: PBM_SETRANGE32 return value + PBM_GETRANGE packing direction.
        IntPtr setRange = SendMessageW(hBar, PBM_SETRANGE32, new IntPtr(20), new IntPtr(150));
        Console.WriteLine("setrange_ret=" + setRange.ToInt64());
        IntPtr getRange = SendMessageW(hBar, PBM_GETRANGE, IntPtr.Zero, IntPtr.Zero);
        long raw = getRange.ToInt64();
        int loA = (int)(raw & 0xFFFFL);
        int hiA = (int)((raw >> 16) & 0xFFFFL);
        int loB = (int)((raw >> 16) & 0xFFFFL);
        int hiB = (int)(raw & 0xFFFFL);
        Console.WriteLine("getrange_raw=" + raw + " loA=" + loA + " hiA=" + hiA + " loB=" + loB + " hiB=" + hiB);

        // Probe 2: clamp-on-set.
        IntPtr pos0 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pos0=" + pos0.ToInt64());
        IntPtr setLo = SendMessageW(hBar, PBM_SETPOS, new IntPtr(10), IntPtr.Zero);
        IntPtr pos1 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("setpos10_prev=" + setLo.ToInt64() + " pos=" + pos1.ToInt64());
        IntPtr setHi = SendMessageW(hBar, PBM_SETPOS, new IntPtr(500), IntPtr.Zero);
        IntPtr pos2 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("setpos500_prev=" + setHi.ToInt64() + " pos=" + pos2.ToInt64());

        // Probe 3: position round-trip -- SETPOS returns PREVIOUS position.
        IntPtr setMid = SendMessageW(hBar, PBM_SETPOS, new IntPtr(140), IntPtr.Zero);
        IntPtr pos3 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("setpos140_prev=" + setMid.ToInt64() + " pos=" + pos3.ToInt64());

        // Probe 4: PBM_GETSTEP default + PBM_SETSTEP prev-return.
        IntPtr stepDef = SendMessageW(hBar, PBM_GETSTEP, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("step_default=" + stepDef.ToInt64());
        IntPtr setStep = SendMessageW(hBar, PBM_SETSTEP, new IntPtr(25), IntPtr.Zero);
        IntPtr stepNow = SendMessageW(hBar, PBM_GETSTEP, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("setstep_prev=" + setStep.ToInt64() + " step=" + stepNow.ToInt64());

        // Probe 5: PBM_STEPIT -- returns PREVIOUS position, advances by step,
        // caps at the high limit (150). Current pos = 140, step = 25.
        IntPtr si1 = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr sp1 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        IntPtr si2 = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr sp2 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        IntPtr si3 = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr sp3 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        IntPtr si4 = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr sp4 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("stepit1_prev=" + si1.ToInt64() + " pos=" + sp1.ToInt64());
        Console.WriteLine("stepit2_prev=" + si2.ToInt64() + " pos=" + sp2.ToInt64());
        Console.WriteLine("stepit3_prev=" + si3.ToInt64() + " pos=" + sp3.ToInt64());
        Console.WriteLine("stepit4_prev=" + si4.ToInt64() + " pos=" + sp4.ToInt64());

        // Probe 6: PBM_SETBARCOLOR prev-color round-trip + PBM_GETBARCOLOR (v6-only).
        IntPtr bc1 = SendMessageW(hBar, PBM_SETBARCOLOR, IntPtr.Zero, new IntPtr(65280));
        IntPtr bc2 = SendMessageW(hBar, PBM_SETBARCOLOR, IntPtr.Zero, new IntPtr(255));
        IntPtr bc3 = SendMessageW(hBar, PBM_SETBARCOLOR, IntPtr.Zero, new IntPtr(65280));
        IntPtr getBC = SendMessageW(hBar, PBM_GETBARCOLOR, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("setbarcolor1_prev=" + bc1.ToInt64());
        Console.WriteLine("setbarcolor2_prev=" + bc2.ToInt64());
        Console.WriteLine("setbarcolor3_prev=" + bc3.ToInt64());
        Console.WriteLine("getbarcolor=" + getBC.ToInt64());

        // Probe 7: PBM_SETBKCOLOR prev-color round-trip.
        IntPtr bk1 = SendMessageW(hBar, PBM_SETBKCOLOR, IntPtr.Zero, new IntPtr(16777215));
        IntPtr bk2 = SendMessageW(hBar, PBM_SETBKCOLOR, IntPtr.Zero, new IntPtr(0));
        IntPtr bk3 = SendMessageW(hBar, PBM_SETBKCOLOR, IntPtr.Zero, new IntPtr(16777215));
        Console.WriteLine("setbkcolor1_prev=" + bk1.ToInt64());
        Console.WriteLine("setbkcolor2_prev=" + bk2.ToInt64());
        Console.WriteLine("setbkcolor3_prev=" + bk3.ToInt64());

        // Step does not affect position (STEPIT is the only advance).
        IntPtr posFinal = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pos_final=" + posFinal.ToInt64());

        Console.WriteLine("destroyed=" + (DestroyWindow(hBar) != 0));
        Console.WriteLine("parent_destroyed=" + (DestroyWindow(hParent) != 0));
    }
}
