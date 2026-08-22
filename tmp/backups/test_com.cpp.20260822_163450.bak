//------------------------------------------------------------------------------
// test_com.cpp -- COM interop runtime unit tests (see il2cpp_com.h).
//
// Coverage (normal + edge + error paths):
//   1. HRESULT / IID constants sanity (IUnknown GUID bytes).
//   2. CCW -- portable, tested on EVERY platform (pure memory + GUID compare):
//        create(managed) -> non-zero IUnknown pointer; QueryInterface for
//        IID_IUnknown returns S_OK and the SAME interface pointer; QI for a
//        random IID returns E_NOINTERFACE; AddRef -> refcount 2, Release ->
//        1, Release -> 0 (destroys); the generic vtable-slot invocation
//        (slot 0 = QI) reaches the same QI through
//        icall_com_call_vtable_slot; the CCW keeps its managed target rooted
//        (GCHandle target == the wrapped object).
//   3. RCW (Windows): create_object("Shell.Application") -- a ProgID always
//        registered on Windows -- yields an RCW; IsComObject true; the
//        GetIUnknownForObject / GetObjectForIUnknown / ReleaseComObject
//        round-trip; a released RCW's later COM op throws
//        InvalidComObjectException; GetTypeFromProgID returns S_OK + the
//        Shell CLSID; an unregistered ProgID yields REGDB_E_CLASSNOTREG;
//        GetActiveObject on an unregistered ProgID throws the COMException
//        chain. (The registered-but-not-running GetActiveObject MK_E path is
//        environment-dependent -- an active Shell.Application on the test
//        machine would satisfy it -- so only the deterministic unregistered
//        path is asserted.)
//   4. STUB (non-Windows): every Windows-gated entry returns the documented
//        stub -- GetTypeFromProgID -> E_NOTIMPL; CreateInstance / CreateObject
//        / GetActiveObject -> NotSupportedException; no crash, explicit error
//        (the netcore Marshal.Unix.cs contract).
//   5. Bounds/arg defense (everywhere): query_interface(null pUnk) ->
//        ArgumentNullException; call_vtable_slot(iface 0) ->
//        ArgumentNullException, negative/oversized slot -> AOOException, and a
//        slot past the CCW's 3-slot IUnknown vtable -> AOOException (OOB
//        guard); GetIUnknownForObject(null) -> ArgumentNullException;
//        IsComObject null -> ArgumentNullException.
//   6. net48 GetIUnknownForObject on a NON-RCW managed object: creates a CCW
//        and returns its IUnknown (identity QI, caller's Release destroys it)
//        -- the coreclr GetComIPFromObjectRef managed-object path.
//   7. IDispatch (phase 2 -- every platform, CCW shape): a CCW QIs for
//        IID_IDispatch (same identity pointer -- the single 7-slot vtable
//        carries IUnknown + IDispatch); GetTypeInfoCount reports 0 (no type
//        library); GetIDsOfNames resolves names to dispIds deterministically
//        (first name -> 1, cached, second -> 2) through the per-CCW registry;
//        Invoke validates dispId (DISP_E_MEMBERNOTFOUND when unregistered),
//        rejects named arguments (DISP_E_BADPARAMCOUNT), rejects a null rgvarg
//        for a non-zero arg count (E_POINTER), validates argument VARIANT
//        types (an unsupported type -> DISP_E_TYPEMISMATCH) and writes the
//        zeroed VT_EMPTY return VARIANT into a provided pVarResult; the OOB
//        guard now protects the 7-slot vtable (slot 7 -> AOOException). The
//        tests drive the CCW vtable exactly as a native automation host does
//        (read the slot and call it).
//   11. Type library evaluation (phase 6 -- every platform, CCW shape):
//        GetTypeInfo (vtable slot 4) exposes NO ITypeInfo -- TYPE_E_
//        ELEMENTNOTFOUND with a null out pointer for every iTInfo index (the
//        documented simplification: the CCW member registry is name -> dispId
//        only, so a synthesized TYPEATTR/FUNCDESC would be fabricated
//        metadata); a null ppTInfo -> E_POINTER (arg defense, mirroring
//        GetTypeInfoCount). This pins the late-bound-only contract an
//        automation host sees after GetTypeInfoCount == 0.
//   8. IDispatch (Windows): the RCW-direction icalls
//        (idispatch_get_ids_of_names / idispatch_invoke) forwarded against a
//        CCW exercise the full native dispatch path deterministically; a real
//        Shell.Application RCW QIs for IDispatch (real automation object
//        shape); Marshal.GetIDispatchForObject returns a CCW IDispatch for a
//        managed object and the native IDispatch for an RCW. 8b (phase 7 --
//        typed invocation): Invoke driven with a DISPPARAMS whose rgvarg is a
//        16-byte VARIANT array accepts VT_I4 / VT_R8 / VT_BSTR arguments
//        (S_OK), rejects an unsupported type (VT_DATE ->
//        DISP_E_TYPEMISMATCH) and writes the zeroed VT_EMPTY return VARIANT
//        into pVarResult. Non-Windows: the two icalls return E_NOTIMPL (the
//        POSIX stub); GetIDispatchForObject on a non-RCW still works (the CCW
//        path is every-platform).
//   9. IOleObject / IOleClientSite (phase 4, Windows): the multi-vtable CCW.
//        QI for IID_IOleObject returns a DISTINCT interface pointer (the OLE
//        view at &ccw->oleVtbl -- a separate 24-slot vtable, NOT the identity
//        vtable); QI for IUnknown from the OLE view returns the base identity
//        address (COM QI identity rule). The minimal hosting lifecycle works:
//        SetClientSite stores the site / GetClientSite returns it / Close
//        clears it / DoVerb accepts activation (S_OK) / Update + IsUpToDate
//        succeed. IOleClientSite QI returns the site view; GetContainer
//        returns E_NOTIMPL with a null out pointer (no container). The OOB
//        guard now knows all three CCW vtable lengths (identity 7, IOleObject
//        24, IOleClientSite 9). Non-Windows: QI for the OLE IIDs returns
//        E_NOINTERFACE (the POSIX stub -- the OLE views are Windows-only).
//  10. IConnectionPointContainer / IConnectionPoint (phase 5, Windows): the
//        event-source views. register_connection_point declares an event
//        interface IID on the CCW (idempotent; IID_NULL/IUnknown rejected);
//        QI for IConnectionPointContainer returns a distinct container view;
//        FindConnectionPoint returns the connection point view for a
//        registered IID (CONNECT_E_NOCONNECTION + null out otherwise);
//        GetConnectionInterface returns the event IID; GetConnectionPoint
//        Container returns the container; Advise QIs the sink (valid sink ->
//        S_OK + cookie, a sink without the interface -> CONNECT_E_CANNOT
//        CONNECT, the stage-9 multi-sink extension: a connection point holds
//        up to kCcwMaxSinksPerCp sinks, each a distinct monotonic cookie) and
//        Unadvise releases the matching connection (stale cookie ->
//        CONNECT_E_NOCONNECTION); the OOB guard
//        knows the 5/8-slot lengths; destroying the CCW releases a
//        still-connected sink. Non-Windows: QI for the two IIDs returns
//        E_NOINTERFACE and register_connection_point returns E_NOTIMPL (the
//        POSIX stub).
//  11. Event-source fire (stage 7, Windows): icall_com_ccw_fire_event invokes
//        the Advise'd sink's event-interface method at vtable slot 3 +
//        methodIndex with the intptr carriers a0..a3. No connection point ->
//        S_FALSE (no listener); registered-but-unconnected -> S_FALSE;
//        connected sink -> the sink's method HRESULT (no AddRef -- the
//        reference count is observation-only). Fires route by the event
//        interface IID across two registered interfaces on one CCW (sinkA /
//        sinkB isolation) and across two CCWs (dual-CCW isolation); the four
//        carriers pass through byte-exact; Unadvise -> S_FALSE again; three
//        consecutive fires deliver exactly three calls; null ccw/iid ->
//        ArgumentNullException, methodIndex -1 / 125 ->
//        ArgumentOutOfRangeException. Non-Windows: ccw_fire_event returns
//        E_NOTIMPL (the POSIX stub).
//  11b. Event-source dispatch fire (stage 8, Windows): the IDispatch VARIANT
//        fire variant icall_com_ccw_fire_dispatch_event delivers an event to
//        an IDispatch-shaped automation sink by calling its IDispatch::Invoke
//        (vtable slot 6) with DISPID_FIRST + n and a DISPPARAMS carrying
//        VARIANTs (the WinForms AxHost automation-sink shape). No connection
//        point -> S_FALSE (no listener); registered-but-unconnected -> S_FALSE;
//        connected sink -> the sink's Invoke HRESULT (no AddRef -- observation
//        only). DISPID_FIRST and DISPID_FIRST + 1 reach the sink with the
//        dispId recorded; VT_I4 and two-argument (reverse-order) DISPPARAMS
//        pass through byte-exact; a pVarResult carrier is forwarded (the sink
//        writes its return into it); fires route by the event interface IID
//        across two registered interfaces; Unadvise -> S_FALSE again; null
//        ccw / null iid / null pDispParams -> ArgumentNullException.
//        Non-Windows: ccw_fire_dispatch_event returns E_NOTIMPL (the POSIX
//        stub).
//  12. IEnumVARIANT (stage 10 -- every platform): the CCW exposes an inlined
//        enumeration view (&ccw->enumView) whose vtable is 7 slots
//        (IUnknown + Next/Skip/Reset/Clone). icall_com_ccw_enum_register_item
//        registers 16-byte VARIANT value copies of the VT_EMPTY/VT_NULL/VT_I4/
//        VT_R8 subset (VT_BSTR and any other type -> E_INVALIDARG; a full
//        kCcwMaxEnumItems = 8 registry -> E_OUTOFMEMORY; null ccw/variant ->
//        ArgumentNullException). QI for IEnumVARIANT returns a distinct view
//        that QIs back to the identity; Next returns S_OK with byte-exact
//        items, S_FALSE past the end (pceltFetched still written), E_POINTER
//        on a null rgelt; Skip clamps + returns S_FALSE; Reset rewinds; Clone
//        hands out a heap view sharing the CCW refcount with an independent
//        cursor (releasing it deletes the heap block); the generic
//        vtable-slot invocation drives the same view (RCW direction, zero new
//        icalls) and the OOB guard now knows the 7-slot enum vtable.
//        Non-Windows: the whole stage-10 surface is every-platform (no ole32
//        dependency) -- 4e exercises it in the POSIX stub block.
//  13. IEnumVARIANT BSTR items (stage 11 -- the oleaut32 ownership extension,
//        WINDOWS only): icall_com_ccw_enum_register_bstr_item SysAllocStringLen's
//        a copy of the managed string (oleaut32 resolved at runtime, kept OUT
//        of GetOle32().ok) and the CCW OWNS that BSTR (SysFreeString on
//        teardown); Next hands the caller a FRESH SysAllocStringLen copy each
//        fetch -- the OLE contract (the caller VariantClear's the returned
//        element; the stored master survives, proven by freeing the returned
//        copy through a TEST-LOADED oleaut32 and then Reset+Next'ing the master
//        back intact). The BSTR registrations share the kCcwMaxEnumItems = 8
//        registry with the stage-10 value items (mixed registries enumerate in
//        order; the raw register_item's VT_BSTR rejection is pinned). Empty
//        strings, Clone fresh copies, capacity overflow and teardown without
//        double-free are covered. Non-Windows: E_NOTIMPL (BSTR is Windows-only
//        -- the stage-10 every-platform claim is carved out for the BSTR path)
//        -- 4f exercises it in the POSIX stub block.
//  14. IStream / COM streams (stage 12 -- Marshal.CreateStreamOnHGlobal,
//        WINDOWS only): ole32 CreateStreamOnHGlobal wraps a native IStream
//        over an HGLOBAL; the runtime exposes it as a ComStream stream object
//        (a streamio handle-registry state of kind kCom -- NOT an RCW,
//        IsComObject false). The stream-object surface
//        (icall_stream_write/read_bytes, Seek, Length, SetLength, Close,
//        ReadByte) delegates to the IStream through the ABI-safe icalls;
//        icall_com_stream_get_hglobal / _get_iunknown expose the HGLOBAL and
//        the wrapped IStream (which QIs for IUnknown -- a real COM object);
//        icall_com_istream_read/write/seek/set_size drive the IStream directly
//        (the RCW direction). The dedicated int64_t-parameter seek/set_size
//        icalls carry the LARGE_INTEGER/ULARGE_INTEGER by-value ABI on x86 and
//        x64 (the 4x-intptr vtable-slot carrier cannot span the two platforms)
//        -- pinned by the 0x0123456789ABCDEF seek rejection discriminator
//        (probe-verified: the HGLOBAL-backed IStream's internal seek is 32-bit
//        wide, so the anchor's wrapped position is rejected -- a SWAPPED
//        4x-intptr carrier would have presented the valid high-32 position
//        0x01234567 and succeeded). Coverage: create
//        (0, true/false), write {0..255} -> Length==Position==256, Seek(0)
//        -> 0, byte-equal read-back, EOF read -> 0, Seek(-1) rejection
//        (IOException / native error), SetLength(128) truncate (native
//        passthrough -- the HGLOBAL seek pointer is unchanged, probe-verified),
//        CanRead/CanWrite/CanSeek, raw write/read round-trip, close
//        idempotent + Length -1, non-COM stream -> ArgumentException, null ->
//        ArgumentNullException. Non-Windows: create throws
//        NotSupportedException and the raw istream icalls return E_NOTIMPL
//        -- 4g exercises the stubs in the POSIX block.
//  15. Marshal BSTR bridge (stage 13 -- Marshal.StringToBSTR / BSTRToString /
//        FreeBSTR, WINDOWS only): the three icalls reuse the stage-11
//        oleaut32 resolution (SysAllocStringLen / SysFreeString, com.cpp
//        :1733-1736 -- kept OUT of GetOle32().ok). net48 semantics:
//        StringToBSTR(null) -> IntPtr.Zero (0, NOT ArgumentNullException);
//        SysAllocStringLen(s->chars, s->length) preserves embedded NUL units;
//        BSTRToString(0) -> null; BSTRToString reads to the FIRST NUL unit
//        (PtrToStringUni semantics -- embedded NULs truncate); FreeBSTR(0) ->
//        no-op; FreeBSTR otherwise SysFreeString. The bridge is a SEPARATE
//        path from the CCW enum ownership (section 13) -- the two never
//        interact. Non-Windows: StringToBSTR / BSTRToString throw
//        NotSupportedException and FreeBSTR is a no-op (the null forms still
//        follow net48 -- the null checks precede the platform gate) -- 4h
//        exercises the stubs in the POSIX block.
//  16. Marshal.GetComInterfaceForObject (stage 14 -- the focused wiring): the
//        managed Type -> IID resolution happens at TRANSPILE time (the runtime
//        Type object cannot be resolved to an IID at compile time), so the
//        icall supports ONLY the two constant Type args the transpiler
//        recognizes -- IUnknown and IDispatch (the net48-most-used interfaces
//        the CCW implements). A NON-RCW managed object is wrapped behind a
//        fresh CCW and QI'd for the IID (the returned pointer is AddRef'd --
//        the caller owns the reference; Release to 0 destroys the CCW); an
//        unsupported IID -> E_NOINTERFACE -> InvalidCastException; null ->
//        ArgumentNullException. Non-Windows: throws NotSupportedException
//        (the POSIX stub; the null guard precedes the platform gate) -- 4i
//        exercises the stub in the POSIX block.
//
// Abort paths (double Release on a destroyed CCW, foreign interface pointers)
// cannot be exercised in-process -- abort() ends the test -- and are
// documented in the header instead.
//
// Plain executable + minimal assertion harness (test_marshalext.cpp style).
//------------------------------------------------------------------------------

#include "il2cpp_com.h"

#include "il2cpp_exception.h"
#include "il2cpp_gchandle.h"
#include "il2cpp_runtime.h"
#include "il2cpp_streamio.h"
#include "il2cpp_string.h"

// COM stage 17 batch 1/2: the transpiled com17type fixture (WinForms-mirror
// IDropTarget contract -- DragDropEffects enum + minimal DragEventArgs +
// IDropTarget interface with DragEventArgs REF params / void returns + dual
// implementers WfDropSource/WfDropSource2 -- and, batch 2, the simplified
// IDataObject contract + dual implementers WfDataObject/WfDataObject2).
// Linked into test_com ONLY via tests/runtime/CMakeLists.txt (committed
// gen_v2); sections 19/20 dispatch it headlessly.
#include "generated.h"

// The generated entry.cpp (linked into this binary) supplies the runtime init
// C API with C linkage (IL2CPP_EXPORT = extern "C"); generated.h does not
// declare it (only il2cpp_entry_main), so section 19 declares it here.
extern "C" void il2cpp_runtime_init(void);

#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(_WIN32)
#include <windows.h> // stage-11 BSTR test: LoadLibraryA / GetProcAddress (SysFreeString)
#endif

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Run 'fn' and report whether it threw a DotNetException whose type chain
// exactly matches 'expectedType' (full chain -- see exceptionext.cpp).
template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedType)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedType) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Expected type chains (must match exceptionext.cpp exactly).
const char* kArgumentNull =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kArgumentOutOfRange =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kArgumentException =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";
const char* kIOException =
    "System.IO.IOException,System.IO.IOException,System.SystemException,System.Exception";
#if !defined(_WIN32)
const char* kNotSupported =
    "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception";
#else
const char* kComException =
    "System.Runtime.InteropServices.COMException,System.Runtime.InteropServices.COMException,"
    "System.Runtime.InteropServices.ExternalException,System.SystemException,System.Exception";
const char* kInvalidComObject =
    "System.Runtime.InteropServices.InvalidComObjectException,"
    "System.Runtime.InteropServices.InvalidComObjectException,"
    "System.SystemException,System.Exception";
const char* kInvalidCast =
    "System.InvalidCastException,System.InvalidCastException,"
    "System.SystemException,System.Exception";
#endif

// A random IID that no interface in phase 1 implements: QI must fail with
// E_NOINTERFACE (verify the GUID dispatch rejects unknown GUIDs).
const uint8_t kSomeOtherIid[16] = {
    0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
    0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
};

// A fake ActiveX event interface IID the phase-5 tests register on a CCW (the
// connection point FindConnectionPoint must locate) and implement on the
// native test sink (the sink QI must answer it for Advise to succeed).
const uint8_t kEventIid[16] = {
    0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, 0x00,
    0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x10, 0x20,
};

// A SECOND fake ActiveX event interface IID the stage-7 fire-routing tests
// register on the SAME CCW (proving icall_com_ccw_fire_event routes fires by
// the event interface IID: fire kEventIid reaches only the kEventIid sink,
// kEventIid2 only the kEventIid2 sink).
const uint8_t kEventIid2[16] = {
    0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0,
    0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89,
};

// A managed object to wrap behind a CCW (any real managed allocation works --
// a string exercises the runtime allocator).
il2cpp::Il2CppObject* MakeManagedTarget()
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs("ccw-target"));
}

// The managed string a stage-11 BSTR registration wraps (MakeManagedTarget's
// payload, typed as the string).
il2cpp::Il2CppString* MakeManagedString(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppString*>(
        il2cpp::il2cpp_string_new_from_cs(ascii));
}

// The CCW IDispatch vtable is driven exactly as a native automation host
// drives it: read the interface pointer's first word (the vtable array), take
// the slot and call it. The calling convention mirrors com.cpp's
// IL2CPP_COM_CALL (the il2cpp_com.h vtable layout contract).
#if defined(_M_IX86) || defined(__i386__)
    #define TEST_COM_CALL __stdcall
#else
    #define TEST_COM_CALL
#endif

typedef int32_t  (TEST_COM_CALL* NativeGetTypeInfoCountFn)(void*, uint32_t*);
typedef int32_t  (TEST_COM_CALL* NativeGetTypeInfoFn)(void*, uint32_t, uint32_t, void**);
typedef int32_t  (TEST_COM_CALL* NativeGetIDsOfNamesFn)(void*, const void*,
                                                       void* const*, uint32_t,
                                                       uint32_t, int32_t*);
typedef int32_t  (TEST_COM_CALL* NativeInvokeFn)(void*, int32_t, const void*,
                                                 uint32_t, uint16_t,
                                                 const void*, void*, void*,
                                                 uint32_t*);

// IEnumVARIANT native method shapes (stage 10 -- every platform): the
// enumeration view's Next/Skip/Reset/Clone slots, driven exactly as a native
// OLE consumer drives them.
typedef int32_t  (TEST_COM_CALL* NativeEnumNextFn)(void*, uint32_t, void*, uint32_t*);
typedef int32_t  (TEST_COM_CALL* NativeEnumSkipFn)(void*, uint32_t);
typedef int32_t  (TEST_COM_CALL* NativeEnumResetFn)(void*);
typedef int32_t  (TEST_COM_CALL* NativeEnumCloneFn)(void*, void**);

// IOleObject / IOleClientSite native method shapes (the minimal phase-4
// hosting surface; driven exactly as a native ActiveX container drives them).
#if defined(_WIN32)
typedef int32_t  (TEST_COM_CALL* NativeSetClientSiteFn)(void*, void*);
typedef int32_t  (TEST_COM_CALL* NativeGetClientSiteFn)(void*, void**);
typedef int32_t  (TEST_COM_CALL* NativeOleCloseFn)(void*, uint32_t);
typedef int32_t  (TEST_COM_CALL* NativeDoVerbFn)(void*, int32_t, const void*,
                                                 void*, int32_t, void*, const void*);
typedef int32_t  (TEST_COM_CALL* NativeUpdateFn)(void*);
typedef int32_t  (TEST_COM_CALL* NativeGetContainerFn)(void*, void**);
#endif

// IConnectionPointContainer / IConnectionPoint native method shapes (the
// phase-5 event-source surface; driven exactly as a native ActiveX host
// drives them -- FindConnectionPoint / GetConnectionInterface /
// GetConnectionPointContainer / Advise / Unadvise).
#if defined(_WIN32)
typedef int32_t  (TEST_COM_CALL* NativeEnumCpcFn)(void*, void**);
typedef int32_t  (TEST_COM_CALL* NativeFindConnectionPointFn)(void*, const void*, void**);
typedef int32_t  (TEST_COM_CALL* NativeGetConnectionInterfaceFn)(void*, void*);
typedef int32_t  (TEST_COM_CALL* NativeGetCpcFn)(void*, void**);
typedef int32_t  (TEST_COM_CALL* NativeAdviseFn)(void*, void*, uint32_t*);
typedef int32_t  (TEST_COM_CALL* NativeUnadviseFn)(void*, uint32_t);
typedef int32_t  (TEST_COM_CALL* NativeEnumConnectionsFn)(void*, void**);

// A minimal native event sink (phase 5): a real IUnknown object whose QI
// answers the fake event interface IID (S_OK, AddRef'd -- exactly how a
// native event sink implements the source interface) and E_NOINTERFACE for
// everything else. 'refs' tracks the reference count so the test can observe
// the connection point's Advise/Unadvise/teardown reference handling.
struct TestSink
{
    void* const* vtbl;
    int32_t refs;
};

int32_t TEST_COM_CALL TestSinkQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* sink = static_cast<TestSink*>(self);
    if (std::memcmp(riid, kEventIid, 16) == 0)
    {
        ++sink->refs;
        *ppv = self;
        return 0; // S_OK
    }
    *ppv = nullptr;
    return static_cast<int32_t>(0x80004002); // E_NOINTERFACE
}

uint32_t TEST_COM_CALL TestSinkAddRef(void* self)
{
    auto* sink = static_cast<TestSink*>(self);
    ++sink->refs;
    return static_cast<uint32_t>(sink->refs);
}

uint32_t TEST_COM_CALL TestSinkRelease(void* self)
{
    auto* sink = static_cast<TestSink*>(self);
    --sink->refs;
    return static_cast<uint32_t>(sink->refs);
}

void* const kTestSinkVtbl[3] = {
    reinterpret_cast<void*>(&TestSinkQueryInterface),
    reinterpret_cast<void*>(&TestSinkAddRef),
    reinterpret_cast<void*>(&TestSinkRelease),
};

// The stage-76 data-advise test sink (section 81): a minimal native
// IAdviseSink whose OnDataChange (vtable slot 3) counts calls -- the
// observable the icall_com_data_object_notify trigger must reach.
struct TestDataAdviseSink
{
    void* const* vtbl;
    int32_t onDataChangeCalls;
};

int32_t TEST_COM_CALL TestDataAdviseSinkOnDataChange(void* self, const void*, const void*)
{
    auto* sink = static_cast<TestDataAdviseSink*>(self);
    ++sink->onDataChangeCalls;
    return 0; // S_OK
}

void* const kTestDataAdviseSinkVtbl[7] = {
    nullptr, nullptr, nullptr,
    reinterpret_cast<void*>(&TestDataAdviseSinkOnDataChange), // slot 3 OnDataChange
    nullptr, nullptr, nullptr,
};

// The stage-16 drop-dispatch test record + callback (section 18). The mutable
// record is a FUNCTION-LOCAL static (TestDropRec -- no namespace-level global):
// the registered dispatch callback is a bare stateless function (the fixed
// 6-parameter signature has no user-data slot), so both the test and the
// callback reach the same record through the accessor. The callback records
// the forwarded raw OLE arguments (method/pDataObj/grfKeyState/x/y), writes a
// controllable *pdwEffect and returns a controllable HRESULT -- the bidirectional
// propagation the forwarding layer must deliver.
struct TestDropRecord
{
    int32_t  calls;
    int32_t  lastMethod;
    intptr_t lastData;
    uint32_t lastKey;
    int32_t  lastX;
    int32_t  lastY;
    uint32_t effectWrite;
    int32_t  returnHr;

    void Reset()
    {
        calls = 0;
        lastMethod = -1;
        lastData = 0;
        lastKey = 0;
        lastX = 0;
        lastY = 0;
        effectWrite = 0;
        returnHr = il2cpp::runtime::kComHrOk;
    }
};

TestDropRecord& TestDropRec()
{
    static TestDropRecord rec;
    return rec;
}

int32_t TEST_COM_CALL TestDropDispatch(int32_t method, intptr_t pDataObj,
                                       uint32_t grfKeyState, int32_t x, int32_t y,
                                       uint32_t* pdwEffect)
{
    TestDropRecord& r = TestDropRec();
    ++r.calls;
    r.lastMethod = method;
    r.lastData = pDataObj;
    r.lastKey = grfKeyState;
    r.lastX = x;
    r.lastY = y;
    if (pdwEffect != nullptr)
    {
        *pdwEffect = r.effectWrite;
    }
    return r.returnHr;
}

// A stage-7 fire-test event sink (icall_com_ccw_fire_event): a real IUnknown
// object whose vtable is IUnknown (3 slots) + TWO event methods (OnEventA at
// slot 3, OnEventB at slot 4) -- the shape the fire icall invokes at vtable
// slot 3 + methodIndex. QI answers BOTH event interface IIDs (the sink
// implements two source interfaces) with an AddRef'd self. Every invocation
// records (methodIndex, all four carriers, callCount) so the fire tests can
// assert delivery, routing isolation and byte-exact carrier passthrough.
struct TestFireSink
{
    void* const* vtbl;
    int32_t refs;
    int32_t callCount;
    int32_t lastMethod;
    intptr_t lastArg0;
    intptr_t lastArg1;
    intptr_t lastArg2;
    intptr_t lastArg3;
};

int32_t TEST_COM_CALL TestFireSinkQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* sink = static_cast<TestFireSink*>(self);
    if (std::memcmp(riid, kEventIid, 16) == 0 ||
        std::memcmp(riid, kEventIid2, 16) == 0)
    {
        ++sink->refs;
        *ppv = self;
        return 0; // S_OK
    }
    *ppv = nullptr;
    return static_cast<int32_t>(0x80004002); // E_NOINTERFACE
}

uint32_t TEST_COM_CALL TestFireSinkAddRef(void* self)
{
    auto* sink = static_cast<TestFireSink*>(self);
    ++sink->refs;
    return static_cast<uint32_t>(sink->refs);
}

uint32_t TEST_COM_CALL TestFireSinkRelease(void* self)
{
    auto* sink = static_cast<TestFireSink*>(self);
    --sink->refs;
    return static_cast<uint32_t>(sink->refs);
}

int32_t TEST_COM_CALL TestFireSinkOnEventA(void* self, intptr_t a0, intptr_t a1,
                                           intptr_t a2, intptr_t a3)
{
    auto* sink = static_cast<TestFireSink*>(self);
    ++sink->callCount;
    sink->lastMethod = 0;
    sink->lastArg0 = a0;
    sink->lastArg1 = a1;
    sink->lastArg2 = a2;
    sink->lastArg3 = a3;
    return 0; // S_OK
}

int32_t TEST_COM_CALL TestFireSinkOnEventB(void* self, intptr_t a0, intptr_t a1,
                                           intptr_t a2, intptr_t a3)
{
    auto* sink = static_cast<TestFireSink*>(self);
    ++sink->callCount;
    sink->lastMethod = 1;
    sink->lastArg0 = a0;
    sink->lastArg1 = a1;
    sink->lastArg2 = a2;
    sink->lastArg3 = a3;
    return 0; // S_OK
}

void* const kTestFireSinkVtbl[5] = {
    reinterpret_cast<void*>(&TestFireSinkQueryInterface),
    reinterpret_cast<void*>(&TestFireSinkAddRef),
    reinterpret_cast<void*>(&TestFireSinkRelease),
    reinterpret_cast<void*>(&TestFireSinkOnEventA), // slot 3 (method 0)
    reinterpret_cast<void*>(&TestFireSinkOnEventB), // slot 4 (method 1)
};

// A stage-8 dispatch-fire event sink (icall_com_ccw_fire_dispatch_event): a
// real IUnknown object whose vtable is IUnknown (3 slots) + IDispatch
// (GetTypeInfoCount/GetTypeInfo/GetIDsOfNames/Invoke -- slots 3..6), the
// automation event-sink shape a WinForms AxHost exposes. QI answers the fake
// event interface IIDs (the dispinterface contract: QI for the event IID
// returns the IDispatch pointer -- i.e. self -- AddRef'd). Invoke records
// (dispId, argument count, the raw VARIANT bytes) so the dispatch-fire tests
// can assert DISPID_FIRST numbering and byte-exact VARIANT carrier
// passthrough.
struct TestDispatchSink
{
    void* const* vtbl;
    int32_t refs;
    int32_t callCount;
    int32_t lastDispId;
    uint32_t lastCArgs;
    uint8_t lastVariants[2][16];
};

int32_t TEST_COM_CALL TestDispatchSinkQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* sink = static_cast<TestDispatchSink*>(self);
    if (std::memcmp(riid, kEventIid, 16) == 0 ||
        std::memcmp(riid, kEventIid2, 16) == 0)
    {
        ++sink->refs;
        *ppv = self;
        return 0; // S_OK
    }
    *ppv = nullptr;
    return static_cast<int32_t>(0x80004002); // E_NOINTERFACE
}

uint32_t TEST_COM_CALL TestDispatchSinkAddRef(void* self)
{
    auto* sink = static_cast<TestDispatchSink*>(self);
    ++sink->refs;
    return static_cast<uint32_t>(sink->refs);
}

uint32_t TEST_COM_CALL TestDispatchSinkRelease(void* self)
{
    auto* sink = static_cast<TestDispatchSink*>(self);
    --sink->refs;
    return static_cast<uint32_t>(sink->refs);
}

int32_t TEST_COM_CALL TestDispatchSinkGetTypeInfoCount(void* self, uint32_t* pctinfo)
{
    (void)self;
    if (pctinfo == nullptr)
    {
        return static_cast<int32_t>(0x80004003); // E_POINTER
    }
    *pctinfo = 0;
    return 0; // S_OK
}

int32_t TEST_COM_CALL TestDispatchSinkGetTypeInfo(void* self, uint32_t, uint32_t, void** ppTInfo)
{
    (void)self;
    if (ppTInfo != nullptr)
    {
        *ppTInfo = nullptr;
    }
    return static_cast<int32_t>(0x80020003); // DISP_E_MEMBERNOTFOUND -- no typelib
}

int32_t TEST_COM_CALL TestDispatchSinkGetIDsOfNames(void*, const void*, void* const*, uint32_t, uint32_t, int32_t*)
{
    return static_cast<int32_t>(0x80004001); // E_NOTIMPL -- the sink is late-bound only
}

int32_t TEST_COM_CALL TestDispatchSinkInvoke(void* self, int32_t dispIdMember, const void* riid,
                                             uint32_t lcid, uint16_t wFlags, const void* pDispParams,
                                             void* pVarResult, void* pExcepInfo, uint32_t* puArgErr)
{
    (void)riid;
    (void)lcid;
    (void)wFlags;
    (void)pExcepInfo;
    (void)puArgErr;
    auto* sink = static_cast<TestDispatchSink*>(self);
    ++sink->callCount;
    sink->lastDispId = dispIdMember;
    // Parse DISPPARAMS the same pointer-size-aware way com.cpp's CcwInvoke
    // does: rgvarg (ptr) and rgdispidNamedArgs (ptr) first, then cArgs (u32)
    // and cNamedArgs (u32). Record up to the first two 16-byte VARIANTs.
    const unsigned char* raw = static_cast<const unsigned char*>(pDispParams);
    const void* rgvarg = nullptr;
    uint32_t cArgs = 0;
    std::memcpy(&rgvarg, raw, sizeof(rgvarg));
    std::memcpy(&cArgs, raw + 2 * sizeof(void*), sizeof(cArgs));
    sink->lastCArgs = cArgs;
    for (uint32_t i = 0; i < cArgs && i < 2; ++i)
    {
        std::memcpy(sink->lastVariants[i],
                    static_cast<const unsigned char*>(rgvarg) + i * 16, 16);
    }
    if (pVarResult != nullptr)
    {
        std::memset(pVarResult, 0, 16); // VT_EMPTY return (test-sink answer)
    }
    return 0; // S_OK
}

void* const kTestDispatchSinkVtbl[7] = {
    reinterpret_cast<void*>(&TestDispatchSinkQueryInterface),
    reinterpret_cast<void*>(&TestDispatchSinkAddRef),
    reinterpret_cast<void*>(&TestDispatchSinkRelease),
    reinterpret_cast<void*>(&TestDispatchSinkGetTypeInfoCount), // slot 3
    reinterpret_cast<void*>(&TestDispatchSinkGetTypeInfo),      // slot 4
    reinterpret_cast<void*>(&TestDispatchSinkGetIDsOfNames),    // slot 5
    reinterpret_cast<void*>(&TestDispatchSinkInvoke),           // slot 6 (IDispatch::Invoke)
};

// Stage-15 register/revoke worker (the DropRegThread design): the MAIN test
// thread is initialized MTA (EnsureOle32's CoInitializeEx(0) -- the RCW tests
// ran it), and OleInitialize (STA) on an already-initialized thread fails with
// RPC_E_CHANGED_MODE. RegisterDragDrop additionally requires the window to
// belong to the calling thread and OLE to be initialized on it -- so the
// register/revoke test runs on a dedicated WORKER thread that OleInitialize's
// its own STA and creates its own hidden window. The worker is a free function
// + a stack struct (NO namespace-level global objects -- the static-init crash
// discipline); the main thread starts it, waits on the thread handle and reads
// the results.
struct DropRegWorkerData
{
    intptr_t view;         // the IDropTarget view to register
    HWND     hwnd;         // the hidden window the worker creates
    int32_t  registerHr;   // RegisterDragDrop result
    int32_t  revokeHr;     // RevokeDragDrop result
    int32_t  registerAgainHr; // re-register after revoke (idempotent)
    bool     oleInitOk;    // OleInitialize succeeded (STA)
    bool     windowOk;     // the hidden window was created
    bool     ran;          // the worker actually ran
};

// Create a hidden top-level window owned by the CALLING thread (user32 entry
// points resolved at runtime -- the test links no user32 import lib, the
// LoadLibraryA/GetProcAddress precedent of the stage-11 oleaut32 test
// resolution). The STATIC class is a predefined system class (no
// RegisterClassEx needed). The caller only ever sees a live window
// (verify-then-mutate: *out is untouched when creation fails).
bool MakeTestHwnd(HWND* out)
{
    HMODULE hUser = ::LoadLibraryA("user32.dll");
    typedef HWND (WINAPI* CreateWindowExFn)(DWORD, LPCWSTR, LPCWSTR, DWORD, int, int, int, int,
                                            HWND, HMENU, HINSTANCE, LPVOID);
    typedef BOOL (WINAPI* DestroyWindowFn)(HWND);
    const CreateWindowExFn createWindowEx = (hUser != nullptr)
        ? reinterpret_cast<CreateWindowExFn>(::GetProcAddress(hUser, "CreateWindowExW")) : nullptr;
    const DestroyWindowFn destroyWindow = (hUser != nullptr)
        ? reinterpret_cast<DestroyWindowFn>(::GetProcAddress(hUser, "DestroyWindow")) : nullptr;
    if (createWindowEx == nullptr || destroyWindow == nullptr)
    {
        return false;
    }
    HWND hwnd = static_cast<HWND>(createWindowEx(
        0, L"STATIC", L"", WS_OVERLAPPED, 0, 0, 0, 0,
        nullptr, nullptr, ::GetModuleHandleW(nullptr), nullptr));
    if (hwnd == nullptr)
    {
        return false;
    }
    *out = hwnd;
    return true;
}

DWORD WINAPI DropRegWorkerProc(LPVOID param)
{
    auto* d = static_cast<DropRegWorkerData*>(param);
    d->ran = true;
    // OleInitialize (STA) via test-side runtime resolution from ole32.dll (the
    // test links no ole32 import lib). RegisterDragDrop requires OLE initialized
    // on the calling thread.
    HMODULE hOle = ::LoadLibraryA("ole32.dll");
    typedef int32_t (WINAPI* OleInitFn)(const void*);
    const OleInitFn oleInit = (hOle != nullptr)
        ? reinterpret_cast<OleInitFn>(::GetProcAddress(hOle, "OleInitialize")) : nullptr;
    d->oleInitOk = (oleInit != nullptr && oleInit(nullptr) == 0);
    d->windowOk = MakeTestHwnd(&d->hwnd);
    if (d->oleInitOk && d->windowOk)
    {
        d->registerHr = il2cpp::runtime::icall_com_ole_register_drag_drop(
            reinterpret_cast<intptr_t>(d->hwnd), d->view);
        d->revokeHr = il2cpp::runtime::icall_com_ole_revoke_drag_drop(
            reinterpret_cast<intptr_t>(d->hwnd));
        d->registerAgainHr = il2cpp::runtime::icall_com_ole_register_drag_drop(
            reinterpret_cast<intptr_t>(d->hwnd), d->view);
        (void)il2cpp::runtime::icall_com_ole_revoke_drag_drop(
            reinterpret_cast<intptr_t>(d->hwnd)); // cleanup revoke
        HMODULE hUser = ::LoadLibraryA("user32.dll");
        typedef BOOL (WINAPI* DestroyWindowFn)(HWND);
        const DestroyWindowFn destroyWindow = (hUser != nullptr)
            ? reinterpret_cast<DestroyWindowFn>(::GetProcAddress(hUser, "DestroyWindow")) : nullptr;
        if (destroyWindow != nullptr)
        {
            (void)destroyWindow(d->hwnd);
        }
    }
    return 0;
}
#endif

void* const* ComVtable(intptr_t iface)
{
    return *static_cast<void* const* const*>(reinterpret_cast<void*>(iface));
}

// A DISPPARAMS-shaped buffer matching the real layout on both x86 and x64
// (two pointer-sized fields then two u32 counts). Tests build these instead
// of hand-crafted u32 arrays so the Invoke parse (pointer-size aware) reads
// valid memory on both platforms.
struct TestDispParams
{
    const void* rgvarg;        // pointer-sized
    const void* rgdispidNamedArgs; // pointer-sized
    uint32_t cArgs;
    uint32_t cNamedArgs;
};

// Build a 16-byte VARIANT with the given vt (the data union zeroed).
void MakeVariant(uint16_t vt, uint8_t out[16])
{
    std::memset(out, 0, 16);
    std::memcpy(out, &vt, sizeof(vt));
}

#if defined(_WIN32)
// Stage-11 BSTR helpers. BstrData reads the VT_BSTR data pointer from a
// returned 16-byte VARIANT (the data union lives at offset 8). BstrEquals
// compares a returned BSTR against the source managed string -- the content
// units PLUS the null terminator (SysAllocStringLen null-terminates; only
// 'length' source units are read, never touching the managed string's
// allocation extent).
const wchar_t* BstrData(const uint8_t variant[16])
{
    const void* p = nullptr;
    std::memcpy(&p, variant + 8, sizeof(p));
    return static_cast<const wchar_t*>(p);
}

bool BstrEquals(const uint8_t variant[16], il2cpp::Il2CppString* str)
{
    uint16_t vt = 0;
    std::memcpy(&vt, variant + il2cpp::runtime::kVariantVtOffset, sizeof(vt));
    if (vt != il2cpp::runtime::kVtBstr)
    {
        return false;
    }
    const wchar_t* data = BstrData(variant);
    if (data == nullptr)
    {
        return false;
    }
    if (std::memcmp(data, str->chars, static_cast<size_t>(str->length) * sizeof(wchar_t)) != 0)
    {
        return false;
    }
    return data[str->length] == L'\0';
}
#endif

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 1. Constants sanity ---
    Expect(std::memcmp(kComIidIUnknown,
                       "\x00\x00\x00\x00\x00\x00\x00\x00"
                       "\xC0\x00\x00\x00\x00\x00\x00\x46", 16) == 0,
           "IID_IUnknown bytes match {00000000-0000-0000-C000-000000000046}");

    // --- 2. CCW (every platform) ---
    il2cpp::Il2CppObject* target = MakeManagedTarget();
    const intptr_t ccw = icall_com_ccw_create(target);
    Expect(ccw != 0, "ccw_create returns a non-null IUnknown");

    // AddRef/Release round-trip while refCount is known (create => 1):
    // AddRef => 2, Release => 1.
    Expect(icall_com_add_ref(ccw) == 2, "CCW AddRef bumps the refcount to 2");
    Expect(icall_com_release(ccw) == 1, "CCW Release drops it back to 1");

    // QI for IUnknown returns S_OK, the SAME interface pointer (identity), and
    // ADDS a reference (net48 QI contract) -- the test releases that reference
    // to keep the count balanced.
    intptr_t qiOut = 0;
    const int32_t hrQi = icall_com_query_interface(ccw, kComIidIUnknown, &qiOut);
    Expect(hrQi == kComHrOk, "CCW QI for IUnknown returns S_OK");
    Expect(qiOut == ccw, "CCW QI for IUnknown returns the same interface pointer");
    icall_com_release(qiOut);

    // QI for an unknown IID -> E_NOINTERFACE (output untouched).
    intptr_t qiUnknown = 0x1234;
    const int32_t hrNo = icall_com_query_interface(ccw, kSomeOtherIid, &qiUnknown);
    Expect(hrNo == kComHrNoInterface, "CCW QI for an unknown IID returns E_NOINTERFACE");
    Expect(qiUnknown == 0x1234, "failed QI leaves the out pointer untouched");

    // Generic vtable-slot invocation reaches the same QI (slot 0) with the
    // method's (riid, ppv) argument pair; balance the added QI reference.
    intptr_t slotPpv = 0;
    const intptr_t slotResult = icall_com_call_vtable_slot(
        ccw, 0,
        reinterpret_cast<intptr_t>(&kComIidIUnknown[0]),
        reinterpret_cast<intptr_t>(&slotPpv),
        0, 0);
    Expect(static_cast<int32_t>(slotResult) == kComHrOk &&
           slotPpv == ccw,
           "vtable-slot invocation of slot 0 (QI) returns S_OK and the interface");
    icall_com_release(slotPpv);

    // The wrapped managed object is still alive (rooted by the CCW).
    Expect(icall_com_is_com_object(target) == false,
           "a plain managed object is not an RCW");

    // Release the CCW back to 0 (destroys it -- do not touch after).
    Expect(icall_com_release(ccw) == 0, "final Release destroys the CCW");

    // --- 5b. arg/bounds defense (everywhere) ---
    intptr_t scratch = 0;
    Expect(ThrowsDotNet([&] { icall_com_query_interface(0, kComIidIUnknown, &scratch); },
                        kArgumentNull),
           "query_interface(null pUnk) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(0, 0, 0, 0, 0, 0); }, kArgumentNull),
           "call_vtable_slot(iface 0) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(ccw == 0 ? 1 : 1, -1, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "call_vtable_slot(negative slot) -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(1, 128, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "call_vtable_slot(slot past 127) -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { (void)icall_com_is_com_object(nullptr); }, kArgumentNull),
           "IsComObject(null) -> ArgumentNullException");

    // --- 5c. net48 GetIUnknownForObject on a NON-RCW managed object ---
    // net48 Marshal.GetIUnknownForObject works on ANY object: a plain managed
    // object is wrapped behind a CCW whose IUnknown is returned (the caller
    // owns one reference). The CCW QIs as IUnknown (identity pointer); the
    // caller's Release to 0 destroys it.
    const intptr_t ccwUnk = icall_com_get_iunknown_for_object(target);
    Expect(ccwUnk != 0, "GetIUnknownForObject on a non-RCW returns a CCW IUnknown (net48)");
    Expect(icall_com_query_interface(ccwUnk, kComIidIUnknown, &qiOut) == kComHrOk &&
           qiOut == ccwUnk,
           "GetIUnknownForObject's CCW QIs as the identity IUnknown");
    icall_com_release(qiOut); // balance the QI reference
    // A slot past the CCW's 7-slot IUnknown+IDispatch vtable is rejected (OOB
    // guard) -- real RCW interfaces may be arbitrarily long, the CCW is
    // bounded.
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(ccwUnk, 7, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "call_vtable_slot(slot 7 on a CCW) -> ArgumentOutOfRangeException (OOB guard)");
    Expect(icall_com_release(ccwUnk) == 0,
           "releasing GetIUnknownForObject's CCW reference destroys it");

    // GetIUnknownForObject(null) -> ArgumentNullException (net48).
    Expect(ThrowsDotNet([&] { (void)icall_com_get_iunknown_for_object(nullptr); }, kArgumentNull),
           "GetIUnknownForObject(null) -> ArgumentNullException");

    //------------------------------------------------------------------------
    // 7. CCW IDispatch shape (phase 2 -- every platform). The CCW's single
    //    vtable now carries IUnknown (slots 0-2) + IDispatch (slots 3-6); a
    //    native automation host QIs for IDispatch, resolves names to dispIds
    //    and invokes. Drive the vtable exactly as the host does.
    //------------------------------------------------------------------------
    il2cpp::Il2CppObject* dispTarget = MakeManagedTarget();
    const intptr_t dispCcw = icall_com_ccw_create(dispTarget);
    Expect(dispCcw != 0, "ccw_create returns a non-null CCW");

    // QI for IDispatch returns S_OK + the SAME interface pointer (the single
    // vtable carries both interfaces); balance the added reference.
    intptr_t dispPtr = 0;
    Expect(icall_com_query_interface(dispCcw, kComIidIDispatch, &dispPtr) == kComHrOk &&
           dispPtr == dispCcw,
           "CCW QI for IDispatch returns S_OK and the identity pointer");
    icall_com_release(dispPtr);

    // The IDispatch pointer QIs back to the identity IUnknown.
    intptr_t dispBack = 0;
    Expect(icall_com_query_interface(dispCcw, kComIidIUnknown, &dispBack) == kComHrOk &&
           dispBack == dispCcw,
           "IDispatch-capable CCW still QIs as the identity IUnknown");
    icall_com_release(dispBack);

    // GetTypeInfoCount (slot 3): reports 0 -- no type library, late-bound only.
    uint32_t typeInfoCount = 99;
    NativeGetTypeInfoCountFn getTypeInfoCount =
        reinterpret_cast<NativeGetTypeInfoCountFn>(const_cast<void*>(ComVtable(dispCcw)[3]));
    Expect(getTypeInfoCount(reinterpret_cast<void*>(dispCcw), &typeInfoCount) == kComHrOk &&
           typeInfoCount == 0,
           "GetTypeInfoCount returns 0 (no type library)");

    // GetTypeInfo (slot 4) -- the phase-6 type library evaluation. The CCW
    // exposes NO ITypeInfo (documented simplification): TYPE_E_ELEMENTNOTFOUND
    // with a null out pointer. Drive slot 4 exactly as an automation host
    // would after seeing GetTypeInfoCount == 0.
    NativeGetTypeInfoFn getTypeInfo =
        reinterpret_cast<NativeGetTypeInfoFn>(const_cast<void*>(ComVtable(dispCcw)[4]));
    void* typeInfoOut = reinterpret_cast<void*>(0x1234);
    Expect(getTypeInfo(reinterpret_cast<void*>(dispCcw), 0, 0, &typeInfoOut) == kComHrTypeElemNotFound &&
           typeInfoOut == nullptr,
           "GetTypeInfo returns TYPE_E_ELEMENTNOTFOUND with a null out pointer (no ITypeInfo)");

    // Any iTInfo index is out of range for a 0-count object (the index is
    // ignored -- the object has no type info to hand out), and a null out
    // pointer -> E_POINTER (arg defense, the same contract as the count form).
    Expect(getTypeInfo(reinterpret_cast<void*>(dispCcw), 1, 0, &typeInfoOut) == kComHrTypeElemNotFound &&
           typeInfoOut == nullptr,
           "GetTypeInfo(iTInfo 1) is also out of range (0-count object)");
    Expect(getTypeInfo(reinterpret_cast<void*>(dispCcw), 0, 0, nullptr) == kComHrBadPointer,
           "GetTypeInfo(null ppTInfo) -> E_POINTER");

    // GetIDsOfNames (slot 5): first name -> dispId 1 (deterministic), the
    // repeat -> the SAME dispId (registry cache), a second name -> dispId 2.
    const uint8_t nullIid[16] = { 0 };
    NativeGetIDsOfNamesFn getIdsOfNames =
        reinterpret_cast<NativeGetIDsOfNamesFn>(const_cast<void*>(ComVtable(dispCcw)[5]));
    wchar_t nameOpen[] = L"Open";
    wchar_t nameClose[] = L"Close";
    void* nameOne[] = { nameOpen };
    int32_t dispIdOpen = -1;
    Expect(getIdsOfNames(reinterpret_cast<void*>(dispCcw), nullIid, nameOne, 1, 0, &dispIdOpen) == kComHrOk &&
           dispIdOpen == 1,
           "GetIDsOfNames resolves the first name to dispId 1");
    int32_t dispIdAgain = -1;
    Expect(getIdsOfNames(reinterpret_cast<void*>(dispCcw), nullIid, nameOne, 1, 0, &dispIdAgain) == kComHrOk &&
           dispIdAgain == 1,
           "GetIDsOfNames caches the dispId for a repeat name");
    void* nameTwo[] = { nameClose };
    int32_t dispIdClose = -1;
    Expect(getIdsOfNames(reinterpret_cast<void*>(dispCcw), nullIid, nameTwo, 1, 0, &dispIdClose) == kComHrOk &&
           dispIdClose == 2,
           "GetIDsOfNames resolves a second name to dispId 2");

    // Invoke (slot 6): registered dispId + zero args -> S_OK; unregistered
    // dispId -> DISP_E_MEMBERNOTFOUND; named arguments (cNamedArgs > 0) ->
    // DISP_E_BADPARAMCOUNT (phase 7 accepts positional arguments only); a
    // non-zero arg count with a null rgvarg -> E_POINTER; an unsupported
    // argument VARIANT type -> DISP_E_TYPEMISMATCH; a provided pVarResult
    // receives the honest VT_EMPTY return VARIANT (no managed dispatch).
    NativeInvokeFn invoke =
        reinterpret_cast<NativeInvokeFn>(const_cast<void*>(ComVtable(dispCcw)[6]));
    TestDispParams emptyDispParams = { nullptr, nullptr, 0, 0 };
    Expect(invoke(reinterpret_cast<void*>(dispCcw), dispIdOpen, nullIid, 0, 1, &emptyDispParams, nullptr, nullptr, nullptr) == kComHrOk,
           "Invoke(registered dispId, 0 args) returns S_OK");
    Expect(invoke(reinterpret_cast<void*>(dispCcw), 1234, nullIid, 0, 1, &emptyDispParams, nullptr, nullptr, nullptr) == kComHrMemberNotFound,
           "Invoke(unregistered dispId) -> DISP_E_MEMBERNOTFOUND");
    TestDispParams namedDispParams = { nullptr, nullptr, 0, 1 }; // cNamedArgs = 1
    Expect(invoke(reinterpret_cast<void*>(dispCcw), dispIdOpen, nullIid, 0, 1, &namedDispParams, nullptr, nullptr, nullptr) == kComHrBadParamCount,
           "Invoke with named arguments -> DISP_E_BADPARAMCOUNT (positional only)");
    TestDispParams nullRgvargParams = { nullptr, nullptr, 2, 0 }; // cArgs = 2, null rgvarg
    Expect(invoke(reinterpret_cast<void*>(dispCcw), dispIdOpen, nullIid, 0, 1, &nullRgvargParams, nullptr, nullptr, nullptr) == kComHrBadPointer,
           "Invoke with a non-zero arg count and null rgvarg -> E_POINTER");
    TestDispParams badVtParams = { nullptr, nullptr, 1, 0 }; // cArgs = 1, unsupported VT
    uint8_t badVtVariant[16];
    MakeVariant(9, badVtVariant); // VT_DISPATCH -- outside the typed subset
    badVtParams.rgvarg = badVtVariant;
    Expect(invoke(reinterpret_cast<void*>(dispCcw), dispIdOpen, nullIid, 0, 1, &badVtParams, nullptr, nullptr, nullptr) == kComHrTypeMismatch,
           "Invoke with an unsupported VARIANT type -> DISP_E_TYPEMISMATCH");
    TestDispParams resultParams = { nullptr, nullptr, 0, 0 };
    uint8_t resultVariant[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                  0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    Expect(invoke(reinterpret_cast<void*>(dispCcw), dispIdOpen, nullIid, 0, 1, &resultParams, resultVariant, nullptr, nullptr) == kComHrOk &&
           resultVariant[0] == 0 && resultVariant[1] == 0,
           "Invoke writes a VT_EMPTY return VARIANT (zeroed) into pVarResult");

    // The IDispatch extension does not widen the dispatch table: an unknown
    // IID still gets E_NOINTERFACE (output untouched).
    intptr_t noIface = 0x7777;
    Expect(icall_com_query_interface(dispCcw, kSomeOtherIid, &noIface) == kComHrNoInterface &&
           noIface == 0x7777,
           "CCW QI for an unknown IID still returns E_NOINTERFACE");

    // GetIDispatchForObject on a non-RCW managed object returns a CCW
    // IDispatch (AddRef'd once -- the caller owns it; Release to 0 destroys
    // the CCW, freeing the registered member names).
    const intptr_t mgrDisp = icall_com_get_idispatch_for_object(MakeManagedTarget());
    Expect(mgrDisp != 0, "GetIDispatchForObject(non-RCW) returns a CCW IDispatch");
    intptr_t mgrDispQi = 0;
    Expect(icall_com_query_interface(mgrDisp, kComIidIDispatch, &mgrDispQi) == kComHrOk &&
           mgrDispQi == mgrDisp,
           "GetIDispatchForObject's pointer QIs as IDispatch");
    icall_com_release(mgrDispQi);
    wchar_t nameActivate[] = L"Activate";
    void* actOne[] = { nameActivate };
    int32_t actDispId = -1;
    Expect(getIdsOfNames(reinterpret_cast<void*>(mgrDisp), nullIid, actOne, 1, 0, &actDispId) == kComHrOk &&
           actDispId == 1,
           "GetIDispatchForObject's CCW resolves names (ActiveX hosting path)");
    Expect(icall_com_release(mgrDisp) == 0,
           "releasing GetIDispatchForObject's reference destroys the CCW");

    // Release the dispCcw back to 0 (destroys it and frees the registry).
    Expect(icall_com_release(dispCcw) == 0, "final Release destroys the IDispatch CCW");

    //------------------------------------------------------------------------
    // 12. IEnumVARIANT (stage 10 -- every platform). The CCW exposes an
    //     enumeration view inlined at &ccw->enumView whose vtable is 7 slots
    //     (IUnknown + Next/Skip/Reset/Clone). The managed wiring layer
    //     registers the enumeration items (16-byte VARIANT value copies of the
    //     VT_EMPTY / VT_NULL / VT_I4 / VT_R8 subset) via
    //     icall_com_ccw_enum_register_item; a native host then QIs for
    //     IEnumVARIANT and drives Next/Skip/Reset/Clone. The identity QI
    //     dispatch lives outside the Windows gate -- the whole stage-10
    //     surface is every-platform (pure memory + GUID compare, no ole32).
    //     The OOB guard knows the 7-slot enum vtable; Clone hands out a heap
    //     view sharing the CCW refcount with an independent cursor; releasing
    //     the clone deletes its heap block.
    //------------------------------------------------------------------------
    const intptr_t enumCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(enumCcw != 0, "ccw_create returns the enumeration CCW");

    // Constants sanity: the IEnumVARIANT IID bytes, the method slot layout and
    // the per-CCW registry capacity.
    Expect(std::memcmp(kComIidIEnumVariant,
                       "\x04\x04\x02\x00\x00\x00\x00\x00"
                       "\xC0\x00\x00\x00\x00\x00\x00\x46", 16) == 0,
           "IID_IEnumVARIANT bytes match {00020404-0000-0000-C000-000000000046}");
    Expect(kComEnumSlotNext == 3 && kComEnumSlotSkip == 4 &&
           kComEnumSlotReset == 5 && kComEnumSlotClone == 6,
           "IEnumVARIANT slot constants: Next 3 / Skip 4 / Reset 5 / Clone 6");
    Expect(kCcwMaxEnumItems == 8,
           "kCcwMaxEnumItems == 8 (the per-CCW enumeration registry capacity)");

    // QI for IEnumVARIANT returns S_OK + a DISTINCT view (not the identity).
    intptr_t enumView = 0;
    Expect(icall_com_query_interface(enumCcw, kComIidIEnumVariant, &enumView) == kComHrOk &&
           enumView != enumCcw,
           "QI for IEnumVARIANT returns a distinct enum view");

    // The enum view QIs back to the identity (COM identity rule) and as
    // itself. Balance the added references.
    intptr_t enumBack = 0;
    Expect(icall_com_query_interface(enumView, kComIidIUnknown, &enumBack) == kComHrOk &&
           enumBack == enumCcw,
           "enum view QI for IUnknown returns the identity pointer");
    icall_com_release(enumBack);
    intptr_t enumSelf = 0;
    Expect(icall_com_query_interface(enumView, kComIidIEnumVariant, &enumSelf) == kComHrOk &&
           enumSelf == enumView,
           "enum view QIs as IEnumVARIANT (its own view)");
    icall_com_release(enumSelf);

    // Registration arg defense: null ccw / null variant -> ArgumentNullException.
    Expect(ThrowsDotNet([&] { icall_com_ccw_enum_register_item(0, kSomeOtherIid); }, kArgumentNull),
           "enum_register_item(null ccw) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_ccw_enum_register_item(enumCcw, nullptr); }, kArgumentNull),
           "enum_register_item(null variant) -> ArgumentNullException");

    // Unsupported VARIANT types are rejected with E_INVALIDARG -- the
    // documented VT_BSTR case (a BSTR would need a release discipline the
    // 16-byte value copy cannot provide) and any other type outside the
    // VT_EMPTY / VT_NULL / VT_I4 / VT_R8 subset.
    uint8_t bstrVar[16];
    MakeVariant(kVtBstr, bstrVar);
    Expect(icall_com_ccw_enum_register_item(enumCcw, bstrVar) == kComHrInvalidArg,
           "enum_register_item(VT_BSTR) -> E_INVALIDARG");
    uint8_t dateVar[16];
    MakeVariant(7, dateVar); // VT_DATE -- outside the enumeration subset
    Expect(icall_com_ccw_enum_register_item(enumCcw, dateVar) == kComHrInvalidArg,
           "enum_register_item(unsupported VARIANT type) -> E_INVALIDARG");

    // Register a mixed-type item sequence (VT_EMPTY / VT_I4 / VT_R8 / VT_NULL)
    // -- four 16-byte VALUE copies the enumerator hands back byte-exact.
    uint8_t enumItems[4][16];
    MakeVariant(kVtEmpty, enumItems[0]);
    MakeVariant(kVtI4, enumItems[1]);
    const int32_t enumI4Value = -7;
    std::memcpy(enumItems[1] + 8, &enumI4Value, sizeof(enumI4Value));
    MakeVariant(kVtR8, enumItems[2]);
    const double enumR8Value = 3.25;
    std::memcpy(enumItems[2] + 8, &enumR8Value, sizeof(enumR8Value));
    MakeVariant(kVtNull, enumItems[3]);
    for (int32_t i = 0; i < 4; ++i)
    {
        Expect(icall_com_ccw_enum_register_item(enumCcw, enumItems[i]) == kComHrOk,
               "enum_register_item accepts a mixed-type item");
    }

    // Capacity (a SEPARATE CCW so the enumeration cursor tests below stay on
    // the 4-item registry): eight registrations fill the kCcwMaxEnumItems = 8
    // registry; the ninth -> E_OUTOFMEMORY.
    const intptr_t enumCapCcw = icall_com_ccw_create(MakeManagedTarget());
    uint8_t capItem[16];
    MakeVariant(kVtI4, capItem);
    for (int32_t i = 0; i < 8; ++i)
    {
        Expect(icall_com_ccw_enum_register_item(enumCapCcw, capItem) == kComHrOk,
               "enum_register_item fills the registry up to capacity");
    }
    Expect(icall_com_ccw_enum_register_item(enumCapCcw, capItem) == kComHrOutOfMemory,
           "enum_register_item past capacity -> E_OUTOFMEMORY");
    Expect(icall_com_release(enumCapCcw) == 0,
           "releasing the capacity CCW destroys it");

    // Next (slot 3): a full fetch returns S_OK with every item byte-exact and
    // pceltFetched = celt.
    NativeEnumNextFn enumNext =
        reinterpret_cast<NativeEnumNextFn>(const_cast<void*>(ComVtable(enumView)[kComEnumSlotNext]));
    uint8_t gotAll[4][16];
    uint32_t fetchedAll = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 4, gotAll, &fetchedAll) == kComHrOk &&
           fetchedAll == 4 &&
           std::memcmp(gotAll, enumItems, 4 * 16) == 0,
           "Next(4) returns S_OK with all 4 items byte-exact");

    // Next past the end: S_FALSE with pceltFetched = 0 (the cursor is at the
    // end -- nothing further to fetch).
    uint8_t gotNone[1][16];
    uint32_t fetchedNone = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 1, gotNone, &fetchedNone) == kComHrSFALSE &&
           fetchedNone == 0,
           "Next at the exhausted cursor -> S_FALSE with pceltFetched = 0");

    // Next(0) -> S_OK with pceltFetched = 0 (fetching zero items is a no-op).
    uint32_t fetchedZero = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 0, gotNone, &fetchedZero) == kComHrOk &&
           fetchedZero == 0,
           "Next(0) -> S_OK with pceltFetched = 0");

    // Reset (slot 5) rewinds the cursor; a partial fetch at the end returns
    // S_FALSE but STILL writes the fetched items and pceltFetched (the
    // documented IEnumVARIANT contract -- "S_FALSE 仍写 pceltFetched").
    NativeEnumResetFn enumReset =
        reinterpret_cast<NativeEnumResetFn>(const_cast<void*>(ComVtable(enumView)[kComEnumSlotReset]));
    Expect(enumReset(reinterpret_cast<void*>(enumView)) == kComHrOk,
           "Reset returns S_OK");
    uint8_t gotPart[4][16];
    uint32_t fetchedPart = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 8, gotPart, &fetchedPart) == kComHrSFALSE &&
           fetchedPart == 4 &&
           std::memcmp(gotPart, enumItems, 4 * 16) == 0,
           "Next past the end -> S_FALSE, writes the partial fetch + pceltFetched");

    // Next(null rgelt) -> E_POINTER (arg defense).
    uint32_t fetchedPtr = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 1, nullptr, &fetchedPtr) == kComHrBadPointer,
           "Next(null rgelt) -> E_POINTER");

    // Skip (slot 4): skips celt items (S_OK) when within range; skipping past
    // the end clamps the cursor and returns S_FALSE; Skip(0) is a no-op.
    NativeEnumSkipFn enumSkip =
        reinterpret_cast<NativeEnumSkipFn>(const_cast<void*>(ComVtable(enumView)[kComEnumSlotSkip]));
    Expect(enumReset(reinterpret_cast<void*>(enumView)) == kComHrOk &&
           enumSkip(reinterpret_cast<void*>(enumView), 2) == kComHrOk,
           "Reset + Skip(2) -> S_OK");
    uint8_t gotTail[2][16];
    uint32_t fetchedTail = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 2, gotTail, &fetchedTail) == kComHrOk &&
           fetchedTail == 2 &&
           std::memcmp(gotTail, enumItems + 2, 2 * 16) == 0,
           "after Skip(2), Next(2) returns the remaining tail items");
    Expect(enumSkip(reinterpret_cast<void*>(enumView), 1) == kComHrSFALSE,
           "Skip past the end -> S_FALSE (cursor clamped)");
    Expect(enumSkip(reinterpret_cast<void*>(enumView), 0) == kComHrOk,
           "Skip(0) -> S_OK (no-op)");

    // RCW direction: the existing generic vtable-slot invocation
    // (icall_com_call_vtable_slot) drives the SAME enum view (slot 3 = Next)
    // with zero new icalls; the OOB guard knows the 7-slot enum vtable (slot 7
    // -> ArgumentOutOfRangeException).
    Expect(enumReset(reinterpret_cast<void*>(enumView)) == kComHrOk,
           "Reset before the RCW-direction fetch");
    uint8_t rcwGot[1][16];
    int32_t rcwFetched = 99;
    const intptr_t rcwResult = icall_com_call_vtable_slot(
        enumView, kComEnumSlotNext, 1,
        reinterpret_cast<intptr_t>(rcwGot), reinterpret_cast<intptr_t>(&rcwFetched), 0);
    Expect(static_cast<int32_t>(rcwResult) == kComHrOk && rcwFetched == 1 &&
           std::memcmp(rcwGot[0], enumItems[0], 16) == 0,
           "call_vtable_slot(enum view, slot 3 = Next) fetches the first item");
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(enumView, 7, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "call_vtable_slot(slot 7 on the enum view) -> AOOException (OOB guard)");

    // Clone (slot 6): a heap view sharing the CCW refcount, starting at the
    // SAME cursor position with an INDEPENDENT cursor.
    NativeEnumCloneFn enumClone =
        reinterpret_cast<NativeEnumCloneFn>(const_cast<void*>(ComVtable(enumView)[kComEnumSlotClone]));
    Expect(enumReset(reinterpret_cast<void*>(enumView)) == kComHrOk &&
           enumSkip(reinterpret_cast<void*>(enumView), 2) == kComHrOk,
           "Reset + Skip(2) positions the cursor for Clone");
    void* cloned = nullptr;
    Expect(enumClone(reinterpret_cast<void*>(enumView), &cloned) == kComHrOk &&
           cloned != nullptr && cloned != reinterpret_cast<void*>(enumView),
           "Clone returns a distinct heap enumerator");
    // The clone holds a shared reference to the CCW: the identity refcount now
    // counts the clone too (create 1 + enum-view QI 1 + clone 1 = 3; AddRef -> 4).
    Expect(icall_com_add_ref(enumCcw) == 4,
           "Clone shares the CCW refcount (identity count includes the clone)");
    icall_com_release(enumCcw);
    Expect(enumClone(reinterpret_cast<void*>(enumView), nullptr) == kComHrBadPointer,
           "Clone(null ppEnum) -> E_POINTER");

    // Cursor independence: the clone and the original each advance their own
    // cursor over the same shared item registry (both started at item 2).
    uint8_t cloneGot[1][16];
    uint32_t cloneFetched = 99;
    Expect(enumNext(reinterpret_cast<void*>(cloned), 1, cloneGot, &cloneFetched) == kComHrOk &&
           cloneFetched == 1 &&
           std::memcmp(cloneGot[0], enumItems[2], 16) == 0,
           "clone's Next returns the item at ITS cursor (item 2)");
    uint8_t origGot[1][16];
    uint32_t origFetched = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 1, origGot, &origFetched) == kComHrOk &&
           origFetched == 1 &&
           std::memcmp(origGot[0], enumItems[2], 16) == 0,
           "original's Next returns the item at ITS cursor (independent)");
    uint8_t cloneGot2[1][16];
    uint32_t cloneFetched2 = 99;
    Expect(enumNext(reinterpret_cast<void*>(cloned), 1, cloneGot2, &cloneFetched2) == kComHrOk &&
           cloneFetched2 == 1 &&
           std::memcmp(cloneGot2[0], enumItems[3], 16) == 0,
           "clone advances independently (item 3)");
    uint8_t origGot2[1][16];
    uint32_t origFetched2 = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 1, origGot2, &origFetched2) == kComHrOk &&
           origFetched2 == 1 &&
           std::memcmp(origGot2[0], enumItems[3], 16) == 0,
           "original advances independently (item 3)");
    uint8_t cloneExhGot[1][16];
    uint32_t cloneExh = 99;
    Expect(enumNext(reinterpret_cast<void*>(cloned), 1, cloneExhGot, &cloneExh) == kComHrSFALSE &&
           cloneExh == 0,
           "clone exhausts independently of the original");
    uint8_t origExhGot[1][16];
    uint32_t origExh = 99;
    Expect(enumNext(reinterpret_cast<void*>(enumView), 1, origExhGot, &origExh) == kComHrSFALSE &&
           origExh == 0,
           "original exhausts independently of the clone");

    // Teardown refcounts: create (1) + the QI'd enum view (1) + the clone's
    // shared reference (1). Releasing the clone deletes its heap block; the
    // final Release destroys the CCW.
    Expect(icall_com_release(reinterpret_cast<intptr_t>(cloned)) == 2,
           "releasing the clone drops the shared refcount to 2");
    Expect(icall_com_release(enumView) == 1,
           "releasing the enum view QI reference drops to 1");
    Expect(icall_com_release(enumCcw) == 0,
           "final Release destroys the enumeration CCW");

#if defined(_WIN32)

    //------------------------------------------------------------------------
    // 13. IEnumVARIANT BSTR items (stage 11 -- the oleaut32 ownership
    //     extension; WINDOWS only -- BSTR is a Windows type). The stage-10
    //     value-copy registry (section 12) rejects VT_BSTR with E_INVALIDARG
    //     because a 16-byte VALUE copy cannot own a released allocation. The
    //     dedicated register path icall_com_ccw_enum_register_bstr_item closes
    //     the gap: the runtime SysAllocStringLen's a copy of the managed
    //     string (oleaut32 resolved at runtime -- the new function pointers
    //     stay OUT of GetOle32().ok) and the CCW OWNS that BSTR (SysFreeString
    //     on teardown). Next hands the caller a FRESH SysAllocStringLen copy
    //     every fetch -- the OLE contract: the caller VariantClear's the
    //     returned elements, the stored master survives. The BSTR registrations
    //     SHARE the kCcwMaxEnumItems registry with the stage-10 value items.
    //------------------------------------------------------------------------
    const intptr_t bstrCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(bstrCcw != 0, "ccw_create returns the BSTR-stage CCW");

    // Test-side oleaut32 resolution (the runtime resolves SysFreeString
    // internally; the test loads it itself to free returned copies and prove
    // ownership separation).
    HMODULE ha = ::LoadLibraryA("oleaut32.dll");
    typedef void (TEST_COM_CALL* FreeBstrFn)(const void*);
    FreeBstrFn freeBstr = (ha != nullptr)
        ? reinterpret_cast<FreeBstrFn>(::GetProcAddress(ha, "SysFreeString")) : nullptr;
    Expect(ha != nullptr && freeBstr != nullptr,
           "test-resolved oleaut32 SysFreeString (ownership-separation free)");

    // Arg defense: null ccw / null str -> ArgumentNullException.
    Expect(ThrowsDotNet([&] { icall_com_ccw_enum_register_bstr_item(0, MakeManagedString("x")); }, kArgumentNull),
           "enum_register_bstr_item(null ccw) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_ccw_enum_register_bstr_item(bstrCcw, nullptr); }, kArgumentNull),
           "enum_register_bstr_item(null str) -> ArgumentNullException");

    // Registration: a BSTR item registers S_OK into the shared registry.
    il2cpp::Il2CppString* bstrMaster = MakeManagedString("stage-11-bstr");
    Expect(icall_com_ccw_enum_register_bstr_item(bstrCcw, bstrMaster) == kComHrOk,
           "enum_register_bstr_item accepts a BSTR item");

    // QI for the enumeration view.
    intptr_t bstrView = 0;
    Expect(icall_com_query_interface(bstrCcw, kComIidIEnumVariant, &bstrView) == kComHrOk &&
           bstrView != bstrCcw,
           "QI for IEnumVARIANT returns the BSTR-stage enum view");

    // Next returns a VT_BSTR VARIANT whose content matches the source string.
    NativeEnumNextFn bstrNext =
        reinterpret_cast<NativeEnumNextFn>(const_cast<void*>(ComVtable(bstrView)[kComEnumSlotNext]));
    uint8_t bstrGot[1][16];
    uint32_t bstrFetched = 99;
    Expect(bstrNext(reinterpret_cast<void*>(bstrView), 1, bstrGot, &bstrFetched) == kComHrOk &&
           bstrFetched == 1 && BstrEquals(bstrGot[0], bstrMaster),
           "Next returns a VT_BSTR copy matching the registered string");

    // Ownership separation: the RETURNED copy is a FRESH allocation -- freeing
    // it (test-side SysFreeString) must not destroy the stored master: Reset +
    // Next fetches the master back INTACT (a freed master could not).
    freeBstr(BstrData(bstrGot[0]));
    NativeEnumResetFn bstrReset =
        reinterpret_cast<NativeEnumResetFn>(const_cast<void*>(ComVtable(bstrView)[kComEnumSlotReset]));
    uint8_t bstrGot2[1][16];
    uint32_t bstrFetched2 = 99;
    Expect(bstrReset(reinterpret_cast<void*>(bstrView)) == kComHrOk &&
           bstrNext(reinterpret_cast<void*>(bstrView), 1, bstrGot2, &bstrFetched2) == kComHrOk &&
           bstrFetched2 == 1 && BstrEquals(bstrGot2[0], bstrMaster),
           "after freeing the returned copy + Reset, the stored master still enumerates intact");
    freeBstr(BstrData(bstrGot2[0]));

    // Empty string: SysAllocStringLen(chars, 0) yields a non-null BSTR whose
    // content is the null terminator only.
    il2cpp::Il2CppString* emptyStr = MakeManagedString("");
    Expect(icall_com_ccw_enum_register_bstr_item(bstrCcw, emptyStr) == kComHrOk,
           "enum_register_bstr_item accepts the empty string");
    uint8_t bstrPair[2][16];
    uint32_t bstrPairFetched = 99;
    Expect(bstrReset(reinterpret_cast<void*>(bstrView)) == kComHrOk &&
           bstrNext(reinterpret_cast<void*>(bstrView), 2, bstrPair, &bstrPairFetched) == kComHrOk &&
           bstrPairFetched == 2 &&
           BstrEquals(bstrPair[0], bstrMaster) && BstrEquals(bstrPair[1], emptyStr),
           "Next returns the master + empty-string BSTR copies in order");
    freeBstr(BstrData(bstrPair[0]));
    freeBstr(BstrData(bstrPair[1]));

    // Mixed registration: BSTR items and stage-10 value items share the same
    // registry, in registration order.
    il2cpp::Il2CppString* mixedStr = MakeManagedString("mixed");
    Expect(icall_com_ccw_enum_register_bstr_item(bstrCcw, mixedStr) == kComHrOk,
           "enum_register_bstr_item registers a second BSTR (mixed registry)");
    uint8_t mixedItem[16];
    MakeVariant(kVtI4, mixedItem);
    const int32_t mixedValue = 42;
    std::memcpy(mixedItem + 8, &mixedValue, sizeof(mixedValue));
    Expect(icall_com_ccw_enum_register_item(bstrCcw, mixedItem) == kComHrOk,
           "a stage-10 value item coexists in the shared BSTR registry");
    uint8_t mixedGot[3][16];
    uint32_t mixedFetched = 99;
    Expect(bstrReset(reinterpret_cast<void*>(bstrView)) == kComHrOk &&
           bstrNext(reinterpret_cast<void*>(bstrView), 3, mixedGot, &mixedFetched) == kComHrOk &&
           mixedFetched == 3 &&
           BstrEquals(mixedGot[0], bstrMaster) && BstrEquals(mixedGot[1], emptyStr) &&
           BstrEquals(mixedGot[2], mixedStr),
           "mixed Next returns every BSTR item as a fresh copy, in order");
    freeBstr(BstrData(mixedGot[0]));
    freeBstr(BstrData(mixedGot[1]));
    freeBstr(BstrData(mixedGot[2]));
    uint8_t valueGot[1][16];
    uint32_t valueFetched = 99;
    Expect(bstrNext(reinterpret_cast<void*>(bstrView), 1, valueGot, &valueFetched) == kComHrOk &&
           valueFetched == 1 && std::memcmp(valueGot[0], mixedItem, 16) == 0,
           "the value item after the BSTR tail stays a byte-exact value copy");

    // Regression pin: the RAW value-copy register path still rejects VT_BSTR
    // (the stage-10 E_INVALIDARG contract is unchanged by the BSTR extension).
    uint8_t bstrRegVar[16];
    MakeVariant(kVtBstr, bstrRegVar);
    Expect(icall_com_ccw_enum_register_item(bstrCcw, bstrRegVar) == kComHrInvalidArg,
           "regression pin: enum_register_item(VT_BSTR) still E_INVALIDARG");

    // Clone: the clone enumerates the SAME registry with fresh copies too.
    NativeEnumCloneFn bstrClone =
        reinterpret_cast<NativeEnumCloneFn>(const_cast<void*>(ComVtable(bstrView)[kComEnumSlotClone]));
    void* bstrCloned = nullptr;
    Expect(bstrReset(reinterpret_cast<void*>(bstrView)) == kComHrOk &&
           bstrClone(reinterpret_cast<void*>(bstrView), &bstrCloned) == kComHrOk &&
           bstrCloned != nullptr && bstrCloned != reinterpret_cast<void*>(bstrView),
           "Clone returns a distinct heap enumerator for the BSTR registry");
    uint8_t cloneBstrGot[1][16];
    uint32_t cloneBstrFetched = 99;
    Expect(bstrNext(reinterpret_cast<void*>(bstrCloned), 1, cloneBstrGot, &cloneBstrFetched) == kComHrOk &&
           cloneBstrFetched == 1 && BstrEquals(cloneBstrGot[0], bstrMaster),
           "clone's Next returns a fresh BSTR copy of the shared registry item");
    freeBstr(BstrData(cloneBstrGot[0]));
    Expect(icall_com_release(reinterpret_cast<intptr_t>(bstrCloned)) == 2,
           "releasing the BSTR clone drops the shared refcount to 2");

    // Capacity (a SEPARATE CCW so the cursor tests above stay on their 4-item
    // registry): the BSTR registrations SHARE the kCcwMaxEnumItems = 8
    // capacity with the stage-10 value items.
    const intptr_t bstrCapCcw = icall_com_ccw_create(MakeManagedTarget());
    il2cpp::Il2CppString* capStr = MakeManagedString("cap");
    for (int32_t i = 0; i < 8; ++i)
    {
        Expect(icall_com_ccw_enum_register_bstr_item(bstrCapCcw, capStr) == kComHrOk,
               "enum_register_bstr_item fills the shared registry to capacity");
    }
    Expect(icall_com_ccw_enum_register_bstr_item(bstrCapCcw, capStr) == kComHrOutOfMemory,
           "enum_register_bstr_item past the shared capacity -> E_OUTOFMEMORY");
    Expect(icall_com_release(bstrCapCcw) == 0,
           "releasing the BSTR capacity CCW destroys it (teardown frees 8 owned BSTRs, no double-free)");

    // Teardown: releasing the view + identity destroys the CCW, SysFreeString'ing
    // every OWNED BSTR master exactly once (the earlier freeBstr calls freed the
    // caller's copies, NOT the masters -- no double-free).
    Expect(icall_com_release(bstrView) == 1 && icall_com_release(bstrCcw) == 0,
           "releasing the BSTR enum view + identity destroys the CCW (no double-free)");

#endif // defined(_WIN32)

#if defined(_WIN32)

    //------------------------------------------------------------------------
    // 3. RCW (Windows): "Shell.Application" is a ProgID always registered on
    //    Windows (shell32's automation object).
    //------------------------------------------------------------------------
    il2cpp::Il2CppObject* shell = icall_com_create_object(
        reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("Shell.Application")));
    Expect(shell != nullptr, "create_object(\"Shell.Application\") returns an RCW");
    Expect(icall_com_is_com_object(shell) == true, "IsComObject(RCW) is true");

    // GetIUnknownForObject returns an AddRef'd pointer; GetObjectForIUnknown
    // wraps it back (taking OWNERSHIP of that added reference).
    const intptr_t unk = icall_com_get_iunknown_for_object(shell);
    Expect(unk != 0, "GetIUnknownForObject returns a non-null IUnknown");
    il2cpp::Il2CppObject* rewrap = icall_com_get_object_for_iunknown(unk);
    Expect(icall_com_is_com_object(rewrap) == true,
           "GetObjectForIUnknown wraps the pointer back into an RCW");
    icall_com_release_com_object(rewrap); // consumes the added reference

    // QueryInterface on the real Shell object for IUnknown succeeds (QI
    // returns an AddRef'd pointer -- release it to balance).
    intptr_t shellQi = 0;
    Expect(icall_com_query_interface(unk, kComIidIUnknown, &shellQi) == kComHrOk &&
           shellQi != 0,
           "Shell QI for IUnknown succeeds (real COM interface dispatch)");
    icall_com_release(shellQi);

    // ReleaseComObject releases the RCW's native reference (returns the
    // remaining wrapper count 0); a later COM op throws InvalidComObject.
    Expect(icall_com_release_com_object(shell) == 0,
           "ReleaseComObject returns 0 remaining references");
    Expect(ThrowsDotNet([&] { icall_com_get_iunknown_for_object(shell); }, kInvalidComObject),
           "COM op on a released RCW -> InvalidComObjectException");

    // FinalReleaseComObject on a fresh RCW is idempotent and safe.
    il2cpp::Il2CppObject* shell2 = icall_com_create_object(
        reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("Shell.Application")));
    Expect(shell2 != nullptr, "create_object creates a second RCW");
    icall_com_final_release_com_object(shell2);
    icall_com_final_release_com_object(shell2); // idempotent -- no crash

    // GetTypeFromProgID resolves the Shell ProgID to its CLSID.
    uint8_t clsid[16] = { 0 };
    const int32_t hrType = icall_com_get_type_from_progid(
        reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("Shell.Application")),
        clsid);
    Expect(hrType == kComHrOk, "GetTypeFromProgID(\"Shell.Application\") returns S_OK");
    bool nonZeroClsid = false;
    for (int i = 0; i < 16; ++i)
    {
        nonZeroClsid = nonZeroClsid || clsid[i] != 0;
    }
    Expect(nonZeroClsid, "GetTypeFromProgID writes a non-zero CLSID");

    // An unregistered ProgID -> CO_E_CLASSSTRING (0x800401F3, the HRESULT
    // CLSIDFromProgID returns for a ProgID absent from the registry; out
    // buffer untouched).
    uint8_t clsid2[16] = { 0xAA };
    const int32_t hrBad = icall_com_get_type_from_progid(
        reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("ILC.No.Such.ProgID.9f3e7c21")),
        clsid2);
    Expect(hrBad == kComHrClassString, "unregistered ProgID -> CO_E_CLASSSTRING");
    Expect(clsid2[0] == 0xAA, "failed GetTypeFromProgID leaves the out buffer untouched");

    // GetActiveObject on an unregistered ProgID throws the COMException chain
    // (the deterministic path -- CLSIDFromProgID fails first).
    Expect(ThrowsDotNet([&] {
               (void)icall_com_get_active_object(
                   reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("ILC.No.Such.ProgID.9f3e7c21")));
           }, kComException),
           "GetActiveObject(unregistered ProgID) -> COMException");

    // CreateInstance by CLSID (the Shell CLSID resolved above) works directly.
    const intptr_t byClsid = icall_com_create_instance_clsid(clsid);
    Expect(byClsid != 0, "create_instance_clsid(Shell CLSID) returns an IUnknown");
    icall_com_release(byClsid);

    // CreateInstance by ProgID matches the object path.
    const intptr_t byProgId = icall_com_create_instance_progid(
        reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("Shell.Application")));
    Expect(byProgId != 0, "create_instance_progid(\"Shell.Application\") returns an IUnknown");
    icall_com_release(byProgId);

    Expect(ThrowsDotNet([&] { icall_com_create_instance_progid(nullptr); }, kArgumentNull),
           "create_instance_progid(null) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { (void)icall_com_get_active_object(nullptr); }, kArgumentNull),
           "GetActiveObject(null) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_create_object(nullptr); }, kArgumentNull),
           "CreateObject(null) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { (void)icall_com_get_type_from_progid(nullptr, clsid); }, kArgumentNull),
           "GetTypeFromProgID(null) -> ArgumentNullException");

    //------------------------------------------------------------------------
    // 8. IDispatch (Windows): the RCW-direction icalls forward through the
    //    native vtable slot 5/6. Driving them against a CCW exercises the
    //    full dispatch path deterministically.
    //------------------------------------------------------------------------
    const intptr_t winCcw = icall_com_ccw_create(MakeManagedTarget());
    intptr_t winDisp = 0;
    Expect(icall_com_query_interface(winCcw, kComIidIDispatch, &winDisp) == kComHrOk &&
           winDisp == winCcw,
           "QI the test CCW for IDispatch (Windows path)");

    wchar_t nameAlpha[] = L"Alpha";
    wchar_t nameBeta[] = L"Beta";
    intptr_t nameCarriers[2] = { reinterpret_cast<intptr_t>(nameAlpha),
                                 reinterpret_cast<intptr_t>(nameBeta) };
    int32_t winDispIds[2] = { 0, 0 };
    Expect(icall_com_idispatch_get_ids_of_names(winDisp, nameCarriers, 2, 0, winDispIds) == kComHrOk &&
           winDispIds[0] == 1 && winDispIds[1] == 2,
           "idispatch_get_ids_of_names resolves names deterministically (1,2)");
    intptr_t nameCarriers1[1] = { reinterpret_cast<intptr_t>(nameAlpha) };
    int32_t winDispIdAlpha = 0;
    Expect(icall_com_idispatch_get_ids_of_names(winDisp, nameCarriers1, 1, 0, &winDispIdAlpha) == kComHrOk &&
           winDispIdAlpha == 1,
           "idispatch_get_ids_of_names caches dispIds");
    TestDispParams winDp = { nullptr, nullptr, 0, 0 };
    Expect(icall_com_idispatch_invoke(winDisp, 1, 0, 1,
                                      reinterpret_cast<intptr_t>(&winDp), 0, 0, 0) == kComHrOk,
           "idispatch_invoke(registered dispId, 0 args) -> S_OK");
    Expect(icall_com_idispatch_invoke(winDisp, 999, 0, 1,
                                      reinterpret_cast<intptr_t>(&winDp), 0, 0, 0) == kComHrMemberNotFound,
           "idispatch_invoke(unregistered dispId) -> DISP_E_MEMBERNOTFOUND");

    // Argument defense on the IDispatch icalls.
    Expect(ThrowsDotNet([&] { icall_com_idispatch_get_ids_of_names(0, nameCarriers1, 1, 0, &winDispIdAlpha); },
                        kArgumentNull),
           "idispatch_get_ids_of_names(null dispatch) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_idispatch_invoke(0, 1, 0, 1,
                                                         reinterpret_cast<intptr_t>(&winDp), 0, 0, 0); },
                        kArgumentNull),
           "idispatch_invoke(null dispatch) -> ArgumentNullException");

    //------------------------------------------------------------------------
    // 8b. Typed invocation (phase 7 -- the VARIANT argument surface). Invoke
    //     now accepts typed automation arguments (VT_I4 / VT_R8 / VT_BSTR)
    //     and writes the return VARIANT. Drive slot 6 exactly as a native
    //     host late-binds a typed call: build a DISPPARAMS whose rgvarg is a
    //     16-byte VARIANT array (in reverse argument order -- rgvarg[0] is
    //     the LAST argument) and call Invoke with a registered dispId.
    //------------------------------------------------------------------------
    const intptr_t typedCcw = icall_com_ccw_create(MakeManagedTarget());
    intptr_t typedDisp = 0;
    Expect(icall_com_query_interface(typedCcw, kComIidIDispatch, &typedDisp) == kComHrOk &&
           typedDisp == typedCcw,
           "QI the typed-call CCW for IDispatch");
    NativeInvokeFn typedInvoke =
        reinterpret_cast<NativeInvokeFn>(const_cast<void*>(ComVtable(typedDisp)[6]));

    // Resolve one registered name (dispId 1) to invoke.
    wchar_t nameCompute[] = L"Compute";
    void* computeOne[] = { nameCompute };
    int32_t computeDispId = -1;
    Expect(getIdsOfNames(reinterpret_cast<void*>(typedDisp), nullIid, computeOne, 1, 0, &computeDispId) == kComHrOk &&
           computeDispId == 1,
           "typed-call CCW resolves a name to dispId 1");

    // VT_I4 argument (a 4-byte int32 at offset 8) -> S_OK.
    TestDispParams i4Params = { nullptr, nullptr, 1, 0 };
    uint8_t i4Variant[16];
    MakeVariant(3, i4Variant); // VT_I4
    int32_t i4Value = 42;
    std::memcpy(i4Variant + 8, &i4Value, sizeof(i4Value));
    i4Params.rgvarg = i4Variant;
    Expect(typedInvoke(reinterpret_cast<void*>(typedDisp), computeDispId, nullIid, 0, 1,
                       &i4Params, nullptr, nullptr, nullptr) == kComHrOk,
           "Invoke with a VT_I4 argument -> S_OK");

    // VT_R8 argument (an 8-byte double at offset 8) -> S_OK.
    TestDispParams r8Params = { nullptr, nullptr, 1, 0 };
    uint8_t r8Variant[16];
    MakeVariant(5, r8Variant); // VT_R8
    double r8Value = 3.5;
    std::memcpy(r8Variant + 8, &r8Value, sizeof(r8Value));
    r8Params.rgvarg = r8Variant;
    Expect(typedInvoke(reinterpret_cast<void*>(typedDisp), computeDispId, nullIid, 0, 1,
                       &r8Params, nullptr, nullptr, nullptr) == kComHrOk,
           "Invoke with a VT_R8 argument -> S_OK");

    // VT_BSTR argument (a pointer-sized string at offset 8) -> S_OK.
    TestDispParams bstrParams = { nullptr, nullptr, 1, 0 };
    uint8_t bstrVariant[16];
    MakeVariant(8, bstrVariant); // VT_BSTR
    wchar_t bstrArg[] = L"hello";
    const void* bstrPtr = bstrArg;
    std::memcpy(bstrVariant + 8, &bstrPtr, sizeof(bstrPtr));
    bstrParams.rgvarg = bstrVariant;
    Expect(typedInvoke(reinterpret_cast<void*>(typedDisp), computeDispId, nullIid, 0, 1,
                       &bstrParams, nullptr, nullptr, nullptr) == kComHrOk,
           "Invoke with a VT_BSTR argument -> S_OK");

    // Multiple arguments (VT_I4 + VT_R8 in the rgvarg array -- rgvarg[0] is
    // the LAST argument, so the array carries [R8, I4]) -> S_OK.
    TestDispParams multiParams = { nullptr, nullptr, 2, 0 };
    uint8_t multiVariants[2][16];
    MakeVariant(3, multiVariants[1]); // rgvarg[1] = the FIRST arg = VT_I4
    int32_t i4First = 7;
    std::memcpy(multiVariants[1] + 8, &i4First, sizeof(i4First));
    MakeVariant(5, multiVariants[0]); // rgvarg[0] = the LAST arg = VT_R8
    double r8Last = 2.5;
    std::memcpy(multiVariants[0] + 8, &r8Last, sizeof(r8Last));
    multiParams.rgvarg = multiVariants;
    Expect(typedInvoke(reinterpret_cast<void*>(typedDisp), computeDispId, nullIid, 0, 1,
                       &multiParams, nullptr, nullptr, nullptr) == kComHrOk,
           "Invoke with a VT_I4 + VT_R8 argument pair -> S_OK");

    // An unsupported argument type (VT_DATE, outside the typed subset) ->
    // DISP_E_TYPEMISMATCH.
    TestDispParams dateParams = { nullptr, nullptr, 1, 0 };
    uint8_t dateVariant[16];
    MakeVariant(7, dateVariant); // VT_DATE
    dateParams.rgvarg = dateVariant;
    Expect(typedInvoke(reinterpret_cast<void*>(typedDisp), computeDispId, nullIid, 0, 1,
                       &dateParams, nullptr, nullptr, nullptr) == kComHrTypeMismatch,
           "Invoke with a VT_DATE argument -> DISP_E_TYPEMISMATCH");

    // A return VARIANT is written (VT_EMPTY -- zeroed 16 bytes) even when the
    // call carries typed arguments.
    uint8_t typedResult[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    Expect(typedInvoke(reinterpret_cast<void*>(typedDisp), computeDispId, nullIid, 0, 1,
                       &i4Params, typedResult, nullptr, nullptr) == kComHrOk &&
           typedResult[0] == 0 && typedResult[15] == 0,
           "Invoke writes a zeroed VT_EMPTY return VARIANT into pVarResult");

    // Release the typed-call CCW (create reference + QI reference).
    Expect(icall_com_release(typedDisp) == 1,
           "typed-call first Release drops to 1");
    Expect(icall_com_release(typedDisp) == 0,
           "typed-call second Release destroys the CCW");

    // Release the CCW through its IDispatch reference: the create reference
    // (1) plus the QI reference (2) -- two Releases to 0 destroy it.
    Expect(icall_com_release(winDisp) == 1,
           "first Release of the IDispatch reference drops to 1");
    Expect(icall_com_release(winDisp) == 0,
           "releasing the IDispatch reference destroys the CCW");

    // GetIDispatchForObject on a real automation RCW (Shell.Application is
    // IDispatch) returns its native IDispatch pointer (AddRef'd).
    il2cpp::Il2CppObject* shellDispObj = icall_com_create_object(
        reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("Shell.Application")));
    Expect(shellDispObj != nullptr, "create_object for the IDispatch RCW test");
    const intptr_t shellIDisp = icall_com_get_idispatch_for_object(shellDispObj);
    Expect(shellIDisp != 0, "GetIDispatchForObject(Shell RCW) returns an IDispatch");
    icall_com_release(shellIDisp);
    icall_com_release_com_object(shellDispObj);

    // QI the real Shell RCW's IUnknown for IDispatch directly (a real
    // automation object implements IDispatch -- the ActiveX host contract).
    il2cpp::Il2CppObject* shellQiObj = icall_com_create_object(
        reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("Shell.Application")));
    const intptr_t shellUnkQi = icall_com_get_iunknown_for_object(shellQiObj);
    intptr_t shellDispQi = 0;
    Expect(icall_com_query_interface(shellUnkQi, kComIidIDispatch, &shellDispQi) == kComHrOk &&
           shellDispQi != 0,
           "Shell QI for IDispatch succeeds (real automation object)");
    icall_com_release(shellDispQi);
    icall_com_release(shellUnkQi);
    icall_com_release_com_object(shellQiObj);

    //------------------------------------------------------------------------
    // 9. IOleObject / IOleClientSite (phase 4 -- the ActiveX hosting surface,
    //    the WinForms AxHost stage-3 dependency). The CCW is now a
    //    MULTI-VTABLE object: QI for IID_IOleObject returns a pointer to the
    //    OLE view (&ccw->oleVtbl -- a distinct address inside the CCW whose
    //    first word is the 24-slot IOleObject vtable), NOT the identity
    //    pointer. Drive the views exactly as a native ActiveX container does.
    //------------------------------------------------------------------------
    const intptr_t oleCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(oleCcw != 0, "ccw_create returns the identity CCW");

    // QI for IOleObject returns S_OK and a DIFFERENT interface pointer (the
    // OLE view -- the slot-3 collision with IDispatch's GetTypeInfoCount
    // forces a separate vtable).
    intptr_t oleObj = 0;
    Expect(icall_com_query_interface(oleCcw, kComIidIOleObject, &oleObj) == kComHrOk &&
           oleObj != oleCcw,
           "QI for IOleObject returns a distinct OLE view pointer");
    intptr_t oleSite = 0;
    Expect(icall_com_query_interface(oleCcw, kComIidIOleClientSite, &oleSite) == kComHrOk &&
           oleSite != oleCcw,
           "QI for IOleClientSite returns a distinct site view pointer");

    // The OLE view QIs for IUnknown and returns the BASE identity address
    // (COM's QI identity rule -- every view answers IUnknown with the same
    // pointer). Balance the added QI references.
    intptr_t oleQi = 0;
    Expect(icall_com_query_interface(oleObj, kComIidIUnknown, &oleQi) == kComHrOk &&
           oleQi == oleCcw,
           "OLE view QI for IUnknown returns the base identity pointer");
    icall_com_release(oleQi);

    // The OLE view's vtable is NOT the identity vtable (its slot 3 is
    // SetClientSite, not GetTypeInfoCount -- verify by reading the vtable
    // pointer: the two interface pointers carry different vtables).
    Expect(ComVtable(oleObj) != ComVtable(oleCcw),
           "the IOleObject view carries a separate vtable");

    // SetClientSite (slot 3) stores the container's site; GetClientSite
    // (slot 4) returns it (the minimal hosting handshake). The site here is
    // a fake native IOleClientSite* (any non-null native pointer -- the CCW
    // stores it, never dereferences it in phase 4).
    NativeSetClientSiteFn setClientSite =
        reinterpret_cast<NativeSetClientSiteFn>(const_cast<void*>(ComVtable(oleObj)[kComOleSlotSetClientSite]));
    NativeGetClientSiteFn getClientSite =
        reinterpret_cast<NativeGetClientSiteFn>(const_cast<void*>(ComVtable(oleObj)[4]));
    void* fakeSite = reinterpret_cast<void*>(0x1234);
    void* gotSite = nullptr;
    Expect(setClientSite(reinterpret_cast<void*>(oleObj), fakeSite) == kComHrOk &&
           getClientSite(reinterpret_cast<void*>(oleObj), &gotSite) == kComHrOk &&
           gotSite == fakeSite,
           "SetClientSite stores / GetClientSite returns the container site");
    void* gotSite2 = reinterpret_cast<void*>(0x9999);
    Expect(setClientSite(reinterpret_cast<void*>(oleObj), nullptr) == kComHrOk &&
           getClientSite(reinterpret_cast<void*>(oleObj), &gotSite2) == kComHrOk &&
           gotSite2 == nullptr,
           "SetClientSite(null) clears the stored site");

    // Close (slot 6) tears down the hosting session (clears the site, S_OK).
    NativeOleCloseFn oleClose =
        reinterpret_cast<NativeOleCloseFn>(const_cast<void*>(ComVtable(oleObj)[kComOleSlotClose]));
    Expect(oleClose(reinterpret_cast<void*>(oleObj), 0u) == kComHrOk,
           "IOleObject::Close returns S_OK");

    // DoVerb (slot 11) accepts the WinForms AxHost activation verb
    // (OLEIVERB_INPLACEACTIVATE == -5) with S_OK.
    NativeDoVerbFn doVerb =
        reinterpret_cast<NativeDoVerbFn>(const_cast<void*>(ComVtable(oleObj)[kComOleSlotDoVerb]));
    Expect(doVerb(reinterpret_cast<void*>(oleObj), kComOleVerbInPlaceActivate,
                  nullptr, nullptr, 0, nullptr, nullptr) == kComHrOk,
           "IOleObject::DoVerb(OLEIVERB_INPLACEACTIVATE) returns S_OK");

    // Update / IsUpToDate (slots 13/14) succeed -- the minimal object is
    // always up to date.
    NativeUpdateFn oleUpdate =
        reinterpret_cast<NativeUpdateFn>(const_cast<void*>(ComVtable(oleObj)[13]));
    Expect(oleUpdate(reinterpret_cast<void*>(oleObj)) == kComHrOk &&
           oleUpdate(reinterpret_cast<void*>(oleObj)) == kComHrOk,
           "IOleObject::Update / IsUpToDate return S_OK");

    // IOleClientSite::GetContainer (slot 5) -- the documented no-container
    // answer: E_NOTIMPL with a null out pointer.
    NativeGetContainerFn getContainer =
        reinterpret_cast<NativeGetContainerFn>(const_cast<void*>(ComVtable(oleSite)[kComSiteSlotGetContainer]));
    void* container = reinterpret_cast<void*>(0x7777);
    Expect(getContainer(reinterpret_cast<void*>(oleSite), &container) == kComHrNotImpl &&
           container == nullptr,
           "IOleClientSite::GetContainer -> E_NOTIMPL with a null container");

    // The OOB guard knows the new vtable lengths: slot 24 on the IOleObject
    // view and slot 9 on the IOleClientSite view are OOB.
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(oleObj, 24, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "call_vtable_slot(slot 24 on the IOleObject view) -> AOOException");
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(oleSite, 9, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "call_vtable_slot(slot 9 on the IOleClientSite view) -> AOOException");

    // The identity view is unchanged: slot 7 is still OOB (7-slot vtable).
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(oleCcw, 7, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "identity view slot 7 is still OOB (7-slot vtable)");

    // Release the three QI references (identity 1 + ole view 1 + site view 1
    // -> the create reference + 2 = 3 Releases to 0 destroy the CCW).
    Expect(icall_com_release(oleSite) == 2,
           "releasing the site view reference drops to 2");
    Expect(icall_com_release(oleObj) == 1,
           "releasing the OLE view reference drops to 1");
    Expect(icall_com_release(oleCcw) == 0,
           "releasing the identity reference destroys the CCW");

    //------------------------------------------------------------------------
    // 10. IConnectionPointContainer / IConnectionPoint (phase 5 -- the
    //     event-source surface, the ActiveX control events dependency). The
    //     CCW gains two more views: the container view (&ccw->cpcVtbl) and
    //     the per-event-interface connection point views (the cps[] array).
    //     The managed wiring layer declares event-source IIDs via
    //     icall_com_ccw_register_connection_point; a native host then QIs for
    //     IConnectionPointContainer, FindConnectionPoint(eventIID) and
    //     Advise(sink). Drive the views exactly as a native ActiveX host does.
    //------------------------------------------------------------------------
    const intptr_t evtCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(evtCcw != 0, "ccw_create returns the event-source CCW");

    // Register the fake event interface on the CCW (the phase-5 registry).
    Expect(icall_com_ccw_register_connection_point(evtCcw, kEventIid) == kComHrOk,
           "register_connection_point declares an event interface");
    Expect(icall_com_ccw_register_connection_point(evtCcw, kEventIid) == kComHrOk,
           "register_connection_point is idempotent for a duplicate IID");
    const uint8_t zeroIid[16] = { 0 };
    Expect(icall_com_ccw_register_connection_point(evtCcw, zeroIid) == kComHrInvalidArg,
           "register_connection_point(IID_NULL) -> E_INVALIDARG");
    Expect(icall_com_ccw_register_connection_point(evtCcw, kComIidIUnknown) == kComHrInvalidArg,
           "register_connection_point(IID_IUnknown) -> E_INVALIDARG");
    Expect(ThrowsDotNet([&] { icall_com_ccw_register_connection_point(0, kEventIid); }, kArgumentNull),
           "register_connection_point(null ccw) -> ArgumentNullException");

    // QI for IConnectionPointContainer returns a DISTINCT view (not the
    // identity pointer).
    intptr_t cpc = 0;
    Expect(icall_com_query_interface(evtCcw, kComIidIConnectionPointContainer, &cpc) == kComHrOk &&
           cpc != evtCcw,
           "QI for IConnectionPointContainer returns a distinct view");

    // The container view QIs back to the base identity address (COM identity
    // rule -- every view answers IUnknown with the same pointer). Balance the
    // added reference.
    intptr_t cpcBack = 0;
    Expect(icall_com_query_interface(cpc, kComIidIUnknown, &cpcBack) == kComHrOk &&
           cpcBack == evtCcw,
           "container view QI for IUnknown returns the identity pointer");
    icall_com_release(cpcBack);

    // FindConnectionPoint (slot 4): the registered event IID returns the
    // connection point view (AddRef'd -- a distinct address).
    NativeFindConnectionPointFn findConnectionPoint =
        reinterpret_cast<NativeFindConnectionPointFn>(
            const_cast<void*>(ComVtable(cpc)[kComCpcSlotFindConnectionPoint]));
    intptr_t cpt = 0;
    Expect(findConnectionPoint(reinterpret_cast<void*>(cpc), kEventIid,
                               reinterpret_cast<void**>(&cpt)) == kComHrOk &&
           cpt != evtCcw && cpt != cpc,
           "FindConnectionPoint(registered IID) returns the CP view");

    // An unknown IID -> CONNECT_E_NOCONNECTION with a null out pointer.
    intptr_t cptNone = 0x5678;
    Expect(findConnectionPoint(reinterpret_cast<void*>(cpc), kSomeOtherIid,
                               reinterpret_cast<void**>(&cptNone)) == kComHrNoConnection &&
           cptNone == 0,
           "FindConnectionPoint(unknown IID) -> CONNECT_E_NOCONNECTION + null out");

    // GetConnectionInterface (slot 3) returns the event interface IID the
    // connection point was created for.
    NativeGetConnectionInterfaceFn getConnectionInterface =
        reinterpret_cast<NativeGetConnectionInterfaceFn>(
            const_cast<void*>(ComVtable(cpt)[kComCpSlotGetConnectionInterface]));
    uint8_t gotIid[16] = { 0 };
    Expect(getConnectionInterface(reinterpret_cast<void*>(cpt), gotIid) == kComHrOk &&
           std::memcmp(gotIid, kEventIid, 16) == 0,
           "GetConnectionInterface returns the event interface IID");

    // GetConnectionPointContainer (slot 4) returns the SAME container view
    // (AddRef'd -- balance the added reference).
    NativeGetCpcFn getConnectionPointContainer =
        reinterpret_cast<NativeGetCpcFn>(
            const_cast<void*>(ComVtable(cpt)[kComCpSlotGetConnectionPointContainer]));
    intptr_t cpcAgain = 0;
    Expect(getConnectionPointContainer(reinterpret_cast<void*>(cpt),
                                       reinterpret_cast<void**>(&cpcAgain)) == kComHrOk &&
           cpcAgain == cpc,
           "GetConnectionPointContainer returns the same container view");
    icall_com_release(cpcAgain);

    // Advise (slot 5): a sink that does NOT implement the event interface ->
    // CONNECT_E_CANNOTCONNECT (a CCW QIs E_NOINTERFACE for the fake event
    // IID) -- checked before any sink is connected.
    NativeAdviseFn advise =
        reinterpret_cast<NativeAdviseFn>(const_cast<void*>(ComVtable(cpt)[kComCpSlotAdvise]));
    const intptr_t badSinkCcw = icall_com_ccw_create(MakeManagedTarget());
    uint32_t cookieBad = 0;
    Expect(advise(reinterpret_cast<void*>(cpt), reinterpret_cast<void*>(badSinkCcw), &cookieBad) == kComHrCannotConnect,
           "Advise(sink without the event interface) -> CONNECT_E_CANNOTCONNECT");
    Expect(icall_com_release(badSinkCcw) == 0,
           "releasing the failed-sink CCW reference destroys it");

    // A valid sink (one implementing the event interface) connects; the
    // connection point QIs the sink (its QI AddRefs -> refs 1) and returns a
    // 1-based cookie.
    TestSink sink = { kTestSinkVtbl, 0 };
    uint32_t cookie = 0;
    Expect(advise(reinterpret_cast<void*>(cpt), &sink, &cookie) == kComHrOk &&
           cookie == 1 && sink.refs == 1,
           "Advise(valid sink) -> S_OK, cookie 1, holds the sink reference");

    // Stage 9 -- multi-sink broadcast: a connection point holds up to
    // kCcwMaxSinksPerCp connected sinks, each its own monotonic cookie. A
    // second Advise on the same connection point now CONNECTS (cookie 2) --
    // CONNECT_E_ADVISELIMIT is reserved for the exhausted-capacity case.
    uint32_t cookieDup = 0;
    Expect(advise(reinterpret_cast<void*>(cpt), &sink, &cookieDup) == kComHrOk &&
           cookieDup == 2 && sink.refs == 2,
           "second Advise connects a second connection (cookie 2, sink refs 2)");

    // Unadvise (slot 6) releases the matching sink reference and frees the
    // connection; a stale cookie -> CONNECT_E_NOCONNECTION.
    NativeUnadviseFn unadvise =
        reinterpret_cast<NativeUnadviseFn>(const_cast<void*>(ComVtable(cpt)[kComCpSlotUnadvise]));
    Expect(unadvise(reinterpret_cast<void*>(cpt), cookie) == kComHrOk && sink.refs == 1,
           "Unadvise(cookie 1) releases one sink reference");
    Expect(unadvise(reinterpret_cast<void*>(cpt), cookie) == kComHrNoConnection,
           "Unadvise(stale cookie) -> CONNECT_E_NOCONNECTION");

    // Re-Advise after Unadvise gets the next monotonic cookie.
    uint32_t cookie2 = 0;
    Expect(advise(reinterpret_cast<void*>(cpt), &sink, &cookie2) == kComHrOk && cookie2 == 3,
           "re-Advise after Unadvise returns the next cookie (monotonic)");

    // Enumeration minimal surface: EnumConnections / EnumConnectionPoints ->
    // E_NOTIMPL with a null out pointer (the documented phase-5 answer).
    NativeEnumConnectionsFn enumConnections =
        reinterpret_cast<NativeEnumConnectionsFn>(
            const_cast<void*>(ComVtable(cpt)[kComCpSlotEnumConnections]));
    void* connEnum = reinterpret_cast<void*>(0x1111);
    Expect(enumConnections(reinterpret_cast<void*>(cpt), &connEnum) == kComHrNotImpl &&
           connEnum == nullptr,
           "IConnectionPoint::EnumConnections -> E_NOTIMPL + null out");
    NativeEnumCpcFn enumConnectionPoints =
        reinterpret_cast<NativeEnumCpcFn>(
            const_cast<void*>(ComVtable(cpc)[kComCpcSlotEnumConnectionPoints]));
    void* cpcEnum = reinterpret_cast<void*>(0x2222);
    Expect(enumConnectionPoints(reinterpret_cast<void*>(cpc), &cpcEnum) == kComHrNotImpl &&
           cpcEnum == nullptr,
           "EnumConnectionPoints -> E_NOTIMPL + null out");

    // The connection point view QIs as IConnectionPoint (this view) and back
    // to the identity IUnknown. Balance the added references.
    intptr_t cpQi = 0;
    Expect(icall_com_query_interface(cpt, kComIidIConnectionPoint, &cpQi) == kComHrOk &&
           cpQi == cpt,
           "connection point view QIs as IConnectionPoint");
    icall_com_release(cpQi);
    intptr_t cpIdentity = 0;
    Expect(icall_com_query_interface(cpt, kComIidIUnknown, &cpIdentity) == kComHrOk &&
           cpIdentity == evtCcw,
           "connection point view QI for IUnknown returns the identity pointer");
    icall_com_release(cpIdentity);

    // The OOB guard knows the two new vtable lengths: slot 5 on the container
    // view and slot 8 on the connection point view are OOB.
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(cpc, 5, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "call_vtable_slot(slot 5 on the container view) -> AOOException");
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(cpt, 8, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "call_vtable_slot(slot 8 on the connection point view) -> AOOException");

    // Balance the event-source CCW references (create 1 + cpc QI + cpt QI):
    // releasing the identity to 0 destroys the CCW -- and the still-connected
    // sink (cookie2 = 2) is released by the CCW destructor (teardown
    // contract).
    Expect(icall_com_release(cpt) == 2,
           "releasing the CP view reference drops to 2");
    Expect(icall_com_release(cpc) == 1,
           "releasing the container view reference drops to 1");
    Expect(icall_com_release(evtCcw) == 0,
           "releasing the identity reference destroys the event-source CCW");
    Expect(sink.refs == 0,
           "the CCW destructor releases a still-connected sink");

    //------------------------------------------------------------------------
    // 11. Event-source fire (stage 7 -- icall_com_ccw_fire_event). The
    //     managed event-wiring layer fires the Advise'd sink's event-interface
    //     method (vtable slot 3 + methodIndex) with the intptr carriers
    //     a0..a3. No connection point registered for the IID -> S_FALSE (no
    //     listener); registered but no connected sink -> S_FALSE; a connected
    //     sink -> the sink's method HRESULT (no AddRef -- observation only).
    //------------------------------------------------------------------------
    const intptr_t fireCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(fireCcw != 0, "fire: ccw_create returns the fire-test CCW");

    // No connection point registered for the fired IID -> S_FALSE (no
    // listener -- a sink is never touched).
    TestFireSink sinkA = { kTestFireSinkVtbl, 0, 0, -1, 0, 0, 0, 0 };
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrSFALSE &&
           sinkA.callCount == 0,
           "fire on an unregistered IID -> S_FALSE (no listener)");

    // Registered but no sink Advise'd -> S_FALSE.
    Expect(icall_com_ccw_register_connection_point(fireCcw, kEventIid) == kComHrOk,
           "fire: register the event interface");
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrSFALSE,
           "fire on a registered-but-unconnected IID -> S_FALSE");

    // Drive the connection point exactly as a native ActiveX host does: QI
    // the container view, FindConnectionPoint(kEventIid), Advise(sinkA).
    intptr_t fireCpc = 0;
    Expect(icall_com_query_interface(fireCcw, kComIidIConnectionPointContainer, &fireCpc) == kComHrOk,
           "fire: QI for IConnectionPointContainer");
    NativeFindConnectionPointFn fireFindCp =
        reinterpret_cast<NativeFindConnectionPointFn>(
            const_cast<void*>(ComVtable(fireCpc)[kComCpcSlotFindConnectionPoint]));
    intptr_t fireCpt = 0;
    Expect(fireFindCp(reinterpret_cast<void*>(fireCpc), kEventIid,
                      reinterpret_cast<void**>(&fireCpt)) == kComHrOk,
           "fire: FindConnectionPoint(kEventIid)");
    NativeAdviseFn fireAdvise =
        reinterpret_cast<NativeAdviseFn>(
            const_cast<void*>(ComVtable(fireCpt)[kComCpSlotAdvise]));
    uint32_t fireCookieA = 0;
    Expect(fireAdvise(reinterpret_cast<void*>(fireCpt), &sinkA, &fireCookieA) == kComHrOk &&
           fireCookieA == 1 && sinkA.refs == 1,
           "fire: Advise(sinkA) -> S_OK, cookie 1");

    // Firing method 0 (vtable slot 3 = OnEventA) delivers to the sink and
    // returns the sink's method HRESULT (S_OK), recording (method 0, a0).
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid, 0, 0xABCD, 0, 0, 0) == kComHrOk &&
           sinkA.callCount == 1 && sinkA.lastMethod == 0 && sinkA.lastArg0 == 0xABCD,
           "fire method 0 -> S_OK, sink records (method 0, a0)");

    // All four intptr carriers pass through byte-exact (distinct 64-bit
    // sentinel patterns -- the x64 intptr carrier width).
    const intptr_t sentinel0 = static_cast<intptr_t>(0x1122334455667788ULL);
    const intptr_t sentinel1 = static_cast<intptr_t>(0x8877665544332211ULL);
    const intptr_t sentinel2 = static_cast<intptr_t>(0xDEADBEEFCAFEBABEULL);
    const intptr_t sentinel3 = static_cast<intptr_t>(0x0123456789ABCDEFULL);
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid, 0,
                                    sentinel0, sentinel1, sentinel2, sentinel3) == kComHrOk &&
           sinkA.callCount == 2 && sinkA.lastArg0 == sentinel0 &&
           sinkA.lastArg1 == sentinel1 && sinkA.lastArg2 == sentinel2 &&
           sinkA.lastArg3 == sentinel3,
           "fire passes all four intptr carriers through unchanged");

    // Firing method 1 (vtable slot 4 = OnEventB) reaches the second event
    // method of the same event interface.
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid, 1, 0, 0, 0, 0) == kComHrOk &&
           sinkA.callCount == 3 && sinkA.lastMethod == 1,
           "fire method 1 (vtable slot 4 = OnEventB) -> S_OK");

    // The fire does NOT AddRef the sink (observation only -- the Advise QI
    // reference is the only one the connection point holds).
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrOk &&
           sinkA.refs == 1,
           "fire does not change the sink's reference count (no AddRef)");

    // Register a SECOND event interface on the same CCW and Advise a second
    // sink; fires route by the event interface IID: kEventIid reaches only
    // sinkA, kEventIid2 only sinkB.
    Expect(icall_com_ccw_register_connection_point(fireCcw, kEventIid2) == kComHrOk,
           "fire: register a second event interface");
    intptr_t fireCptB = 0;
    Expect(fireFindCp(reinterpret_cast<void*>(fireCpc), kEventIid2,
                      reinterpret_cast<void**>(&fireCptB)) == kComHrOk,
           "fire: FindConnectionPoint(kEventIid2)");
    TestFireSink sinkB = { kTestFireSinkVtbl, 0, 0, -1, 0, 0, 0, 0 };
    uint32_t fireCookieB = 0;
    Expect(fireAdvise(reinterpret_cast<void*>(fireCptB), &sinkB, &fireCookieB) == kComHrOk &&
           fireCookieB == 1 && sinkB.refs == 1,
           "fire: Advise(sinkB) -> S_OK, cookie 1");

    const int32_t sinkACount = sinkA.callCount;
    const int32_t sinkBCount = sinkB.callCount;
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrOk &&
           sinkA.callCount == sinkACount + 1 && sinkB.callCount == sinkBCount,
           "fire kEventIid reaches only sinkA (sinkB untouched)");
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid2, 0, 0, 0, 0, 0) == kComHrOk &&
           sinkA.callCount == sinkACount + 1 && sinkB.callCount == sinkBCount + 1,
           "fire kEventIid2 reaches only sinkB (sinkA untouched)");

    // Unadvise disconnects the sink: fire -> S_FALSE (no listener) and the
    // connection point releases the sink reference.
    NativeUnadviseFn fireUnadvise =
        reinterpret_cast<NativeUnadviseFn>(
            const_cast<void*>(ComVtable(fireCpt)[kComCpSlotUnadvise]));
    Expect(fireUnadvise(reinterpret_cast<void*>(fireCpt), fireCookieA) == kComHrOk &&
           sinkA.refs == 0,
           "fire: Unadvise releases sinkA's reference");
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrSFALSE,
           "fire after Unadvise -> S_FALSE (no sink connected)");

    // Consecutive fires deliver exactly once each (callCount is the delivery
    // counter -- the exact-once contract).
    const int32_t sinkBCount3 = sinkB.callCount;
    Expect(icall_com_ccw_fire_event(fireCcw, kEventIid2, 1, 0, 0, 0, 0) == kComHrOk &&
           icall_com_ccw_fire_event(fireCcw, kEventIid2, 1, 0, 0, 0, 0) == kComHrOk &&
           icall_com_ccw_fire_event(fireCcw, kEventIid2, 1, 0, 0, 0, 0) == kComHrOk &&
           sinkB.callCount == sinkBCount3 + 3,
           "three consecutive fires deliver exactly three calls");

    // Dual-CCW isolation: two CCWs each register the SAME event IID and
    // Advise their own sink; firing one CCW's IID reaches only that CCW's
    // sink.
    const intptr_t fireCcwA = icall_com_ccw_create(MakeManagedTarget());
    Expect(icall_com_ccw_register_connection_point(fireCcwA, kEventIid) == kComHrOk,
           "fire: register kEventIid on CCW-A");
    intptr_t fireCpcA = 0;
    Expect(icall_com_query_interface(fireCcwA, kComIidIConnectionPointContainer, &fireCpcA) == kComHrOk,
           "fire: QI for the container view on CCW-A");
    intptr_t fireCptA = 0;
    Expect(fireFindCp(reinterpret_cast<void*>(fireCpcA), kEventIid,
                      reinterpret_cast<void**>(&fireCptA)) == kComHrOk,
           "fire: FindConnectionPoint(kEventIid) on CCW-A");
    TestFireSink sinkA2 = { kTestFireSinkVtbl, 0, 0, -1, 0, 0, 0, 0 };
    uint32_t fireCookieA2 = 0;
    Expect(fireAdvise(reinterpret_cast<void*>(fireCptA), &sinkA2, &fireCookieA2) == kComHrOk &&
           sinkA2.refs == 1,
           "fire: Advise(sinkA2) on CCW-A");

    const intptr_t fireCcwB = icall_com_ccw_create(MakeManagedTarget());
    Expect(icall_com_ccw_register_connection_point(fireCcwB, kEventIid) == kComHrOk,
           "fire: register kEventIid on CCW-B");
    intptr_t fireCpcB = 0;
    Expect(icall_com_query_interface(fireCcwB, kComIidIConnectionPointContainer, &fireCpcB) == kComHrOk,
           "fire: QI for the container view on CCW-B");
    intptr_t fireCptB2 = 0;
    Expect(fireFindCp(reinterpret_cast<void*>(fireCpcB), kEventIid,
                      reinterpret_cast<void**>(&fireCptB2)) == kComHrOk,
           "fire: FindConnectionPoint(kEventIid) on CCW-B");
    TestFireSink sinkB2 = { kTestFireSinkVtbl, 0, 0, -1, 0, 0, 0, 0 };
    uint32_t fireCookieB2 = 0;
    Expect(fireAdvise(reinterpret_cast<void*>(fireCptB2), &sinkB2, &fireCookieB2) == kComHrOk &&
           sinkB2.refs == 1,
           "fire: Advise(sinkB2) on CCW-B");

    Expect(icall_com_ccw_fire_event(fireCcwA, kEventIid, 0, 0, 0, 0, 0) == kComHrOk &&
           sinkA2.callCount == 1 && sinkB2.callCount == 0,
           "fire CCW-A's IID reaches only CCW-A's sink");
    Expect(icall_com_ccw_fire_event(fireCcwB, kEventIid, 0, 0, 0, 0, 0) == kComHrOk &&
           sinkA2.callCount == 1 && sinkB2.callCount == 1,
           "fire CCW-B's IID reaches only CCW-B's sink");

    // Bounds/arg defense: null ccw / null iid -> ArgumentNullException;
    // methodIndex out of 0..124 -> ArgumentOutOfRangeException.
    Expect(ThrowsDotNet([&] { icall_com_ccw_fire_event(0, kEventIid, 0, 0, 0, 0, 0); }, kArgumentNull),
           "fire(null ccw) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_ccw_fire_event(fireCcw, nullptr, 0, 0, 0, 0, 0); }, kArgumentNull),
           "fire(null iid) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_ccw_fire_event(fireCcw, kEventIid, -1, 0, 0, 0, 0); }, kArgumentOutOfRange),
           "fire(negative methodIndex) -> ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_com_ccw_fire_event(fireCcw, kEventIid, 125, 0, 0, 0, 0); }, kArgumentOutOfRange),
           "fire(methodIndex 125) -> ArgumentOutOfRangeException");

    // Balance the references: CCW-A (create 1 + cpc QI + cpt QI) and CCW-B
    // release to 0 -- their destructors release the still-connected sinks.
    Expect(icall_com_release(fireCptA) == 2, "fire: releasing the CCW-A CP view reference");
    Expect(icall_com_release(fireCpcA) == 1, "fire: releasing the CCW-A container view reference");
    Expect(icall_com_release(fireCcwA) == 0,
           "fire: releasing the CCW-A identity reference destroys it");
    Expect(sinkA2.refs == 0, "fire: the CCW-A destructor releases a still-connected sink");
    Expect(icall_com_release(fireCptB2) == 2, "fire: releasing the CCW-B CP view reference");
    Expect(icall_com_release(fireCpcB) == 1, "fire: releasing the CCW-B container view reference");
    Expect(icall_com_release(fireCcwB) == 0,
           "fire: releasing the CCW-B identity reference destroys it");
    Expect(sinkB2.refs == 0, "fire: the CCW-B destructor releases a still-connected sink");

    // fireCcw: create 1 + cpc QI 1 + cpt A QI 1 + cpt B QI 1 -- release to 0
    // (sinkA was Unadvise'd; sinkB is released by the destructor).
    Expect(icall_com_release(fireCptB) == 3, "fire: releasing the fireCcw CP-B view reference");
    Expect(icall_com_release(fireCpt) == 2, "fire: releasing the fireCcw CP-A view reference");
    Expect(icall_com_release(fireCpc) == 1, "fire: releasing the fireCcw container view reference");
    Expect(icall_com_release(fireCcw) == 0,
           "fire: releasing the fireCcw identity reference destroys it");
    Expect(sinkB.refs == 0, "fire: the fireCcw destructor releases a still-connected sink");

    //------------------------------------------------------------------------
    // 11b. Event-source dispatch fire (stage 8 -- the IDispatch VARIANT fire
    //      variant, icall_com_ccw_fire_dispatch_event). A WinForms AxHost's
    //      real automation event sink is IDispatch-shaped: the source
    //      delivers each event by calling the sink's IDispatch::Invoke with
    //      DISPID_FIRST + n (the oaidl dispinterface event numbering) and a
    //      DISPPARAMS whose rgvarg carries the event arguments as VARIANTs.
    //      The fire variant reuses the stage-7 connection-point sink storage
    //      (view->sink) and the idispatch_invoke DISPPARAMS pointer shape;
    //      the sink's Invoke (vtable slot 6) is authoritative for dispId and
    //      VARIANT validation (the source only forwards).
    //------------------------------------------------------------------------
    const intptr_t dFireCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(dFireCcw != 0, "dispatch-fire: ccw_create returns the fire-test CCW");

    // No connection point registered for the fired IID -> S_FALSE (no
    // listener -- the sink is never touched).
    TestDispatchSink dSinkA = { kTestDispatchSinkVtbl, 0, 0, -1, 0, {{0}} };
    TestDispParams dNoArgs = { nullptr, nullptr, 0, 0 };
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dNoArgs), 0) == kComHrSFALSE &&
           dSinkA.callCount == 0,
           "dispatch-fire on an unregistered IID -> S_FALSE (no listener)");

    // Registered but no sink Advise'd -> S_FALSE.
    Expect(icall_com_ccw_register_connection_point(dFireCcw, kEventIid) == kComHrOk,
           "dispatch-fire: register the event interface");
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dNoArgs), 0) == kComHrSFALSE,
           "dispatch-fire on a registered-but-unconnected IID -> S_FALSE");

    // Drive the connection point exactly as a native ActiveX host does: QI
    // the container view, FindConnectionPoint(kEventIid), Advise(sinkA).
    intptr_t dFireCpc = 0;
    Expect(icall_com_query_interface(dFireCcw, kComIidIConnectionPointContainer, &dFireCpc) == kComHrOk,
           "dispatch-fire: QI for IConnectionPointContainer");
    NativeFindConnectionPointFn dFindCp =
        reinterpret_cast<NativeFindConnectionPointFn>(
            const_cast<void*>(ComVtable(dFireCpc)[kComCpcSlotFindConnectionPoint]));
    intptr_t dFireCpt = 0;
    Expect(dFindCp(reinterpret_cast<void*>(dFireCpc), kEventIid,
                   reinterpret_cast<void**>(&dFireCpt)) == kComHrOk,
           "dispatch-fire: FindConnectionPoint(kEventIid)");
    NativeAdviseFn dAdvise =
        reinterpret_cast<NativeAdviseFn>(
            const_cast<void*>(ComVtable(dFireCpt)[kComCpSlotAdvise]));
    uint32_t dCookieA = 0;
    Expect(dAdvise(reinterpret_cast<void*>(dFireCpt), &dSinkA, &dCookieA) == kComHrOk &&
           dCookieA == 1 && dSinkA.refs == 1,
           "dispatch-fire: Advise(IDispatch-shaped sinkA) -> S_OK, cookie 1");

    // Firing DISPID_FIRST (2) with no args delivers to the sink and returns
    // the sink's Invoke HRESULT (S_OK); the sink records the dispId.
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dNoArgs), 0) == kComHrOk &&
           dSinkA.callCount == 1 && dSinkA.lastDispId == kComDispIdFirst,
           "dispatch-fire DISPID_FIRST -> S_OK, sink records (dispId 2)");

    // DISPID_FIRST + 1 (the next event method's dispId) reaches the sink with
    // the same numbering the host expects.
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst + 1,
                                             reinterpret_cast<intptr_t>(&dNoArgs), 0) == kComHrOk &&
           dSinkA.callCount == 2 && dSinkA.lastDispId == kComDispIdFirst + 1,
           "dispatch-fire DISPID_FIRST + 1 -> S_OK, sink records (dispId 3)");

    // VARIANT carrier passthrough: a VT_I4 argument reaches the sink
    // byte-exact (the source forwards the DISPPARAMS untouched).
    TestDispParams dI4Args = { nullptr, nullptr, 1, 0 };
    uint8_t dI4Variant[16];
    MakeVariant(3, dI4Variant); // VT_I4
    int32_t dI4Value = 42;
    std::memcpy(dI4Variant + 8, &dI4Value, sizeof(dI4Value));
    dI4Args.rgvarg = dI4Variant;
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dI4Args), 0) == kComHrOk &&
           dSinkA.callCount == 3 && dSinkA.lastCArgs == 1 &&
           std::memcmp(dSinkA.lastVariants[0], dI4Variant, 16) == 0,
           "dispatch-fire forwards a VT_I4 VARIANT to the sink byte-exact");

    // A two-argument DISPPARAMS (rgvarg[0] = the LAST arg -- the reverse
    // order convention) passes both VARIANTs through in order.
    TestDispParams dMultiArgs = { nullptr, nullptr, 2, 0 };
    uint8_t dMultiVariants[2][16];
    MakeVariant(3, dMultiVariants[1]); // first arg = VT_I4
    int32_t dI4First = 7;
    std::memcpy(dMultiVariants[1] + 8, &dI4First, sizeof(dI4First));
    MakeVariant(5, dMultiVariants[0]); // last arg = VT_R8
    double dR8Last = 2.5;
    std::memcpy(dMultiVariants[0] + 8, &dR8Last, sizeof(dR8Last));
    dMultiArgs.rgvarg = dMultiVariants;
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dMultiArgs), 0) == kComHrOk &&
           dSinkA.callCount == 4 && dSinkA.lastCArgs == 2 &&
           std::memcmp(dSinkA.lastVariants[0], dMultiVariants[0], 16) == 0 &&
           std::memcmp(dSinkA.lastVariants[1], dMultiVariants[1], 16) == 0,
           "dispatch-fire forwards a two-argument DISPPARAMS in reverse order");

    // A pVarResult carrier is forwarded to the sink's Invoke (the sink writes
    // its VT_EMPTY return into it -- the return path stays honest).
    uint8_t dRet[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                         0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dNoArgs),
                                             reinterpret_cast<intptr_t>(&dRet)) == kComHrOk &&
           dRet[0] == 0 && dRet[15] == 0,
           "dispatch-fire forwards pVarResult (the sink zeroes it)");

    // The fire does NOT AddRef the sink (observation only -- the Advise QI
    // reference is the only one the connection point holds).
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dNoArgs), 0) == kComHrOk &&
           dSinkA.refs == 1,
           "dispatch-fire does not change the sink's reference count (no AddRef)");

    // Routing by the event interface IID: a second registered interface fires
    // only its own sink.
    Expect(icall_com_ccw_register_connection_point(dFireCcw, kEventIid2) == kComHrOk,
           "dispatch-fire: register a second event interface");
    intptr_t dFireCptB = 0;
    Expect(dFindCp(reinterpret_cast<void*>(dFireCpc), kEventIid2,
                   reinterpret_cast<void**>(&dFireCptB)) == kComHrOk,
           "dispatch-fire: FindConnectionPoint(kEventIid2)");
    TestDispatchSink dSinkB = { kTestDispatchSinkVtbl, 0, 0, -1, 0, {{0}} };
    uint32_t dCookieB = 0;
    Expect(dAdvise(reinterpret_cast<void*>(dFireCptB), &dSinkB, &dCookieB) == kComHrOk &&
           dCookieB == 1 && dSinkB.refs == 1,
           "dispatch-fire: Advise(sinkB) -> S_OK, cookie 1");

    const int32_t dSinkACount = dSinkA.callCount;
    const int32_t dSinkBCount = dSinkB.callCount;
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dNoArgs), 0) == kComHrOk &&
           dSinkA.callCount == dSinkACount + 1 && dSinkB.callCount == dSinkBCount,
           "dispatch-fire kEventIid reaches only sinkA (sinkB untouched)");
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid2, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dNoArgs), 0) == kComHrOk &&
           dSinkA.callCount == dSinkACount + 1 && dSinkB.callCount == dSinkBCount + 1,
           "dispatch-fire kEventIid2 reaches only sinkB (sinkA untouched)");

    // Unadvise disconnects the sink: fire -> S_FALSE (no listener).
    NativeUnadviseFn dUnadvise =
        reinterpret_cast<NativeUnadviseFn>(
            const_cast<void*>(ComVtable(dFireCpt)[kComCpSlotUnadvise]));
    Expect(dUnadvise(reinterpret_cast<void*>(dFireCpt), dCookieA) == kComHrOk &&
           dSinkA.refs == 0,
           "dispatch-fire: Unadvise releases sinkA's reference");
    Expect(icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dNoArgs), 0) == kComHrSFALSE,
           "dispatch-fire after Unadvise -> S_FALSE (no sink connected)");

    // Bounds/arg defense: null ccw / null iid / null pDispParams ->
    // ArgumentNullException.
    Expect(ThrowsDotNet([&] { icall_com_ccw_fire_dispatch_event(0, kEventIid, kComDispIdFirst,
                                                                reinterpret_cast<intptr_t>(&dNoArgs), 0); }, kArgumentNull),
           "dispatch-fire(null ccw) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_ccw_fire_dispatch_event(dFireCcw, nullptr, kComDispIdFirst,
                                                                reinterpret_cast<intptr_t>(&dNoArgs), 0); }, kArgumentNull),
           "dispatch-fire(null iid) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_ccw_fire_dispatch_event(dFireCcw, kEventIid, kComDispIdFirst,
                                                                0, 0); }, kArgumentNull),
           "dispatch-fire(null pDispParams) -> ArgumentNullException");

    // Balance the references (create 1 + cpc QI + cpt A QI + cpt B QI).
    Expect(icall_com_release(dFireCptB) == 3, "dispatch-fire: releasing the CP-B view reference");
    Expect(icall_com_release(dFireCpt) == 2, "dispatch-fire: releasing the CP-A view reference");
    Expect(icall_com_release(dFireCpc) == 1, "dispatch-fire: releasing the container view reference");
    Expect(icall_com_release(dFireCcw) == 0,
           "dispatch-fire: releasing the identity reference destroys it");
    Expect(dSinkB.refs == 0, "dispatch-fire: the CCW destructor releases a still-connected sink");

    //------------------------------------------------------------------------
    // 11c. Multi-sink broadcast (stage 9 -- the connection point holds up to
    //      kCcwMaxSinksPerCp connected sinks and every fire delivers to ALL of
    //      them). A single Advise'd sink is unchanged; two or more sinks on
    //      the SAME event interface all receive every fire (the broadcast
    //      contract the stage-5 single-sink ADVISELIMIT deliberately deferred
    //      -- the audit-flagged future extension). Fire returns the first
    //      non-S_OK sink HRESULT (a failing sink does not silence the
    //      delivery); an exhausted sink array -> CONNECT_E_ADVISELIMIT.
    //------------------------------------------------------------------------
    const intptr_t bCastCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(bCastCcw != 0, "broadcast: ccw_create returns the broadcast-test CCW");
    Expect(icall_com_ccw_register_connection_point(bCastCcw, kEventIid) == kComHrOk,
           "broadcast: register the event interface");
    intptr_t bCastCpc = 0;
    Expect(icall_com_query_interface(bCastCcw, kComIidIConnectionPointContainer, &bCastCpc) == kComHrOk,
           "broadcast: QI for IConnectionPointContainer");
    NativeFindConnectionPointFn bCastFindCp =
        reinterpret_cast<NativeFindConnectionPointFn>(
            const_cast<void*>(ComVtable(bCastCpc)[kComCpcSlotFindConnectionPoint]));
    intptr_t bCastCpt = 0;
    Expect(bCastFindCp(reinterpret_cast<void*>(bCastCpc), kEventIid,
                       reinterpret_cast<void**>(&bCastCpt)) == kComHrOk,
           "broadcast: FindConnectionPoint(kEventIid)");
    NativeAdviseFn bCastAdvise =
        reinterpret_cast<NativeAdviseFn>(
            const_cast<void*>(ComVtable(bCastCpt)[kComCpSlotAdvise]));
    NativeUnadviseFn bCastUnadvise =
        reinterpret_cast<NativeUnadviseFn>(
            const_cast<void*>(ComVtable(bCastCpt)[kComCpSlotUnadvise]));

    // Three distinct sinks connect to the SAME connection point (the old
    // one-sink-per-CP limit): cookies 1, 2, 3, each held reference.
    TestFireSink sinkM1 = { kTestFireSinkVtbl, 0, 0, -1, 0, 0, 0, 0 };
    TestFireSink sinkM2 = { kTestFireSinkVtbl, 0, 0, -1, 0, 0, 0, 0 };
    TestFireSink sinkM3 = { kTestFireSinkVtbl, 0, 0, -1, 0, 0, 0, 0 };
    uint32_t cookieM1 = 0, cookieM2 = 0, cookieM3 = 0;
    Expect(bCastAdvise(reinterpret_cast<void*>(bCastCpt), &sinkM1, &cookieM1) == kComHrOk &&
           cookieM1 == 1 && sinkM1.refs == 1,
           "broadcast: Advise(sinkM1) -> S_OK, cookie 1");
    Expect(bCastAdvise(reinterpret_cast<void*>(bCastCpt), &sinkM2, &cookieM2) == kComHrOk &&
           cookieM2 == 2 && sinkM2.refs == 1,
           "broadcast: Advise(sinkM2) -> S_OK, cookie 2 (multi-sink)");
    Expect(bCastAdvise(reinterpret_cast<void*>(bCastCpt), &sinkM3, &cookieM3) == kComHrOk &&
           cookieM3 == 3 && sinkM3.refs == 1,
           "broadcast: Advise(sinkM3) -> S_OK, cookie 3 (multi-sink)");

    // A fire delivers to ALL connected sinks: every sink's callCount advances
    // exactly once with its own carrier (the broadcast contract).
    Expect(icall_com_ccw_fire_event(bCastCcw, kEventIid, 0, 0x1111, 0, 0, 0) == kComHrOk &&
           sinkM1.callCount == 1 && sinkM2.callCount == 1 && sinkM3.callCount == 1,
           "broadcast: one fire reaches all three sinks exactly once");
    Expect(icall_com_ccw_fire_event(bCastCcw, kEventIid, 0,
                                    static_cast<intptr_t>(0x2222),
                                    static_cast<intptr_t>(0x3333), 0, 0) == kComHrOk &&
           sinkM1.callCount == 2 && sinkM1.lastArg0 == 0x2222 &&
           sinkM2.callCount == 2 && sinkM2.lastArg0 == 0x2222 &&
           sinkM3.callCount == 2 && sinkM3.lastArg0 == 0x2222,
           "broadcast: every sink receives the same carriers");

    // A dispatch fire broadcasts to every IDispatch-shaped sink too. The
    // dispatch sink connects on its OWN event interface (kEventIid2) -- a
    // connection point's sinks are all the same shape (the fire sinks on
    // kEventIid are vtable-method sinks, not IDispatch; the broadcast delivers
    // the correct event form per connection point).
    Expect(icall_com_ccw_register_connection_point(bCastCcw, kEventIid2) == kComHrOk,
           "broadcast: register the dispatch event interface");
    intptr_t bCastCptB = 0;
    Expect(bCastFindCp(reinterpret_cast<void*>(bCastCpc), kEventIid2,
                       reinterpret_cast<void**>(&bCastCptB)) == kComHrOk,
           "broadcast: FindConnectionPoint(kEventIid2)");
    TestDispatchSink dSinkM = { kTestDispatchSinkVtbl, 0, 0, -1, 0, {{0}} };
    uint32_t dCookieM = 0;
    Expect(bCastAdvise(reinterpret_cast<void*>(bCastCptB), &dSinkM, &dCookieM) == kComHrOk &&
           dCookieM == 1 && dSinkM.refs == 1,
           "broadcast: Advise(IDispatch sinkM) on kEventIid2 -> S_OK, cookie 1");
    TestDispParams dMNoArgs = { nullptr, nullptr, 0, 0 };
    Expect(icall_com_ccw_fire_dispatch_event(bCastCcw, kEventIid2, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dMNoArgs), 0) == kComHrOk &&
           dSinkM.callCount == 1 && dSinkM.lastDispId == kComDispIdFirst,
           "broadcast: dispatch fire reaches the IDispatch sink");

    // The two event forms coexist: a kEventIid fire reaches only the fire
    // sinks, a kEventIid2 dispatch fire only the IDispatch sink.
    Expect(icall_com_ccw_fire_event(bCastCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrOk &&
           sinkM1.callCount == 3 && sinkM2.callCount == 3 && sinkM3.callCount == 3 &&
           dSinkM.callCount == 1,
           "broadcast: fire events and dispatch fires coexist on their own interfaces");
    Expect(icall_com_ccw_fire_dispatch_event(bCastCcw, kEventIid2, kComDispIdFirst + 1,
                                             reinterpret_cast<intptr_t>(&dMNoArgs), 0) == kComHrOk &&
           dSinkM.callCount == 2 && dSinkM.lastDispId == kComDispIdFirst + 1 &&
           sinkM1.callCount == 3,
           "broadcast: a dispatch fire reaches only the IDispatch sink");

    // Per-cookie Unadvise: disconnecting ONE sink leaves the others connected.
    const int32_t m1Count = sinkM1.callCount;
    const int32_t m2Count = sinkM2.callCount;
    const int32_t m3Count = sinkM3.callCount;
    Expect(bCastUnadvise(reinterpret_cast<void*>(bCastCpt), cookieM2) == kComHrOk &&
           sinkM2.refs == 0,
           "broadcast: Unadvise(cookie 2) releases only sinkM2");
    Expect(icall_com_ccw_fire_event(bCastCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrOk &&
           sinkM1.callCount == m1Count + 1 && sinkM2.callCount == m2Count &&
           sinkM3.callCount == m3Count + 1,
           "broadcast: after Unadvise, fire reaches the remaining sinks only");
    Expect(icall_com_ccw_fire_event(bCastCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrOk &&
           sinkM1.callCount == m1Count + 2 && sinkM3.callCount == m3Count + 2 &&
           dSinkM.callCount == 2,
           "broadcast: the kEventIid2 dispatch sink keeps its own connection");

    // Capacity: an exhausted sink array -> CONNECT_E_ADVISELIMIT (stage 9:
    // the limit is now per-connection-point CAPACITY, not the phase-5
    // single-sink rule). sinkM1 + sinkM3 + sinkCap fill the
    // kCcwMaxSinksPerCp = 4 array; the next Advise is the capacity overflow.
    uint32_t cookieCap = 0;
    TestFireSink sinkCap = { kTestFireSinkVtbl, 0, 0, -1, 0, 0, 0, 0 };
    Expect(bCastAdvise(reinterpret_cast<void*>(bCastCpt), &sinkCap, &cookieCap) == kComHrOk &&
           cookieCap == 4 && sinkCap.refs == 1,
           "broadcast: 4th sink Advise -> S_OK, cookie 4");
    uint32_t cookieFull = 0;
    Expect(bCastAdvise(reinterpret_cast<void*>(bCastCpt), &sinkCap, &cookieFull) == kComHrOk &&
           cookieFull == 5,
           "broadcast: 5th sink Advise fills the last slot (cookie 5)");
    uint32_t cookieOverflow = 0;
    Expect(bCastAdvise(reinterpret_cast<void*>(bCastCpt), &sinkCap, &cookieOverflow) == kComHrAdviseLimit,
           "broadcast: Advise past capacity -> CONNECT_E_ADVISELIMIT");

    // Unadvising all remaining sinks -> fire returns S_FALSE (no listener)
    // and every released sink reference is gone. sinkM2 was already
    // unadvised; sinkM1 / sinkM3 / sinkCap (slots 0/2/1) + the kEventIid2
    // dispatch sink remain.
    Expect(bCastUnadvise(reinterpret_cast<void*>(bCastCpt), cookieM1) == kComHrOk &&
           sinkM1.refs == 0,
           "broadcast: Unadvise(cookie 1) releases sinkM1");
    Expect(bCastUnadvise(reinterpret_cast<void*>(bCastCpt), cookieM3) == kComHrOk &&
           sinkM3.refs == 0,
           "broadcast: Unadvise(cookie 3) releases sinkM3");
    Expect(bCastUnadvise(reinterpret_cast<void*>(bCastCpt), cookieCap) == kComHrOk &&
           sinkCap.refs == 1,
           "broadcast: Unadvise(cookie 4) releases the sinkCap connection");
    Expect(bCastUnadvise(reinterpret_cast<void*>(bCastCpt), cookieFull) == kComHrOk &&
           sinkCap.refs == 0,
           "broadcast: Unadvise(cookie 5) releases the last sinkCap connection");
    Expect(icall_com_ccw_fire_event(bCastCcw, kEventIid, 0, 0, 0, 0, 0) == kComHrSFALSE,
           "broadcast: all kEventIid sinks unadvised -> fire returns S_FALSE");
    Expect(bCastUnadvise(reinterpret_cast<void*>(bCastCptB), dCookieM) == kComHrOk &&
           dSinkM.refs == 0,
           "broadcast: Unadvise the dispatch sink on kEventIid2");
    Expect(icall_com_ccw_fire_dispatch_event(bCastCcw, kEventIid2, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dMNoArgs), 0) == kComHrSFALSE,
           "broadcast: all kEventIid2 sinks unadvised -> dispatch fire S_FALSE");

    // Balance the broadcast references (create 1 + cpc QI + cpt A QI + cpt B
    // QI): the destructor finds no remaining connected sinks.
    Expect(icall_com_release(bCastCptB) == 3, "broadcast: releasing the CP-B view reference");
    Expect(icall_com_release(bCastCpt) == 2, "broadcast: releasing the CP-A view reference");
    Expect(icall_com_release(bCastCpc) == 1, "broadcast: releasing the container view reference");
    Expect(icall_com_release(bCastCcw) == 0,
           "broadcast: releasing the identity reference destroys the CCW");

    //------------------------------------------------------------------------
    // 14. IStream / COM streams (stage 12 -- Marshal.CreateStreamOnHGlobal,
    //     WINDOWS only). ole32 CreateStreamOnHGlobal wraps a native IStream
    //     over an HGLOBAL; the runtime exposes it as a ComStream stream object
    //     (a streamio handle-registry state of kind kCom -- NOT an RCW,
    //     IsComObject false). The stream-object surface (write/read_bytes,
    //     Seek, Length, SetLength, Close, ReadByte) delegates to the IStream
    //     through the ABI-safe icalls; get_hglobal / get_iunknown expose the
    //     HGLOBAL and the wrapped IStream (the RCW direction drives the same
    //     object through the dedicated int64_t-parameter istream icalls --
    //     the LARGE_INTEGER by-value ABI pinned by the 0x0123456789ABCDEF
    //     seek rejection discriminator (the HGLOBAL seek is 32-bit wide; a
    //     swapped carrier would have sought to the valid high-32 position).
    //------------------------------------------------------------------------
    il2cpp::Il2CppObject* comStream = icall_com_stream_create_on_hglobal(0, 1);
    Expect(comStream != nullptr,
           "CreateStreamOnHGlobal(0, true) returns a stream object");
    Expect(icall_com_is_com_object(comStream) == false,
           "the ComStream object is NOT an RCW (IsComObject false)");

    // A null HGLOBAL -> CreateStreamOnHGlobal allocates a fresh one; the
    // stream object exposes it through the HGLOBAL icall.
    const intptr_t comHg = icall_com_stream_get_hglobal(comStream);
    Expect(comHg != 0, "GetHGlobalFromStream returns a non-zero HGLOBAL");

    // The wrapped IStream (RCW direction -- the managed stream delegates to
    // it). It QIs for IUnknown: a REAL COM object behind the ComStream.
    const intptr_t comIStream = icall_com_stream_get_iunknown(comStream);
    Expect(comIStream != 0, "get_iunknown returns the wrapped IStream");
    intptr_t comQi = 0;
    Expect(icall_com_query_interface(comIStream, kComIidIUnknown, &comQi) == kComHrOk &&
           comQi != 0,
           "the wrapped IStream QIs for IUnknown (S_OK)");
    icall_com_release(comQi);

    // Write {0..255} through the STREAM OBJECT; Length and Position both
    // advance to 256 (the kCom delegation drives the IStream::Write).
    il2cpp::Il2CppArray* comData = il2cpp::il2cpp_array_new(nullptr, 1, false, 256);
    for (int32_t i = 0; i < 256; ++i)
    {
        comData->data[i] = static_cast<char>(i);
    }
    icall_stream_write_bytes(comStream, comData, 0, 256);
    Expect(icall_stream_get_length(comStream) == 256 &&
           icall_stream_get_position(comStream) == 256,
           "after writing 256 bytes, Length == Position == 256");

    // Seek(0, Begin) then read the bytes back equal.
    Expect(icall_stream_seek(comStream, 0, 0) == 0,
           "Stream.Seek(0, Begin) -> 0");
    il2cpp::Il2CppArray* comBack = il2cpp::il2cpp_array_new(nullptr, 1, false, 256);
    Expect(icall_stream_read_bytes(comStream, comBack, 0, 256) == 256,
           "reading back 256 bytes returns the full count");
    bool comEq = true;
    for (int32_t i = 0; i < 256; ++i)
    {
        comEq = comEq && comBack->data[i] == comData->data[i];
    }
    Expect(comEq, "read-back bytes match the written {0..255}");

    // EOF: reading past the end returns 0 (the position is now at 256).
    Expect(icall_stream_read_bytes(comStream, comBack, 0, 1) == 0,
           "reading at end-of-stream returns 0 (EOF)");

    // A seek before the beginning of the stream is rejected: the stream
    // object throws IOException (net48); the raw IStream rejects the
    // negative Begin seek (the native error surfaces as a non-S_OK HRESULT).
    Expect(ThrowsDotNet([&] { icall_stream_seek(comStream, -1, 0); }, kIOException),
           "Stream.Seek(-1, Begin) -> IOException");
    int64_t negPos = 0x1234;
    Expect(icall_com_istream_seek(comIStream, -1, 0, &negPos) != kComHrOk,
           "istream_seek(-1, Begin) is rejected (native negative-position error)");

    // SetLength(128): truncates the stream. The native IStream passthrough
    // (probe-verified) leaves the seek pointer UNCHANGED -- unlike the
    // file/memory backends, the kCom delegation does not clamp (net48's
    // CreateStreamOnHGlobal stream calls the same native SetSize).
    icall_stream_set_length(comStream, 128);
    Expect(icall_stream_get_length(comStream) == 128,
           "SetLength(128) truncates the stream to 128 bytes");
    Expect(icall_stream_get_position(comStream) == 256,
           "SetLength leaves the position unchanged (native IStream passthrough)");

    // Capability flags: read/write/seek all supported.
    Expect(icall_stream_get_can_read(comStream) == 1 &&
           icall_stream_get_can_write(comStream) == 1 &&
           icall_stream_get_can_seek(comStream) == 1,
           "the ComStream reports CanRead / CanWrite / CanSeek");

    // LARGE_INTEGER regression anchor (probe-verified): the HGLOBAL-backed
    // IStream's internal seek is 32-bit WIDE -- seeking to 0x0123456789ABCDEF
    // is REJECTED (STG_E_INVALIDFUNCTION; the wrapped low-32 position
    // 0x89ABCDEF is invalid), and the position stays put (verify-then-mutate).
    // A BROKEN 4x-intptr vtable-slot carrier (the two 32-bit halves swapped
    // between x86/x64) would present the HIGH 32 bits 0x01234567 -- a VALID
    // position the native seek accepts (probe: S_OK at 0x01234567). The
    // dedicated int64_t-parameter icall therefore pins the by-value ABI.
    int64_t anchorSentinel = 0x123456789LL;
    Expect(icall_com_istream_seek(comIStream, 0x0123456789ABCDEFLL, 0, &anchorSentinel) != kComHrOk &&
           anchorSentinel == 0x123456789LL,
           "istream_seek(0x0123456789ABCDEF) -> rejected (the 64-bit LARGE_INTEGER ABI anchor)");

    // Raw write/read round-trip through the RCW-direction istream icalls.
    uint8_t rawBuf[256];
    for (int32_t i = 0; i < 256; ++i)
    {
        rawBuf[i] = static_cast<uint8_t>((i * 7) & 0xFF);
    }
    int32_t rawWritten = -1;
    Expect(icall_com_istream_seek(comIStream, 0, 0, &anchorSentinel) == kComHrOk &&
           icall_com_istream_write(comIStream,
               reinterpret_cast<intptr_t>(rawBuf), 256, &rawWritten) == kComHrOk &&
           rawWritten == 256,
           "istream_write writes 256 bytes (raw RCW direction)");
    uint8_t rawBack[256];
    int32_t rawRead = -1;
    Expect(icall_com_istream_seek(comIStream, 0, 0, &anchorSentinel) == kComHrOk &&
           icall_com_istream_read(comIStream,
               reinterpret_cast<intptr_t>(rawBack), 256, &rawRead) == kComHrOk &&
           rawRead == 256 && std::memcmp(rawBuf, rawBack, 256) == 0,
           "istream_read reads the 256 written bytes back equal");
    int32_t rawEof = -1;
    Expect(icall_com_istream_read(comIStream,
               reinterpret_cast<intptr_t>(rawBack), 4, &rawEof) == kComHrOk &&
           rawEof == 0,
           "istream_read at end-of-stream -> 0 bytes (EOF)");

    // Close is idempotent; after Close the Length reports -1 (M3 convention).
    Expect(icall_stream_close(comStream) == 0, "closing the ComStream");
    Expect(icall_stream_close(comStream) == 0,
           "a second Close is a no-op (idempotent)");
    Expect(icall_stream_get_length(comStream) == -1,
           "after Close the Length reports -1 (M3)");

    // Arg guards: a NON-COM stream object -> ArgumentException for the
    // HGLOBAL / IUnknown accessors.
    il2cpp::Il2CppObject* plainStream =
        il2cpp::il2cpp_object_new(&System_IO_MemoryStream_class);
    icall_stream_memory_ctor(plainStream);
    Expect(ThrowsDotNet([&] { icall_com_stream_get_hglobal(plainStream); }, kArgumentException),
           "GetHGlobalFromStream on a non-COM stream -> ArgumentException");
    Expect(ThrowsDotNet([&] { icall_com_stream_get_iunknown(plainStream); }, kArgumentException),
           "get_iunknown on a non-COM stream -> ArgumentException");
    icall_stream_close(plainStream);

    // null guards -> ArgumentNullException.
    Expect(ThrowsDotNet([&] { icall_com_stream_get_hglobal(nullptr); }, kArgumentNull),
           "GetHGlobalFromStream(null) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_stream_get_iunknown(nullptr); }, kArgumentNull),
           "get_iunknown(null) -> ArgumentNullException");
    int32_t nullRead = 0;
    Expect(ThrowsDotNet([&] { icall_com_istream_read(0, 0, 1, &nullRead); }, kArgumentNull),
           "istream_read(null stream) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_istream_write(0, 0, 1, &nullRead); }, kArgumentNull),
           "istream_write(null stream) -> ArgumentNullException");
    int64_t nullPos = 0;
    Expect(ThrowsDotNet([&] { icall_com_istream_seek(0, 0, 0, &nullPos); }, kArgumentNull),
           "istream_seek(null stream) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_istream_set_size(0, 1); }, kArgumentNull),
           "istream_set_size(null stream) -> ArgumentNullException");

    // fDeleteOnRelease = false variant: the stream still works; the HGLOBAL
    // stays owned by the CALLER (the stream's Release does not free it).
    il2cpp::Il2CppObject* comKeep = icall_com_stream_create_on_hglobal(0, 0);
    Expect(comKeep != nullptr && icall_com_stream_get_hglobal(comKeep) != 0 &&
           icall_com_stream_get_iunknown(comKeep) != 0,
           "CreateStreamOnHGlobal(0, false) works (caller-kept HGLOBAL)");
    icall_stream_close(comKeep);

    //------------------------------------------------------------------------
    // 15. Marshal BSTR bridge (stage 13 -- Marshal.StringToBSTR /
    //     BSTRToString / FreeBSTR, WINDOWS only). The three icalls reuse the
    //     stage-11 oleaut32 resolution (SysAllocStringLen / SysFreeString,
    //     com.cpp:1733-1736 -- kept OUT of GetOle32().ok). net48 semantics:
    //     StringToBSTR(null) -> IntPtr.Zero (0, NOT ArgumentNullException);
    //     SysAllocStringLen(s->chars, s->length) preserves embedded NUL units;
    //     BSTRToString(0) -> null; BSTRToString reads to the FIRST NUL unit
    //     (PtrToStringUni semantics -- embedded NULs truncate); FreeBSTR(0) ->
    //     no-op; FreeBSTR otherwise SysFreeString. The bridge is a SEPARATE
    //     path from the CCW enum ownership (section 13) -- the two never
    //     interact.
    //------------------------------------------------------------------------

    // Round-trip "hello": the BSTR content is byte-identical to the source
    // string's UTF-16 units (raw memcmp), null-terminated after the content,
    // and BSTRToString decodes it back to an equal managed string.
    il2cpp::Il2CppString* hello = MakeManagedString("hello");
    const intptr_t helloBstr = icall_com_bstr_from_string(hello);
    Expect(helloBstr != 0, "StringToBSTR(\"hello\") returns a non-zero BSTR");
    Expect(std::memcmp(reinterpret_cast<const void*>(helloBstr), hello->chars,
                       static_cast<size_t>(hello->length) * sizeof(char16_t)) == 0,
           "the BSTR content is byte-identical to the source UTF-16 units");
    Expect(reinterpret_cast<const char16_t*>(helloBstr)[hello->length] == u'\0',
           "the BSTR is null-terminated after the content");
    il2cpp::Il2CppObject* helloBack = icall_com_string_from_bstr(helloBstr);
    Expect(helloBack != nullptr, "BSTRToString returns a managed string");
    il2cpp::Il2CppString* helloBackStr = reinterpret_cast<il2cpp::Il2CppString*>(helloBack);
    Expect(helloBackStr->length == hello->length &&
           std::memcmp(helloBackStr->chars, hello->chars,
                       static_cast<size_t>(hello->length) * sizeof(char16_t)) == 0,
           "BSTRToString round-trips the content byte-identically");
    icall_com_bstr_free(helloBstr);

    // Embedded NUL dual semantics: StringToBSTR preserves the FULL length
    // (SysAllocStringLen copies 'length' units -- "a\0b" stays 3 units);
    // BSTRToString reads to the FIRST NUL unit (net48 PtrToStringUni
    // semantics -- "a\0b" decodes to "a").
    const char16_t embedded[3] = { u'a', u'\0', u'b' };
    il2cpp::Il2CppString* embStr = il2cpp::il2cpp_string_new(embedded, 3);
    const intptr_t embBstr = icall_com_bstr_from_string(embStr);
    Expect(embBstr != 0, "StringToBSTR(\"a\\0b\") returns a non-zero BSTR");
    Expect(std::memcmp(reinterpret_cast<const void*>(embBstr), embedded,
                       3 * sizeof(char16_t)) == 0,
           "StringToBSTR preserves the embedded NUL (full 3-unit copy)");
    il2cpp::Il2CppObject* embBack = icall_com_string_from_bstr(embBstr);
    il2cpp::Il2CppString* embBackStr = reinterpret_cast<il2cpp::Il2CppString*>(embBack);
    Expect(embBackStr->length == 1 && embBackStr->chars[0] == u'a',
           "BSTRToString truncates at the first NUL (\"a\\0b\" -> \"a\")");
    icall_com_bstr_free(embBstr);

    // Unicode: a non-ASCII BMP string round-trips byte-identically.
    const char16_t uni[4] = { 0x4E2D, 0x6587, 0x00E9, 0x00E8 }; // 中文 + é + è
    il2cpp::Il2CppString* uniStr = il2cpp::il2cpp_string_new(uni, 4);
    const intptr_t uniBstr = icall_com_bstr_from_string(uniStr);
    Expect(uniBstr != 0, "StringToBSTR(Unicode) returns a non-zero BSTR");
    Expect(std::memcmp(reinterpret_cast<const void*>(uniBstr), uni,
                       4 * sizeof(char16_t)) == 0,
           "the Unicode BSTR content is byte-identical");
    il2cpp::Il2CppObject* uniBack = icall_com_string_from_bstr(uniBstr);
    il2cpp::Il2CppString* uniBackStr = reinterpret_cast<il2cpp::Il2CppString*>(uniBack);
    Expect(uniBackStr->length == 4 &&
           std::memcmp(uniBackStr->chars, uni, 4 * sizeof(char16_t)) == 0,
           "BSTRToString round-trips the Unicode content");
    icall_com_bstr_free(uniBstr);

    // Empty string: SysAllocStringLen(chars, 0) yields a non-null BSTR whose
    // content is the null terminator only; BSTRToString decodes it to "".
    il2cpp::Il2CppString* bstrEmptyStr = MakeManagedString("");
    const intptr_t emptyBstr = icall_com_bstr_from_string(bstrEmptyStr);
    Expect(emptyBstr != 0, "StringToBSTR(\"\") returns a non-zero BSTR");
    Expect(reinterpret_cast<const char16_t*>(emptyBstr)[0] == u'\0',
           "the empty BSTR holds only the null terminator");
    il2cpp::Il2CppObject* emptyBack = icall_com_string_from_bstr(emptyBstr);
    il2cpp::Il2CppString* emptyBackStr = reinterpret_cast<il2cpp::Il2CppString*>(emptyBack);
    Expect(emptyBackStr->length == 0, "BSTRToString(empty BSTR) -> \"\"");
    icall_com_bstr_free(emptyBstr);

    // null three semantics: StringToBSTR(null) -> 0 (net48, NOT
    // ArgumentNullException); BSTRToString(0) -> null; FreeBSTR(0) -> no-op.
    Expect(icall_com_bstr_from_string(nullptr) == 0,
           "StringToBSTR(null) -> IntPtr.Zero (net48, NOT ArgumentNullException)");
    Expect(icall_com_string_from_bstr(0) == nullptr,
           "BSTRToString(IntPtr.Zero) -> null (net48)");
    icall_com_bstr_free(0);
    Expect(true, "FreeBSTR(IntPtr.Zero) is a no-op (no crash)");
    Expect(ThrowsDotNet([&] { icall_com_bstr_from_string(nullptr); }, kArgumentNull) == false,
           "StringToBSTR(null) does NOT throw ArgumentNullException (returns 0)");

    // Free: FreeBSTR releases a valid BSTR (every allocation above is freed
    // exactly once -- a double-free would crash the process).
    const intptr_t bstrFreePtr = icall_com_bstr_from_string(MakeManagedString("free-me"));
    Expect(bstrFreePtr != 0, "StringToBSTR allocates a BSTR for the free test");
    icall_com_bstr_free(bstrFreePtr);
    Expect(true, "FreeBSTR releases the allocation (no crash)");

    // CCW enum isolation: the BSTR bridge is a SEPARATE path from the CCW
    // enum ownership (section 13) -- StringToBSTR/BSTRToString/FreeBSTR never
    // touch the CCW's enum registry or owned-BSTR list. A CCW with a
    // registered BSTR item stays intact around the bridge calls.
    const intptr_t isoCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(isoCcw != 0, "ccw_create returns the isolation CCW");
    il2cpp::Il2CppString* isoStr = MakeManagedString("iso");
    Expect(icall_com_ccw_enum_register_bstr_item(isoCcw, isoStr) == kComHrOk,
           "the CCW enum BSTR registration still works alongside the bridge");
    const intptr_t isoBstr = icall_com_bstr_from_string(isoStr);
    Expect(isoBstr != 0, "StringToBSTR works independently of the CCW enum");
    icall_com_bstr_free(isoBstr);
    Expect(icall_com_release(isoCcw) == 0,
           "releasing the isolation CCW destroys it (enum BSTR teardown unaffected by the bridge)");

    //------------------------------------------------------------------------
    // 16. Marshal.GetComInterfaceForObject (stage 14 -- the focused wiring).
    //     The managed Type -> IID resolution happens at TRANSPILE time (the
    //     runtime Type object cannot be resolved to an IID at compile time),
    //     so the icall supports ONLY the two constant Type args the
    //     transpiler recognizes: IUnknown and IDispatch (the net48-most-used
    //     interfaces the CCW implements). The icall receives the resolved IID
    //     (16 bytes, .NET Guid layout) plus the object. A NON-RCW managed
    //     object is wrapped behind a fresh CCW and QI'd for the IID; an RCW
    //     QIs its wrapped native IUnknown. The returned pointer is AddRef'd
    //     once -- the caller owns the reference; Release to 0 destroys the
    //     CCW. Any other IID -> E_NOINTERFACE -> InvalidCastException (net48);
    //     null o -> ArgumentNullException.
    //------------------------------------------------------------------------

    // GetComInterfaceForObject(non-RCW, IUnknown): a plain managed object is
    // wrapped behind a CCW whose IUnknown is returned (AddRef'd). The pointer
    // QIs as the identity IUnknown; the caller's Release to 0 destroys it.
    const intptr_t gciUnk = icall_com_get_com_interface_for_object(MakeManagedTarget(), kComIidIUnknown);
    Expect(gciUnk != 0, "GetComInterfaceForObject(non-RCW, IUnknown) returns a non-zero IUnknown");
    intptr_t gciUnkQi = 0;
    Expect(icall_com_query_interface(gciUnk, kComIidIUnknown, &gciUnkQi) == kComHrOk &&
           gciUnkQi == gciUnk,
           "GetComInterfaceForObject's IUnknown QIs as the identity IUnknown");
    icall_com_release(gciUnkQi);
    Expect(icall_com_release(gciUnk) == 0,
           "releasing GetComInterfaceForObject's IUnknown reference destroys the CCW");

    // GetComInterfaceForObject(non-RCW, IDispatch): the same CCW path returns
    // the IDispatch view (the single 7-slot vtable carries IUnknown +
    // IDispatch -- the IDispatch pointer equals the identity pointer).
    const intptr_t gciDisp = icall_com_get_com_interface_for_object(MakeManagedTarget(), kComIidIDispatch);
    Expect(gciDisp != 0, "GetComInterfaceForObject(non-RCW, IDispatch) returns a non-zero IDispatch");
    intptr_t gciDispQi = 0;
    Expect(icall_com_query_interface(gciDisp, kComIidIDispatch, &gciDispQi) == kComHrOk &&
           gciDispQi == gciDisp,
           "GetComInterfaceForObject's IDispatch QIs as IDispatch");
    icall_com_release(gciDispQi);
    Expect(icall_com_release(gciDisp) == 0,
           "releasing GetComInterfaceForObject's IDispatch reference destroys the CCW");

    // An unsupported IID (any interface other than IUnknown/IDispatch) ->
    // E_NOINTERFACE -> InvalidCastException (net48).
    Expect(ThrowsDotNet([&] { (void)icall_com_get_com_interface_for_object(MakeManagedTarget(), kSomeOtherIid); },
                        kInvalidCast),
           "GetComInterfaceForObject with an unsupported IID -> InvalidCastException");

    // null object -> ArgumentNullException (net48).
    Expect(ThrowsDotNet([&] { (void)icall_com_get_com_interface_for_object(nullptr, kComIidIUnknown); },
                        kArgumentNull),
           "GetComInterfaceForObject(null, IUnknown) -> ArgumentNullException");

    //------------------------------------------------------------------------
    // 17. OLE drag-drop minimal surface (stage 15 -- WINDOWS only): the CCW
    //     exposes an IDropTarget view (a 7-slot vtable at &ccw->dropVtbl --
    //     the OLE drop-target surface a WinForms Control.AllowDrop registration
    //     needs). The minimal stage-15 surface implements DragLeave (S_OK -- no
    //     parameters, no marshaling); DragEnter/DragOver/Drop return E_NOTIMPL
    //     (the managed dispatch -- DragEventArgs + a typed IDataObject RCW -- is
    //     the stage-16+ path). icall_com_ole_register_drag_drop /
    //     icall_com_ole_revoke_drag_drop wrap ole32 RegisterDragDrop /
    //     RevokeDragDrop (resolved at runtime, kept OUT of GetOle32().ok;
    //     EnsureOle32 inside). The register/revoke test runs on a DEDICATED
    //     worker thread (the DropRegThread design): the main test thread is MTA
    //     (EnsureOle32's CoInitializeEx(0)) and OleInitialize's STA is
    //     incompatible with it, while RegisterDragDrop needs OLE initialized on
    //     the calling thread with a window the calling thread created.
    //------------------------------------------------------------------------

    // QI for the IDropTarget view: a distinct address inside the CCW
    // allocation (a separate 7-slot vtable -- NOT the identity view).
    const intptr_t dropCcw = icall_com_ccw_create(MakeManagedTarget());
    Expect(dropCcw != 0, "ccw_create returns the stage-15 CCW");
    intptr_t dropView = 0;
    Expect(icall_com_query_interface(dropCcw, kComIidIDropTarget, &dropView) == kComHrOk &&
           dropView != dropCcw,
           "QI for IID_IDropTarget returns the distinct drop-target view");

    // View refcount (create => 1, QI => 2): AddRef via the view reaches the
    // shared CCW refcount.
    Expect(icall_com_add_ref(dropView) == 3,
           "the drop view AddRef shares the CCW refcount");
    Expect(icall_com_release(dropView) == 2,
           "the drop view Release drops the shared refcount back");

    // View QI identity: QI(IUnknown) from the drop view returns the identity
    // (base address -- the COM QI identity rule); QI(IDropTarget) returns the
    // view itself.
    intptr_t dropId = 0;
    Expect(icall_com_query_interface(dropView, kComIidIUnknown, &dropId) == kComHrOk &&
           dropId == dropCcw,
           "QI(IUnknown) from the drop view returns the identity address");
    icall_com_release(dropId);
    intptr_t dropSelf = 0;
    Expect(icall_com_query_interface(dropView, kComIidIDropTarget, &dropSelf) == kComHrOk &&
           dropSelf == dropView,
           "QI(IDropTarget) from the drop view returns the view itself");
    icall_com_release(dropSelf);

    // DragLeave (slot 5 -- no parameters, no marshaling): S_OK.
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView, kComDropSlotDragLeave, 0, 0, 0, 0)) == kComHrOk,
           "IDropTarget::DragLeave (slot 5) returns S_OK");

    // DragEnter / DragOver / Drop (the managed dispatch is stage 16+): the
    // minimal surface returns E_NOTIMPL. DragEnter/Drop take an IDataObject*
    // first argument -- the minimal stub's pointer is passed (the method body
    // never dereferences it).
    TestSink dropDataObj;
    dropDataObj.vtbl = kTestSinkVtbl;
    dropDataObj.refs = 0;
    const intptr_t dropData = reinterpret_cast<intptr_t>(&dropDataObj);
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView, kComDropSlotDragEnter, dropData, 0, 0, 0)) == kComHrNotImpl,
           "IDropTarget::DragEnter (slot 3) returns E_NOTIMPL (managed dispatch stage 16+)");
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView, kComDropSlotDragOver, 0, 0, 0, 0)) == kComHrNotImpl,
           "IDropTarget::DragOver (slot 4) returns E_NOTIMPL");
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView, kComDropSlotDrop, dropData, 0, 0, 0)) == kComHrNotImpl,
           "IDropTarget::Drop (slot 6) returns E_NOTIMPL");

    // The OOB guard now knows the 7-slot drop vtable (slot 7 -> AOOException).
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(dropView, 7, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "drop view slot 7 is OOB (the 7-slot guard)");

    // Register/revoke arg defense (checked before any OLE access):
    // register(hwnd 0) and register(dropTarget 0) -> ArgumentNullException;
    // revoke(hwnd 0) -> ArgumentNullException.
    Expect(ThrowsDotNet([&] { icall_com_ole_register_drag_drop(0, dropView); },
                        kArgumentNull),
           "register_drag_drop(hwnd 0) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_ole_register_drag_drop(0x1, 0); },
                        kArgumentNull),
           "register_drag_drop(dropTarget 0) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_com_ole_revoke_drag_drop(0); },
                        kArgumentNull),
           "revoke_drag_drop(hwnd 0) -> ArgumentNullException");

    // Register/revoke on the DropRegThread worker (STA + a window the worker
    // thread created): register S_OK, revoke S_OK, re-register S_OK (idempotent
    // across a revoke -- RegisterDragDrop on an already-registered window would
    // fail with DRAGDROP_E_ALREADYREGISTERED, so idempotency is register ->
    // revoke -> register again).
    DropRegWorkerData dropReg = {};
    dropReg.view = dropView;
    HANDLE hDropThread = ::CreateThread(nullptr, 0, &DropRegWorkerProc, &dropReg, 0, nullptr);
    Expect(hDropThread != nullptr, "the DropRegThread worker thread starts");
    if (hDropThread != nullptr)
    {
        ::WaitForSingleObject(hDropThread, 10000);
        ::CloseHandle(hDropThread);
    }
    Expect(dropReg.ran, "the DropRegThread worker ran");
    Expect(dropReg.oleInitOk, "OleInitialize (STA) succeeded on the worker thread");
    Expect(dropReg.windowOk, "the worker thread created its own hidden window");
    Expect(dropReg.registerHr == kComHrOk,
           "RegisterDragDrop on a valid HWND returns S_OK");
    Expect(dropReg.revokeHr == kComHrOk,
           "RevokeDragDrop returns S_OK");
    Expect(dropReg.registerAgainHr == kComHrOk,
           "re-register after revoke returns S_OK (idempotent)");

    // Release the view's QI reference (2 -> 1), then the identity reference
    // (1 -> 0 -- destroys the CCW).
    Expect(icall_com_release(dropView) == 1,
           "releasing the drop view leaves the identity reference");
    Expect(icall_com_release(dropCcw) == 0,
           "releasing the identity reference destroys the stage-15 CCW");

    //------------------------------------------------------------------------
    // 18. IDropTarget dispatch forwarding (stage 16 -- WINDOWS only): the
    //     register_drop_dispatch icall stores a per-CCW NATIVE dispatch
    //     callback the IDropTarget view methods forward the raw OLE arguments
    //     to. DragEnter/DragOver/Drop decode the by-value POINTL (the intptr
    //     carrier: x = low 32 bits, y = high 32 bits -- the stage-15
    //     `const void* pt` declaration was a wrong by-value POINTL shape, UB
    //     when dereferenced) and forward method / pDataObj / grfKeyState / x /
    //     y / the effect in-out pointer to the callback, returning its HRESULT
    //     and propagating its *pdwEffect write. Unregistered -> the stage-15
    //     E_NOTIMPL surface; DragLeave stays S_OK (the stage-15 locked
    //     contract); fn == 0 clears the dispatch; ccw == 0 ->
    //     ArgumentNullException (precedes the platform gate).
    //------------------------------------------------------------------------

    const intptr_t dropCcw16 = icall_com_ccw_create(MakeManagedTarget());

    Expect(dropCcw16 != 0, "stage-16 ccw_create returns a CCW");
    intptr_t dropView16 = 0;
    Expect(icall_com_query_interface(dropCcw16, kComIidIDropTarget, &dropView16) == kComHrOk &&
           dropView16 != dropCcw16,
           "stage-16 QI for IID_IDropTarget returns the distinct drop-target view");

    // Unregistered view: the stage-15 fallback (E_NOTIMPL) -- the dispatch
    // state is per-CCW, so a fresh CCW starts unregistered.
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView16, kComDropSlotDragEnter, dropData, 0, 0, 0)) == kComHrNotImpl,
           "unregistered stage-16 DragEnter falls back to the stage-15 E_NOTIMPL");

    // Register the dispatch callback -> S_OK.
    Expect(icall_com_ccw_register_drop_dispatch(dropCcw16, reinterpret_cast<intptr_t>(&TestDropDispatch)) == kComHrOk,
           "register_drop_dispatch stores the callback (S_OK)");

    // DragEnter (slot 3): the raw OLE args forwarded to the callback -- the
    // method discriminator, the data object pointer (byte-exact), the key
    // state bits and the DECODED POINTL x/y (x = low 32 bits of the intptr
    // carrier, y = high 32 bits -- the stage-16 POINTL discriminator).
    uint32_t enterEffect = 0x1;
    const intptr_t ptBits = (static_cast<intptr_t>(0x55667788u) << 32) | 0x11223344u;
    TestDropRec().Reset();
    TestDropRec().effectWrite = 0x2;
    TestDropRec().returnHr = kComHrOk;
    Expect(icall_com_call_vtable_slot(dropView16, kComDropSlotDragEnter,
                                      dropData, 0xABCD, ptBits,
                                      reinterpret_cast<intptr_t>(&enterEffect)) == kComHrOk &&
           TestDropRec().calls == 1 &&
           TestDropRec().lastMethod == kComDropMethodDragEnter &&
           TestDropRec().lastData == dropData &&
           TestDropRec().lastKey == 0xABCD &&
           TestDropRec().lastX == 0x11223344 &&
           TestDropRec().lastY == 0x55667788 &&
           enterEffect == 0x2,
           "DragEnter forwards method/pDataObj/grfKeyState + decoded POINTL x/y and propagates the effect write (0x1 -> 0x2)");

    // HRESULT propagation: a non-S_OK callback HRESULT is returned unmasked.
    TestDropRec().Reset();
    TestDropRec().returnHr = kComHrSFALSE;
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView16, kComDropSlotDragEnter, dropData, 0, ptBits, 0)) == kComHrSFALSE &&
           TestDropRec().calls == 1,
           "DragEnter returns the callback's non-S_OK HRESULT unmasked (S_FALSE)");

    // DragOver (slot 4 -- THREE params after self: grfKeyState, pt, pdwEffect;
    // the 4x-intptr vtable-slot carrier is slot-signature-aligned, so a3 is
    // the ignored trailing carrier): no data object (pDataObj == 0), key/x/y
    // forwarded.
    uint32_t overEffect = 0x8;
    TestDropRec().Reset();
    TestDropRec().effectWrite = 0x1;
    TestDropRec().returnHr = kComHrOk;
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView16, kComDropSlotDragOver, 0x55, ptBits,
               reinterpret_cast<intptr_t>(&overEffect), 0)) == kComHrOk &&
           TestDropRec().calls == 1 &&
           TestDropRec().lastMethod == kComDropMethodDragOver &&
           TestDropRec().lastData == 0 &&
           TestDropRec().lastKey == 0x55 &&
           TestDropRec().lastX == 0x11223344 &&
           TestDropRec().lastY == 0x55667788 &&
           overEffect == 0x1,
           "DragOver forwards method/no-data/keyState/x/y and propagates the effect write");

    // Drop (slot 6): data object + key + x/y + effect write all forwarded.
    uint32_t dropEffect = 0x1;
    TestDropRec().Reset();
    TestDropRec().effectWrite = 0x4;
    TestDropRec().returnHr = kComHrOk;
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView16, kComDropSlotDrop, dropData, 0x321, ptBits,
               reinterpret_cast<intptr_t>(&dropEffect))) == kComHrOk &&
           TestDropRec().calls == 1 &&
           TestDropRec().lastMethod == kComDropMethodDrop &&
           TestDropRec().lastData == dropData &&
           TestDropRec().lastKey == 0x321 &&
           TestDropRec().lastX == 0x11223344 &&
           TestDropRec().lastY == 0x55667788 &&
           dropEffect == 0x4,
           "Drop forwards method/data/keyState/x/y and propagates the effect write");

    // DragLeave (slot 5) keeps the stage-15 S_OK contract after registration.
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView16, kComDropSlotDragLeave, 0, 0, 0, 0)) == kComHrOk,
           "DragLeave stays S_OK after dispatch registration (stage-15 contract)");

    // fn == 0 clears the dispatch -> the stage-15 fallback returns.
    Expect(icall_com_ccw_register_drop_dispatch(dropCcw16, 0) == kComHrOk,
           "register_drop_dispatch(fn 0) clears the dispatch (S_OK)");
    TestDropRec().Reset();
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView16, kComDropSlotDragEnter, dropData, 0, 0, 0)) == kComHrNotImpl &&
           TestDropRec().calls == 0,
           "the cleared dispatch restores the stage-15 DragEnter E_NOTIMPL without invoking the callback");

    // Dual-CCW isolation: a SECOND unregistered CCW stays on the stage-15
    // fallback while the first CCW (re-registered) still forwards -- the
    // dispatch callback is per-CCW state.
    Expect(icall_com_ccw_register_drop_dispatch(dropCcw16, reinterpret_cast<intptr_t>(&TestDropDispatch)) == kComHrOk,
           "re-registering the callback on the first CCW (S_OK)");
    const intptr_t dropCcw16b = icall_com_ccw_create(MakeManagedTarget());
    intptr_t dropView16b = 0;
    Expect(icall_com_query_interface(dropCcw16b, kComIidIDropTarget, &dropView16b) == kComHrOk &&
           dropView16b != dropCcw16b,
           "the second CCW QI for IDropTarget returns its own distinct view");
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView16b, kComDropSlotDragEnter, dropData, 0, 0, 0)) == kComHrNotImpl,
           "the unregistered second CCW DragEnter returns E_NOTIMPL (per-CCW dispatch state)");
    TestDropRec().Reset();
    TestDropRec().returnHr = kComHrOk;
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               dropView16, kComDropSlotDragEnter, dropData, 0, ptBits, 0)) == kComHrOk &&
           TestDropRec().calls == 1,
           "the first CCW still forwards after the second CCW exists (isolation)");

    // Registration does not perturb the CCW refcount (create => 1, QI => 2:
    // AddRef -> 3, Release -> 2 -- the callback is a stored pointer, never
    // AddRef'd or released).
    Expect(icall_com_add_ref(dropView16) == 3 && icall_com_release(dropView16) == 2,
           "registering the dispatch does not perturb the CCW refcount");

    // null guard: ccw == 0 -> ArgumentNullException (before the platform gate).
    Expect(ThrowsDotNet([&] { icall_com_ccw_register_drop_dispatch(0, reinterpret_cast<intptr_t>(&TestDropDispatch)); },
                        kArgumentNull),
           "register_drop_dispatch(ccw 0) -> ArgumentNullException");

    // OOB guard regression: the 7-slot drop vtable still guards slot 7.
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(dropView16, 7, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "drop view slot 7 stays OOB after stage 16 (the 7-slot guard)");

    // Release the second CCW's view QI reference, then its identity reference
    // (destroys); same for the first CCW.
    Expect(icall_com_release(dropView16b) == 1 &&
           icall_com_release(dropCcw16b) == 0,
           "releasing the second CCW's view + identity references destroys it");
    Expect(icall_com_release(dropView16) == 1 &&
           icall_com_release(dropCcw16) == 0,
           "releasing the first CCW's view + identity references destroys it");

#else // !defined(_WIN32)

    //------------------------------------------------------------------------
    // 4. STUB (non-Windows): the Windows-gated surface returns the documented
    //    explicit-error contract (netcore Marshal.Unix.cs mirror) -- never a
    //    crash, never a silent value where netcore throws.
    //------------------------------------------------------------------------
    il2cpp::Il2CppString* progId =
        reinterpret_cast<il2cpp::Il2CppString*>(il2cpp::il2cpp_string_new_from_cs("Shell.Application"));
    uint8_t stubClsid[16] = { 0 };
    Expect(icall_com_get_type_from_progid(progId, stubClsid) == kComHrNotImpl,
           "GetTypeFromProgID on non-Windows returns E_NOTIMPL (no COM)");
    Expect(ThrowsDotNet([&] { icall_com_create_instance_progid(progId); }, kNotSupported),
           "CreateInstance(ProgID) on non-Windows throws NotSupportedException");
    Expect(ThrowsDotNet([&] { (void)icall_com_create_instance_clsid(kComIidIUnknown); }, kNotSupported),
           "CreateInstance(CLSID) on non-Windows throws NotSupportedException");
    Expect(ThrowsDotNet([&] { (void)icall_com_create_object(progId); }, kNotSupported),
           "CreateObject on non-Windows throws NotSupportedException");
    Expect(ThrowsDotNet([&] { (void)icall_com_get_active_object(progId); }, kNotSupported),
           "GetActiveObject on non-Windows throws NotSupportedException");

    //------------------------------------------------------------------------
    // 4b. IDispatch stubs (non-Windows): the RCW-direction icalls return
    //     E_NOTIMPL (the POSIX stub -- no real COM objects to dispatch into;
    //     the CCW IDispatch protocol itself is exercised every-platform in
    //     section 7 above).
    //------------------------------------------------------------------------
    intptr_t stubNames[1] = { 0 };
    int32_t stubDispId = 0;
    Expect(icall_com_idispatch_get_ids_of_names(0x1, stubNames, 1, 0, &stubDispId) == kComHrNotImpl,
           "idispatch_get_ids_of_names on non-Windows returns E_NOTIMPL");
    TestDispParams stubDp = { nullptr, nullptr, 0, 0 };
    Expect(icall_com_idispatch_invoke(0x1, 1, 0, 1,
                                      reinterpret_cast<intptr_t>(&stubDp), 0, 0, 0) == kComHrNotImpl,
           "idispatch_invoke on non-Windows returns E_NOTIMPL");

    // GetIDispatchForObject on a NON-RCW managed object still works (the CCW
    // path is every-platform -- it QIs the fresh CCW for IDispatch).
    const intptr_t posixDisp = icall_com_get_idispatch_for_object(MakeManagedTarget());
    Expect(posixDisp != 0, "GetIDispatchForObject(non-RCW) works on non-Windows (CCW path)");
    Expect(icall_com_release(posixDisp) == 0,
           "releasing the non-Windows GetIDispatchForObject reference destroys the CCW");

    //------------------------------------------------------------------------
    // 4c. IOleObject / IOleClientSite stubs (non-Windows): QI for the OLE
    //     IIDs returns E_NOINTERFACE -- the phase-4 OLE views are the
    //     Windows implementation + POSIX stub contract (the identity CCW
    //     still exists; only the OLE extension is not offered).
    //------------------------------------------------------------------------
    const intptr_t posixOle = icall_com_ccw_create(MakeManagedTarget());
    intptr_t posixOleObj = 0;
    Expect(icall_com_query_interface(posixOle, kComIidIOleObject, &posixOleObj) == kComHrNoInterface,
           "QI for IOleObject on non-Windows -> E_NOINTERFACE (POSIX stub)");
    intptr_t posixOleSite = 0;
    Expect(icall_com_query_interface(posixOle, kComIidIOleClientSite, &posixOleSite) == kComHrNoInterface,
           "QI for IOleClientSite on non-Windows -> E_NOINTERFACE (POSIX stub)");
    Expect(icall_com_release(posixOle) == 0,
           "releasing the non-Windows CCW reference destroys it");

    //------------------------------------------------------------------------
    // 4d. IConnectionPointContainer / IConnectionPoint stubs (non-Windows):
    //     QI for the phase-5 IIDs returns E_NOINTERFACE and the register
    //     icall returns E_NOTIMPL -- the phase-5 event-source views are the
    //     Windows implementation + POSIX stub contract.
    //------------------------------------------------------------------------
    const intptr_t posixEvt = icall_com_ccw_create(MakeManagedTarget());
    intptr_t posixCpc = 0;
    Expect(icall_com_query_interface(posixEvt, kComIidIConnectionPointContainer, &posixCpc) == kComHrNoInterface,
           "QI for IConnectionPointContainer on non-Windows -> E_NOINTERFACE (POSIX stub)");
    intptr_t posixCp = 0;
    Expect(icall_com_query_interface(posixEvt, kComIidIConnectionPoint, &posixCp) == kComHrNoInterface,
           "QI for IConnectionPoint on non-Windows -> E_NOINTERFACE (POSIX stub)");
    Expect(icall_com_ccw_register_connection_point(posixEvt, kEventIid) == kComHrNotImpl,
           "register_connection_point on non-Windows returns E_NOTIMPL");
    Expect(icall_com_ccw_fire_event(posixEvt, kEventIid, 0, 0, 0, 0, 0) == kComHrNotImpl,
           "ccw_fire_event on non-Windows returns E_NOTIMPL (POSIX stub)");
    TestDispParams dStubDp = { nullptr, nullptr, 0, 0 };
    Expect(icall_com_ccw_fire_dispatch_event(posixEvt, kEventIid, kComDispIdFirst,
                                             reinterpret_cast<intptr_t>(&dStubDp), 0) == kComHrNotImpl,
           "ccw_fire_dispatch_event on non-Windows returns E_NOTIMPL (POSIX stub)");
    Expect(icall_com_release(posixEvt) == 0,
           "releasing the non-Windows event-source CCW reference destroys it");

    //------------------------------------------------------------------------
    // 4e. IEnumVARIANT (non-Windows): the stage-10 enumeration surface is
    //     EVERY-platform (pure memory layout + GUID compare -- no ole32
    //     dependency). The register icall works and the identity QI dispatches
    //     IEnumVARIANT to the inlined enum view (the QI dispatch lives outside
    //     the Windows gate).
    //------------------------------------------------------------------------
    const intptr_t posixEnum = icall_com_ccw_create(MakeManagedTarget());
    uint8_t posixItem[16];
    MakeVariant(kVtI4, posixItem);
    Expect(icall_com_ccw_enum_register_item(posixEnum, posixItem) == kComHrOk,
           "enum_register_item works on non-Windows (every-platform)");
    intptr_t posixEnumView = 0;
    Expect(icall_com_query_interface(posixEnum, kComIidIEnumVariant, &posixEnumView) == kComHrOk &&
           posixEnumView != posixEnum,
           "QI for IEnumVARIANT works on non-Windows (every-platform view)");
    uint8_t posixGot[1][16];
    int32_t posixFetched = 99;
    Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
               posixEnumView, kComEnumSlotNext, 1,
               reinterpret_cast<intptr_t>(posixGot),
               reinterpret_cast<intptr_t>(&posixFetched), 0)) == kComHrOk &&
           posixFetched == 1 &&
           std::memcmp(posixGot[0], posixItem, 16) == 0,
           "enum Next works on non-Windows (every-platform vtable)");
    Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(posixEnumView, 7, 0, 0, 0, 0); },
                        kArgumentOutOfRange),
           "enum slot 7 is OOB on non-Windows too (7-slot guard)");
    Expect(icall_com_release(posixEnumView) == 1 &&
           icall_com_release(posixEnum) == 0,
           "releasing the non-Windows enum view + identity destroys the CCW");

    //------------------------------------------------------------------------
    // 4f. IEnumVARIANT BSTR items (non-Windows): BSTR is a Windows-only type
    //     -- the stage-10 every-platform claim is carved out for the BSTR
    //     path; the register icall returns E_NOTIMPL (the POSIX stub).
    //------------------------------------------------------------------------
    il2cpp::Il2CppString* posixBstr = MakeManagedString("bstr");
    Expect(icall_com_ccw_enum_register_bstr_item(posixEnum, posixBstr) == kComHrNotImpl,
           "enum_register_bstr_item on non-Windows returns E_NOTIMPL (BSTR is Windows-only)");

    //------------------------------------------------------------------------
    // 4g. IStream (non-Windows): the stage-12 CreateStreamOnHGlobal surface is
    //     Windows-only -- create throws NotSupportedException (the platform-
    //     limitation contract) and the raw istream icalls return E_NOTIMPL
    //     (the POSIX stub -- no real COM IStream can exist there).
    //------------------------------------------------------------------------
    Expect(ThrowsDotNet([&] { icall_com_stream_create_on_hglobal(0, 1); }, kNotSupported),
           "CreateStreamOnHGlobal on non-Windows throws NotSupportedException");
    int32_t stubPcb = 0;
    Expect(icall_com_istream_read(0x1, 0, 1, &stubPcb) == kComHrNotImpl,
           "istream_read on non-Windows returns E_NOTIMPL");
    Expect(icall_com_istream_write(0x1, 0, 1, &stubPcb) == kComHrNotImpl,
           "istream_write on non-Windows returns E_NOTIMPL");
    int64_t stubPos = 0;
    Expect(icall_com_istream_seek(0x1, 0, 0, &stubPos) == kComHrNotImpl,
           "istream_seek on non-Windows returns E_NOTIMPL");
    Expect(icall_com_istream_set_size(0x1, 1) == kComHrNotImpl,
           "istream_set_size on non-Windows returns E_NOTIMPL");

    //------------------------------------------------------------------------
    // 4h. Marshal BSTR bridge (non-Windows): BSTR is a Windows-only type --
    //     StringToBSTR / BSTRToString throw NotSupportedException (the
    //     platform-limitation contract) and FreeBSTR is a no-op (nothing to
    //     free). The null forms still follow net48 (StringToBSTR(null) -> 0,
    //     BSTRToString(0) -> null, FreeBSTR(0) -> no-op) -- the null checks
    //     precede the platform gate.
    //------------------------------------------------------------------------
    Expect(icall_com_bstr_from_string(nullptr) == 0,
           "StringToBSTR(null) -> 0 on non-Windows too (null precedes the platform gate)");
    Expect(icall_com_string_from_bstr(0) == nullptr,
           "BSTRToString(0) -> null on non-Windows too");
    icall_com_bstr_free(0);
    Expect(true, "FreeBSTR(0) is a no-op on non-Windows");
    Expect(ThrowsDotNet([&] { icall_com_bstr_from_string(MakeManagedString("x")); }, kNotSupported),
           "StringToBSTR on non-Windows throws NotSupportedException");
    Expect(ThrowsDotNet([&] { icall_com_string_from_bstr(0x1); }, kNotSupported),
           "BSTRToString on non-Windows throws NotSupportedException");
    icall_com_bstr_free(0x1);
    Expect(true, "FreeBSTR on non-Windows is a no-op (no crash)");

    //------------------------------------------------------------------------
    // 4i. Marshal.GetComInterfaceForObject (non-Windows): the stage-14 icall
    //     is the POSIX stub -- throws NotSupportedException (the platform-
    //     limitation contract). The null guard precedes the platform gate
    //     (null -> ArgumentNullException on every platform, net48).
    //------------------------------------------------------------------------
    Expect(ThrowsDotNet([&] { (void)icall_com_get_com_interface_for_object(MakeManagedTarget(), kComIidIUnknown); },
                        kNotSupported),
           "GetComInterfaceForObject on non-Windows throws NotSupportedException (POSIX stub)");
    Expect(ThrowsDotNet([&] { (void)icall_com_get_com_interface_for_object(nullptr, kComIidIUnknown); },
                        kArgumentNull),
           "GetComInterfaceForObject(null) -> ArgumentNullException on non-Windows too (null precedes the platform gate)");

    //------------------------------------------------------------------------
    // 4j. OLE drag-drop (non-Windows): the stage-15 register/revoke icalls
    //     return E_NOTIMPL (the POSIX stub -- the platform-limitation
    //     contract) and the IDropTarget view is not offered (QI ->
    //     E_NOINTERFACE). The null guards precede the platform gate
    //     (register/revoke with a zero hwnd -> ArgumentNullException on every
    //     platform).
    //------------------------------------------------------------------------
    Expect(icall_com_ole_register_drag_drop(0x1, 0x1) == kComHrNotImpl,
           "register_drag_drop on non-Windows returns E_NOTIMPL (POSIX stub)");
    Expect(icall_com_ole_revoke_drag_drop(0x1) == kComHrNotImpl,
           "revoke_drag_drop on non-Windows returns E_NOTIMPL (POSIX stub)");
    Expect(ThrowsDotNet([&] { icall_com_ole_register_drag_drop(0, 0x1); },
                        kArgumentNull),
           "register_drag_drop(hwnd 0) -> ArgumentNullException on non-Windows too");
    Expect(ThrowsDotNet([&] { icall_com_ole_revoke_drag_drop(0); },
                        kArgumentNull),
           "revoke_drag_drop(hwnd 0) -> ArgumentNullException on non-Windows too");
    intptr_t posixDrop = 0;
    Expect(icall_com_query_interface(posixEvt, kComIidIDropTarget, &posixDrop) == kComHrNoInterface,
           "QI for IDropTarget on non-Windows -> E_NOINTERFACE (POSIX stub)");

    //------------------------------------------------------------------------
    // 4k. IDropTarget dispatch forwarding (non-Windows): the stage-16 register
    //     icall returns E_NOTIMPL (the POSIX stub -- the platform-limitation
    //     contract) and the null guard precedes the platform gate (ccw 0 ->
    //     ArgumentNullException on every platform).
    //------------------------------------------------------------------------
    Expect(icall_com_ccw_register_drop_dispatch(0x1, 0x1) == kComHrNotImpl,
           "register_drop_dispatch on non-Windows returns E_NOTIMPL (POSIX stub)");
    Expect(ThrowsDotNet([&] { icall_com_ccw_register_drop_dispatch(0, 0x1); },
                        kArgumentNull),
           "register_drop_dispatch(ccw 0) -> ArgumentNullException on non-Windows too (null precedes the platform gate)");

#endif // defined(_WIN32)

    //------------------------------------------------------------------------
    // 19. COM stage 17 batch 1 (every platform): real WinForms-shaped
    //     interface dispatch. The com17type fixture
    //     (tests/cases/com17type, committed gen_v1) mirrors the WinForms
    //     IDropTarget contract: DragDropEffects enum + minimal DragEventArgs
    //     (X/Y/Effect int32 fields) + the 4-method IDropTarget interface with
    //     DragEventArgs REF params and void returns -- the FIRST r:-param
    //     interface-dispatch coverage (all prior interface e2e cases are
    //     int-signed). Types are linked into this binary by CMakeLists and
    //     registered via the generated entry.cpp's il2cpp_runtime_init; the
    //     dispatch is driven HEADLESSLY through il2cpp_interface_dispatch,
    //     slot by slot, asserting the method ran + arg side effects + dual-
    //     implementer isolation + CCW/GCHandle target recovery (the batch-3
    //     bridge base).
    //
    //     INTERFACE SLOT ORDER (audit-correction): the planner sorts the
    //     interface's virtual methods ALPHABETICALLY, not by declaration
    //     order -- IDropTarget slots are OnDragDrop=0, OnDragEnter=1,
    //     OnDragLeave=2, OnDragOver=3 (cross-checked against the generated
    //     Cases_Com17type.cpp callvirt slots and the committed slotMap).
    //------------------------------------------------------------------------
    {
        il2cpp_runtime_init();

        // 19.1 Registration: the generated init registered every com17type
        // descriptor (idempotent runtime bootstrap + type table).
        Expect(il2cpp::il2cpp_class_by_name("IDropTarget") == &IDropTarget_class,
               "il2cpp_runtime_init registers the IDropTarget descriptor");
        Expect(il2cpp::il2cpp_class_by_name("WfDropSource") == &WfDropSource_class,
               "il2cpp_runtime_init registers the WfDropSource descriptor");
        Expect(il2cpp::il2cpp_class_by_name("WfDropSource2") == &WfDropSource2_class,
               "il2cpp_runtime_init registers the WfDropSource2 descriptor");
        Expect(il2cpp::il2cpp_class_by_name("DragEventArgs") == &DragEventArgs_class,
               "il2cpp_runtime_init registers the DragEventArgs descriptor");

        // 19.2 Interface descriptor shape: no instances, 4 method slots, no
        // own interface table.
        Expect(IDropTarget_class.instanceSize == 0 &&
               IDropTarget_class.vtableSize == 4 &&
               IDropTarget_class.interfaceCount == 0 &&
               IDropTarget_class.interfaces == nullptr,
               "IDropTarget descriptor: instanceSize 0, vtableSize 4, no interfaces");

        // 19.3 Concrete class interface table: WfDropSource maps exactly
        // IDropTarget, and every interface slot resolves in vtable range.
        Expect(WfDropSource_class.interfaceCount == 1 &&
               WfDropSource_class.interfaces != nullptr &&
               WfDropSource_class.interfaces[0].iface == &IDropTarget_class,
               "WfDropSource interface table maps IDropTarget");
        {
            const InterfaceMapEntry& entry = WfDropSource_class.interfaces[0];
            bool mapInRange = entry.slotMap != nullptr;
            for (int32_t s = 0; s < 4 && mapInRange; ++s)
                mapInRange = entry.slotMap[s] >= 0 && entry.slotMap[s] < WfDropSource_class.vtableSize;
            Expect(mapInRange, "WfDropSource IDropTarget slotMap resolves all 4 slots in range");
        }

        // 19.4 Object construction: allocate + run the generated ctor.
        il2cpp::Il2CppObject* wf = il2cpp::il2cpp_object_new(&WfDropSource_class);
        WfDropSource__ctor_ac53c7667218b16d(wf);
        Expect(wf != nullptr && wf->klass == &WfDropSource_class,
               "il2cpp_object_new + ctor stamps the WfDropSource descriptor");
        auto* wfView = reinterpret_cast<WfDropSource*>(wf);
        Expect(wfView->EnterCount == 0 && wfView->OverCount == 0,
               "WfDropSource observer fields zero-initialized by allocation");

        // 19.5 DragEventArgs payload (constructed through the generated ctor:
        // X/Y + the enum value as its int32 backing -- Effect is an int field
        // in batch 1, the M2 value-type-field constraint; batch 3 adds the
        // IDataObject Data reference arg, null here).
        il2cpp::Il2CppObject* e1 = il2cpp::il2cpp_object_new(&DragEventArgs_class);
        DragEventArgs__ctor_095e8b5153636074(e1, nullptr, 0x1122, 0x3344, 2 /* DragDropEffects.Move */);
        auto* e1View = reinterpret_cast<DragEventArgs*>(e1);
        Expect(e1View->X == 0x1122 && e1View->Y == 0x3344 && e1View->Effect == 2,
               "DragEventArgs ctor sets X/Y/Effect (int32 backing)");

        // 19.6 OnDragEnter (interface slot 1): dispatch resolves the vtable
        // slot, the call delivers the DragEventArgs REF arg, and the method
        // writes the observed side effects.
        auto* enterSlot = il2cpp::il2cpp_interface_dispatch(wf, &IDropTarget_class, 1);
        auto enterFn = reinterpret_cast<void (*)(il2cpp::Il2CppObject*, il2cpp::Il2CppObject*)>(*enterSlot);
        enterFn(wf, e1);
        Expect(wfView->EnterCount == 1, "OnDragEnter dispatch called the method (EnterCount 1)");
        Expect(wfView->LastX == 0x1122 && wfView->LastY == 0x3344 && wfView->LastEffect == 2,
               "OnDragEnter dispatch delivered the arg side effects (X/Y/Effect)");

        // 19.7 OnDragOver (slot 3) and OnDragLeave (slot 2) -- slot-specific
        // dispatch reaches the matching method.
        auto* overSlot = il2cpp::il2cpp_interface_dispatch(wf, &IDropTarget_class, 3);
        reinterpret_cast<void (*)(il2cpp::Il2CppObject*, il2cpp::Il2CppObject*)>(*overSlot)(wf, e1);
        auto* leaveSlot = il2cpp::il2cpp_interface_dispatch(wf, &IDropTarget_class, 2);
        reinterpret_cast<void (*)(il2cpp::Il2CppObject*, il2cpp::Il2CppObject*)>(*leaveSlot)(wf, e1);
        Expect(wfView->OverCount == 1 && wfView->LeaveCount == 1,
               "OnDragOver/OnDragLeave dispatch reached the matching methods");

        // 19.8 OnDragDrop (slot 0) with a FRESH discriminator payload -- the
        // new X/Y/Effect values prove the argument is the exact object passed.
        il2cpp::Il2CppObject* e2 = il2cpp::il2cpp_object_new(&DragEventArgs_class);
        DragEventArgs__ctor_095e8b5153636074(e2, nullptr, 0x5566, 0x7788, 4 /* DragDropEffects.Link */);
        auto* dropSlot = il2cpp::il2cpp_interface_dispatch(wf, &IDropTarget_class, 0);
        reinterpret_cast<void (*)(il2cpp::Il2CppObject*, il2cpp::Il2CppObject*)>(*dropSlot)(wf, e2);
        Expect(wfView->DropCount == 1, "OnDragDrop dispatch called the method (DropCount 1)");
        Expect(wfView->LastX == 0x5566 && wfView->LastY == 0x7788 && wfView->LastEffect == 4,
               "OnDragDrop dispatch delivered the NEW discriminator arg side effects");

        // 19.9 slotMap discriminator: OnDragEnter (slot 1) and OnDragDrop
        // (slot 0) resolve to DIFFERENT vtable slots (slot 0 maps to slot 0
        // = OnDragDrop, slot 1 maps to slot 1 = OnDragEnter).
        Expect(enterSlot != dropSlot, "interface slots 0 and 1 resolve to different vtable slots");

        // 19.10 Dual-implementer isolation: a WfDropSource2 object dispatches
        // its OWN OnDragEnter (its EnterCount), while the first object's state
        // is untouched.
        il2cpp::Il2CppObject* wf2 = il2cpp::il2cpp_object_new(&WfDropSource2_class);
        WfDropSource2__ctor_2358e6889d397787(wf2);
        auto* wf2View = reinterpret_cast<WfDropSource2*>(wf2);
        auto* enter2Slot = il2cpp::il2cpp_interface_dispatch(wf2, &IDropTarget_class, 1);
        reinterpret_cast<void (*)(il2cpp::Il2CppObject*, il2cpp::Il2CppObject*)>(*enter2Slot)(wf2, e1);
        Expect(wf2View->EnterCount == 1 && wfView->EnterCount == 1,
               "dual-implementer isolation: WfDropSource2's EnterCount bumped, WfDropSource untouched");

        // 19.11 CCW bridge prelude (batch-3 base): the object wraps behind a
        // CCW (non-zero, roots the object) and is NOT an RCW.
        const intptr_t wfCcw = icall_com_ccw_create(wf);
        Expect(wfCcw != 0, "ccw_create wraps the WfDropSource object");
        Expect(icall_com_is_com_object(wf) == false,
               "the plain WfDropSource object is not an RCW");
        const intptr_t wf2Ccw = icall_com_ccw_create(wf2);
        Expect(wf2Ccw != 0 && wf2Ccw != wfCcw, "each implementer gets its own CCW");

        // 19.12 CCW/GCHandle target recovery: a strong GCHandle on the object
        // (what the CCW internally holds -- com.cpp icall_com_ccw_create uses
        // icall_gchandle_alloc_normal) recovers the EXACT object, and dispatch
        // still resolves through the same (rooted) receiver.
        const int32_t gh = icall_gchandle_alloc_normal(wf);
        Expect(icall_gchandle_get_target(gh) == wf,
               "GCHandle target recovery returns the exact WfDropSource object");
        Expect(il2cpp::il2cpp_interface_dispatch(wf, &IDropTarget_class, 0) == dropSlot,
               "dispatch still resolves on the rooted receiver");
        icall_gchandle_free(gh);

        // 19.13 CCW release balances to 0 (destroys the CCW).
        Expect(icall_com_release(wf2Ccw) == 0, "releasing the WfDropSource2 CCW to 0 destroys it");
        Expect(icall_com_release(wfCcw) == 0, "releasing the WfDropSource CCW to 0 destroys it");
    }

    //------------------------------------------------------------------------
    // 20. COM stage 17 batch 2 (every platform): the typed-RCW foundation --
    //     the IDataObject view + per-CCW typed interface view registration
    //     (icall_com_ccw_register_interface_view). The com17type fixture
    //     (committed gen_v2) adds a SIMPLIFIED IDataObject contract
    //     (GetData(int,int) void / QueryGetData(int) int / SetData(int,int)
    //     void -- int32 discriminators for the real OLE FORMATETC/STGMEDIUM)
    //     with dual implementers WfDataObject/WfDataObject2 (instance
    //     observer fields). The CCW exposes a 12-slot IDataObject view
    //     (IUnknown + the 9 canonical oleidl.h methods) whose native slots
    //     (3..11) bridge to the MANAGED interface BY NAME (the typeinfo
    //     virtualSlot lookup -- the interface slots are ALPHABETICAL
    //     GetData=0/QueryGetData=1/SetData=2, NOT the OLE declarative order)
    //     via il2cpp_interface_dispatch; undeclared native methods fall to
    //     E_NOTIMPL. QI hands the view out only when registered AND
    //     il2cpp_is_instance confirms the implementer.
    //------------------------------------------------------------------------
    {
        // 20.1 The gen_v2 fixture types registered by il2cpp_runtime_init.
        Expect(il2cpp::il2cpp_class_by_name("IDataObject") == &IDataObject_class &&
               il2cpp::il2cpp_class_by_name("WfDataObject") == &WfDataObject_class &&
               il2cpp::il2cpp_class_by_name("WfDataObject2") == &WfDataObject2_class,
               "il2cpp_runtime_init registers the IDataObject/WfDataObject/WfDataObject2 descriptors");

        // 20.2 Interface descriptor shape: 3 method slots (GetData/QueryGetData/
        // SetData -- alphabetical, NOT the declarative OLE order), no instances,
        // no own interface table.
        Expect(IDataObject_class.instanceSize == 0 &&
               IDataObject_class.vtableSize == 3 &&
               IDataObject_class.interfaceCount == 0 &&
               IDataObject_class.interfaces == nullptr,
               "IDataObject descriptor: instanceSize 0, vtableSize 3, no interfaces");

        // 20.3 Concrete class interface table: WfDataObject maps exactly
        // IDataObject.
        Expect(WfDataObject_class.interfaceCount == 1 &&
               WfDataObject_class.interfaces != nullptr &&
               WfDataObject_class.interfaces[0].iface == &IDataObject_class,
               "WfDataObject interface table maps IDataObject");
        {
            const InterfaceMapEntry& entry = WfDataObject_class.interfaces[0];
            bool mapInRange = entry.slotMap != nullptr;
            for (int32_t s = 0; s < 3 && mapInRange; ++s)
                mapInRange = entry.slotMap[s] >= 0 && entry.slotMap[s] < WfDataObject_class.vtableSize;
            Expect(mapInRange, "WfDataObject IDataObject slotMap resolves all 3 slots in range");
        }

        // 20.4 Object construction: allocate + run the generated ctor; the
        // observer fields are zero-initialized by the allocation.
        il2cpp::Il2CppObject* dobj = il2cpp::il2cpp_object_new(&WfDataObject_class);
        WfDataObject__ctor_9a53ee649df188aa(dobj);
        Expect(dobj != nullptr && dobj->klass == &WfDataObject_class,
               "il2cpp_object_new + ctor stamps the WfDataObject descriptor");
        auto* doView = reinterpret_cast<WfDataObject*>(dobj);
        Expect(doView->GetDataCount == 0 && doView->SetDataCount == 0 && doView->QueryCount == 0,
               "WfDataObject observer fields zero-initialized by allocation");

        // 20.5 Typed wrapper + view registration: wrap behind a CCW and
        // declare the IDataObject view with the interface descriptor (the
        // per-CCW typed view registry -- the register_connection_point
        // precedent).
        const intptr_t doCcw = icall_com_ccw_create(dobj);
        Expect(doCcw != 0 &&
               icall_com_ccw_register_interface_view(
                   doCcw, kComIidIDataObject, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk,
               "ccw_create + register_interface_view declare the IDataObject view on the CCW");

        // 20.6 QI hands out the view (registered AND il2cpp_is_instance
        // confirms the WfDataObject implementer) at a DISTINCT address.
        intptr_t dataView = 0;
        Expect(icall_com_query_interface(doCcw, kComIidIDataObject, &dataView) == kComHrOk &&
               dataView != 0 && dataView != doCcw,
               "QI for IDataObject returns the registered view (distinct address)");

        // 20.7 GetData (native oleidl slot 3) dispatches into the MANAGED
        // method by name (interface slot 0 -- alphabetical GetData): void ->
        // S_OK + the discriminator int args reach the observer fields.
        Expect(icall_com_call_vtable_slot(dataView, kComDataObjectSlotGetData,
                                          0x1111, 0x2222, 0, 0) == kComHrOk,
               "GetData (native slot 3) dispatch returns S_OK");
        Expect(doView->GetDataCount == 1 && doView->LastFmt == 0x1111 &&
               doView->LastMedium == 0x2222,
               "GetData dispatch called the managed method (count 1 + discriminator args)");

        // 20.8 SetData (native slot 7) -> interface slot 2, void -> S_OK.
        Expect(icall_com_call_vtable_slot(dataView, kComDataObjectSlotSetData,
                                          0x3333, 0x4444, 0, 0) == kComHrOk,
               "SetData (native slot 7) dispatch returns S_OK");
        Expect(doView->SetDataCount == 1 && doView->LastFmt == 0x3333 &&
               doView->LastValue == 0x4444,
               "SetData dispatch called the managed method (count 1 + discriminator args)");

        // 20.9 QueryGetData (native slot 5) -> interface slot 1, int return:
        // the managed int discriminator 0x55 is propagated back as the
        // method's HRESULT carrier.
        doView->QueryResult = 0x55;
        Expect(icall_com_call_vtable_slot(dataView, kComDataObjectSlotQueryGetData,
                                          0x5555, 0, 0, 0) == 0x55,
               "QueryGetData (native slot 5) dispatch returns the managed int 0x55");
        Expect(doView->QueryCount == 1 && doView->LastFmt == 0x5555,
               "QueryGetData dispatch called the managed method (count 1 + arg)");

        // 20.10 Undeclared native methods -> E_NOTIMPL (the minimal-surface
        // answer): GetDataHere/GetCanonicalFormatEtc/EnumFormatEtc are NOT
        // declared by the fixture interface, so their typeinfo name resolution
        // leaves the slot unregistered. (DAdvise / DUnadvise are the stage-76
        // native data-notification path; DEnumFormatEtc is the stage-76d
        // EnumDAdvise view hand-out -- no longer E_NOTIMPL.)
        // (Each call_vtable_slot result is cast to int32_t -- the icall's
        // intptr carrier zero-extends the method's 32-bit HRESULT on x64.)
        Expect(static_cast<int32_t>(icall_com_call_vtable_slot(dataView, kComDataObjectSlotGetDataHere, 0, 0, 0, 0)) == kComHrNotImpl &&
               static_cast<int32_t>(icall_com_call_vtable_slot(dataView, kComDataObjectSlotGetCanonicalFormatEtc, 0, 0, 0, 0)) == kComHrNotImpl &&
               static_cast<int32_t>(icall_com_call_vtable_slot(dataView, kComDataObjectSlotEnumFormatEtc, 0, 0, 0, 0)) == kComHrNotImpl,
               "undeclared native methods (GetDataHere/GetCanonicalFormatEtc/EnumFormatEtc) -> E_NOTIMPL");

        // 20.11 Method-slot distinctness: the interface's alphabetical slots
        // 0/1/2 (GetData/QueryGetData/SetData) resolve to DIFFERENT vtable
        // slots -- the name-resolution map bridges the native oleidl order
        // (3/5/7) onto them.
        {
            auto* getSlot = il2cpp::il2cpp_interface_dispatch(dobj, &IDataObject_class, 0);
            auto* querySlot = il2cpp::il2cpp_interface_dispatch(dobj, &IDataObject_class, 1);
            auto* setSlot = il2cpp::il2cpp_interface_dispatch(dobj, &IDataObject_class, 2);
            Expect(getSlot != querySlot && querySlot != setSlot && getSlot != setSlot,
                   "IDataObject interface slots 0/1/2 resolve to distinct vtable slots");
        }

        // 20.12 Dual-implementer isolation: a WfDataObject2 object (own CCW +
        // view) dispatches into ITS OWN methods; the first object untouched.
        il2cpp::Il2CppObject* dobj2 = il2cpp::il2cpp_object_new(&WfDataObject2_class);
        WfDataObject2__ctor_1e91ca7fe8f07a32(dobj2);
        auto* do2View = reinterpret_cast<WfDataObject2*>(dobj2);
        const intptr_t doCcw2 = icall_com_ccw_create(dobj2);
        intptr_t dataView2 = 0;
        Expect(icall_com_ccw_register_interface_view(
                   doCcw2, kComIidIDataObject, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk &&
               icall_com_query_interface(doCcw2, kComIidIDataObject, &dataView2) == kComHrOk &&
               dataView2 != 0 && dataView2 != doCcw2,
               "second implementer: register + QI return its own IDataObject view");
        Expect(icall_com_call_vtable_slot(dataView2, kComDataObjectSlotGetData,
                                          0xAAAA, 0xBBBB, 0, 0) == kComHrOk &&
               do2View->GetDataCount == 1 && do2View->LastFmt == 0xAAAA &&
               doView->GetDataCount == 1,
               "dual-implementer isolation: WfDataObject2's GetDataCount bumped, WfDataObject untouched");

        // 20.13 Unregistered CCW -> E_NOINTERFACE for the IDataObject view (no
        // view declared on a fresh CCW; out pointer untouched).
        il2cpp::Il2CppObject* plain = il2cpp::il2cpp_object_new(&WfDropSource_class);
        WfDropSource__ctor_ac53c7667218b16d(plain);
        const intptr_t plainCcw = icall_com_ccw_create(plain);
        intptr_t plainView = 0x1234;
        Expect(icall_com_query_interface(plainCcw, kComIidIDataObject, &plainView) == kComHrNoInterface &&
               plainView == 0x1234,
               "QI for IDataObject on an unregistered CCW -> E_NOINTERFACE (out untouched)");

        // 20.14 Non-implementer object -> E_NOINTERFACE even after the view is
        // registered (the il2cpp_is_instance QI gate: a WfDropSource object
        // does NOT implement IDataObject).
        Expect(icall_com_ccw_register_interface_view(
                   plainCcw, kComIidIDataObject, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk,
               "register_interface_view on the non-implementer CCW still registers (no is_instance at register)");
        intptr_t nonImplView = 0x5678;
        Expect(icall_com_query_interface(plainCcw, kComIidIDataObject, &nonImplView) == kComHrNoInterface &&
               nonImplView == 0x5678,
               "QI for IDataObject on a non-implementer CCW -> E_NOINTERFACE (is_instance gate)");

        // 20.15 Registration guards: null ccw / null iid / null ifaceClass ->
        // ArgumentNullException; a non-IDataObject IID -> E_INVALIDARG (the
        // typed view registry currently serves the IDataObject IID only).
        Expect(ThrowsDotNet([&] { icall_com_ccw_register_interface_view(
                                     0, kComIidIDataObject, reinterpret_cast<intptr_t>(&IDataObject_class)); },
                            kArgumentNull),
               "register_interface_view(ccw 0) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_com_ccw_register_interface_view(
                                     doCcw, nullptr, reinterpret_cast<intptr_t>(&IDataObject_class)); },
                            kArgumentNull),
               "register_interface_view(iid null) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_com_ccw_register_interface_view(doCcw, kComIidIDataObject, 0); },
                            kArgumentNull),
               "register_interface_view(ifaceClass 0) -> ArgumentNullException");
        Expect(icall_com_ccw_register_interface_view(
                   doCcw, kSomeOtherIid, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrInvalidArg,
               "register_interface_view(non-IDataObject iid) -> E_INVALIDARG");

        // 20.16 OOB non-regression: the vtable-slot guard knows the 12-slot
        // IDataObject view (slot 12 -> ArgumentOutOfRangeException).
        Expect(ThrowsDotNet([&] { icall_com_call_vtable_slot(dataView, 12, 0, 0, 0, 0); },
                            kArgumentOutOfRange),
               "IDataObject view slot 12 -> ArgumentOutOfRangeException (12-slot OOB guard)");

        // 20.17 Refcount balance: every QI'd view is released, then each CCW
        // release lands on 0 (destroys the CCW + frees the GCHandle).
        Expect(icall_com_release(dataView) == 1 &&
               icall_com_release(dataView2) == 1,
               "releasing the QI'd IDataObject views balances the refcounts");
        Expect(icall_com_release(doCcw) == 0, "releasing the WfDataObject CCW to 0 destroys it");
        Expect(icall_com_release(doCcw2) == 0, "releasing the WfDataObject2 CCW to 0 destroys it");
        Expect(icall_com_release(plainCcw) == 0, "releasing the non-implementer CCW to 0 destroys it");
    }

    //------------------------------------------------------------------------
    // 21. COM stage 17 batch 3 (every platform): the managed ctor invocation
    //     mechanism (il2cpp_object_construct) + the full DragEventArgs
    //     construction (the IDataObject Data field) + the typed-RCW direction
    //     (icall_com_wrap_data_object -- a native IDataObject* wrapped into a
    //     managed IDataObject implementer). The com17type fixture (committed
    //     gen_v3) grows DragEventArgs to a single 4-arg ctor
    //     (IDataObject data, int x, int y, DragDropEffects effect) + a public
    //     IDataObject Data field (an INTERFACE-typed reference field -- the
    //     transpiler emits it as a plain Il2CppObject* GC slot). The
    //     mechanism: il2cpp_object_construct allocates via il2cpp_object_new,
    //     resolves the type's ".ctor" through its typeinfo (the fnPtr +
    //     signature + paramCount), decodes each parameter (r: -> Il2CppObject*,
    //     i / V: -> int32) and dispatches on paramCount 0..4; any failure
    //     (null klass / no typeinfo / no .ctor / null fnPtr / unsupported
    //     signature) returns nullptr -- never throws.
    //------------------------------------------------------------------------
    {
        // 21.1 Mechanism construction: il2cpp_object_construct allocates +
        // runs the typeinfo ".ctor" (signature "v(r:IDataObject,i,i,V:
        // DragDropEffects)", paramCount 4) -- the X/Y/Effect discriminators
        // land in the int32 slots and the null data arg leaves Data null.
        il2cpp::Il2CppObject* ev = il2cpp::il2cpp_object_construct(
            &DragEventArgs_class, 0, 0x1122, 0x3344, 2 /* DragDropEffects.Move */);
        Expect(ev != nullptr && ev->klass == &DragEventArgs_class,
               "il2cpp_object_construct allocates + stamps the DragEventArgs descriptor");
        auto* evView = reinterpret_cast<DragEventArgs*>(ev);
        Expect(evView->X == 0x1122 && evView->Y == 0x3344 && evView->Effect == 2,
               "il2cpp_object_construct ran the .ctor (X/Y/Effect discriminators)");
        Expect(evView->Data == nullptr,
               "il2cpp_object_construct passed the null r: data arg (Data null)");

        // 21.2 Data field wiring + dispatch: construct with a WfDataObject as
        // the r: data arg -- the pointer round-trips into the Data slot, and
        // the Data object dispatches GetData (interface slot 0) into its own
        // method.
        il2cpp::Il2CppObject* dobj = il2cpp::il2cpp_object_new(&WfDataObject_class);
        WfDataObject__ctor_9a53ee649df188aa(dobj);
        auto* doView = reinterpret_cast<WfDataObject*>(dobj);
        il2cpp::Il2CppObject* ev2 = il2cpp::il2cpp_object_construct(
            &DragEventArgs_class, reinterpret_cast<intptr_t>(dobj), 0x5566, 0x7788, 4);
        auto* ev2View = reinterpret_cast<DragEventArgs*>(ev2);
        Expect(ev2View->Data == dobj,
               "il2cpp_object_construct round-trips the r: data arg into the Data slot");
        auto* getSlot = il2cpp::il2cpp_interface_dispatch(ev2View->Data, &IDataObject_class, 0);
        reinterpret_cast<void (*)(il2cpp::Il2CppObject*, int32_t, int32_t)>(*getSlot)(
            ev2View->Data, 0x1111, 0x2222);
        Expect(doView->GetDataCount == 1 && doView->LastFmt == 0x1111 && doView->LastMedium == 0x2222,
               "the Data field dispatches GetData into the wrapped implementer");

        // 21.3 is_instance: the constructed object is a DragEventArgs; the
        // Data field object is an IDataObject implementer.
        Expect(il2cpp::il2cpp_is_instance(ev2, &DragEventArgs_class),
               "il2cpp_object_construct result is_instance DragEventArgs");
        Expect(il2cpp::il2cpp_is_instance(ev2View->Data, &IDataObject_class),
               "the Data field object is_instance IDataObject");

        // 21.4 n=0 path: a parameterless ctor (WfDropSource ".ctor" "v()")
        // constructs through the paramCount-0 switch arm.
        il2cpp::Il2CppObject* wf = il2cpp::il2cpp_object_construct(&WfDropSource_class, 0, 0, 0, 0);
        Expect(wf != nullptr && wf->klass == &WfDropSource_class,
               "il2cpp_object_construct runs the n=0 ctor (WfDropSource)");

        // 21.5 4-carrier arity: the paramCount switch consumes EXACTLY the
        // declared params -- all four carriers reach the 4-param DragEventArgs
        // ctor (data/x/y/effect), and the n=0 ctor ignores all four.
        il2cpp::Il2CppObject* ev3 = il2cpp::il2cpp_object_construct(
            &DragEventArgs_class, reinterpret_cast<intptr_t>(dobj), 0x1122, 0x3344, 0xDEADBEEF);
        auto* ev3View = reinterpret_cast<DragEventArgs*>(ev3);
        Expect(ev3View->Data == dobj && ev3View->X == 0x1122 && ev3View->Y == 0x3344 &&
                   ev3View->Effect == 0xDEADBEEF,
               "4-carrier arity: all four carriers reach the 4-param ctor (data/x/y/effect)");
        il2cpp::Il2CppObject* wf2 = il2cpp::il2cpp_object_construct(
            &WfDropSource_class, 0xDEADBEEF, 0xDEADBEEF, 0xDEADBEEF, 0xDEADBEEF);
        Expect(wf2 != nullptr && wf2->klass == &WfDropSource_class,
               "4-carrier arity: the n=0 ctor ignores all four carriers");

        // 21.6 Guards: null klass / interface (no .ctor) / enum (no methods)
        // -> nullptr (never throws).
        Expect(il2cpp::il2cpp_object_construct(nullptr, 0, 0, 0, 0) == nullptr,
               "il2cpp_object_construct(null klass) -> nullptr");
        Expect(il2cpp::il2cpp_object_construct(&IDataObject_class, 0, 0, 0, 0) == nullptr,
               "il2cpp_object_construct(interface, no .ctor) -> nullptr");
        Expect(il2cpp::il2cpp_object_construct(&DragDropEffects_class, 2, 0, 0, 0) == nullptr,
               "il2cpp_object_construct(enum, no methods) -> nullptr");

        // 21.7 Typed-RCW CCW loopback (the DragEventArgs.Data truth source):
        // a native IDataObject* (the CCW's QI'd view) wraps into a managed
        // IDataObject implementer (icall_com_wrap_data_object), the wrapper
        // lands in DragEventArgs.Data via il2cpp_object_construct, and a
        // dispatch through Data forwards through the native view BACK into
        // the original WfDataObject's methods -- the full circle. The native
        // pointer is BORROWED (the wrapper does not own the view; the caller
        // keeps the CCW alive for the wrapper's lifetime).
        il2cpp::Il2CppObject* src = il2cpp::il2cpp_object_new(&WfDataObject_class);
        WfDataObject__ctor_9a53ee649df188aa(src);
        auto* srcView = reinterpret_cast<WfDataObject*>(src);
        const intptr_t srcCcw = icall_com_ccw_create(src);
        Expect(icall_com_ccw_register_interface_view(
                   srcCcw, kComIidIDataObject, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk,
               "loopback: register the IDataObject view on the source CCW");
        intptr_t srcDataView = 0;
        Expect(icall_com_query_interface(srcCcw, kComIidIDataObject, &srcDataView) == kComHrOk &&
                   srcDataView != 0,
               "loopback: QI returns the native IDataObject view");
        const intptr_t wrapper = icall_com_wrap_data_object(
            srcDataView, reinterpret_cast<intptr_t>(&IDataObject_class));
        auto* wrapperObj = reinterpret_cast<il2cpp::Il2CppObject*>(wrapper);
        Expect(wrapper != 0 && il2cpp::il2cpp_is_instance(wrapperObj, &IDataObject_class),
               "loopback: wrap_data_object wraps the native view into a typed-RCW IDataObject");
        il2cpp::Il2CppObject* evLoop = il2cpp::il2cpp_object_construct(
            &DragEventArgs_class, wrapper, 0x1122, 0x3344, 2 /* DragDropEffects.Move */);
        auto* evLoopView = reinterpret_cast<DragEventArgs*>(evLoop);
        Expect(evLoopView->Data == wrapperObj,
               "loopback: DragEventArgs.Data carries the typed-RCW wrapper");
        auto* loopSlot = il2cpp::il2cpp_interface_dispatch(evLoopView->Data, &IDataObject_class, 0);
        reinterpret_cast<void (*)(il2cpp::Il2CppObject*, int32_t, int32_t)>(*loopSlot)(
            evLoopView->Data, 0x4444, 0x5555);
        Expect(srcView->GetDataCount == 1 && srcView->LastFmt == 0x4444 && srcView->LastMedium == 0x5555,
               "loopback: dispatch through Data forwards back to the ORIGINAL WfDataObject");
        Expect(ThrowsDotNet([&] { icall_com_wrap_data_object(0, reinterpret_cast<intptr_t>(&IDataObject_class)); },
                            kArgumentNull),
               "loopback: wrap_data_object(0) -> ArgumentNullException (null precedes the platform gate)");
        Expect(icall_com_release(srcDataView) == 1 && icall_com_release(srcCcw) == 0,
               "loopback: releasing the view + CCW balances to 0");
    }

    //------------------------------------------------------------------------
    // 22. COM stage 17 batch 4 (every platform): the CCW -> managed dispatch
    //     bridge -- the full managed drag-drop dispatch
    //     (icall_com_ccw_register_drop_dispatch_managed +
    //     icall_com_ccw_drop_dispatch_managed). Registration is
    //     self-validating: the interface descriptor must be an interface
    //     (instanceSize 0), every drop method must match the "v(r:<args>)"
    //     shape, the args class must have a ".ctor", and the Effect field
    //     offset is cached for the write-back. The pump drives the pipeline:
    //     wrap the native pDataObj into a typed-RCW IDataObject -> construct
    //     the DragEventArgs (data/x/y/effect) -> il2cpp_interface_dispatch
    //     into the managed OnDragEnter/OnDragOver/OnDragDrop -> effect
    //     write-back. The is_instance gate guards the dispatch (a
    //     non-implementer returns E_NOTIMPL -- never an interface_dispatch
    //     abort); unregistered / undeclared method / construct failure all
    //     fall to E_NOTIMPL.
    //------------------------------------------------------------------------
    {
        // 22.1 The drop target: a WfDropSource object (an IDropTarget
        // implementer) wrapped behind a CCW.
        il2cpp::Il2CppObject* dst = il2cpp::il2cpp_object_new(&WfDropSource_class);
        WfDropSource__ctor_ac53c7667218b16d(dst);
        auto* dstView = reinterpret_cast<WfDropSource*>(dst);
        const intptr_t dstCcw = icall_com_ccw_create(dst);
        Expect(dstCcw != 0, "ccw_create wraps the WfDropSource drop target");

        // 22.2 The pDataObj: a native IDataObject* -- the CCW's QI'd IDataObject
        // view of a WfDataObject (the section 21.7 loopback pattern).
        il2cpp::Il2CppObject* src = il2cpp::il2cpp_object_new(&WfDataObject_class);
        WfDataObject__ctor_9a53ee649df188aa(src);
        const intptr_t srcCcw = icall_com_ccw_create(src);
        Expect(icall_com_ccw_register_interface_view(
                   srcCcw, kComIidIDataObject, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk,
               "register the IDataObject view on the pDataObj source CCW");
        intptr_t dataView = 0;
        Expect(icall_com_query_interface(srcCcw, kComIidIDataObject, &dataView) == kComHrOk &&
               dataView != 0,
               "QI returns the native IDataObject* (the pump's pDataObj)");

        // 22.3 Registration: declare the managed drop dispatch (interface =
        // IDropTarget, args = DragEventArgs) -> S_OK.
        Expect(icall_com_ccw_register_drop_dispatch_managed(
                   dstCcw, reinterpret_cast<intptr_t>(&IDropTarget_class),
                   reinterpret_cast<intptr_t>(&DragEventArgs_class)) == kComHrOk,
               "register_drop_dispatch_managed declares the managed drop dispatch (S_OK)");

        // 22.4 Null guards: ccw / ifaceClass / argsClass 0 -> ArgumentNullException.
        Expect(ThrowsDotNet([&] { icall_com_ccw_register_drop_dispatch_managed(0, 0, 0); },
                            kArgumentNull),
               "register_drop_dispatch_managed(ccw 0) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_com_ccw_register_drop_dispatch_managed(
                                       dstCcw, 0, reinterpret_cast<intptr_t>(&DragEventArgs_class)); },
                            kArgumentNull),
               "register_drop_dispatch_managed(ifaceClass 0) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_com_ccw_register_drop_dispatch_managed(
                                       dstCcw, reinterpret_cast<intptr_t>(&IDropTarget_class), 0); },
                            kArgumentNull),
               "register_drop_dispatch_managed(argsClass 0) -> ArgumentNullException");

        // 22.5 Self-validation: a non-interface ifaceClass (instanceSize != 0)
        // and a mismatched argsClass (not the "v(r:<args>)" parameter type)
        // are both E_INVALIDARG -- and leave the prior registration intact
        // (validate-then-write).
        Expect(icall_com_ccw_register_drop_dispatch_managed(
                   dstCcw, reinterpret_cast<intptr_t>(&WfDropSource_class),
                   reinterpret_cast<intptr_t>(&DragEventArgs_class)) == kComHrInvalidArg,
               "register_drop_dispatch_managed(non-interface ifaceClass) -> E_INVALIDARG");
        Expect(icall_com_ccw_register_drop_dispatch_managed(
                   dstCcw, reinterpret_cast<intptr_t>(&IDropTarget_class),
                   reinterpret_cast<intptr_t>(&WfDropSource_class)) == kComHrInvalidArg,
               "register_drop_dispatch_managed(mismatched argsClass) -> E_INVALIDARG");

        // 22.6 DragEnter pump (method 0): wrap dataView -> construct
        // DragEventArgs(data, x, y, effect) -> dispatch the managed
        // OnDragEnter -> the handler observes X/Y/Effect -> the effect writes
        // back. keyState (0xABCD) is DROPPED (the fixture DragEventArgs has no
        // KeyState field) without perturbing the dispatch.
        uint32_t effect = 0x2; // DragDropEffects.Move -- the allowed mask
        Expect(icall_com_ccw_drop_dispatch_managed(
                   dstCcw, kComDropMethodDragEnter, dataView, 0xABCD,
                   0x1122, 0x3344, &effect) == kComHrOk &&
               dstView->EnterCount == 1 &&
               dstView->LastX == 0x1122 && dstView->LastY == 0x3344 && dstView->LastEffect == 2 &&
               effect == 0x2,
               "DragEnter pump dispatches OnDragEnter (EnterCount 1 + X/Y/Effect discriminators + effect write-back)");

        // 22.7 DragOver pump (method 1): no data object (pDataObj 0 -> the
        // construct seeds a null Data) -- the managed OnDragOver still runs.
        Expect(icall_com_ccw_drop_dispatch_managed(
                   dstCcw, kComDropMethodDragOver, 0, 0x55, 0x0102, 0x0304, nullptr) == kComHrOk &&
               dstView->OverCount == 1,
               "DragOver pump dispatches OnDragOver (pDataObj 0 -> null Data, keyState dropped)");

        // 22.8 Drop pump (method 2): a FRESH discriminator payload -- the new
        // X/Y/Effect values prove the argument is the exact constructed object.
        uint32_t dropEffect = 0x4; // DragDropEffects.Link
        Expect(icall_com_ccw_drop_dispatch_managed(
                   dstCcw, kComDropMethodDrop, dataView, 0x321, 0x5566, 0x7788, &dropEffect) == kComHrOk &&
               dstView->DropCount == 1 &&
               dstView->LastX == 0x5566 && dstView->LastY == 0x7788 && dstView->LastEffect == 4 &&
               dropEffect == 0x4,
               "Drop pump dispatches OnDragDrop (fresh X/Y/Effect discriminators + effect write-back)");

        // 22.9 DragLeave pump (method 3): the zero-value args construct reaches
        // the managed OnDragLeave (the programmatic path the view never uses).
        Expect(icall_com_ccw_drop_dispatch_managed(
                   dstCcw, kComDropMethodDragLeave, 0, 0, 0, 0, nullptr) == kComHrOk &&
               dstView->LeaveCount == 1,
               "DragLeave pump (method 3) dispatches the managed OnDragLeave (zero-value args)");

        // 22.10 Unregistered fallback: a FRESH CCW's pump -> E_NOTIMPL (the
        // managed dispatch is per-CCW state).
        const intptr_t freshCcw = icall_com_ccw_create(dst);
        Expect(icall_com_ccw_drop_dispatch_managed(
                   freshCcw, kComDropMethodDragEnter, dataView, 0, 0, 0, nullptr) == kComHrNotImpl,
               "an unregistered CCW's pump -> E_NOTIMPL (per-CCW managed dispatch)");

        // 22.11 Non-implementer gate: registration on a WfDataObject CCW
        // succeeds (register does NOT check is_instance -- the batch-2 QI-gate
        // precedent), but the pump's is_instance gate answers E_NOTIMPL
        // instead of hitting the interface_dispatch abort.
        const intptr_t nonImplCcw = icall_com_ccw_create(src);
        Expect(icall_com_ccw_register_drop_dispatch_managed(
                   nonImplCcw, reinterpret_cast<intptr_t>(&IDropTarget_class),
                   reinterpret_cast<intptr_t>(&DragEventArgs_class)) == kComHrOk,
               "registering a non-implementer CCW still succeeds (no is_instance at register)");
        Expect(icall_com_ccw_drop_dispatch_managed(
                   nonImplCcw, kComDropMethodDragEnter, dataView, 0, 0, 0, nullptr) == kComHrNotImpl,
               "the non-implementer pump -> E_NOTIMPL (is_instance gate -- no dispatch abort)");

        // 22.12 Dual-CCW isolation: the unregistered second CCW stays
        // E_NOTIMPL while the registered first CCW still dispatches.
        Expect(icall_com_ccw_drop_dispatch_managed(
                   freshCcw, kComDropMethodDragEnter, dataView, 0, 0x9999, 0xAAAA, nullptr) == kComHrNotImpl,
               "dual-CCW isolation: the unregistered second CCW stays E_NOTIMPL");
        Expect(icall_com_ccw_drop_dispatch_managed(
                   dstCcw, kComDropMethodDragEnter, dataView, 0, 0x1122, 0x3344, nullptr) == kComHrOk &&
               dstView->EnterCount == 2,
               "dual-CCW isolation: the registered first CCW still dispatches (EnterCount 2)");

        // 22.13 Arg defense: pump method OOB -> ArgumentOutOfRangeException;
        // pump ccw 0 -> ArgumentNullException.
        Expect(ThrowsDotNet([&] { icall_com_ccw_drop_dispatch_managed(dstCcw, 4, 0, 0, 0, 0, nullptr); },
                            kArgumentOutOfRange),
               "pump method 4 -> ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&] { icall_com_ccw_drop_dispatch_managed(0, 0, 0, 0, 0, 0, nullptr); },
                            kArgumentNull),
               "pump ccw 0 -> ArgumentNullException");

        // 22.14 Registration does not perturb the CCW refcount (create => 1:
        // AddRef -> 2, Release -> 1 -- the registered state is stored
        // pointers, never AddRef'd or released).
        Expect(icall_com_add_ref(dstCcw) == 2 && icall_com_release(dstCcw) == 1,
               "registering the managed dispatch does not perturb the CCW refcount");

        // 22.15 Release balance: every QI'd view + every CCW releases to 0
        // (destroys the CCWs and frees the GCHandles).
        Expect(icall_com_release(dataView) == 1 &&
               icall_com_release(srcCcw) == 0 &&
               icall_com_release(nonImplCcw) == 0 &&
               icall_com_release(freshCcw) == 0 &&
               icall_com_release(dstCcw) == 0,
               "releasing the views + CCWs balances to 0 (destroys every CCW)");
    }

#if defined(_WIN32)
    //------------------------------------------------------------------------
    // 22b. The IDropTarget view integration (WINDOWS only -- the stage-18
    //      direct-drive template, no window/OLE registration needed). The
    //      managed drop dispatch is registered on a WfDropSource CCW; QI
    //      returns the IDropTarget view; call_vtable_slot drives the view
    //      methods, which DECODE the by-value POINTL (x = low 32 bits of the
    //      intptr carrier, y = high 32 bits) and call the pump -- the
    //      native view -> bridge -> managed handler pipeline end-to-end.
    //      DragLeave keeps the stage-15 S_OK notification contract (the view
    //      never dispatches it -- LeaveCount stays 0).
    //------------------------------------------------------------------------
    {
        il2cpp::Il2CppObject* wf22 = il2cpp::il2cpp_object_new(&WfDropSource_class);
        WfDropSource__ctor_ac53c7667218b16d(wf22);
        auto* wf22View = reinterpret_cast<WfDropSource*>(wf22);
        const intptr_t wf22Ccw = icall_com_ccw_create(wf22);
        Expect(icall_com_ccw_register_drop_dispatch_managed(
                   wf22Ccw, reinterpret_cast<intptr_t>(&IDropTarget_class),
                   reinterpret_cast<intptr_t>(&DragEventArgs_class)) == kComHrOk,
               "22b: register the managed drop dispatch on the view CCW (S_OK)");
        intptr_t wf22ViewPtr = 0;
        Expect(icall_com_query_interface(wf22Ccw, kComIidIDropTarget, &wf22ViewPtr) == kComHrOk &&
               wf22ViewPtr != 0 && wf22ViewPtr != wf22Ccw,
               "22b: QI for IDropTarget returns the distinct view");

        // The pDataObj: a native IDataObject* (the section 21.7 loopback
        // pattern -- the view's first argument).
        il2cpp::Il2CppObject* src22 = il2cpp::il2cpp_object_new(&WfDataObject_class);
        WfDataObject__ctor_9a53ee649df188aa(src22);
        const intptr_t src22Ccw = icall_com_ccw_create(src22);
        Expect(icall_com_ccw_register_interface_view(
                   src22Ccw, kComIidIDataObject, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk,
               "22b: register the IDataObject view on the pDataObj source CCW");
        intptr_t dataView22 = 0;
        Expect(icall_com_query_interface(src22Ccw, kComIidIDataObject, &dataView22) == kComHrOk &&
               dataView22 != 0,
               "22b: QI returns the native IDataObject* (the view's pDataObj)");

        // DragEnter (slot 3): the by-value POINTL carrier decodes into x/y
        // (x = low 32, y = high 32) and the bridge dispatches the managed
        // OnDragEnter end-to-end -- X/Y/Effect observers + the effect write-back.
        uint32_t enterEff = 0x2;
        const intptr_t ptBits = (static_cast<intptr_t>(0x55667788u) << 32) | 0x11223344u;
        Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
                   wf22ViewPtr, kComDropSlotDragEnter, dataView22, 0xABCD, ptBits,
                   reinterpret_cast<intptr_t>(&enterEff))) == kComHrOk &&
               wf22View->EnterCount == 1 &&
               wf22View->LastX == 0x11223344 && wf22View->LastY == 0x55667788 &&
               wf22View->LastEffect == 2 && enterEff == 0x2,
               "22b: DragEnter view -> POINTL decode -> bridge -> managed OnDragEnter (X/Y/Effect + effect write-back)");

        // DragOver (slot 4 -- THREE params after self: grfKeyState, pt,
        // pdwEffect; a3 is the ignored trailing carrier): no data object --
        // the bridge constructs with null Data and dispatches the managed
        // OnDragOver.
        uint32_t overEff = 0x1;
        Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
                   wf22ViewPtr, kComDropSlotDragOver, 0x55, ptBits,
                   reinterpret_cast<intptr_t>(&overEff), 0)) == kComHrOk &&
               wf22View->OverCount == 1 && overEff == 0x1,
               "22b: DragOver view -> bridge -> managed OnDragOver (OverCount 1 + effect write-back)");

        // Drop (slot 6): data object + key + x/y -- the fresh Effect
        // discriminator proves the write-back reads the constructed args.
        uint32_t dropEff = 0x4;
        Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
                   wf22ViewPtr, kComDropSlotDrop, dataView22, 0x321, ptBits,
                   reinterpret_cast<intptr_t>(&dropEff))) == kComHrOk &&
               wf22View->DropCount == 1 &&
               wf22View->LastX == 0x11223344 && wf22View->LastY == 0x55667788 &&
               wf22View->LastEffect == 4 && dropEff == 0x4,
               "22b: Drop view -> bridge -> managed OnDragDrop (DropCount 1 + fresh Effect write-back)");

        // DragLeave (slot 5): the stage-15 S_OK notification contract -- the
        // view NEVER dispatches it (LeaveCount stays 0).
        Expect(static_cast<int32_t>(icall_com_call_vtable_slot(
                   wf22ViewPtr, kComDropSlotDragLeave, 0, 0, 0, 0)) == kComHrOk &&
               wf22View->LeaveCount == 0,
               "22b: DragLeave view stays S_OK without dispatching the managed handler (stage-15 contract)");

        // Release the views + identity references (destroys every CCW).
        Expect(icall_com_release(wf22ViewPtr) == 1 &&
               icall_com_release(wf22Ccw) == 0 &&
               icall_com_release(dataView22) == 1 &&
               icall_com_release(src22Ccw) == 0,
               "22b: releasing the views + CCWs balances to 0");
    }
#endif

    //--------------------------------------------------------------------------
    // 23. IDispatch::Invoke -> managed method dispatch (stage 18): the
    // ActiveX / WebBrowser automation backing. GetIDsOfNames resolves a name
    // to a dispId; Invoke validates the DISPPARAMS (phase 7) and then -- when
    // the invoke dispatch is REGISTERED -- resolves the member name to the
    // managed interface slot and calls through il2cpp_interface_dispatch:
    // 'i' methods return the int32 as VT_I4, 'v' methods run with VT_EMPTY.
    //--------------------------------------------------------------------------
    {
        // A WfDataObject implementer behind a CCW + the invoke dispatch.
        il2cpp::Il2CppObject* dobj23 = il2cpp::il2cpp_object_new(&WfDataObject_class);
        WfDataObject__ctor_9a53ee649df188aa(dobj23);
        auto* do23 = reinterpret_cast<WfDataObject*>(dobj23);
        const intptr_t ccw23 = icall_com_ccw_create(dobj23);
        Expect(ccw23 != 0, "23: ccw_create wraps the WfDataObject implementer");

        // Register the IDispatch::Invoke -> managed dispatch (interface
        // descriptor + typeinfo; validate-then-write).
        Expect(icall_com_ccw_register_invoke_dispatch(
                   ccw23, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk,
               "23: register_invoke_dispatch accepts the IDataObject interface");

        // GetIDsOfNames: "QueryGetData" -> dispId 1 (deterministic).
        const uint8_t nullIid23[16] = { 0 };
        NativeGetIDsOfNamesFn getIds23 =
            reinterpret_cast<NativeGetIDsOfNamesFn>(const_cast<void*>(ComVtable(ccw23)[5]));
        wchar_t qName[] = L"QueryGetData";
        wchar_t gName[] = L"GetData";
        void* qNames[] = { qName };
        int32_t qDispId = -1;
        Expect(getIds23(reinterpret_cast<void*>(ccw23), nullIid23, qNames, 1, 0, &qDispId) == kComHrOk &&
               qDispId == 1,
               "23: GetIDsOfNames(QueryGetData) -> dispId 1");
        void* gNames[] = { gName };
        int32_t gDispId = -1;
        Expect(getIds23(reinterpret_cast<void*>(ccw23), nullIid23, gNames, 1, 0, &gDispId) == kComHrOk &&
               gDispId == 2,
               "23: GetIDsOfNames(GetData) -> dispId 2");

        // Invoke(QueryGetData, VT_I4 fmt) -- the int-returning managed method:
        // the int32 return arrives as VT_I4 (lVal@8) and the observer records
        // the discriminator argument. Set the fixture's QueryResult so the
        // return value is distinguishable.
        do23->QueryResult = 0x55;
        NativeInvokeFn invoke23 =
            reinterpret_cast<NativeInvokeFn>(const_cast<void*>(ComVtable(ccw23)[6]));
        uint8_t argVariant23[16];
        MakeVariant(kVtI4, argVariant23);
        const int32_t argVal23 = 0x77;
        std::memcpy(argVariant23 + kVariantVtOffset + 8, &argVal23, sizeof(int32_t));
        TestDispParams qParams23 = { argVariant23, nullptr, 1, 0 };
        uint8_t qResult23[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                  0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke23(reinterpret_cast<void*>(ccw23), qDispId, nullIid23, 0, 1,
                        &qParams23, qResult23, nullptr, nullptr) == kComHrOk &&
               do23->QueryCount == 1 && do23->LastFmt == 0x77,
               "23: Invoke(QueryGetData) dispatched to the managed method (count + discriminator)");
        uint16_t qVt = 0;
        std::memcpy(&qVt, qResult23, sizeof(qVt));
        int32_t qRet = 0;
        std::memcpy(&qRet, qResult23 + kVariantVtOffset + 8, sizeof(qRet));
        Expect(qVt == kVtI4 && qRet == 0x55,
               "23: the managed int32 return arrives as VT_I4 (lVal 0x55)");

        // Invoke(GetData, VT_I4 fmt, VT_I4 medium) -- the void method: runs
        // with VT_EMPTY return. Two positional args (reverse-order rgvarg:
        // argVariant[0] = medium, argVariant[1] = fmt).
        uint8_t argA23[16], argB23[16];
        MakeVariant(kVtI4, argA23);
        const int32_t argMed23 = 0x2222;
        std::memcpy(argA23 + kVariantVtOffset + 8, &argMed23, sizeof(int32_t));
        MakeVariant(kVtI4, argB23);
        const int32_t argFmt23 = 0x1111;
        std::memcpy(argB23 + kVariantVtOffset + 8, &argFmt23, sizeof(int32_t));
        uint8_t gArgs23[32];
        std::memcpy(gArgs23, argA23, 16);          // rgvarg[0] = LAST arg (medium)
        std::memcpy(gArgs23 + 16, argB23, 16);     // rgvarg[1] = FIRST arg (fmt)
        TestDispParams gParams23 = { gArgs23, nullptr, 2, 0 };
        uint8_t gResult23[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                  0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke23(reinterpret_cast<void*>(ccw23), gDispId, nullIid23, 0, 1,
                        &gParams23, gResult23, nullptr, nullptr) == kComHrOk &&
               do23->GetDataCount == 1 && do23->LastFmt == 0x1111 &&
               do23->LastMedium == 0x2222,
               "23: Invoke(GetData, fmt, medium) dispatched with reverse-order VT_I4 args");
        uint16_t gVt = 0;
        std::memcpy(&gVt, gResult23, sizeof(gVt));
        Expect(gVt == kVtEmpty,
               "23: the void managed method returns VT_EMPTY");

        // Invoke without pVarResult still runs the managed method.
        Expect(invoke23(reinterpret_cast<void*>(ccw23), qDispId, nullIid23, 0, 1,
                        &qParams23, nullptr, nullptr, nullptr) == kComHrOk &&
               do23->QueryCount == 2,
               "23: Invoke(null pVarResult) still dispatches the managed method");

        // Non-implementer: a plain string object behind a CCW + the same
        // registration -> E_NOTIMPL (the is_instance gate; never an abort).
        // The member name must be registered on THIS CCW first (dispIds are
        // per-CCW).
        il2cpp::Il2CppString* str23 = il2cpp::il2cpp_string_new_from_cs("nope");
        const intptr_t strCcw23 = icall_com_ccw_create(reinterpret_cast<il2cpp::Il2CppObject*>(str23));
        int32_t strDisp23 = -1;
        Expect(getIds23(reinterpret_cast<void*>(strCcw23), nullIid23, qNames, 1, 0, &strDisp23) == kComHrOk &&
               strDisp23 == 1 &&
               icall_com_ccw_register_invoke_dispatch(
                   strCcw23, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk &&
               invoke23(reinterpret_cast<void*>(strCcw23), strDisp23, nullIid23, 0, 1,
                        &qParams23, qResult23, nullptr, nullptr) == kComHrNotImpl,
               "23: non-implementer Invoke -> E_NOTIMPL (is_instance gate)");
        Expect(icall_com_release(strCcw23) == 0,
               "23: releasing the non-implementer CCW balances to 0");

        // Unregistered invoke dispatch: the phase-7 honest VT_EMPTY answer
        // (a fresh CCW WITHOUT the invoke registration -- Invoke validates,
        // the member is registered, then returns S_OK with a zeroed result
        // VARIANT).
        il2cpp::Il2CppObject* plain23 = il2cpp::il2cpp_object_new(&WfDataObject_class);
        WfDataObject__ctor_9a53ee649df188aa(plain23);
        const intptr_t plainCcw23 = icall_com_ccw_create(plain23);
        int32_t plainDisp23 = -1;
        Expect(getIds23(reinterpret_cast<void*>(plainCcw23), nullIid23, qNames, 1, 0, &plainDisp23) == kComHrOk &&
               plainDisp23 == 1,
               "23: member name registered on the unregistered CCW");
        uint8_t plainResult23[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                      0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke23(reinterpret_cast<void*>(plainCcw23), plainDisp23, nullIid23, 0, 1,
                        &qParams23, plainResult23, nullptr, nullptr) == kComHrOk &&
               plainResult23[0] == 0 && plainResult23[1] == 0,
               "23: unregistered Invoke keeps the honest VT_EMPTY answer");
        Expect(icall_com_release(plainCcw23) == 0,
               "23: releasing the unregistered CCW balances to 0");

        // Release the registered CCW (destroys it).
        Expect(icall_com_release(ccw23) == 0,
               "23: releasing the registered CCW balances to 0");
    }

    //--------------------------------------------------------------------------
    // 24. IDispatch::Invoke typed arguments (stage 19): VT_R8 (double) and
    // VT_BSTR (string) parameters + a double return -- the automation
    // argument surface beyond the int32 (VT_I4) shape. Scale(factor, label)
    // is the void two-arg case; Rate(factor) is the double-return one-arg
    // case.
    //--------------------------------------------------------------------------
    {
        il2cpp::Il2CppObject* autoObj = il2cpp::il2cpp_object_new(&WfAutoScale_class);
        WfAutoScale__ctor_19c5c3d511b8b242(autoObj);
        auto* autoView = reinterpret_cast<WfAutoScale*>(autoObj);
        const intptr_t autoCcw = icall_com_ccw_create(autoObj);
        Expect(autoCcw != 0 &&
               icall_com_ccw_register_invoke_dispatch(
                   autoCcw, reinterpret_cast<intptr_t>(&IAutoScale_class)) == kComHrOk,
               "24: ccw_create + register_invoke_dispatch (IAutoScale)");

        const uint8_t nullIid24[16] = { 0 };
        NativeGetIDsOfNamesFn getIds24 =
            reinterpret_cast<NativeGetIDsOfNamesFn>(const_cast<void*>(ComVtable(autoCcw)[5]));
        NativeInvokeFn invoke24 =
            reinterpret_cast<NativeInvokeFn>(const_cast<void*>(ComVtable(autoCcw)[6]));
        wchar_t scaleName[] = L"Scale";
        wchar_t rateName[] = L"Rate";
        void* scaleNames[] = { scaleName };
        void* rateNames[] = { rateName };
        int32_t scaleDisp = -1, rateDisp = -1;
        Expect(getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, scaleNames, 1, 0, &scaleDisp) == kComHrOk &&
               scaleDisp == 1 &&
               getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, rateNames, 1, 0, &rateDisp) == kComHrOk &&
               rateDisp == 2,
               "24: GetIDsOfNames(Scale/Rate) -> dispIds 1/2");

        // Scale(double 2.5, BSTR "auto") -- the void two-arg case with a
        // VT_R8 + VT_BSTR argument pair (reverse-order rgvarg: [0] = label,
        // [1] = factor).
        uint8_t factorVar[16], labelVar[16];
        MakeVariant(kVtR8, factorVar);
        const double factorVal = 2.5;
        std::memcpy(factorVar + kVariantVtOffset + 8, &factorVal, sizeof(factorVal));
        MakeVariant(kVtBstr, labelVar);
        wchar_t labelBuf[] = L"auto";
        void* labelPtr = labelBuf;
        std::memcpy(labelVar + kVariantVtOffset + 8, &labelPtr, sizeof(labelPtr));
        uint8_t scaleArgs24[32];
        std::memcpy(scaleArgs24, labelVar, 16);       // rgvarg[0] = LAST (label)
        std::memcpy(scaleArgs24 + 16, factorVar, 16); // rgvarg[1] = FIRST (factor)
        TestDispParams scaleParams24 = { scaleArgs24, nullptr, 2, 0 };
        uint8_t scaleResult24[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                      0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), scaleDisp, nullIid24, 0, 1,
                        &scaleParams24, scaleResult24, nullptr, nullptr) == kComHrOk &&
               autoView->ScaleCount == 1 && autoView->LastFactorInt == 2 &&
               autoView->LastLabel != nullptr &&
               reinterpret_cast<il2cpp::Il2CppString*>(autoView->LastLabel)->length == 4 &&
               std::memcmp(reinterpret_cast<il2cpp::Il2CppString*>(autoView->LastLabel)->chars,
                           L"auto", 4 * sizeof(wchar_t)) == 0,
               "24: Invoke(Scale, VT_R8 2.5, VT_BSTR auto) dispatched (count + factor int + label)");
        uint16_t scaleVt = 0;
        std::memcpy(&scaleVt, scaleResult24, sizeof(scaleVt));
        Expect(scaleVt == kVtEmpty,
               "24: the void Scale returns VT_EMPTY");

        // Rate(double 3.0) -> the double return arrives as VT_R8.
        autoView->RateResultInt = 7;
        uint8_t rateVar[16];
        MakeVariant(kVtR8, rateVar);
        const double rateFactor = 3.0;
        std::memcpy(rateVar + kVariantVtOffset + 8, &rateFactor, sizeof(rateFactor));
        TestDispParams rateParams24 = { rateVar, nullptr, 1, 0 };
        uint8_t rateResult24[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                     0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), rateDisp, nullIid24, 0, 1,
                        &rateParams24, rateResult24, nullptr, nullptr) == kComHrOk,
               "24: Invoke(Rate, VT_R8 3.0) returns S_OK");
        uint16_t rateVt = 0;
        std::memcpy(&rateVt, rateResult24, sizeof(rateVt));
        double rateRet = 0.0;
        std::memcpy(&rateRet, rateResult24 + kVariantVtOffset + 8, sizeof(rateRet));
        Expect(rateVt == kVtR8 && rateRet == 21.0,
               "24: the double return arrives as VT_R8 (7 * 3.0 = 21.0)");

        // ===== Section 25: IDispatch PROPERTY access (stage 20) =====
        // GetIDsOfNames("ScaleCountProp") -> dispId 3 (the property name, not
        // the accessor). Invoke with DISPATCH_PROPERTYGET (wFlags = 2) routes
        // to get_ScaleCountProp (the int return as VT_I4); DISPATCH_PROPERTYPUT
        // (wFlags = 4) routes to set_ScaleCountProp (the single value arg).
        // The property-put value travels in rgvarg[0] (reverse order, cArgs 1).
        wchar_t propName[] = L"ScaleCountProp";
        void* propNames[] = { propName };
        int32_t propDisp = -1;
        Expect(getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, propNames, 1, 0, &propDisp) == kComHrOk &&
               propDisp == 3,
               "25: GetIDsOfNames(ScaleCountProp) -> dispId 3");
        autoView->ScaleCount = 7;
        uint8_t propGetResult[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                      0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        TestDispParams propGetParams = { nullptr, nullptr, 0, 0 };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), propDisp, nullIid24, 0, 2,
                        &propGetParams, propGetResult, nullptr, nullptr) == kComHrOk &&
               propGetResult[0] == kVtI4 &&
               std::memcmp(propGetResult + 8, "\x07\x00\x00\x00", 4) == 0,
               "25: Invoke(PROPERTYGET ScaleCountProp) returns VT_I4 7");
        uint8_t propPutVar[16];
        MakeVariant(kVtI4, propPutVar);
        std::memcpy(propPutVar + kVariantVtOffset + 8, "\x05\x00\x00\x00", 4);
        TestDispParams propPutParams = { propPutVar, nullptr, 1, 0 };
        uint8_t propPutResult[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                      0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), propDisp, nullIid24, 0, 4,
                        &propPutParams, propPutResult, nullptr, nullptr) == kComHrOk &&
               autoView->ScaleCount == 5,
               "25: Invoke(PROPERTYPUT ScaleCountProp 5) sets ScaleCount to 5");
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), propDisp, nullIid24, 0, 2,
                        &propGetParams, propGetResult, nullptr, nullptr) == kComHrOk &&
               std::memcmp(propGetResult + 8, "\x05\x00\x00\x00", 4) == 0,
               "25: Invoke(PROPERTYGET after put) returns VT_I4 5");

        // ===== Section 26: IDispatch VT_BOOL + VT_UI4 arguments (stage 21) =====
        // Adjust(bool on, uint amount) -- the bool (VT_BOOL, 0/1) and the
        // unsigned int (VT_UI4) both ride the 4-byte lVal slot; reverse-order
        // rgvarg: [0] = amount (10), [1] = on (TRUE). The int return arrives
        // as VT_I4 (1 + 10 = 11).
        wchar_t adjName[] = L"Adjust";
        void* adjNames[] = { adjName };
        int32_t adjDisp = -1;
        Expect(getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, adjNames, 1, 0, &adjDisp) == kComHrOk &&
               adjDisp == 4,
               "26: GetIDsOfNames(Adjust) -> dispId 4");
        uint8_t adjAmountVar[16], adjOnVar[16];
        MakeVariant(kVtUI4, adjAmountVar);
        std::memcpy(adjAmountVar + kVariantVtOffset + 8, "\x0a\x00\x00\x00", 4);
        MakeVariant(kVtBool, adjOnVar);
        std::memcpy(adjOnVar + kVariantVtOffset + 8, "\x01\x00\x00\x00", 4);
        uint8_t adjArgs26[32];
        std::memcpy(adjArgs26, adjAmountVar, 16);       // rgvarg[0] = LAST (amount)
        std::memcpy(adjArgs26 + 16, adjOnVar, 16);      // rgvarg[1] = FIRST (on)
        TestDispParams adjParams26 = { adjArgs26, nullptr, 2, 0 };
        uint8_t adjResult26[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), adjDisp, nullIid24, 0, 1,
                        &adjParams26, adjResult26, nullptr, nullptr) == kComHrOk &&
               adjResult26[0] == kVtI4 &&
               std::memcmp(adjResult26 + 8, "\x0b\x00\x00\x00", 4) == 0,
               "26: Invoke(Adjust, VT_BOOL TRUE + VT_UI4 10) returns VT_I4 11");

        // ===== Section 27: IDispatch VT_BOOL return (stage 22) =====
        // IsEnabled() has the "b()" signature (bool return) -- the int32
        // return is written as VT_BOOL (0/1); GetCount() has "i()" (VT_I4).
        // ScaleCount = 3 -> IsEnabled TRUE (VT_BOOL 1) + GetCount 3 (VT_I4).
        wchar_t isOnName[] = L"IsEnabled";
        void* isOnNames[] = { isOnName };
        int32_t isOnDisp = -1;
        Expect(getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, isOnNames, 1, 0, &isOnDisp) == kComHrOk &&
               isOnDisp == 5,
               "27: GetIDsOfNames(IsEnabled) -> dispId 5");
        wchar_t cntName[] = L"GetCount";
        void* cntNames[] = { cntName };
        int32_t cntDisp = -1;
        Expect(getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, cntNames, 1, 0, &cntDisp) == kComHrOk &&
               cntDisp == 6,
               "27: GetIDsOfNames(GetCount) -> dispId 6");
        autoView->ScaleCount = 3;
        TestDispParams emptyParams27 = { nullptr, nullptr, 0, 0 };
        uint8_t isOnResult[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                   0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        uint8_t cntResult[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                  0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), isOnDisp, nullIid24, 0, 1,
                        &emptyParams27, isOnResult, nullptr, nullptr) == kComHrOk &&
               isOnResult[0] == kVtBool &&
               std::memcmp(isOnResult + 8, "\x01\x00\x00\x00", 4) == 0,
               "27: Invoke(IsEnabled) returns VT_BOOL TRUE");
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), cntDisp, nullIid24, 0, 1,
                        &emptyParams27, cntResult, nullptr, nullptr) == kComHrOk &&
               cntResult[0] == kVtI4 &&
               std::memcmp(cntResult + 8, "\x03\x00\x00\x00", 4) == 0,
               "27: Invoke(GetCount) returns VT_I4 3");

        // ===== Section 28: IDispatch VT_BSTR return (stage 23) =====
        // GetLabel() has the "r:System.String()" signature -- the managed
        // string return is SysAllocStringLen'd into a caller-owned BSTR
        // (bstrVal@8). Set LastLabel = "auto", invoke, read the BSTR back.
        wchar_t lblName[] = L"GetLabel";
        void* lblNames[] = { lblName };
        int32_t lblDisp = -1;
        Expect(getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, lblNames, 1, 0, &lblDisp) == kComHrOk &&
               lblDisp == 7,
               "28: GetIDsOfNames(GetLabel) -> dispId 7");
        il2cpp::Il2CppObject* lblManaged = il2cpp::il2cpp_pinvoke_string_from_utf16(
            reinterpret_cast<const char16_t*>(L"auto"));
        autoView->LastLabel = lblManaged;
        TestDispParams lblParams28 = { nullptr, nullptr, 0, 0 };
        uint8_t lblResult28[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), lblDisp, nullIid24, 0, 1,
                        &lblParams28, lblResult28, nullptr, nullptr) == kComHrOk &&
               lblResult28[0] == kVtBstr,
               "28: Invoke(GetLabel) returns VT_BSTR");
        void* lblBstr = nullptr;
        std::memcpy(&lblBstr, lblResult28 + kVariantVtOffset + 8, sizeof(lblBstr));
        Expect(lblBstr != nullptr && std::memcmp(lblBstr, L"auto", 4 * sizeof(wchar_t)) == 0,
               "28: the returned BSTR reads 'auto'");

        // ===== Section 29: IDispatch VT_I8 argument (stage 24) =====
        // SetBig(long) has the "v(l)" signature -- the 64-bit argument arrives
        // as VT_I8 (llVal@8). The observer stores the low 32 bits (BigLow).
        wchar_t bigName[] = L"SetBig";
        void* bigNames[] = { bigName };
        int32_t bigDisp = -1;
        Expect(getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, bigNames, 1, 0, &bigDisp) == kComHrOk &&
               bigDisp == 8,
               "29: GetIDsOfNames(SetBig) -> dispId 8");
        uint8_t bigVar[16];
        MakeVariant(kVtI8, bigVar);
        const int64_t bigVal = INT64_C(0x123456789A);
        std::memcpy(bigVar + kVariantVtOffset + 8, &bigVal, sizeof(bigVal));
        TestDispParams bigParams29 = { bigVar, nullptr, 1, 0 };
        uint8_t bigResult29[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), bigDisp, nullIid24, 0, 1,
                        &bigParams29, bigResult29, nullptr, nullptr) == kComHrOk &&
               bigResult29[0] == kVtEmpty &&
               autoView->BigLow == 0x3456789A,
               "29: Invoke(SetBig, VT_I8 0x123456789A) stores the low 32 bits");

        // ===== Section 30: IDispatch VT_I8 return (stage 25) =====
        // GetBig() has the "l()" signature -- the 64-bit return arrives as
        // VT_I8 (llVal@8) = 5000000000.
        wchar_t gbName[] = L"GetBig";
        void* gbNames[] = { gbName };
        int32_t gbDisp = -1;
        Expect(getIds24(reinterpret_cast<void*>(autoCcw), nullIid24, gbNames, 1, 0, &gbDisp) == kComHrOk &&
               gbDisp == 9,
               "30: GetIDsOfNames(GetBig) -> dispId 9");
        TestDispParams gbParams30 = { nullptr, nullptr, 0, 0 };
        uint8_t gbResult30[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                   0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), gbDisp, nullIid24, 0, 1,
                        &gbParams30, gbResult30, nullptr, nullptr) == kComHrOk &&
               gbResult30[0] == kVtI8,
               "30: Invoke(GetBig) returns VT_I8");
        int64_t gbRet = 0;
        std::memcpy(&gbRet, gbResult30 + kVariantVtOffset + 8, sizeof(gbRet));
        Expect(gbRet == INT64_C(5000000000),
               "30: the VT_I8 return reads 5000000000");

        // ===== Section 31: property put with DISPID_PROPERTYPUT named arg (stage 26) =====
        // The standard property-put shape: cNamed 1 with rgdispidNamedArgs[0]
        // = DISPID_PROPERTYPUT (-3), the value in rgvarg[0]. ScaleCountProp is
        // set to 9 through this path (vs the positional section 25).
        uint8_t putNVar[16];
        MakeVariant(kVtI4, putNVar);
        std::memcpy(putNVar + kVariantVtOffset + 8, "\x09\x00\x00\x00", 4);
        int32_t putNDispId = kDispIdPropertyPut;
        TestDispParams putNParams = { putNVar, &putNDispId, 1, 1 };
        uint8_t putNResult[16] = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                                   0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Expect(invoke24(reinterpret_cast<void*>(autoCcw), propDisp, nullIid24, 0, 4,
                        &putNParams, putNResult, nullptr, nullptr) == kComHrOk &&
               autoView->ScaleCount == 9,
               "31: Invoke(PROPERTYPUT ScaleCountProp 9, DISPID_PROPERTYPUT named) sets ScaleCount to 9");

        // ===== Section 32: IOleInPlaceObject view (stage 27) =====
        // QI for IID_IOleInPlaceObject hands the 9-slot in-place view;
        // GetWindow writes a null hwnd (headless, S_OK), InPlaceDeactivate /
        // UIDeactivate record the call (the com.cpp counters) and return S_OK.
        void* inPlaceObjRaw = nullptr;
        intptr_t inPlaceObj = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleInPlaceObject, &inPlaceObj) == kComHrOk &&
               inPlaceObj != 0,
               "32: QI(IID_IOleInPlaceObject) hands the in-place view");
        inPlaceObjRaw = reinterpret_cast<void*>(inPlaceObj);
        typedef int32_t (TEST_COM_CALL* GetWindowFn)(void*, void**);
        typedef int32_t (TEST_COM_CALL* VoidFn)(void*);
        GetWindowFn inGetWindow = reinterpret_cast<GetWindowFn>(ComVtable(inPlaceObj)[3]);
        VoidFn inDeactivate = reinterpret_cast<VoidFn>(ComVtable(inPlaceObj)[5]);
        VoidFn inUiDeactivate = reinterpret_cast<VoidFn>(ComVtable(inPlaceObj)[6]);
        void* inHwnd = reinterpret_cast<void*>(0x1234);
        Expect(inGetWindow(inPlaceObjRaw, &inHwnd) == kComHrOk && inHwnd == nullptr,
               "32: GetWindow writes a null hwnd (headless)");
        Expect(inDeactivate(inPlaceObjRaw) == kComHrOk,
               "32: InPlaceDeactivate returns S_OK");
        Expect(inUiDeactivate(inPlaceObjRaw) == kComHrOk,
               "32: UIDeactivate returns S_OK");
        // Balance the QI AddRef: Release the in-place view through its own
        // vtable (slot 2), so the section-24 final release sees refCount 1.
        typedef uint32_t (TEST_COM_CALL* ReleaseFn)(void*);
        ReleaseFn inRelease = reinterpret_cast<ReleaseFn>(ComVtable(inPlaceObj)[2]);
        inRelease(inPlaceObjRaw);

        // ===== Section 33: IProvideClassInfo (stage 28) =====
        // QI for IID_IProvideClassInfo hands the 4-slot class-info view;
        // GetClassInfo answers E_NOTIMPL (no ITypeInfo is built).
        intptr_t classInfo = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIProvideClassInfo, &classInfo) == kComHrOk &&
               classInfo != 0,
               "33: QI(IID_IProvideClassInfo) hands the class-info view");
        void* classInfoRaw = reinterpret_cast<void*>(classInfo);
        typedef int32_t (TEST_COM_CALL* GetClassInfoFn)(void*, void**);
        GetClassInfoFn ciGet = reinterpret_cast<GetClassInfoFn>(ComVtable(classInfo)[3]);
        void* ciTypeInfo = reinterpret_cast<void*>(0x1357);
        Expect(ciGet(classInfoRaw, &ciTypeInfo) == kComHrNotImpl && ciTypeInfo == nullptr,
               "33: GetClassInfo is E_NOTIMPL with a null out");
        ReleaseFn ciRelease = reinterpret_cast<ReleaseFn>(ComVtable(classInfo)[2]);
        ciRelease(classInfoRaw);

        // ===== Section 34: IOleInPlaceSite (stage 29) =====
        // QI for IID_IOleInPlaceSite hands the 15-slot in-place-site view:
        // GetWindow null hwnd, OnInPlaceActivate / CanInPlaceActivate S_OK.
        intptr_t siteObj = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleInPlaceSite, &siteObj) == kComHrOk &&
               siteObj != 0,
               "34: QI(IID_IOleInPlaceSite) hands the in-place-site view");
        void* siteObjRaw = reinterpret_cast<void*>(siteObj);
        GetWindowFn siteGetWindow = reinterpret_cast<GetWindowFn>(ComVtable(siteObj)[3]);
        VoidFn siteActivate = reinterpret_cast<VoidFn>(ComVtable(siteObj)[5]);
        void* siteHwnd = reinterpret_cast<void*>(0x2468);
        Expect(siteGetWindow(siteObjRaw, &siteHwnd) == kComHrOk && siteHwnd == nullptr,
               "34: IOleInPlaceSite GetWindow writes a null hwnd");
        Expect(siteActivate(siteObjRaw) == kComHrOk,
               "34: CanInPlaceActivate returns S_OK");
        ReleaseFn siteRelease = reinterpret_cast<ReleaseFn>(ComVtable(siteObj)[2]);
        siteRelease(siteObjRaw);

        // ===== Section 35: IAdviseSink (stage 30) =====
        // QI for IID_IAdviseSink hands the 7-slot advise-sink view;
        // OnDataChange / OnSave answer S_OK (the notification is accepted and
        // dropped headless).
        intptr_t sinkObj = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIAdviseSink, &sinkObj) == kComHrOk &&
               sinkObj != 0,
               "35: QI(IID_IAdviseSink) hands the advise-sink view");
        void* sinkObjRaw = reinterpret_cast<void*>(sinkObj);
        typedef int32_t (TEST_COM_CALL* DataChangeFn)(void*, const void*, const void*);
        DataChangeFn sinkData = reinterpret_cast<DataChangeFn>(ComVtable(sinkObj)[3]);
        VoidFn sinkSave = reinterpret_cast<VoidFn>(ComVtable(sinkObj)[6]);
        Expect(sinkData(sinkObjRaw, nullptr, nullptr) == kComHrOk,
               "35: OnDataChange returns S_OK");
        Expect(sinkSave(sinkObjRaw) == kComHrOk,
               "35: OnSave returns S_OK");
        ReleaseFn sinkRelease = reinterpret_cast<ReleaseFn>(ComVtable(sinkObj)[2]);
        sinkRelease(sinkObjRaw);

        // ===== Section 36: IOleInPlaceUIWindow (stage 31) =====
        // QI for IID_IOleInPlaceUIWindow hands the 9-slot UI-window view:
        // GetWindow null hwnd, GetBorder E_NOTIMPL.
        intptr_t uiWin = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleInPlaceUIWindow, &uiWin) == kComHrOk &&
               uiWin != 0,
               "36: QI(IID_IOleInPlaceUIWindow) hands the UI-window view");
        void* uiWinRaw = reinterpret_cast<void*>(uiWin);
        GetWindowFn uiGetWindow = reinterpret_cast<GetWindowFn>(ComVtable(uiWin)[3]);
        typedef int32_t (TEST_COM_CALL* GetBorderFn)(void*, void*);
        GetBorderFn uiGetBorder = reinterpret_cast<GetBorderFn>(ComVtable(uiWin)[5]);
        void* uiHwnd = reinterpret_cast<void*>(0x1357);
        Expect(uiGetWindow(uiWinRaw, &uiHwnd) == kComHrOk && uiHwnd == nullptr,
               "36: IOleInPlaceUIWindow GetWindow writes a null hwnd");
        Expect(uiGetBorder(uiWinRaw, nullptr) == kComHrNotImpl,
               "36: GetBorder is E_NOTIMPL");
        ReleaseFn uiRelease = reinterpret_cast<ReleaseFn>(ComVtable(uiWin)[2]);
        uiRelease(uiWinRaw);

        // ===== Section 37: IOleInPlaceFrame (stage 32) =====
        // QI for IID_IOleInPlaceFrame hands the 15-slot in-place-frame view:
        // GetWindow null hwnd, SetActiveObject S_OK, GetBorder E_NOTIMPL.
        intptr_t frameObj = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleInPlaceFrame, &frameObj) == kComHrOk &&
               frameObj != 0,
               "37: QI(IID_IOleInPlaceFrame) hands the in-place-frame view");
        void* frameObjRaw = reinterpret_cast<void*>(frameObj);
        GetWindowFn frameGetWindow = reinterpret_cast<GetWindowFn>(ComVtable(frameObj)[3]);
        typedef int32_t (TEST_COM_CALL* SetActiveFn)(void*, void*, void*);
        SetActiveFn frameSetActive = reinterpret_cast<SetActiveFn>(ComVtable(frameObj)[8]);
        typedef int32_t (TEST_COM_CALL* GetBorderFn2)(void*, void*);
        GetBorderFn2 frameGetBorder = reinterpret_cast<GetBorderFn2>(ComVtable(frameObj)[5]);
        void* frameHwnd = reinterpret_cast<void*>(0x8642);
        Expect(frameGetWindow(frameObjRaw, &frameHwnd) == kComHrOk && frameHwnd == nullptr,
               "37: IOleInPlaceFrame GetWindow writes a null hwnd");
        Expect(frameSetActive(frameObjRaw, nullptr, nullptr) == kComHrOk,
               "37: SetActiveObject returns S_OK");
        Expect(frameGetBorder(frameObjRaw, nullptr) == kComHrNotImpl,
               "37: GetBorder is E_NOTIMPL");
        ReleaseFn frameRelease = reinterpret_cast<ReleaseFn>(ComVtable(frameObj)[2]);
        frameRelease(frameObjRaw);

        // ===== Section 38: IOleContainer (stage 33) =====
        // QI for IID_IOleContainer hands the 6-slot container view:
        // ParseDisplayName / EnumObjects / LockContainer all E_NOTIMPL.
        intptr_t containerObj = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleContainer, &containerObj) == kComHrOk &&
               containerObj != 0,
               "38: QI(IID_IOleContainer) hands the container view");
        void* containerRaw = reinterpret_cast<void*>(containerObj);
        typedef int32_t (TEST_COM_CALL* EnumObjsFn)(void*, uint32_t, void**);
        EnumObjsFn contEnum = reinterpret_cast<EnumObjsFn>(ComVtable(containerObj)[4]);
        typedef int32_t (TEST_COM_CALL* LockFn)(void*, int32_t);
        LockFn contLock = reinterpret_cast<LockFn>(ComVtable(containerObj)[5]);
        void* contEnumOut = nullptr;
        Expect(contEnum(containerRaw, 0, &contEnumOut) == kComHrNotImpl && contEnumOut == nullptr,
               "38: EnumObjects is E_NOTIMPL with a null out");
        Expect(contLock(containerRaw, 1) == kComHrNotImpl,
               "38: LockContainer is E_NOTIMPL");
        ReleaseFn contRelease = reinterpret_cast<ReleaseFn>(ComVtable(containerObj)[2]);
        contRelease(containerRaw);

        // ===== Section 39: IEnumUnknown (stage 34) =====
        // QI for IID_IEnumUnknown hands the 7-slot unknown-enumerator view:
        // Next / Skip / Reset / Clone all E_NOTIMPL (empty headless).
        intptr_t enumUnk = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIEnumUnknown, &enumUnk) == kComHrOk &&
               enumUnk != 0,
               "39: QI(IID_IEnumUnknown) hands the unknown-enumerator view");
        void* enumUnkRaw = reinterpret_cast<void*>(enumUnk);
        typedef int32_t (TEST_COM_CALL* EnumNextFn)(void*, uint32_t, void**, uint32_t*);
        EnumNextFn euNext = reinterpret_cast<EnumNextFn>(ComVtable(enumUnk)[3]);
        VoidFn euReset = reinterpret_cast<VoidFn>(ComVtable(enumUnk)[5]);
        void* euOut = reinterpret_cast<void*>(0x1234);
        Expect(euNext(enumUnkRaw, 1, &euOut, nullptr) == kComHrNotImpl && euOut == reinterpret_cast<void*>(0x1234),
               "39: Next is E_NOTIMPL with the out untouched");
        Expect(euReset(enumUnkRaw) == kComHrNotImpl,
               "39: Reset is E_NOTIMPL");
        ReleaseFn euRelease = reinterpret_cast<ReleaseFn>(ComVtable(enumUnk)[2]);
        euRelease(enumUnkRaw);

        // ===== Section 40: IEnumString (stage 35) =====
        // QI for IID_IEnumString hands the 7-slot string-enumerator view:
        // Next / Skip / Reset / Clone all E_NOTIMPL (empty headless).
        intptr_t enumStr = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIEnumString, &enumStr) == kComHrOk &&
               enumStr != 0,
               "40: QI(IID_IEnumString) hands the string-enumerator view");
        void* enumStrRaw = reinterpret_cast<void*>(enumStr);
        EnumNextFn esNext = reinterpret_cast<EnumNextFn>(ComVtable(enumStr)[3]);
        VoidFn esReset = reinterpret_cast<VoidFn>(ComVtable(enumStr)[5]);
        void* esOut = reinterpret_cast<void*>(0x5678);
        Expect(esNext(enumStrRaw, 1, &esOut, nullptr) == kComHrNotImpl && esOut == reinterpret_cast<void*>(0x5678),
               "40: Next is E_NOTIMPL with the out untouched");
        Expect(esReset(enumStrRaw) == kComHrNotImpl,
               "40: Reset is E_NOTIMPL");
        ReleaseFn esRelease = reinterpret_cast<ReleaseFn>(ComVtable(enumStr)[2]);
        esRelease(enumStrRaw);

        // ===== Section 41: IEnumFORMATETC (stage 36) =====
        // QI for IID_IEnumFORMATETC hands the 7-slot format-enumerator view:
        // Next / Skip / Reset / Clone all E_NOTIMPL (empty headless).
        intptr_t enumFmt = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIEnumFormatEtc, &enumFmt) == kComHrOk &&
               enumFmt != 0,
               "41: QI(IID_IEnumFORMATETC) hands the format-enumerator view");
        void* enumFmtRaw = reinterpret_cast<void*>(enumFmt);
        typedef int32_t (TEST_COM_CALL* FmtNextFn)(void*, uint32_t, void*, uint32_t*);
        FmtNextFn efNext = reinterpret_cast<FmtNextFn>(ComVtable(enumFmt)[3]);
        VoidFn efReset = reinterpret_cast<VoidFn>(ComVtable(enumFmt)[5]);
        uint8_t efOut[32] = { 0 };
        Expect(efNext(enumFmtRaw, 1, efOut, nullptr) == kComHrNotImpl,
               "41: Next is E_NOTIMPL");
        Expect(efReset(enumFmtRaw) == kComHrNotImpl,
               "41: Reset is E_NOTIMPL");
        ReleaseFn efRelease = reinterpret_cast<ReleaseFn>(ComVtable(enumFmt)[2]);
        efRelease(enumFmtRaw);

        // ===== Section 42: IPersistStream (stage 37) =====
        // QI for IID_IPersistStream hands the 8-slot stream-persistence view:
        // GetClassID / IsDirty / Save all E_NOTIMPL.
        intptr_t persist = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIPersistStream, &persist) == kComHrOk &&
               persist != 0,
               "42: QI(IID_IPersistStream) hands the persist-stream view");
        void* persistRaw = reinterpret_cast<void*>(persist);
        typedef int32_t (TEST_COM_CALL* GetClassIdFn)(void*, void*);
        GetClassIdFn psGetClassId = reinterpret_cast<GetClassIdFn>(ComVtable(persist)[3]);
        VoidFn psIsDirty = reinterpret_cast<VoidFn>(ComVtable(persist)[4]);
        uint8_t psClsid[16] = { 0 };
        Expect(psGetClassId(persistRaw, psClsid) == kComHrNotImpl,
               "42: GetClassID is E_NOTIMPL");
        Expect(psIsDirty(persistRaw) == kComHrNotImpl,
               "42: IsDirty is E_NOTIMPL");
        ReleaseFn psRelease = reinterpret_cast<ReleaseFn>(ComVtable(persist)[2]);
        psRelease(persistRaw);

        // ===== Section 43: IPersistFile (stage 38) =====
        // QI for IID_IPersistFile hands the 9-slot file-persistence view:
        // GetClassID / IsDirty all E_NOTIMPL.
        intptr_t persistFile = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIPersistFile, &persistFile) == kComHrOk &&
               persistFile != 0,
               "43: QI(IID_IPersistFile) hands the persist-file view");
        void* persistFileRaw = reinterpret_cast<void*>(persistFile);
        GetClassIdFn pfGetClassId = reinterpret_cast<GetClassIdFn>(ComVtable(persistFile)[3]);
        VoidFn pfIsDirty = reinterpret_cast<VoidFn>(ComVtable(persistFile)[4]);
        uint8_t pfClsid[16] = { 0 };
        Expect(pfGetClassId(persistFileRaw, pfClsid) == kComHrNotImpl,
               "43: GetClassID is E_NOTIMPL");
        Expect(pfIsDirty(persistFileRaw) == kComHrNotImpl,
               "43: IsDirty is E_NOTIMPL");
        ReleaseFn pfRelease = reinterpret_cast<ReleaseFn>(ComVtable(persistFile)[2]);
        pfRelease(persistFileRaw);

        // ===== Section 44: IPersist (stage 39) =====
        // QI for IID_IPersist hands the 4-slot base-persistence view:
        // GetClassID E_NOTIMPL.
        intptr_t persistBase = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIPersist, &persistBase) == kComHrOk &&
               persistBase != 0,
               "44: QI(IID_IPersist) hands the base-persist view");
        void* persistBaseRaw = reinterpret_cast<void*>(persistBase);
        GetClassIdFn pbGetClassId = reinterpret_cast<GetClassIdFn>(ComVtable(persistBase)[3]);
        uint8_t pbClsid[16] = { 0 };
        Expect(pbGetClassId(persistBaseRaw, pbClsid) == kComHrNotImpl,
               "44: GetClassID is E_NOTIMPL");
        ReleaseFn pbRelease = reinterpret_cast<ReleaseFn>(ComVtable(persistBase)[2]);
        pbRelease(persistBaseRaw);

        // ===== Section 45: IOleInPlaceActiveObject (stage 40) =====
        // QI for IID_IOleInPlaceActiveObject hands the 12-slot in-place
        // active-object view: GetWindow null hwnd + EnableModeless E_NOTIMPL.
        intptr_t ipActive = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleInPlaceActiveObject, &ipActive) == kComHrOk &&
               ipActive != 0,
               "45: QI(IID_IOleInPlaceActiveObject) hands the in-place active-object view");
        void* ipActiveRaw = reinterpret_cast<void*>(ipActive);
        typedef int32_t (TEST_COM_CALL* GetWindowFn)(void*, void**);
        GetWindowFn ipaGetWindow = reinterpret_cast<GetWindowFn>(ComVtable(ipActive)[3]);
        VoidFn ipaEnableModeless = reinterpret_cast<VoidFn>(ComVtable(ipActive)[11]);
        void* ipaHwnd = reinterpret_cast<void*>(0x1);
        Expect(ipaGetWindow(ipActiveRaw, &ipaHwnd) == kComHrOk && ipaHwnd == nullptr,
               "45: GetWindow answers null hwnd");
        Expect(ipaEnableModeless(ipActiveRaw) == kComHrNotImpl,
               "45: EnableModeless is E_NOTIMPL");
        ReleaseFn ipaRelease = reinterpret_cast<ReleaseFn>(ComVtable(ipActive)[2]);
        ipaRelease(ipActiveRaw);

        // ===== Section 46: IPersistPropertyBag (stage 41) =====
        // QI for IID_IPersistPropertyBag hands the 7-slot property-bag
        // persistence view: GetClassID / InitNew all E_NOTIMPL.
        intptr_t ppBag = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIPersistPropertyBag, &ppBag) == kComHrOk &&
               ppBag != 0,
               "46: QI(IID_IPersistPropertyBag) hands the property-bag view");
        void* ppBagRaw = reinterpret_cast<void*>(ppBag);
        GetClassIdFn ppbGetClassId = reinterpret_cast<GetClassIdFn>(ComVtable(ppBag)[3]);
        VoidFn ppbInitNew = reinterpret_cast<VoidFn>(ComVtable(ppBag)[4]);
        uint8_t ppbClsid[16] = { 0 };
        Expect(ppbGetClassId(ppBagRaw, ppbClsid) == kComHrNotImpl,
               "46: GetClassID is E_NOTIMPL");
        Expect(ppbInitNew(ppBagRaw) == kComHrNotImpl,
               "46: InitNew is E_NOTIMPL");
        ReleaseFn ppbRelease = reinterpret_cast<ReleaseFn>(ComVtable(ppBag)[2]);
        ppbRelease(ppBagRaw);

        // ===== Section 47: IParseDisplayName (stage 42) =====
        // QI for IID_IParseDisplayName hands the 4-slot display-name parser
        // view: ParseDisplayName E_NOTIMPL.
        intptr_t parseDn = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIParseDisplayName, &parseDn) == kComHrOk &&
               parseDn != 0,
               "47: QI(IID_IParseDisplayName) hands the display-name parser view");
        void* parseDnRaw = reinterpret_cast<void*>(parseDn);
        typedef int32_t (TEST_COM_CALL* ParseDisplayNameFn)(void*, void*, void*, uint32_t*, void**);
        ParseDisplayNameFn pdParse = reinterpret_cast<ParseDisplayNameFn>(ComVtable(parseDn)[3]);
        uint32_t pdEaten = 0;
        void* pdMoniker = reinterpret_cast<void*>(0x1);
        Expect(pdParse(parseDnRaw, nullptr, nullptr, &pdEaten, &pdMoniker) == kComHrNotImpl,
               "47: ParseDisplayName is E_NOTIMPL");
        ReleaseFn pdRelease = reinterpret_cast<ReleaseFn>(ComVtable(parseDn)[2]);
        pdRelease(parseDnRaw);

        // ===== Section 48: IViewObject (stage 43) =====
        // QI for IID_IViewObject hands the 9-slot view-object view:
        // Unfreeze E_NOTIMPL.
        intptr_t viewObj = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIViewObject, &viewObj) == kComHrOk &&
               viewObj != 0,
               "48: QI(IID_IViewObject) hands the view-object view");
        void* viewObjRaw = reinterpret_cast<void*>(viewObj);
        VoidFn voUnfreeze = reinterpret_cast<VoidFn>(ComVtable(viewObj)[6]);
        Expect(voUnfreeze(viewObjRaw) == kComHrNotImpl,
               "48: Unfreeze is E_NOTIMPL");
        ReleaseFn voRelease = reinterpret_cast<ReleaseFn>(ComVtable(viewObj)[2]);
        voRelease(viewObjRaw);

        // ===== Section 49: IEnumSTATDATA (stage 44) =====
        // QI for IID_IEnumSTATDATA hands the 7-slot STATDATA enumerator view:
        // Next / Reset all E_NOTIMPL.
        intptr_t enumSd = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIEnumSTATDATA, &enumSd) == kComHrOk &&
               enumSd != 0,
               "49: QI(IID_IEnumSTATDATA) hands the STATDATA enumerator view");
        void* enumSdRaw = reinterpret_cast<void*>(enumSd);
        typedef int32_t (TEST_COM_CALL* SdNextFn)(void*, uint32_t, void*, uint32_t*);
        SdNextFn sdNext = reinterpret_cast<SdNextFn>(ComVtable(enumSd)[3]);
        VoidFn sdReset = reinterpret_cast<VoidFn>(ComVtable(enumSd)[5]);
        uint8_t sdOut[32] = { 0 };
        Expect(sdNext(enumSdRaw, 1, sdOut, nullptr) == kComHrNotImpl,
               "49: Next is E_NOTIMPL");
        Expect(sdReset(enumSdRaw) == kComHrNotImpl,
               "49: Reset is E_NOTIMPL");
        ReleaseFn sdRelease = reinterpret_cast<ReleaseFn>(ComVtable(enumSd)[2]);
        sdRelease(enumSdRaw);

        // ===== Section 50: IEnumOLEVERB (stage 45) =====
        // QI for IID_IEnumOLEVERB hands the 7-slot OLEVERB enumerator view:
        // Next / Reset all E_NOTIMPL.
        intptr_t enumOv = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIEnumOLEVERB, &enumOv) == kComHrOk &&
               enumOv != 0,
               "50: QI(IID_IEnumOLEVERB) hands the OLEVERB enumerator view");
        void* enumOvRaw = reinterpret_cast<void*>(enumOv);
        typedef int32_t (TEST_COM_CALL* OvNextFn)(void*, uint32_t, void*, uint32_t*);
        OvNextFn ovNext = reinterpret_cast<OvNextFn>(ComVtable(enumOv)[3]);
        VoidFn ovReset = reinterpret_cast<VoidFn>(ComVtable(enumOv)[5]);
        uint8_t ovOut[32] = { 0 };
        Expect(ovNext(enumOvRaw, 1, ovOut, nullptr) == kComHrNotImpl,
               "50: Next is E_NOTIMPL");
        Expect(ovReset(enumOvRaw) == kComHrNotImpl,
               "50: Reset is E_NOTIMPL");
        ReleaseFn ovRelease = reinterpret_cast<ReleaseFn>(ComVtable(enumOv)[2]);
        ovRelease(enumOvRaw);

        // ===== Section 51: IEnumMoniker (stage 46) =====
        // QI for IID_IEnumMoniker hands the 7-slot moniker enumerator view:
        // Next / Reset all E_NOTIMPL.
        intptr_t enumMk = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIEnumMoniker, &enumMk) == kComHrOk &&
               enumMk != 0,
               "51: QI(IID_IEnumMoniker) hands the moniker enumerator view");
        void* enumMkRaw = reinterpret_cast<void*>(enumMk);
        typedef int32_t (TEST_COM_CALL* MkNextFn)(void*, uint32_t, void*, uint32_t*);
        MkNextFn mkNext = reinterpret_cast<MkNextFn>(ComVtable(enumMk)[3]);
        VoidFn mkReset = reinterpret_cast<VoidFn>(ComVtable(enumMk)[5]);
        uint8_t mkOut[32] = { 0 };
        Expect(mkNext(enumMkRaw, 1, mkOut, nullptr) == kComHrNotImpl,
               "51: Next is E_NOTIMPL");
        Expect(mkReset(enumMkRaw) == kComHrNotImpl,
               "51: Reset is E_NOTIMPL");
        ReleaseFn mkRelease = reinterpret_cast<ReleaseFn>(ComVtable(enumMk)[2]);
        mkRelease(enumMkRaw);

        // ===== Section 52: IViewObject2 (stage 47) =====
        // QI for IID_IViewObject2 hands the 10-slot view-object extension
        // view: GetExtent E_NOTIMPL.
        intptr_t viewObj2 = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIViewObject2, &viewObj2) == kComHrOk &&
               viewObj2 != 0,
               "52: QI(IID_IViewObject2) hands the view-object extension view");
        void* viewObj2Raw = reinterpret_cast<void*>(viewObj2);
        typedef int32_t (TEST_COM_CALL* GetExtentFn)(void*, uint32_t, int32_t, void*);
        GetExtentFn vo2GetExtent = reinterpret_cast<GetExtentFn>(ComVtable(viewObj2)[9]);
        uint8_t vo2Sizel[8] = { 0 };
        Expect(vo2GetExtent(viewObj2Raw, 0, 0, vo2Sizel) == kComHrNotImpl,
               "52: GetExtent is E_NOTIMPL");
        ReleaseFn vo2Release = reinterpret_cast<ReleaseFn>(ComVtable(viewObj2)[2]);
        vo2Release(viewObj2Raw);

        // ===== Section 53: IOleLink (stage 48) =====
        // QI for IID_IOleLink hands the 12-slot OLE link view:
        // Unbind E_NOTIMPL.
        intptr_t oleLink = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleLink, &oleLink) == kComHrOk &&
               oleLink != 0,
               "53: QI(IID_IOleLink) hands the OLE link view");
        void* oleLinkRaw = reinterpret_cast<void*>(oleLink);
        VoidFn olUnbind = reinterpret_cast<VoidFn>(ComVtable(oleLink)[10]);
        Expect(olUnbind(oleLinkRaw) == kComHrNotImpl,
               "53: Unbind is E_NOTIMPL");
        ReleaseFn olRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleLink)[2]);
        olRelease(oleLinkRaw);

        // ===== Section 54: IMoniker (stage 49) =====
        // QI for IID_IMoniker hands the 23-slot moniker view (IMoniker
        // derives from IPersistStream): IsSystemMoniker E_NOTIMPL.
        intptr_t moniker = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIMoniker, &moniker) == kComHrOk &&
               moniker != 0,
               "54: QI(IID_IMoniker) hands the moniker view");
        void* monikerRaw = reinterpret_cast<void*>(moniker);
        typedef int32_t (TEST_COM_CALL* IsSystemMonikerFn)(void*, uint32_t*);
        IsSystemMonikerFn mkIsSystem = reinterpret_cast<IsSystemMonikerFn>(ComVtable(moniker)[22]);
        uint32_t mkSys = 0;
        Expect(mkIsSystem(monikerRaw, &mkSys) == kComHrNotImpl,
               "54: IsSystemMoniker is E_NOTIMPL");
        ReleaseFn mk2Release = reinterpret_cast<ReleaseFn>(ComVtable(moniker)[2]);
        mk2Release(monikerRaw);

        // ===== Section 55: IRunningObjectTable (stage 50) =====
        // QI for IID_IRunningObjectTable hands the 10-slot ROT view:
        // EnumRunning E_NOTIMPL.
        intptr_t rot = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIRunningObjectTable, &rot) == kComHrOk &&
               rot != 0,
               "55: QI(IID_IRunningObjectTable) hands the ROT view");
        void* rotRaw = reinterpret_cast<void*>(rot);
        typedef int32_t (TEST_COM_CALL* EnumRunningFn)(void*, void**);
        EnumRunningFn rotEnum = reinterpret_cast<EnumRunningFn>(ComVtable(rot)[9]);
        void* rotEnumOut = reinterpret_cast<void*>(0x1);
        Expect(rotEnum(rotRaw, &rotEnumOut) == kComHrNotImpl,
               "55: EnumRunning is E_NOTIMPL");
        ReleaseFn rotRelease = reinterpret_cast<ReleaseFn>(ComVtable(rot)[2]);
        rotRelease(rotRaw);

        // ===== Section 56: IBindCtx (stage 51) =====
        // QI for IID_IBindCtx hands the 13-slot bind-context view:
        // ReleaseBoundObjects E_NOTIMPL.
        intptr_t bindCtx = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIBindCtx, &bindCtx) == kComHrOk &&
               bindCtx != 0,
               "56: QI(IID_IBindCtx) hands the bind-context view");
        void* bindCtxRaw = reinterpret_cast<void*>(bindCtx);
        VoidFn bcReleaseBound = reinterpret_cast<VoidFn>(ComVtable(bindCtx)[5]);
        Expect(bcReleaseBound(bindCtxRaw) == kComHrNotImpl,
               "56: ReleaseBoundObjects is E_NOTIMPL");
        ReleaseFn bcRelease = reinterpret_cast<ReleaseFn>(ComVtable(bindCtx)[2]);
        bcRelease(bindCtxRaw);

        // ===== Section 57: IOleAdviseHolder (stage 52) =====
        // QI for IID_IOleAdviseHolder hands the 6-slot advise-holder view:
        // EnumAdvise E_NOTIMPL.
        intptr_t oleAdv = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleAdviseHolder, &oleAdv) == kComHrOk &&
               oleAdv != 0,
               "57: QI(IID_IOleAdviseHolder) hands the advise-holder view");
        void* oleAdvRaw = reinterpret_cast<void*>(oleAdv);
        typedef int32_t (TEST_COM_CALL* EnumAdviseFn)(void*, void**);
        EnumAdviseFn oaEnum = reinterpret_cast<EnumAdviseFn>(ComVtable(oleAdv)[5]);
        void* oaEnumOut = reinterpret_cast<void*>(0x1);
        Expect(oaEnum(oleAdvRaw, &oaEnumOut) == kComHrNotImpl,
               "57: EnumAdvise is E_NOTIMPL");
        ReleaseFn oaRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleAdv)[2]);
        oaRelease(oleAdvRaw);

        // ===== Section 58: IDataAdviseHolder (stage 53) =====
        // QI for IID_IDataAdviseHolder hands the 6-slot data advise-holder
        // view: EnumAdvise E_NOTIMPL.
        intptr_t dataAdv = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIDataAdviseHolder, &dataAdv) == kComHrOk &&
               dataAdv != 0,
               "58: QI(IID_IDataAdviseHolder) hands the data advise-holder view");
        void* dataAdvRaw = reinterpret_cast<void*>(dataAdv);
        EnumAdviseFn daEnum = reinterpret_cast<EnumAdviseFn>(ComVtable(dataAdv)[5]);
        void* daEnumOut = reinterpret_cast<void*>(0x1);
        Expect(daEnum(dataAdvRaw, &daEnumOut) == kComHrNotImpl,
               "58: EnumAdvise is E_NOTIMPL");
        ReleaseFn daRelease = reinterpret_cast<ReleaseFn>(ComVtable(dataAdv)[2]);
        daRelease(dataAdvRaw);

        // ===== Section 59: IOleCache (stage 54) =====
        // QI for IID_IOleCache hands the 8-slot OLE cache view:
        // InitCache E_NOTIMPL.
        intptr_t oleCache = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleCache, &oleCache) == kComHrOk &&
               oleCache != 0,
               "59: QI(IID_IOleCache) hands the OLE cache view");
        void* oleCacheRaw = reinterpret_cast<void*>(oleCache);
        typedef int32_t (TEST_COM_CALL* InitCacheFn)(void*, void*);
        InitCacheFn ocInit = reinterpret_cast<InitCacheFn>(ComVtable(oleCache)[6]);
        Expect(ocInit(oleCacheRaw, nullptr) == kComHrNotImpl,
               "59: InitCache is E_NOTIMPL");
        ReleaseFn ocRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleCache)[2]);
        ocRelease(oleCacheRaw);

        // ===== Section 60: IOleCache2 (stage 55) =====
        // QI for IID_IOleCache2 hands the 10-slot OLE cache extension view:
        // DiscardCache E_NOTIMPL.
        intptr_t oleCache2 = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleCache2, &oleCache2) == kComHrOk &&
               oleCache2 != 0,
               "60: QI(IID_IOleCache2) hands the OLE cache extension view");
        void* oleCache2Raw = reinterpret_cast<void*>(oleCache2);
        VoidFn oc2Discard = reinterpret_cast<VoidFn>(ComVtable(oleCache2)[9]);
        Expect(oc2Discard(oleCache2Raw) == kComHrNotImpl,
               "60: DiscardCache is E_NOTIMPL");
        ReleaseFn oc2Release = reinterpret_cast<ReleaseFn>(ComVtable(oleCache2)[2]);
        oc2Release(oleCache2Raw);

        // ===== Section 61: IOleCacheControl (stage 56) =====
        // QI for IID_IOleCacheControl hands the 5-slot cache-control view:
        // OnStop E_NOTIMPL.
        intptr_t oleCacheCtl = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleCacheControl, &oleCacheCtl) == kComHrOk &&
               oleCacheCtl != 0,
               "61: QI(IID_IOleCacheControl) hands the cache-control view");
        void* oleCacheCtlRaw = reinterpret_cast<void*>(oleCacheCtl);
        VoidFn occOnStop = reinterpret_cast<VoidFn>(ComVtable(oleCacheCtl)[4]);
        Expect(occOnStop(oleCacheCtlRaw) == kComHrNotImpl,
               "61: OnStop is E_NOTIMPL");
        ReleaseFn occRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleCacheCtl)[2]);
        occRelease(oleCacheCtlRaw);

        // ===== Section 62: IOleItemContainer (stage 57) =====
        // QI for IID_IOleItemContainer hands the 8-slot item-container view:
        // LockContainer E_NOTIMPL.
        intptr_t oleItem = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleItemContainer, &oleItem) == kComHrOk &&
               oleItem != 0,
               "62: QI(IID_IOleItemContainer) hands the item-container view");
        void* oleItemRaw = reinterpret_cast<void*>(oleItem);
        typedef int32_t (TEST_COM_CALL* LockContainerFn)(void*, int32_t);
        LockContainerFn oiLock = reinterpret_cast<LockContainerFn>(ComVtable(oleItem)[7]);
        Expect(oiLock(oleItemRaw, 1) == kComHrNotImpl,
               "62: LockContainer is E_NOTIMPL");
        ReleaseFn oiRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleItem)[2]);
        oiRelease(oleItemRaw);

        // ===== Section 63: IOleUILinkContainer (stage 58) =====
        // QI for IID_IOleUILinkContainer hands the 9-slot UI link-container
        // view: CancelLink E_NOTIMPL.
        intptr_t oleUiLink = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUILinkContainer, &oleUiLink) == kComHrOk &&
               oleUiLink != 0,
               "63: QI(IID_IOleUILinkContainer) hands the UI link-container view");
        void* oleUiLinkRaw = reinterpret_cast<void*>(oleUiLink);
        VoidFn oulCancel = reinterpret_cast<VoidFn>(ComVtable(oleUiLink)[8]);
        Expect(oulCancel(oleUiLinkRaw) == kComHrNotImpl,
               "63: CancelLink is E_NOTIMPL");
        ReleaseFn oulRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiLink)[2]);
        oulRelease(oleUiLinkRaw);

        // ===== Section 64: IOleUILinkInfo (stage 59) =====
        // QI for IID_IOleUILinkInfo hands the 10-slot UI link-info view:
        // GetLastUpdate E_NOTIMPL.
        intptr_t oleUiInfo = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUILinkInfo, &oleUiInfo) == kComHrOk &&
               oleUiInfo != 0,
               "64: QI(IID_IOleUILinkInfo) hands the UI link-info view");
        void* oleUiInfoRaw = reinterpret_cast<void*>(oleUiInfo);
        typedef int32_t (TEST_COM_CALL* GetLastUpdateFn)(void*, uint32_t, void*);
        GetLastUpdateFn ouiLast = reinterpret_cast<GetLastUpdateFn>(ComVtable(oleUiInfo)[9]);
        uint8_t ouiFt[8] = { 0 };
        Expect(ouiLast(oleUiInfoRaw, 0, ouiFt) == kComHrNotImpl,
               "64: GetLastUpdate is E_NOTIMPL");
        ReleaseFn ouiRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiInfo)[2]);
        ouiRelease(oleUiInfoRaw);

        // ===== Section 65: IOleUIObjInfo (stage 60) =====
        // QI for IID_IOleUIObjInfo hands the 7-slot UI object-info view:
        // GetTypeName E_NOTIMPL.
        intptr_t oleUiObj = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUIObjInfo, &oleUiObj) == kComHrOk &&
               oleUiObj != 0,
               "65: QI(IID_IOleUIObjInfo) hands the UI object-info view");
        void* oleUiObjRaw = reinterpret_cast<void*>(oleUiObj);
        typedef int32_t (TEST_COM_CALL* GetTypeNameFn)(void*, uint32_t, void**);
        GetTypeNameFn ouoType = reinterpret_cast<GetTypeNameFn>(ComVtable(oleUiObj)[5]);
        void* ouoTypeOut = reinterpret_cast<void*>(0x1);
        Expect(ouoType(oleUiObjRaw, 0, &ouoTypeOut) == kComHrNotImpl,
               "65: GetTypeName is E_NOTIMPL");
        ReleaseFn ouoRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiObj)[2]);
        ouoRelease(oleUiObjRaw);

        // ===== Section 66: IOleUIVerify (stage 61) =====
        // QI for IID_IOleUIVerify hands the 4-slot UI verify view:
        // Verify E_NOTIMPL.
        intptr_t oleUiVerify = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUIVerify, &oleUiVerify) == kComHrOk &&
               oleUiVerify != 0,
               "66: QI(IID_IOleUIVerify) hands the UI verify view");
        void* oleUiVerifyRaw = reinterpret_cast<void*>(oleUiVerify);
        typedef int32_t (TEST_COM_CALL* VerifyFn)(void*, void*);
        VerifyFn ouvVerify = reinterpret_cast<VerifyFn>(ComVtable(oleUiVerify)[3]);
        Expect(ouvVerify(oleUiVerifyRaw, nullptr) == kComHrNotImpl,
               "66: Verify is E_NOTIMPL");
        ReleaseFn ouvRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiVerify)[2]);
        ouvRelease(oleUiVerifyRaw);

        // ===== Section 67: IOleUILinkContainerA (stage 62) =====
        // QI for IID_IOleUILinkContainerA hands the 9-slot ANSI UI
        // link-container view: CancelLink E_NOTIMPL.
        intptr_t oleUiLinkA = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUILinkContainerA, &oleUiLinkA) == kComHrOk &&
               oleUiLinkA != 0,
               "67: QI(IID_IOleUILinkContainerA) hands the ANSI UI link-container view");
        void* oleUiLinkARaw = reinterpret_cast<void*>(oleUiLinkA);
        VoidFn oulaCancel = reinterpret_cast<VoidFn>(ComVtable(oleUiLinkA)[8]);
        Expect(oulaCancel(oleUiLinkARaw) == kComHrNotImpl,
               "67: CancelLink is E_NOTIMPL");
        ReleaseFn oulaRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiLinkA)[2]);
        oulaRelease(oleUiLinkARaw);

        // ===== Section 68: IOleUILinkInfoA (stage 63) =====
        // QI for IID_IOleUILinkInfoA hands the 10-slot ANSI UI link-info
        // view: GetLastUpdate E_NOTIMPL.
        intptr_t oleUiInfoA = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUILinkInfoA, &oleUiInfoA) == kComHrOk &&
               oleUiInfoA != 0,
               "68: QI(IID_IOleUILinkInfoA) hands the ANSI UI link-info view");
        void* oleUiInfoARaw = reinterpret_cast<void*>(oleUiInfoA);
        GetLastUpdateFn ouiaLast = reinterpret_cast<GetLastUpdateFn>(ComVtable(oleUiInfoA)[9]);
        uint8_t ouiaFt[8] = { 0 };
        Expect(ouiaLast(oleUiInfoARaw, 0, ouiaFt) == kComHrNotImpl,
               "68: GetLastUpdate is E_NOTIMPL");
        ReleaseFn ouiaRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiInfoA)[2]);
        ouiaRelease(oleUiInfoARaw);

        // ===== Section 69: IOleUIObjInfoA (stage 64) =====
        // QI for IID_IOleUIObjInfoA hands the 7-slot ANSI UI object-info
        // view: GetTypeName E_NOTIMPL.
        intptr_t oleUiObjA = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUIObjInfoA, &oleUiObjA) == kComHrOk &&
               oleUiObjA != 0,
               "69: QI(IID_IOleUIObjInfoA) hands the ANSI UI object-info view");
        void* oleUiObjARaw = reinterpret_cast<void*>(oleUiObjA);
        GetTypeNameFn ouoaType = reinterpret_cast<GetTypeNameFn>(ComVtable(oleUiObjA)[5]);
        void* ouoaTypeOut = reinterpret_cast<void*>(0x1);
        Expect(ouoaType(oleUiObjARaw, 0, &ouoaTypeOut) == kComHrNotImpl,
               "69: GetTypeName is E_NOTIMPL");
        ReleaseFn ouoaRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiObjA)[2]);
        ouoaRelease(oleUiObjARaw);

        // ===== Section 70: IOleUIVerifyA (stage 65) =====
        // QI for IID_IOleUIVerifyA hands the 4-slot ANSI UI verify view:
        // Verify E_NOTIMPL.
        intptr_t oleUiVerifyA = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUIVerifyA, &oleUiVerifyA) == kComHrOk &&
               oleUiVerifyA != 0,
               "70: QI(IID_IOleUIVerifyA) hands the ANSI UI verify view");
        void* oleUiVerifyARaw = reinterpret_cast<void*>(oleUiVerifyA);
        VerifyFn ouvaVerify = reinterpret_cast<VerifyFn>(ComVtable(oleUiVerifyA)[3]);
        Expect(ouvaVerify(oleUiVerifyARaw, nullptr) == kComHrNotImpl,
               "70: Verify is E_NOTIMPL");
        ReleaseFn ouvaRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiVerifyA)[2]);
        ouvaRelease(oleUiVerifyARaw);

        // ===== Section 71: IOleUILinkContainerW (stage 66) =====
        // QI for IID_IOleUILinkContainerW hands the 9-slot Unicode UI
        // link-container view: CancelLink E_NOTIMPL.
        intptr_t oleUiLinkW = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUILinkContainerW, &oleUiLinkW) == kComHrOk &&
               oleUiLinkW != 0,
               "71: QI(IID_IOleUILinkContainerW) hands the Unicode UI link-container view");
        void* oleUiLinkWRaw = reinterpret_cast<void*>(oleUiLinkW);
        VoidFn oulwCancel = reinterpret_cast<VoidFn>(ComVtable(oleUiLinkW)[8]);
        Expect(oulwCancel(oleUiLinkWRaw) == kComHrNotImpl,
               "71: CancelLink is E_NOTIMPL");
        ReleaseFn oulwRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiLinkW)[2]);
        oulwRelease(oleUiLinkWRaw);

        // ===== Section 72: IOleUILinkInfoW (stage 67) =====
        // QI for IID_IOleUILinkInfoW hands the 10-slot Unicode UI link-info
        // view: GetLastUpdate E_NOTIMPL.
        intptr_t oleUiInfoW = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUILinkInfoW, &oleUiInfoW) == kComHrOk &&
               oleUiInfoW != 0,
               "72: QI(IID_IOleUILinkInfoW) hands the Unicode UI link-info view");
        void* oleUiInfoWRaw = reinterpret_cast<void*>(oleUiInfoW);
        GetLastUpdateFn ouiwLast = reinterpret_cast<GetLastUpdateFn>(ComVtable(oleUiInfoW)[9]);
        uint8_t ouiwFt[8] = { 0 };
        Expect(ouiwLast(oleUiInfoWRaw, 0, ouiwFt) == kComHrNotImpl,
               "72: GetLastUpdate is E_NOTIMPL");
        ReleaseFn ouiwRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiInfoW)[2]);
        ouiwRelease(oleUiInfoWRaw);

        // ===== Section 73: IOleUIObjInfoW (stage 68) =====
        // QI for IID_IOleUIObjInfoW hands the 7-slot Unicode UI object-info
        // view: GetTypeName E_NOTIMPL.
        intptr_t oleUiObjW = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUIObjInfoW, &oleUiObjW) == kComHrOk &&
               oleUiObjW != 0,
               "73: QI(IID_IOleUIObjInfoW) hands the Unicode UI object-info view");
        void* oleUiObjWRaw = reinterpret_cast<void*>(oleUiObjW);
        GetTypeNameFn ouowType = reinterpret_cast<GetTypeNameFn>(ComVtable(oleUiObjW)[5]);
        void* ouowTypeOut = reinterpret_cast<void*>(0x1);
        Expect(ouowType(oleUiObjWRaw, 0, &ouowTypeOut) == kComHrNotImpl,
               "73: GetTypeName is E_NOTIMPL");
        ReleaseFn ouowRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiObjW)[2]);
        ouowRelease(oleUiObjWRaw);

        // ===== Section 74: IOleUIVerifyW (stage 69) =====
        // QI for IID_IOleUIVerifyW hands the 4-slot Unicode UI verify view:
        // Verify E_NOTIMPL.
        intptr_t oleUiVerifyW = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIOleUIVerifyW, &oleUiVerifyW) == kComHrOk &&
               oleUiVerifyW != 0,
               "74: QI(IID_IOleUIVerifyW) hands the Unicode UI verify view");
        void* oleUiVerifyWRaw = reinterpret_cast<void*>(oleUiVerifyW);
        VerifyFn ouvwVerify = reinterpret_cast<VerifyFn>(ComVtable(oleUiVerifyW)[3]);
        Expect(ouvwVerify(oleUiVerifyWRaw, nullptr) == kComHrNotImpl,
               "74: Verify is E_NOTIMPL");
        ReleaseFn ouvwRelease = reinterpret_cast<ReleaseFn>(ComVtable(oleUiVerifyW)[2]);
        ouvwRelease(oleUiVerifyWRaw);

        // ===== Section 75: IEnumConnectionPoints (stage 70) =====
        // QI for IID_IEnumConnectionPoints hands the 7-slot connection-point
        // enumerator view: Next / Reset all E_NOTIMPL.
        intptr_t enumCp = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIEnumConnectionPoints, &enumCp) == kComHrOk &&
               enumCp != 0,
               "75: QI(IID_IEnumConnectionPoints) hands the connection-point enumerator view");
        void* enumCpRaw = reinterpret_cast<void*>(enumCp);
        typedef int32_t (TEST_COM_CALL* CpNextFn)(void*, uint32_t, void*, uint32_t*);
        CpNextFn cpNext = reinterpret_cast<CpNextFn>(ComVtable(enumCp)[3]);
        VoidFn cpReset = reinterpret_cast<VoidFn>(ComVtable(enumCp)[5]);
        uint8_t cpOut[32] = { 0 };
        Expect(cpNext(enumCpRaw, 1, cpOut, nullptr) == kComHrNotImpl,
               "75: Next is E_NOTIMPL");
        Expect(cpReset(enumCpRaw) == kComHrNotImpl,
               "75: Reset is E_NOTIMPL");
        ReleaseFn cpRelease = reinterpret_cast<ReleaseFn>(ComVtable(enumCp)[2]);
        cpRelease(enumCpRaw);

        // ===== Section 76: IEnumSTATSTG (stage 71) =====
        // QI for IID_IEnumSTATSTG hands the 7-slot storage-element enumerator
        // view: Next / Reset all E_NOTIMPL.
        intptr_t enumStg = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIEnumSTATSTG, &enumStg) == kComHrOk &&
               enumStg != 0,
               "76: QI(IID_IEnumSTATSTG) hands the storage-element enumerator view");
        void* enumStgRaw = reinterpret_cast<void*>(enumStg);
        typedef int32_t (TEST_COM_CALL* StgNextFn)(void*, uint32_t, void*, uint32_t*);
        StgNextFn stgNext = reinterpret_cast<StgNextFn>(ComVtable(enumStg)[3]);
        VoidFn stgReset = reinterpret_cast<VoidFn>(ComVtable(enumStg)[5]);
        uint8_t stgOut[32] = { 0 };
        Expect(stgNext(enumStgRaw, 1, stgOut, nullptr) == kComHrNotImpl,
               "76: Next is E_NOTIMPL");
        Expect(stgReset(enumStgRaw) == kComHrNotImpl,
               "76: Reset is E_NOTIMPL");
        ReleaseFn stgRelease = reinterpret_cast<ReleaseFn>(ComVtable(enumStg)[2]);
        stgRelease(enumStgRaw);

        // ===== Section 77: IAdviseSink2 (stage 72) =====
        // QI for IID_IAdviseSink2 hands the 8-slot advise-sink view with
        // link-source change: OnLinkSrcChange S_OK (accepted and dropped).
        intptr_t advSink2 = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIAdviseSink2, &advSink2) == kComHrOk &&
               advSink2 != 0,
               "77: QI(IID_IAdviseSink2) hands the advise-sink2 view");
        void* advSink2Raw = reinterpret_cast<void*>(advSink2);
        typedef int32_t (TEST_COM_CALL* LinkSrcChangeFn)(void*, void*);
        LinkSrcChangeFn linkSrc = reinterpret_cast<LinkSrcChangeFn>(ComVtable(advSink2)[7]);
        Expect(linkSrc(advSink2Raw, nullptr) == kComHrOk,
               "77: OnLinkSrcChange is S_OK");
        ReleaseFn advSink2Release = reinterpret_cast<ReleaseFn>(ComVtable(advSink2)[2]);
        advSink2Release(advSink2Raw);

        // ===== Section 78: IAdviseSinkEx (stage 73) =====
        // QI for IID_IAdviseSinkEx hands the 9-slot advise-sink view with
        // view-status change: OnViewStatusChange S_OK (accepted and dropped).
        intptr_t advSinkEx = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIAdviseSinkEx, &advSinkEx) == kComHrOk &&
               advSinkEx != 0,
               "78: QI(IID_IAdviseSinkEx) hands the advise-sink-ex view");
        void* advSinkExRaw = reinterpret_cast<void*>(advSinkEx);
        typedef int32_t (TEST_COM_CALL* ViewStatusChangeFn)(void*, uint32_t, int32_t);
        ViewStatusChangeFn viewStatus = reinterpret_cast<ViewStatusChangeFn>(ComVtable(advSinkEx)[7]);
        Expect(viewStatus(advSinkExRaw, 0, 0) == kComHrOk,
               "78: OnViewStatusChange is S_OK");
        ReleaseFn advSinkExRelease = reinterpret_cast<ReleaseFn>(ComVtable(advSinkEx)[2]);
        advSinkExRelease(advSinkExRaw);

        // ===== Section 79: IStream (stage 74) =====
        // QI for IID_IStream hands the 14-slot byte-stream view: Read / Revert
        // all E_NOTIMPL (the stream is empty headless).
        intptr_t stream = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIStream, &stream) == kComHrOk &&
               stream != 0,
               "79: QI(IID_IStream) hands the byte-stream view");
        void* streamRaw = reinterpret_cast<void*>(stream);
        typedef int32_t (TEST_COM_CALL* StreamReadFn)(void*, void*, uint32_t, uint32_t*);
        StreamReadFn streamRead = reinterpret_cast<StreamReadFn>(ComVtable(stream)[3]);
        VoidFn streamRevert = reinterpret_cast<VoidFn>(ComVtable(stream)[9]);
        uint8_t streamBuf[16] = { 0 };
        Expect(streamRead(streamRaw, streamBuf, 16, nullptr) == kComHrNotImpl,
               "79: Read is E_NOTIMPL");
        Expect(streamRevert(streamRaw) == kComHrNotImpl,
               "79: Revert is E_NOTIMPL");
        ReleaseFn streamRelease = reinterpret_cast<ReleaseFn>(ComVtable(stream)[2]);
        streamRelease(streamRaw);

        // ===== Section 80: IStorage (stage 75) =====
        // QI for IID_IStorage hands the 18-slot compound-file storage view:
        // CreateStream / Revert all E_NOTIMPL (the storage is empty headless).
        intptr_t storage = 0;
        Expect(icall_com_query_interface(autoCcw, kComIidIStorage, &storage) == kComHrOk &&
               storage != 0,
               "80: QI(IID_IStorage) hands the compound-file storage view");
        void* storageRaw = reinterpret_cast<void*>(storage);
        typedef int32_t (TEST_COM_CALL* CreateStreamFn)(void*, const void*, uint32_t, uint32_t, uint32_t, void**);
        CreateStreamFn createStream = reinterpret_cast<CreateStreamFn>(ComVtable(storage)[3]);
        VoidFn storageRevert = reinterpret_cast<VoidFn>(ComVtable(storage)[10]);
        void* streamOut = nullptr;
        Expect(createStream(storageRaw, nullptr, 0, 0, 0, &streamOut) == kComHrNotImpl,
               "80: CreateStream is E_NOTIMPL");
        Expect(storageRevert(storageRaw) == kComHrNotImpl,
               "80: Revert is E_NOTIMPL");
        ReleaseFn storageRelease = reinterpret_cast<ReleaseFn>(ComVtable(storage)[2]);
        storageRelease(storageRaw);

        // ===== Section 81: IDataObject DAdvise/DUnadvise (stage 76) =====
        // A fresh WfDataObject CCW with the IDataObject view registered;
        // DAdvise registers native IAdviseSinks with STABLE cookies, the
        // notify trigger fires OnDataChange on every registered sink,
        // DUnadvise removes by cookie (compacting without renumbering), and
        // a notify after removal reaches nobody.
        il2cpp::Il2CppObject* advObj = il2cpp::il2cpp_object_new(&WfDataObject_class);
        WfDataObject__ctor_9a53ee649df188aa(advObj);
        const intptr_t advCcw = icall_com_ccw_create(advObj);
        Expect(advCcw != 0 &&
               icall_com_ccw_register_interface_view(
                   advCcw, kComIidIDataObject, reinterpret_cast<intptr_t>(&IDataObject_class)) == kComHrOk,
               "81: ccw_create + register_interface_view declare the IDataObject view");
        intptr_t advView = 0;
        Expect(icall_com_query_interface(advCcw, kComIidIDataObject, &advView) == kComHrOk &&
               advView != 0,
               "81: QI(IID_IDataObject) hands the data-object view");
        void* advViewRaw = reinterpret_cast<void*>(advView);
        typedef int32_t (TEST_COM_CALL* DataAdviseFn)(void*, intptr_t, intptr_t, intptr_t, intptr_t);
        typedef int32_t (TEST_COM_CALL* DataUnadviseFn)(void*, intptr_t, intptr_t, intptr_t, intptr_t);
        DataAdviseFn dAdvise = reinterpret_cast<DataAdviseFn>(ComVtable(advView)[9]);
        DataUnadviseFn dUnadvise = reinterpret_cast<DataUnadviseFn>(ComVtable(advView)[10]);
        TestDataAdviseSink advSinkA = { kTestDataAdviseSinkVtbl, 0 };
        TestDataAdviseSink advSinkB = { kTestDataAdviseSinkVtbl, 0 };
        uint32_t advCookieA = 0;
        uint32_t advCookieB = 0;
        Expect(dAdvise(advViewRaw, 0, 0, reinterpret_cast<intptr_t>(&advSinkA),
                       reinterpret_cast<intptr_t>(&advCookieA)) == kComHrOk &&
               advCookieA == 1,
               "81: DAdvise registers sink A and returns cookie 1");
        Expect(dAdvise(advViewRaw, 0, 0, 0, reinterpret_cast<intptr_t>(&advCookieB)) == kComHrInvalidArg,
               "81: DAdvise with a null sink is E_INVALIDARG");
        Expect(dAdvise(advViewRaw, 0, 0, reinterpret_cast<intptr_t>(&advSinkB),
                       reinterpret_cast<intptr_t>(&advCookieB)) == kComHrOk &&
               advCookieB == 2,
               "81: DAdvise registers sink B and returns cookie 2");
        Expect(icall_com_data_object_notify(advCcw, 0, 0) == kComHrOk &&
               advSinkA.onDataChangeCalls == 1 && advSinkB.onDataChangeCalls == 1,
               "81: notify fires OnDataChange on both registered sinks");
        Expect(dUnadvise(advViewRaw, advCookieA, 0, 0, 0) == kComHrOk,
               "81: DUnadvise removes sink A by cookie");
        Expect(icall_com_data_object_notify(advCcw, 0, 0) == kComHrOk &&
               advSinkA.onDataChangeCalls == 1 && advSinkB.onDataChangeCalls == 2,
               "81: notify after DUnadvise(A) reaches only B (A's count unchanged)");
        Expect(dUnadvise(advViewRaw, advCookieB, 0, 0, 0) == kComHrOk,
               "81: DUnadvise removes sink B by its stable cookie");
        Expect(icall_com_data_object_notify(advCcw, 0, 0) == kComHrOk &&
               advSinkA.onDataChangeCalls == 1 && advSinkB.onDataChangeCalls == 2,
               "81: notify after DUnadvise(B) reaches nobody (counts unchanged)");
#if defined(_WIN32)
        // 81d: EnumDAdvise (slot 11) hands the IEnumSTATDATA view (the
        // registered data-advise enumeration -- Windows-only view).
        typedef int32_t (TEST_COM_CALL* DataEnumDAdviseFn)(void*, intptr_t, intptr_t, intptr_t, intptr_t);
        DataEnumDAdviseFn dEnumAdvise = reinterpret_cast<DataEnumDAdviseFn>(ComVtable(advView)[11]);
        intptr_t enumDAdviseOut = 0;
        Expect(dEnumAdvise(advViewRaw, reinterpret_cast<intptr_t>(&enumDAdviseOut), 0, 0, 0) == kComHrOk &&
               enumDAdviseOut != 0 && enumDAdviseOut != advView,
               "81: EnumDAdvise hands the IEnumSTATDATA view (distinct address)");
#endif
        ReleaseFn advViewRelease = reinterpret_cast<ReleaseFn>(ComVtable(advView)[2]);
        advViewRelease(advViewRaw);
        Expect(icall_com_release(advCcw) == 0,
               "81: releasing the data-advise CCW balances to 0");

        Expect(icall_com_release(autoCcw) == 0,
               "24: releasing the IAutoScale CCW balances to 0");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d com assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime com tests passed\n");
    return 0;
}

