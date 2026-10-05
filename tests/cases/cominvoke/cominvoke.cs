//------------------------------------------------------------------------------
// cominvoke.cs -- C2 (A2): Type.InvokeMember late-bound IDispatch invocation
// on a REAL COM RCW, net48 oracle byte-for-byte (Windows-only).
//
// The C2 surface: icall_com_invoke_member (Type.InvokeMember on a COM Type)
// does the ONE-CALL protocol -- GetIDsOfNames + DISPPARAMS (positional args,
// VT_I4/VT_R8/VT_BSTR/bool subset) + Invoke (DISPATCH_METHOD |
// DISPATCH_PROPERTYGET) + the result-VARIANT decode; property-PUT is LOUD by
// design. Marshal.GetIDispatchForObject leaves the COM LOUD set.
//
// "Shell.Application" is a built-in ProgID on every Windows SKU (shell32's
// automation object). Deterministic probes only -- every line is a labeled
// bool so a diff shows exactly which assertion flipped:
//   * Activator.CreateInstance(Type.GetTypeFromProgID) -> a live RCW
//     (IsComObject true), the C1 chain.
//   * InvokeMember("MinimizeAll", InvokeMethod): a VOID method -> null.
//   * InvokeMember("Name", GetProperty): a BSTR propertyget -> a string.
//   * an unknown member -> an exception (net48 dispatch miss; both sides
//     throw -- the bool does not depend on the exact exception type).
//   * GetIDispatchForObject(rcw) -> a non-null IntPtr (the C2 LOUD move).
// Note the shell windows minimize briefly during the probe (MinimizeAll).
//------------------------------------------------------------------------------
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Cases {
public static class ComInvoke {
    static bool ThrowsAny(Action a) {
        try { a(); return false; } catch (Exception) { return true; }
    }

    public static void Main() {
        // ---- 1. create a live Shell RCW (the C1 chain) ----
        Type ct = null;
        object shell = null;
        try { ct = Type.GetTypeFromProgID("Shell.Application"); } catch (Exception) { }
        try { if (ct != null) shell = Activator.CreateInstance(ct); } catch (Exception) { }
        Console.WriteLine("shell_nonnull=" + (shell != null));
        Console.WriteLine("shell_iscom=" + (shell != null && Marshal.IsComObject(shell)));

        // ---- 2. Method invocation: MinimizeAll() is void -> null ----
        Type t = shell != null ? shell.GetType() : typeof(object);
        object r0 = null;
        try { r0 = t.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, new object[0]); } catch (Exception) { }
        Console.WriteLine("invoke_null=" + (r0 == null));

        // ---- 3. Propertyget: Name is a BSTR -> a managed string ----
        object r1 = null;
        try { r1 = t.InvokeMember("Name", BindingFlags.GetProperty, null, shell, new object[0]); } catch (Exception) { }
        Console.WriteLine("propget_string=" + (r1 is string));

        // ---- 4. An unknown member -> an exception on both sides ----
        Console.WriteLine("unknown_throws=" + ThrowsAny(
            () => t.InvokeMember("NoSuchMemberXYZ", BindingFlags.InvokeMethod, null, shell, null)));

        // ---- 5. GetIDispatchForObject leaves the LOUD set (C2) ----
        IntPtr disp = IntPtr.Zero;
        try { if (shell != null) disp = Marshal.GetIDispatchForObject(shell); } catch (Exception) { }
        Console.WriteLine("disp_nonzero=" + (disp != IntPtr.Zero));

        // ---- 6. release the RCW (net48 API) ----
        int rc = -1;
        try { if (shell != null) rc = Marshal.ReleaseComObject(shell); } catch (Exception) { }
        Console.WriteLine("release_done=" + (rc >= 0));
    }
}
}