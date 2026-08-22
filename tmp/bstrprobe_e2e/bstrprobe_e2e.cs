using System;
using System.Runtime.InteropServices;
namespace Cases { public static class BstrCase {
    public static void Main() {
        // StringToBSTR("hello") -> a non-zero BSTR; PtrToStringUni decodes it
        // back to "hello" (the net48 BSTRToString semantic); FreeBSTR releases.
        IntPtr b = Marshal.StringToBSTR("hello");
        Console.WriteLine("bstr_hello_nonzero=" + (b != IntPtr.Zero));
        Console.WriteLine("bstr_hello_roundtrip=" + (Marshal.PtrToStringUni(b) == "hello"));
        Marshal.FreeBSTR(b);
        // Embedded NUL: StringToBSTR preserves the full length; PtrToStringUni
        // reads to the FIRST NUL (net48 PtrToStringUni semantics) -> "a".
        IntPtr e = Marshal.StringToBSTR("a\0b");
        Console.WriteLine("bstr_emb_roundtrip=" + Marshal.PtrToStringUni(e));
        Marshal.FreeBSTR(e);
        // null semantics: StringToBSTR(null) -> IntPtr.Zero (0, NOT
        // ArgumentNullException); FreeBSTR(0) no-op. (BSTRToString(0) -> null
        // is pinned by the runtime test -- the net48-equivalent PtrToStringUni
        // has a pre-existing null->empty-string divergence, out of this
        // batch's scope.)
        Console.WriteLine("bstr_null_zero=" + (Marshal.StringToBSTR(null) == IntPtr.Zero));
        // Empty string round-trip.
        IntPtr z = Marshal.StringToBSTR("");
        Console.WriteLine("bstr_empty_roundtrip=" + (Marshal.PtrToStringUni(z) == ""));
        Marshal.FreeBSTR(z);
        Marshal.FreeBSTR(IntPtr.Zero);
        Console.WriteLine("bstr_free_zero_ok=True");
    }
}}