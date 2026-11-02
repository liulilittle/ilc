//------------------------------------------------------------------------------
// pinvoke-com-byval.cs -- a BYVAL [ComImport]-interface P/Invoke parameter.
//
// MEASURED net48 marshalling contract (com-verify NOTES section 3(d), case
// F13): a byval COM-interface argument is passed AddRef'd for the DURATION of
// the call and Released afterwards -- the callee observes refs = n+1 on entry
// and on exit, and the post-call count is back at n. The transpiler reproduces
// this with
//     il2cpp_pinvoke_com_arg(il2cpp_com_rcw_borrow_iunknown(obj))
// (borrow the RCW's wrapped pointer WITHOUT AddRef; the RAII holder owns the
// per-call +1/-1 pair).
//
// WHY A NATIVE FIXTURE INSTEAD OF AN OS COM OBJECT: this stage has no out/ref
// COM parameter, so there is no way to obtain an OS object through a P/Invoke
// signature. The fixture instead builds its OWN COM object (a real
// IUnknown + one interface method vtable entry) and the managed side wraps the
// raw pointer with Marshal.GetObjectForIUnknown -- an RCW exactly like the
// net48 case. The fixture reports the RAW refcount and the counts it sees at
// the callee entry/exit; every printed record is a DELTA against the count
// measured just before the call, so the oracle and this runtime agree even
// though Marshal.GetObjectForIUnknown's own AddRef policy differs (measured
// divergence, com-verify NOTES section 6).
//
// The [Guid] below is the IID the fixture's QueryInterface answers. The
// interface is declared INSIDE this assembly with [ComImport] +
// [InterfaceType(InterfaceIsIUnknown)], which is the shape the transpiler's
// guard accepts (same-assembly TypeDef, Interface | Import, readable [Guid]).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("8F2A1B3C-4D5E-6F70-8192-A3B4C5D6E7F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITestCom
{
    int GetValue();
}

public static class PinvokeComByval
{
    [DllImport("combyvalprobe.dll")]
    private static extern IntPtr com_create();

    [DllImport("combyvalprobe.dll")]
    private static extern int probe_byval(ITestCom obj);

    // Negative record: the COM parameter coexists with a CharSet.Unicode
    // string and an int32 -- the COM arm must not disturb the other
    // marshalling arms (or the reverse).
    [DllImport("combyvalprobe.dll", CharSet = CharSet.Unicode)]
    private static extern int probe_mixed(ITestCom obj, int x, string s);

    [DllImport("combyvalprobe.dll")]
    private static extern int com_refcount();

    [DllImport("combyvalprobe.dll")]
    private static extern int com_entry();

    [DllImport("combyvalprobe.dll")]
    private static extern int com_exit();

    [DllImport("combyvalprobe.dll")]
    private static extern int com_was_null();

    public static void Main()
    {
        IntPtr raw = com_create();
        object rcw = Marshal.GetObjectForIUnknown(raw);
        ITestCom typed = (ITestCom)rcw;

        // ---- 1. a successful byval call returns GetValue() -----------------
        int baseRefs = com_refcount();
        int value = probe_byval(typed);
        Record("call.ret", value);
        Record("call.entry.delta", com_entry() - baseRefs);
        Record("call.exit.delta", com_exit() - baseRefs);
        Record("call.after.delta", com_refcount() - baseRefs);

        // ---- 2. the same RCW called twice: each call is independently +1/-1
        int base2 = com_refcount();
        probe_byval(typed);
        Record("two.first.entry.delta", com_entry() - base2);
        Record("two.first.after.delta", com_refcount() - base2);
        int base3 = com_refcount();
        probe_byval(typed);
        Record("two.second.entry.delta", com_entry() - base3);
        Record("two.second.after.delta", com_refcount() - base3);

        // ---- 3. a null interface argument: net48 passes NULL, no AddRef ----
        int baseNull = com_refcount();
        int nullRet = probe_byval(null);
        Record("null.ret", nullRet);
        Record("null.was.null", com_was_null());
        Record("null.after.delta", com_refcount() - baseNull);

        // ---- 4. the COM param coexists with CharSet.Unicode string + int32 -
        int baseMix = com_refcount();
        int mixed = probe_mixed(typed, 5, "abc");
        Record("mixed.ret", mixed);
        Record("mixed.after.delta", com_refcount() - baseMix);

        // ---- 5. a released RCW: the documented net48 behaviour is measured
        //         on the oracle FIRST (do not assume) ------------------------
        Marshal.ReleaseComObject(rcw);
        try
        {
            int releasedRet = probe_byval(typed);
            Record("released.ret", releasedRet);
        }
        catch (Exception e)
        {
            Record("released.ex", e.GetType().Name);
        }
    }

    private static void Record(string id, int value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }

    private static void Record(string id, string value)
    {
        Console.WriteLine("[" + id + "]=" + value);
    }
}
