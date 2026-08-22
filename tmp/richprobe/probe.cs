using System;
using System.Runtime.InteropServices;

public static class RichProbe
{
    [DllImport("kernel32", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryW(string lpFileName);
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName,
        int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);

    public const int WS_CHILD = 0x40000000;
    public const int ES_MULTILINE = 0x0004;
    public const int WM_SETTEXT = 0x000C;
    public const int WM_GETTEXT = 0x000D;
    public const int WM_GETTEXTLENGTH = 0x000E;
    public const int EM_SETSEL = 0x00B1;
    public const int EM_REPLACESEL = 0x00C2;
    public const int EM_GETCHARFORMAT = 0x043A;
    public const int EM_SETCHARFORMAT = 0x0444;
    public const int EM_SETEVENTMASK = 0x0445;
    public const int ENM_NONE = 0x0000;
    public const int SCF_SELECTION = 0x0001;
    public const int SCF_ALL = 0x0004;
    public const int CFM_COLOR = 0x40000000;
    public const int CFE_AUTOCOLOR = 0x40000000;

    public static void Main()
    {
        IntPtr hLib = LoadLibraryW("riched20.dll");
        Console.WriteLine("lib=" + (hLib != IntPtr.Zero));
        IntPtr hMod = GetModuleHandleW(null);
        IntPtr hParent = CreateWindowExW(0, "STATIC", "parent", 0, 0, 0, 300, 200, IntPtr.Zero, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("parent=" + (hParent != IntPtr.Zero));
        IntPtr hRich = CreateWindowExW(0, "RichEdit20W", "", WS_CHILD | ES_MULTILINE, 0, 0, 200, 100, hParent, IntPtr.Zero, hMod, IntPtr.Zero);
        Console.WriteLine("created=" + (hRich != IntPtr.Zero));

        IntPtr prevMask = SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_NONE));
        Console.WriteLine("prev_mask=" + prevMask.ToInt64());

        IntPtr set28 = Marshal.StringToHGlobalUni("Wf28Rich");
        SendMessageW(hRich, WM_SETTEXT, IntPtr.Zero, set28);
        Marshal.FreeHGlobal(set28);

        IntPtr len28 = SendMessageW(hRich, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("len=" + len28.ToInt64());

        IntPtr gbuf = Marshal.AllocHGlobal(256);
        SendMessageW(hRich, WM_GETTEXT, new IntPtr(256), gbuf);
        Console.WriteLine("text=" + Marshal.PtrToStringUni(gbuf));

        SendMessageW(hRich, EM_SETSEL, new IntPtr(0), new IntPtr(2));
        IntPtr rep = Marshal.StringToHGlobalUni("X");
        IntPtr repRet = SendMessageW(hRich, EM_REPLACESEL, IntPtr.Zero, rep);
        Marshal.FreeHGlobal(rep);
        Console.WriteLine("replaced=" + (repRet != IntPtr.Zero));

        SendMessageW(hRich, WM_GETTEXT, new IntPtr(256), gbuf);
        Console.WriteLine("text2=" + Marshal.PtrToStringUni(gbuf));

        // Color round-trip with SCF_SELECTION (empty selection at caret)
        int[] cf = new int[29];
        cf[0] = 116;
        cf[1] = CFM_COLOR;
        cf[2] = 0;
        cf[5] = 0x00FF0000;
        GCHandle cfPin = GCHandle.Alloc(cf, GCHandleType.Pinned);
        IntPtr cfPtr = cfPin.AddrOfPinnedObject();
        IntPtr setCf = SendMessageW(hRich, EM_SETCHARFORMAT, new IntPtr(SCF_SELECTION), cfPtr);
        Console.WriteLine("setcf_sel=" + setCf.ToInt64());

        int[] cfB = new int[29];
        cfB[0] = 116;
        GCHandle cfBPin = GCHandle.Alloc(cfB, GCHandleType.Pinned);
        IntPtr cfBPtr = cfBPin.AddrOfPinnedObject();
        SendMessageW(hRich, EM_GETCHARFORMAT, new IntPtr(SCF_SELECTION), cfBPtr);
        Console.WriteLine("getcf_sel mask=" + cfB[1].ToString("X8") + " effects=" + cfB[2].ToString("X8") + " color=" + cfB[5].ToString("X8"));
        cfBPin.Free();
        cfPin.Free();

        // Try SCF_ALL
        int[] cf2 = new int[29];
        cf2[0] = 116;
        cf2[1] = CFM_COLOR;
        cf2[2] = 0;
        cf2[5] = 0x00FF0000;
        GCHandle cf2Pin = GCHandle.Alloc(cf2, GCHandleType.Pinned);
        IntPtr cf2Ptr = cf2Pin.AddrOfPinnedObject();
        IntPtr setCf2 = SendMessageW(hRich, EM_SETCHARFORMAT, new IntPtr(SCF_ALL), cf2Ptr);
        Console.WriteLine("setcf_all=" + setCf2.ToInt64());

        int[] cf2B = new int[29];
        cf2B[0] = 116;
        GCHandle cf2BPin = GCHandle.Alloc(cf2B, GCHandleType.Pinned);
        IntPtr cf2BPtr = cf2BPin.AddrOfPinnedObject();
        SendMessageW(hRich, EM_GETCHARFORMAT, new IntPtr(SCF_ALL), cf2BPtr);
        Console.WriteLine("getcf_all mask=" + cf2B[1].ToString("X8") + " effects=" + cf2B[2].ToString("X8") + " color=" + cf2B[5].ToString("X8"));
        cf2BPin.Free();
        cf2Pin.Free();

        // Select all text first, then set color on the selection
        SendMessageW(hRich, EM_SETSEL, new IntPtr(0), new IntPtr(-1));
        int[] cf3 = new int[29];
        cf3[0] = 116;
        cf3[1] = CFM_COLOR;
        cf3[2] = 0;
        cf3[5] = 0x00FF0000;
        GCHandle cf3Pin = GCHandle.Alloc(cf3, GCHandleType.Pinned);
        IntPtr cf3Ptr = cf3Pin.AddrOfPinnedObject();
        IntPtr setCf3 = SendMessageW(hRich, EM_SETCHARFORMAT, new IntPtr(SCF_SELECTION), cf3Ptr);
        Console.WriteLine("setcf_selall=" + setCf3.ToInt64());

        int[] cf3B = new int[29];
        cf3B[0] = 116;
        GCHandle cf3BPin = GCHandle.Alloc(cf3B, GCHandleType.Pinned);
        IntPtr cf3BPtr = cf3BPin.AddrOfPinnedObject();
        SendMessageW(hRich, EM_GETCHARFORMAT, new IntPtr(SCF_SELECTION), cf3BPtr);
        Console.WriteLine("getcf_selall mask=" + cf3B[1].ToString("X8") + " effects=" + cf3B[2].ToString("X8") + " color=" + cf3B[5].ToString("X8"));
        cf3BPin.Free();
        cf3Pin.Free();

        // Two-step event mask: set ENM_CHANGE first, then ENM_NONE
        IntPtr prev1 = SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(1));
        IntPtr prev2 = SendMessageW(hRich, EM_SETEVENTMASK, IntPtr.Zero, new IntPtr(ENM_NONE));
        Console.WriteLine("mask_step1=" + prev1.ToInt64() + " mask_step2=" + prev2.ToInt64());

        Marshal.FreeHGlobal(gbuf);
        DestroyWindow(hRich);
        DestroyWindow(hParent);
    }
}