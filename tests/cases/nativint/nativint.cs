using System;
namespace Cases { public static class NativeIntCase {
    public static void Main() {
        IntPtr[] arr = new IntPtr[3];
        arr[0] = new IntPtr(5);
        arr[1] = new IntPtr(0x100000000L);
        arr[2] = new IntPtr(-3);
        Console.WriteLine(arr[0].ToInt64());
        Console.WriteLine(arr[1].ToInt64());
        Console.WriteLine(arr[2].ToInt64());
        arr[1] = new IntPtr(42);
        Console.WriteLine(arr[1].ToInt64());
        UIntPtr[] ua = new UIntPtr[2];
        ua[0] = new UIntPtr(7);
        Console.WriteLine(ua[0].ToUInt64());
        ua[1] = new UIntPtr(0x100000000UL);
        Console.WriteLine(ua[1].ToUInt64());
    }
}}
