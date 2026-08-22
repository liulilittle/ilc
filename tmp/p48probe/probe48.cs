using System;
using System.Runtime.InteropServices;
public class Probe48c
{
    public delegate int CompareFn(int l1, int l2, int sort);
    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr lp);
    [DllImport("user32", EntryPoint = "DestroyWindow")]
    public static extern int DestroyWindow(IntPtr hwnd);
    [DllImport("user32", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct ICCEX { public int cbSize; public int dwICC; }
    [DllImport("comctl32", EntryPoint = "InitCommonControlsEx")]
    public static extern int InitCommonControlsEx(ref ICCEX icc);
    public static int Callbacks = 0;
    public static int CompareCall(int l1, int l2, int sort) { Callbacks++; return l1.CompareTo(l2); }
    public static void Main()
    {
        ICCEX icc = new ICCEX(); icc.cbSize = 8; icc.dwICC = 0x4 | 0x8;
        int ic = InitCommonControlsEx(ref icc);
        Console.WriteLine("icc_ret=" + ic);
        IntPtr inst = Marshal.GetHINSTANCE(typeof(Probe48c).Module);
        IntPtr root = CreateWindowExW(0, null, "r", 0xC00000, 0, 0, 400, 300, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        Console.WriteLine("root=" + (root != IntPtr.Zero));
        IntPtr list = CreateWindowExW(0, "SysListView32", null, 0x54000001, 0, 0, 300, 200, root, IntPtr.Zero, inst, IntPtr.Zero);
        Console.WriteLine("list=" + (list != IntPtr.Zero));
        IntPtr colText = Marshal.StringToHGlobalUni("Val");
        int[] col = new int[13]; col[0] = 0x4; col[6] = (int)colText;
        IntPtr colPin = Marshal.AllocHGlobal(52);
        Marshal.Copy(col, 0, colPin, 13);
        SendMessageW(list, 0x1061, new IntPtr(0), colPin); // LVM_INSERTCOLUMNW
        for (int i = 0; i < 3; i++) {
            IntPtr t = Marshal.StringToHGlobalUni("r" + i);
            int[] lvi = new int[14]; lvi[0] = 0x1; lvi[1] = i; lvi[10] = (int)t; lvi[11] = 1; lvi[13] = new int[]{2,0,1}[i];
            IntPtr lviPin = Marshal.AllocHGlobal(56);
            Marshal.Copy(lvi, 0, lviPin, 14);
            SendMessageW(list, 0x1073, IntPtr.Zero, lviPin); // LVM_INSERTITEMW
            IntPtr lbl = Marshal.StringToHGlobalUni("lbl" + i);
            int[] s = new int[14]; s[0] = 0x1; s[1] = i; s[10] = (int)lbl; s[11] = 4; s[13] = new int[]{2,0,1}[i];
            IntPtr sPin = Marshal.AllocHGlobal(56);
            Marshal.Copy(s, 0, sPin, 14);
            SendMessageW(list, 0x1074, IntPtr.Zero, sPin); // LVM_SETITEMW
            Marshal.FreeHGlobal(sPin);
        }
        IntPtr header = SendMessageW(list, 0x101F, IntPtr.Zero, IntPtr.Zero);
        Console.WriteLine("header_nz=" + (header != IntPtr.Zero));
        IntPtr fn = Marshal.GetFunctionPointerForDelegate(new CompareFn(CompareCall));
        IntPtr r = SendMessageW(header, 0x1230, IntPtr.Zero, fn);
        Console.WriteLine("sort_ret=" + r.ToInt64() + " cb=" + Callbacks);
        for (int i = 0; i < 3; i++) {
            int[] g = new int[14]; g[0] = 0x1; g[1] = i; g[13] = -1;
            IntPtr gPin = Marshal.AllocHGlobal(56);
            Marshal.Copy(g, 0, gPin, 14);
            SendMessageW(list, 0x1077, new IntPtr(i), gPin);
            Marshal.Copy(gPin, g, 0, 14);
            Console.WriteLine("item" + i + " lParam=" + g[13]);
        }
        DestroyWindow(list); DestroyWindow(root);
    }
}
