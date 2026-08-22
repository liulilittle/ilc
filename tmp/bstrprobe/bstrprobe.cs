using System;
using System.Runtime.InteropServices;
using System.Text;
namespace Cases { public static class BstrOracle {
    public static void Main() {
        // StringToBSTR("hello") -> a non-zero BSTR whose raw UTF-16 units are
        // byte-identical to the source string (ReadInt16 byte comparison).
        IntPtr b = Marshal.StringToBSTR("hello");
        Console.WriteLine("bstr_hello_nonzero=" + (b != IntPtr.Zero));
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 5; i++) sb.Append(Marshal.ReadInt16(b, i * 2).ToString("X4"));
        Console.WriteLine("bstr_hello_units=" + sb.ToString());
        Console.WriteLine("bstr_hello_roundtrip=" + (Marshal.PtrToStringUni(b) == "hello"));
        Marshal.FreeBSTR(b);
        // Embedded NUL: StringToBSTR preserves the full length; PtrToStringUni
        // reads to the FIRST NUL (net48 PtrToStringUni semantics) -> "a".
        IntPtr e = Marshal.StringToBSTR("a\0b");
        Console.WriteLine("bstr_emb_roundtrip=" + Marshal.PtrToStringUni(e));
        Marshal.FreeBSTR(e);
        // null three semantics: StringToBSTR(null) -> IntPtr.Zero (0, NOT
        // ArgumentNullException); PtrToStringUni(0) -> null; FreeBSTR(0) no-op.
        Console.WriteLine("bstr_null_zero=" + (Marshal.StringToBSTR(null) == IntPtr.Zero));
        Console.WriteLine("bstr_zero_null=" + (Marshal.PtrToStringUni(IntPtr.Zero) == null));
        // Empty string round-trip.
        IntPtr z = Marshal.StringToBSTR("");
        Console.WriteLine("bstr_empty_roundtrip=" + (Marshal.PtrToStringUni(z) == ""));
        Marshal.FreeBSTR(z);
        Marshal.FreeBSTR(IntPtr.Zero);
        Console.WriteLine("bstr_free_zero_ok=True");
    }
}}