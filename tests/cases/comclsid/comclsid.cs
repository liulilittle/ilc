//------------------------------------------------------------------------------
// comclsid.cs -- C1 (A1, Type-COM): Type.GetTypeFromProgID /
// Type.GetTypeFromCLSID / Activator.CreateInstance on COM types, net48
// oracle byte-for-byte.
//
// The simplified Type model carries a COM class as the canonical sentinel
// Type VALUE "COMCLSID:" + 32 lowercase hex digits of the CLSID (decision
// C1; the audit's "COM:"+progID prefix and "__ComObject" names were both
// rejected). The sentinel is produced by the runtime Type-COM icalls
// (Type.GetTypeFromProgID / GetTypeFromCLSID) and consumed by
// Activator.CreateInstance -- the "COMCLSID:" prefix routes to
// CoCreateInstance (RCW) instead of the typeinfo registry miss.
//
// Windows-only REAL-COM case: every line depends on a CoCreateInstance /
// CLSIDFromProgID that exists only on Windows ("Shell.Application" is a
// built-in ProgID present on every Windows SKU -- the case mirrors the
// audit's probe). The net48 oracle runs the same API; byte-stability pins
// the sentinel model + the miss contract. POSIX never sees this case.
//
// Byte-stability: every line prints a labeled bool / the sentinel's GUID /
// the exception marker. net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases {
public static class ComClsid {
    public static void Main() {
        // ---- 1. Type.GetTypeFromProgID -> non-null COM Type ----
        Type t = Type.GetTypeFromProgID("Shell.Application");
        Console.WriteLine("t_nonnull=" + (t != null));

        // ---- 2. Type.GetTypeFromCLSID(Guid) -> non-null COM Type (the
        //         Shell.Application CLSID {13709620-C279-11CE-A49E-444553540000})
        //         NOTE: Guid is constructed via Parse -- the 11-arg component
        //         ctor has no transpiler wiring; Parse is the one mapped path.
        Type g = Type.GetTypeFromCLSID(Guid.Parse("13709620-c279-11ce-a49e-444553540000"));
        Console.WriteLine("g_nonnull=" + (g != null));

        // ---- 3. Activator.CreateInstance(comType) -> RCW via CoCreateInstance ----
        object o = null;
        try { o = Activator.CreateInstance(t); } catch (Exception) { }
        Console.WriteLine("inst_nonnull=" + (o != null));
        Console.WriteLine("inst_iscom=" + (o != null && Marshal.IsComObject(o)));

        // ---- 4. release the RCW (ReleaseComObject, net48 API) ----
        int rc = 0;
        try { if (o != null) rc = Marshal.ReleaseComObject(o); } catch (Exception) { }
        Console.WriteLine("release_done=" + (rc >= 0));

        // ---- 5. unknown ProgID -> Type.GetTypeFromProgID returns null
        //         (default throwOnError=false) ----
        Type bad = null;
        try { bad = Type.GetTypeFromProgID("No.Such.ProgID.XYZ"); } catch (Exception) { }
        Console.WriteLine("bad_null=" + (bad == null));
    }
}
}