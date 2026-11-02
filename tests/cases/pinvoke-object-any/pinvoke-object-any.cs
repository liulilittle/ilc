//------------------------------------------------------------------------------
// pinvoke-object-any.cs -- [MarshalAs(UnmanagedType.AsAny)] object P/Invoke
// parameters.
//
// MEASURED on the net48 oracle (executable probes in .opencode/tmp/
// impl-pinvoke-object/phase0/oracle -- a raw byte dump, an INTEGER-typed
// receiver that distinguishes "value" from "address", and a real
// user32!CreateWindowEx call whose WndProc inspects
// CREATESTRUCT.lpCreateParams):
//
//   * the callee receives the ADDRESS OF A NATIVE TEMP COPY of the boxed
//     value's unmanaged image -- NOT the value itself, NOT a VARIANT and NOT
//     the managed box. (CreateWindowEx: lpCreateParams is that temp address
//     and *lpCreateParams is the boxed IntPtr value.)
//   * a boxed value type arrives as its field bytes at offset 0 of the temp.
//   * null -> NULL.
//   * [In, Out] copies the temp BACK into the MANAGED BOX after the call;
//     [In] alone (and the default, no bits) does NOT.
//
// The fixture asserts exactly those bytes, so "pass the boxed value directly"
// (a wrong reading of the raw dump) and a VARIANT marshaler both turn these
// records red. The copy-back row holds the BOX in a local so the write-back is
// observable (a temporary box would simply be discarded by both sides).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct AnyPair
{
    public int a;
    public int b;
    public long c;
}

public static class ObjectAnyProbe
{
    [DllImport("anyprobe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int any_null([MarshalAs(UnmanagedType.AsAny)] object o);

    [DllImport("anyprobe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int any_struct([MarshalAs(UnmanagedType.AsAny)] object o,
        int a, int b, long c);

    [DllImport("anyprobe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int any_struct_write([MarshalAs(UnmanagedType.AsAny)] object o);

    [DllImport("anyprobe.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int any_struct_write_io(
        [In, Out][MarshalAs(UnmanagedType.AsAny)] object o);

    public static void Main()
    {
        // 1. null -> NULL.
        Console.WriteLine("[asany.null]=" + any_null(null));

        AnyPair pair = new AnyPair();
        pair.a = 0x11223344;
        pair.b = 0x55667788;
        pair.c = 0x1122334455667788L;

        // 2. boxed struct: the field bytes are readable through the temp.
        Console.WriteLine("[asany.struct]=" + any_struct((object)pair,
            0x11223344, 0x55667788, 0x1122334455667788L));

        // 3/4 hold the BOX so a native write-back is observable.
        object box = (object)pair;

        // 3. no [Out]: a native write to the temp does NOT reach the box.
        Console.WriteLine("[asany.write.n]=" + any_struct_write(box));
        Console.WriteLine("[asany.write.a0]=" + ((AnyPair)box).a);
        Console.WriteLine("[asany.write.b0]=" + ((AnyPair)box).b);

        // 4. [In, Out]: the native write IS copied back into the box.
        Console.WriteLine("[asany.write.io.n]=" + any_struct_write_io(box));
        Console.WriteLine("[asany.write.a1]=" + ((AnyPair)box).a);
        Console.WriteLine("[asany.write.b1]=" + ((AnyPair)box).b);
    }
}
