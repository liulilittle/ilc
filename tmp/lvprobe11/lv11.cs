using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace Cases { public static class LvProbe11 {
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint="CreateWindowExW", CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string n);
    public const int LVM_FIRST = 0x1000;
    public const int LVM_INSERTITEMW = LVM_FIRST + 77;
    public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    public const int LVIF_TEXT = 1;
    public const int WS_CHILD = unchecked((int)0x40000000);
    public const int WS_OVERLAPPED = 0;
    public const int LVS_REPORT = 1;
    public static void Main() {
        // WinForms ListView as the environment sanity check
        Form frm = new Form();
        ListView lv = new ListView();
        lv.View = View.Details;
        lv.Columns.Add("Col", 100);
        lv.Items.Add("Alpha");
        lv.Items.Add("Beta");
        frm.Controls.Add(lv);
        frm.CreateControl();
        Console.WriteLine("wf_count=" + lv.Items.Count);
        Console.WriteLine("wf_text=" + lv.Items[0].Text);
        // raw handle now
        IntPtr h = lv.Handle;
        Console.WriteLine("raw_count=" + SendMessageW(h, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        // raw insert into the SAME control
        IntPtr tp = Marshal.StringToHGlobalUni("Gamma");
        int[] lvi = new int[22];
        long p = tp.ToInt64();
        lvi[0] = LVIF_TEXT; lvi[1] = -1; lvi[6] = (int)(p & 0xFFFFFFFFL); lvi[7] = (int)((p >> 32) & 0xFFFFFFFFL); lvi[8] = 128;
        GCHandle pin = GCHandle.Alloc(lvi, GCHandleType.Pinned);
        IntPtr r = SendMessageW(h, LVM_INSERTITEMW, IntPtr.Zero, pin.AddrOfPinnedObject());
        pin.Free();
        Console.WriteLine("raw_insert=" + r.ToInt64());
        Console.WriteLine("raw_count2=" + SendMessageW(h, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64());
        lv.Dispose(); frm.Dispose();
    }
}}
