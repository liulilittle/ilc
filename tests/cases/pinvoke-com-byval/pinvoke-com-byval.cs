//------------------------------------------------------------------------------
// pinvoke-com-byval.cs -- a BYVAL [ComImport]-interface P/Invoke parameter, and
// the RCW -> COM-interface castclass semantics.
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
// CASTCLASS: casting an RCW to a [ComImport] interface is a QueryInterface in
// net48. A WRONG interface must throw InvalidCastException -- an identity
// reinterpret-cast would silently let a later byval call hand native code the
// wrong vtable. The runtime helper (il2cpp_com_cast_to_interface) emits the
// typed-wrap (QI); this case pins both the good and the bad cast on the oracle.
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
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("8F2A1B3C-4D5E-6F70-8192-A3B4C5D6E7F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITestCom
{
    int GetValue();
}

// A DIFFERENT interface the fixture object does NOT implement: casting an
// ITestCom RCW to it must throw InvalidCastException (net48 QueryInterface).
[ComImport, Guid("0BADF00D-1111-2222-3333-444455556666"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IWrongCom
{
    int Other();
}

public static class PinvokeComByval
{
    [DllImport("combyvalprobe.dll")]
    private static extern IntPtr com_create();

    [DllImport("combyvalprobe.dll")]
    private static extern IntPtr com_create2();

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


        // ---- 4b. a cast to an interface the object DOES implement returns
        //          the SAME RCW (net48 identity: ReferenceEquals is true) ----
        ITestCom sameAgain = (ITestCom)rcw;
        Record("cast.identity", object.ReferenceEquals(rcw, sameAgain) ? 1 : 0);
        // ---- 5. casting the RCW to an interface it does NOT implement must
        //         throw InvalidCastException (net48 QueryInterface) ----------
        try
        {
            var bad = (IWrongCom)rcw;
            Record("badcast.ret", bad.Other());
        }
        catch (Exception e)
        {
            Record("badcast.ex", e.GetType().Name);
        }

        // ---- 6. an already-typed RCW (GetTypedObjectForIUnknown) byval ------
        object typedObj = Marshal.GetTypedObjectForIUnknown(raw, typeof(ITestCom));
        ITestCom typed2 = (ITestCom)typedObj;
        int baseTyped = com_refcount();
        int typedRet = probe_byval(typed2);
        Record("typed.ret", typedRet);
        Record("typed.entry.delta", com_entry() - baseTyped);
        Record("typed.after.delta", com_refcount() - baseTyped);

        // ---- 7. a released RCW: measured on the oracle FIRST. A FRESH,
        //         independent object (com_create2) and a SINGLE reference: the
        //         ReleaseComObject below kills the wrapper we then pass, so
        //         both net48 and this runtime throw InvalidComObjectException
        //         (do not assume -- the oracle pins it). Using object 0 here
        //         would keep it alive through the typed / badcast references.
        IntPtr raw2 = com_create2();
        object rcw2 = Marshal.GetObjectForIUnknown(raw2);
        ITestCom typedReleased = (ITestCom)rcw2;
        Marshal.ReleaseComObject(typedReleased);
        try
        {
            int releasedRet = probe_byval(typedReleased);
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
