using System;
using System.Runtime.InteropServices;
namespace Cases { public static class PinvokeExt {
    // String PARAMETER marshalling (CharSet.Ansi): the runtime transcodes the
    // managed string to a UTF-8 buffer (il2cpp_pinvoke_string_to_utf8_alloc)
    // before the call and frees it after; the native callee sees a
    // null-terminated byte string.
    [DllImport("mylib", EntryPoint = "utf8_byte_len", CharSet = CharSet.Ansi)]
    public static extern int utf8_byte_len(string s);

    // String RETURN value: the native side keeps the reversed text in a
    // static buffer inside the DLL and returns its address. The return is
    // marshaled explicitly with PtrToStringAnsi instead of a string-typed
    // DllImport return: the CLR frees a string-typed native return with
    // CoTaskMemFree, which would corrupt the DLL's static buffer.
    [DllImport("mylib", EntryPoint = "reverse_static", CharSet = CharSet.Ansi)]
    public static extern IntPtr reverse_static(string s);

    // SetLastError demo: a negative code stores an OS error (SetLastError /
    // errno) and returns -1; a non-negative code returns 0 WITHOUT touching
    // the error slot.
    [DllImport("mylib", EntryPoint = "set_error_if_negative", SetLastError = true)]
    public static extern int set_error_if_negative(int code);

    // Explicit pre-call cleanup: zeroes the OS error slot. This mirrors the
    // runtime's il2cpp_pinvoke_clear_last_error -- the pre-call reset the
    // interop layer emits so the post-call capture reflects only what the
    // callee set. NOTE (verified against the net48 reference): .NET
    // Framework does NOT clear the slot before a SetLastError=true call, so
    // without this explicit clear a stale error leaks into the next capture.
    [DllImport("mylib", EntryPoint = "clear_error")]
    public static extern void clear_error();

    public static void Main() {
        // --- String parameter marshalling ---
        Console.WriteLine(utf8_byte_len("hello"));        // 5
        Console.WriteLine(utf8_byte_len(""));             // 0 (empty string)

        // --- String return value (native static buffer -> managed string) ---
        Console.WriteLine(Marshal.PtrToStringAnsi(reverse_static("abc"))); // cba

        // --- SetLastError: capture + pre-call cleanup ---
        Console.WriteLine(set_error_if_negative(-123));  // -1 (error slot = 123)
        Console.WriteLine(Marshal.GetLastWin32Error());  // 123
        Console.WriteLine(set_error_if_negative(7));     // 0 (error slot untouched)
        // 123 -- STALE: .NET Framework does not clear the slot before the
        // call, so the previous 123 leaks into this capture.
        Console.WriteLine(Marshal.GetLastWin32Error());
        clear_error();                                    // explicit pre-call cleanup
        Console.WriteLine(set_error_if_negative(7));     // 0 (error slot untouched)
        // 0 -- the pre-call cleanup zeroed the slot; the callee set nothing,
        // so the post-call capture sees zero (not the stale 123).
        Console.WriteLine(Marshal.GetLastWin32Error());
    }
}}
