using System;
using System.Runtime.InteropServices;
public class Probe46b
{
    [DllImport("ole32", EntryPoint = "OleInitialize")]
    public static extern int OleInitialize(IntPtr pvReserved);
    [DllImport("ole32", EntryPoint = "OleUninitialize")]
    public static extern void OleUninitialize();
    [DllImport("ole32", EntryPoint = "RevokeDragDrop")]
    public static extern int RevokeDragDrop(IntPtr hwnd);
    public static int Main()
    {
        int r1 = OleInitialize(IntPtr.Zero);
        int r2 = RevokeDragDrop(IntPtr.Zero);
        Console.WriteLine("ole_init=" + r1);
        Console.WriteLine("revoke_null=" + r2);
        OleUninitialize();
        return 0;
    }
}
