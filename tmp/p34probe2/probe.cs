using System;
using System.Runtime.InteropServices;

public static class P34Probe2
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
    public struct PBRANGE { public int iMin; public int iMax; }

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
        IntPtr clsName = Marshal.StringToHGlobalUni("P34Probe2Parent");
        wc.lpszClassName = clsName;
        ushort atom = RegisterClassExW(ref wc);
        Console.WriteLine("parent_class=" + (atom != 0));
        Marshal.FreeHGlobal(clsName);

        IntPtr hParent = CreateWindowExW(0, "P34Probe2Parent", "parent", 0, 0, 0, 320, 200, IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("parent=" + (hParent != IntPtr.Zero));

        IntPtr hBar = CreateWindowExW(0, PROGRESS_CLASS, "",
            WS_CHILD, 0, 0, 320, 20, hParent, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("created=" + (hBar != IntPtr.Zero));

        // GETRANGE on the DEFAULT range (0..100) before any SETRANGE32.
        IntPtr grDefault = SendMessageW(hBar, PBM_GETRANGE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("getrange_default=" + grDefault.ToInt64());

        // GETRANGE with wParam=1 (fRedraw) -- does wParam change the return?
        IntPtr grRedraw = SendMessageW(hBar, PBM_GETRANGE, new IntPtr(1), IntPtr.Zero);
        Console.WriteLine("getrange_redraw=" + grRedraw.ToInt64());

        // GETRANGE into a PBRANGE struct.
        int[] range = new int[2];
        GCHandle rp = GCHandle.Alloc(range, GCHandleType.Pinned);
        IntPtr grStruct = SendMessageW(hBar, PBM_GETRANGE, IntPtr.Zero, rp.AddrOfPinnedObject());
        rp.Free();
        Console.WriteLine("getrange_struct_ret=" + grStruct.ToInt64() + " imin=" + range[0] + " imax=" + range[1]);

        // SETRANGE32 to 20..150; check the PREV-range packing via a second set.
        IntPtr sr1 = SendMessageW(hBar, PBM_SETRANGE32, new IntPtr(20), new IntPtr(150));
        Console.WriteLine("setrange1_ret=" + sr1.ToInt64() + " hex=" + String.Format("0x{0:X8}", (uint)sr1.ToInt64()));
        IntPtr sr2 = SendMessageW(hBar, PBM_SETRANGE32, new IntPtr(30), new IntPtr(200));
        Console.WriteLine("setrange2_ret=" + sr2.ToInt64() + " hex=" + String.Format("0x{0:X8}", (uint)sr2.ToInt64()));
        IntPtr sr3 = SendMessageW(hBar, PBM_SETRANGE32, new IntPtr(20), new IntPtr(150));
        Console.WriteLine("setrange3_ret=" + sr3.ToInt64() + " hex=" + String.Format("0x{0:X8}", (uint)sr3.ToInt64()));

        // GETRANGE after SETRANGE32(20,150), struct + raw.
        IntPtr grNow = SendMessageW(hBar, PBM_GETRANGE, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("getrange_now=" + grNow.ToInt64() + " hex=" + String.Format("0x{0:X8}", (uint)grNow.ToInt64()));
        int[] range2 = new int[2];
        GCHandle rp2 = GCHandle.Alloc(range2, GCHandleType.Pinned);
        SendMessageW(hBar, PBM_GETRANGE, IntPtr.Zero, rp2.AddrOfPinnedObject());
        rp2.Free();
        Console.WriteLine("getrange_now_struct imin=" + range2[0] + " imax=" + range2[1]);

        // Clamp re-confirm on the new range 20..150.
        IntPtr posC = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("pos_after_setrange=" + posC.ToInt64());
        SendMessageW(hBar, PBM_SETPOS, new IntPtr(10), IntPtr.Zero);
        IntPtr posC1 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("clamp_lo=" + posC1.ToInt64());
        SendMessageW(hBar, PBM_SETPOS, new IntPtr(500), IntPtr.Zero);
        IntPtr posC2 = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("clamp_hi=" + posC2.ToInt64());

        // STEPIT wrap analysis (step=25, range 20..150).
        IntPtr stepDef = SendMessageW(hBar, PBM_GETSTEP, IntPtr.Zero, IntPtr.Zero);
        IntPtr setStep = SendMessageW(hBar, PBM_SETSTEP, new IntPtr(25), IntPtr.Zero);
        IntPtr stepNow = SendMessageW(hBar, PBM_GETSTEP, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("getstep_def=" + stepDef.ToInt64() + " setstep_prev=" + setStep.ToInt64() + " getstep_now=" + stepNow.ToInt64());

        SendMessageW(hBar, PBM_SETPOS, new IntPtr(100), IntPtr.Zero);
        IntPtr siA = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr spA = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("stepit_from100 prev=" + siA.ToInt64() + " pos=" + spA.ToInt64());

        SendMessageW(hBar, PBM_SETPOS, new IntPtr(140), IntPtr.Zero);
        IntPtr siB = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr spB = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("stepit_from140 prev=" + siB.ToInt64() + " pos=" + spB.ToInt64());

        SendMessageW(hBar, PBM_SETPOS, new IntPtr(150), IntPtr.Zero);
        IntPtr siC = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr spC = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("stepit_from150 prev=" + siC.ToInt64() + " pos=" + spC.ToInt64());

        SendMessageW(hBar, PBM_SETPOS, new IntPtr(35), IntPtr.Zero);
        IntPtr siD = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr spD = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("stepit_from35 prev=" + siD.ToInt64() + " pos=" + spD.ToInt64());

        // Low-side wrap check: pos 20, step 25 -> 20-25 = -5 < 20.
        SendMessageW(hBar, PBM_SETPOS, new IntPtr(20), IntPtr.Zero);
        IntPtr siE = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr spE = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("stepit_from20 prev=" + siE.ToInt64() + " pos=" + spE.ToInt64());

        // Reconfirm: after wrap to 35, next stepit -> 60.
        SendMessageW(hBar, PBM_SETPOS, new IntPtr(35), IntPtr.Zero);
        IntPtr siF = SendMessageW(hBar, PBM_STEPIT, IntPtr.Zero, IntPtr.Zero);
        IntPtr spF = SendMessageW(hBar, PBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("stepit_from35b prev=" + siF.ToInt64() + " pos=" + spF.ToInt64());

        Console.WriteLine("destroyed=" + (DestroyWindow(hBar) != 0));
        Console.WriteLine("parent_destroyed=" + (DestroyWindow(hParent) != 0));
    }
}

