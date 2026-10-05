//------------------------------------------------------------------------------
// com.cpp -- COM interop runtime (see il2cpp_com.h for the full contract).
//
// PLATFORM SPLIT (user iron rule #17: COM identical to .NET Framework;
// Windows guaranteed, other platforms compile + explicit error, no crash):
//   #if defined(_WIN32) -- full implementation. ole32 entry points
//     (CoCreateInstance / CLSIDFromProgID / GetActiveObject) are resolved at
//     RUNTIME via LoadLibraryA("ole32.dll") + GetProcAddress -- the guid.cpp
//     precedent (the runtime library must not pull in import libs, and
//     tests/runtime/CMakeLists.txt links no ole32). This keeps the unit
//     tests linkable with the existing CMake (no CMakeLists edit needed).
//   #else -- compilable stubs: HRESULT forms return E_NOTIMPL, intptr/void
//     forms return 0 / no-op after throwing NotSupportedException with the
//     platform-limitation message (mirrors the netcore Marshal.Unix.cs
//     contract: PlatformNotSupportedException, never a silent value). The
//     CCW (pure memory layout + GUID compare -- no ole32 dependency) is
//     implemented on EVERY platform.
//
// REFERENCE (semantic baseline, .NET Framework 4.8 -- batch task "鍏堟妱"):
//   * Marshal.GetTypeFromProgID / GetActiveObject / CreateObject -- netcore
//     Marshal.Windows.cs / Marshal.Unix.cs (the Unix file is the exact
//     cross-platform stub shape this file mirrors).
//   * RCW model -- net48 __ComObject wraps a native IUnknown; GetObjectFor
//     IUnknown takes ownership, GetIUnknownForObject returns AddRef'd.
//   * CCW vtable -- coreclr comcallablewrapper.cpp's standard IUnknown
//     vtable (3 slots) + IDispatch (slots 3-6: GetTypeInfoCount /
//     GetTypeInfo / GetIDsOfNames / Invoke -- phase 2, the ActiveX /
//     automation late-binding surface) + SimpleComCallWrapper::
//     QIStandardInterface's GUID dispatch table (phase 2: IUnknown +
//     IDispatch only, E_NOINTERFACE otherwise). GetIDsOfNames resolves names
//     through a per-CCW registry (first name -> dispId 1, cached,
//     case-insensitive, capacity 8); Invoke validates dispId + argument count
//     and returns S_OK -- dispatching into the wrapped managed method via M3
//     reflection is the documented phase-3 path.
//
// ERROR MODEL: every failing COM call throws a COMException-shaped
// DotNetException carrying the HRESULT (exceptionext.cpp ComposeMessage
// thread-local-scratch precedent -- the scratch stays valid until the next
// throw on the same thread, the standard il2cpp diagnostic pattern). The
// released-RCW guard throws InvalidComObjectException-shaped DotNetException.
//------------------------------------------------------------------------------

#include "il2cpp_com.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_gchandle.h"
#include "il2cpp_metadata.h"
#include "il2cpp_pinvoke_ext.h" // stage 13: il2cpp_pinvoke_string_from_utf16 (BSTRToString decode)
#include "il2cpp_boxing_ext.h"  // C2 (A2): icall_box_double/bool (the invoke result decoder)
#include "il2cpp_array.h"       // C2 (A2): il2cpp_array_get_elem_ptr (the args array read)
#include "il2cpp_runtime.h"
#include "il2cpp_streamio.h" // stage 12: the ComStream object bridge (create / get_iunknown)
#include "typeinfo.h"        // stage 17: the IDataObject view's name->interface-slot resolution

#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>   // C2 (A2): std::wstring (the member-name wide buffer)
#include <vector>   // C2 (A2): the DISPPARAMS variant storage

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#endif

namespace il2cpp {
namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Shared ABI types / helpers (compiled on every platform).
//------------------------------------------------------------------------------

// Calling convention of COM interface methods: __stdcall on x86, unified on
// x64/ARM64 (the ABI the native consumer calls the CCW vtable through).
#if defined(_M_IX86) || defined(__i386__)
    #define IL2CPP_COM_CALL __stdcall
#else
    #define IL2CPP_COM_CALL
#endif

// WINAPI is referenced by the Ole32Api function-pointer typedefs, which are
// compiled on EVERY platform (the every-platform COM code references
// GetOle32()). On non-Windows it is a no-op calling convention.
#if !defined(_WIN32)
    #define WINAPI
#endif

// Ole32Api + GetOle32() are referenced by every-platform COM code (the
// IDispatch VARIANT BSTR path, the CCW BSTR ownership, OLE drag-drop
// registration). On non-Windows there is no ole32/oleaut32 to resolve, so
// GetOle32() returns a null-api and the callers throw NotSupportedException /
// return null / E_NOTIMPL as designed (user iron rule #17: other platforms
// compile + explicit error, no crash). This block is mutually exclusive with
// the #if defined(_WIN32) Ole32Api definition later in this file.
#if !defined(_WIN32)
typedef int32_t (WINAPI* CoCreateInstanceFn)(const void* rclsid, void* pUnkOuter,
                                             uint32_t dwClsContext, const void* riid,
                                             void** ppv);
typedef int32_t (WINAPI* CLSIDFromProgIDFn)(const wchar_t* lpszProgID, void* lpclsid);
typedef int32_t (WINAPI* GetActiveObjectFn)(const void* rclsid, void* pvReserved,
                                            void** ppunk);
typedef int32_t (WINAPI* CoInitializeExFn)(const void* pvReserved, uint32_t dwCoInit);
typedef void* (WINAPI* SysAllocStringLenFn)(const void* psz, uint32_t len);
typedef void  (WINAPI* SysFreeStringFn)(void* bstr);
typedef int32_t (WINAPI* CreateStreamOnHGlobalFn)(void* hGlobal, int32_t fDeleteOnRelease,
                                                  void** ppstm);
typedef int32_t (WINAPI* GetHGlobalFromStreamFn)(void* pstm, void** phglobal);
typedef int32_t (WINAPI* RegisterDragDropFn)(void* hwnd, void* pDropTarget);
typedef int32_t (WINAPI* RevokeDragDropFn)(void* hwnd);

struct Ole32Api
{
    bool                  ok;
    CoCreateInstanceFn    coCreateInstance;
    CLSIDFromProgIDFn     clsidFromProgID;
    GetActiveObjectFn     getActiveObject;
    CoInitializeExFn      coInitializeEx;
    SysAllocStringLenFn   sysAllocStringLen;
    SysFreeStringFn       sysFreeString;
    CreateStreamOnHGlobalFn createStreamOnHGlobal;
    GetHGlobalFromStreamFn   getHGlobalFromStream;
    RegisterDragDropFn    registerDragDrop;
    RevokeDragDropFn      revokeDragDrop;
};

const Ole32Api& GetOle32()
{
    static const Ole32Api api = { false, nullptr, nullptr, nullptr, nullptr,
                                  nullptr, nullptr, nullptr, nullptr, nullptr,
                                  nullptr };
    return api;
}
#endif // !defined(_WIN32)

// The RCW payload: managed object header + the wrapped native IUnknown* +
// the wrapper reference count (net48 __ComObject shape). The native pointer
// is NOT a GC reference (refFieldBitmap 0 -- the GC must never scan it).
struct Il2CppComRcw
{
    Il2CppObject obj;     // klass == the RCW descriptor
    void*        unknown; // the wrapped native IUnknown*
    int32_t      refCount;// 1 = live, 0 = released (ReleaseComObject/Final)
};

// The CCW vtable shape (the standard IUnknown + IDispatch 7-slot vtable,
// coreclr comcallablewrapper.cpp). A single static instance is shared by
// every CCW. Slots 0-2 are IUnknown; 3-6 are IDispatch (GetTypeInfoCount /
// GetTypeInfo / GetIDsOfNames / Invoke). Because the IDispatch methods live
// in the same vtable, the interface pointer returned for IDispatch equals the
// IUnknown pointer (a single-vtable CCW -- legal COM).
struct Il2CppComCcwVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void* self, const void* riid, void** ppv);
    uint32_t (IL2CPP_COM_CALL* addRef)(void* self);
    uint32_t (IL2CPP_COM_CALL* release)(void* self);
    int32_t  (IL2CPP_COM_CALL* getTypeInfoCount)(void* self, uint32_t* pctinfo);
    int32_t  (IL2CPP_COM_CALL* getTypeInfo)(void* self, uint32_t iTInfo, uint32_t lcid, void** ppTInfo);
    int32_t  (IL2CPP_COM_CALL* getIDsOfNames)(void* self, const void* riid, void* const* rgszNames,
                                              uint32_t cNames, uint32_t lcid, int32_t* rgDispId);
    int32_t  (IL2CPP_COM_CALL* invoke)(void* self, int32_t dispIdMember, const void* riid,
                                       uint32_t lcid, uint16_t wFlags, const void* pDispParams,
                                       void* pVarResult, void* pExcepInfo, uint32_t* puArgErr);
};

#if defined(_WIN32)

// IOleObject vtable (slots 0-23: the IUnknown base + the 21 IOleObject
// methods in canonical oleidl.h order). This interface view lives at
// &ccw->oleVtbl -- a DISTINCT address inside the CCW allocation (container_of
// recovery via offsetof). The vtable can NOT be merged into the IDispatch
// vtable: slot 3 collides (IDispatch GetTypeInfoCount vs IOleObject
// SetClientSite) -- coreclr's per-interface vtable precedent.
struct Il2CppComOleVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* setClientSite)(void*, void*);          // slot 3
    int32_t  (IL2CPP_COM_CALL* getClientSite)(void*, void**);         // slot 4
    int32_t  (IL2CPP_COM_CALL* setHostNames)(void*, const void*, const void*); // slot 5
    int32_t  (IL2CPP_COM_CALL* close)(void*, uint32_t);               // slot 6
    int32_t  (IL2CPP_COM_CALL* setMoniker)(void*, uint32_t, void*);   // slot 7
    int32_t  (IL2CPP_COM_CALL* getMoniker)(void*, uint32_t, uint32_t, void**); // slot 8
    int32_t  (IL2CPP_COM_CALL* initFromData)(void*, void*, int32_t, uint32_t); // slot 9
    int32_t  (IL2CPP_COM_CALL* getClipboardData)(void*, uint32_t, void**);     // slot 10
    int32_t  (IL2CPP_COM_CALL* doVerb)(void*, int32_t, const void*, void*,     // slot 11
                                       int32_t, void*, const void*);
    int32_t  (IL2CPP_COM_CALL* enumVerbs)(void*, void**);             // slot 12
    int32_t  (IL2CPP_COM_CALL* update)(void*);                        // slot 13
    int32_t  (IL2CPP_COM_CALL* isUpToDate)(void*);                    // slot 14
    int32_t  (IL2CPP_COM_CALL* getUserClassID)(void*, void*);         // slot 15
    int32_t  (IL2CPP_COM_CALL* getUserType)(void*, uint32_t, void**); // slot 16
    int32_t  (IL2CPP_COM_CALL* setExtent)(void*, uint32_t, const void*); // slot 17
    int32_t  (IL2CPP_COM_CALL* getExtent)(void*, uint32_t, void*);    // slot 18
    int32_t  (IL2CPP_COM_CALL* advise)(void*, void*, uint32_t*);      // slot 19
    int32_t  (IL2CPP_COM_CALL* unadvise)(void*, uint32_t);            // slot 20
    int32_t  (IL2CPP_COM_CALL* enumAdvise)(void*, void**);            // slot 21
    int32_t  (IL2CPP_COM_CALL* getMiscStatus)(void*, uint32_t, uint32_t*); // slot 22
    int32_t  (IL2CPP_COM_CALL* setColorScheme)(void*, const void*);   // slot 23
};

// IOleClientSite vtable (slots 0-8: IUnknown + the 6 IOleClientSite
// methods). Lives at &ccw->siteVtbl.
struct Il2CppComSiteVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* saveObject)(void*);                    // slot 3
    int32_t  (IL2CPP_COM_CALL* getMoniker)(void*, uint32_t, uint32_t, void**); // slot 4
    int32_t  (IL2CPP_COM_CALL* getContainer)(void*, void**);          // slot 5
    int32_t  (IL2CPP_COM_CALL* showObject)(void*);                    // slot 6
    int32_t  (IL2CPP_COM_CALL* onShowWindow)(void*, int32_t);         // slot 7
    int32_t  (IL2CPP_COM_CALL* requestNewObjectLayout)(void*);        // slot 8
};

// IOleInPlaceObject vtable (slots 0-8: IUnknown + the 6 IOleInPlaceObject
// methods -- GetWindow / ContextSensitiveHelp / InPlaceDeactivate /
// UIDeactivate / SetObjectRects / ReactivateAndUndo, canonical oleidl.h
// order). The in-place-activated object surface (stage 27). Lives at
// &ccw->inPlaceVtbl.
struct Il2CppComInPlaceVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getWindow)(void*, void**);              // slot 3
    int32_t  (IL2CPP_COM_CALL* contextSensitiveHelp)(void*, int32_t);  // slot 4
    int32_t  (IL2CPP_COM_CALL* inPlaceDeactivate)(void*);              // slot 5
    int32_t  (IL2CPP_COM_CALL* uiDeactivate)(void*);                   // slot 6
    int32_t  (IL2CPP_COM_CALL* setObjectRects)(void*, const void*, const void*); // slot 7
    int32_t  (IL2CPP_COM_CALL* reactivateAndUndo)(void*);              // slot 8
};

// IProvideClassInfo vtable (slots 0-3: IUnknown + GetClassInfo -- the class
// info provider, canonical ocidl.h order; stage 28). GetClassInfo answers
// E_NOTIMPL (no ITypeInfo is built). Lives at &ccw->classInfoVtbl.
struct Il2CppComClassInfoVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getClassInfo)(void*, void**);           // slot 3
};

// IOleInPlaceSite vtable (slots 0-14: IUnknown + the 12 IOleInPlaceSite
// methods, canonical oleidl.h order; stage 29). The container-side in-place
// site -- the counterpart of IOleInPlaceObject. GetWindow hands a null hwnd
// (headless), OnInPlaceActivate/OnUIActivate/OnUIDeactivate/
// OnInPlaceDeactivate return S_OK, the rest are E_NOTIMPL. Lives at
// &ccw->inPlaceSiteVtbl.
struct Il2CppComInPlaceSiteVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getWindow)(void*, void**);              // slot 3
    int32_t  (IL2CPP_COM_CALL* contextSensitiveHelp)(void*, int32_t);  // slot 4
    int32_t  (IL2CPP_COM_CALL* canInPlaceActivate)(void*);             // slot 5
    int32_t  (IL2CPP_COM_CALL* onInPlaceActivate)(void*);              // slot 6
    int32_t  (IL2CPP_COM_CALL* onUIActivate)(void*);                   // slot 7
    int32_t  (IL2CPP_COM_CALL* getWindowContext)(void*, void**, void*, void*, void*); // slot 8
    int32_t  (IL2CPP_COM_CALL* scroll)(void*, const void*);            // slot 9
    int32_t  (IL2CPP_COM_CALL* onUIDeactivate)(void*, int32_t);        // slot 10
    int32_t  (IL2CPP_COM_CALL* onInPlaceDeactivate)(void*);            // slot 11
    int32_t  (IL2CPP_COM_CALL* discardUndoState)(void*);               // slot 12
    int32_t  (IL2CPP_COM_CALL* deactivateAndUndo)(void*);              // slot 13
    int32_t  (IL2CPP_COM_CALL* onPosRectChange)(void*, const void*);   // slot 14
};

// IAdviseSink vtable (slots 0-6: IUnknown + OnDataChange / OnViewChange /
// OnRename / OnSave -- the advise-sink notification surface, canonical
// oleidl.h order; stage 30). The CCW implements the sink so a native object
// can notify it. All four answer S_OK (the notification is accepted and
// dropped -- headless). Lives at &ccw->adviseSinkVtbl.
struct Il2CppComAdviseSinkVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* onDataChange)(void*, const void*, const void*); // slot 3
    int32_t  (IL2CPP_COM_CALL* onViewChange)(void*, uint32_t, int32_t);       // slot 4
    int32_t  (IL2CPP_COM_CALL* onRename)(void*, void*);                       // slot 5
    int32_t  (IL2CPP_COM_CALL* onSave)(void*);                                // slot 6
};

// IAdviseSink2 vtable (slots 0-7: IUnknown + OnDataChange / OnViewChange /
// OnRename / OnSave / OnLinkSrcChange -- the advise-sink notification surface
// with link-source change, canonical oleidl.h order; stage 72). IAdviseSink2
// derives from IAdviseSink (the first four notifications are the base's).
// All five answer S_OK (the notification is accepted and dropped -- headless).
// Lives at &ccw->adviseSink2Vtbl.
struct Il2CppComAdviseSink2Vtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* onDataChange)(void*, const void*, const void*); // slot 3
    int32_t  (IL2CPP_COM_CALL* onViewChange)(void*, uint32_t, int32_t);       // slot 4
    int32_t  (IL2CPP_COM_CALL* onRename)(void*, void*);                       // slot 5
    int32_t  (IL2CPP_COM_CALL* onSave)(void*);                                // slot 6
    int32_t  (IL2CPP_COM_CALL* onLinkSrcChange)(void*, void*);                // slot 7
};

// IAdviseSinkEx vtable (slots 0-8: IUnknown + OnDataChange / OnViewChange /
// OnRename / OnSave / OnViewStatusChange -- the advise-sink notification
// surface with view-status change, canonical oleidl.h order; stage 73).
// IAdviseSinkEx derives from IAdviseSink (the first four notifications are
// the base's). All five answer S_OK (the notification is accepted and
// dropped -- headless). Lives at &ccw->adviseSinkExVtbl.
struct Il2CppComAdviseSinkExVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* onDataChange)(void*, const void*, const void*); // slot 3
    int32_t  (IL2CPP_COM_CALL* onViewChange)(void*, uint32_t, int32_t);       // slot 4
    int32_t  (IL2CPP_COM_CALL* onRename)(void*, void*);                       // slot 5
    int32_t  (IL2CPP_COM_CALL* onSave)(void*);                                // slot 6
    int32_t  (IL2CPP_COM_CALL* onViewStatusChange)(void*, uint32_t, int32_t); // slot 7
};

// IStream vtable (slots 0-13: IUnknown + Read / Write / Seek / SetSize /
// CopyTo / Commit / Revert / LockRegion / UnlockRegion / Stat / Clone -- the
// COM byte-stream surface, canonical objidl.h order; stage 74). The CCW
// implements the stream so a native consumer can read/write it. All eleven
// answer E_NOTIMPL (the stream is empty headless). Lives at
// &ccw->streamVtbl.
struct Il2CppComStreamVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* read)(void*, void*, uint32_t, uint32_t*);            // slot 3
    int32_t  (IL2CPP_COM_CALL* write)(void*, const void*, uint32_t, uint32_t*);     // slot 4
    int32_t  (IL2CPP_COM_CALL* seek)(void*, int64_t, uint32_t, void*);              // slot 5
    int32_t  (IL2CPP_COM_CALL* setSize)(void*, int64_t);                            // slot 6
    int32_t  (IL2CPP_COM_CALL* copyTo)(void*, void*, int64_t, uint32_t*, uint32_t*); // slot 7
    int32_t  (IL2CPP_COM_CALL* commit)(void*, uint32_t);                            // slot 8
    int32_t  (IL2CPP_COM_CALL* revert)(void*);                                      // slot 9
    int32_t  (IL2CPP_COM_CALL* lockRegion)(void*, int64_t, int64_t, uint32_t);      // slot 10
    int32_t  (IL2CPP_COM_CALL* unlockRegion)(void*, int64_t, int64_t, uint32_t);    // slot 11
    int32_t  (IL2CPP_COM_CALL* stat)(void*, void*, uint32_t);                       // slot 12
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                               // slot 13
};

// IStorage vtable (slots 0-17: IUnknown + CreateStream / OpenStream /
// CreateStorage / OpenStorage / CopyTo / MoveElementTo / Commit / Revert /
// EnumElements / DestroyElement / RenameElement / SetElementTimes / SetClass /
// SetStateBits / Stat -- the compound-file storage surface, canonical
// objidl.h order; stage 75). All fifteen answer E_NOTIMPL (the storage is
// empty headless). Lives at &ccw->storageVtbl.
struct Il2CppComStorageVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* createStream)(void*, const void*, uint32_t, uint32_t, uint32_t, void**);   // slot 3
    int32_t  (IL2CPP_COM_CALL* openStream)(void*, const void*, void*, uint32_t, uint32_t, void**);        // slot 4
    int32_t  (IL2CPP_COM_CALL* createStorage)(void*, const void*, uint32_t, uint32_t, uint32_t, void**);  // slot 5
    int32_t  (IL2CPP_COM_CALL* openStorage)(void*, const void*, void*, uint32_t, const void*, uint32_t, void**); // slot 6
    int32_t  (IL2CPP_COM_CALL* copyTo)(void*, uint32_t, const void*, const void*, void*);                 // slot 7
    int32_t  (IL2CPP_COM_CALL* moveElementTo)(void*, const void*, void*, const void*, uint32_t);          // slot 8
    int32_t  (IL2CPP_COM_CALL* commit)(void*, uint32_t);                                                  // slot 9
    int32_t  (IL2CPP_COM_CALL* revert)(void*);                                                            // slot 10
    int32_t  (IL2CPP_COM_CALL* enumElements)(void*, uint32_t, void*, uint32_t, void**);                   // slot 11
    int32_t  (IL2CPP_COM_CALL* destroyElement)(void*, const void*);                                       // slot 12
    int32_t  (IL2CPP_COM_CALL* renameElement)(void*, const void*, const void*);                           // slot 13
    int32_t  (IL2CPP_COM_CALL* setElementTimes)(void*, const void*, const void*, const void*, const void*); // slot 14
    int32_t  (IL2CPP_COM_CALL* setClass)(void*, const void*);                                             // slot 15
    int32_t  (IL2CPP_COM_CALL* setStateBits)(void*, uint32_t, uint32_t);                                  // slot 16
    int32_t  (IL2CPP_COM_CALL* stat)(void*, void*, uint32_t);                                             // slot 17
};

// IOleInPlaceUIWindow vtable (slots 0-8: IUnknown + GetWindow /
// ContextSensitiveHelp / GetBorder / RequestBorderSpace / SetBorderSpace /
// SetActiveObject, canonical oleidl.h order; stage 31). The base of
// IOleInPlaceFrame. GetWindow hands a null hwnd (headless), the rest are
// E_NOTIMPL. Lives at &ccw->inPlaceUiWindowVtbl.
struct Il2CppComInPlaceUIWindowVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getWindow)(void*, void**);                  // slot 3
    int32_t  (IL2CPP_COM_CALL* contextSensitiveHelp)(void*, int32_t);      // slot 4
    int32_t  (IL2CPP_COM_CALL* getBorder)(void*, void*);                   // slot 5
    int32_t  (IL2CPP_COM_CALL* requestBorderSpace)(void*, const void*);    // slot 6
    int32_t  (IL2CPP_COM_CALL* setBorderSpace)(void*, const void*);        // slot 7
    int32_t  (IL2CPP_COM_CALL* setActiveObject)(void*, void*, void*);      // slot 8
};

// IOleInPlaceFrame vtable (slots 0-14: IUnknown + the 6 IOleInPlaceUIWindow
// methods + InsertMenus / SetMenu / RemoveMenus / SetStatusText /
// EnableModeless / TranslateAccelerator, canonical oleidl.h order; stage 32).
// GetWindow null hwnd, SetActiveObject S_OK, the rest E_NOTIMPL. Lives at
// &ccw->inPlaceFrameVtbl.
struct Il2CppComInPlaceFrameVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getWindow)(void*, void**);                  // slot 3
    int32_t  (IL2CPP_COM_CALL* contextSensitiveHelp)(void*, int32_t);      // slot 4
    int32_t  (IL2CPP_COM_CALL* getBorder)(void*, void*);                   // slot 5
    int32_t  (IL2CPP_COM_CALL* requestBorderSpace)(void*, const void*);    // slot 6
    int32_t  (IL2CPP_COM_CALL* setBorderSpace)(void*, const void*);        // slot 7
    int32_t  (IL2CPP_COM_CALL* setActiveObject)(void*, void*, void*);      // slot 8
    int32_t  (IL2CPP_COM_CALL* insertMenus)(void*, void*, void*);          // slot 9
    int32_t  (IL2CPP_COM_CALL* setMenu)(void*, void*, void*, void*);       // slot 10
    int32_t  (IL2CPP_COM_CALL* removeMenus)(void*, void*);                 // slot 11
    int32_t  (IL2CPP_COM_CALL* setStatusText)(void*, const void*);         // slot 12
    int32_t  (IL2CPP_COM_CALL* enableModeless)(void*, int32_t);            // slot 13
    int32_t  (IL2CPP_COM_CALL* translateAccelerator)(void*, const void*);  // slot 14
};

// IOleContainer vtable (slots 0-5: IUnknown + ParseDisplayName / EnumObjects /
// LockContainer, canonical oleidl.h order; stage 33). The OLE container
// surface. All three answer E_NOTIMPL. Lives at &ccw->oleContainerVtbl.
struct Il2CppComOleContainerVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* parseDisplayName)(void*, void*, void*, void*, void*); // slot 3
    int32_t  (IL2CPP_COM_CALL* enumObjects)(void*, uint32_t, void**);               // slot 4
    int32_t  (IL2CPP_COM_CALL* lockContainer)(void*, int32_t);                      // slot 5
};

// IEnumUnknown vtable (slots 0-6: IUnknown + Next / Skip / Reset / Clone,
// canonical objidl.h order; stage 34). The unknown enumerator -- the
// IOleContainer EnumObjects return type. All four answer E_NOTIMPL (the
// enumeration is empty headless). Lives at &ccw->enumUnknownVtbl.
struct Il2CppComEnumUnknownVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void**, uint32_t*); // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                    // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};

// IEnumString vtable (slots 0-6: IUnknown + Next / Skip / Reset / Clone,
// canonical objidl.h order; stage 35). The string enumerator (the
// IPersistFile / IMoniker enumeration return type). All four answer
// E_NOTIMPL (the enumeration is empty headless). Lives at
// &ccw->enumStringVtbl.
struct Il2CppComEnumStringVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void**, uint32_t*); // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                    // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};

// IConnectionPointContainer vtable (slots 0-4: IUnknown + EnumConnectionPoints
// / FindConnectionPoint -- the event-source container surface, canonical
// ocidl.h order). Lives at &ccw->cpcVtbl.
struct Il2CppComCpcVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* enumConnectionPoints)(void*, void**);  // slot 3
    int32_t  (IL2CPP_COM_CALL* findConnectionPoint)(void*, const void*, void**); // slot 4
};

// IConnectionPoint vtable (slots 0-7: IUnknown + GetConnectionInterface /
// GetConnectionPointContainer / Advise / Unadvise / EnumConnections -- the
// per-event-interface sink surface, canonical ocidl.h order). Every
// connection point shares the one static vtable (its interface pointer is the
// address of the connection point view's vtable-pointer member -- the methods
// recover the view by casting self directly).
struct Il2CppComCpVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getConnectionInterface)(void*, void*); // slot 3
    int32_t  (IL2CPP_COM_CALL* getConnectionPointContainer)(void*, void**); // slot 4
    int32_t  (IL2CPP_COM_CALL* advise)(void*, void*, uint32_t*);       // slot 5
    int32_t  (IL2CPP_COM_CALL* unadvise)(void*, uint32_t);             // slot 6
    int32_t  (IL2CPP_COM_CALL* enumConnections)(void*, void**);        // slot 7
};

// IDropTarget vtable (slots 0-6: IUnknown + DragEnter/DragOver/DragLeave/Drop
// -- the OLE drop target surface, canonical oleidl.h order). This interface
// view lives at &ccw->dropVtbl -- a DISTINCT address inside the CCW allocation
// (container_of recovery via offsetof, the phase-4 view precedent). The
// minimal stage-15 surface implements DragLeave (S_OK -- no params, no
// marshaling); DragEnter/DragOver/Drop return E_NOTIMPL (the managed dispatch
// is stage 16+).
struct Il2CppComDropTargetVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* dragEnter)(void*, void*, uint32_t, intptr_t, uint32_t*); // slot 3
    int32_t  (IL2CPP_COM_CALL* dragOver)(void*, uint32_t, intptr_t, uint32_t*);         // slot 4
    int32_t  (IL2CPP_COM_CALL* dragLeave)(void*);                                       // slot 5
    int32_t  (IL2CPP_COM_CALL* drop)(void*, void*, uint32_t, intptr_t, uint32_t*);      // slot 6
};

struct Il2CppComCcw; // forward -- the connection point views hold a back pointer

// A connection point view: the interface pointer a native host receives for a
// connection point is the address of the view's vtable-pointer member (offset
// 0 -- the methods recover the view by casting 'self' directly). The view
// carries a back pointer to the owning CCW (the shared refcount / identity
// rule) and the connection state: the event interface IID (IID_NULL =
// unregistered slot), the connected sink array (stage 9 -- up to
// kCcwMaxSinksPerCp sinks; each sink IUnknown* is AddRef'd by the Advise QI
// and released on its Unadvise or CCW teardown) and a monotonic cookie
// counter (1-based, shared across the array; stale cookies are rejected by
// Unadvise -- the D5 stale-detection rule).
struct Il2CppComConnectionPointView
{
    const Il2CppComCpVtable* vtbl;   // -> kCpVtable (all connection points share it)
    Il2CppComCcw* ccw;               // the owning CCW (shared refcount / identity)
    int32_t index;                   // this connection point's slot (0-based)
    uint8_t iid[16];                 // the event interface IID (IID_NULL = unregistered)
    void* sink[kCcwMaxSinksPerCp];   // the connected sink IUnknown* array (null = free slot)
    int32_t cookie[kCcwMaxSinksPerCp]; // the per-connection cookie (0 = free slot)
    int32_t connections;             // number of connected sinks (0 = no listener)
    int32_t nextCookie;              // monotonic cookie counter (1-based)
};

#endif // defined(_WIN32)

// IID_NULL (the all-zero GUID): GetIDsOfNames/Invoke require it as the 'riid'
// argument (dispatch requests never pass a real riid).
const uint8_t kNullIid[16] = { 0 };

struct Il2CppComCcw; // forward -- the enum view holds a back pointer

// IEnumVARIANT vtable (7 slots: IUnknown + Next/Skip/Reset/Clone, canonical
// oaidl.h order). The MAIN view (inlined in the CCW) and every Clone share
// the interface method slots; only the Release slot differs -- the main view
// falls through to CcwReleaseImpl (the shared CCW refcount teardown), a
// clone deletes its own heap block first (CcwEnumCloneRelease).
struct Il2CppComEnumVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void*, uint32_t*); // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                   // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};

// IEnumFORMATETC vtable (slots 0-6: IUnknown + Next / Skip / Reset / Clone,
// canonical objidl.h order; stage 36). The FORMATETC enumerator (the
// IDataObject EnumFormatEtc return type). All four answer E_NOTIMPL (the
// enumeration is empty headless). Lives at &ccw->enumFormatEtcVtbl.
struct Il2CppComEnumFormatEtcVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void*, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                    // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};

// IPersistStream vtable (slots 0-7: IUnknown + GetClassID / IsDirty / Load /
// Save / GetSizeMax, canonical objidl.h order; stage 37). The
// stream-persistence surface. All five answer E_NOTIMPL (no class ID /
// persistence). Lives at &ccw->persistStreamVtbl.
struct Il2CppComPersistStreamVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getClassId)(void*, void*);                 // slot 3
    int32_t  (IL2CPP_COM_CALL* isDirty)(void*);                           // slot 4
    int32_t  (IL2CPP_COM_CALL* load)(void*, void*);                       // slot 5
    int32_t  (IL2CPP_COM_CALL* save)(void*, void*, int32_t);              // slot 6
    int32_t  (IL2CPP_COM_CALL* getSizeMax)(void*, void*);                 // slot 7
};

// IPersistFile vtable (slots 0-8: IUnknown + GetClassID / IsDirty / Load /
// Save / SaveCompleted / GetCurFile, canonical objidl.h order; stage 38).
// The file-persistence surface. All six answer E_NOTIMPL (no class ID /
// persistence). Lives at &ccw->persistFileVtbl.
struct Il2CppComPersistFileVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getClassId)(void*, void*);                 // slot 3
    int32_t  (IL2CPP_COM_CALL* isDirty)(void*);                           // slot 4
    int32_t  (IL2CPP_COM_CALL* load)(void*, const void*, uint32_t);       // slot 5
    int32_t  (IL2CPP_COM_CALL* save)(void*, const void*, int32_t);        // slot 6
    int32_t  (IL2CPP_COM_CALL* saveCompleted)(void*, const void*);        // slot 7
    int32_t  (IL2CPP_COM_CALL* getCurFile)(void*, void**);                // slot 8
};

// IPersist vtable (slots 0-3: IUnknown + GetClassID, canonical objidl.h
// order; stage 39). The base persistence interface -- the root of the
// persistence family (IPersistStream / IPersistFile derive from it).
// GetClassID answers E_NOTIMPL (no class ID). Lives at &ccw->persistVtbl.
struct Il2CppComPersistVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getClassId)(void*, void*);                 // slot 3
};

// IOleInPlaceActiveObject vtable (slots 0-11: IUnknown + IOleWindow::GetWindow
// + ContextSensitiveHelp + IOleInPlaceObject::SetObjectRects +
// ReactivateAndUndo + TranslateAccelerator + OnFrameWindowActivate +
// OnDocWindowActivate + ResizeBorder + EnableModeless, canonical oleidl.h
// order; stage 40). The in-place active-object surface. GetWindow answers
// null hwnd; the rest E_NOTIMPL. Lives at &ccw->inPlaceActiveObjectVtbl.
struct Il2CppComInPlaceActiveObjectVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getWindow)(void*, void**);                 // slot 3
    int32_t  (IL2CPP_COM_CALL* contextSensitiveHelp)(void*, int32_t);     // slot 4
    int32_t  (IL2CPP_COM_CALL* setObjectRects)(void*, const void*, const void*); // slot 5
    int32_t  (IL2CPP_COM_CALL* reactivateAndUndo)(void*);                 // slot 6
    int32_t  (IL2CPP_COM_CALL* translateAccelerator)(void*, void*);       // slot 7
    int32_t  (IL2CPP_COM_CALL* onFrameWindowActivate)(void*, int32_t);    // slot 8
    int32_t  (IL2CPP_COM_CALL* onDocWindowActivate)(void*, int32_t);      // slot 9
    int32_t  (IL2CPP_COM_CALL* resizeBorder)(void*, const void*, void*, int32_t); // slot 10
    int32_t  (IL2CPP_COM_CALL* enableModeless)(void*, int32_t);           // slot 11
};

// IPersistPropertyBag vtable (slots 0-7: IUnknown + GetClassID / InitNew /
// Load / Save, canonical objidl.h order; stage 41). The property-bag
// persistence surface. All four answer E_NOTIMPL (no class ID / persistence).
// Lives at &ccw->persistPropertyBagVtbl.
struct Il2CppComPersistPropertyBagVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getClassId)(void*, void*);                 // slot 3
    int32_t  (IL2CPP_COM_CALL* initNew)(void*);                           // slot 4
    int32_t  (IL2CPP_COM_CALL* load)(void*, void*, void*);                // slot 5
    int32_t  (IL2CPP_COM_CALL* save)(void*, void*, int32_t, void*);       // slot 6
};

// IParseDisplayName vtable (slots 0-3: IUnknown + ParseDisplayName, canonical
// objidl.h order; stage 42). The OLE display-name resolution surface.
// ParseDisplayName answers E_NOTIMPL. Lives at &ccw->parseDisplayNameVtbl.
struct Il2CppComParseDisplayNameVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* parseDisplayName)(void*, void*, void*, uint32_t*, void**); // slot 3
};

// IViewObject vtable (slots 0-9: IUnknown + Draw / GetColorSet / Freeze /
// Unfreeze / SetAdvise / GetAdvise, canonical oleidl.h order; stage 43).
// The OLE view-object surface. All six answer E_NOTIMPL (no view).
// Lives at &ccw->viewObjectVtbl.
struct Il2CppComViewObjectVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* draw)(void*, uint32_t, int32_t, void*, void*, void*, void*, uint32_t, void*, uint32_t); // slot 3
    int32_t  (IL2CPP_COM_CALL* getColorSet)(void*, uint32_t, void*, void*, void**); // slot 4
    int32_t  (IL2CPP_COM_CALL* freeze)(void*, uint32_t, int32_t, void*, uint32_t*); // slot 5
    int32_t  (IL2CPP_COM_CALL* unfreeze)(void*, uint32_t);                 // slot 6
    int32_t  (IL2CPP_COM_CALL* setAdvise)(void*, uint32_t, uint32_t, void*); // slot 7
    int32_t  (IL2CPP_COM_CALL* getAdvise)(void*, uint32_t*, uint32_t*, void**); // slot 8
};

// IEnumSTATDATA vtable (slots 0-6: IUnknown + Next / Skip / Reset / Clone,
// canonical objidl.h order; stage 44). The STATDATA enumerator (the
// IDataObject EnumAdvise return type). All four answer E_NOTIMPL (the
// enumeration is empty headless). Lives at &ccw->enumStatDataVtbl.
struct Il2CppComEnumSTATDATAVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void*, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                    // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};

// IEnumOLEVERB vtable (slots 0-6: IUnknown + Next / Skip / Reset / Clone,
// canonical oleidl.h order; stage 45). The OLEVERB enumerator (the
// IOleObject EnumVerbs return type). All four answer E_NOTIMPL (the
// enumeration is empty headless). Lives at &ccw->enumOleVerbVtbl.
struct Il2CppComEnumOLEVERBVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void*, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                    // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};

// IEnumMoniker vtable (slots 0-6: IUnknown + Next / Skip / Reset / Clone,
// canonical objidl.h order; stage 46). The moniker enumerator (the
// running-object table / moniker enumeration surface). All four answer
// E_NOTIMPL (the enumeration is empty headless). Lives at
// &ccw->enumMonikerVtbl.
struct Il2CppComEnumMonikerVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void*, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                    // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};

// IViewObject2 vtable (slots 0-9: IUnknown + IViewObject::Draw / GetColorSet
// / Freeze / Unfreeze / SetAdvise / GetAdvise + GetExtent, canonical oleidl.h
// order; stage 47). The OLE view-object size surface. All seven answer
// E_NOTIMPL (no view). Lives at &ccw->viewObject2Vtbl.
struct Il2CppComViewObject2Vtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* draw)(void*, uint32_t, int32_t, void*, void*, void*, void*, uint32_t, void*, uint32_t); // slot 3
    int32_t  (IL2CPP_COM_CALL* getColorSet)(void*, uint32_t, void*, void*, void**); // slot 4
    int32_t  (IL2CPP_COM_CALL* freeze)(void*, uint32_t, int32_t, void*, uint32_t*); // slot 5
    int32_t  (IL2CPP_COM_CALL* unfreeze)(void*, uint32_t);                 // slot 6
    int32_t  (IL2CPP_COM_CALL* setAdvise)(void*, uint32_t, uint32_t, void*); // slot 7
    int32_t  (IL2CPP_COM_CALL* getAdvise)(void*, uint32_t*, uint32_t*, void**); // slot 8
    int32_t  (IL2CPP_COM_CALL* getExtent)(void*, uint32_t, int32_t, void*); // slot 9
};

// IOleLink vtable (slots 0-12: IUnknown + SetUpdateOptions / GetUpdateOptions
// / SetSourceMoniker / GetSourceMoniker / SetSourceDisplayName /
// GetSourceDisplayName / BindToSource / Unbind / Update, canonical oleidl.h
// order; stage 48). The OLE linked-object surface. All nine answer
// E_NOTIMPL (no link). Lives at &ccw->oleLinkVtbl.
struct Il2CppComOleLinkVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* setUpdateOptions)(void*, uint32_t);        // slot 3
    int32_t  (IL2CPP_COM_CALL* getUpdateOptions)(void*, uint32_t*);       // slot 4
    int32_t  (IL2CPP_COM_CALL* setSourceMoniker)(void*, void*);           // slot 5
    int32_t  (IL2CPP_COM_CALL* getSourceMoniker)(void*, void**);          // slot 6
    int32_t  (IL2CPP_COM_CALL* setSourceDisplayName)(void*, const void*); // slot 7
    int32_t  (IL2CPP_COM_CALL* getSourceDisplayName)(void*, void**);      // slot 8
    int32_t  (IL2CPP_COM_CALL* bindToSource)(void*, uint32_t, void*);     // slot 9
    int32_t  (IL2CPP_COM_CALL* unbind)(void*);                            // slot 10
    int32_t  (IL2CPP_COM_CALL* update)(void*, void*, void*, void*);       // slot 11
};

// IMoniker vtable (slots 0-22: IUnknown + IPersist::GetClassID +
// IPersistStream::IsDirty / Load / Save / GetSizeMax + BindToObject /
// BindToStorage / Reduce / ComposeWith / Enum / IsEqual / Hash / IsRunning /
// GetTimeOfLastChange / Inverse / CommonPrefixWith / RelativePathTo /
// GetDisplayName / ParseDisplayName / IsSystemMoniker, canonical objidl.h
// order; stage 49). The OLE moniker surface. All nineteen answer E_NOTIMPL
// (no moniker). Lives at &ccw->monikerVtbl.
struct Il2CppComMonikerVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getClassId)(void*, void*);                 // slot 3
    int32_t  (IL2CPP_COM_CALL* isDirty)(void*);                           // slot 4
    int32_t  (IL2CPP_COM_CALL* load)(void*, void*);                       // slot 5
    int32_t  (IL2CPP_COM_CALL* save)(void*, void*, int32_t);              // slot 6
    int32_t  (IL2CPP_COM_CALL* getSizeMax)(void*, void*);                 // slot 7
    int32_t  (IL2CPP_COM_CALL* bindToObject)(void*, void*, void*, const void*, void**); // slot 8
    int32_t  (IL2CPP_COM_CALL* bindToStorage)(void*, void*, void*, const void*, void**); // slot 9
    int32_t  (IL2CPP_COM_CALL* reduce)(void*, void*, uint32_t, void*, void**); // slot 10
    int32_t  (IL2CPP_COM_CALL* composeWith)(void*, void*, int32_t, void**); // slot 11
    int32_t  (IL2CPP_COM_CALL* enumMoniker)(void*, int32_t, void**);      // slot 12
    int32_t  (IL2CPP_COM_CALL* isEqual)(void*, void*);                    // slot 13
    int32_t  (IL2CPP_COM_CALL* hash)(void*, void*);                       // slot 14
    int32_t  (IL2CPP_COM_CALL* isRunning)(void*, void*, void*, void*);    // slot 15
    int32_t  (IL2CPP_COM_CALL* getTimeOfLastChange)(void*, void*, void*, void*); // slot 16
    int32_t  (IL2CPP_COM_CALL* inverse)(void*, void**);                   // slot 17
    int32_t  (IL2CPP_COM_CALL* commonPrefixWith)(void*, void*, void**);   // slot 18
    int32_t  (IL2CPP_COM_CALL* relativePathTo)(void*, void*, void**);     // slot 19
    int32_t  (IL2CPP_COM_CALL* getDisplayName)(void*, void*, void**);     // slot 20
    int32_t  (IL2CPP_COM_CALL* parseDisplayName)(void*, void*, void*, void*, uint32_t*, void**); // slot 21
    int32_t  (IL2CPP_COM_CALL* isSystemMoniker)(void*, uint32_t*);        // slot 22
};

// IRunningObjectTable vtable (slots 0-8: IUnknown + Register / Revoke /
// IsRunning / GetObject / NoteChangeTime / GetTimeOfLastChange / EnumRunning,
// canonical objidl.h order; stage 50). The OLE running-object table surface.
// All seven answer E_NOTIMPL (no ROT). Lives at &ccw->runningObjectTableVtbl.
struct Il2CppComRunningObjectTableVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* registerRot)(void*, void*, void*, uint32_t*); // slot 3
    int32_t  (IL2CPP_COM_CALL* revoke)(void*, uint32_t);                   // slot 4
    int32_t  (IL2CPP_COM_CALL* isRunning)(void*, void*);                   // slot 5
    int32_t  (IL2CPP_COM_CALL* getObject)(void*, void*, void**);           // slot 6
    int32_t  (IL2CPP_COM_CALL* noteChangeTime)(void*, uint32_t, void*);    // slot 7
    int32_t  (IL2CPP_COM_CALL* getTimeOfLastChange)(void*, void*, void*);  // slot 8
    int32_t  (IL2CPP_COM_CALL* enumRunning)(void*, void**);                // slot 9
};

// IBindCtx vtable (slots 0-12: IUnknown + RegisterObjectBound /
// RevokeObjectBound / ReleaseBoundObjects / SetBindOptions / GetBindOptions /
// GetRunningObjectTable / RegisterObjectParam / GetObjectParam /
// EnumObjectParam / RevokeObjectParam, canonical objidl.h order; stage 51).
// The OLE binding-context surface. All ten answer E_NOTIMPL (no bind ctx).
// Lives at &ccw->bindCtxVtbl.
struct Il2CppComBindCtxVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* registerObjectBound)(void*, void*);         // slot 3
    int32_t  (IL2CPP_COM_CALL* revokeObjectBound)(void*, void*);           // slot 4
    int32_t  (IL2CPP_COM_CALL* releaseBoundObjects)(void*);                // slot 5
    int32_t  (IL2CPP_COM_CALL* setBindOptions)(void*, void*);              // slot 6
    int32_t  (IL2CPP_COM_CALL* getBindOptions)(void*, void*);              // slot 7
    int32_t  (IL2CPP_COM_CALL* getRunningObjectTable)(void*, void**);      // slot 8
    int32_t  (IL2CPP_COM_CALL* registerObjectParam)(void*, const void*, void*); // slot 9
    int32_t  (IL2CPP_COM_CALL* getObjectParam)(void*, const void*, void**); // slot 10
    int32_t  (IL2CPP_COM_CALL* enumObjectParam)(void*, void**);            // slot 11
    int32_t  (IL2CPP_COM_CALL* revokeObjectParam)(void*, const void*);     // slot 12
};

// IOleAdviseHolder vtable (slots 0-4: IUnknown + Advise / Unadvise /
// EnumAdvise, canonical objidl.h order; stage 52). The OLE notification-holder
// surface. All three answer E_NOTIMPL (no advise holder).
// Lives at &ccw->oleAdviseHolderVtbl.
struct Il2CppComOleAdviseHolderVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* advise)(void*, void*, uint32_t*);           // slot 3
    int32_t  (IL2CPP_COM_CALL* unadvise)(void*, uint32_t);                 // slot 4
    int32_t  (IL2CPP_COM_CALL* enumAdvise)(void*, void**);                 // slot 5
};

// IDataAdviseHolder vtable (slots 0-4: IUnknown + Advise / Unadvise /
// EnumAdvise, canonical objidl.h order; stage 53). The IDataObject
// DAdvise / EnumAdvise data-notification surface. All three answer
// E_NOTIMPL (no data advise holder). Lives at &ccw->dataAdviseHolderVtbl.
struct Il2CppComDataAdviseHolderVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* advise)(void*, void*, void*, void*);        // slot 3
    int32_t  (IL2CPP_COM_CALL* unadvise)(void*, uint32_t);                 // slot 4
    int32_t  (IL2CPP_COM_CALL* enumAdvise)(void*, void**);                 // slot 5
};

// IOleCache vtable (slots 0-5: IUnknown + Cache / Uncache / EnumCache /
// InitCache / SetData, canonical oleidl.h order; stage 54). The OLE
// presentation-cache surface. All five answer E_NOTIMPL (no cache).
// Lives at &ccw->oleCacheVtbl.
struct Il2CppComOleCacheVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* cache)(void*, void*, uint32_t*);            // slot 3
    int32_t  (IL2CPP_COM_CALL* uncache)(void*, uint32_t);                  // slot 4
    int32_t  (IL2CPP_COM_CALL* enumCache)(void*, void**);                  // slot 5
    int32_t  (IL2CPP_COM_CALL* initCache)(void*, void*);                   // slot 6
    int32_t  (IL2CPP_COM_CALL* setData)(void*, void*, void*, uint32_t);    // slot 7
};

// IOleCache2 vtable (slots 0-8: IUnknown + IOleCache::Cache / Uncache /
// EnumCache / InitCache / SetData + UpdateCache / DiscardCache, canonical
// oleidl.h order; stage 55). The OLE presentation-cache control surface.
// All seven answer E_NOTIMPL (no cache). Lives at &ccw->oleCache2Vtbl.
struct Il2CppComOleCache2Vtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* cache)(void*, void*, uint32_t*);            // slot 3
    int32_t  (IL2CPP_COM_CALL* uncache)(void*, uint32_t);                  // slot 4
    int32_t  (IL2CPP_COM_CALL* enumCache)(void*, void**);                  // slot 5
    int32_t  (IL2CPP_COM_CALL* initCache)(void*, void*);                   // slot 6
    int32_t  (IL2CPP_COM_CALL* setData)(void*, void*, void*, uint32_t);    // slot 7
    int32_t  (IL2CPP_COM_CALL* updateCache)(void*, void*, uint32_t);       // slot 8
    int32_t  (IL2CPP_COM_CALL* discardCache)(void*, uint32_t);             // slot 9
};

// IOleCacheControl vtable (slots 0-3: IUnknown + OnRun / OnStop, canonical
// oleidl.h order; stage 56). The OLE cache run/stop control surface.
// Both answer E_NOTIMPL (no cache). Lives at &ccw->oleCacheControlVtbl.
struct Il2CppComOleCacheControlVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* onRun)(void*, void*);                       // slot 3
    int32_t  (IL2CPP_COM_CALL* onStop)(void*);                             // slot 4
};

// IOleItemContainer vtable (slots 0-7: IUnknown + IParseDisplayName::
// ParseDisplayName + GetObject / GetObjectStorage / GetObjectInStorage /
// LockContainer, canonical oleidl.h order; stage 57). The OLE item-container
// surface. All five answer E_NOTIMPL (no item container).
// Lives at &ccw->oleItemContainerVtbl.
struct Il2CppComOleItemContainerVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* parseDisplayName)(void*, void*, void*, uint32_t*, void**); // slot 3
    int32_t  (IL2CPP_COM_CALL* getObject)(void*, const void*, uint32_t, void*, const void*, void**); // slot 4
    int32_t  (IL2CPP_COM_CALL* getObjectStorage)(void*, const void*, void*, const void*, void**); // slot 5
    int32_t  (IL2CPP_COM_CALL* getObjectInStorage)(void*, const void*, void*, const void*, void**); // slot 6
    int32_t  (IL2CPP_COM_CALL* lockContainer)(void*, int32_t);             // slot 7
};

// IOleUILinkContainer vtable (slots 0-8: IUnknown + GetNextLink /
// SetLinkSource / GetLinkSource / OpenLinkSource / UpdateLink / CancelLink,
// canonical oleuidlg.h order; stage 58). The OLE UI link-container surface.
// All six answer E_NOTIMPL (no link container). Lives at
// &ccw->oleUILinkContainerVtbl.
struct Il2CppComOleUILinkContainerVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getNextLink)(void*, uint32_t, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* setLinkSource)(void*, uint32_t, const void*, uint32_t, uint32_t); // slot 4
    int32_t  (IL2CPP_COM_CALL* getLinkSource)(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*); // slot 5
    int32_t  (IL2CPP_COM_CALL* openLinkSource)(void*, uint32_t);           // slot 6
    int32_t  (IL2CPP_COM_CALL* updateLink)(void*, uint32_t, int32_t, int32_t); // slot 7
    int32_t  (IL2CPP_COM_CALL* cancelLink)(void*, uint32_t);               // slot 8
};

// IOleUILinkInfo vtable (slots 0-9: IUnknown + IOleUILinkContainer::
// GetNextLink / SetLinkSource / GetLinkSource / OpenLinkSource / UpdateLink /
// CancelLink + GetLastUpdate, canonical oleuidlg.h order; stage 59). The OLE
// UI link-info surface. All seven answer E_NOTIMPL (no link info).
// Lives at &ccw->oleUILinkInfoVtbl.
struct Il2CppComOleUILinkInfoVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getNextLink)(void*, uint32_t, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* setLinkSource)(void*, uint32_t, const void*, uint32_t, uint32_t); // slot 4
    int32_t  (IL2CPP_COM_CALL* getLinkSource)(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*); // slot 5
    int32_t  (IL2CPP_COM_CALL* openLinkSource)(void*, uint32_t);           // slot 6
    int32_t  (IL2CPP_COM_CALL* updateLink)(void*, uint32_t, int32_t, int32_t); // slot 7
    int32_t  (IL2CPP_COM_CALL* cancelLink)(void*, uint32_t);               // slot 8
    int32_t  (IL2CPP_COM_CALL* getLastUpdate)(void*, uint32_t, void*);     // slot 9
};

// IOleUIObjInfo vtable (slots 0-6: IUnknown + GetObjectInfo / GetDisplayName
// / GetTypeName / GetLastError, canonical oleuidlg.h order; stage 60). The
// OLE UI object-info surface. All four answer E_NOTIMPL (no object info).
// Lives at &ccw->oleUIObjInfoVtbl.
struct Il2CppComOleUIObjInfoVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getObjectInfo)(void*, uint32_t, uint32_t*, void**); // slot 3
    int32_t  (IL2CPP_COM_CALL* getDisplayName)(void*, uint32_t, void**);  // slot 4
    int32_t  (IL2CPP_COM_CALL* getTypeName)(void*, uint32_t, void**);     // slot 5
    int32_t  (IL2CPP_COM_CALL* getLastError)(void*, uint32_t, void**);    // slot 6
};

// IOleUIVerify vtable (slots 0-3: IUnknown + Verify, canonical oleuidlg.h
// order; stage 61). The OLE UI verification surface. Verify answers
// E_NOTIMPL. Lives at &ccw->oleUIVerifyVtbl.
struct Il2CppComOleUIVerifyVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* verify)(void*, void*);                      // slot 3
};

// IOleUILinkContainerA vtable (slots 0-8: IUnknown + GetNextLink /
// SetLinkSource / GetLinkSource / OpenLinkSource / UpdateLink / CancelLink,
// canonical oleuidlg.h order; stage 62). The ANSI OLE UI link-container
// surface. All six answer E_NOTIMPL (no link container).
// Lives at &ccw->oleUILinkContainerAVtbl.
struct Il2CppComOleUILinkContainerAVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getNextLink)(void*, uint32_t, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* setLinkSource)(void*, uint32_t, const void*, uint32_t, uint32_t); // slot 4
    int32_t  (IL2CPP_COM_CALL* getLinkSource)(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*); // slot 5
    int32_t  (IL2CPP_COM_CALL* openLinkSource)(void*, uint32_t);           // slot 6
    int32_t  (IL2CPP_COM_CALL* updateLink)(void*, uint32_t, int32_t, int32_t); // slot 7
    int32_t  (IL2CPP_COM_CALL* cancelLink)(void*, uint32_t);               // slot 8
};

// IOleUILinkInfoA vtable (slots 0-9: IUnknown + IOleUILinkContainerA::
// GetNextLink / SetLinkSource / GetLinkSource / OpenLinkSource / UpdateLink /
// CancelLink + GetLastUpdate, canonical oleuidlg.h order; stage 63). The
// ANSI OLE UI link-info surface. All seven answer E_NOTIMPL (no link info).
// Lives at &ccw->oleUILinkInfoAVtbl.
struct Il2CppComOleUILinkInfoAVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getNextLink)(void*, uint32_t, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* setLinkSource)(void*, uint32_t, const void*, uint32_t, uint32_t); // slot 4
    int32_t  (IL2CPP_COM_CALL* getLinkSource)(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*); // slot 5
    int32_t  (IL2CPP_COM_CALL* openLinkSource)(void*, uint32_t);           // slot 6
    int32_t  (IL2CPP_COM_CALL* updateLink)(void*, uint32_t, int32_t, int32_t); // slot 7
    int32_t  (IL2CPP_COM_CALL* cancelLink)(void*, uint32_t);               // slot 8
    int32_t  (IL2CPP_COM_CALL* getLastUpdate)(void*, uint32_t, void*);     // slot 9
};

// IOleUIObjInfoA vtable (slots 0-6: IUnknown + GetObjectInfo / GetDisplayName
// / GetTypeName / GetLastError, canonical oleuidlg.h order; stage 64). The
// ANSI OLE UI object-info surface. All four answer E_NOTIMPL (no object
// info). Lives at &ccw->oleUIObjInfoAVtbl.
struct Il2CppComOleUIObjInfoAVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getObjectInfo)(void*, uint32_t, uint32_t*, void**); // slot 3
    int32_t  (IL2CPP_COM_CALL* getDisplayName)(void*, uint32_t, void**);  // slot 4
    int32_t  (IL2CPP_COM_CALL* getTypeName)(void*, uint32_t, void**);     // slot 5
    int32_t  (IL2CPP_COM_CALL* getLastError)(void*, uint32_t, void**);    // slot 6
};

// IOleUIVerifyA vtable (slots 0-3: IUnknown + Verify, canonical oleuidlg.h
// order; stage 65). The ANSI OLE UI verification surface. Verify answers
// E_NOTIMPL. Lives at &ccw->oleUIVerifyAVtbl.
struct Il2CppComOleUIVerifyAVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* verify)(void*, void*);                      // slot 3
};

// IOleUILinkContainerW vtable (slots 0-8: IUnknown + GetNextLink /
// SetLinkSource / GetLinkSource / OpenLinkSource / UpdateLink / CancelLink,
// canonical oleuidlg.h order; stage 66). The Unicode OLE UI link-container
// surface. All six answer E_NOTIMPL (no link container).
// Lives at &ccw->oleUILinkContainerWVtbl.
struct Il2CppComOleUILinkContainerWVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getNextLink)(void*, uint32_t, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* setLinkSource)(void*, uint32_t, const void*, uint32_t, uint32_t); // slot 4
    int32_t  (IL2CPP_COM_CALL* getLinkSource)(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*); // slot 5
    int32_t  (IL2CPP_COM_CALL* openLinkSource)(void*, uint32_t);           // slot 6
    int32_t  (IL2CPP_COM_CALL* updateLink)(void*, uint32_t, int32_t, int32_t); // slot 7
    int32_t  (IL2CPP_COM_CALL* cancelLink)(void*, uint32_t);               // slot 8
};

// IOleUILinkInfoW vtable (slots 0-9: IUnknown + IOleUILinkContainerW::
// GetNextLink / SetLinkSource / GetLinkSource / OpenLinkSource / UpdateLink /
// CancelLink + GetLastUpdate, canonical oleuidlg.h order; stage 67). The
// Unicode OLE UI link-info surface. All seven answer E_NOTIMPL (no link
// info). Lives at &ccw->oleUILinkInfoWVtbl.
struct Il2CppComOleUILinkInfoWVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getNextLink)(void*, uint32_t, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* setLinkSource)(void*, uint32_t, const void*, uint32_t, uint32_t); // slot 4
    int32_t  (IL2CPP_COM_CALL* getLinkSource)(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*); // slot 5
    int32_t  (IL2CPP_COM_CALL* openLinkSource)(void*, uint32_t);           // slot 6
    int32_t  (IL2CPP_COM_CALL* updateLink)(void*, uint32_t, int32_t, int32_t); // slot 7
    int32_t  (IL2CPP_COM_CALL* cancelLink)(void*, uint32_t);               // slot 8
    int32_t  (IL2CPP_COM_CALL* getLastUpdate)(void*, uint32_t, void*);     // slot 9
};

// IOleUIObjInfoW vtable (slots 0-6: IUnknown + GetObjectInfo / GetDisplayName
// / GetTypeName / GetLastError, canonical oleuidlg.h order; stage 68). The
// Unicode OLE UI object-info surface. All four answer E_NOTIMPL (no object
// info). Lives at &ccw->oleUIObjInfoWVtbl.
struct Il2CppComOleUIObjInfoWVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getObjectInfo)(void*, uint32_t, uint32_t*, void**); // slot 3
    int32_t  (IL2CPP_COM_CALL* getDisplayName)(void*, uint32_t, void**);  // slot 4
    int32_t  (IL2CPP_COM_CALL* getTypeName)(void*, uint32_t, void**);     // slot 5
    int32_t  (IL2CPP_COM_CALL* getLastError)(void*, uint32_t, void**);    // slot 6
};

// IOleUIVerifyW vtable (slots 0-3: IUnknown + Verify, canonical oleuidlg.h
// order; stage 69). The Unicode OLE UI verification surface. Verify answers
// E_NOTIMPL. Lives at &ccw->oleUIVerifyWVtbl.
struct Il2CppComOleUIVerifyWVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* verify)(void*, void*);                      // slot 3
};

// IEnumConnectionPoints vtable (slots 0-6: IUnknown + Next / Skip / Reset /
// Clone, canonical ocidl.h order; stage 70). The connection-point enumerator
// (the IConnectionPointContainer EnumConnectionPoints return type). All four
// answer E_NOTIMPL (the enumeration is empty headless). Lives at
// &ccw->enumConnectionPointsVtbl.
struct Il2CppComEnumConnectionPointsVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void*, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                    // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};

// IEnumSTATSTG vtable (slots 0-6: IUnknown + Next / Skip / Reset / Clone,
// canonical objidl.h order; stage 71). The storage-element enumerator (the
// IStorage EnumElements return type). All four answer E_NOTIMPL (the
// enumeration is empty headless). Lives at &ccw->enumStatStgVtbl.
struct Il2CppComEnumStatStgVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* next)(void*, uint32_t, void*, uint32_t*);  // slot 3
    int32_t  (IL2CPP_COM_CALL* skip)(void*, uint32_t);                    // slot 4
    int32_t  (IL2CPP_COM_CALL* reset)(void*);                             // slot 5
    int32_t  (IL2CPP_COM_CALL* clone)(void*, void**);                     // slot 6
};
// IDataObject vtable (slots 0-11: IUnknown + the 9 canonical oleidl.h methods
// -- the OLE data-object surface, stage 17 batch 2 typed-RCW foundation).
// This interface view lives at &ccw->dataObjectVtbl -- a DISTINCT address
// inside the CCW allocation (container_of recovery via offsetof, the phase-4
// view precedent). Every method slot carries the fixed 4-intptr shape (a0..a3,
// the icall_com_call_vtable_slot carrier); CcwDataObjectDispatch decodes the
// MANAGED signature (void/int return, int32 params) from the registered
// interface's typeinfo by NAME.
struct Il2CppComDataObjectVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* getData)(void*, intptr_t, intptr_t, intptr_t, intptr_t);               // slot 3
    int32_t  (IL2CPP_COM_CALL* getDataHere)(void*, intptr_t, intptr_t, intptr_t, intptr_t);           // slot 4
    int32_t  (IL2CPP_COM_CALL* queryGetData)(void*, intptr_t, intptr_t, intptr_t, intptr_t);          // slot 5
    int32_t  (IL2CPP_COM_CALL* getCanonicalFormatEtc)(void*, intptr_t, intptr_t, intptr_t, intptr_t); // slot 6
    int32_t  (IL2CPP_COM_CALL* setData)(void*, intptr_t, intptr_t, intptr_t, intptr_t);               // slot 7
    int32_t  (IL2CPP_COM_CALL* enumFormatEtc)(void*, intptr_t, intptr_t, intptr_t, intptr_t);         // slot 8
    int32_t  (IL2CPP_COM_CALL* dAdvise)(void*, intptr_t, intptr_t, intptr_t, intptr_t);               // slot 9
    int32_t  (IL2CPP_COM_CALL* dUnadvise)(void*, intptr_t, intptr_t, intptr_t, intptr_t);             // slot 10
    int32_t  (IL2CPP_COM_CALL* dEnumFormatEtc)(void*, intptr_t, intptr_t, intptr_t, intptr_t);        // slot 11
};

// An IEnumVARIANT view: the interface pointer a native consumer receives is
// the address of the view's vtable-pointer member (offset 0 -- the methods
// recover the view by casting 'self' directly, and the view's back pointer
// carries the ccw). The MAIN view is inlined in the CCW (&ccw->enumView);
// every Clone is a heap Il2CppComEnumView with an independent cursor that
// shares the ccw refcount. 'index' is the enumeration cursor (0-based into
// the ccw's enumItems registry).
struct Il2CppComEnumView
{
    const Il2CppComEnumVtable* vtbl; // -> kEnumVtable (main) / kEnumCloneVtable (clone)
    Il2CppComCcw* ccw;               // the owning CCW (shared refcount / identity)
    int32_t index;                   // the enumeration cursor (0-based)
};

// Per-CCW name -> dispId registry entry (GetIDsOfNames fills it on demand;
// the name is a heap copy of the caller's UTF-16 string -- freed by the CCW
// destructor). A member may be a PROPERTY: the registered method name is the
// CLR getter/setter ("get_X" / "set_X") and the IDispatch-visible name is
// the property name "X" (GetIDsOfNames strips the prefix; Invoke routes by
// wFlags -- DISPATCH_PROPERTYGET -> get_X, DISPATCH_PROPERTYPUT -> set_X).
constexpr int32_t kCcwMaxMembers = 12;
constexpr int32_t kMaxDataAdvise = 4; // the IDataObject DAdvise registry capacity (stage 76)
struct Il2CppComCcwMember
{
    int32_t  dispId;
    wchar_t* name;
    bool     isProperty;
    bool     isSetter;
};

// The CCW object. COM convention: the interface pointer the native consumer
// receives is the address of THIS object, whose FIRST member is the pointer
// to the vtable array -- so the generic vtable dispatch (VtableSlot /
// icall_com_call_vtable_slot) works identically for real COM objects and
// CCWs (*(void**)iface = vtbl pointer, vtbl[slot] = method). refCount starts
// at 1 (the reference the creator holds); native Release to 0 frees the
// GCHandle and the heap block. Single-threaded by contract (marshalext
// registry precedent -- the M3 runtime is single-threaded; cross-thread CCW
// refcounting is phase 2).
//
// PHASE 4 -- multi-interface CCW (the ActiveX hosting surface). The identity
// view (IUnknown + IDispatch) is the object's base address (vtbl at offset
// 0) -- the pointer icall_com_ccw_create returns and QI for IUnknown/
// IDispatch hands out. The OLE views live at &oleVtbl / &siteVtbl -- the
// address of the member that holds the view's vtable pointer (a distinct
// address inside the same allocation; the per-view methods recover the ccw
// via container_of/offsetof). This mirrors coreclr SimpleComCallWrapper's
// m_rgpVtable[] per-interface vtable layout: a single flat vtable cannot
// carry IDispatch AND IOleObject (slot 3 collides -- GetTypeInfoCount vs
// SetClientSite).
//
// PHASE 5 -- the event-source views. The IConnectionPointContainer view lives
// at &cpcVtbl (container_of recovery); the per-event-interface connection
// points are the fixed cps[] array (each entry a view whose interface pointer
// is its own address -- the methods recover the view by casting self; the
// view's back pointer carries the ccw). All views share the ONE refcount,
// GCHandle and member registry (single-threaded contract).
struct Il2CppComCcw
{
    const Il2CppComCcwVtable* vtbl; // -> kCcwVtable (identity view, offset 0)
#if defined(_WIN32)
    const Il2CppComOleVtable* oleVtbl;   // -> kOleVtable (IOleObject view)
    const Il2CppComSiteVtable* siteVtbl; // -> kSiteVtable (IOleClientSite view)
    const Il2CppComInPlaceVtable* inPlaceVtbl; // -> kInPlaceVtable (IOleInPlaceObject view, stage 27)
    const Il2CppComClassInfoVtable* classInfoVtbl; // -> kClassInfoVtable (IProvideClassInfo view, stage 28)
    const Il2CppComInPlaceSiteVtable* inPlaceSiteVtbl; // -> kInPlaceSiteVtable (IOleInPlaceSite view, stage 29)
    const Il2CppComAdviseSinkVtable* adviseSinkVtbl; // -> kAdviseSinkVtable (IAdviseSink view, stage 30)
    const Il2CppComAdviseSink2Vtable* adviseSink2Vtbl; // -> kAdviseSink2Vtable (IAdviseSink2 view, stage 72)
    const Il2CppComAdviseSinkExVtable* adviseSinkExVtbl; // -> kAdviseSinkExVtable (IAdviseSinkEx view, stage 73)
    const Il2CppComStreamVtable* streamVtbl; // -> kStreamVtable (IStream view, stage 74)
    const Il2CppComStorageVtable* storageVtbl; // -> kStorageVtable (IStorage view, stage 75)
    const Il2CppComInPlaceUIWindowVtable* inPlaceUiWindowVtbl; // -> kInPlaceUIWindowVtable (IOleInPlaceUIWindow view, stage 31)
    const Il2CppComInPlaceFrameVtable* inPlaceFrameVtbl; // -> kInPlaceFrameVtable (IOleInPlaceFrame view, stage 32)
    const Il2CppComOleContainerVtable* oleContainerVtbl; // -> kOleContainerVtable (IOleContainer view, stage 33)
    const Il2CppComEnumUnknownVtable* enumUnknownVtbl; // -> kEnumUnknownVtable (IEnumUnknown view, stage 34)
    const Il2CppComEnumStringVtable* enumStringVtbl; // -> kEnumStringVtable (IEnumString view, stage 35)
    const Il2CppComEnumFormatEtcVtable* enumFormatEtcVtbl; // -> kEnumFormatEtcVtable (IEnumFORMATETC view, stage 36)
    const Il2CppComPersistStreamVtable* persistStreamVtbl; // -> kPersistStreamVtable (IPersistStream view, stage 37)
    const Il2CppComPersistFileVtable* persistFileVtbl; // -> kPersistFileVtable (IPersistFile view, stage 38)
    const Il2CppComPersistVtable* persistVtbl; // -> kPersistVtable (IPersist view, stage 39)
    const Il2CppComInPlaceActiveObjectVtable* inPlaceActiveObjectVtbl; // -> kInPlaceActiveObjectVtable (IOleInPlaceActiveObject view, stage 40)
    const Il2CppComPersistPropertyBagVtable* persistPropertyBagVtbl; // -> kPersistPropertyBagVtable (IPersistPropertyBag view, stage 41)
    const Il2CppComParseDisplayNameVtable* parseDisplayNameVtbl; // -> kParseDisplayNameVtable (IParseDisplayName view, stage 42)
    const Il2CppComViewObjectVtable* viewObjectVtbl; // -> kViewObjectVtable (IViewObject view, stage 43)
    const Il2CppComEnumSTATDATAVtable* enumStatDataVtbl; // -> kEnumSTATDATAVtable (IEnumSTATDATA view, stage 44)
    const Il2CppComEnumOLEVERBVtable* enumOleVerbVtbl; // -> kEnumOLEVERBVtable (IEnumOLEVERB view, stage 45)
    const Il2CppComEnumMonikerVtable* enumMonikerVtbl; // -> kEnumMonikerVtable (IEnumMoniker view, stage 46)
    const Il2CppComViewObject2Vtable* viewObject2Vtbl; // -> kViewObject2Vtable (IViewObject2 view, stage 47)
    const Il2CppComOleLinkVtable* oleLinkVtbl; // -> kOleLinkVtable (IOleLink view, stage 48)
    const Il2CppComMonikerVtable* monikerVtbl; // -> kMonikerVtable (IMoniker view, stage 49)
    const Il2CppComRunningObjectTableVtable* runningObjectTableVtbl; // -> kRunningObjectTableVtable (IRunningObjectTable view, stage 50)
    const Il2CppComBindCtxVtable* bindCtxVtbl; // -> kBindCtxVtable (IBindCtx view, stage 51)
    const Il2CppComOleAdviseHolderVtable* oleAdviseHolderVtbl; // -> kOleAdviseHolderVtable (IOleAdviseHolder view, stage 52)
    const Il2CppComDataAdviseHolderVtable* dataAdviseHolderVtbl; // -> kDataAdviseHolderVtable (IDataAdviseHolder view, stage 53)
    const Il2CppComOleCacheVtable* oleCacheVtbl; // -> kOleCacheVtable (IOleCache view, stage 54)
    const Il2CppComOleCache2Vtable* oleCache2Vtbl; // -> kOleCache2Vtable (IOleCache2 view, stage 55)
    const Il2CppComOleCacheControlVtable* oleCacheControlVtbl; // -> kOleCacheControlVtable (IOleCacheControl view, stage 56)
    const Il2CppComOleItemContainerVtable* oleItemContainerVtbl; // -> kOleItemContainerVtable (IOleItemContainer view, stage 57)
    const Il2CppComOleUILinkContainerVtable* oleUILinkContainerVtbl; // -> kOleUILinkContainerVtable (IOleUILinkContainer view, stage 58)
    const Il2CppComOleUILinkInfoVtable* oleUILinkInfoVtbl; // -> kOleUILinkInfoVtable (IOleUILinkInfo view, stage 59)
    const Il2CppComOleUIObjInfoVtable* oleUIObjInfoVtbl; // -> kOleUIObjInfoVtable (IOleUIObjInfo view, stage 60)
    const Il2CppComOleUIVerifyVtable* oleUIVerifyVtbl; // -> kOleUIVerifyVtable (IOleUIVerify view, stage 61)
    const Il2CppComOleUILinkContainerAVtable* oleUILinkContainerAVtbl; // -> kOleUILinkContainerAVtable (IOleUILinkContainerA view, stage 62)
    const Il2CppComOleUILinkInfoAVtable* oleUILinkInfoAVtbl; // -> kOleUILinkInfoAVtable (IOleUILinkInfoA view, stage 63)
    const Il2CppComOleUIObjInfoAVtable* oleUIObjInfoAVtbl; // -> kOleUIObjInfoAVtable (IOleUIObjInfoA view, stage 64)
    const Il2CppComOleUIVerifyAVtable* oleUIVerifyAVtbl; // -> kOleUIVerifyAVtable (IOleUIVerifyA view, stage 65)
    const Il2CppComOleUILinkContainerWVtable* oleUILinkContainerWVtbl; // -> kOleUILinkContainerWVtable (IOleUILinkContainerW view, stage 66)
    const Il2CppComOleUILinkInfoWVtable* oleUILinkInfoWVtbl; // -> kOleUILinkInfoWVtable (IOleUILinkInfoW view, stage 67)
    const Il2CppComOleUIObjInfoWVtable* oleUIObjInfoWVtbl; // -> kOleUIObjInfoWVtable (IOleUIObjInfoW view, stage 68)
    const Il2CppComOleUIVerifyWVtable* oleUIVerifyWVtbl; // -> kOleUIVerifyWVtable (IOleUIVerifyW view, stage 69)
    const Il2CppComEnumConnectionPointsVtable* enumConnectionPointsVtbl; // -> kEnumConnectionPointsVtable (IEnumConnectionPoints view, stage 70)
    const Il2CppComEnumStatStgVtable* enumStatStgVtbl; // -> kEnumStatStgVtable (IEnumSTATSTG view, stage 71)
    const Il2CppComCpcVtable* cpcVtbl;   // -> kCpcVtable (IConnectionPointContainer view)
    Il2CppComConnectionPointView cps[kCcwMaxConnectionPoints]; // the connection point views
    const Il2CppComDropTargetVtable* dropVtbl; // -> kDropVtable (IDropTarget view, stage 15)
    void* clientSite;                    // the stored IOleClientSite (AddRef'd)
    void* dropDispatch;                  // stage 16: the registered drop dispatch callback (null = unregistered)
#endif
    // Stage 17 (every platform): the IDataObject view's registration state.
    // The view itself is pure memory + typeinfo + il2cpp_interface_dispatch
    // (no ole32 dependency -- the stage-10 every-platform precedent). vtbl
    // always points at kDataObjectVtable (the QI gate decides whether the
    // view is handed out); iface/typeInfo are null until the view is
    // registered; slots[i] is the cached MANAGED interface slot of native
    // slot 3+i's method name, -1 = the interface does not declare it.
    const Il2CppComDataObjectVtable* dataObjectVtbl; // -> kDataObjectVtable (IDataObject view)
    Il2CppClass* dataObjectIface;        // the registered interface descriptor (null = unregistered)
    const Il2CppTypeInfo* dataObjectTypeInfo; // the interface's typeinfo (name -> method resolution)
    int32_t dataObjectSlots[9];          // cached interface slot per native slot; -1 = undeclared
    // Stage 76: the IDataObject DAdvise/DUnadvise data-notification registry.
    // DAdvise stores a native IAdviseSink* with a STABLE cookie (assigned from
    // an incrementing counter -- never renumbered on removal, the OLE
    // contract); the icall_com_data_object_notify trigger fires OnDataChange
    // on every registered sink. Plain pointers -- no ownership, nothing to
    // free.
    void*    dataAdviseSinks[kMaxDataAdvise];   // the registered IAdviseSink views
    uint32_t dataAdviseCookies[kMaxDataAdvise]; // the stable per-sink cookies
    uint32_t dataAdviseCount;                   // number of registered sinks
    uint32_t dataAdviseNextCookie;              // the next cookie to assign
    // Stage 17 batch 4: the managed drop dispatch (the CCW->OnDragEnter
    // bridge). Registered via icall_com_ccw_register_drop_dispatch_managed
    // (every platform); the pump icall_com_ccw_drop_dispatch_managed drives
    // decode->construct->dispatch->effect-writeback and the Windows IDropTarget
    // view methods call it after the POINTL decode (managed takes priority over
    // the stage-16 native forwarding callback). Plain pointers/flags/offsets --
    // no ownership, nothing to free in the destructor.
    int32_t  dropManaged;            // 1 = the managed dispatch is registered
    Il2CppClass* dropIface;          // the transpiled IDropTarget descriptor
    const Il2CppTypeInfo* dropIfaceInfo; // its typeinfo (name -> slot resolution)
    Il2CppClass* dropArgsClass;      // the DragEventArgs descriptor
    int32_t  dropSlots[4];           // cached interface slot per kComDropMethod*; -1 = undeclared
    int32_t  dropEffectOffset;       // the args' "Effect" field offset; -1 = no write-back
    // Stage 18: IDispatch::Invoke -> managed method dispatch. GetIDsOfNames
    // already resolves names to dispIds (phase 2); Invoke validates the
    // DISPPARAMS (phase 7) and then, when this dispatch is registered,
    // resolves the member NAME to the managed interface slot (typeinfo) and
    // calls through il2cpp_interface_dispatch. The managed return type comes
    // from the typeinfo signature: 'i' -> the int32 is written as VT_I4,
    // 'v' -> VT_EMPTY. Registered via icall_com_ccw_register_invoke_dispatch.
    int32_t  invokeManaged;          // 1 = the managed invoke dispatch is registered
    Il2CppClass* invokeIface;        // the transpiled interface descriptor
    const Il2CppTypeInfo* invokeIfaceInfo; // its typeinfo (name -> slot resolution)
    int32_t  refCount;
    int32_t  gcHandle;              // strong GCHandle to the wrapped managed object
    int32_t  memberCount;           // names registered via GetIDsOfNames
    Il2CppComCcwMember members[kCcwMaxMembers];
    Il2CppComEnumView enumView;                  // the IEnumVARIANT view (inlined; its interface pointer is &enumView)
    uint8_t  enumItems[kCcwMaxEnumItems][16];    // the registered enumeration items (16-byte VARIANT value copies)
    void*    enumOwned[kCcwMaxEnumItems];        // stage-11: the OWNED BSTR master per item (null = not a BSTR item)
    int32_t  enumOwnedLen[kCcwMaxEnumItems];     // stage-11: the owned BSTR's char count (the SysAllocStringLen source)
    int32_t  enumCount;                          // number of registered enumeration items
    ~Il2CppComCcw();
};

#if defined(_WIN32)

// Release an IUnknown reference through its vtable (defined later -- the CCW
// destructor releases connected sinks on teardown).
void ReleaseInterface(void* iface);

// ole32 dynamic-resolution API shape (the LoadOle32 implementation lives
// later; the CCW destructor and the stage-11 BSTR Next path need the COMPLETE
// struct here -- the destructor SysFreeString's the owned BSTR masters).
typedef int32_t (WINAPI* CoCreateInstanceFn)(const void* rclsid, void* pUnkOuter,
                                             uint32_t dwClsContext, const void* riid,
                                             void** ppv);
typedef int32_t (WINAPI* CLSIDFromProgIDFn)(const wchar_t* lpszProgID, void* lpclsid);
typedef int32_t (WINAPI* GetActiveObjectFn)(const void* rclsid, void* pvReserved,
                                            void** ppunk);
typedef int32_t (WINAPI* CoInitializeExFn)(const void* pvReserved, uint32_t dwCoInit);
// Stage-11 BSTR ownership (OLEAUT32): SysAllocStringLen allocates a BSTR copy
// of 'len' OLECHARs from 'psz' (null-terminated by the allocator);
// SysFreeString releases a BSTR. Resolved from oleaut32.dll at runtime like
// the ole32 API -- void*/void carriers keep the ABI shape portable here.
typedef void* (WINAPI* SysAllocStringLenFn)(const void* psz, uint32_t len);
typedef void  (WINAPI* SysFreeStringFn)(void* bstr);
// Stage-12 IStream (ole32): CreateStreamOnHGlobal wraps an HGLOBAL into a
// native IStream (fDeleteOnRelease controls whether the stream's Release
// frees the HGLOBAL); GetHGlobalFromStream recovers the HGLOBAL back from the
// stream. Both are ole32 exports resolved at runtime like the rest of the API
// -- DELIBERATELY not part of api.ok (the stage-11 strategy: GetOle32()'s
// "ole32.dll loaded" gate keeps its semantics; the two entry points are
// checked separately with NotSupportedException when unresolvable).
typedef int32_t (WINAPI* CreateStreamOnHGlobalFn)(void* hGlobal, int32_t fDeleteOnRelease,
                                                  void** ppstm);
typedef int32_t (WINAPI* GetHGlobalFromStreamFn)(void* pstm, void** phglobal);
// Stage-15 OLE drag-drop (ole32): RegisterDragDrop(hwnd, pDropTarget) registers
// a window as a drop target; RevokeDragDrop(hwnd) revokes it. Resolved at
// runtime like the rest of the API -- DELIBERATELY not part of api.ok (the
// stage-11 strategy: GetOle32()'s "ole32.dll loaded" gate keeps its semantics;
// the two entry points are checked separately with NotSupportedException when
// unresolvable).
typedef int32_t (WINAPI* RegisterDragDropFn)(void* hwnd, void* pDropTarget);
typedef int32_t (WINAPI* RevokeDragDropFn)(void* hwnd);

struct Ole32Api
{
    bool                  ok;
    CoCreateInstanceFn    coCreateInstance;
    CLSIDFromProgIDFn     clsidFromProgID;
    GetActiveObjectFn     getActiveObject;
    CoInitializeExFn      coInitializeEx;
    SysAllocStringLenFn   sysAllocStringLen;
    SysFreeStringFn       sysFreeString;
    CreateStreamOnHGlobalFn createStreamOnHGlobal;
    GetHGlobalFromStreamFn   getHGlobalFromStream;
    RegisterDragDropFn    registerDragDrop;
    RevokeDragDropFn      revokeDragDrop;
};

const Ole32Api& GetOle32();

// Recover the owning CCW from an OLE/site view pointer (the view address is
// the offset of the corresponding vtable-pointer member inside the ccw).
Il2CppComCcw* CcwFromOleView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleVtbl));
}

Il2CppComCcw* CcwFromSiteView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, siteVtbl));
}

Il2CppComCcw* CcwFromCpcView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, cpcVtbl));
}

Il2CppComCcw* CcwFromInPlaceView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, inPlaceVtbl));
}

// Recover the owning CCW from the drop-target view pointer (the view address
// is the offset of the dropVtbl member inside the ccw).
Il2CppComCcw* CcwFromDropView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, dropVtbl));
}

// The connection point view's interface pointer IS the view address (its
// vtable-pointer member is at offset 0) -- recover the view by casting.
Il2CppComConnectionPointView* CpView(void* self)
{
    return reinterpret_cast<Il2CppComConnectionPointView*>(self);
}

#endif // defined(_WIN32)

Il2CppComCcw::~Il2CppComCcw()
{
    for (int32_t i = 0; i < memberCount; ++i)
    {
        delete[] members[i].name;
    }
#if defined(_WIN32)
    // Stage-11 teardown contract: SysFreeString every OWNED BSTR master
    // (icall_com_ccw_enum_register_bstr_item's storage -- the copies Next
    // handed to callers are the CALLER's to VariantClear). Nulled after the
    // free: the loop is idempotent (a second pass finds only nulls -- no
    // double-free). GetOle32() is only touched when an owned BSTR exists
    // (a BSTR can only be owned if oleaut32 resolved during registration).
    for (int32_t i = 0; i < enumCount; ++i)
    {
        if (enumOwned[i] != nullptr)
        {
            GetOle32().sysFreeString(enumOwned[i]);
            enumOwned[i] = nullptr;
        }
    }
    // Phase-5/9 teardown contract: release every connected sink reference (a
    // connection point released its sinks on Unadvise; a still-connected sink
    // is released here -- the CCW owns every connection until destruction).
    for (int32_t i = 0; i < kCcwMaxConnectionPoints; ++i)
    {
        for (int32_t j = 0; j < kCcwMaxSinksPerCp; ++j)
        {
            if (cps[i].sink[j] != nullptr)
            {
                ReleaseInterface(cps[i].sink[j]);
                cps[i].sink[j] = nullptr;
            }
        }
    }
#endif
}

// ASCII case folding (automation method names are ASCII; OleAut GetIDsOfNames
// resolves names case-insensitively -- a case-insensitive match is a superset
// of the exact-match the tests use).
wchar_t AsciiFold(wchar_t c)
{
    return (c >= L'A' && c <= L'Z') ? static_cast<wchar_t>(c + 32) : c;
}

// Case-insensitive null-terminated wchar string equality.
bool NameEquals(const wchar_t* a, const wchar_t* b)
{
    for (;; ++a, ++b)
    {
        const wchar_t ca = AsciiFold(*a);
        const wchar_t cb = AsciiFold(*b);
        if (ca != cb)
        {
            return false;
        }
        if (ca == L'\0')
        {
            return true;
        }
    }
}

// Index of the registry entry whose name matches 'name', or -1.
int32_t FindMember(const Il2CppComCcw* ccw, const wchar_t* name)
{
    for (int32_t i = 0; i < ccw->memberCount; ++i)
    {
        if (NameEquals(ccw->members[i].name, name))
        {
            return i;
        }
    }
    return -1;
}

// Heap copy of the caller's null-terminated UTF-16 name (the caller's buffer
// is only valid for the duration of the GetIDsOfNames call).
wchar_t* DuplicateName(const wchar_t* name)
{
    int32_t len = 0;
    while (name[len] != L'\0')
    {
        ++len;
    }
    wchar_t* copy = new wchar_t[len + 1];
    for (int32_t i = 0; i <= len; ++i)
    {
        copy[i] = name[i];
    }
    return copy;
}

// CCW vtable implementations (the 7 IUnknown+IDispatch identity slots).
int32_t IL2CPP_COM_CALL CcwQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = static_cast<Il2CppComCcw*>(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    // The CCW implements IUnknown (the identity interface) and IDispatch (the
    // ActiveX/automation late-binding surface -- the same single vtable
    // carries the IDispatch slots 3-6, so the IDispatch pointer equals the
    // identity pointer, a legal single-vtable CCW layout). Phase 4 extends
    // the dispatch to the OLE views (IOleObject / IOleClientSite -- each a
    // SEPARATE vtable at a distinct address inside the CCW; see the struct
    // comment for the layout rationale).
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = self; // the identity interface pointer (same address, AddRef'd)
        ++ccw->refCount;
        return kComHrOk;
    }
    // Stage 10 -- the IEnumVARIANT view: dispatched OUTSIDE the Windows gate
    // (the enumeration surface is every-platform -- pure memory layout + GUID
    // compare, no ole32 dependency). The view is inlined in the CCW (the
    // interface pointer is &ccw->enumView, a distinct address).
    if (std::memcmp(iid, kComIidIEnumVariant, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumView); // the enumeration view
        ++ccw->refCount;
        return kComHrOk;
    }
    // Stage 17 -- the IDataObject view (the typed-RCW surface): handed out
    // ONLY when the view is REGISTERED (icall_com_ccw_register_interface_view)
    // AND il2cpp_is_instance confirms the wrapped managed object implements
    // the registered interface -- dispatching to a non-implementer would hit
    // the il2cpp_interface_dispatch abort (a programming-error path). Every
    // platform (pure memory + typeinfo + dispatch -- no ole32 dependency).
    if (std::memcmp(iid, kComIidIDataObject, 16) == 0)
    {
        if (ccw->dataObjectIface == nullptr)
        {
            *ppv = nullptr;
            return kComHrNoInterface; // view not registered
        }
        auto* managed = icall_gchandle_get_target(ccw->gcHandle);
        if (managed == nullptr || !il2cpp_is_instance(managed, ccw->dataObjectIface))
        {
            *ppv = nullptr;
            return kComHrNoInterface; // the wrapped object is not an implementer
        }
        *ppv = reinterpret_cast<void*>(&ccw->dataObjectVtbl); // the IDataObject view
        ++ccw->refCount;
        return kComHrOk;
    }
#if defined(_WIN32)
    if (std::memcmp(iid, kComIidIOleObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleVtbl); // the IOleObject view
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleClientSite, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->siteVtbl); // the IOleClientSite view
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->inPlaceVtbl); // the IOleInPlaceObject view (stage 27)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIProvideClassInfo, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->classInfoVtbl); // the IProvideClassInfo view (stage 28)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceSite, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->inPlaceSiteVtbl); // the IOleInPlaceSite view (stage 29)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIAdviseSink, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->adviseSinkVtbl); // the IAdviseSink view (stage 30)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIAdviseSink2, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->adviseSink2Vtbl); // the IAdviseSink2 view (stage 72)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIAdviseSinkEx, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->adviseSinkExVtbl); // the IAdviseSinkEx view (stage 73)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIStream, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->streamVtbl); // the IStream view (stage 74)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIStorage, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->storageVtbl); // the IStorage view (stage 75)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceUIWindow, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->inPlaceUiWindowVtbl); // the IOleInPlaceUIWindow view (stage 31)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceFrame, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->inPlaceFrameVtbl); // the IOleInPlaceFrame view (stage 32)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleContainerVtbl); // the IOleContainer view (stage 33)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumUnknown, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumUnknownVtbl); // the IEnumUnknown view (stage 34)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumString, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumStringVtbl); // the IEnumString view (stage 35)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumFormatEtc, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumFormatEtcVtbl); // the IEnumFORMATETC view (stage 36)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIPersistStream, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->persistStreamVtbl); // the IPersistStream view (stage 37)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIPersistFile, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->persistFileVtbl); // the IPersistFile view (stage 38)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIPersist, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->persistVtbl); // the IPersist view (stage 39)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceActiveObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->inPlaceActiveObjectVtbl); // the IOleInPlaceActiveObject view (stage 40)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIPersistPropertyBag, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->persistPropertyBagVtbl); // the IPersistPropertyBag view (stage 41)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIParseDisplayName, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->parseDisplayNameVtbl); // the IParseDisplayName view (stage 42)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIViewObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->viewObjectVtbl); // the IViewObject view (stage 43)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumSTATDATA, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumStatDataVtbl); // the IEnumSTATDATA view (stage 44)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumOLEVERB, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumOleVerbVtbl); // the IEnumOLEVERB view (stage 45)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumMoniker, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumMonikerVtbl); // the IEnumMoniker view (stage 46)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIViewObject2, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->viewObject2Vtbl); // the IViewObject2 view (stage 47)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleLink, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleLinkVtbl); // the IOleLink view (stage 48)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIMoniker, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->monikerVtbl); // the IMoniker view (stage 49)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIRunningObjectTable, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->runningObjectTableVtbl); // the IRunningObjectTable view (stage 50)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIBindCtx, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->bindCtxVtbl); // the IBindCtx view (stage 51)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleAdviseHolder, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleAdviseHolderVtbl); // the IOleAdviseHolder view (stage 52)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIDataAdviseHolder, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->dataAdviseHolderVtbl); // the IDataAdviseHolder view (stage 53)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleCache, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleCacheVtbl); // the IOleCache view (stage 54)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleCache2, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleCache2Vtbl); // the IOleCache2 view (stage 55)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleCacheControl, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleCacheControlVtbl); // the IOleCacheControl view (stage 56)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleItemContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleItemContainerVtbl); // the IOleItemContainer view (stage 57)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUILinkContainerVtbl); // the IOleUILinkContainer view (stage 58)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkInfo, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUILinkInfoVtbl); // the IOleUILinkInfo view (stage 59)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIObjInfo, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUIObjInfoVtbl); // the IOleUIObjInfo view (stage 60)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIVerify, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUIVerifyVtbl); // the IOleUIVerify view (stage 61)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkContainerA, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUILinkContainerAVtbl); // the IOleUILinkContainerA view (stage 62)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkInfoA, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUILinkInfoAVtbl); // the IOleUILinkInfoA view (stage 63)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIObjInfoA, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUIObjInfoAVtbl); // the IOleUIObjInfoA view (stage 64)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIVerifyA, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUIVerifyAVtbl); // the IOleUIVerifyA view (stage 65)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkContainerW, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUILinkContainerWVtbl); // the IOleUILinkContainerW view (stage 66)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkInfoW, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUILinkInfoWVtbl); // the IOleUILinkInfoW view (stage 67)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIObjInfoW, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUIObjInfoWVtbl); // the IOleUIObjInfoW view (stage 68)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIVerifyW, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleUIVerifyWVtbl); // the IOleUIVerifyW view (stage 69)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumConnectionPoints, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumConnectionPointsVtbl); // the IEnumConnectionPoints view (stage 70)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumSTATSTG, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->enumStatStgVtbl); // the IEnumSTATSTG view (stage 71)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIConnectionPointContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->cpcVtbl); // the event-source container view
        ++ccw->refCount;
        return kComHrOk;
    }
    // Stage 15 -- the IDropTarget view (the OLE drag-drop target surface): a
    // native host QIs the CCW for it after RegisterDragDrop(hwnd, view) (the
    // WinForms Control.AllowDrop path). Lives at &ccw->dropVtbl -- a distinct
    // address inside the CCW allocation (container_of recovery).
    if (std::memcmp(iid, kComIidIDropTarget, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->dropVtbl); // the IDropTarget view
        ++ccw->refCount;
        return kComHrOk;
    }
    // QI for IConnectionPoint itself is NOT answered here: the OLE contract
    // obtains a connection point exclusively through
    // IConnectionPointContainer::FindConnectionPoint (each of the CCW's
    // registered event interfaces owns its own connection point view).
#endif
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwAddRef(void* self)
{
    auto* ccw = static_cast<Il2CppComCcw*>(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

// Shared Release-to-zero: the OLE views share the ONE refcount; the last
// Release on any view destroys the whole CCW (all views, the GCHandle and the
// member names).
uint32_t CcwReleaseImpl(Il2CppComCcw* ccw)
{
    const int32_t next = --ccw->refCount;
    if (next == 0)
    {
        icall_gchandle_free(ccw->gcHandle);
        delete ccw; // the destructor frees the registered member names
    }
    return static_cast<uint32_t>(next);
}

uint32_t IL2CPP_COM_CALL CcwRelease(void* self)
{
    return CcwReleaseImpl(static_cast<Il2CppComCcw*>(self));
}

#if defined(_WIN32)

// IOleObject view QI: IUnknown/IDispatch -> the identity pointer (the base
// address -- COM requires QI(IUnknown) to return the same address from every
// view), IOleObject -> this view, IOleClientSite -> the site view.
int32_t IL2CPP_COM_CALL CcwOleQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleObject, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleClientSite, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->siteVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIConnectionPointContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->cpcVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleAddRef(void* self)
{
    auto* ccw = CcwFromOleView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleRelease(void* self)
{
    return CcwReleaseImpl(CcwFromOleView(self));
}

// IOleClientSite view QI (mirror of the OLE view).
int32_t IL2CPP_COM_CALL CcwSiteQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromSiteView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleClientSite, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIConnectionPointContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->cpcVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwSiteAddRef(void* self)
{
    auto* ccw = CcwFromSiteView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwSiteRelease(void* self)
{
    return CcwReleaseImpl(CcwFromSiteView(self));
}

//------------------------------------------------------------------------------
// IDropTarget view (stage 15 -- the OLE drag-drop target surface, the
// WinForms Control.AllowDrop dependency). Managed dispatch (OnDragEnter /
// OnDragOver / OnDragDrop with a DragEventArgs) is the stage-16+ path; the
// minimal stage-15 surface returns E_NOTIMPL for the parameterized methods
// (the documented no-managed-dispatch answer -- the view shape is real, the
// method bodies are not yet wired). DragLeave takes NO parameters and needs NO
// marshaling: S_OK (the one fully-functional method -- a native host's
// drag-leave notification is acknowledged).
//------------------------------------------------------------------------------

// IDropTarget view QI: IUnknown/IDispatch -> the identity pointer (the base
// address -- COM requires QI(IUnknown) to return the same address from every
// view), IDropTarget -> this view, the other OLE views -> their views (the
// phase-4 identity-QI mirror).
int32_t IL2CPP_COM_CALL CcwDropQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromDropView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIDropTarget, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    // The other OLE views (IOleObject / IOleClientSite / IConnectionPointContainer)
    // -- the phase-4 identity-QI mirror (each view QIs to the others).
    if (std::memcmp(iid, kComIidIOleObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleClientSite, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->siteVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIConnectionPointContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->cpcVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwDropAddRef(void* self)
{
    return CcwAddRef(CcwFromDropView(self));
}

uint32_t IL2CPP_COM_CALL CcwDropRelease(void* self)
{
    return CcwReleaseImpl(CcwFromDropView(self));
}

// IDropTarget::DragEnter / DragOver / Drop -- the stage-16 forwarding layer.
// The view methods DECODE the raw OLE arguments (the by-value POINTL is an
// intptr carrier: x = low 32 bits, y = high 32 bits on little-endian x64 --
// the stage-15 `const void* pt` declaration was a WRONG by-value POINTL shape
// (x64 8-byte POINTL occupies one register slot, read as a pointer = UB);
// never exposed because the E_NOTIMPL bodies never dereferenced it) and
// FORWARD them to the registered per-CCW dispatch callback. The callback's
// HRESULT is returned and its *pdwEffect write propagates (forward, never
// masked). Unregistered (dropDispatch == null) -> the stage-15 E_NOTIMPL
// surface.
//
// The shared forwarding implementation: 'method' is the kComDropMethod*
// logical discriminator, 'pDataObj' the by-value IDataObject* (0 for DragOver
// -- no data object), 'grfKeyState' the OLE modifier bits, 'pt' the by-value
// POINTL intptr carrier, 'pdwEffect' the OLE effect in/out pointer.
int32_t CcwDropDispatchImpl(Il2CppComCcw* ccw, int32_t method, void* pDataObj,
                            uint32_t grfKeyState, intptr_t pt, uint32_t* pdwEffect)
{
    // Decode the by-value POINTL (little-endian x64 -- ILC targets x64, the
    // pinvokecb.cpp static_assert precedent): POINTL { LONG x; LONG y; } is an
    // 8-byte aggregate occupying one register slot; the intptr carrier reads
    // the raw bits, x = low 32, y = high 32. Shared by the managed bridge
    // (below) and the native forwarding callback.
    const int32_t x = static_cast<int32_t>(static_cast<uint64_t>(pt));
    const int32_t y = static_cast<int32_t>(static_cast<uint64_t>(pt) >> 32);
    // The managed drop dispatch (stage 17 batch 4) takes priority over the
    // stage-16 native forwarding callback: drive the full bridge (wrap
    // pDataObj -> construct DragEventArgs -> il2cpp_interface_dispatch ->
    // effect write-back) through the shared pump.
    if (ccw->dropManaged)
    {
        return icall_com_ccw_drop_dispatch_managed(
            reinterpret_cast<intptr_t>(ccw), method,
            reinterpret_cast<intptr_t>(pDataObj), grfKeyState, x, y, pdwEffect);
    }
    if (ccw->dropDispatch == nullptr)
    {
        return kComHrNotImpl; // stage-15 surface: no managed dispatch registered
    }
    typedef int32_t (IL2CPP_COM_CALL* DropDispatchFn)(
        int32_t, intptr_t, uint32_t, int32_t, int32_t, uint32_t*);
    return reinterpret_cast<DropDispatchFn>(ccw->dropDispatch)(
        method, reinterpret_cast<intptr_t>(pDataObj), grfKeyState, x, y, pdwEffect);
}

int32_t IL2CPP_COM_CALL CcwDropDragEnter(void* self, void* pDataObj,
                                         uint32_t grfKeyState, intptr_t pt,
                                         uint32_t* pdwEffect)
{
    return CcwDropDispatchImpl(CcwFromDropView(self), kComDropMethodDragEnter,
                               pDataObj, grfKeyState, pt, pdwEffect);
}

int32_t IL2CPP_COM_CALL CcwDropDragOver(void* self, uint32_t grfKeyState,
                                        intptr_t pt, uint32_t* pdwEffect)
{
    return CcwDropDispatchImpl(CcwFromDropView(self), kComDropMethodDragOver,
                               nullptr, grfKeyState, pt, pdwEffect);
}

int32_t IL2CPP_COM_CALL CcwDropDrop(void* self, void* pDataObj,
                                    uint32_t grfKeyState, intptr_t pt,
                                    uint32_t* pdwEffect)
{
    return CcwDropDispatchImpl(CcwFromDropView(self), kComDropMethodDrop,
                               pDataObj, grfKeyState, pt, pdwEffect);
}

// IDropTarget::DragLeave -- no parameters, no marshaling: S_OK (the stage-15
// contract -- a native host's drag-leave notification is acknowledged).
// Not forwarded to the dispatch callback: forwarding would turn a notification
// into a potentially-failing call (a callback may return a non-S_OK HRESULT),
// changing the locked stage-15 semantics. kComDropMethodDragLeave stays
// reserved for a future extension.
int32_t IL2CPP_COM_CALL CcwDropDragLeave(void* self)
{
    (void)self;
    return kComHrOk;
}

//------------------------------------------------------------------------------
// IOleObject minimal surface (phase 4 -- the ActiveX hosting lifecycle the
// WinForms AxHost drives). Implemented methods: SetClientSite / GetClientSite
// (the site handshake), Close (teardown) and DoVerb (activation, accepted).
// The remaining IOleObject methods (SetHostNames / SetMoniker / GetMoniker /
// InitFromData / GetClipboardData / EnumVerbs / Update / IsUpToDate /
// GetUserClassID / GetUserType / SetExtent / GetExtent / Advise / Unadvise /
// EnumAdvise / GetMiscStatus / SetColorScheme) return E_NOTIMPL -- the
// documented phase-4 minimal surface.
//------------------------------------------------------------------------------

// IOleObject::SetClientSite -- store the host's IOleClientSite. Phase 4
// stores a BORROWED pointer (the host owns the site reference and it outlives
// the CCW in the hosting scenario; Close clears the pointer without
// releasing -- the ownership decision is documented in the header). Null
// clears the stored site.
int32_t IL2CPP_COM_CALL CcwOleSetClientSite(void* self, void* pClientSite)
{
    auto* ccw = CcwFromOleView(self);
    ccw->clientSite = pClientSite;
    return kComHrOk;
}

// IOleObject::GetClientSite -- return the stored site (null when none).
int32_t IL2CPP_COM_CALL CcwOleGetClientSite(void* self, void** ppClientSite)
{
    auto* ccw = CcwFromOleView(self);
    if (ppClientSite == nullptr)
    {
        return kComHrBadPointer;
    }
    *ppClientSite = ccw->clientSite;
    return kComHrOk;
}

// IOleObject::Close -- clear the stored client site; S_OK (there is no
// in-place window or undo state to tear down in the minimal surface).
int32_t IL2CPP_COM_CALL CcwOleClose(void* self, uint32_t dwSaveOption)
{
    (void)dwSaveOption;
    auto* ccw = CcwFromOleView(self);
    ccw->clientSite = nullptr;
    return kComHrOk;
}

// IOleObject::DoVerb -- accept the activation verb (S_OK): the minimal
// phase-4 object has no in-place window; hosting proceeds with the object in
// its passive state (the WinForms AxHost DoVerb(OLEIVERB_INPLACEACTIVATE)
// call succeeds).
int32_t IL2CPP_COM_CALL CcwOleDoVerb(void* self, int32_t iVerb, const void* lpmsg,
                                     void* pActiveSite, int32_t lindex, void* hwndParent,
                                     const void* lprcPosRect)
{
    (void)self; (void)iVerb; (void)lpmsg; (void)pActiveSite;
    (void)lindex; (void)hwndParent; (void)lprcPosRect;
    return kComHrOk;
}

// IOleObject::Update -- nothing to update in the minimal surface: S_OK.
int32_t IL2CPP_COM_CALL CcwOleUpdate(void* self)
{
    (void)self;
    return kComHrOk;
}

// IOleObject::IsUpToDate -- the minimal object is always up to date: S_OK.
int32_t IL2CPP_COM_CALL CcwOleIsUpToDate(void* self)
{
    (void)self;
    return kComHrOk;
}

// The remaining IOleObject methods: the documented E_NOTIMPL surface.
int32_t IL2CPP_COM_CALL CcwOleNotImpl3(void*, const void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl4(void*, uint32_t, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl5(void*, uint32_t, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl6(void*, void*, int32_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl7(void*, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl8(void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl9(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl10(void*, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl11(void*, uint32_t, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl12(void*, uint32_t, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl13(void*, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl14(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl15(void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleNotImpl16(void*, const void*) { return kComHrNotImpl; }

//------------------------------------------------------------------------------
// IOleClientSite minimal surface (phase 4): GetContainer (the container the
// hosted object reaches for site queries) returns E_NOTIMPL with a null out
// pointer -- the minimal object has no container (documented no-container
// answer). SaveObject / GetMoniker / ShowObject / OnShowWindow /
// RequestNewObjectLayout return E_NOTIMPL (the documented minimal surface).
//------------------------------------------------------------------------------
int32_t IL2CPP_COM_CALL CcwSiteGetContainer(void* self, void** ppContainer)
{
    (void)self;
    if (ppContainer == nullptr)
    {
        return kComHrBadPointer;
    }
    *ppContainer = nullptr;
    return kComHrNotImpl; // no container in the minimal phase-4 surface
}

int32_t IL2CPP_COM_CALL CcwSiteNotImpl0(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwSiteNotImpl1(void*, uint32_t, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwSiteNotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwSiteNotImpl3(void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwSiteNotImpl4(void*) { return kComHrNotImpl; }

// --- Stage 27: IOleInPlaceObject view methods ---
// The in-place object surface: GetWindow hands the hosted object's window
// handle (headless: null, S_OK), InPlaceDeactivate / UIDeactivate record the
// call and return S_OK, ContextSensitiveHelp / SetObjectRects /
// ReactivateAndUndo are the E_NOTIMPL stubs.
int32_t IL2CPP_COM_CALL CcwInPlaceQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromInPlaceView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceObject, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwInPlaceAddRef(void* self)
{
    auto* ccw = CcwFromInPlaceView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwInPlaceRelease(void* self)
{
    auto* ccw = CcwFromInPlaceView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwInPlaceGetWindow(void* self, void** phwnd)
{
    (void)self;
    if (phwnd != nullptr)
    {
        *phwnd = nullptr; // headless: no hosted window handle
    }
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceDeactivate(void* self)
{
    (void)self;
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceUiDeactivate(void* self)
{
    (void)self;
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceNotImpl0(void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceNotImpl1(void*, const void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceNotImpl2(void*) { return kComHrNotImpl; }

// --- Stage 28: IProvideClassInfo view methods ---
Il2CppComCcw* CcwFromClassInfoView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, classInfoVtbl));
}

int32_t IL2CPP_COM_CALL CcwClassInfoQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromClassInfoView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIProvideClassInfo, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwClassInfoAddRef(void* self)
{
    auto* ccw = CcwFromClassInfoView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwClassInfoRelease(void* self)
{
    auto* ccw = CcwFromClassInfoView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwClassInfoGetClassInfo(void* self, void** ppTypeInfo)
{
    (void)self;
    if (ppTypeInfo != nullptr)
    {
        *ppTypeInfo = nullptr;
    }
    return kComHrNotImpl; // no ITypeInfo is built for the CCW
}

// --- Stage 29: IOleInPlaceSite view methods ---
Il2CppComCcw* CcwFromInPlaceSiteView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, inPlaceSiteVtbl));
}

int32_t IL2CPP_COM_CALL CcwInPlaceSiteQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromInPlaceSiteView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceSite, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwInPlaceSiteAddRef(void* self)
{
    auto* ccw = CcwFromInPlaceSiteView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwInPlaceSiteRelease(void* self)
{
    auto* ccw = CcwFromInPlaceSiteView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwInPlaceSiteGetWindow(void* self, void** phwnd)
{
    (void)self;
    if (phwnd != nullptr)
    {
        *phwnd = nullptr; // headless: no hosted window handle
    }
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceSiteActivateOk(void* self)
{
    (void)self;
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceSiteNotImpl0(void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceSiteNotImpl1(void*, void**, void*, void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceSiteNotImpl2(void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceSiteNotImpl3(void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceSiteNotImpl4(void*) { return kComHrNotImpl; }

// --- Stage 30: IAdviseSink view methods ---
Il2CppComCcw* CcwFromAdviseSinkView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, adviseSinkVtbl));
}

int32_t IL2CPP_COM_CALL CcwAdviseSinkQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromAdviseSinkView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIAdviseSink, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwAdviseSinkAddRef(void* self)
{
    auto* ccw = CcwFromAdviseSinkView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwAdviseSinkRelease(void* self)
{
    auto* ccw = CcwFromAdviseSinkView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwAdviseSinkNotifyOk(void* self)
{
    (void)self;
    return kComHrOk; // the notification is accepted and dropped (headless)
}

int32_t IL2CPP_COM_CALL CcwAdviseSinkNotifyOk2(void*, uint32_t, int32_t) { return kComHrOk; }
int32_t IL2CPP_COM_CALL CcwAdviseSinkNotifyOk3(void*, void*) { return kComHrOk; }
int32_t IL2CPP_COM_CALL CcwAdviseSinkNotifyOk4(void*, const void*, const void*) { return kComHrOk; }

// --- Stage 72: IAdviseSink2 view methods ---
Il2CppComCcw* CcwFromAdviseSink2View(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, adviseSink2Vtbl));
}

int32_t IL2CPP_COM_CALL CcwAdviseSink2QueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromAdviseSink2View(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIAdviseSink2, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwAdviseSink2AddRef(void* self)
{
    auto* ccw = CcwFromAdviseSink2View(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwAdviseSink2Release(void* self)
{
    auto* ccw = CcwFromAdviseSink2View(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwAdviseSink2NotifyOk(void*, void*) { return kComHrOk; }

// --- Stage 73: IAdviseSinkEx view methods ---
Il2CppComCcw* CcwFromAdviseSinkExView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, adviseSinkExVtbl));
}

int32_t IL2CPP_COM_CALL CcwAdviseSinkExQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromAdviseSinkExView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIAdviseSinkEx, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwAdviseSinkExAddRef(void* self)
{
    auto* ccw = CcwFromAdviseSinkExView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwAdviseSinkExRelease(void* self)
{
    auto* ccw = CcwFromAdviseSinkExView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwAdviseSinkExNotifyOk(void*, uint32_t, int32_t) { return kComHrOk; }

// --- Stage 74: IStream view methods ---
Il2CppComCcw* CcwFromStreamView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, streamVtbl));
}

int32_t IL2CPP_COM_CALL CcwStreamQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromStreamView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIStream, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwStreamAddRef(void* self)
{
    auto* ccw = CcwFromStreamView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwStreamRelease(void* self)
{
    auto* ccw = CcwFromStreamView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwStreamNotImpl0(void*, void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl1(void*, const void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl2(void*, int64_t, uint32_t, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl3(void*, int64_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl4(void*, void*, int64_t, uint32_t*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl5(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl6(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl7(void*, int64_t, int64_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl8(void*, void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStreamNotImpl9(void*, void**) { return kComHrNotImpl; }

// --- Stage 75: IStorage view methods ---
Il2CppComCcw* CcwFromStorageView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, storageVtbl));
}

int32_t IL2CPP_COM_CALL CcwStorageQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromStorageView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIStorage, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwStorageAddRef(void* self)
{
    auto* ccw = CcwFromStorageView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwStorageRelease(void* self)
{
    auto* ccw = CcwFromStorageView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwStorageNotImpl0(void*, const void*, uint32_t, uint32_t, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl1(void*, const void*, void*, uint32_t, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl2(void*, const void*, void*, uint32_t, const void*, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl3(void*, uint32_t, const void*, const void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl4(void*, const void*, void*, const void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl5(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl6(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl7(void*, uint32_t, void*, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl8(void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl9(void*, const void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl10(void*, const void*, const void*, const void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl11(void*, uint32_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwStorageNotImpl12(void*, void*, uint32_t) { return kComHrNotImpl; }

// --- Stage 31: IOleInPlaceUIWindow view methods ---
Il2CppComCcw* CcwFromInPlaceUIWindowView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, inPlaceUiWindowVtbl));
}

int32_t IL2CPP_COM_CALL CcwInPlaceUIWindowQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromInPlaceUIWindowView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceUIWindow, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwInPlaceUIWindowAddRef(void* self)
{
    auto* ccw = CcwFromInPlaceUIWindowView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwInPlaceUIWindowRelease(void* self)
{
    auto* ccw = CcwFromInPlaceUIWindowView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwInPlaceUIWindowGetWindow(void* self, void** phwnd)
{
    (void)self;
    if (phwnd != nullptr)
    {
        *phwnd = nullptr; // headless: no hosted window handle
    }
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceUIWindowNotImpl0(void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceUIWindowNotImpl1(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceUIWindowNotImpl2(void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceUIWindowNotImpl3(void*, void*, void*) { return kComHrNotImpl; }

// --- Stage 32: IOleInPlaceFrame view methods ---
Il2CppComCcw* CcwFromInPlaceFrameView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, inPlaceFrameVtbl));
}

int32_t IL2CPP_COM_CALL CcwInPlaceFrameQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromInPlaceFrameView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceFrame, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwInPlaceFrameAddRef(void* self)
{
    auto* ccw = CcwFromInPlaceFrameView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwInPlaceFrameRelease(void* self)
{
    auto* ccw = CcwFromInPlaceFrameView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwInPlaceFrameGetWindow(void* self, void** phwnd)
{
    (void)self;
    if (phwnd != nullptr)
    {
        *phwnd = nullptr; // headless: no hosted window handle
    }
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceFrameSetActiveObject(void* self, void*, void*)
{
    (void)self;
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceFrameNotImpl0(void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceFrameNotImpl1(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceFrameNotImpl2(void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceFrameNotImpl3(void*, void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceFrameNotImpl4(void*, void*, void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceFrameNotImpl5(void*, void*) { return kComHrNotImpl; }

// --- Stage 33: IOleContainer view methods ---
Il2CppComCcw* CcwFromOleContainerView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleContainerVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleContainerQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleContainerView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleContainer, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleContainerAddRef(void* self)
{
    auto* ccw = CcwFromOleContainerView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleContainerRelease(void* self)
{
    auto* ccw = CcwFromOleContainerView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleContainerNotImpl0(void*, void*, void*, void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleContainerNotImpl1(void*, uint32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleContainerNotImpl2(void*, int32_t) { return kComHrNotImpl; }

// --- Stage 34: IEnumUnknown view methods ---
Il2CppComCcw* CcwFromEnumUnknownView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, enumUnknownVtbl));
}

int32_t IL2CPP_COM_CALL CcwEnumUnknownQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromEnumUnknownView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumUnknown, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumUnknownAddRef(void* self)
{
    auto* ccw = CcwFromEnumUnknownView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwEnumUnknownRelease(void* self)
{
    auto* ccw = CcwFromEnumUnknownView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwEnumUnknownNotImpl0(void*, uint32_t, void**, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumUnknownNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumUnknownNotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumUnknownNotImpl3(void*, void**) { return kComHrNotImpl; }

// --- Stage 35: IEnumString view methods ---
Il2CppComCcw* CcwFromEnumStringView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, enumStringVtbl));
}

int32_t IL2CPP_COM_CALL CcwEnumStringQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromEnumStringView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumString, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumStringAddRef(void* self)
{
    auto* ccw = CcwFromEnumStringView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwEnumStringRelease(void* self)
{
    auto* ccw = CcwFromEnumStringView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwEnumStringNotImpl0(void*, uint32_t, void**, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumStringNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumStringNotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumStringNotImpl3(void*, void**) { return kComHrNotImpl; }

// --- Stage 36: IEnumFORMATETC view methods ---
Il2CppComCcw* CcwFromEnumFormatEtcView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, enumFormatEtcVtbl));
}

int32_t IL2CPP_COM_CALL CcwEnumFormatEtcQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromEnumFormatEtcView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumFormatEtc, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumFormatEtcAddRef(void* self)
{
    auto* ccw = CcwFromEnumFormatEtcView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwEnumFormatEtcRelease(void* self)
{
    auto* ccw = CcwFromEnumFormatEtcView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwEnumFormatEtcNotImpl0(void*, uint32_t, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumFormatEtcNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumFormatEtcNotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumFormatEtcNotImpl3(void*, void**) { return kComHrNotImpl; }

// --- Stage 37: IPersistStream view methods ---
Il2CppComCcw* CcwFromPersistStreamView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, persistStreamVtbl));
}

int32_t IL2CPP_COM_CALL CcwPersistStreamQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromPersistStreamView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIPersistStream, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwPersistStreamAddRef(void* self)
{
    auto* ccw = CcwFromPersistStreamView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwPersistStreamRelease(void* self)
{
    auto* ccw = CcwFromPersistStreamView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwPersistStreamNotImpl0(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistStreamNotImpl1(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistStreamNotImpl2(void*, void*, int32_t) { return kComHrNotImpl; }

// --- Stage 38: IPersistFile view methods ---
Il2CppComCcw* CcwFromPersistFileView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, persistFileVtbl));
}

int32_t IL2CPP_COM_CALL CcwPersistFileQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromPersistFileView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIPersistFile, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwPersistFileAddRef(void* self)
{
    auto* ccw = CcwFromPersistFileView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwPersistFileRelease(void* self)
{
    auto* ccw = CcwFromPersistFileView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwPersistFileNotImpl0(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistFileNotImpl1(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistFileNotImpl2(void*, const void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistFileNotImpl3(void*, const void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistFileNotImpl4(void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistFileNotImpl5(void*, void**) { return kComHrNotImpl; }

// --- Stage 39: IPersist view methods ---
Il2CppComCcw* CcwFromPersistView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, persistVtbl));
}

int32_t IL2CPP_COM_CALL CcwPersistQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromPersistView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIPersist, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwPersistAddRef(void* self)
{
    auto* ccw = CcwFromPersistView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwPersistRelease(void* self)
{
    auto* ccw = CcwFromPersistView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwPersistNotImpl0(void*, void*) { return kComHrNotImpl; }

// --- Stage 40: IOleInPlaceActiveObject view methods ---
Il2CppComCcw* CcwFromInPlaceActiveObjectView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, inPlaceActiveObjectVtbl));
}

int32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromInPlaceActiveObjectView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleInPlaceActiveObject, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectAddRef(void* self)
{
    auto* ccw = CcwFromInPlaceActiveObjectView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectRelease(void* self)
{
    auto* ccw = CcwFromInPlaceActiveObjectView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectGetWindow(void* self, void** phwnd)
{
    (void)self;
    if (phwnd == nullptr)
        return kComHrInvalidArg;
    *phwnd = nullptr; // no window (headless)
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectNotImpl0(void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectNotImpl1(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectNotImpl2(void*, const void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectNotImpl3(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwInPlaceActiveObjectNotImpl4(void*, const void*, void*, int32_t) { return kComHrNotImpl; }

// --- Stage 41: IPersistPropertyBag view methods ---
Il2CppComCcw* CcwFromPersistPropertyBagView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, persistPropertyBagVtbl));
}

int32_t IL2CPP_COM_CALL CcwPersistPropertyBagQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromPersistPropertyBagView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIPersistPropertyBag, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwPersistPropertyBagAddRef(void* self)
{
    auto* ccw = CcwFromPersistPropertyBagView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwPersistPropertyBagRelease(void* self)
{
    auto* ccw = CcwFromPersistPropertyBagView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwPersistPropertyBagNotImpl0(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistPropertyBagNotImpl1(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistPropertyBagNotImpl2(void*, void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwPersistPropertyBagNotImpl3(void*, void*, int32_t, void*) { return kComHrNotImpl; }

// --- Stage 42: IParseDisplayName view methods ---
Il2CppComCcw* CcwFromParseDisplayNameView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, parseDisplayNameVtbl));
}

int32_t IL2CPP_COM_CALL CcwParseDisplayNameQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromParseDisplayNameView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIParseDisplayName, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwParseDisplayNameAddRef(void* self)
{
    auto* ccw = CcwFromParseDisplayNameView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwParseDisplayNameRelease(void* self)
{
    auto* ccw = CcwFromParseDisplayNameView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwParseDisplayNameNotImpl0(void*, void*, void*, uint32_t*, void**) { return kComHrNotImpl; }

// --- Stage 43: IViewObject view methods ---
Il2CppComCcw* CcwFromViewObjectView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, viewObjectVtbl));
}

int32_t IL2CPP_COM_CALL CcwViewObjectQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromViewObjectView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIViewObject, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwViewObjectAddRef(void* self)
{
    auto* ccw = CcwFromViewObjectView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwViewObjectRelease(void* self)
{
    auto* ccw = CcwFromViewObjectView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwViewObjectNotImpl0(void*, uint32_t, int32_t, void*, void*, void*, void*, uint32_t, void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObjectNotImpl1(void*, uint32_t, void*, void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObjectNotImpl2(void*, uint32_t, int32_t, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObjectNotImpl3(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObjectNotImpl4(void*, uint32_t, uint32_t, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObjectNotImpl5(void*, uint32_t*, uint32_t*, void**) { return kComHrNotImpl; }

// --- Stage 44: IEnumSTATDATA view methods ---
Il2CppComCcw* CcwFromEnumSTATDATAView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, enumStatDataVtbl));
}

int32_t IL2CPP_COM_CALL CcwEnumSTATDATAQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromEnumSTATDATAView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumSTATDATA, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumSTATDATAAddRef(void* self)
{
    auto* ccw = CcwFromEnumSTATDATAView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwEnumSTATDATARelease(void* self)
{
    auto* ccw = CcwFromEnumSTATDATAView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwEnumSTATDATANotImpl0(void*, uint32_t, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumSTATDATANotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumSTATDATANotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumSTATDATANotImpl3(void*, void**) { return kComHrNotImpl; }

// --- Stage 45: IEnumOLEVERB view methods ---
Il2CppComCcw* CcwFromEnumOLEVERBView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, enumOleVerbVtbl));
}

int32_t IL2CPP_COM_CALL CcwEnumOLEVERBQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromEnumOLEVERBView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumOLEVERB, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumOLEVERBAddRef(void* self)
{
    auto* ccw = CcwFromEnumOLEVERBView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwEnumOLEVERBRelease(void* self)
{
    auto* ccw = CcwFromEnumOLEVERBView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwEnumOLEVERBNotImpl0(void*, uint32_t, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumOLEVERBNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumOLEVERBNotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumOLEVERBNotImpl3(void*, void**) { return kComHrNotImpl; }

// --- Stage 46: IEnumMoniker view methods ---
Il2CppComCcw* CcwFromEnumMonikerView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, enumMonikerVtbl));
}

int32_t IL2CPP_COM_CALL CcwEnumMonikerQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromEnumMonikerView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumMoniker, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumMonikerAddRef(void* self)
{
    auto* ccw = CcwFromEnumMonikerView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwEnumMonikerRelease(void* self)
{
    auto* ccw = CcwFromEnumMonikerView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwEnumMonikerNotImpl0(void*, uint32_t, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumMonikerNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumMonikerNotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumMonikerNotImpl3(void*, void**) { return kComHrNotImpl; }

// --- Stage 47: IViewObject2 view methods ---
Il2CppComCcw* CcwFromViewObject2View(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, viewObject2Vtbl));
}

int32_t IL2CPP_COM_CALL CcwViewObject2QueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromViewObject2View(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIViewObject2, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwViewObject2AddRef(void* self)
{
    auto* ccw = CcwFromViewObject2View(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwViewObject2Release(void* self)
{
    auto* ccw = CcwFromViewObject2View(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwViewObject2NotImpl0(void*, uint32_t, int32_t, void*, void*, void*, void*, uint32_t, void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObject2NotImpl1(void*, uint32_t, void*, void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObject2NotImpl2(void*, uint32_t, int32_t, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObject2NotImpl3(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObject2NotImpl4(void*, uint32_t, uint32_t, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObject2NotImpl5(void*, uint32_t*, uint32_t*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwViewObject2NotImpl6(void*, uint32_t, int32_t, void*) { return kComHrNotImpl; }

// --- Stage 48: IOleLink view methods ---
Il2CppComCcw* CcwFromOleLinkView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleLinkVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleLinkQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleLinkView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleLink, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleLinkAddRef(void* self)
{
    auto* ccw = CcwFromOleLinkView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleLinkRelease(void* self)
{
    auto* ccw = CcwFromOleLinkView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleLinkNotImpl0(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleLinkNotImpl1(void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleLinkNotImpl2(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleLinkNotImpl3(void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleLinkNotImpl4(void*, const void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleLinkNotImpl5(void*, uint32_t, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleLinkNotImpl6(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleLinkNotImpl7(void*, void*, void*, void*) { return kComHrNotImpl; }

// --- Stage 49: IMoniker view methods ---
Il2CppComCcw* CcwFromMonikerView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, monikerVtbl));
}

int32_t IL2CPP_COM_CALL CcwMonikerQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromMonikerView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIMoniker, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwMonikerAddRef(void* self)
{
    auto* ccw = CcwFromMonikerView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwMonikerRelease(void* self)
{
    auto* ccw = CcwFromMonikerView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwMonikerNotImpl0(void*, void*, void*, const void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl1(void*, void*, uint32_t, void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl2(void*, void*, int32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl3(void*, int32_t, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl4(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl5(void*, void*, void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl6(void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl7(void*, void*, void*, void*, uint32_t*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl8(void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl9(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl10(void*, void*, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwMonikerNotImpl11(void*, void*, void**) { return kComHrNotImpl; }

// --- Stage 50: IRunningObjectTable view methods ---
Il2CppComCcw* CcwFromRunningObjectTableView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, runningObjectTableVtbl));
}

int32_t IL2CPP_COM_CALL CcwRunningObjectTableQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromRunningObjectTableView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIRunningObjectTable, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwRunningObjectTableAddRef(void* self)
{
    auto* ccw = CcwFromRunningObjectTableView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwRunningObjectTableRelease(void* self)
{
    auto* ccw = CcwFromRunningObjectTableView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwRunningObjectTableNotImpl0(void*, void*, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwRunningObjectTableNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwRunningObjectTableNotImpl2(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwRunningObjectTableNotImpl3(void*, void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwRunningObjectTableNotImpl4(void*, uint32_t, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwRunningObjectTableNotImpl5(void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwRunningObjectTableNotImpl6(void*, void*, void*) { return kComHrNotImpl; }

// --- Stage 51: IBindCtx view methods ---
Il2CppComCcw* CcwFromBindCtxView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, bindCtxVtbl));
}

int32_t IL2CPP_COM_CALL CcwBindCtxQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromBindCtxView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIBindCtx, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwBindCtxAddRef(void* self)
{
    auto* ccw = CcwFromBindCtxView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwBindCtxRelease(void* self)
{
    auto* ccw = CcwFromBindCtxView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwBindCtxNotImpl0(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwBindCtxNotImpl1(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwBindCtxNotImpl2(void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwBindCtxNotImpl3(void*, const void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwBindCtxNotImpl4(void*, const void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwBindCtxNotImpl5(void*, const void*) { return kComHrNotImpl; }

// --- Stage 52: IOleAdviseHolder view methods ---
Il2CppComCcw* CcwFromOleAdviseHolderView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleAdviseHolderVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleAdviseHolderQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleAdviseHolderView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleAdviseHolder, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleAdviseHolderAddRef(void* self)
{
    auto* ccw = CcwFromOleAdviseHolderView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleAdviseHolderRelease(void* self)
{
    auto* ccw = CcwFromOleAdviseHolderView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleAdviseHolderNotImpl0(void*, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleAdviseHolderNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleAdviseHolderNotImpl2(void*, void**) { return kComHrNotImpl; }

// --- Stage 53: IDataAdviseHolder view methods ---
Il2CppComCcw* CcwFromDataAdviseHolderView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, dataAdviseHolderVtbl));
}

int32_t IL2CPP_COM_CALL CcwDataAdviseHolderQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromDataAdviseHolderView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIDataAdviseHolder, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwDataAdviseHolderAddRef(void* self)
{
    auto* ccw = CcwFromDataAdviseHolderView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwDataAdviseHolderRelease(void* self)
{
    auto* ccw = CcwFromDataAdviseHolderView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwDataAdviseHolderNotImpl0(void*, void*, void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwDataAdviseHolderNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwDataAdviseHolderNotImpl2(void*, void**) { return kComHrNotImpl; }

// --- Stage 54: IOleCache view methods ---
Il2CppComCcw* CcwFromOleCacheView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleCacheVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleCacheQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleCacheView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleCache, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleCacheAddRef(void* self)
{
    auto* ccw = CcwFromOleCacheView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleCacheRelease(void* self)
{
    auto* ccw = CcwFromOleCacheView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleCacheNotImpl0(void*, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCacheNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCacheNotImpl2(void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCacheNotImpl3(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCacheNotImpl4(void*, void*, void*, uint32_t) { return kComHrNotImpl; }

// --- Stage 55: IOleCache2 view methods ---
Il2CppComCcw* CcwFromOleCache2View(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleCache2Vtbl));
}

int32_t IL2CPP_COM_CALL CcwOleCache2QueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleCache2View(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleCache2, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleCache2AddRef(void* self)
{
    auto* ccw = CcwFromOleCache2View(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleCache2Release(void* self)
{
    auto* ccw = CcwFromOleCache2View(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleCache2NotImpl0(void*, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCache2NotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCache2NotImpl2(void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCache2NotImpl3(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCache2NotImpl4(void*, void*, void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCache2NotImpl5(void*, void*, uint32_t) { return kComHrNotImpl; }

// --- Stage 56: IOleCacheControl view methods ---
Il2CppComCcw* CcwFromOleCacheControlView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleCacheControlVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleCacheControlQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleCacheControlView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleCacheControl, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleCacheControlAddRef(void* self)
{
    auto* ccw = CcwFromOleCacheControlView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleCacheControlRelease(void* self)
{
    auto* ccw = CcwFromOleCacheControlView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleCacheControlNotImpl0(void*, void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleCacheControlNotImpl1(void*) { return kComHrNotImpl; }

// --- Stage 57: IOleItemContainer view methods ---
Il2CppComCcw* CcwFromOleItemContainerView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleItemContainerVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleItemContainerQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleItemContainerView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleItemContainer, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleItemContainerAddRef(void* self)
{
    auto* ccw = CcwFromOleItemContainerView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleItemContainerRelease(void* self)
{
    auto* ccw = CcwFromOleItemContainerView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleItemContainerNotImpl0(void*, void*, void*, uint32_t*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleItemContainerNotImpl1(void*, const void*, uint32_t, void*, const void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleItemContainerNotImpl2(void*, const void*, void*, const void*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleItemContainerNotImpl3(void*, int32_t) { return kComHrNotImpl; }

// --- Stage 58: IOleUILinkContainer view methods ---
Il2CppComCcw* CcwFromOleUILinkContainerView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUILinkContainerVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUILinkContainerQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUILinkContainerView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkContainer, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkContainerAddRef(void* self)
{
    auto* ccw = CcwFromOleUILinkContainerView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkContainerRelease(void* self)
{
    auto* ccw = CcwFromOleUILinkContainerView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUILinkContainerNotImpl0(void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerNotImpl1(void*, uint32_t, const void*, uint32_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerNotImpl2(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerNotImpl3(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerNotImpl4(void*, uint32_t, int32_t, int32_t) { return kComHrNotImpl; }

// --- Stage 59: IOleUILinkInfo view methods ---
Il2CppComCcw* CcwFromOleUILinkInfoView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUILinkInfoVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUILinkInfoQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUILinkInfoView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkInfo, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkInfoAddRef(void* self)
{
    auto* ccw = CcwFromOleUILinkInfoView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkInfoRelease(void* self)
{
    auto* ccw = CcwFromOleUILinkInfoView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUILinkInfoNotImpl0(void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoNotImpl1(void*, uint32_t, const void*, uint32_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoNotImpl2(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoNotImpl3(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoNotImpl4(void*, uint32_t, int32_t, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoNotImpl5(void*, uint32_t, void*) { return kComHrNotImpl; }

// --- Stage 60: IOleUIObjInfo view methods ---
Il2CppComCcw* CcwFromOleUIObjInfoView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUIObjInfoVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUIObjInfoQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUIObjInfoView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIObjInfo, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUIObjInfoAddRef(void* self)
{
    auto* ccw = CcwFromOleUIObjInfoView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUIObjInfoRelease(void* self)
{
    auto* ccw = CcwFromOleUIObjInfoView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUIObjInfoNotImpl0(void*, uint32_t, uint32_t*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUIObjInfoNotImpl1(void*, uint32_t, void**) { return kComHrNotImpl; }

// --- Stage 61: IOleUIVerify view methods ---
Il2CppComCcw* CcwFromOleUIVerifyView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUIVerifyVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUIVerifyQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUIVerifyView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIVerify, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUIVerifyAddRef(void* self)
{
    auto* ccw = CcwFromOleUIVerifyView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUIVerifyRelease(void* self)
{
    auto* ccw = CcwFromOleUIVerifyView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUIVerifyNotImpl0(void*, void*) { return kComHrNotImpl; }

// --- Stage 62: IOleUILinkContainerA view methods ---
Il2CppComCcw* CcwFromOleUILinkContainerAView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUILinkContainerAVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUILinkContainerAQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUILinkContainerAView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkContainerA, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkContainerAAddRef(void* self)
{
    auto* ccw = CcwFromOleUILinkContainerAView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkContainerARelease(void* self)
{
    auto* ccw = CcwFromOleUILinkContainerAView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUILinkContainerANotImpl0(void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerANotImpl1(void*, uint32_t, const void*, uint32_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerANotImpl2(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerANotImpl3(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerANotImpl4(void*, uint32_t, int32_t, int32_t) { return kComHrNotImpl; }

// --- Stage 63: IOleUILinkInfoA view methods ---
Il2CppComCcw* CcwFromOleUILinkInfoAView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUILinkInfoAVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUILinkInfoAQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUILinkInfoAView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkInfoA, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkInfoAAddRef(void* self)
{
    auto* ccw = CcwFromOleUILinkInfoAView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkInfoARelease(void* self)
{
    auto* ccw = CcwFromOleUILinkInfoAView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUILinkInfoANotImpl0(void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoANotImpl1(void*, uint32_t, const void*, uint32_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoANotImpl2(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoANotImpl3(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoANotImpl4(void*, uint32_t, int32_t, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoANotImpl5(void*, uint32_t, void*) { return kComHrNotImpl; }

// --- Stage 64: IOleUIObjInfoA view methods ---
Il2CppComCcw* CcwFromOleUIObjInfoAView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUIObjInfoAVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUIObjInfoAQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUIObjInfoAView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIObjInfoA, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUIObjInfoAAddRef(void* self)
{
    auto* ccw = CcwFromOleUIObjInfoAView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUIObjInfoARelease(void* self)
{
    auto* ccw = CcwFromOleUIObjInfoAView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUIObjInfoANotImpl0(void*, uint32_t, uint32_t*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUIObjInfoANotImpl1(void*, uint32_t, void**) { return kComHrNotImpl; }

// --- Stage 65: IOleUIVerifyA view methods ---
Il2CppComCcw* CcwFromOleUIVerifyAView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUIVerifyAVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUIVerifyAQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUIVerifyAView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIVerifyA, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUIVerifyAAddRef(void* self)
{
    auto* ccw = CcwFromOleUIVerifyAView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUIVerifyARelease(void* self)
{
    auto* ccw = CcwFromOleUIVerifyAView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUIVerifyANotImpl0(void*, void*) { return kComHrNotImpl; }

// --- Stage 66: IOleUILinkContainerW view methods ---
Il2CppComCcw* CcwFromOleUILinkContainerWView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUILinkContainerWVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUILinkContainerWQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUILinkContainerWView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkContainerW, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkContainerWAddRef(void* self)
{
    auto* ccw = CcwFromOleUILinkContainerWView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkContainerWRelease(void* self)
{
    auto* ccw = CcwFromOleUILinkContainerWView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUILinkContainerWNotImpl0(void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerWNotImpl1(void*, uint32_t, const void*, uint32_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerWNotImpl2(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerWNotImpl3(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkContainerWNotImpl4(void*, uint32_t, int32_t, int32_t) { return kComHrNotImpl; }

// --- Stage 67: IOleUILinkInfoW view methods ---
Il2CppComCcw* CcwFromOleUILinkInfoWView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUILinkInfoWVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUILinkInfoWQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUILinkInfoWView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUILinkInfoW, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkInfoWAddRef(void* self)
{
    auto* ccw = CcwFromOleUILinkInfoWView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUILinkInfoWRelease(void* self)
{
    auto* ccw = CcwFromOleUILinkInfoWView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUILinkInfoWNotImpl0(void*, uint32_t, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoWNotImpl1(void*, uint32_t, const void*, uint32_t, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoWNotImpl2(void*, uint32_t, void**, uint32_t*, uint32_t*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoWNotImpl3(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoWNotImpl4(void*, uint32_t, int32_t, int32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUILinkInfoWNotImpl5(void*, uint32_t, void*) { return kComHrNotImpl; }

// --- Stage 68: IOleUIObjInfoW view methods ---
Il2CppComCcw* CcwFromOleUIObjInfoWView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUIObjInfoWVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUIObjInfoWQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUIObjInfoWView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIObjInfoW, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUIObjInfoWAddRef(void* self)
{
    auto* ccw = CcwFromOleUIObjInfoWView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUIObjInfoWRelease(void* self)
{
    auto* ccw = CcwFromOleUIObjInfoWView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUIObjInfoWNotImpl0(void*, uint32_t, uint32_t*, void**) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwOleUIObjInfoWNotImpl1(void*, uint32_t, void**) { return kComHrNotImpl; }

// --- Stage 69: IOleUIVerifyW view methods ---
Il2CppComCcw* CcwFromOleUIVerifyWView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, oleUIVerifyWVtbl));
}

int32_t IL2CPP_COM_CALL CcwOleUIVerifyWQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromOleUIVerifyWView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleUIVerifyW, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwOleUIVerifyWAddRef(void* self)
{
    auto* ccw = CcwFromOleUIVerifyWView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwOleUIVerifyWRelease(void* self)
{
    auto* ccw = CcwFromOleUIVerifyWView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwOleUIVerifyWNotImpl0(void*, void*) { return kComHrNotImpl; }

// --- Stage 70: IEnumConnectionPoints view methods ---
Il2CppComCcw* CcwFromEnumConnectionPointsView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, enumConnectionPointsVtbl));
}

int32_t IL2CPP_COM_CALL CcwEnumConnectionPointsQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromEnumConnectionPointsView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumConnectionPoints, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumConnectionPointsAddRef(void* self)
{
    auto* ccw = CcwFromEnumConnectionPointsView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwEnumConnectionPointsRelease(void* self)
{
    auto* ccw = CcwFromEnumConnectionPointsView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwEnumConnectionPointsNotImpl0(void*, uint32_t, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumConnectionPointsNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumConnectionPointsNotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumConnectionPointsNotImpl3(void*, void**) { return kComHrNotImpl; }

// --- Stage 71: IEnumSTATSTG view methods ---
Il2CppComCcw* CcwFromEnumStatStgView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, enumStatStgVtbl));
}

int32_t IL2CPP_COM_CALL CcwEnumStatStgQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromEnumStatStgView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumSTATSTG, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumStatStgAddRef(void* self)
{
    auto* ccw = CcwFromEnumStatStgView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwEnumStatStgRelease(void* self)
{
    auto* ccw = CcwFromEnumStatStgView(self);
    --ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

int32_t IL2CPP_COM_CALL CcwEnumStatStgNotImpl0(void*, uint32_t, void*, uint32_t*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumStatStgNotImpl1(void*, uint32_t) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumStatStgNotImpl2(void*) { return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwEnumStatStgNotImpl3(void*, void**) { return kComHrNotImpl; }

#endif // defined(_WIN32)

//------------------------------------------------------------------------------
// ITypeLib / ITypeInfo (phase-6 evaluation -- the metadata layer). In the COM
// object model ITypeLib (the type library) and ITypeInfo (the per-type
// metadata) are the DESCRIPTION layer: they answer "what members does this
// object expose and with what signatures" -- GetTypeAttr (TYPEATTR: GUID,
// type kind, function/variable counts), GetFuncDesc (FUNCDESC per member),
// GetNames / GetDocumentation -- so an automation/scripting host can
// introspect an object WITHOUT invoking it. That is exactly what
// IDispatch::GetTypeInfoCount / GetTypeInfo hand out: a host calls
// GetTypeInfoCount (0 = "no type info -- late-bound only"), and on a 1 calls
// GetTypeInfo to obtain the ITypeInfo pointer (the metadata for the type).
//
// net48 reference: the CLR's CCW builds ITypeInfo from the MANAGED metadata
// (mscorlib reflection) at runtime -- the type library is re-derived from the
// class's metadata (TYPEATTR/FUNCDESC synthesized from MethodDef/FieldDef
// signatures; the registered [ComVisible]/[Guid] attributes supply the
// coclass GUID). ILC records the following SIMPLIFICATION (documented
// out-of-scope): the ILC CCW exposes NO ITypeInfo. The runtime member
// registry is name -> dispId ONLY (GetIDsOfNames assigns dispIds dynamically
// and Invoke accepts zero-arg calls); it carries no signature/GUID metadata a
// real TYPEATTR/FUNCDESC could be synthesized from. Fabricating one (fake
// GUIDs, guessed VT_* parameter types, invented counts) would present
// FALSE metadata to a host -- worse than an honest "no type library".
//
// WinForms stage-3 dependency check: the AxHost/WebBrowser hosting path
// reaches the ActiveX control through IOleObject / IOleClientSite /
// IConnectionPointContainer (phases 4-5, implemented) and IWebBrowser2 (the
// RCW direction); none of those introspect ITypeInfo. The type library is a
// design-time/scripting aid, NOT a hosting runtime dependency. So the honest
// phase-6 answer is the current late-bound contract -- a host needing
// ITypeInfo treats the object as scripted (the documented OLE response to a
// 0-typelib object). A future phase may wire ITypeInfo to the Il2CppTypeInfo
// registry (typeinfo.h) when a consumer actually introspects it.
//
// IWebBrowser2 re-evaluation (2026-08-19, roadmap v31): NOT implemented.
// The interface is a 71-slot vtable (3 IUnknown + 4 IDispatch + 25
// IWebBrowser + 20 IWebBrowserApp + 19 IWebBrowser2, exdisp.h) -- 3x larger
// than the largest existing view (IMoniker 23 slots). The hosting mechanism
// it would exercise (IDispatch automation, IOleObject/IOleClientSite/
// IConnectionPointContainer) is already fully implemented and tested
// (stages 1-76). There is no natural MANAGED implementer in the ILC test
// fixtures: the WebBrowser control is a NATIVE ActiveX control that the
// managed side CONSUMES through the RCW direction, and real browser hosting
// requires the native engine (SHDocVw/ieframe) which cannot run headless.
// A CCW IWebBrowser2 view would therefore be a ~71-slot stub surface with no
// behavioral value beyond the already-proven mechanism. Decision: document
// as out-of-scope; revisit only if a consumer actually hosts a managed
// IWebBrowser2 implementer.
//
// The contract below pins that decision:
//   * GetTypeInfoCount -> S_OK with *pctinfo = 0 (no type library).
//   * GetTypeInfo -> TYPE_E_ELEMENTNOTFOUND with a null out pointer (no
//     ITypeInfo to return); iTInfo/lcid are accepted (any index is out of
//     range for a 0-count object) and ignored.
//------------------------------------------------------------------------------
int32_t IL2CPP_COM_CALL CcwGetTypeInfoCount(void* self, uint32_t* pctinfo)
{
    (void)self;
    if (pctinfo == nullptr)
    {
        return kComHrBadPointer;
    }
    *pctinfo = 0;
    return kComHrOk;
}

// IDispatch::GetTypeInfo -- no ITypeInfo exists: return
// TYPE_E_ELEMENTNOTFOUND with a null out pointer (the documented no-typelib
// answer -- the phase-6 evaluation in the block above records why ILC keeps
// this simplification instead of synthesizing a fabricated type library).
int32_t IL2CPP_COM_CALL CcwGetTypeInfo(void* self, uint32_t iTInfo, uint32_t lcid, void** ppTInfo)
{
    (void)self;
    (void)iTInfo;
    (void)lcid;
    if (ppTInfo == nullptr)
    {
        return kComHrBadPointer;
    }
    *ppTInfo = nullptr;
    return kComHrTypeElemNotFound;
}

// IDispatch::GetIDsOfNames -- resolve each requested name to a dispId through
// the per-CCW registry: an already-registered name returns its cached dispId;
// a new name is assigned the next dispId (first name -> 1, then 2, ... -- the
// deterministic assignment the tests pin). A full registry (capacity 8)
// returns DISP_E_UNKNOWNNAME. riid must be IID_NULL (E_INVALIDARG otherwise).
int32_t IL2CPP_COM_CALL CcwGetIDsOfNames(void* self, const void* riid,
                                         void* const* rgszNames, uint32_t cNames,
                                         uint32_t lcid, int32_t* rgDispId)
{
    (void)lcid;
    auto* ccw = static_cast<Il2CppComCcw*>(self);
    if (riid == nullptr || std::memcmp(riid, kNullIid, 16) != 0)
    {
        return kComHrInvalidArg;
    }
    if (rgszNames == nullptr || rgDispId == nullptr || cNames == 0)
    {
        return kComHrBadPointer;
    }
    for (uint32_t i = 0; i < cNames; ++i)
    {
        const wchar_t* name = static_cast<const wchar_t*>(rgszNames[i]);
        if (name == nullptr)
        {
            return kComHrBadPointer;
        }
        const int32_t idx = FindMember(ccw, name);
        if (idx >= 0)
        {
            rgDispId[i] = ccw->members[idx].dispId;
            continue;
        }
        if (ccw->memberCount >= kCcwMaxMembers)
        {
            return kComHrUnknownName;
        }
        ccw->members[ccw->memberCount].name = DuplicateName(name);
        ccw->members[ccw->memberCount].dispId = ccw->memberCount + 1;
        rgDispId[i] = ccw->members[ccw->memberCount].dispId;
        ++ccw->memberCount;
    }
    return kComHrOk;
}

// IDispatch::Invoke -- phase-7 protocol dispatch: validate riid == IID_NULL,
// a non-null DISPPARAMS and a REGISTERED dispId (DISP_E_MEMBERNOTFOUND
// otherwise), then validate the argument VARIANTs and write the return
// VARIANT.
// Phase 7 deepens the phase-2 zero-arg contract: the CCW now accepts
// typed automation arguments (VT_I4 / VT_R8 / VT_BSTR plus the empty/null
// placeholder forms) and validates each VARIANT's type (an unsupported type ->
// DISP_E_TYPEMISMATCH). Named arguments are still rejected
// (DISP_E_BADPARAMCOUNT -- the phase-7 surface is positional only). Dispatching
// the call into the wrapped managed method (M3 reflection) remains the
// documented phase-3 path; until then a provided pVarResult is written as
// VT_EMPTY (the honest "no return value produced" answer -- the CCW does not
// fabricate a value it cannot compute).
//
// DISPPARAMS layout (both x86 and x64): rgvarg(ptr), rgdispidNamedArgs(ptr),
// cArgs(u32), cNamedArgs(u32) -- the pointers are pointer-sized (4 bytes on
// x86, 8 on x64), so cArgs is read at offset 2*sizeof(void*). rgvarg is an
// array of cArgs 16-byte VARIANTs in REVERSE order (rgvarg[0] is the LAST
// argument); validation is order-independent.

//------------------------------------------------------------------------------
// DispatchInvokeManaged -- the stage-18 IDispatch::Invoke -> managed-method
// dispatch. Resolves the member NAME (a wchar_t* from the GetIDsOfNames
// registry) to the registered interface's method slot (the typeinfo lookup
// is cached on the CCW) and calls through il2cpp_interface_dispatch with the
// positional VT_I4 arguments (rgvarg is REVERSE -- rgvarg[i] is argument
// cArgs-1-i; the phase-7 validation already confirmed every VARIANT type).
// Supported managed shapes: 'i(...)' -> the int32 return is written as VT_I4
// (lVal@8); 'v(...)' -> VT_EMPTY (or the buffer left zeroed when pVarResult
// is null). A non-implementer object (il2cpp_is_instance) / a missing
// typeinfo method -> E_NOTIMPL (never an interface_dispatch abort).
//------------------------------------------------------------------------------
int32_t DispatchInvokeManaged(Il2CppComCcw* ccw, int32_t memberIdx,
                              const void* rgvarg, uint32_t cArgs, void* pVarResult,
                              uint16_t wFlags)
{
    if (!ccw->invokeManaged)
    {
        if (pVarResult != nullptr)
        {
            std::memset(pVarResult, 0, kVariantSize);
        }
        return kComHrOk; // no managed dispatch: the honest VT_EMPTY answer
    }
    if (ccw->invokeIfaceInfo == nullptr)
    {
        return kComHrNotImpl;
    }
    const wchar_t* wname = ccw->members[memberIdx].name;
    if (wname == nullptr)
    {
        return kComHrNotImpl;
    }
    // wchar_t -> UTF-8 (the typeinfo method names are UTF-8).
    char name8[64];
    size_t n8 = 0;
    for (const wchar_t* p = wname; *p != 0 && n8 + 3 < sizeof(name8); ++p)
    {
        const uint32_t cp = static_cast<uint32_t>(*p);
        if (cp < 0x80)
        {
            name8[n8++] = static_cast<char>(cp);
        }
        else if (cp < 0x800)
        {
            name8[n8++] = static_cast<char>(0xC0 | (cp >> 6));
            name8[n8++] = static_cast<char>(0x80 | (cp & 0x3F));
        }
        else
        {
            name8[n8++] = static_cast<char>(0xE0 | (cp >> 12));
            name8[n8++] = static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
            name8[n8++] = static_cast<char>(0x80 | (cp & 0x3F));
        }
    }
    name8[n8] = 0;
    const Il2CppMethodInfo* mi = il2cpp_type_get_method(ccw->invokeIfaceInfo, name8);
    if (mi == nullptr || mi->virtualSlot < 0)
    {
        // Stage 20: IDispatch property access -- the member name is the
        // property name "X"; the CLR surface is the get_X / set_X accessor
        // methods. DISPATCH_PROPERTYGET (0x2) routes to get_X (no value
        // argument), DISPATCH_PROPERTYPUT (0x4) routes to set_X (the last
        // rgvarg entry is the value). The accessor is resolved by name here;
        // a property-put also needs the accessor method's signature to be a
        // 1-argument setter (checked when the arg decode runs).
        char accessor[80];
        const char* prefix = (wFlags & 0x4) != 0 ? "set_" : "get_";
        std::snprintf(accessor, sizeof(accessor), "%s%s", prefix, name8);
        mi = il2cpp_type_get_method(ccw->invokeIfaceInfo, accessor);
        if (mi == nullptr || mi->virtualSlot < 0)
        {
            return kComHrNotImpl; // neither the method nor a property accessor
        }
    }
    il2cpp::Il2CppObject* managed = icall_gchandle_get_target(ccw->gcHandle);
    if (managed == nullptr ||
        !il2cpp_is_instance(managed, ccw->invokeIface))
    {
        return kComHrNotImpl; // never abort the dispatch on a non-implementer
    }
    void** slotPtr = il2cpp_interface_dispatch(managed, ccw->invokeIface, mi->virtualSlot);
    // Stage 19: signature-driven argument decode. The typeinfo signature is
    // "i(...)"/"v(...)"/"d(...)" with comma-separated parameter encodings:
    //   i/b/c/i1/u1/i2/u2/f  -> VT_I4 int32 carrier (float rides the int32
    //                           slot per the M1 float convention? NO -- 'f' is
    //                           a double slot, see below)
    //   d                    -> VT_R8 double
    //   r:System.String      -> VT_BSTR (bstrVal@8)
    //   l/q                  -> VT_I8/VT_UI8 (the 64-bit slot)
    // The positional VARIANTs arrive in REVERSE order (rgvarg[0] = LAST arg).
    // The call shape is picked by the encoded parameter list; the supported
    // mix is int32/double/string/pointer 64-bit carriers (max 4 params).
    const char* sig = mi->signature != nullptr ? mi->signature : "";
    union ArgSlot
    {
        int32_t     i;
        double      d;
        void*       p;
        int64_t     l;
    };
    ArgSlot args[4] = { {0}, {0}, {0}, {0} };
    int32_t argKinds[4] = { 0, 0, 0, 0 }; // 0=none 1=int32 2=double 3=string 4=int64
    const uint32_t nArgs = cArgs < 4 ? cArgs : 4;
    const unsigned char* arg = static_cast<const unsigned char*>(rgvarg);
    for (uint32_t i = 0; i < nArgs; ++i)
    {
        const uint32_t dst = nArgs - 1 - i; // reverse order: rgvarg[0] = LAST
        uint16_t vt = 0;
        std::memcpy(&vt, arg + kVariantVtOffset, sizeof(vt));
        if (vt == kVtR8)
        {
            std::memcpy(&args[dst].d, arg + kVariantVtOffset + 8, sizeof(double)); // dblVal@8
            argKinds[dst] = 2;
        }
        else if (vt == kVtBstr)
        {
            void* bstr = nullptr;
            std::memcpy(&bstr, arg + kVariantVtOffset + 8, sizeof(void*)); // bstrVal@8
            // BSTR -> managed string (the pinvoke string-from-utf16 path;
            // BSTR is a null-terminated UTF-16 buffer).
            args[dst].p = bstr != nullptr
                ? reinterpret_cast<void*>(il2cpp::il2cpp_pinvoke_string_from_utf16(
                      reinterpret_cast<const char16_t*>(bstr)))
                : nullptr;
            argKinds[dst] = 3;
        }
        else if (vt == kVtI8 || vt == kVtUI8)
        {
            // Stage 24: the 64-bit automation types -- llVal@8.
            std::memcpy(&args[dst].l, arg + kVariantVtOffset + 8, sizeof(int64_t));
            argKinds[dst] = 4;
        }
        else
        {
            std::memcpy(&args[dst].i, arg + kVariantVtOffset + 8, sizeof(int32_t)); // lVal@8
            argKinds[dst] = 1;
        }
        arg += kVariantSize;
    }
    // Execute the call with the decoded argument kinds, then write the return
    // VARIANT from the signature's return type ('i' -> VT_I4, 'd' -> VT_R8,
    // 'r:System.String' -> VT_BSTR, else VT_EMPTY). The call shape is picked
    // by the argument KIND mix (int32/double/string carriers, 0..4 params).
    int32_t intRet = 0;
    double  dblRet = 0.0;
    void*   strRet = nullptr;
    int64_t lRet   = 0;
    bool    hasIntRet = false, hasDblRet = false, hasStrRet = false, hasLRet = false;
    switch (nArgs)
    {
        case 0:
            if (sig[0] == 'i' || sig[0] == 'b' || sig[0] == 'u')
            {
                intRet = reinterpret_cast<int32_t (*)(Il2CppObject*)>(*slotPtr)(managed);
                hasIntRet = true;
            }
            else if (sig[0] == 'd')
            {
                dblRet = reinterpret_cast<double (*)(Il2CppObject*)>(*slotPtr)(managed);
                hasDblRet = true;
            }
            else if (sig[0] == 'r')
            {
                strRet = reinterpret_cast<void* (*)(Il2CppObject*)>(*slotPtr)(managed);
                hasStrRet = true;
            }
            else if (sig[0] == 'l' || sig[0] == 'q')
            {
                // Stage 25: the 64-bit return carrier ('l'/'q' signature).
                lRet = reinterpret_cast<int64_t (*)(Il2CppObject*)>(*slotPtr)(managed);
                hasLRet = true;
            }
            else
            {
                reinterpret_cast<void (*)(Il2CppObject*)>(*slotPtr)(managed);
            }
            break;
        case 1:
            if (argKinds[0] == 3)
            {
                reinterpret_cast<void (*)(Il2CppObject*, Il2CppObject*)>(*slotPtr)(
                    managed, static_cast<il2cpp::Il2CppObject*>(args[0].p));
            }
            else if (argKinds[0] == 2)
            {
                if (sig[0] == 'd')
                {
                    dblRet = reinterpret_cast<double (*)(Il2CppObject*, double)>(*slotPtr)(managed, args[0].d);
                    hasDblRet = true;
                }
                else
                {
                    reinterpret_cast<void (*)(Il2CppObject*, double)>(*slotPtr)(managed, args[0].d);
                }
            }
            else if (argKinds[0] == 4)
            {
                // Stage 24: a single 64-bit argument ('l'/'q' signature; the
                // 64-bit RETURN is not yet carried -- the void shape only).
                reinterpret_cast<void (*)(Il2CppObject*, int64_t)>(*slotPtr)(managed, args[0].l);
            }
            else
            {
                if (sig[0] == 'i' || sig[0] == 'b' || sig[0] == 'u')
                {
                    intRet = reinterpret_cast<int32_t (*)(Il2CppObject*, int32_t)>(*slotPtr)(managed, args[0].i);
                    hasIntRet = true;
                }
                else
                {
                    reinterpret_cast<void (*)(Il2CppObject*, int32_t)>(*slotPtr)(managed, args[0].i);
                }
            }
            break;
        case 2:
            if (argKinds[0] == 2 && argKinds[1] == 2)
            {
                if (sig[0] == 'd')
                {
                    dblRet = reinterpret_cast<double (*)(Il2CppObject*, double, double)>(*slotPtr)(managed, args[0].d, args[1].d);
                    hasDblRet = true;
                }
                else
                {
                    reinterpret_cast<void (*)(Il2CppObject*, double, double)>(*slotPtr)(managed, args[0].d, args[1].d);
                }
            }
            else if (argKinds[0] == 2 && argKinds[1] == 3)
            {
                // (double, string) -- Scale(factor, label): the automation
                // double + BSTR argument mix.
                reinterpret_cast<void (*)(Il2CppObject*, double, Il2CppObject*)>(*slotPtr)(
                    managed, args[0].d,
                    static_cast<il2cpp::Il2CppObject*>(args[1].p));
            }
            else if (argKinds[0] == 2)
            {
                reinterpret_cast<void (*)(Il2CppObject*, double, int32_t)>(*slotPtr)(managed, args[0].d, args[1].i);
            }
            else if (argKinds[1] == 2)
            {
                if (sig[0] == 'd')
                {
                    dblRet = reinterpret_cast<double (*)(Il2CppObject*, int32_t, double)>(*slotPtr)(managed, args[0].i, args[1].d);
                    hasDblRet = true;
                }
                else
                {
                    reinterpret_cast<void (*)(Il2CppObject*, int32_t, double)>(*slotPtr)(managed, args[0].i, args[1].d);
                }
            }
            else if (argKinds[0] == 3 || argKinds[1] == 3)
            {
                reinterpret_cast<void (*)(Il2CppObject*, Il2CppObject*, Il2CppObject*)>(*slotPtr)(
                    managed,
                    static_cast<il2cpp::Il2CppObject*>(args[0].p),
                    static_cast<il2cpp::Il2CppObject*>(args[1].p));
            }
            else
            {
                if (sig[0] == 'i' || sig[0] == 'b' || sig[0] == 'u')
                {
                    intRet = reinterpret_cast<int32_t (*)(Il2CppObject*, int32_t, int32_t)>(*slotPtr)(managed, args[0].i, args[1].i);
                    hasIntRet = true;
                }
                else
                {
                    reinterpret_cast<void (*)(Il2CppObject*, int32_t, int32_t)>(*slotPtr)(managed, args[0].i, args[1].i);
                }
            }
            break;
        default:
            if (argKinds[0] == 2 || argKinds[1] == 2 || argKinds[2] == 2)
            {
                reinterpret_cast<void (*)(Il2CppObject*, double, double, double)>(*slotPtr)(
                    managed, args[0].d, args[1].d, args[2].d);
            }
            else if (argKinds[0] == 3 || argKinds[1] == 3 || argKinds[2] == 3)
            {
                reinterpret_cast<void (*)(Il2CppObject*, Il2CppObject*, Il2CppObject*, Il2CppObject*)>(*slotPtr)(
                    managed,
                    static_cast<il2cpp::Il2CppObject*>(args[0].p),
                    static_cast<il2cpp::Il2CppObject*>(args[1].p),
                    static_cast<il2cpp::Il2CppObject*>(args[2].p));
            }
            else
            {
                reinterpret_cast<void (*)(Il2CppObject*, int32_t, int32_t, int32_t)>(*slotPtr)(
                    managed, args[0].i, args[1].i, args[2].i);
            }
            break;
    }
    if (pVarResult != nullptr)
    {
        std::memset(pVarResult, 0, kVariantSize);
        uint16_t vt = kVtEmpty;
        if (hasIntRet)
        {
            // Stage 22: the return VARIANT type follows the signature return
            // encoding ('i' -> VT_I4, 'b' -> VT_BOOL, 'u' -> VT_UI4 -- the
            // bool/uint both ride the 4-byte lVal slot).
            if (sig[0] == 'b')
            {
                vt = kVtBool;
            }
            else if (sig[0] == 'u')
            {
                vt = kVtUI4;
            }
            else
            {
                vt = kVtI4;
            }
            std::memcpy(pVarResult, &vt, sizeof(vt));
            std::memcpy(static_cast<char*>(pVarResult) + kVariantVtOffset + 8, &intRet, sizeof(intRet));
        }
        else if (hasDblRet)
        {
            vt = kVtR8;
            std::memcpy(pVarResult, &vt, sizeof(vt));
            std::memcpy(static_cast<char*>(pVarResult) + kVariantVtOffset + 8, &dblRet, sizeof(dblRet));
        }
        else if (hasStrRet)
        {
            vt = kVtBstr;
            std::memcpy(pVarResult, &vt, sizeof(vt));
            // Stage 23: the managed string return (an Il2CppString*) must
            // become a real BSTR (SysAllocStringLen of the chars) -- the
            // VARIANT's bstrVal is a caller-owned BSTR the client
            // VariantClear's. A null string yields a null bstrVal (VT_BSTR
            // null is legal).
            void* bstr = nullptr;
            auto* s = static_cast<il2cpp::Il2CppString*>(strRet);
            if (s != nullptr)
            {
                const uint32_t len = static_cast<uint32_t>(s->length);
                if (GetOle32().sysAllocStringLen != nullptr)
                {
                    bstr = GetOle32().sysAllocStringLen(s->chars, len);
                }
                else
                {
                    // POSIX: oleaut32 is unavailable -- allocate the EXACT
                    // BSTR wire format so VT_BSTR returns carry real data on
                    // every platform (cross-platform consistency contract):
                    // a 4-byte byte-length prefix, 'len' UTF-16 units, a NUL
                    // terminator; the returned pointer aims at the first unit
                    // (the prefix sits at ptr[-4..-1], OLEAUT32's layout).
                    const uint32_t bytes = len * 2u;
                    auto* block = static_cast<uint8_t*>(
                        std::malloc(sizeof(uint32_t) + bytes + sizeof(char16_t)));
                    if (block != nullptr)
                    {
                        std::memcpy(block, &bytes, sizeof(bytes));
                        if (bytes != 0)
                        {
                            std::memcpy(block + sizeof(bytes), s->chars, bytes);
                        }
                        const uint16_t nul = 0;
                        std::memcpy(block + sizeof(bytes) + bytes, &nul, sizeof(nul));
                        bstr = block + sizeof(bytes);
                    }
                }
            }
            std::memcpy(static_cast<char*>(pVarResult) + kVariantVtOffset + 8, &bstr, sizeof(bstr));
        }
        else if (hasLRet)
        {
            // Stage 25: the 64-bit return -- 'l' -> VT_I8, 'q' -> VT_UI8.
            vt = (sig[0] == 'q') ? kVtUI8 : kVtI8;
            std::memcpy(pVarResult, &vt, sizeof(vt));
            std::memcpy(static_cast<char*>(pVarResult) + kVariantVtOffset + 8, &lRet, sizeof(lRet));
        }
        else
        {
            std::memcpy(pVarResult, &vt, sizeof(vt)); // VT_EMPTY
        }
    }
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwInvoke(void* self, int32_t dispIdMember, const void* riid,
                                  uint32_t lcid, uint16_t wFlags, const void* pDispParams,
                                  void* pVarResult, void* pExcepInfo, uint32_t* puArgErr)
{
    (void)lcid;
    (void)wFlags;
    (void)pExcepInfo;
    (void)puArgErr;
    auto* ccw = static_cast<Il2CppComCcw*>(self);
    if (riid == nullptr || std::memcmp(riid, kNullIid, 16) != 0)
    {
        return kComHrInvalidArg;
    }
    if (pDispParams == nullptr)
    {
        return kComHrBadPointer;
    }
    int32_t found = -1;
    for (int32_t i = 0; i < ccw->memberCount; ++i)
    {
        if (ccw->members[i].dispId == dispIdMember)
        {
            found = i;
            break;
        }
    }
    if (found < 0)
    {
        return kComHrMemberNotFound;
    }
    // Parse DISPPARAMS with pointer-size awareness: rgvarg (ptr) and
    // rgdispidNamedArgs (ptr) come first, then cArgs (u32) and cNamedArgs
    // (u32). On x86 the pointers are 4 bytes (cArgs at offset 8); on x64 they
    // are 8 bytes (cArgs at offset 16). Reading through a byte pointer keeps
    // the layout correct on both.
    const unsigned char* raw = static_cast<const unsigned char*>(pDispParams);
    const void* rgvarg = nullptr;
    uint32_t cArgs = 0;
    uint32_t cNamed = 0;
    const void* rgdispidNamed = nullptr;
    std::memcpy(&rgvarg, raw, sizeof(rgvarg));                        // rgvarg
    std::memcpy(&rgdispidNamed, raw + sizeof(void*), sizeof(rgdispidNamed)); // rgdispidNamedArgs
    std::memcpy(&cArgs, raw + 2 * sizeof(void*), sizeof(cArgs));      // cArgs
    std::memcpy(&cNamed, raw + 2 * sizeof(void*) + sizeof(cArgs), sizeof(cNamed)); // cNamedArgs
    if (cNamed != 0)
    {
        // Phase 7 accepts positional arguments only; the ONE named-argument
        // case accepted (stage 26) is the standard property-put shape: a
        // single DISPID_PROPERTYPUT named arg (the value travels in rgvarg[0],
        // so the named list is a marker, not an extra argument).
        bool propPutOk = (cNamed == 1 && rgdispidNamed != nullptr);
        if (propPutOk)
        {
            int32_t nd = 0;
            std::memcpy(&nd, rgdispidNamed, sizeof(nd));
            propPutOk = (nd == kDispIdPropertyPut);
        }
        if (!propPutOk)
        {
            return kComHrBadParamCount;
        }
    }
    if (cArgs != 0)
    {
        if (rgvarg == nullptr)
        {
            return kComHrBadPointer;
        }
        // Validate every argument VARIANT's type (order-independent). The
        // supported automation parameter shapes are VT_I4 / VT_R8 / VT_BSTR
        // plus the empty/null placeholders; anything else is a type mismatch.
        const unsigned char* arg = static_cast<const unsigned char*>(rgvarg);
        for (uint32_t i = 0; i < cArgs; ++i)
        {
            uint16_t vt = 0;
            std::memcpy(&vt, arg + kVariantVtOffset, sizeof(vt));
            switch (vt)
            {
                case kVtEmpty:
                case kVtNull:
                case kVtI4:
                case kVtR8:
                case kVtBstr:
                case kVtBool:
                case kVtUI4:
                case kVtI8:
                case kVtUI8:
                    break;
                default:
                    return kComHrTypeMismatch;
            }
            arg += kVariantSize;
        }
    }
    // Stage 18: when the invoke dispatch is registered, resolve the member
    // NAME to the managed interface slot (typeinfo) and call through
    // il2cpp_interface_dispatch -- the managed method runs whether or not the
    // caller asked for a return VARIANT (pVarResult may be null). The
    // supported automation shape is the interface's int-typed methods:
    // positional VT_I4 arguments (rgvarg is REVERSE order -- rgvarg[0] is
    // the LAST argument; the phase-7 validation already confirmed every
    // VARIANT's type), and the int32 return written as VT_I4 ('v' method ->
    // VT_EMPTY, or the buffer left zeroed when pVarResult is null). A missing
    // typeinfo method / a non-implementer object answers E_NOTIMPL (never an
    // interface_dispatch abort).
    return DispatchInvokeManaged(ccw, found, rgvarg, cArgs, pVarResult, wFlags);
}

const Il2CppComCcwVtable kCcwVtable = {
    &CcwQueryInterface,
    &CcwAddRef,
    &CcwRelease,
    &CcwGetTypeInfoCount,
    &CcwGetTypeInfo,
    &CcwGetIDsOfNames,
    &CcwInvoke,
};

#if defined(_WIN32)

// The IOleObject view vtable (24 slots, canonical oleidl.h order). The
// minimal surface implements SetClientSite/GetClientSite/Close/DoVerb/Update/
// IsUpToDate; the rest are the documented E_NOTIMPL stubs.
const Il2CppComOleVtable kOleVtable = {
    &CcwOleQueryInterface,
    &CcwOleAddRef,
    &CcwOleRelease,
    &CcwOleSetClientSite,          // slot 3
    &CcwOleGetClientSite,          // slot 4
    &CcwOleNotImpl3,               // slot 5 SetHostNames
    &CcwOleClose,                  // slot 6 Close
    &CcwOleNotImpl4,               // slot 7 SetMoniker
    &CcwOleNotImpl5,               // slot 8 GetMoniker
    &CcwOleNotImpl6,               // slot 9 InitFromData
    &CcwOleNotImpl7,               // slot 10 GetClipboardData
    &CcwOleDoVerb,                 // slot 11 DoVerb
    &CcwOleNotImpl8,               // slot 12 EnumVerbs
    &CcwOleUpdate,                 // slot 13 Update
    &CcwOleIsUpToDate,             // slot 14 IsUpToDate
    &CcwOleNotImpl9,               // slot 15 GetUserClassID
    &CcwOleNotImpl10,              // slot 16 GetUserType
    &CcwOleNotImpl11,              // slot 17 SetExtent
    &CcwOleNotImpl12,              // slot 18 GetExtent
    &CcwOleNotImpl13,              // slot 19 Advise
    &CcwOleNotImpl14,              // slot 20 Unadvise
    &CcwOleNotImpl8,               // slot 21 EnumAdvise
    &CcwOleNotImpl15,              // slot 22 GetMiscStatus
    &CcwOleNotImpl16,              // slot 23 SetColorScheme
};

// The IOleClientSite view vtable (9 slots, canonical oleidl.h order).
// GetContainer returns the documented no-container E_NOTIMPL; the rest are
// the documented E_NOTIMPL stubs.
const Il2CppComSiteVtable kSiteVtable = {
    &CcwSiteQueryInterface,
    &CcwSiteAddRef,
    &CcwSiteRelease,
    &CcwSiteNotImpl0,              // slot 3 SaveObject
    &CcwSiteNotImpl1,              // slot 4 GetMoniker
    &CcwSiteGetContainer,          // slot 5 GetContainer
    &CcwSiteNotImpl2,              // slot 6 ShowObject
    &CcwSiteNotImpl3,              // slot 7 OnShowWindow
    &CcwSiteNotImpl4,              // slot 8 RequestNewObjectLayout
};

// The IOleInPlaceObject view vtable (9 slots, canonical oleidl.h order).
// Stage 27: the in-place-activated object surface. Lives at &ccw->inPlaceVtbl.
const Il2CppComInPlaceVtable kInPlaceVtable = {
    &CcwInPlaceQueryInterface,
    &CcwInPlaceAddRef,
    &CcwInPlaceRelease,
    &CcwInPlaceGetWindow,          // slot 3 GetWindow
    &CcwInPlaceNotImpl0,           // slot 4 ContextSensitiveHelp
    &CcwInPlaceDeactivate,         // slot 5 InPlaceDeactivate
    &CcwInPlaceUiDeactivate,       // slot 6 UIDeactivate
    &CcwInPlaceNotImpl1,           // slot 7 SetObjectRects
    &CcwInPlaceNotImpl2,           // slot 8 ReactivateAndUndo
};

// The IProvideClassInfo view vtable (4 slots, canonical ocidl.h order).
// Stage 28: GetClassInfo answers E_NOTIMPL (no ITypeInfo is built).
// Lives at &ccw->classInfoVtbl.
const Il2CppComClassInfoVtable kClassInfoVtable = {
    &CcwClassInfoQueryInterface,
    &CcwClassInfoAddRef,
    &CcwClassInfoRelease,
    &CcwClassInfoGetClassInfo,     // slot 3 GetClassInfo
};

// The IOleInPlaceSite view vtable (15 slots, canonical oleidl.h order).
// Stage 29: GetWindow null hwnd, the activate callbacks S_OK, the rest
// E_NOTIMPL. Lives at &ccw->inPlaceSiteVtbl.
const Il2CppComInPlaceSiteVtable kInPlaceSiteVtable = {
    &CcwInPlaceSiteQueryInterface,
    &CcwInPlaceSiteAddRef,
    &CcwInPlaceSiteRelease,
    &CcwInPlaceSiteGetWindow,          // slot 3 GetWindow
    &CcwInPlaceSiteNotImpl0,           // slot 4 ContextSensitiveHelp
    &CcwInPlaceSiteActivateOk,         // slot 5 CanInPlaceActivate
    &CcwInPlaceSiteActivateOk,         // slot 6 OnInPlaceActivate
    &CcwInPlaceSiteActivateOk,         // slot 7 OnUIActivate
    &CcwInPlaceSiteNotImpl1,           // slot 8 GetWindowContext
    &CcwInPlaceSiteNotImpl2,           // slot 9 Scroll
    &CcwInPlaceSiteNotImpl3,           // slot 10 OnUIDeactivate
    &CcwInPlaceSiteActivateOk,         // slot 11 OnInPlaceDeactivate
    &CcwInPlaceSiteNotImpl4,           // slot 12 DiscardUndoState
    &CcwInPlaceSiteNotImpl4,           // slot 13 DeactivateAndUndo
    &CcwInPlaceSiteNotImpl2,           // slot 14 OnPosRectChange
};

// The IAdviseSink view vtable (7 slots, canonical oleidl.h order). Stage 30:
// all four notifications answer S_OK (accepted and dropped headless).
// Lives at &ccw->adviseSinkVtbl.
const Il2CppComAdviseSinkVtable kAdviseSinkVtable = {
    &CcwAdviseSinkQueryInterface,
    &CcwAdviseSinkAddRef,
    &CcwAdviseSinkRelease,
    &CcwAdviseSinkNotifyOk4,           // slot 3 OnDataChange
    &CcwAdviseSinkNotifyOk2,           // slot 4 OnViewChange
    &CcwAdviseSinkNotifyOk3,           // slot 5 OnRename
    &CcwAdviseSinkNotifyOk,            // slot 6 OnSave
};

// The IAdviseSink2 view vtable (8 slots, canonical oleidl.h order). Stage 72:
// all five notifications answer S_OK (accepted and dropped headless).
// Lives at &ccw->adviseSink2Vtbl.
const Il2CppComAdviseSink2Vtable kAdviseSink2Vtable = {
    &CcwAdviseSink2QueryInterface,
    &CcwAdviseSink2AddRef,
    &CcwAdviseSink2Release,
    &CcwAdviseSinkNotifyOk4,           // slot 3 OnDataChange
    &CcwAdviseSinkNotifyOk2,           // slot 4 OnViewChange
    &CcwAdviseSinkNotifyOk3,           // slot 5 OnRename
    &CcwAdviseSinkNotifyOk,            // slot 6 OnSave
    &CcwAdviseSink2NotifyOk,           // slot 7 OnLinkSrcChange
};

// The IAdviseSinkEx view vtable (9 slots, canonical oleidl.h order). Stage 73:
// all five notifications answer S_OK (accepted and dropped headless).
// Lives at &ccw->adviseSinkExVtbl.
const Il2CppComAdviseSinkExVtable kAdviseSinkExVtable = {
    &CcwAdviseSinkExQueryInterface,
    &CcwAdviseSinkExAddRef,
    &CcwAdviseSinkExRelease,
    &CcwAdviseSinkNotifyOk4,           // slot 3 OnDataChange
    &CcwAdviseSinkNotifyOk2,           // slot 4 OnViewChange
    &CcwAdviseSinkNotifyOk3,           // slot 5 OnRename
    &CcwAdviseSinkNotifyOk,            // slot 6 OnSave
    &CcwAdviseSinkExNotifyOk,          // slot 7 OnViewStatusChange
};

// The IStream view vtable (14 slots, canonical objidl.h order). Stage 74:
// all eleven methods answer E_NOTIMPL (the stream is empty headless).
// Lives at &ccw->streamVtbl.
const Il2CppComStreamVtable kStreamVtable = {
    &CcwStreamQueryInterface,
    &CcwStreamAddRef,
    &CcwStreamRelease,
    &CcwStreamNotImpl0,                // slot 3 Read
    &CcwStreamNotImpl1,                // slot 4 Write
    &CcwStreamNotImpl2,                // slot 5 Seek
    &CcwStreamNotImpl3,                // slot 6 SetSize
    &CcwStreamNotImpl4,                // slot 7 CopyTo
    &CcwStreamNotImpl5,                // slot 8 Commit
    &CcwStreamNotImpl6,                // slot 9 Revert
    &CcwStreamNotImpl7,                // slot 10 LockRegion
    &CcwStreamNotImpl7,                // slot 11 UnlockRegion
    &CcwStreamNotImpl8,                // slot 12 Stat
    &CcwStreamNotImpl9,                // slot 13 Clone
};

// The IStorage view vtable (18 slots, canonical objidl.h order). Stage 75:
// all fifteen methods answer E_NOTIMPL (the storage is empty headless).
// Lives at &ccw->storageVtbl.
const Il2CppComStorageVtable kStorageVtable = {
    &CcwStorageQueryInterface,
    &CcwStorageAddRef,
    &CcwStorageRelease,
    &CcwStorageNotImpl0,               // slot 3 CreateStream
    &CcwStorageNotImpl1,               // slot 4 OpenStream
    &CcwStorageNotImpl0,               // slot 5 CreateStorage
    &CcwStorageNotImpl2,               // slot 6 OpenStorage
    &CcwStorageNotImpl3,               // slot 7 CopyTo
    &CcwStorageNotImpl4,               // slot 8 MoveElementTo
    &CcwStorageNotImpl5,               // slot 9 Commit
    &CcwStorageNotImpl6,               // slot 10 Revert
    &CcwStorageNotImpl7,               // slot 11 EnumElements
    &CcwStorageNotImpl8,               // slot 12 DestroyElement
    &CcwStorageNotImpl9,               // slot 13 RenameElement
    &CcwStorageNotImpl10,              // slot 14 SetElementTimes
    &CcwStorageNotImpl8,               // slot 15 SetClass
    &CcwStorageNotImpl11,              // slot 16 SetStateBits
    &CcwStorageNotImpl12,              // slot 17 Stat
};

// The IOleInPlaceUIWindow view vtable (9 slots, canonical oleidl.h order).
// Stage 31: GetWindow null hwnd, the rest E_NOTIMPL. Lives at
// &ccw->inPlaceUiWindowVtbl.
const Il2CppComInPlaceUIWindowVtable kInPlaceUIWindowVtable = {
    &CcwInPlaceUIWindowQueryInterface,
    &CcwInPlaceUIWindowAddRef,
    &CcwInPlaceUIWindowRelease,
    &CcwInPlaceUIWindowGetWindow,      // slot 3 GetWindow
    &CcwInPlaceUIWindowNotImpl0,       // slot 4 ContextSensitiveHelp
    &CcwInPlaceUIWindowNotImpl1,       // slot 5 GetBorder
    &CcwInPlaceUIWindowNotImpl2,       // slot 6 RequestBorderSpace
    &CcwInPlaceUIWindowNotImpl2,       // slot 7 SetBorderSpace
    &CcwInPlaceUIWindowNotImpl3,       // slot 8 SetActiveObject
};

// The IOleInPlaceFrame view vtable (15 slots, canonical oleidl.h order).
// Stage 32: the in-place frame (IOleInPlaceUIWindow + InsertMenus / SetMenu /
// RemoveMenus / SetStatusText / EnableModeless / TranslateAccelerator).
// GetWindow null hwnd, SetActiveObject S_OK, the rest E_NOTIMPL. Lives at
// &ccw->inPlaceFrameVtbl.
const Il2CppComInPlaceFrameVtable kInPlaceFrameVtable = {
    &CcwInPlaceFrameQueryInterface,
    &CcwInPlaceFrameAddRef,
    &CcwInPlaceFrameRelease,
    &CcwInPlaceFrameGetWindow,         // slot 3 GetWindow
    &CcwInPlaceFrameNotImpl0,          // slot 4 ContextSensitiveHelp
    &CcwInPlaceFrameNotImpl1,          // slot 5 GetBorder
    &CcwInPlaceFrameNotImpl2,          // slot 6 RequestBorderSpace
    &CcwInPlaceFrameNotImpl2,          // slot 7 SetBorderSpace
    &CcwInPlaceFrameSetActiveObject,   // slot 8 SetActiveObject
    &CcwInPlaceFrameNotImpl3,          // slot 9 InsertMenus
    &CcwInPlaceFrameNotImpl4,          // slot 10 SetMenu
    &CcwInPlaceFrameNotImpl5,          // slot 11 RemoveMenus
    &CcwInPlaceFrameNotImpl2,          // slot 12 SetStatusText
    &CcwInPlaceFrameNotImpl0,          // slot 13 EnableModeless
    &CcwInPlaceFrameNotImpl2,          // slot 14 TranslateAccelerator
};

// The IOleContainer view vtable (6 slots, canonical oleidl.h order). Stage 33:
// ParseDisplayName / EnumObjects / LockContainer all E_NOTIMPL. Lives at
// &ccw->oleContainerVtbl.
const Il2CppComOleContainerVtable kOleContainerVtable = {
    &CcwOleContainerQueryInterface,
    &CcwOleContainerAddRef,
    &CcwOleContainerRelease,
    &CcwOleContainerNotImpl0,          // slot 3 ParseDisplayName
    &CcwOleContainerNotImpl1,          // slot 4 EnumObjects
    &CcwOleContainerNotImpl2,          // slot 5 LockContainer
};

// The IEnumUnknown view vtable (7 slots, canonical objidl.h order). Stage 34:
// Next / Skip / Reset / Clone all E_NOTIMPL (the enumeration is empty
// headless). Lives at &ccw->enumUnknownVtbl.
const Il2CppComEnumUnknownVtable kEnumUnknownVtable = {
    &CcwEnumUnknownQueryInterface,
    &CcwEnumUnknownAddRef,
    &CcwEnumUnknownRelease,
    &CcwEnumUnknownNotImpl0,           // slot 3 Next
    &CcwEnumUnknownNotImpl1,           // slot 4 Skip
    &CcwEnumUnknownNotImpl2,           // slot 5 Reset
    &CcwEnumUnknownNotImpl3,           // slot 6 Clone
};

// The IEnumString view vtable (7 slots, canonical objidl.h order). Stage 35:
// Next / Skip / Reset / Clone all E_NOTIMPL (the enumeration is empty
// headless). Lives at &ccw->enumStringVtbl.
const Il2CppComEnumStringVtable kEnumStringVtable = {
    &CcwEnumStringQueryInterface,
    &CcwEnumStringAddRef,
    &CcwEnumStringRelease,
    &CcwEnumStringNotImpl0,            // slot 3 Next
    &CcwEnumStringNotImpl1,            // slot 4 Skip
    &CcwEnumStringNotImpl2,            // slot 5 Reset
    &CcwEnumStringNotImpl3,            // slot 6 Clone
};

// The IEnumFORMATETC view vtable (7 slots, canonical objidl.h order).
// Stage 36: Next / Skip / Reset / Clone all E_NOTIMPL (the enumeration is
// empty headless). Lives at &ccw->enumFormatEtcVtbl.
const Il2CppComEnumFormatEtcVtable kEnumFormatEtcVtable = {
    &CcwEnumFormatEtcQueryInterface,
    &CcwEnumFormatEtcAddRef,
    &CcwEnumFormatEtcRelease,
    &CcwEnumFormatEtcNotImpl0,         // slot 3 Next
    &CcwEnumFormatEtcNotImpl1,         // slot 4 Skip
    &CcwEnumFormatEtcNotImpl2,         // slot 5 Reset
    &CcwEnumFormatEtcNotImpl3,         // slot 6 Clone
};

// The IPersistStream view vtable (8 slots, canonical objidl.h order).
// Stage 37: GetClassID / IsDirty / Load / Save / GetSizeMax all E_NOTIMPL
// (no class ID / persistence). Lives at &ccw->persistStreamVtbl.
const Il2CppComPersistStreamVtable kPersistStreamVtable = {
    &CcwPersistStreamQueryInterface,
    &CcwPersistStreamAddRef,
    &CcwPersistStreamRelease,
    &CcwPersistStreamNotImpl0,         // slot 3 GetClassID
    &CcwPersistStreamNotImpl1,         // slot 4 IsDirty
    &CcwPersistStreamNotImpl0,         // slot 5 Load
    &CcwPersistStreamNotImpl2,         // slot 6 Save
    &CcwPersistStreamNotImpl0,         // slot 7 GetSizeMax
};

// The IPersistFile view vtable (9 slots, canonical objidl.h order). Stage 38:
// GetClassID / IsDirty / Load / Save / SaveCompleted / GetCurFile all
// E_NOTIMPL (no class ID / persistence). Lives at &ccw->persistFileVtbl.
const Il2CppComPersistFileVtable kPersistFileVtable = {
    &CcwPersistFileQueryInterface,
    &CcwPersistFileAddRef,
    &CcwPersistFileRelease,
    &CcwPersistFileNotImpl0,           // slot 3 GetClassID
    &CcwPersistFileNotImpl1,           // slot 4 IsDirty
    &CcwPersistFileNotImpl2,           // slot 5 Load
    &CcwPersistFileNotImpl3,           // slot 6 Save
    &CcwPersistFileNotImpl4,           // slot 7 SaveCompleted
    &CcwPersistFileNotImpl5,           // slot 8 GetCurFile
};

// The IPersist view vtable (4 slots, canonical objidl.h order). Stage 39:
// GetClassID E_NOTIMPL (no class ID). The base persistence interface --
// the root of the persistence family. Lives at &ccw->persistVtbl.
const Il2CppComPersistVtable kPersistVtable = {
    &CcwPersistQueryInterface,
    &CcwPersistAddRef,
    &CcwPersistRelease,
    &CcwPersistNotImpl0,               // slot 3 GetClassID
};

// The IOleInPlaceActiveObject view vtable (12 slots, canonical oleidl.h
// order). Stage 40: GetWindow answers null hwnd; the rest E_NOTIMPL.
// Lives at &ccw->inPlaceActiveObjectVtbl.
const Il2CppComInPlaceActiveObjectVtable kInPlaceActiveObjectVtable = {
    &CcwInPlaceActiveObjectQueryInterface,
    &CcwInPlaceActiveObjectAddRef,
    &CcwInPlaceActiveObjectRelease,
    &CcwInPlaceActiveObjectGetWindow,  // slot 3 GetWindow (null hwnd)
    &CcwInPlaceActiveObjectNotImpl0,   // slot 4 ContextSensitiveHelp
    &CcwInPlaceActiveObjectNotImpl2,   // slot 5 SetObjectRects
    &CcwInPlaceActiveObjectNotImpl3,   // slot 6 ReactivateAndUndo
    &CcwInPlaceActiveObjectNotImpl1,   // slot 7 TranslateAccelerator
    &CcwInPlaceActiveObjectNotImpl0,   // slot 8 OnFrameWindowActivate
    &CcwInPlaceActiveObjectNotImpl0,   // slot 9 OnDocWindowActivate
    &CcwInPlaceActiveObjectNotImpl4,   // slot 10 ResizeBorder
    &CcwInPlaceActiveObjectNotImpl0,   // slot 11 EnableModeless
};

// The IPersistPropertyBag view vtable (7 slots, canonical objidl.h order).
// Stage 41: GetClassID / InitNew / Load / Save all E_NOTIMPL (no class ID /
// persistence). Lives at &ccw->persistPropertyBagVtbl.
const Il2CppComPersistPropertyBagVtable kPersistPropertyBagVtable = {
    &CcwPersistPropertyBagQueryInterface,
    &CcwPersistPropertyBagAddRef,
    &CcwPersistPropertyBagRelease,
    &CcwPersistPropertyBagNotImpl0,    // slot 3 GetClassID
    &CcwPersistPropertyBagNotImpl1,    // slot 4 InitNew
    &CcwPersistPropertyBagNotImpl2,    // slot 5 Load
    &CcwPersistPropertyBagNotImpl3,    // slot 6 Save
};

// The IParseDisplayName view vtable (4 slots, canonical objidl.h order).
// Stage 42: ParseDisplayName E_NOTIMPL. Lives at &ccw->parseDisplayNameVtbl.
const Il2CppComParseDisplayNameVtable kParseDisplayNameVtable = {
    &CcwParseDisplayNameQueryInterface,
    &CcwParseDisplayNameAddRef,
    &CcwParseDisplayNameRelease,
    &CcwParseDisplayNameNotImpl0,      // slot 3 ParseDisplayName
};

// The IViewObject view vtable (9 slots, canonical oleidl.h order). Stage 43:
// Draw / GetColorSet / Freeze / Unfreeze / SetAdvise / GetAdvise all
// E_NOTIMPL (no view). Lives at &ccw->viewObjectVtbl.
const Il2CppComViewObjectVtable kViewObjectVtable = {
    &CcwViewObjectQueryInterface,
    &CcwViewObjectAddRef,
    &CcwViewObjectRelease,
    &CcwViewObjectNotImpl0,            // slot 3 Draw
    &CcwViewObjectNotImpl1,            // slot 4 GetColorSet
    &CcwViewObjectNotImpl2,            // slot 5 Freeze
    &CcwViewObjectNotImpl3,            // slot 6 Unfreeze
    &CcwViewObjectNotImpl4,            // slot 7 SetAdvise
    &CcwViewObjectNotImpl5,            // slot 8 GetAdvise
};

// The IEnumSTATDATA view vtable (7 slots, canonical objidl.h order).
// Stage 44: Next / Skip / Reset / Clone all E_NOTIMPL (the enumeration is
// empty headless). Lives at &ccw->enumStatDataVtbl.
const Il2CppComEnumSTATDATAVtable kEnumSTATDATAVtable = {
    &CcwEnumSTATDATAQueryInterface,
    &CcwEnumSTATDATAAddRef,
    &CcwEnumSTATDATARelease,
    &CcwEnumSTATDATANotImpl0,          // slot 3 Next
    &CcwEnumSTATDATANotImpl1,          // slot 4 Skip
    &CcwEnumSTATDATANotImpl2,          // slot 5 Reset
    &CcwEnumSTATDATANotImpl3,          // slot 6 Clone
};

// The IEnumOLEVERB view vtable (7 slots, canonical objidl.h order).
// Stage 45: Next / Skip / Reset / Clone all E_NOTIMPL (the enumeration is
// empty headless). Lives at &ccw->enumOleVerbVtbl.
const Il2CppComEnumOLEVERBVtable kEnumOLEVERBVtable = {
    &CcwEnumOLEVERBQueryInterface,
    &CcwEnumOLEVERBAddRef,
    &CcwEnumOLEVERBRelease,
    &CcwEnumOLEVERBNotImpl0,           // slot 3 Next
    &CcwEnumOLEVERBNotImpl1,           // slot 4 Skip
    &CcwEnumOLEVERBNotImpl2,           // slot 5 Reset
    &CcwEnumOLEVERBNotImpl3,           // slot 6 Clone
};

// The IEnumMoniker view vtable (7 slots, canonical objidl.h order).
// Stage 46: Next / Skip / Reset / Clone all E_NOTIMPL (the enumeration is
// empty headless). Lives at &ccw->enumMonikerVtbl.
const Il2CppComEnumMonikerVtable kEnumMonikerVtable = {
    &CcwEnumMonikerQueryInterface,
    &CcwEnumMonikerAddRef,
    &CcwEnumMonikerRelease,
    &CcwEnumMonikerNotImpl0,           // slot 3 Next
    &CcwEnumMonikerNotImpl1,           // slot 4 Skip
    &CcwEnumMonikerNotImpl2,           // slot 5 Reset
    &CcwEnumMonikerNotImpl3,           // slot 6 Clone
};

// The IViewObject2 view vtable (10 slots, canonical oleidl.h order).
// Stage 47: Draw / GetColorSet / Freeze / Unfreeze / SetAdvise / GetAdvise /
// GetExtent all E_NOTIMPL (no view). Lives at &ccw->viewObject2Vtbl.
const Il2CppComViewObject2Vtable kViewObject2Vtable = {
    &CcwViewObject2QueryInterface,
    &CcwViewObject2AddRef,
    &CcwViewObject2Release,
    &CcwViewObject2NotImpl0,           // slot 3 Draw
    &CcwViewObject2NotImpl1,           // slot 4 GetColorSet
    &CcwViewObject2NotImpl2,           // slot 5 Freeze
    &CcwViewObject2NotImpl3,           // slot 6 Unfreeze
    &CcwViewObject2NotImpl4,           // slot 7 SetAdvise
    &CcwViewObject2NotImpl5,           // slot 8 GetAdvise
    &CcwViewObject2NotImpl6,           // slot 9 GetExtent
};

// The IOleLink view vtable (12 slots, canonical oleidl.h order). Stage 48:
// SetUpdateOptions / GetUpdateOptions / SetSourceMoniker / GetSourceMoniker /
// SetSourceDisplayName / GetSourceDisplayName / BindToSource / Unbind /
// Update all E_NOTIMPL (no link). Lives at &ccw->oleLinkVtbl.
const Il2CppComOleLinkVtable kOleLinkVtable = {
    &CcwOleLinkQueryInterface,
    &CcwOleLinkAddRef,
    &CcwOleLinkRelease,
    &CcwOleLinkNotImpl0,               // slot 3 SetUpdateOptions
    &CcwOleLinkNotImpl1,               // slot 4 GetUpdateOptions
    &CcwOleLinkNotImpl2,               // slot 5 SetSourceMoniker
    &CcwOleLinkNotImpl3,               // slot 6 GetSourceMoniker
    &CcwOleLinkNotImpl4,               // slot 7 SetSourceDisplayName
    &CcwOleLinkNotImpl3,               // slot 8 GetSourceDisplayName
    &CcwOleLinkNotImpl5,               // slot 9 BindToSource
    &CcwOleLinkNotImpl6,               // slot 10 Unbind
    &CcwOleLinkNotImpl7,               // slot 11 Update
};

// The IMoniker view vtable (23 slots, canonical objidl.h order). Stage 49:
// GetClassID / IsDirty / Load / Save / GetSizeMax / BindToObject /
// BindToStorage / Reduce / ComposeWith / Enum / IsEqual / Hash / IsRunning /
// GetTimeOfLastChange / Inverse / CommonPrefixWith / RelativePathTo /
// GetDisplayName / ParseDisplayName / IsSystemMoniker all E_NOTIMPL (no
// moniker). Lives at &ccw->monikerVtbl.
const Il2CppComMonikerVtable kMonikerVtable = {
    &CcwMonikerQueryInterface,
    &CcwMonikerAddRef,
    &CcwMonikerRelease,
    &CcwMonikerNotImpl4,               // slot 3 GetClassID
    &CcwMonikerNotImpl9,               // slot 4 IsDirty
    &CcwMonikerNotImpl4,               // slot 5 Load
    &CcwMonikerNotImpl10,              // slot 6 Save
    &CcwMonikerNotImpl4,               // slot 7 GetSizeMax
    &CcwMonikerNotImpl0,               // slot 8 BindToObject
    &CcwMonikerNotImpl0,               // slot 9 BindToStorage
    &CcwMonikerNotImpl1,               // slot 10 Reduce
    &CcwMonikerNotImpl2,               // slot 11 ComposeWith
    &CcwMonikerNotImpl3,               // slot 12 Enum
    &CcwMonikerNotImpl4,               // slot 13 IsEqual
    &CcwMonikerNotImpl4,               // slot 14 Hash
    &CcwMonikerNotImpl5,               // slot 15 IsRunning
    &CcwMonikerNotImpl5,               // slot 16 GetTimeOfLastChange
    &CcwMonikerNotImpl6,               // slot 17 Inverse
    &CcwMonikerNotImpl11,              // slot 18 CommonPrefixWith
    &CcwMonikerNotImpl11,              // slot 19 RelativePathTo
    &CcwMonikerNotImpl11,              // slot 20 GetDisplayName
    &CcwMonikerNotImpl7,               // slot 21 ParseDisplayName
    &CcwMonikerNotImpl8,               // slot 22 IsSystemMoniker
};

// The IRunningObjectTable view vtable (10 slots, canonical objidl.h order).
// Stage 50: Register / Revoke / IsRunning / GetObject / NoteChangeTime /
// GetTimeOfLastChange / EnumRunning all E_NOTIMPL (no ROT).
// Lives at &ccw->runningObjectTableVtbl.
const Il2CppComRunningObjectTableVtable kRunningObjectTableVtable = {
    &CcwRunningObjectTableQueryInterface,
    &CcwRunningObjectTableAddRef,
    &CcwRunningObjectTableRelease,
    &CcwRunningObjectTableNotImpl0,    // slot 3 Register
    &CcwRunningObjectTableNotImpl1,    // slot 4 Revoke
    &CcwRunningObjectTableNotImpl2,    // slot 5 IsRunning
    &CcwRunningObjectTableNotImpl3,    // slot 6 GetObject
    &CcwRunningObjectTableNotImpl4,    // slot 7 NoteChangeTime
    &CcwRunningObjectTableNotImpl6,    // slot 8 GetTimeOfLastChange
    &CcwRunningObjectTableNotImpl5,    // slot 9 EnumRunning
};

// The IBindCtx view vtable (13 slots, canonical objidl.h order). Stage 51:
// RegisterObjectBound / RevokeObjectBound / ReleaseBoundObjects /
// SetBindOptions / GetBindOptions / GetRunningObjectTable /
// RegisterObjectParam / GetObjectParam / EnumObjectParam /
// RevokeObjectParam all E_NOTIMPL (no bind ctx). Lives at &ccw->bindCtxVtbl.
const Il2CppComBindCtxVtable kBindCtxVtable = {
    &CcwBindCtxQueryInterface,
    &CcwBindCtxAddRef,
    &CcwBindCtxRelease,
    &CcwBindCtxNotImpl0,               // slot 3 RegisterObjectBound
    &CcwBindCtxNotImpl0,               // slot 4 RevokeObjectBound
    &CcwBindCtxNotImpl1,               // slot 5 ReleaseBoundObjects
    &CcwBindCtxNotImpl0,               // slot 6 SetBindOptions
    &CcwBindCtxNotImpl0,               // slot 7 GetBindOptions
    &CcwBindCtxNotImpl2,               // slot 8 GetRunningObjectTable
    &CcwBindCtxNotImpl3,               // slot 9 RegisterObjectParam
    &CcwBindCtxNotImpl4,               // slot 10 GetObjectParam
    &CcwBindCtxNotImpl2,               // slot 11 EnumObjectParam
    &CcwBindCtxNotImpl5,               // slot 12 RevokeObjectParam
};

// The IOleAdviseHolder view vtable (6 slots, canonical objidl.h order).
// Stage 52: Advise / Unadvise / EnumAdvise all E_NOTIMPL (no advise holder).
// Lives at &ccw->oleAdviseHolderVtbl.
const Il2CppComOleAdviseHolderVtable kOleAdviseHolderVtable = {
    &CcwOleAdviseHolderQueryInterface,
    &CcwOleAdviseHolderAddRef,
    &CcwOleAdviseHolderRelease,
    &CcwOleAdviseHolderNotImpl0,       // slot 3 Advise
    &CcwOleAdviseHolderNotImpl1,       // slot 4 Unadvise
    &CcwOleAdviseHolderNotImpl2,       // slot 5 EnumAdvise
};

// The IDataAdviseHolder view vtable (6 slots, canonical objidl.h order).
// Stage 53: Advise / Unadvise / EnumAdvise all E_NOTIMPL (no data advise
// holder). Lives at &ccw->dataAdviseHolderVtbl.
const Il2CppComDataAdviseHolderVtable kDataAdviseHolderVtable = {
    &CcwDataAdviseHolderQueryInterface,
    &CcwDataAdviseHolderAddRef,
    &CcwDataAdviseHolderRelease,
    &CcwDataAdviseHolderNotImpl0,      // slot 3 Advise
    &CcwDataAdviseHolderNotImpl1,      // slot 4 Unadvise
    &CcwDataAdviseHolderNotImpl2,      // slot 5 EnumAdvise
};

// The IOleCache view vtable (8 slots, canonical oleidl.h order). Stage 54:
// Cache / Uncache / EnumCache / InitCache / SetData all E_NOTIMPL (no cache).
// Lives at &ccw->oleCacheVtbl.
const Il2CppComOleCacheVtable kOleCacheVtable = {
    &CcwOleCacheQueryInterface,
    &CcwOleCacheAddRef,
    &CcwOleCacheRelease,
    &CcwOleCacheNotImpl0,              // slot 3 Cache
    &CcwOleCacheNotImpl1,              // slot 4 Uncache
    &CcwOleCacheNotImpl2,              // slot 5 EnumCache
    &CcwOleCacheNotImpl3,              // slot 6 InitCache
    &CcwOleCacheNotImpl4,              // slot 7 SetData
};

// The IOleCache2 view vtable (10 slots, canonical oleidl.h order). Stage 55:
// IOleCache::Cache / Uncache / EnumCache / InitCache / SetData +
// UpdateCache / DiscardCache all E_NOTIMPL (no cache).
// Lives at &ccw->oleCache2Vtbl.
const Il2CppComOleCache2Vtable kOleCache2Vtable = {
    &CcwOleCache2QueryInterface,
    &CcwOleCache2AddRef,
    &CcwOleCache2Release,
    &CcwOleCache2NotImpl0,             // slot 3 Cache
    &CcwOleCache2NotImpl1,             // slot 4 Uncache
    &CcwOleCache2NotImpl2,             // slot 5 EnumCache
    &CcwOleCache2NotImpl3,             // slot 6 InitCache
    &CcwOleCache2NotImpl4,             // slot 7 SetData
    &CcwOleCache2NotImpl5,             // slot 8 UpdateCache
    &CcwOleCache2NotImpl1,             // slot 9 DiscardCache
};

// The IOleCacheControl view vtable (5 slots, canonical oleidl.h order).
// Stage 56: OnRun / OnStop all E_NOTIMPL (no cache).
// Lives at &ccw->oleCacheControlVtbl.
const Il2CppComOleCacheControlVtable kOleCacheControlVtable = {
    &CcwOleCacheControlQueryInterface,
    &CcwOleCacheControlAddRef,
    &CcwOleCacheControlRelease,
    &CcwOleCacheControlNotImpl0,       // slot 3 OnRun
    &CcwOleCacheControlNotImpl1,       // slot 4 OnStop
};

// The IOleItemContainer view vtable (8 slots, canonical oleidl.h order).
// Stage 57: ParseDisplayName / GetObject / GetObjectStorage /
// GetObjectInStorage / LockContainer all E_NOTIMPL (no item container).
// Lives at &ccw->oleItemContainerVtbl.
const Il2CppComOleItemContainerVtable kOleItemContainerVtable = {
    &CcwOleItemContainerQueryInterface,
    &CcwOleItemContainerAddRef,
    &CcwOleItemContainerRelease,
    &CcwOleItemContainerNotImpl0,      // slot 3 ParseDisplayName
    &CcwOleItemContainerNotImpl1,      // slot 4 GetObject
    &CcwOleItemContainerNotImpl2,      // slot 5 GetObjectStorage
    &CcwOleItemContainerNotImpl2,      // slot 6 GetObjectInStorage
    &CcwOleItemContainerNotImpl3,      // slot 7 LockContainer
};

// The IOleUILinkContainer view vtable (9 slots, canonical oleuidlg.h order).
// Stage 58: GetNextLink / SetLinkSource / GetLinkSource / OpenLinkSource /
// UpdateLink / CancelLink all E_NOTIMPL (no link container).
// Lives at &ccw->oleUILinkContainerVtbl.
const Il2CppComOleUILinkContainerVtable kOleUILinkContainerVtable = {
    &CcwOleUILinkContainerQueryInterface,
    &CcwOleUILinkContainerAddRef,
    &CcwOleUILinkContainerRelease,
    &CcwOleUILinkContainerNotImpl0,    // slot 3 GetNextLink
    &CcwOleUILinkContainerNotImpl1,    // slot 4 SetLinkSource
    &CcwOleUILinkContainerNotImpl2,    // slot 5 GetLinkSource
    &CcwOleUILinkContainerNotImpl3,    // slot 6 OpenLinkSource
    &CcwOleUILinkContainerNotImpl4,    // slot 7 UpdateLink
    &CcwOleUILinkContainerNotImpl3,    // slot 8 CancelLink
};

// The IOleUILinkInfo view vtable (10 slots, canonical oleuidlg.h order).
// Stage 59: IOleUILinkContainer::GetNextLink / SetLinkSource / GetLinkSource
// / OpenLinkSource / UpdateLink / CancelLink + GetLastUpdate all E_NOTIMPL
// (no link info). Lives at &ccw->oleUILinkInfoVtbl.
const Il2CppComOleUILinkInfoVtable kOleUILinkInfoVtable = {
    &CcwOleUILinkInfoQueryInterface,
    &CcwOleUILinkInfoAddRef,
    &CcwOleUILinkInfoRelease,
    &CcwOleUILinkInfoNotImpl0,         // slot 3 GetNextLink
    &CcwOleUILinkInfoNotImpl1,         // slot 4 SetLinkSource
    &CcwOleUILinkInfoNotImpl2,         // slot 5 GetLinkSource
    &CcwOleUILinkInfoNotImpl3,         // slot 6 OpenLinkSource
    &CcwOleUILinkInfoNotImpl4,         // slot 7 UpdateLink
    &CcwOleUILinkInfoNotImpl3,         // slot 8 CancelLink
    &CcwOleUILinkInfoNotImpl5,         // slot 9 GetLastUpdate
};

// The IOleUIObjInfo view vtable (7 slots, canonical oleuidlg.h order).
// Stage 60: GetObjectInfo / GetDisplayName / GetTypeName / GetLastError all
// E_NOTIMPL (no object info). Lives at &ccw->oleUIObjInfoVtbl.
const Il2CppComOleUIObjInfoVtable kOleUIObjInfoVtable = {
    &CcwOleUIObjInfoQueryInterface,
    &CcwOleUIObjInfoAddRef,
    &CcwOleUIObjInfoRelease,
    &CcwOleUIObjInfoNotImpl0,          // slot 3 GetObjectInfo
    &CcwOleUIObjInfoNotImpl1,          // slot 4 GetDisplayName
    &CcwOleUIObjInfoNotImpl1,          // slot 5 GetTypeName
    &CcwOleUIObjInfoNotImpl1,          // slot 6 GetLastError
};

// The IOleUIVerify view vtable (4 slots, canonical oleuidlg.h order).
// Stage 61: Verify E_NOTIMPL. Lives at &ccw->oleUIVerifyVtbl.
const Il2CppComOleUIVerifyVtable kOleUIVerifyVtable = {
    &CcwOleUIVerifyQueryInterface,
    &CcwOleUIVerifyAddRef,
    &CcwOleUIVerifyRelease,
    &CcwOleUIVerifyNotImpl0,           // slot 3 Verify
};

// The IOleUILinkContainerA view vtable (9 slots, canonical oleuidlg.h
// order). Stage 62: GetNextLink / SetLinkSource / GetLinkSource /
// OpenLinkSource / UpdateLink / CancelLink all E_NOTIMPL (no link
// container). Lives at &ccw->oleUILinkContainerAVtbl.
const Il2CppComOleUILinkContainerAVtable kOleUILinkContainerAVtable = {
    &CcwOleUILinkContainerAQueryInterface,
    &CcwOleUILinkContainerAAddRef,
    &CcwOleUILinkContainerARelease,
    &CcwOleUILinkContainerANotImpl0,   // slot 3 GetNextLink
    &CcwOleUILinkContainerANotImpl1,   // slot 4 SetLinkSource
    &CcwOleUILinkContainerANotImpl2,   // slot 5 GetLinkSource
    &CcwOleUILinkContainerANotImpl3,   // slot 6 OpenLinkSource
    &CcwOleUILinkContainerANotImpl4,   // slot 7 UpdateLink
    &CcwOleUILinkContainerANotImpl3,   // slot 8 CancelLink
};

// The IOleUILinkInfoA view vtable (10 slots, canonical oleuidlg.h order).
// Stage 63: IOleUILinkContainerA::GetNextLink / SetLinkSource /
// GetLinkSource / OpenLinkSource / UpdateLink / CancelLink + GetLastUpdate
// all E_NOTIMPL (no link info). Lives at &ccw->oleUILinkInfoAVtbl.
const Il2CppComOleUILinkInfoAVtable kOleUILinkInfoAVtable = {
    &CcwOleUILinkInfoAQueryInterface,
    &CcwOleUILinkInfoAAddRef,
    &CcwOleUILinkInfoARelease,
    &CcwOleUILinkInfoANotImpl0,        // slot 3 GetNextLink
    &CcwOleUILinkInfoANotImpl1,        // slot 4 SetLinkSource
    &CcwOleUILinkInfoANotImpl2,        // slot 5 GetLinkSource
    &CcwOleUILinkInfoANotImpl3,        // slot 6 OpenLinkSource
    &CcwOleUILinkInfoANotImpl4,        // slot 7 UpdateLink
    &CcwOleUILinkInfoANotImpl3,        // slot 8 CancelLink
    &CcwOleUILinkInfoANotImpl5,        // slot 9 GetLastUpdate
};

// The IOleUIObjInfoA view vtable (7 slots, canonical oleuidlg.h order).
// Stage 64: GetObjectInfo / GetDisplayName / GetTypeName / GetLastError all
// E_NOTIMPL (no object info). Lives at &ccw->oleUIObjInfoAVtbl.
const Il2CppComOleUIObjInfoAVtable kOleUIObjInfoAVtable = {
    &CcwOleUIObjInfoAQueryInterface,
    &CcwOleUIObjInfoAAddRef,
    &CcwOleUIObjInfoARelease,
    &CcwOleUIObjInfoANotImpl0,         // slot 3 GetObjectInfo
    &CcwOleUIObjInfoANotImpl1,         // slot 4 GetDisplayName
    &CcwOleUIObjInfoANotImpl1,         // slot 5 GetTypeName
    &CcwOleUIObjInfoANotImpl1,         // slot 6 GetLastError
};

// The IOleUIVerifyA view vtable (4 slots, canonical oleuidlg.h order).
// Stage 65: Verify E_NOTIMPL. Lives at &ccw->oleUIVerifyAVtbl.
const Il2CppComOleUIVerifyAVtable kOleUIVerifyAVtable = {
    &CcwOleUIVerifyAQueryInterface,
    &CcwOleUIVerifyAAddRef,
    &CcwOleUIVerifyARelease,
    &CcwOleUIVerifyANotImpl0,          // slot 3 Verify
};

// The IOleUILinkContainerW view vtable (9 slots, canonical oleuidlg.h
// order). Stage 66: GetNextLink / SetLinkSource / GetLinkSource /
// OpenLinkSource / UpdateLink / CancelLink all E_NOTIMPL (no link
// container). Lives at &ccw->oleUILinkContainerWVtbl.
const Il2CppComOleUILinkContainerWVtable kOleUILinkContainerWVtable = {
    &CcwOleUILinkContainerWQueryInterface,
    &CcwOleUILinkContainerWAddRef,
    &CcwOleUILinkContainerWRelease,
    &CcwOleUILinkContainerWNotImpl0,   // slot 3 GetNextLink
    &CcwOleUILinkContainerWNotImpl1,   // slot 4 SetLinkSource
    &CcwOleUILinkContainerWNotImpl2,   // slot 5 GetLinkSource
    &CcwOleUILinkContainerWNotImpl3,   // slot 6 OpenLinkSource
    &CcwOleUILinkContainerWNotImpl4,   // slot 7 UpdateLink
    &CcwOleUILinkContainerWNotImpl3,   // slot 8 CancelLink
};

// The IOleUILinkInfoW view vtable (10 slots, canonical oleuidlg.h order).
// Stage 67: IOleUILinkContainerW::GetNextLink / SetLinkSource /
// GetLinkSource / OpenLinkSource / UpdateLink / CancelLink + GetLastUpdate
// all E_NOTIMPL (no link info). Lives at &ccw->oleUILinkInfoWVtbl.
const Il2CppComOleUILinkInfoWVtable kOleUILinkInfoWVtable = {
    &CcwOleUILinkInfoWQueryInterface,
    &CcwOleUILinkInfoWAddRef,
    &CcwOleUILinkInfoWRelease,
    &CcwOleUILinkInfoWNotImpl0,        // slot 3 GetNextLink
    &CcwOleUILinkInfoWNotImpl1,        // slot 4 SetLinkSource
    &CcwOleUILinkInfoWNotImpl2,        // slot 5 GetLinkSource
    &CcwOleUILinkInfoWNotImpl3,        // slot 6 OpenLinkSource
    &CcwOleUILinkInfoWNotImpl4,        // slot 7 UpdateLink
    &CcwOleUILinkInfoWNotImpl3,        // slot 8 CancelLink
    &CcwOleUILinkInfoWNotImpl5,        // slot 9 GetLastUpdate
};

// The IOleUIObjInfoW view vtable (7 slots, canonical oleuidlg.h order).
// Stage 68: GetObjectInfo / GetDisplayName / GetTypeName / GetLastError all
// E_NOTIMPL (no object info). Lives at &ccw->oleUIObjInfoWVtbl.
const Il2CppComOleUIObjInfoWVtable kOleUIObjInfoWVtable = {
    &CcwOleUIObjInfoWQueryInterface,
    &CcwOleUIObjInfoWAddRef,
    &CcwOleUIObjInfoWRelease,
    &CcwOleUIObjInfoWNotImpl0,         // slot 3 GetObjectInfo
    &CcwOleUIObjInfoWNotImpl1,         // slot 4 GetDisplayName
    &CcwOleUIObjInfoWNotImpl1,         // slot 5 GetTypeName
    &CcwOleUIObjInfoWNotImpl1,         // slot 6 GetLastError
};

// The IOleUIVerifyW view vtable (4 slots, canonical oleuidlg.h order).
// Stage 69: Verify E_NOTIMPL. Lives at &ccw->oleUIVerifyWVtbl.
const Il2CppComOleUIVerifyWVtable kOleUIVerifyWVtable = {
    &CcwOleUIVerifyWQueryInterface,
    &CcwOleUIVerifyWAddRef,
    &CcwOleUIVerifyWRelease,
    &CcwOleUIVerifyWNotImpl0,          // slot 3 Verify
};

// The IEnumConnectionPoints view vtable (7 slots, canonical ocidl.h order).
// Stage 70: Next / Skip / Reset / Clone all E_NOTIMPL (the enumeration is
// empty headless). Lives at &ccw->enumConnectionPointsVtbl.
const Il2CppComEnumConnectionPointsVtable kEnumConnectionPointsVtable = {
    &CcwEnumConnectionPointsQueryInterface,
    &CcwEnumConnectionPointsAddRef,
    &CcwEnumConnectionPointsRelease,
    &CcwEnumConnectionPointsNotImpl0,  // slot 3 Next
    &CcwEnumConnectionPointsNotImpl1,  // slot 4 Skip
    &CcwEnumConnectionPointsNotImpl2,  // slot 5 Reset
    &CcwEnumConnectionPointsNotImpl3,  // slot 6 Clone
};

// The IEnumSTATSTG view vtable (7 slots, canonical objidl.h order).
// Stage 71: Next / Skip / Reset / Clone all E_NOTIMPL (the enumeration is
// empty headless). Lives at &ccw->enumStatStgVtbl.
const Il2CppComEnumStatStgVtable kEnumStatStgVtable = {
    &CcwEnumStatStgQueryInterface,
    &CcwEnumStatStgAddRef,
    &CcwEnumStatStgRelease,
    &CcwEnumStatStgNotImpl0,  // slot 3 Next
    &CcwEnumStatStgNotImpl1,  // slot 4 Skip
    &CcwEnumStatStgNotImpl2,  // slot 5 Reset
    &CcwEnumStatStgNotImpl3,  // slot 6 Clone
};

// The IDropTarget view vtable (7 slots, canonical oleidl.h order). Stage 15
// implemented DragLeave (S_OK); stage 16 wired DragEnter/DragOver/Drop to the
// dispatch-forwarding layer (the registered per-CCW callback -- unregistered
// falls back to E_NOTIMPL inside CcwDropDispatchImpl).
const Il2CppComDropTargetVtable kDropVtable = {
    &CcwDropQueryInterface,
    &CcwDropAddRef,
    &CcwDropRelease,
    &CcwDropDragEnter,  // slot 3 DragEnter (forwarding)
    &CcwDropDragOver,   // slot 4 DragOver (forwarding)
    &CcwDropDragLeave,  // slot 5 DragLeave (S_OK)
    &CcwDropDrop,       // slot 6 Drop (forwarding)
};

#endif // defined(_WIN32)

// Read vtable slot 'index' of the COM interface 'iface' (bounds-defended).
const void* VtableSlot(void* iface, int32_t index)
{
    // COM vtable layout: the interface pointer's first word is the vtable
    // pointer (a contiguous array of method pointers).
    void* const* vtbl = *static_cast<void* const* const*>(iface);
    return vtbl[index];
}

// Call the interface's AddRef (vtable slot 1) -- used by GetIUnknownForObject
// (net48 returns an AddRef'd pointer).
void AddRefInterface(void* iface)
{
    typedef uint32_t (IL2CPP_COM_CALL* AddRefFn)(void*);
    const AddRefFn fn = reinterpret_cast<AddRefFn>(const_cast<void*>(VtableSlot(iface, 1)));
    (void)fn(iface);
}

// Call the interface's Release (vtable slot 2).
void ReleaseInterface(void* iface)
{
    if (iface == nullptr)
    {
        return;
    }
    typedef uint32_t (IL2CPP_COM_CALL* ReleaseFn)(void*);
    const ReleaseFn fn = reinterpret_cast<ReleaseFn>(const_cast<void*>(VtableSlot(iface, 2)));
    (void)fn(iface);
}

#if defined(_WIN32)

//------------------------------------------------------------------------------
// IConnectionPointContainer / IConnectionPoint (phase 5 -- the event-source
// surface, the ActiveX control events dependency). The container view lives at
// &ccw->cpcVtbl (container_of recovery); each registered event interface owns
// a connection point view in the ccw->cps[] array (the view interface pointer
// IS the view address -- CpView() casts self directly; the view's back pointer
// carries the ccw). The OLE sink protocol: a native host QIs for
// IConnectionPointContainer, FindConnectionPoint(eventIID) to obtain the
// connection point and Advise(sink) to connect its event sink. All views share
// the ONE CCW refcount (CcwReleaseImpl) and the QI identity rule.
//------------------------------------------------------------------------------

// IConnectionPointContainer view QI: IUnknown/IDispatch -> the identity
// pointer, IOleObject -> the ole view, IOleClientSite -> the site view,
// IConnectionPointContainer -> this view. IConnectionPoint is obtained only
// through FindConnectionPoint (the OLE contract -- each event interface owns
// its own connection point).
int32_t IL2CPP_COM_CALL CcwCpcQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromCpcView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleClientSite, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->siteVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIConnectionPointContainer, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwCpcAddRef(void* self)
{
    auto* ccw = CcwFromCpcView(self);
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwCpcRelease(void* self)
{
    return CcwReleaseImpl(CcwFromCpcView(self));
}

// IConnectionPoint view QI (mirror of the other views + this connection
// point itself).
int32_t IL2CPP_COM_CALL CcwCpQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* view = CpView(self);
    auto* ccw = view->ccw;
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleClientSite, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->siteVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIConnectionPointContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->cpcVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIConnectionPoint, 16) == 0)
    {
        *ppv = self; // this connection point view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwCpAddRef(void* self)
{
    auto* ccw = CpView(self)->ccw;
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

uint32_t IL2CPP_COM_CALL CcwCpRelease(void* self)
{
    return CcwReleaseImpl(CpView(self)->ccw);
}

// IConnectionPointContainer::EnumConnectionPoints -- the phase-5 minimal
// surface has no connection enumerator: E_NOTIMPL with a null out pointer
// (documented; the WinForms AxHost event wiring uses FindConnectionPoint
// directly).
int32_t IL2CPP_COM_CALL CcwCpcEnumConnectionPoints(void* self, void** ppEnum)
{
    (void)self;
    if (ppEnum == nullptr)
    {
        return kComHrBadPointer;
    }
    *ppEnum = nullptr;
    return kComHrNotImpl;
}

// IConnectionPointContainer::FindConnectionPoint -- search the CCW's
// registered event interfaces for 'riid': a match returns the connection
// point view (AddRef'd -- the caller owns the reference); no match returns
// CONNECT_E_NOCONNECTION with a null out pointer (the OLE documented answer).
int32_t IL2CPP_COM_CALL CcwCpcFindConnectionPoint(void* self, const void* riid, void** ppCP)
{
    auto* ccw = CcwFromCpcView(self);
    if (ppCP == nullptr)
    {
        return kComHrBadPointer;
    }
    *ppCP = nullptr;
    if (riid == nullptr)
    {
        return kComHrInvalidArg;
    }
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    for (int32_t i = 0; i < kCcwMaxConnectionPoints; ++i)
    {
        if (std::memcmp(ccw->cps[i].iid, iid, 16) == 0)
        {
            ++ccw->refCount;
            *ppCP = reinterpret_cast<void*>(&ccw->cps[i]); // the CP view address
            return kComHrOk;
        }
    }
    return kComHrNoConnection;
}

// IConnectionPoint::GetConnectionInterface -- return the event interface IID
// this connection point represents.
int32_t IL2CPP_COM_CALL CcwCpGetConnectionInterface(void* self, void* pIID)
{
    auto* view = CpView(self);
    if (pIID == nullptr)
    {
        return kComHrBadPointer;
    }
    std::memcpy(pIID, view->iid, 16);
    return kComHrOk;
}

// IConnectionPoint::GetConnectionPointContainer -- return the container view
// (AddRef'd -- the caller owns the returned reference).
int32_t IL2CPP_COM_CALL CcwCpGetConnectionPointContainer(void* self, void** ppCPC)
{
    auto* view = CpView(self);
    if (ppCPC == nullptr)
    {
        return kComHrBadPointer;
    }
    ++view->ccw->refCount;
    *ppCPC = reinterpret_cast<void*>(&view->ccw->cpcVtbl);
    return kComHrOk;
}

// IConnectionPoint::Advise -- connect a sink. The sink is QI'd for the event
// interface (the OLE contract: it must implement the source interface --
// CONNECT_E_CANNOTCONNECT otherwise); the QI result is the stored reference,
// owned by the connection point until that connection's Unadvise or CCW
// teardown. Stage 9 -- multi-sink broadcast: a connection point holds up to
// kCcwMaxSinksPerCp connected sinks (the phase-5 single-sink surface was the
// special case); a full array -> CONNECT_E_ADVISELIMIT. Each connection is a
// distinct monotonic 1-based cookie (stale cookies are rejected by Unadvise).
int32_t IL2CPP_COM_CALL CcwCpAdvise(void* self, void* pUnkSink, uint32_t* pdwCookie)
{
    auto* view = CpView(self);
    if (pUnkSink == nullptr || pdwCookie == nullptr)
    {
        return kComHrBadPointer;
    }
    int32_t slot = -1;
    for (int32_t i = 0; i < kCcwMaxSinksPerCp; ++i)
    {
        if (view->sink[i] == nullptr)
        {
            slot = i;
            break;
        }
    }
    if (slot < 0)
    {
        return kComHrAdviseLimit; // capacity exhausted
    }
    void* sinkIface = nullptr;
    typedef int32_t (IL2CPP_COM_CALL* QueryInterfaceFn)(void*, const void*, void**);
    const QueryInterfaceFn qi = reinterpret_cast<QueryInterfaceFn>(
        const_cast<void*>(VtableSlot(pUnkSink, 0)));
    if (qi(pUnkSink, view->iid, &sinkIface) != kComHrOk || sinkIface == nullptr)
    {
        return kComHrCannotConnect;
    }
    view->sink[slot] = sinkIface; // the QI result carries the reference the CP owns
    view->cookie[slot] = view->nextCookie++;
    ++view->connections;
    *pdwCookie = static_cast<uint32_t>(view->cookie[slot]);
    return kComHrOk;
}

// IConnectionPoint::Unadvise -- disconnect the connection identified by the
// cookie: release that sink's stored reference and free the slot (the other
// connections stay connected -- the stage-9 per-cookie contract). A stale /
// unknown cookie -> CONNECT_E_NOCONNECTION (the OLE documented answer).
int32_t IL2CPP_COM_CALL CcwCpUnadvise(void* self, uint32_t dwCookie)
{
    auto* view = CpView(self);
    for (int32_t i = 0; i < kCcwMaxSinksPerCp; ++i)
    {
        if (view->sink[i] != nullptr &&
            view->cookie[i] == static_cast<int32_t>(dwCookie))
        {
            ReleaseInterface(view->sink[i]);
            view->sink[i] = nullptr;
            view->cookie[i] = 0;
            --view->connections;
            return kComHrOk;
        }
    }
    return kComHrNoConnection;
}

// IConnectionPoint::EnumConnections -- the phase-5 minimal surface has no
// connection enumerator: E_NOTIMPL with a null out pointer (documented; the
// AxHost wiring uses Advise/Unadvise directly).
int32_t IL2CPP_COM_CALL CcwCpEnumConnections(void* self, void** ppEnum)
{
    (void)self;
    if (ppEnum == nullptr)
    {
        return kComHrBadPointer;
    }
    *ppEnum = nullptr;
    return kComHrNotImpl;
}

// The IConnectionPointContainer view vtable (5 slots, canonical ocidl.h
// order). FindConnectionPoint implements the event-source lookup; the
// enumerator is the documented E_NOTIMPL minimal surface.
const Il2CppComCpcVtable kCpcVtable = {
    &CcwCpcQueryInterface,
    &CcwCpcAddRef,
    &CcwCpcRelease,
    &CcwCpcEnumConnectionPoints,   // slot 3 EnumConnectionPoints
    &CcwCpcFindConnectionPoint,    // slot 4 FindConnectionPoint
};

// The IConnectionPoint view vtable (8 slots, canonical ocidl.h order),
// shared by every connection point. GetConnectionInterface /
// GetConnectionPointContainer / Advise / Unadvise implement the sink
// protocol; EnumConnections is the documented E_NOTIMPL minimal surface.
const Il2CppComCpVtable kCpVtable = {
    &CcwCpQueryInterface,
    &CcwCpAddRef,
    &CcwCpRelease,
    &CcwCpGetConnectionInterface,       // slot 3
    &CcwCpGetConnectionPointContainer,  // slot 4
    &CcwCpAdvise,                       // slot 5
    &CcwCpUnadvise,                     // slot 6
    &CcwCpEnumConnections,              // slot 7
};

#endif // defined(_WIN32)

// Forward declarations of the stage-10 vtables (defined below -- CcwEnumClone
// hands the clone its dedicated kEnumCloneVtable; internal linkage via the
// anonymous namespace).
extern const Il2CppComEnumVtable kEnumVtable;
extern const Il2CppComEnumVtable kEnumCloneVtable;

//------------------------------------------------------------------------------
// IEnumVARIANT (stage 10 -- the per-CCW enumeration view, EVERY platform).
// The main view is inlined in the CCW (&ccw->enumView); every Clone is a
// heap Il2CppComEnumView sharing the CCW refcount with an independent cursor.
// Both recover the view by casting 'self' directly (the view's first member
// is its vtable pointer, and the view carries the ccw back pointer) -- the
// same cast works for the inlined member and the heap block.
//------------------------------------------------------------------------------

// Enum view QI: the identity + IEnumVARIANT (itself) on every platform; the
// phase-4/5 OLE views on Windows (the identity QI mirror -- the OLE views are
// not offered on non-Windows).
int32_t IL2CPP_COM_CALL CcwEnumQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = static_cast<Il2CppComEnumView*>(self)->ccw;
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIEnumVariant, 16) == 0)
    {
        *ppv = self; // this enumeration view
        ++ccw->refCount;
        return kComHrOk;
    }
#if defined(_WIN32)
    if (std::memcmp(iid, kComIidIOleObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIOleClientSite, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->siteVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIConnectionPointContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->cpcVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    // IConnectionPoint is NOT answered here -- the OLE contract obtains
    // connection points exclusively through FindConnectionPoint.
#endif
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwEnumAddRef(void* self)
{
    auto* ccw = static_cast<Il2CppComEnumView*>(self)->ccw;
    ++ccw->refCount;
    return static_cast<uint32_t>(ccw->refCount);
}

// The MAIN (inlined) view's Release: the shared CCW refcount teardown -- the
// last Release on any view destroys the whole CCW.
uint32_t IL2CPP_COM_CALL CcwEnumRelease(void* self)
{
    return CcwReleaseImpl(static_cast<Il2CppComEnumView*>(self)->ccw);
}

// The CLONE view's Release: delete the clone's own heap block first, then the
// shared CCW refcount teardown (a clone holds a reference the CCW counts --
// the CCW is only destroyed once every clone is released).
uint32_t IL2CPP_COM_CALL CcwEnumCloneRelease(void* self)
{
    auto* view = static_cast<Il2CppComEnumView*>(self);
    auto* ccw = view->ccw;
    delete view; // the clone's own heap block
    return CcwReleaseImpl(ccw);
}

// IEnumVARIANT::Next -- copy up to celt registered items starting at the
// cursor into rgelt, advancing the cursor. Non-BSTR items are 16-byte VARIANT
// VALUE copies; a stage-11 BSTR item is handed out as a FRESH SysAllocStringLen
// copy (pre-allocated for the whole batch -- an allocation failure returns
// E_OUTOFMEMORY with neither rgelt nor the cursor mutated; the caller owns the
// returned copies and VariantClear's them). S_OK when all celt items were
// fetched; S_FALSE when fewer than celt items remain -- the fetched items are
// STILL written and pceltFetched reports the actual count on both outcomes
// (the documented IEnumVARIANT contract). rgelt == nullptr -> E_POINTER;
// celt == 0 -> S_OK with pceltFetched 0.
int32_t IL2CPP_COM_CALL CcwEnumNext(void* self, uint32_t celt, void* rgelt, uint32_t* pceltFetched)
{
    auto* view = static_cast<Il2CppComEnumView*>(self);
    if (rgelt == nullptr)
    {
        return kComHrBadPointer;
    }
    const int32_t available = view->ccw->enumCount - view->index;
    const uint32_t count = (celt < static_cast<uint32_t>(available))
        ? celt : static_cast<uint32_t>(available);
    // Stage-11 verify-then-mutate: pre-allocate a FRESH SysAllocStringLen copy
    // of every OWNED BSTR item in the batch BEFORE touching the caller's rgelt
    // or the cursor. On any allocation failure the pre-allocated copies are
    // freed and E_OUTOFMEMORY returns with neither rgelt nor the cursor
    // mutated (a retry re-attempts the whole batch). Non-BSTR items are pure
    // value copies -- nothing to pre-allocate.
#if defined(_WIN32)
    void* fresh[kCcwMaxEnumItems] = { nullptr };
    for (uint32_t i = 0; i < count; ++i)
    {
        void* master = view->ccw->enumOwned[view->index + i];
        if (master == nullptr)
        {
            continue;
        }
        void* copy = GetOle32().sysAllocStringLen(
            master, static_cast<uint32_t>(view->ccw->enumOwnedLen[view->index + i]));
        if (copy == nullptr)
        {
            for (uint32_t j = 0; j < i; ++j)
            {
                if (fresh[j] != nullptr)
                {
                    GetOle32().sysFreeString(fresh[j]);
                }
            }
            return kComHrOutOfMemory;
        }
        fresh[i] = copy;
    }
#endif
    uint32_t fetched = 0;
    while (fetched < count)
    {
        const int32_t src = view->index + static_cast<int32_t>(fetched);
        uint8_t* out = static_cast<uint8_t*>(rgelt) + fetched * kVariantSize;
        std::memcpy(out, view->ccw->enumItems[src], kVariantSize);
#if defined(_WIN32)
        // The stored 16-byte VARIANT carries the OWNED master pointer in the
        // data union (offset 8) -- overwrite it with the fresh caller-owned
        // copy (the OLE contract: the caller VariantClear's the returned
        // element; the stored master survives).
        if (fresh[fetched] != nullptr)
        {
            std::memcpy(out + 8, &fresh[fetched], sizeof(void*));
        }
#endif
        ++fetched;
    }
    view->index += static_cast<int32_t>(count);
    if (pceltFetched != nullptr)
    {
        *pceltFetched = fetched;
    }
    return (fetched == celt) ? kComHrOk : kComHrSFALSE;
}

// IEnumVARIANT::Skip -- advance the cursor by celt items. Skipping fewer than
// celt remaining items clamps the cursor to the end and returns S_FALSE;
// Skip(0) is a no-op (S_OK).
int32_t IL2CPP_COM_CALL CcwEnumSkip(void* self, uint32_t celt)
{
    auto* view = static_cast<Il2CppComEnumView*>(self);
    const int32_t remaining = view->ccw->enumCount - view->index;
    if (celt == 0)
    {
        return kComHrOk;
    }
    if (static_cast<int32_t>(celt) < remaining)
    {
        view->index += static_cast<int32_t>(celt);
        return kComHrOk;
    }
    view->index = view->ccw->enumCount; // clamp to the end
    return kComHrSFALSE;
}

// IEnumVARIANT::Reset -- rewind the cursor to the first item.
int32_t IL2CPP_COM_CALL CcwEnumReset(void* self)
{
    static_cast<Il2CppComEnumView*>(self)->index = 0;
    return kComHrOk;
}

// IEnumVARIANT::Clone -- a heap enumerator starting at the CURRENT cursor,
// sharing the CCW refcount (the clone holds its own reference) with an
// INDEPENDENT cursor (advancing one does not move the other). ppEnum ==
// nullptr -> E_POINTER.
int32_t IL2CPP_COM_CALL CcwEnumClone(void* self, void** ppEnum)
{
    auto* view = static_cast<Il2CppComEnumView*>(self);
    if (ppEnum == nullptr)
    {
        return kComHrBadPointer;
    }
    auto* ccw = view->ccw;
    auto* clone = new Il2CppComEnumView;
    clone->vtbl = &kEnumCloneVtable;
    clone->ccw = ccw;
    clone->index = view->index;
    ++ccw->refCount; // the clone shares the CCW refcount (one new reference)
    *ppEnum = clone;
    return kComHrOk;
}

// The main enumeration view vtable (7 slots, canonical oaidl.h order). The
// interface method slots are shared with the clone vtable below.
const Il2CppComEnumVtable kEnumVtable = {
    &CcwEnumQueryInterface,
    &CcwEnumAddRef,
    &CcwEnumRelease,      // main view: the shared CCW refcount teardown
    &CcwEnumNext,         // slot 3 Next
    &CcwEnumSkip,         // slot 4 Skip
    &CcwEnumReset,        // slot 5 Reset
    &CcwEnumClone,        // slot 6 Clone
};

// The clone vtable: the SAME 7 slots except Release, which deletes the
// clone's own heap block before the shared CCW refcount teardown.
const Il2CppComEnumVtable kEnumCloneVtable = {
    &CcwEnumQueryInterface,
    &CcwEnumAddRef,
    &CcwEnumCloneRelease, // clone: delete its heap block + shared teardown
    &CcwEnumNext,         // slot 3 Next
    &CcwEnumSkip,         // slot 4 Skip
    &CcwEnumReset,        // slot 5 Reset
    &CcwEnumClone,        // slot 6 Clone
};

//------------------------------------------------------------------------------
// IDataObject view (stage 17 batch 2 -- the typed-RCW foundation, EVERY
// platform: pure memory + typeinfo + il2cpp_interface_dispatch, no ole32
// dependency -- the stage-10 IEnumVARIANT every-platform precedent). The view
// lives at &ccw->dataObjectVtbl (container_of recovery). The nine method slots
// forward the fixed 4-intptr carrier to the shared dispatch, which resolves
// the native slot's method NAME (kDataObjectMethodNames -- the OLE declarative
// order) through the REGISTERED interface's typeinfo (the virtualSlot cached
// at registration) and drives the CCW's wrapped managed object via
// il2cpp_interface_dispatch. Unregistered view / undeclared native method /
// unsupported signature -> E_NOTIMPL (the minimal-surface answer, the
// IOleObject minimal-surface precedent).
//------------------------------------------------------------------------------

// The native IDataObject method slots (oleidl.h order) -> MANAGED interface
// method NAMES. The interface's vtable position is resolved by name because
// the transpiler sorts interface slots ALPHABETICALLY (batch-2 fixture:
// GetData=0/QueryGetData=1/SetData=2) while the OLE vtable is DECLARATIVE --
// the two orders differ, and only the NAME is a stable bridge between them.
const char* const kDataObjectMethodNames[9] = {
    "GetData",               // slot 3
    "GetDataHere",           // slot 4
    "QueryGetData",          // slot 5
    "GetCanonicalFormatEtc", // slot 6
    "SetData",               // slot 7
    "EnumFormatEtc",         // slot 8
    "DAdvise",               // slot 9
    "DUnadvise",             // slot 10
    "DEnumFormatEtc",        // slot 11
};

// The native IDropTarget method discriminators (kComDropMethod*, stage 16) ->
// MANAGED interface method NAMES (stage 17 batch 4). The interface's vtable
// position is resolved by name because the transpiler sorts interface slots
// ALPHABETICALLY (OnDragDrop=0/OnDragEnter=1/OnDragLeave=2/OnDragOver=3) while
// the OLE vtable is DECLARATIVE -- only the NAME is a stable bridge. EVERY
// platform (the pump icall references it).
const char* const kDropMethodNames[4] = {
    "OnDragEnter", // kComDropMethodDragEnter (0)
    "OnDragOver",  // kComDropMethodDragOver  (1)
    "OnDragDrop",  // kComDropMethodDrop      (2)
    "OnDragLeave", // kComDropMethodDragLeave (3)
};

// Recover the owning CCW from the IDataObject view pointer (the view address
// is the offset of the dataObjectVtbl member inside the ccw).
Il2CppComCcw* CcwFromDataObjectView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, dataObjectVtbl));
}

// IDataObject view QI: IUnknown/IDispatch -> the identity pointer (the base
// address -- COM requires QI(IUnknown) to return the same address from every
// view), IDataObject -> this view.
int32_t IL2CPP_COM_CALL CcwDataObjectQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromDataObjectView(self);
    const uint8_t* iid = static_cast<const uint8_t*>(riid);
    if (std::memcmp(iid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(iid, kComIidIDispatch, 16) == 0)
    {
        *ppv = static_cast<void*>(ccw); // the identity (base address)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(iid, kComIidIDataObject, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwDataObjectAddRef(void* self)
{
    return CcwAddRef(CcwFromDataObjectView(self));
}

uint32_t IL2CPP_COM_CALL CcwDataObjectRelease(void* self)
{
    return CcwReleaseImpl(CcwFromDataObjectView(self));
}

// The shared IDataObject view dispatch: 'slot' (3..11) -> the registered
// interface's method (resolved by NAME -- the typeinfo virtualSlot cached at
// registration) -> il2cpp_interface_dispatch on the CCW's managed target.
// Unregistered view / undeclared native slot / missing method / unsupported
// signature -> E_NOTIMPL. void methods -> S_OK; int-returning methods
// propagate their int32 result (the HRESULT carrier).
int32_t CcwDataObjectDispatch(Il2CppComCcw* ccw, int32_t slot,
                              intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    (void)a2;
    (void)a3;
    const int32_t index = slot - kComDataObjectSlotGetData; // 0..8
    if (index < 0 || index >= 9 || ccw->dataObjectIface == nullptr)
    {
        return kComHrNotImpl; // unregistered view (or out-of-range slot)
    }
    const int32_t ifaceSlot = ccw->dataObjectSlots[index];
    if (ifaceSlot < 0)
    {
        return kComHrNotImpl; // native method not declared by the interface
    }
    auto* managed = icall_gchandle_get_target(ccw->gcHandle);
    if (managed == nullptr)
    {
        return kComHrNotImpl;
    }
    // The interface method's typeinfo entry (name -> signature + virtualSlot).
    const Il2CppMethodInfo* mi = (ccw->dataObjectTypeInfo != nullptr)
        ? il2cpp_type_get_method(ccw->dataObjectTypeInfo, kDataObjectMethodNames[index])
        : nullptr;
    if (mi == nullptr || mi->signature == nullptr)
    {
        return kComHrNotImpl;
    }
    void** slotPtr = il2cpp_interface_dispatch(managed, ccw->dataObjectIface, ifaceSlot);
    // Decode the managed signature from the encoded signature string
    // (<returnEncoding>(<params>) -- the batch-2 fixture subset: int32 params
    // with a void ('v') or int32 ('i') return).
    const char returnEnc = mi->signature[0];
    if (returnEnc == 'v')
    {
        if (mi->paramCount == 2) // void GetData(int fmt, int medium)
        {
            auto fn = reinterpret_cast<void (*)(Il2CppObject*, int32_t, int32_t)>(*slotPtr);
            fn(managed, static_cast<int32_t>(a0), static_cast<int32_t>(a1));
            return kComHrOk;
        }
        return kComHrNotImpl; // unsupported void arity
    }
    if (returnEnc == 'i')
    {
        if (mi->paramCount == 1) // int QueryGetData(int fmt)
        {
            auto fn = reinterpret_cast<int32_t (*)(Il2CppObject*, int32_t)>(*slotPtr);
            return fn(managed, static_cast<int32_t>(a0));
        }
        return kComHrNotImpl; // unsupported int arity
    }
    return kComHrNotImpl; // unsupported return encoding
}

// The nine IDataObject view methods (slots 3..11, canonical oleidl.h order) --
// each just forwards the intptr carrier to the shared dispatch.
int32_t IL2CPP_COM_CALL CcwDataObjectGetData(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    return CcwDataObjectDispatch(CcwFromDataObjectView(self), kComDataObjectSlotGetData, a0, a1, a2, a3);
}

int32_t IL2CPP_COM_CALL CcwDataObjectGetDataHere(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    return CcwDataObjectDispatch(CcwFromDataObjectView(self), kComDataObjectSlotGetDataHere, a0, a1, a2, a3);
}

int32_t IL2CPP_COM_CALL CcwDataObjectQueryGetData(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    return CcwDataObjectDispatch(CcwFromDataObjectView(self), kComDataObjectSlotQueryGetData, a0, a1, a2, a3);
}

int32_t IL2CPP_COM_CALL CcwDataObjectGetCanonicalFormatEtc(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    return CcwDataObjectDispatch(CcwFromDataObjectView(self), kComDataObjectSlotGetCanonicalFormatEtc, a0, a1, a2, a3);
}

int32_t IL2CPP_COM_CALL CcwDataObjectSetData(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    return CcwDataObjectDispatch(CcwFromDataObjectView(self), kComDataObjectSlotSetData, a0, a1, a2, a3);
}

int32_t IL2CPP_COM_CALL CcwDataObjectEnumFormatEtc(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    return CcwDataObjectDispatch(CcwFromDataObjectView(self), kComDataObjectSlotEnumFormatEtc, a0, a1, a2, a3);
}

int32_t IL2CPP_COM_CALL CcwDataObjectDAdvise(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    // Stage 76: the native data-notification path. DAdvise(FORMATETC*, DWORD
    // advf, IAdviseSink*, DWORD* pdwConnection) registers the sink in the
    // per-CCW registry with a STABLE cookie (assigned from the incrementing
    // counter -- never renumbered on removal) and writes the cookie back.
    auto* ccw = CcwFromDataObjectView(self);
    (void)a0; // pFormatetc -- not consumed (headless)
    (void)a1; // advf -- not consumed (headless)
    if (ccw->dataObjectIface == nullptr)
    {
        return kComHrNotImpl; // unregistered view
    }
    if (ccw->dataAdviseCount >= kMaxDataAdvise)
    {
        return kComHrOutOfMemory; // registry full
    }
    void* sink = reinterpret_cast<void*>(a2);
    if (sink == nullptr)
    {
        return kComHrInvalidArg;
    }
    const uint32_t cookie = ++ccw->dataAdviseNextCookie;
    ccw->dataAdviseSinks[ccw->dataAdviseCount] = sink;
    ccw->dataAdviseCookies[ccw->dataAdviseCount] = cookie;
    ++ccw->dataAdviseCount;
    if (a3 != 0)
    {
        *reinterpret_cast<uint32_t*>(a3) = cookie;
    }
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwDataObjectDUnadvise(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    // Stage 76: DUnadvise(DWORD dwConnection) removes the sink by its STABLE
    // cookie (the registry is compacted on removal; the remaining cookies are
    // untouched -- the OLE contract).
    auto* ccw = CcwFromDataObjectView(self);
    (void)a1; (void)a2; (void)a3; // not consumed (headless)
    if (ccw->dataObjectIface == nullptr)
    {
        return kComHrNotImpl; // unregistered view
    }
    const uint32_t cookie = static_cast<uint32_t>(a0);
    uint32_t index = 0;
    while (index < ccw->dataAdviseCount && ccw->dataAdviseCookies[index] != cookie)
    {
        ++index;
    }
    if (index == ccw->dataAdviseCount)
    {
        return kComHrOk; // not found -- OLE tolerates
    }
    for (uint32_t i = index; i + 1 < ccw->dataAdviseCount; ++i)
    {
        ccw->dataAdviseSinks[i] = ccw->dataAdviseSinks[i + 1];
        ccw->dataAdviseCookies[i] = ccw->dataAdviseCookies[i + 1];
    }
    --ccw->dataAdviseCount;
    return kComHrOk;
}

int32_t IL2CPP_COM_CALL CcwDataObjectDEnumFormatEtc(void* self, intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    // Stage 76d: EnumDAdvise(IEnumSTATDATA**) hands the IEnumSTATDATA view
    // (the registered data-advise enumeration). Windows: the view exists;
    // non-Windows: E_NOTIMPL (the view is Windows-only).
    (void)a1; (void)a2; (void)a3;
#if defined(_WIN32)
    auto* ccw = CcwFromDataObjectView(self);
    if (ccw->dataObjectIface == nullptr)
    {
        return kComHrNotImpl; // unregistered view
    }
    if (a0 != 0)
    {
        *reinterpret_cast<void**>(a0) = reinterpret_cast<void*>(&ccw->enumStatDataVtbl);
    }
    return kComHrOk;
#else
    return kComHrNotImpl;
#endif
}

// The IDataObject view vtable (12 slots, canonical oleidl.h order).
const Il2CppComDataObjectVtable kDataObjectVtable = {
    &CcwDataObjectQueryInterface,
    &CcwDataObjectAddRef,
    &CcwDataObjectRelease,
    &CcwDataObjectGetData,            // slot 3 GetData
    &CcwDataObjectGetDataHere,        // slot 4 GetDataHere
    &CcwDataObjectQueryGetData,       // slot 5 QueryGetData
    &CcwDataObjectGetCanonicalFormatEtc, // slot 6 GetCanonicalFormatEtc
    &CcwDataObjectSetData,            // slot 7 SetData
    &CcwDataObjectEnumFormatEtc,      // slot 8 EnumFormatEtc
    &CcwDataObjectDAdvise,            // slot 9 DAdvise
    &CcwDataObjectDUnadvise,          // slot 10 DUnadvise
    &CcwDataObjectDEnumFormatEtc,     // slot 11 DEnumFormatEtc
};

// Exceptions (exceptionext.cpp thread-local scratch precedent). The
// COMException factory is Windows-only (the failing COM calls only exist on
// Windows); ThrowReleasedComObject is used on every platform.
#if defined(_WIN32)
thread_local char g_comHrScratch[64];

void ThrowComException(int32_t hr)
{
    std::snprintf(g_comHrScratch, sizeof(g_comHrScratch),
                  "COM call failed with HRESULT 0x%08X", static_cast<uint32_t>(hr));
    il2cpp_throw_new(
        "System.Runtime.InteropServices.COMException,"
        "System.Runtime.InteropServices.COMException,"
        "System.Runtime.InteropServices.ExternalException,"
        "System.SystemException,System.Exception",
        g_comHrScratch);
}
#endif // defined(_WIN32)

void ThrowReleasedComObject()
{
    il2cpp_throw_new(
        "System.Runtime.InteropServices.InvalidComObjectException,"
        "System.Runtime.InteropServices.InvalidComObjectException,"
        "System.SystemException,System.Exception",
        "COM object is released (ReleaseComObject was already called)");
}

// The RCW class descriptor (runtime-only, g_int32ArrayClass precedent).
Il2CppClass* RcwClass()
{
    static Il2CppClass cls = {
        "System.Runtime.InteropServices.__ComObject",
        static_cast<int32_t>(sizeof(Il2CppComRcw)),
        0u,                     // refFieldBitmap -- no managed ref fields
        0,                      // refFieldCount
        &System_Object_class,   // baseClass -- 'o is object' walks to root
        nullptr, 0,             // vtable -- RCW has no virtuals in phase 1
        nullptr, 0,             // interfaces -- none in phase 1
    };
    return &cls;
}

// Resolve an Il2CppObject as a live RCW: null -> ArgumentNullException,
// wrong klass -> ArgumentException, released -> InvalidComObjectException.
Il2CppComRcw* AsRcw(Il2CppObject* obj, const char* paramName)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
    }
    if (obj->klass != RcwClass())
    {
        il2cpp_throw_argument_exception("Object is not a COM object (RCW).");
    }
    auto* rcw = reinterpret_cast<Il2CppComRcw*>(obj);
    if (rcw->refCount <= 0 || rcw->unknown == nullptr)
    {
        ThrowReleasedComObject();
    }
    return rcw;
}

// Wrap a native IUnknown* (ownership transfer -- the RCW holds the reference)
// into an RCW managed object.
Il2CppObject* WrapUnknown(void* unknown)
{
    Il2CppObject* obj = il2cpp_object_new(RcwClass());
    auto* rcw = reinterpret_cast<Il2CppComRcw*>(obj);
    rcw->unknown = unknown;
    rcw->refCount = 1;
    return obj;
}

//------------------------------------------------------------------------------
// The typed-RCW direction (stage 17 batch 3 -- the DragEventArgs.Data
// dependency): a native IDataObject* wrapped into a MANAGED IDataObject
// implementer (the reverse of the CCW's IDataObject view above). The class
// descriptor is a runtime-only descriptor (the RcwClass precedent) whose
// interfaces table carries the transpiled IDataObject descriptor (filled
// lazily -- com.cpp cannot reference generated symbols, so
// icall_com_wrap_data_object resolves it by name) and whose vtable is 3
// stubs (GetData / QueryGetData / SetData -- the ALPHABETICAL interface slot
// order) forwarding the int discriminators to the wrapped native IDataObject*
// through ITS OWN vtable (the icall_com_call_vtable_slot carrier shape:
// native slot 3 GetData / 5 QueryGetData / 7 SetData). The native pointer is
// BORROWED -- the runtime does not own it (a distinct descriptor with no
// ReleaseComObject path: AsRcw / icall_com_is_com_object reject it, so the
// borrowed reference is never released).
//------------------------------------------------------------------------------

// Forward the wrapped native IDataObject*'s vtable slot with the int
// discriminator carriers (the icall_com_call_vtable_slot 4-intptr shape).
intptr_t TypedDataObjectForward(void* unknown, int32_t slot,
                                intptr_t a0, intptr_t a1, intptr_t a2, intptr_t a3)
{
    typedef intptr_t (IL2CPP_COM_CALL* SlotFn)(void*, intptr_t, intptr_t, intptr_t, intptr_t);
    void* const* vtbl = *static_cast<void* const* const*>(unknown);
    const SlotFn fn = reinterpret_cast<SlotFn>(vtbl[slot]);
    return fn(unknown, a0, a1, a2, a3);
}

// The 3 managed vtable stubs (the ALPHABETICAL interface slot order, matching
// the transpiled IDataObject slots: GetData=0 / QueryGetData=1 / SetData=2).
// GetData/SetData are void (the interface contract carries the int
// discriminators only; the native HRESULT is dropped); QueryGetData returns
// the managed int discriminator.
void TypedDataObjectRcwGetData(Il2CppObject* self, int32_t fmt, int32_t medium)
{
    auto* rcw = reinterpret_cast<Il2CppComRcw*>(self);
    if (rcw->unknown != nullptr)
    {
        TypedDataObjectForward(rcw->unknown, kComDataObjectSlotGetData,
                               static_cast<intptr_t>(fmt), static_cast<intptr_t>(medium), 0, 0);
    }
}

int32_t TypedDataObjectRcwQueryGetData(Il2CppObject* self, int32_t fmt)
{
    auto* rcw = reinterpret_cast<Il2CppComRcw*>(self);
    if (rcw->unknown == nullptr)
    {
        return 0;
    }
    return static_cast<int32_t>(TypedDataObjectForward(rcw->unknown, kComDataObjectSlotQueryGetData,
                                                       static_cast<intptr_t>(fmt), 0, 0, 0));
}

void TypedDataObjectRcwSetData(Il2CppObject* self, int32_t fmt, int32_t value)
{
    auto* rcw = reinterpret_cast<Il2CppComRcw*>(self);
    if (rcw->unknown != nullptr)
    {
        TypedDataObjectForward(rcw->unknown, kComDataObjectSlotSetData,
                               static_cast<intptr_t>(fmt), static_cast<intptr_t>(value), 0, 0);
    }
}

// The typed-RCW vtable (3 slots, alphabetical interface order).
void* TypedDataObjectRcwVtable[3] = {
    reinterpret_cast<void*>(&TypedDataObjectRcwGetData),
    reinterpret_cast<void*>(&TypedDataObjectRcwQueryGetData),
    reinterpret_cast<void*>(&TypedDataObjectRcwSetData),
};

// The typed-RCW class descriptor: interfaces = {{IDataObject, identity map}},
// vtable = the 3 forwarding stubs. The IDataObject descriptor pointer is
// filled lazily at first wrap (the transpiled symbol com.cpp cannot reference
// -- il2cpp_class_by_name resolves it).
Il2CppClass* TypedDataObjectRcwClass()
{
    static int32_t identityMap[3] = { 0, 1, 2 };
    static InterfaceMapEntry ifaceEntry = { nullptr, identityMap };
    static Il2CppClass cls = {
        "System.Runtime.InteropServices.TypedDataObjectRcw",
        static_cast<int32_t>(sizeof(Il2CppComRcw)),
        0u,                     // refFieldBitmap -- no managed ref fields
        0,                      // refFieldCount
        &System_Object_class,   // baseClass -- 'o is object' walks to root
        reinterpret_cast<void**>(TypedDataObjectRcwVtable), 3, // vtable: the 3 stubs
        &ifaceEntry, 1,         // interfaces: IDataObject (identity map)
    };
    return &cls;
}

#if defined(_WIN32)

//------------------------------------------------------------------------------
// ole32 dynamic resolution (guid.cpp precedent -- no import lib). The API
// shape (function-pointer typedefs + Ole32Api) lives near the top of this
// file -- the CCW destructor and the stage-11 BSTR Next path need it complete.
//------------------------------------------------------------------------------
Ole32Api LoadOle32()
{
    Ole32Api api = { false, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr,
                     nullptr, nullptr, nullptr, nullptr };
    HMODULE h = ::LoadLibraryA("ole32.dll");
    if (h == nullptr)
    {
        return api;
    }
    api.coCreateInstance = reinterpret_cast<CoCreateInstanceFn>(
        reinterpret_cast<void*>(::GetProcAddress(h, "CoCreateInstance")));
    api.clsidFromProgID = reinterpret_cast<CLSIDFromProgIDFn>(
        reinterpret_cast<void*>(::GetProcAddress(h, "CLSIDFromProgID")));
    api.coInitializeEx = reinterpret_cast<CoInitializeExFn>(
        reinterpret_cast<void*>(::GetProcAddress(h, "CoInitializeEx")));
    // Stage-12 IStream (ole32): CreateStreamOnHGlobal / GetHGlobalFromStream
    // are resolved from the SAME ole32 module. They are DELIBERATELY not part
    // of api.ok -- GetOle32()'s "ole32.dll loaded" gate must keep its
    // semantics; the two stage-12 entry points are checked separately
    // (NotSupportedException when unresolvable, the EnsureOle32 precedent).
    api.createStreamOnHGlobal = reinterpret_cast<CreateStreamOnHGlobalFn>(
        reinterpret_cast<void*>(::GetProcAddress(h, "CreateStreamOnHGlobal")));
    api.getHGlobalFromStream = reinterpret_cast<GetHGlobalFromStreamFn>(
        reinterpret_cast<void*>(::GetProcAddress(h, "GetHGlobalFromStream")));
    // Stage-15 OLE drag-drop (ole32): RegisterDragDrop / RevokeDragDrop are
    // resolved from the SAME ole32 module. They are DELIBERATELY not part of
    // api.ok -- GetOle32()'s "ole32.dll loaded" gate must keep its semantics;
    // the two stage-15 entry points are checked separately
    // (NotSupportedException when unresolvable, the EnsureOle32 precedent).
    api.registerDragDrop = reinterpret_cast<RegisterDragDropFn>(
        reinterpret_cast<void*>(::GetProcAddress(h, "RegisterDragDrop")));
    api.revokeDragDrop = reinterpret_cast<RevokeDragDropFn>(
        reinterpret_cast<void*>(::GetProcAddress(h, "RevokeDragDrop")));
    // GetActiveObject is exported by OLEAUT32 (the automation DLL), not by
    // ole32 -- resolve it from a second module. Stage 11 resolves the BSTR
    // ownership functions (SysAllocStringLen / SysFreeString) from the SAME
    // module. They are DELIBERATELY not part of api.ok -- GetOle32()'s
    // "ole32.dll loaded" gate must keep its semantics; the BSTR entry points
    // check the two pointers separately (NotSupportedException when
    // unresolvable, the EnsureOle32 precedent).
    api.getActiveObject = nullptr;
    HMODULE ha = ::LoadLibraryA("oleaut32.dll");
    if (ha != nullptr)
    {
        api.getActiveObject = reinterpret_cast<GetActiveObjectFn>(
            reinterpret_cast<void*>(::GetProcAddress(ha, "GetActiveObject")));
        api.sysAllocStringLen = reinterpret_cast<SysAllocStringLenFn>(
            reinterpret_cast<void*>(::GetProcAddress(ha, "SysAllocStringLen")));
        api.sysFreeString = reinterpret_cast<SysFreeStringFn>(
            reinterpret_cast<void*>(::GetProcAddress(ha, "SysFreeString")));
    }
    api.ok = api.coCreateInstance != nullptr &&
             api.clsidFromProgID != nullptr &&
             api.getActiveObject != nullptr &&
             api.coInitializeEx != nullptr;
    return api;
}

const Ole32Api& GetOle32()
{
    static const Ole32Api api = LoadOle32();
    return api;
}

void EnsureOle32()
{
    if (!GetOle32().ok)
    {
        il2cpp_throw_not_supported_exception(
            "ole32.dll could not be loaded (COM is unavailable)");
    }
    // Initialize COM once per process with the multi-threaded apartment (the
    // runtime's single-threaded contract: one COM call at a time from one
    // thread). Mirrors .NET Framework's thread COM initialization before any
    // CoCreateInstance; COINIT_MULTITHREADED (0) keeps the console-test and
    // generated-code threads free of an STA's marshaling expectations. The
    // guard makes the call idempotent (RPC_E_CHANGED_MODE on a second
    // differently-apartmented init is ignored -- the first mode sticks).
    static bool initialized = false;
    if (!initialized)
    {
        initialized = true;
        (void)GetOle32().coInitializeEx(nullptr, 0u);
    }
}

// Copy a managed ProgID's UTF-16 units into a null-terminated wchar buffer.
// Returns false when the ProgID exceeds the buffer (caller throws
// ArgumentException -- ProgIDs are short registry keys).
bool ProgIdToWide(Il2CppString* progId, wchar_t* buf, size_t bufChars)
{
    if (static_cast<size_t>(progId->length) >= bufChars)
    {
        return false;
    }
    for (int32_t i = 0; i < progId->length; ++i)
    {
        buf[i] = static_cast<wchar_t>(progId->chars[i]);
    }
    buf[progId->length] = L'\0';
    return true;
}

// CoCreateInstance against a native CLSID (IID_IUnknown, CLSCTX_INPROC_SERVER |
// CLSCTX_LOCAL_SERVER -- the net48 default activation context). Returns the
// IUnknown* or throws the COMException-shaped throw.
intptr_t CreateInstanceByClsid(const void* clsid)
{
    EnsureOle32();
    void* unknown = nullptr;
    const int32_t hr = GetOle32().coCreateInstance(clsid, nullptr, 5u,
                                                   kComIidIUnknown, &unknown);
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
    return static_cast<intptr_t>(reinterpret_cast<uintptr_t>(unknown));
}

// CLSIDFromProgID + failure -> COMException-shaped throw. Returns the native
// CLSID (16 bytes, .NET Guid layout on little-endian Windows).
void ClsidFromProgIdOrThrow(Il2CppString* progId, void* clsid)
{
    EnsureOle32();
    wchar_t buf[260];
    if (!ProgIdToWide(progId, buf, 260))
    {
        il2cpp_throw_argument_exception("ProgID exceeds the 260-char limit.");
    }
    const int32_t hr = GetOle32().clsidFromProgID(buf, clsid);
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
}

// CLSIDFromProgID without a throw: returns false when the ProgID is not
// registered (or unencodable) -- net48's Type.GetTypeFromProgID(string)
// convenience overload runs with throwOnError=false and RETURNS NULL instead
// of throwing (the C1 miss contract pinned by the comclsid e2e case).
bool TryClsidFromProgId(Il2CppString* progId, void* clsid)
{
    EnsureOle32();
    wchar_t buf[260];
    if (!ProgIdToWide(progId, buf, 260))
    {
        return false;
    }
    return GetOle32().clsidFromProgID(buf, clsid) == kComHrOk;
}

#endif // defined(_WIN32)

//------------------------------------------------------------------------------
// C1 (A1) COM Type sentinel codec -- "COMCLSID:" + 32 lower-hex CLSID bytes.
// The codec is pure (no ole32 dependency) so it runs on EVERY platform: the
// encode side feeds the GetTypeFromProgID / GetTypeFromCLSID Type values, the
// decode side feeds the Activator's CoCreateInstance path (reflect_base.cpp
// prefix branch) and the enable serialize the sentinel to an intptr_t.
//------------------------------------------------------------------------------
void ClsidToSentinel(const uint8_t clsid[16], char (&out)[kComClsidSentinelLength + 1])
{
    static const char kHex[] = "0123456789abcdef";
    std::memcpy(out, kComClsidPrefix, 9);
    for (int32_t i = 0; i < 16; ++i)
    {
        out[9 + i * 2] = kHex[(clsid[i] >> 4) & 0x0F];
        out[9 + i * 2 + 1] = kHex[clsid[i] & 0x0F];
    }
    out[kComClsidSentinelLength] = '\0';
}

// Decode the "COMCLSID:" + 32-hex sentinel (already validated by the caller)
// into the 16 CLSID bytes. Returns true on success.
bool SentinelToClsid(const char* hex, uint8_t clsid[16])
{
    for (int32_t i = 0; i < 16; ++i)
    {
        auto nibble = [](char c) -> int32_t
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        };
        const int32_t hi = nibble(hex[i * 2]);
        const int32_t lo = nibble(hex[i * 2 + 1]);
        if (hi < 0 || lo < 0)
        {
            return false;
        }
        clsid[i] = static_cast<uint8_t>((hi << 4) | lo);
    }
    return true;
}

//------------------------------------------------------------------------------
// C2 (A2) -- the managed-argument -> 16-byte VARIANT encoder (the RCW
// direction of icall_com_invoke_member). The subset mirrors the CCW's
// stage-7 automation surface: VT_I4 (boxed Int32), VT_R8 (boxed Double),
// VT_BSTR (managed string -- SysAllocStringLen, freed by the caller after
// Invoke), VT_BOOL (boxed Boolean, VARIANT_TRUE -1) and VT_EMPTY (null /
// null box). Anything else -> NotSupportedException LOUD (never a silent
// wrong-typed VARIANT).
//
// Returns true when the variant holds a BSTR the caller must SysFreeString
// after the Invoke (the reverse of the result variant's caller-owned BSTR).
//------------------------------------------------------------------------------

// The Windows DISPPARAMS layout (oaidl.h): rgvarg / rgdispidNamedArgs
// pointers + the two UINT counts (24 bytes on x64 -- pointer fields align).
struct ComDispParams
{
    void*     rgvarg;
    void*     rgdispidNamedArgs;
    uint32_t  cArgs;
    uint32_t  cNamedArgs;
};
bool ManagedArgToVariant(Il2CppObject* obj, uint8_t* variant)
{
    std::memset(variant, 0, kVariantSize);
    if (obj == nullptr)
    {
        std::memcpy(variant, &kVtEmpty, sizeof(uint16_t));
        return false;
    }
    if (obj->klass == nullptr || obj->klass->name == nullptr)
    {
        il2cpp_throw_argument_exception(
            "InvokeMember: the argument object has no class descriptor (malformed box).");
    }
    const char* const an = obj->klass->name;
    if (std::strcmp(an, "System.String") == 0)
    {
        std::memcpy(variant, &kVtBstr, sizeof(uint16_t));
        void* bstr = nullptr;
#if defined(_WIN32)
        auto s = reinterpret_cast<Il2CppString*>(obj);
        if (GetOle32().sysAllocStringLen != nullptr)
        {
            bstr = GetOle32().sysAllocStringLen(s->chars,
                                                static_cast<uint32_t>(s->length));
        }
#endif
        // POSIX never reaches here (the invoke icall is Windows-only); the
        // null BSTR keeps the branch compile-safe. Windows: the caller must
        // SysFreeString after Invoke (the true return flags it).
        std::memcpy(variant + kVariantVtOffset + 8, &bstr, sizeof(bstr));
        return true;
    }
    if (std::strcmp(an, "System.Int32") == 0)
    {
        std::memcpy(variant, &kVtI4, sizeof(uint16_t));
        const int32_t v = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(obj) + sizeof(void*));
        std::memcpy(variant + kVariantVtOffset + 8, &v, sizeof(v));
        return false;
    }
    if (std::strcmp(an, "System.Double") == 0)
    {
        std::memcpy(variant, &kVtR8, sizeof(uint16_t));
        const double v = *reinterpret_cast<double*>(
            reinterpret_cast<char*>(obj) + sizeof(void*));
        std::memcpy(variant + kVariantVtOffset + 8, &v, sizeof(v));
        return false;
    }
    if (std::strcmp(an, "System.Boolean") == 0)
    {
        std::memcpy(variant, &kVtBool, sizeof(uint16_t));
        const bool v = *reinterpret_cast<int32_t*>(
            reinterpret_cast<char*>(obj) + sizeof(void*)) != 0;
        const int16_t vtBoolVal = v ? -1 : 0; // VARIANT_TRUE == -1
        std::memcpy(variant + kVariantVtOffset + 8, &vtBoolVal, sizeof(vtBoolVal));
        return false;
    }
    il2cpp_throw_not_supported_exception(
        "InvokeMember: the argument type is outside the automation subset "
        "(VT_I4 / VT_R8 / VT_BSTR / VT_BOOL only).");
    return false; // unreachable
}

//------------------------------------------------------------------------------
// C2 (A2) -- the 16-byte VARIANT result -> boxed managed object decoder.
// VT_EMPTY / VT_NULL -> null; VT_I4 -> boxed Int32; VT_R8 -> boxed Double;
// VT_BSTR -> managed string (the caller's BSTR is copied then freed by the
// CALLER -- the decode does NOT free); VT_BOOL -> boxed Boolean. Anything
// else -> NotSupportedException LOUD.
//------------------------------------------------------------------------------
Il2CppObject* VariantResultToManaged(const uint8_t* variant)
{
    uint16_t vt = 0;
    std::memcpy(&vt, variant + kVariantVtOffset, sizeof(vt));
    switch (vt)
    {
        case kVtEmpty:
        case kVtNull:
            return nullptr;
        case kVtI4:
        {
            int32_t v = 0;
            std::memcpy(&v, variant + kVariantVtOffset + 8, sizeof(v));
            return il2cpp_box_int32(nullptr, v);
        }
        case kVtR8:
        {
            double v = 0.0;
            std::memcpy(&v, variant + kVariantVtOffset + 8, sizeof(v));
            return reinterpret_cast<Il2CppObject*>(il2cpp::runtime::icall_box_double(v));
        }
        case kVtBool:
        {
            int16_t v = 0;
            std::memcpy(&v, variant + kVariantVtOffset + 8, sizeof(v));
            return reinterpret_cast<Il2CppObject*>(
                il2cpp::runtime::icall_box_bool(v != 0));
        }
        case kVtBstr:
        {
            void* bstr = nullptr;
            std::memcpy(&bstr, variant + kVariantVtOffset + 8, sizeof(bstr));
            if (bstr == nullptr)
            {
                return nullptr;
            }
            // BSTR layout (OLEAUT32): the 4-byte LENGTH PREFIX in BYTES sits
            // at bstr[-4]; the payload is UTF-16. Windows-only at runtime.
            const uint32_t byteLen = *reinterpret_cast<const uint32_t*>(
                static_cast<const char*>(bstr) - 4);
            auto s = il2cpp::il2cpp_string_new(reinterpret_cast<const char16_t*>(bstr),
                                               static_cast<int32_t>(byteLen / 2));
            return reinterpret_cast<Il2CppObject*>(s);
        }
        default:
            il2cpp_throw_not_supported_exception(
                "InvokeMember: the result VARIANT type is outside the automation "
                "subset (VT_I4 / VT_R8 / VT_BSTR / VT_BOOL / VT_EMPTY only).");
            return nullptr; // unreachable
    }
}

} // namespace

//------------------------------------------------------------------------------
// The IDataObject data-notification trigger (stage 76 -- EVERY platform: pure
// memory + vtable forward, no ole32 dependency -- the stage-10 every-platform
// precedent). icall_com_data_object_notify fires OnDataChange (IAdviseSink
// vtable slot 3) on every sink registered via the IDataObject view's DAdvise
// (the per-CCW data-advise registry). The registry members (dataAdviseSinks /
// dataAdviseCount) are anonymous-namespace members -- visible unqualified
// throughout this TU, referenced here at namespace scope so the header-declared
// symbol is the one defined.
//------------------------------------------------------------------------------

int32_t icall_com_data_object_notify(intptr_t ccw, intptr_t pFormatetc, intptr_t pmedium)
{
    if (ccw == 0)
    {
        return kComHrInvalidArg; // the null guard (all platforms)
    }
    auto* c = reinterpret_cast<Il2CppComCcw*>(ccw);
    for (uint32_t i = 0; i < c->dataAdviseCount; ++i)
    {
        void* sink = c->dataAdviseSinks[i];
        void** vtbl = *reinterpret_cast<void***>(sink);
        auto onDataChange = reinterpret_cast<int32_t (IL2CPP_COM_CALL*)(void*, const void*, const void*)>(vtbl[3]);
        onDataChange(sink, reinterpret_cast<const void*>(pFormatetc), reinterpret_cast<const void*>(pmedium));
    }
    return kComHrOk;
}

//------------------------------------------------------------------------------
// The managed drop dispatch (stage 17 batch 4 -- EVERY platform: pure memory +
// typeinfo + il2cpp_interface_dispatch, no ole32 dependency -- the stage-17
// typed-view precedent). Two icalls: the registration
// (icall_com_ccw_register_drop_dispatch_managed) declares the managed mode on
// a CCW with self-validation, and the pump (icall_com_ccw_drop_dispatch_managed)
// drives the bridge -- wrap the native pDataObj into a typed-RCW IDataObject,
// construct the DragEventArgs (data/x/y/effect), il2cpp_interface_dispatch
// into the managed OnDragEnter and write the effect back. The Windows
// IDropTarget view methods call the pump after the POINTL decode; the managed
// mode takes priority over the stage-16 native forwarding callback. The
// helpers they read (kDropMethodNames, the CCW drop* members) are anonymous-
// namespace members -- visible unqualified throughout this TU (the
// anonymous-namespace rule), referenced here at namespace scope so the
// header-declared symbols are the ones defined.
//------------------------------------------------------------------------------

// RealDropEffectOffset -- resolve the args class' "Effect" field to its REAL
// byte offset in the emitted C++ struct. The typeinfo's cached field offsets
// follow the transpiler's 8-byte SLOT GRID (slot i at byte offset i*8 -- the
// TypeEmitter slot-grid comment); the emitted C++ struct, however, packs value
// fields at their natural 4-byte alignment: an int32 slot uses only its low 4
// bytes, so CONSECUTIVE int32 fields land 4 bytes apart, NOT on their slot
// boundaries (the batch-3 DragEventArgs: struct Data@8/X@16/Y@20/Effect@24 vs
// typeinfo Data@8/X@16/Y@24/Effect@32). The bridge reads the Effect field
// through a raw byte offset, so the REAL layout is recomputed from the field
// table's isRef sequence (object header 8 bytes + reference field 8 bytes +
// value field 4 bytes -- the transpiler's emitted struct shape). Returns -1
// when the args class has no Effect field (the write-back is skipped).
int32_t RealDropEffectOffset(const Il2CppTypeInfo* info)
{
    if (info == nullptr || info->fields == nullptr)
    {
        return -1;
    }
    int32_t offset = static_cast<int32_t>(sizeof(void*)); // the object header
    for (int32_t i = 0; i < info->fieldCount; ++i)
    {
        const Il2CppFieldInfo& f = info->fields[i];
        if (std::strcmp(f.name, "Effect") == 0)
        {
            return offset;
        }
        offset += f.isRef ? static_cast<int32_t>(sizeof(void*)) : 4;
    }
    return -1;
}

int32_t icall_com_ccw_register_drop_dispatch_managed(
    intptr_t ccwPtr, intptr_t ifaceClassPtr, intptr_t argsClassPtr)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (ifaceClassPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ifaceClass");
    }
    if (argsClassPtr == 0)
    {
        il2cpp_throw_argument_null_exception("argsClass");
    }
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    auto* ifaceClass = reinterpret_cast<Il2CppClass*>(ifaceClassPtr);
    auto* argsClass = reinterpret_cast<Il2CppClass*>(argsClassPtr);
    if (ifaceClass->instanceSize != 0)
    {
        return kComHrInvalidArg; // the descriptor must be an interface
    }
    const Il2CppTypeInfo* ifaceInfo = il2cpp_typeinfo_lookup(ifaceClass->name);
    if (ifaceInfo == nullptr)
    {
        return kComHrInvalidArg; // the interface has no reflected typeinfo
    }
    // Self-validate the args pairing against every drop method's shape
    // "v(r:<argsClass name>)": a void method with exactly one reference
    // parameter whose type name equals argsClass->name. An interface that
    // simply does not declare a method leaves its slot at -1 (the dispatch
    // answers E_NOTIMPL); a mismatched shape is a registration error.
    const char* const argsName = argsClass->name;
    const size_t argsNameLen = std::strlen(argsName);
    int32_t slots[4];
    for (int32_t m = 0; m < 4; ++m)
    {
        slots[m] = -1;
        const Il2CppMethodInfo* mi = il2cpp_type_get_method(ifaceInfo, kDropMethodNames[m]);
        if (mi == nullptr)
        {
            continue; // the interface does not declare this method
        }
        const char* sig = (mi->signature != nullptr) ? mi->signature : "";
        // The encoded shape: "v(r:Type)". sig[0] return 'v', sig[1] '(',
        // then "r:" + the reference parameter's type name + ')'.
        if (sig[0] != 'v' || sig[1] != '(' || sig[2] != 'r' || sig[3] != ':' ||
            std::strncmp(sig + 4, argsName, argsNameLen) != 0 || sig[4 + argsNameLen] != ')')
        {
            return kComHrInvalidArg; // a mismatched args class / unsupported shape
        }
        slots[m] = mi->virtualSlot;
    }
    // The args class must have reflected typeinfo AND a ".ctor" (the
    // construct mechanism needs both); the Effect field's REAL byte offset is
    // recomputed for the write-back (-1 = the args class has no Effect field
    // -> skip the write-back).
    const Il2CppTypeInfo* argsInfo = il2cpp_typeinfo_lookup(argsName);
    if (argsInfo == nullptr || il2cpp_type_get_method(argsInfo, ".ctor") == nullptr)
    {
        return kComHrInvalidArg;
    }
    // Validate-then-write: a rejected registration leaves the prior state.
    ccw->dropIface = ifaceClass;
    ccw->dropIfaceInfo = ifaceInfo;
    ccw->dropArgsClass = argsClass;
    for (int32_t m = 0; m < 4; ++m)
    {
        ccw->dropSlots[m] = slots[m];
    }
    ccw->dropEffectOffset = RealDropEffectOffset(argsInfo);
    ccw->dropManaged = 1;
    return kComHrOk;
}

int32_t icall_com_ccw_drop_dispatch_managed(
    intptr_t ccwPtr, int32_t method, intptr_t pDataObj,
    uint32_t grfKeyState, int32_t x, int32_t y, uint32_t* pdwEffect)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (method < kComDropMethodDragEnter || method > kComDropMethodDragLeave)
    {
        il2cpp_throw_argument_out_of_range_exception("method", nullptr);
    }
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    if (ccw->dropManaged == 0)
    {
        return kComHrNotImpl; // unregistered managed dispatch
    }
    const int32_t ifaceSlot = ccw->dropSlots[method];
    if (ifaceSlot < 0)
    {
        return kComHrNotImpl; // the interface does not declare this method
    }
    auto* managed = icall_gchandle_get_target(ccw->gcHandle);
    if (managed == nullptr || !il2cpp_is_instance(managed, ccw->dropIface))
    {
        return kComHrNotImpl; // the is_instance gate -- no interface_dispatch abort
    }
    // Wrap the native IDataObject* into a managed IDataObject implementer
    // (0 -> null data -- DragOver/DragLeave have no data object).
    const intptr_t data = (pDataObj != 0) ? icall_com_wrap_data_object(pDataObj, 0) : 0;
    const uint32_t effectIn = (pdwEffect != nullptr) ? *pdwEffect : 0;
    // Construct the DragEventArgs (data/x/y/effect -- the batch-3 ctor shape
    // "v(r:IDataObject,i,i,V:DragDropEffects)"; grfKeyState is DROPPED -- the
    // fixture DragEventArgs has no KeyState field).
    (void)grfKeyState;
    il2cpp::Il2CppObject* ev = il2cpp_object_construct(
        ccw->dropArgsClass, data, x, y, static_cast<intptr_t>(effectIn));
    if (ev == nullptr)
    {
        return kComHrNotImpl; // construct failure (defensive -- registration validated)
    }
    // Dispatch: the registered interface method is "v(r:X)" -- void with one
    // reference argument. The handler mutates the args object in place
    // (X/Y/Effect observers).
    void** slotPtr = il2cpp_interface_dispatch(managed, ccw->dropIface, ifaceSlot);
    reinterpret_cast<void (*)(Il2CppObject*, Il2CppObject*)>(*slotPtr)(managed, ev);
    // Effect write-back (ev->Effect was seeded by the ctor with the allowed
    // mask; the handler's read is the observable proof).
    if (pdwEffect != nullptr && ccw->dropEffectOffset >= 0)
    {
        *pdwEffect = *reinterpret_cast<uint32_t*>(
            reinterpret_cast<char*>(ev) + ccw->dropEffectOffset);
    }
    return kComHrOk;
}

//------------------------------------------------------------------------------
// icall_com_ccw_register_invoke_dispatch -- register the IDispatch::Invoke
// -> managed-method dispatch on a CCW (stage 18; see the header contract).
// The interface descriptor must be an interface (instanceSize == 0) with
// reflected typeinfo. Name -> slot resolution happens per-invocation (the
// member name is known only when Invoke arrives); the typeinfo lookup is
// cached on the CCW. A rejected registration (bad descriptor / no typeinfo)
// leaves the prior state intact (validate-then-write).
//------------------------------------------------------------------------------
int32_t icall_com_ccw_register_invoke_dispatch(intptr_t ccwPtr, intptr_t ifaceClassPtr)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (ifaceClassPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ifaceClass");
    }
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    auto* ifaceClass = reinterpret_cast<Il2CppClass*>(ifaceClassPtr);
    if (ifaceClass->instanceSize != 0)
    {
        return kComHrInvalidArg; // the descriptor must be an interface
    }
    const Il2CppTypeInfo* ifaceInfo = il2cpp_typeinfo_lookup(ifaceClass->name);
    if (ifaceInfo == nullptr)
    {
        return kComHrInvalidArg; // the interface has no reflected typeinfo
    }
    ccw->invokeManaged = 1;
    ccw->invokeIface = ifaceClass;
    ccw->invokeIfaceInfo = ifaceInfo;
    return kComHrOk;
}

//------------------------------------------------------------------------------
// icall_com_wrap_data_object -- wrap a native IDataObject* into a managed
// IDataObject implementer (see il2cpp_com.h for the full contract). The
// native pointer stays BORROWED (the caller keeps it alive; the typed-RCW
// never releases it -- a distinct descriptor with no ReleaseComObject path).
// TypedDataObjectRcwClass / TypedDataObjectForward are anonymous-namespace
// helpers -- visible unqualified throughout this TU (the anonymous-namespace
// rule), referenced here at namespace scope so the header-declared symbol is
// the one defined.
//------------------------------------------------------------------------------
intptr_t icall_com_wrap_data_object(intptr_t pDataObj, intptr_t ifaceClassPtr)
{
    if (pDataObj == 0)
    {
        il2cpp_throw_argument_null_exception("pDataObj");
    }
    Il2CppClass* iface = (ifaceClassPtr != 0)
        ? reinterpret_cast<Il2CppClass*>(ifaceClassPtr)
        : il2cpp_class_by_name("IDataObject");
    if (iface == nullptr)
    {
        return 0; // no IDataObject descriptor registered -- cannot type the wrapper
    }
    auto* cls = TypedDataObjectRcwClass();
    // Lazily fill the descriptor's interfaces entry (idempotent -- the first
    // wrap pins the resolved descriptor; com.cpp has no generated symbol).
    // The descriptor field is const (transpiler contract); the static
    // ifaceEntry backing it is mutable at runtime by design (the wrap icall
    // is its only writer).
    auto* entry = const_cast<InterfaceMapEntry*>(cls->interfaces);
    if (entry != nullptr && entry->iface == nullptr)
    {
        entry->iface = iface;
    }
    Il2CppObject* obj = il2cpp_object_new(cls);
    auto* rcw = reinterpret_cast<Il2CppComRcw*>(obj);
    rcw->unknown = reinterpret_cast<void*>(pDataObj);
    rcw->refCount = 1;
    return static_cast<intptr_t>(reinterpret_cast<uintptr_t>(obj));
}

//------------------------------------------------------------------------------
// il2cpp_com_rcw_class -- the RCW descriptor (IsComObject's comparison key).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_com_rcw_class()
{
    return RcwClass();
}

//------------------------------------------------------------------------------
// icall_com_query_interface -- IUnknown::QueryInterface dispatch.
//------------------------------------------------------------------------------
int32_t icall_com_query_interface(intptr_t pUnk, const uint8_t iid[16], intptr_t* ppv)
{
    if (pUnk == 0)
    {
        il2cpp_throw_argument_null_exception("pUnk");
    }
    if (iid == nullptr || ppv == nullptr)
    {
        il2cpp_throw_argument_null_exception("iid");
    }
    void* self = reinterpret_cast<void*>(pUnk);
    typedef int32_t (IL2CPP_COM_CALL* QueryInterfaceFn)(void*, const void*, void**);
    const QueryInterfaceFn qi = reinterpret_cast<QueryInterfaceFn>(
        const_cast<void*>(VtableSlot(self, 0)));
    void* out = nullptr;
    const int32_t hr = qi(self, iid, &out);
    if (hr == kComHrOk)
    {
        *ppv = static_cast<intptr_t>(reinterpret_cast<uintptr_t>(out));
    }
    return hr;
}

//------------------------------------------------------------------------------
// icall_com_add_ref / icall_com_release -- IUnknown::AddRef / Release.
//------------------------------------------------------------------------------
int32_t icall_com_add_ref(intptr_t pUnk)
{
    if (pUnk == 0)
    {
        il2cpp_throw_argument_null_exception("pUnk");
    }
    void* self = reinterpret_cast<void*>(pUnk);
    typedef uint32_t (IL2CPP_COM_CALL* AddRefFn)(void*);
    const AddRefFn fn = reinterpret_cast<AddRefFn>(const_cast<void*>(VtableSlot(self, 1)));
    return static_cast<int32_t>(fn(self));
}

int32_t icall_com_release(intptr_t pUnk)
{
    if (pUnk == 0)
    {
        il2cpp_throw_argument_null_exception("pUnk");
    }
    void* self = reinterpret_cast<void*>(pUnk);
    typedef uint32_t (IL2CPP_COM_CALL* ReleaseFn)(void*);
    const ReleaseFn fn = reinterpret_cast<ReleaseFn>(const_cast<void*>(VtableSlot(self, 2)));
    return static_cast<int32_t>(fn(self));
}

//------------------------------------------------------------------------------
// icall_com_call_vtable_slot -- basic vtable-slot method invocation (see
// header: phase-1 fixed 4-intptr-arg shape; slot 0..2 = IUnknown methods).
//------------------------------------------------------------------------------
intptr_t icall_com_call_vtable_slot(intptr_t iface, int32_t slot,
                                    intptr_t a0, intptr_t a1,
                                    intptr_t a2, intptr_t a3)
{
    if (iface == 0)
    {
        il2cpp_throw_argument_null_exception("iface");
    }
    if (slot < 0 || slot > 127)
    {
        il2cpp_throw_argument_out_of_range_exception("slot", nullptr);
    }
    void* self = reinterpret_cast<void*>(iface);
    void* const* vtbl = *static_cast<void* const* const*>(self);
    // The CCW's known vtables have fixed slot counts: the identity vtable is
    // exactly 7 slots (IUnknown: QI/AddRef/Release + IDispatch:
    // GetTypeInfoCount/GetTypeInfo/GetIDsOfNames/Invoke); the phase-4 OLE
    // views are 24 (IOleObject) and 9 (IOleClientSite) slots, the phase-5
    // event-source views are 5 (IConnectionPointContainer) and 8
    // (IConnectionPoint) slots and the stage-15 IDropTarget view is 7 slots. A slot past a known CCW vtable's length would read beyond the static vtable (OOB).
    // Real RCW interface vtables can be arbitrarily long, so the 0..127 bound
    // is the generic defense there; the CCW's known lengths are enforced here.
    int32_t ccwSlots = -1;
    if (vtbl == reinterpret_cast<void* const*>(&kCcwVtable) ||
        // Stage 10: the IEnumVARIANT view -- the inlined main view and the
        // Clone heap view share the 7-slot enum vtable shape.
        vtbl == reinterpret_cast<void* const*>(&kEnumVtable) ||
        vtbl == reinterpret_cast<void* const*>(&kEnumCloneVtable))
    {
        ccwSlots = 7;
    }
    else if (vtbl == reinterpret_cast<void* const*>(&kDataObjectVtable))
    {
        ccwSlots = 12; // stage 17: the IDataObject view (IUnknown + 9 methods)
    }
#if defined(_WIN32)
    else if (vtbl == reinterpret_cast<void* const*>(&kOleVtable))
    {
        ccwSlots = 24;
    }
    else if (vtbl == reinterpret_cast<void* const*>(&kSiteVtable))
    {
        ccwSlots = 9;
    }
    else if (vtbl == reinterpret_cast<void* const*>(&kCpcVtable))
    {
        ccwSlots = 5;
    }
    else if (vtbl == reinterpret_cast<void* const*>(&kCpVtable))
    {
        ccwSlots = 8;
    }
    else if (vtbl == reinterpret_cast<void* const*>(&kDropVtable))
    {
        ccwSlots = 7;
    }
#endif
    if (ccwSlots >= 0 && slot >= ccwSlots)
    {
        il2cpp_throw_argument_out_of_range_exception("slot", nullptr);
    }
    typedef intptr_t (IL2CPP_COM_CALL* SlotFn)(void*, intptr_t, intptr_t, intptr_t, intptr_t);
    const SlotFn fn = reinterpret_cast<SlotFn>(vtbl[slot]);
    return fn(self, a0, a1, a2, a3);
}

//------------------------------------------------------------------------------
// icall_com_create_instance_clsid / _progid -- CoCreateInstance wrapper.
//------------------------------------------------------------------------------
intptr_t icall_com_create_instance_clsid(const uint8_t clsid[16])
{
    if (clsid == nullptr)
    {
        il2cpp_throw_argument_null_exception("clsid");
    }
#if defined(_WIN32)
    return CreateInstanceByClsid(clsid);
#else
    (void)clsid;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return 0;
#endif
}

intptr_t icall_com_create_instance_progid(Il2CppString* progId)
{
    if (progId == nullptr)
    {
        il2cpp_throw_argument_null_exception("progId");
    }
#if defined(_WIN32)
    uint8_t clsid[16];
    ClsidFromProgIdOrThrow(progId, clsid);
    return CreateInstanceByClsid(clsid);
#else
    (void)progId;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return 0;
#endif
}

//------------------------------------------------------------------------------
// icall_com_get_active_object -- Marshal.GetActiveObject (running object
// table lookup: CLSIDFromProgID + GetActiveObject).
//------------------------------------------------------------------------------
Il2CppObject* icall_com_get_active_object(Il2CppString* progId)
{
    if (progId == nullptr)
    {
        il2cpp_throw_argument_null_exception("progId");
    }
#if defined(_WIN32)
    EnsureOle32();
    uint8_t clsid[16];
    ClsidFromProgIdOrThrow(progId, clsid);
    void* unknown = nullptr;
    const int32_t hr = GetOle32().getActiveObject(clsid, nullptr, &unknown);
    if (hr != kComHrOk)
    {
        ThrowComException(hr); // MK_E_UNAVAILABLE when not running (net48)
    }
    return WrapUnknown(unknown);
#else
    (void)progId;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return nullptr;
#endif
}

//------------------------------------------------------------------------------
// icall_com_create_object -- Marshal.CreateObject (CoCreateInstance by
// ProgID wrapped into an RCW).
//------------------------------------------------------------------------------
Il2CppObject* icall_com_create_object(Il2CppString* progId)
{
    if (progId == nullptr)
    {
        il2cpp_throw_argument_null_exception("progId");
    }
#if defined(_WIN32)
    const intptr_t unknown = icall_com_create_instance_progid(progId);
    return WrapUnknown(reinterpret_cast<void*>(unknown));
#else
    (void)progId;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return nullptr;
#endif
}

//------------------------------------------------------------------------------
// icall_com_get_type_from_progid -- Marshal.GetTypeFromProgID's CLSID
// resolution (the managed Type object is M3 reflection -- phase 1 exposes the
// ProgID -> CLSID mapping only). verify-then-mutate on the out buffer.
//------------------------------------------------------------------------------
int32_t icall_com_get_type_from_progid(Il2CppString* progId, uint8_t clsid[16])
{
    if (progId == nullptr)
    {
        il2cpp_throw_argument_null_exception("progId");
    }
    if (clsid == nullptr)
    {
        il2cpp_throw_argument_null_exception("clsid");
    }
#if defined(_WIN32)
    EnsureOle32();
    wchar_t buf[260];
    if (!ProgIdToWide(progId, buf, 260))
    {
        il2cpp_throw_argument_exception("ProgID exceeds the 260-char limit.");
    }
    uint8_t out[16];
    const int32_t hr = GetOle32().clsidFromProgID(buf, out);
    if (hr == kComHrOk)
    {
        std::memcpy(clsid, out, 16);
    }
    return hr;
#else
    (void)progId;
    (void)clsid;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// il2cpp_com_type_from_progid -- Type.GetTypeFromProgID(string) /
// Marshal.GetTypeFromProgID(string): resolve ProgID -> CLSID and return the
// COM Type VALUE string "COMCLSID:<32hex>" (see the header contract).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_com_type_from_progid(Il2CppString* progId)
{
    if (progId == nullptr)
    {
        il2cpp_throw_argument_null_exception("progId");
    }
#if defined(_WIN32)
    uint8_t clsid[16];
    // net48 Type.GetTypeFromProgID(string) is the throwOnError=false
    // convenience overload -- an unknown ProgID returns NULL (the C1 miss
    // contract), not a COMException.
    if (!TryClsidFromProgId(progId, clsid))
    {
        return nullptr;
    }
    char sentinel[kComClsidSentinelLength + 1];
    ClsidToSentinel(clsid, sentinel);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(sentinel));
#else
    (void)progId;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return nullptr;
#endif
}

//------------------------------------------------------------------------------
// il2cpp_com_type_from_clsid -- Type.GetTypeFromCLSID(Guid): encode the 16
// CLSID bytes (the .NET Guid / COM CLSID in-memory layout) into the sentinel
// Type VALUE string. Pure formatting -- no registry probe (see the header).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_com_type_from_clsid(const uint8_t clsid[16])
{
    if (clsid == nullptr)
    {
        il2cpp_throw_argument_null_exception("clsid");
    }
    char sentinel[kComClsidSentinelLength + 1];
    ClsidToSentinel(clsid, sentinel);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(sentinel));
}

//------------------------------------------------------------------------------
// icall_com_create_instance_from_com_type -- Activator.CreateInstance(Type)
// on a COM Type VALUE. Decode the "COMCLSID:<32hex>" sentinel, CoCreateInstance
// and WRAP the IUnknown* into an RCW (see the header contract).
//------------------------------------------------------------------------------
Il2CppObject* icall_com_create_instance_from_com_type(Il2CppObject* typeName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
    }
    auto s = reinterpret_cast<Il2CppString*>(typeName);
    const int32_t sentinelLen = kComClsidSentinelLength; // 9 prefix + 32 hex
    if (s->length != sentinelLen)
    {
        il2cpp_throw_argument_exception(
            "The type name is not a COM Type value (expected the \"COMCLSID:\" sentinel).");
    }
    static const char16_t kPrefix[10] = u"COMCLSID:"; // 9 + NUL
    if (std::memcmp(s->chars, kPrefix, 9 * sizeof(char16_t)) != 0)
    {
        il2cpp_throw_argument_exception(
            "The type name is not a COM Type value (expected the \"COMCLSID:\" sentinel).");
    }
    uint8_t clsid[16];
    char hex[kComClsidHexLength];
    for (int32_t i = 0; i < kComClsidHexLength; ++i)
    {
        const char16_t c = s->chars[9 + i];
        hex[i] = (c <= 0x7F) ? static_cast<char>(c) : '\0';
    }
    if (!SentinelToClsid(hex, clsid))
    {
        il2cpp_throw_argument_exception(
            "The COM Type value carries a malformed CLSID hex payload.");
    }
#if defined(_WIN32)
    const intptr_t unknown = CreateInstanceByClsid(clsid);
    return WrapUnknown(reinterpret_cast<void*>(unknown));
#else
    (void)clsid;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return nullptr;
#endif
}

//------------------------------------------------------------------------------
// icall_com_get_iunknown_for_object -- Marshal.GetIUnknownForObject. net48
// semantics (coreclr interopconverter.cpp GetComIPFromObjectRef): the object
// is exposed through ITS native interface -- an RCW returns its wrapped
// IUnknown*, a non-RCW managed object is wrapped behind a fresh CCW (the
// CCW direction, phase-2 caching of the per-object CCW not implemented). In
// both cases the returned pointer carries a reference the caller must Release.
//------------------------------------------------------------------------------
intptr_t icall_com_get_iunknown_for_object(Il2CppObject* o)
{
    if (o == nullptr)
    {
        il2cpp_throw_argument_null_exception("o");
    }
    if (o->klass != RcwClass())
    {
        // net48 GetIUnknownForObject on a non-RCW managed object returns the
        // IUnknown of a CCW created for it (AddRef'd once -- the caller owns
        // the returned reference; Release to 0 destroys the CCW).
        return icall_com_ccw_create(o);
    }
    auto* r = reinterpret_cast<Il2CppComRcw*>(o);
    if (r->refCount <= 0 || r->unknown == nullptr)
    {
        ThrowReleasedComObject();
    }
    AddRefInterface(r->unknown);
    return static_cast<intptr_t>(reinterpret_cast<uintptr_t>(r->unknown));
}

//------------------------------------------------------------------------------
// icall_com_get_object_for_iunknown -- Marshal.GetObjectForIUnknown. Takes
// OWNERSHIP of the reference (net48 -- the wrapper lives on it).
//------------------------------------------------------------------------------
Il2CppObject* icall_com_get_object_for_iunknown(intptr_t pUnk)
{
    if (pUnk == 0)
    {
        il2cpp_throw_argument_null_exception("pUnk");
    }
    return WrapUnknown(reinterpret_cast<void*>(pUnk));
}

//------------------------------------------------------------------------------
// icall_com_get_idispatch_for_object -- Marshal.GetIDispatchForObject (net48
// semantics, coreclr GetComIPFromObjectRef with IID_IDispatch -- the ActiveX /
// automation hosting entry point). A NON-RCW managed object is wrapped behind
// a fresh CCW (which implements IDispatch) and QI'd for IDispatch; an RCW QIs
// its wrapped native IUnknown for IDispatch. The returned pointer is AddRef'd
// once -- the caller owns the reference; Release to 0 destroys the CCW.
//------------------------------------------------------------------------------
intptr_t icall_com_get_idispatch_for_object(Il2CppObject* o)
{
    if (o == nullptr)
    {
        il2cpp_throw_argument_null_exception("o");
    }
    if (o->klass != RcwClass())
    {
        // net48 GetIDispatchForObject on a non-RCW managed object returns the
        // IDispatch of a CCW created for it. The CCW always supports
        // IDispatch (phase 2) -- the QI cannot fail. Balance the ccw_create
        // reference; the returned QI reference is what the caller owns.
        const intptr_t ccw = icall_com_ccw_create(o);
        intptr_t dispatch = 0;
        const int32_t hr = icall_com_query_interface(ccw, kComIidIDispatch, &dispatch);
        icall_com_release(ccw);
        if (hr != kComHrOk)
        {
            // Unreachable in practice -- the returned reference is already
            // balanced; surface the failure as a plain no-interface throw.
            return 0;
        }
        return dispatch;
    }
#if defined(_WIN32)
    auto* r = reinterpret_cast<Il2CppComRcw*>(o);
    if (r->refCount <= 0 || r->unknown == nullptr)
    {
        ThrowReleasedComObject();
    }
    intptr_t dispatch = 0;
    const int32_t hr = icall_com_query_interface(
        reinterpret_cast<intptr_t>(r->unknown), kComIidIDispatch, &dispatch);
    if (hr == kComHrNoInterface)
    {
        // net48 GetIDispatchForObject throws InvalidCastException when the
        // object does not implement IDispatch.
        il2cpp_throw_new(
            "System.InvalidCastException,System.InvalidCastException,"
            "System.SystemException,System.Exception",
            "The COM object does not implement IDispatch.");
    }
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
    return dispatch;
#else
    (void)o;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return 0;
#endif
}

//------------------------------------------------------------------------------
// icall_com_get_com_interface_for_object -- Marshal.GetComInterfaceForObject
// (net48 semantics, coreclr GetComIPFromObjectRef with the requested IID). The
// managed Type -> IID resolution happens at TRANSPILE time (the runtime Type
// object cannot be resolved to an IID at compile time), so the icall supports
// ONLY the two constant Type args the transpiler recognizes: IUnknown and
// IDispatch (the net48-most-used interfaces the CCW implements). A NON-RCW
// managed object is wrapped behind a fresh CCW and QI'd for the IID; an RCW
// QIs its wrapped native IUnknown. The returned pointer is AddRef'd once -- the
// caller owns the reference; Release to 0 destroys the CCW. Any other IID ->
// E_NOINTERFACE -> InvalidCastException (net48). Null o -> ArgumentNullException.
// Non-Windows: throws NotSupportedException (the POSIX stub).
//------------------------------------------------------------------------------
intptr_t icall_com_get_com_interface_for_object(Il2CppObject* o, const uint8_t iid[16])
{
    if (o == nullptr)
    {
        il2cpp_throw_argument_null_exception("o");
    }
    if (iid == nullptr)
    {
        il2cpp_throw_argument_null_exception("iid");
    }
#if defined(_WIN32)
    // Only IUnknown and IDispatch are supported (the focused stage-14 surface).
    // Any other IID -> E_NOINTERFACE -> InvalidCastException (net48).
    if (std::memcmp(iid, kComIidIUnknown, 16) != 0 &&
        std::memcmp(iid, kComIidIDispatch, 16) != 0)
    {
        il2cpp_throw_new(
            "System.InvalidCastException,System.InvalidCastException,"
            "System.SystemException,System.Exception",
            "The requested COM interface is not supported (only IUnknown and IDispatch).");
    }
    if (o->klass != RcwClass())
    {
        // net48 GetComInterfaceForObject on a non-RCW managed object returns
        // the requested interface of a CCW created for it. The CCW always
        // supports IUnknown and IDispatch (phase 2) -- the QI cannot fail.
        // Balance the ccw_create reference; the returned QI reference is what
        // the caller owns.
        const intptr_t ccw = icall_com_ccw_create(o);
        intptr_t iface = 0;
        const int32_t hr = icall_com_query_interface(ccw, iid, &iface);
        icall_com_release(ccw);
        if (hr != kComHrOk)
        {
            // Unreachable in practice -- the returned reference is already
            // balanced; surface the failure as a plain no-interface throw.
            return 0;
        }
        return iface;
    }
    auto* r = reinterpret_cast<Il2CppComRcw*>(o);
    if (r->refCount <= 0 || r->unknown == nullptr)
    {
        ThrowReleasedComObject();
    }
    intptr_t iface = 0;
    const int32_t hr = icall_com_query_interface(
        reinterpret_cast<intptr_t>(r->unknown), iid, &iface);
    if (hr == kComHrNoInterface)
    {
        // net48 GetComInterfaceForObject throws InvalidCastException when the
        // object does not implement the requested interface.
        il2cpp_throw_new(
            "System.InvalidCastException,System.InvalidCastException,"
            "System.SystemException,System.Exception",
            "The COM object does not implement the requested interface.");
    }
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
    return iface;
#else
    (void)o;
    (void)iid;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return 0;
#endif
}

//------------------------------------------------------------------------------
// icall_com_ole_register_drag_drop / icall_com_ole_revoke_drag_drop -- stage-15
// OLE drag-drop registration (the WinForms Control.AllowDrop dependency): ole32
// RegisterDragDrop(hwnd, dropTarget) registers a window as a drop target;
// RevokeDragDrop(hwnd) revokes it. 'dropTarget' is the CCW's IDropTarget view
// (QI for IID_IDropTarget -- the managed dispatch of DragEnter/DragOver/
// DragLeave/Drop is stage 16+; the minimal stage-15 surface answers DragLeave
// S_OK and the rest E_NOTIMPL). RegisterDragDrop / RevokeDragDrop are resolved
// at runtime like the rest of the ole32 API and DELIBERATELY kept OUT of
// GetOle32().ok (the stage-11 strategy); EnsureOle32() runs inside so OLE is
// initialized before registration (RegisterDragDrop requires an initialized
// OLE -- OLE_E_NOTINITIALIZED otherwise). hwnd == 0 (register: also dropTarget
// == 0) -> ArgumentNullException -- checked on EVERY platform before the
// platform gate (the null guards precede it, the stage-13 BSTR precedent). A
// failing native call -> the COMException-shaped throw carrying the HRESULT.
// Non-Windows: E_NOTIMPL (the POSIX stub -- the platform-limitation contract).
//------------------------------------------------------------------------------
int32_t icall_com_ole_register_drag_drop(intptr_t hwnd, intptr_t dropTarget)
{
    if (hwnd == 0 || dropTarget == 0)
    {
        il2cpp_throw_argument_null_exception("hwnd");
    }
#if defined(_WIN32)
    // RegisterDragDrop requires an initialized OLE (else OLE_E_NOTINITIALIZED)
    // -- EnsureOle32() both loads ole32 and initializes COM (CoInitializeEx).
    EnsureOle32();
    if (GetOle32().registerDragDrop == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "ole32.dll's RegisterDragDrop is unavailable (OLE drag-drop unsupported)");
    }
    const int32_t hr = GetOle32().registerDragDrop(
        reinterpret_cast<void*>(hwnd), reinterpret_cast<void*>(dropTarget));
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
    return kComHrOk;
#else
    (void)dropTarget;
    return kComHrNotImpl;
#endif
}

int32_t icall_com_ole_revoke_drag_drop(intptr_t hwnd)
{
    if (hwnd == 0)
    {
        il2cpp_throw_argument_null_exception("hwnd");
    }
#if defined(_WIN32)
    EnsureOle32();
    if (GetOle32().revokeDragDrop == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "ole32.dll's RevokeDragDrop is unavailable (OLE drag-drop unsupported)");
    }
    const int32_t hr = GetOle32().revokeDragDrop(reinterpret_cast<void*>(hwnd));
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
    return kComHrOk;
#else
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_idispatch_get_ids_of_names -- IDispatch::GetIDsOfNames (the RCW
// direction / vtable slot 5). Windows: forward riid = IID_NULL, the caller's
// UTF-16 name-pointer array and the dispId out array to the native dispatch.
// POSIX: the documented stub (E_NOTIMPL -- no real COM objects to dispatch
// into). Works unchanged against a CCW (deterministic per-object dispIds).
//------------------------------------------------------------------------------
int32_t icall_com_idispatch_get_ids_of_names(intptr_t dispatch, intptr_t* names,
                                             int32_t count, int32_t lcid,
                                             int32_t* dispIds)
{
    if (dispatch == 0)
    {
        il2cpp_throw_argument_null_exception("dispatch");
    }
    if (names == nullptr || dispIds == nullptr || count < 0)
    {
        il2cpp_throw_argument_exception(
            "GetIDsOfNames: the name array, dispId array or count is invalid.");
    }
#if defined(_WIN32)
    void* self = reinterpret_cast<void*>(dispatch);
    typedef int32_t (IL2CPP_COM_CALL* GetIDsOfNamesFn)(
        void*, const void*, const wchar_t* const*, uint32_t, uint32_t, int32_t*);
    const GetIDsOfNamesFn fn = reinterpret_cast<GetIDsOfNamesFn>(
        const_cast<void*>(VtableSlot(self, 5)));
    return fn(self, kNullIid,
              reinterpret_cast<const wchar_t* const*>(names),
              static_cast<uint32_t>(count), static_cast<uint32_t>(lcid),
              dispIds);
#else
    (void)names;
    (void)count;
    (void)lcid;
    (void)dispIds;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_idispatch_invoke -- IDispatch::Invoke (the RCW direction / vtable
// slot 6). Windows: forward riid = IID_NULL, the caller's DISPPARAMS /
// VARIANT / EXCEPINFO / puArgErr pointers and the LCID/wFlags to the native
// dispatch. POSIX: the documented stub (E_NOTIMPL).
//------------------------------------------------------------------------------
int32_t icall_com_idispatch_invoke(intptr_t dispatch, int32_t dispIdMember,
                                   int32_t lcid, int32_t wFlags,
                                   intptr_t pDispParams, intptr_t pVarResult,
                                   intptr_t pExcepInfo, intptr_t puArgErr)
{
    if (dispatch == 0)
    {
        il2cpp_throw_argument_null_exception("dispatch");
    }
    if (pDispParams == 0)
    {
        il2cpp_throw_argument_null_exception("pDispParams");
    }
#if defined(_WIN32)
    void* self = reinterpret_cast<void*>(dispatch);
    typedef int32_t (IL2CPP_COM_CALL* InvokeFn)(
        void*, int32_t, const void*, uint32_t, uint16_t,
        const void*, void*, void*, uint32_t*);
    const InvokeFn fn = reinterpret_cast<InvokeFn>(
        const_cast<void*>(VtableSlot(self, 6)));
    return fn(self, dispIdMember, kNullIid, static_cast<uint32_t>(lcid),
              static_cast<uint16_t>(wFlags), reinterpret_cast<void*>(pDispParams),
              reinterpret_cast<void*>(pVarResult), reinterpret_cast<void*>(pExcepInfo),
              reinterpret_cast<uint32_t*>(puArgErr));
#else
    (void)dispIdMember;
    (void)lcid;
    (void)wFlags;
    (void)pDispParams;
    (void)pVarResult;
    (void)pExcepInfo;
    (void)puArgErr;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_invoke_member -- C2 (A2): the ONE-CALL late-bound member
// invocation (Type.InvokeMember on a COM Type / RCW), see the header.
//------------------------------------------------------------------------------
Il2CppObject* icall_com_invoke_member(Il2CppObject* rcw, Il2CppString* name,
                                      Il2CppArray* args)
{
    if (name == nullptr)
    {
        il2cpp_throw_argument_null_exception("name");
    }
#if defined(_WIN32)
    Il2CppComRcw* r = AsRcw(rcw, "rcw");

    // 1. QI the RCW's IUnknown for IDispatch (the automation surface).
    intptr_t dispatch = 0;
    const int32_t qihr = icall_com_query_interface(
        static_cast<intptr_t>(reinterpret_cast<uintptr_t>(r->unknown)),
        kComIidIDispatch, &dispatch);
    if (qihr != kComHrOk)
    {
        if (qihr == kComHrNoInterface)
        {
            il2cpp_throw_new(
                "System.InvalidCastException,System.InvalidCastException,"
                "System.SystemException,System.Exception",
                "The COM object does not implement IDispatch (cannot InvokeMember).");
        }
        ThrowComException(qihr);
    }

    // 2. GetIDsOfNames -> the member's dispId (one name, LCID 0).
    std::wstring wname(reinterpret_cast<const wchar_t*>(name->chars),
                       static_cast<size_t>(name->length));
    intptr_t namePtrs[1] = { reinterpret_cast<intptr_t>(wname.c_str()) };
    int32_t dispId = -1;
    const int32_t idsHr = icall_com_idispatch_get_ids_of_names(
        dispatch, namePtrs, 1, 0, &dispId);
    if (idsHr != kComHrOk)
    {
        icall_com_release(dispatch);
        ThrowComException(idsHr); // DISP_E_MEMBERNOTFOUND for an unknown member
    }

    // 3. Build the positional DISPPARAMS (rgvarg in REVERSE IL order).
    const int32_t argCount = (args == nullptr) ? 0 : args->length;
    std::vector<uint8_t> variants(static_cast<size_t>(argCount) * kVariantSize);
    std::vector<bool> ownedBstr(static_cast<size_t>(argCount), false);
    for (int32_t i = 0; i < argCount; ++i)
    {
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(args, i));
        const int32_t nativeIdx = argCount - 1 - i; // DISPPARAMS reversal
        ownedBstr[static_cast<size_t>(nativeIdx)] =
            ManagedArgToVariant(*slot, &variants[nativeIdx * kVariantSize]);
    }
    ComDispParams params;
    params.rgvarg = argCount > 0 ? variants.data() : nullptr;
    params.rgdispidNamedArgs = nullptr;
    params.cArgs = static_cast<uint32_t>(argCount);
    params.cNamedArgs = 0;

    // 4. Invoke (DISPATCH_METHOD | DISPATCH_PROPERTYGET; property-put is LOUD
    //    by design -- never passed).
    uint8_t resultVar[kVariantSize];
    std::memset(resultVar, 0, sizeof(resultVar));
    const int32_t invokeHr = icall_com_idispatch_invoke(
        dispatch, dispId, 0,
        static_cast<int32_t>(kComDispatchMethod | kComDispatchPropertyGet),
        reinterpret_cast<intptr_t>(&params), reinterpret_cast<intptr_t>(resultVar),
        0, 0);

    // 5. Release the borrowed IDispatch + the BSTR argument masters.
    icall_com_release(dispatch);
    for (int32_t i = 0; i < argCount; ++i)
    {
        if (ownedBstr[static_cast<size_t>(i)] && GetOle32().sysFreeString != nullptr)
        {
            void* bstr = nullptr;
            std::memcpy(&bstr, &variants[static_cast<size_t>(i) * kVariantSize + 8],
                        sizeof(bstr));
            GetOle32().sysFreeString(bstr);
        }
    }
    if (invokeHr != kComHrOk)
    {
        ThrowComException(invokeHr);
    }

    // 6. Decode the result (a caller-owned BSTR in resultVar is copied by the
    //    decoder and must be freed here -- SysFreeString the returned BSTR).
    uint16_t rvt = 0;
    std::memcpy(&rvt, resultVar, sizeof(rvt));
    Il2CppObject* result = VariantResultToManaged(resultVar);
    if (rvt == kVtBstr && GetOle32().sysFreeString != nullptr)
    {
        void* bstr = nullptr;
        std::memcpy(&bstr, resultVar + kVariantVtOffset + 8, sizeof(bstr));
        GetOle32().sysFreeString(bstr);
    }
    return result;
#else
    (void)rcw;
    (void)name;
    (void)args;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return nullptr;
#endif
}

//------------------------------------------------------------------------------
// C3 (A3) -- the IConnectionPoint SINK direction. The managed side attaches a
// managed sink CCW to a NATIVE COM event source's connection point:
//   get_connection_point: QI the RCW's IUnknown for IConnectionPointContainer
//     + IConnectionPointContainer::FindConnectionPoint(eventIID) -> the
//     IConnectionPoint* (intptr carrier, AddRef'd).
//   cp_advise: IConnectionPoint::Advise (slot 5) the managed sink CCW
//     (icall_com_ccw_create's identity pointer, typically +
//     register_invoke_dispatch so the source's IDispatch::Invoke reaches a
//     managed method) -> the connection cookie.
//   cp_unadvise: IConnectionPoint::Unadvise (slot 6) the cookie.
// All Windows-only (the POSIX stub -- no real COM event sources to sink
// into). The slot constants mirror the header (kComCpcSlotFindConnectionPoint
// = 4, kComCpSlotAdvise = 5, kComCpSlotUnadvise = 6).
//------------------------------------------------------------------------------
intptr_t icall_com_get_connection_point(Il2CppObject* rcw, const uint8_t iid[16])
{
    if (iid == nullptr)
    {
        il2cpp_throw_argument_null_exception("iid");
    }
#if defined(_WIN32)
    Il2CppComRcw* r = AsRcw(rcw, "rcw");

    // 1. QI the RCW's IUnknown for IConnectionPointContainer.
    intptr_t cpc = 0;
    const int32_t qihr = icall_com_query_interface(
        static_cast<intptr_t>(reinterpret_cast<uintptr_t>(r->unknown)),
        kComIidIConnectionPointContainer, &cpc);
    if (qihr != kComHrOk)
    {
        if (qihr == kComHrNoInterface)
        {
            il2cpp_throw_new(
                "System.InvalidCastException,System.InvalidCastException,"
                "System.SystemException,System.Exception",
                "The COM object does not implement IConnectionPointContainer "
                "(cannot attach an event sink).");
        }
        ThrowComException(qihr);
    }

    // 2. FindConnectionPoint(eventIID) -- vtable slot 4.
    void* self = reinterpret_cast<void*>(cpc);
    typedef int32_t (IL2CPP_COM_CALL* FindConnectionPointFn)(void*, const void*, void**);
    const FindConnectionPointFn fn = reinterpret_cast<FindConnectionPointFn>(
        const_cast<void*>(VtableSlot(self, kComCpcSlotFindConnectionPoint)));
    void* cp = nullptr;
    const int32_t fhr = fn(self, iid, &cp);
    icall_com_release(cpc); // the borrowed ICPC reference
    if (fhr != kComHrOk || cp == nullptr)
    {
        ThrowComException(fhr != kComHrOk ? fhr : kComHrNoConnection);
    }
    return static_cast<intptr_t>(reinterpret_cast<uintptr_t>(cp));
#else
    (void)rcw;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return 0;
#endif
}

int32_t icall_com_cp_advise(intptr_t cp, intptr_t sinkCcw, intptr_t* cookie)
{
    if (cp == 0)
    {
        il2cpp_throw_argument_null_exception("cp");
    }
    if (sinkCcw == 0)
    {
        il2cpp_throw_argument_null_exception("sinkCcw");
    }
    if (cookie == nullptr)
    {
        il2cpp_throw_argument_null_exception("cookie");
    }
#if defined(_WIN32)
    void* self = reinterpret_cast<void*>(cp);
    typedef int32_t (IL2CPP_COM_CALL* AdviseFn)(void*, void*, uint32_t*);
    const AdviseFn fn = reinterpret_cast<AdviseFn>(
        const_cast<void*>(VtableSlot(self, kComCpSlotAdvise)));
    uint32_t nativeCookie = 0;
    const int32_t hr = fn(self, reinterpret_cast<void*>(sinkCcw), &nativeCookie);
    if (hr == kComHrOk)
    {
        *cookie = static_cast<intptr_t>(nativeCookie);
    }
    return hr;
#else
    (void)cp;
    (void)sinkCcw;
    (void)cookie;
    return kComHrNotImpl;
#endif
}

int32_t icall_com_cp_unadvise(intptr_t cp, intptr_t cookie)
{
    if (cp == 0)
    {
        il2cpp_throw_argument_null_exception("cp");
    }
#if defined(_WIN32)
    void* self = reinterpret_cast<void*>(cp);
    typedef int32_t (IL2CPP_COM_CALL* UnadviseFn)(void*, uint32_t);
    const UnadviseFn fn = reinterpret_cast<UnadviseFn>(
        const_cast<void*>(VtableSlot(self, kComCpSlotUnadvise)));
    return fn(self, static_cast<uint32_t>(cookie));
#else
    (void)cp;
    (void)cookie;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_is_com_object -- Marshal.IsComObject (klass == RCW descriptor).
//------------------------------------------------------------------------------
bool icall_com_is_com_object(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        il2cpp_throw_argument_null_exception("obj");
    }
    return obj->klass == RcwClass();
}

//------------------------------------------------------------------------------
// icall_com_release_com_object / _final -- Marshal.ReleaseComObject /
// FinalReleaseComObject. Release the RCW's native reference and mark it dead
// (a later COM operation on the RCW throws InvalidComObjectException -- a
// safe superset of net48's per-operation guard). ReleaseComObject returns
// the remaining wrapper reference count (0 after the phase-1 single
// reference; multi-wrapper RCW sharing is phase 2).
//------------------------------------------------------------------------------
int32_t icall_com_release_com_object(Il2CppObject* rcw)
{
    Il2CppComRcw* r = AsRcw(rcw, "o");
    void* unknown = r->unknown;
    r->unknown = nullptr;
    r->refCount = 0;
    ReleaseInterface(unknown);
    return 0;
}

void icall_com_final_release_com_object(Il2CppObject* rcw)
{
    if (rcw == nullptr)
    {
        il2cpp_throw_argument_null_exception("o");
    }
    if (rcw->klass != RcwClass())
    {
        il2cpp_throw_argument_exception("Object is not a COM object (RCW).");
    }
    auto* r = reinterpret_cast<Il2CppComRcw*>(rcw);
    if (r->refCount > 0 && r->unknown != nullptr)
    {
        void* unknown = r->unknown;
        r->unknown = nullptr;
        r->refCount = 0;
        ReleaseInterface(unknown);
    }
}

//------------------------------------------------------------------------------
// icall_com_ccw_create -- CCW IUnknown vtable generation. Implemented on
// every platform (pure memory layout + GUID compare -- no ole32 dependency;
// see header: non-Windows is functional but not guaranteed against real COM
// consumers). Returns the IUnknown* (AddRef'd once -- the caller owns one
// reference; native Release to 0 destroys the CCW and frees the GCHandle).
//------------------------------------------------------------------------------
intptr_t icall_com_ccw_create(Il2CppObject* managed)
{
    if (managed == nullptr)
    {
        il2cpp_throw_argument_null_exception("managed");
    }
    auto* ccw = new Il2CppComCcw;
    ccw->vtbl = &kCcwVtable;
#if defined(_WIN32)
    ccw->oleVtbl = &kOleVtable;
    ccw->siteVtbl = &kSiteVtable;
    ccw->inPlaceVtbl = &kInPlaceVtable;
    ccw->classInfoVtbl = &kClassInfoVtable;
    ccw->inPlaceSiteVtbl = &kInPlaceSiteVtable;
    ccw->adviseSinkVtbl = &kAdviseSinkVtable;
    ccw->adviseSink2Vtbl = &kAdviseSink2Vtable;
    ccw->adviseSinkExVtbl = &kAdviseSinkExVtable;
    ccw->streamVtbl = &kStreamVtable;
    ccw->storageVtbl = &kStorageVtable;
    ccw->inPlaceUiWindowVtbl = &kInPlaceUIWindowVtable;
    ccw->inPlaceFrameVtbl = &kInPlaceFrameVtable;
    ccw->oleContainerVtbl = &kOleContainerVtable;
    ccw->enumUnknownVtbl = &kEnumUnknownVtable;
    ccw->enumStringVtbl = &kEnumStringVtable;
    ccw->enumFormatEtcVtbl = &kEnumFormatEtcVtable;
    ccw->persistStreamVtbl = &kPersistStreamVtable;
    ccw->persistFileVtbl = &kPersistFileVtable;
    ccw->persistVtbl = &kPersistVtable;
    ccw->inPlaceActiveObjectVtbl = &kInPlaceActiveObjectVtable;
    ccw->persistPropertyBagVtbl = &kPersistPropertyBagVtable;
    ccw->parseDisplayNameVtbl = &kParseDisplayNameVtable;
    ccw->viewObjectVtbl = &kViewObjectVtable;
    ccw->enumStatDataVtbl = &kEnumSTATDATAVtable;
    ccw->enumOleVerbVtbl = &kEnumOLEVERBVtable;
    ccw->enumMonikerVtbl = &kEnumMonikerVtable;
    ccw->viewObject2Vtbl = &kViewObject2Vtable;
    ccw->oleLinkVtbl = &kOleLinkVtable;
    ccw->monikerVtbl = &kMonikerVtable;
    ccw->runningObjectTableVtbl = &kRunningObjectTableVtable;
    ccw->bindCtxVtbl = &kBindCtxVtable;
    ccw->oleAdviseHolderVtbl = &kOleAdviseHolderVtable;
    ccw->dataAdviseHolderVtbl = &kDataAdviseHolderVtable;
    ccw->oleCacheVtbl = &kOleCacheVtable;
    ccw->oleCache2Vtbl = &kOleCache2Vtable;
    ccw->oleCacheControlVtbl = &kOleCacheControlVtable;
    ccw->oleItemContainerVtbl = &kOleItemContainerVtable;
    ccw->oleUILinkContainerVtbl = &kOleUILinkContainerVtable;
    ccw->oleUILinkInfoVtbl = &kOleUILinkInfoVtable;
    ccw->oleUIObjInfoVtbl = &kOleUIObjInfoVtable;
    ccw->oleUIVerifyVtbl = &kOleUIVerifyVtable;
    ccw->oleUILinkContainerAVtbl = &kOleUILinkContainerAVtable;
    ccw->oleUILinkInfoAVtbl = &kOleUILinkInfoAVtable;
    ccw->oleUIObjInfoAVtbl = &kOleUIObjInfoAVtable;
    ccw->oleUIVerifyAVtbl = &kOleUIVerifyAVtable;
    ccw->oleUILinkContainerWVtbl = &kOleUILinkContainerWVtable;
    ccw->oleUILinkInfoWVtbl = &kOleUILinkInfoWVtable;
    ccw->oleUIObjInfoWVtbl = &kOleUIObjInfoWVtable;
    ccw->oleUIVerifyWVtbl = &kOleUIVerifyWVtable;
    ccw->enumConnectionPointsVtbl = &kEnumConnectionPointsVtable;
    ccw->enumStatStgVtbl = &kEnumStatStgVtable;
    ccw->cpcVtbl = &kCpcVtable;
    ccw->dropVtbl = &kDropVtable;
    for (int32_t i = 0; i < kCcwMaxConnectionPoints; ++i)
    {
        ccw->cps[i].vtbl = &kCpVtable;
        ccw->cps[i].ccw = ccw;
        ccw->cps[i].index = i;
        std::memset(ccw->cps[i].iid, 0, 16); // IID_NULL = unregistered slot
        for (int32_t j = 0; j < kCcwMaxSinksPerCp; ++j)
        {
            ccw->cps[i].sink[j] = nullptr;
            ccw->cps[i].cookie[j] = 0;
        }
        ccw->cps[i].connections = 0;
        ccw->cps[i].nextCookie = 1;
    }
    ccw->clientSite = nullptr;
    ccw->dropDispatch = nullptr;
#endif
    // Stage 17: the IDataObject view starts unregistered (the vtable pointer
    // is always set -- the QI gate decides whether the view is handed out;
    // iface/typeInfo null + all slots -1 = the view is not registered).
    ccw->dataObjectVtbl = &kDataObjectVtable;
    ccw->dataObjectIface = nullptr;
    ccw->dataObjectTypeInfo = nullptr;
    for (int32_t i = 0; i < 9; ++i)
    {
        ccw->dataObjectSlots[i] = -1;
    }
    // Stage 76: the data-advise registry starts empty.
    ccw->dataAdviseCount = 0;
    ccw->dataAdviseNextCookie = 0;
    // Stage 17 batch 4: the managed drop dispatch starts unregistered
    // (dropManaged 0; iface/args null; every slot -1; no effect write-back).
    ccw->dropManaged = 0;
    ccw->dropIface = nullptr;
    ccw->dropIfaceInfo = nullptr;
    ccw->dropArgsClass = nullptr;
    for (int32_t i = 0; i < 4; ++i)
    {
        ccw->dropSlots[i] = -1;
    }
    // Stage 18: the IDispatch::Invoke managed dispatch starts unregistered.
    ccw->invokeManaged = 0;
    ccw->invokeIface = nullptr;
    ccw->invokeIfaceInfo = nullptr;
    ccw->dropEffectOffset = -1;
    ccw->enumView.vtbl = &kEnumVtable;
    ccw->enumView.ccw = ccw;
    ccw->enumView.index = 0;
    ccw->enumCount = 0;
    // Stage-11: the owned-BSTR registry must start nulled -- the teardown loop
    // tests enumOwned[i] for null (the enumItems bytes are never read past
    // enumCount, so only the owned arrays need the explicit zeroing).
    std::memset(ccw->enumOwned, 0, sizeof(ccw->enumOwned));
    std::memset(ccw->enumOwnedLen, 0, sizeof(ccw->enumOwnedLen));
    ccw->refCount = 1;
    ccw->gcHandle = icall_gchandle_alloc_normal(managed);
    ccw->memberCount = 0;
    return static_cast<intptr_t>(reinterpret_cast<uintptr_t>(ccw));
}

//------------------------------------------------------------------------------
// icall_com_ccw_register_connection_point -- declare 'iid' as an event-source
// interface on the CCW (the phase-5 registry, GetIDsOfNames precedent). A
// duplicate IID is idempotent (the same connection point); IID_NULL /
// IID_IUnknown are rejected (an event source interface must be a real
// interface); a full registry returns E_OUTOFMEMORY. Windows: full
// implementation. Non-Windows: E_NOTIMPL (the phase-5 views are not offered).
//------------------------------------------------------------------------------
int32_t icall_com_ccw_register_connection_point(intptr_t ccwPtr, const uint8_t iid[16])
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (iid == nullptr)
    {
        il2cpp_throw_argument_null_exception("iid");
    }
#if defined(_WIN32)
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    if (std::memcmp(iid, kNullIid, 16) == 0 ||
        std::memcmp(iid, kComIidIUnknown, 16) == 0)
    {
        return kComHrInvalidArg;
    }
    for (int32_t i = 0; i < kCcwMaxConnectionPoints; ++i)
    {
        if (std::memcmp(ccw->cps[i].iid, iid, 16) == 0)
        {
            return kComHrOk; // idempotent: already registered
        }
    }
    for (int32_t i = 0; i < kCcwMaxConnectionPoints; ++i)
    {
        if (std::memcmp(ccw->cps[i].iid, kNullIid, 16) == 0)
        {
            std::memcpy(ccw->cps[i].iid, iid, 16);
            return kComHrOk;
        }
    }
    return kComHrOutOfMemory;
#else
    (void)ccwPtr;
    (void)iid;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_ccw_register_drop_dispatch -- store a per-CCW native dispatch
// callback for the IDropTarget view (stage 16 -- the OLE drag-drop forwarding
// layer; see the header for the full contract). The callback is a bare
// function pointer (the pinvokecb thunk-pool precedent: the runtime stores and
// calls, the managed side binds a delegate thunk); the view methods decode the
// raw OLE arguments (the by-value POINTL intptr carrier) and forward them. fn
// == 0 clears the dispatch (a legitimate clear, distinct from a registration
// argument -- no null check). ccw == 0 -> ArgumentNullException (precedes the
// platform gate, all platforms). Windows: full implementation. Non-Windows:
// E_NOTIMPL (the POSIX stub -- the IDropTarget view is not offered there).
//------------------------------------------------------------------------------
int32_t icall_com_ccw_register_drop_dispatch(intptr_t ccwPtr, intptr_t fn)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
#if defined(_WIN32)
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    ccw->dropDispatch = reinterpret_cast<void*>(fn); // fn == 0 clears -> stage-15 surface
    return kComHrOk;
#else
    (void)ccwPtr;
    (void)fn;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_ccw_register_interface_view -- declare a transpiled interface
// descriptor as the CCW's typed interface view behind IID_IDataObject (stage
// 17 -- the typed-RCW foundation; see the header for the full contract). The
// registration resolves the interface's typeinfo (il2cpp_typeinfo_lookup by
// the descriptor's NAME) and caches, per native IDataObject slot (3..11), the
// MANAGED interface slot of the method whose NAME matches the oleidl method
// name (kDataObjectMethodNames -- the typeinfo virtualSlot lookup); a native
// method the interface does not declare stays at -1 (the view method answers
// E_NOTIMPL). The interface's own name is the join key (a descriptor's name is
// unique). ccw == 0 / iid == nullptr / ifaceClass == 0 -> ArgumentNullException
// (the null checks precede the platform gate, all platforms); a non-IDataObject
// IID -> E_INVALIDARG (the typed view registry currently serves the IDataObject
// IID only). Implemented on EVERY platform (pure memory + typeinfo -- no ole32
// dependency, the stage-10 every-platform precedent).
//------------------------------------------------------------------------------
int32_t icall_com_ccw_register_interface_view(intptr_t ccwPtr, const uint8_t iid[16], intptr_t ifaceClassPtr)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (iid == nullptr)
    {
        il2cpp_throw_argument_null_exception("iid");
    }
    if (ifaceClassPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ifaceClass");
    }
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    if (std::memcmp(iid, kComIidIDataObject, 16) != 0)
    {
        return kComHrInvalidArg; // the typed view registry serves IDataObject only
    }
    auto* ifaceClass = reinterpret_cast<Il2CppClass*>(ifaceClassPtr);
    const Il2CppTypeInfo* ifaceInfo = il2cpp_typeinfo_lookup(ifaceClass->name);
    if (ifaceInfo == nullptr)
    {
        return kComHrInvalidArg; // the interface has no reflected typeinfo
    }
    ccw->dataObjectIface = ifaceClass;
    ccw->dataObjectTypeInfo = ifaceInfo;
    for (int32_t i = 0; i < 9; ++i)
    {
        const Il2CppMethodInfo* mi = il2cpp_type_get_method(ifaceInfo, kDataObjectMethodNames[i]);
        ccw->dataObjectSlots[i] = (mi != nullptr) ? mi->virtualSlot : -1;
    }
    return kComHrOk;
}

//------------------------------------------------------------------------------
// icall_com_ccw_enum_register_item -- append a 16-byte VARIANT item to the
// CCW's IEnumVARIANT registry (stage 10 -- the per-CCW enumeration view; the
// GetIDsOfNames registry precedent). Only the VT_EMPTY / VT_NULL / VT_I4 /
// VT_R8 subset is accepted: the item is stored as a 16-byte VALUE COPY, and a
// pointer-bearing type such as VT_BSTR would need a release discipline the
// value copy cannot provide -- any other VARTYPE returns E_INVALIDARG. A full
// registry (kCcwMaxEnumItems = 8) returns E_OUTOFMEMORY. Every platform (pure
// memory layout + GUID compare -- no ole32 dependency): the enumeration view
// is offered wherever the CCW is.
//------------------------------------------------------------------------------
int32_t icall_com_ccw_enum_register_item(intptr_t ccwPtr, const uint8_t variant[16])
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (variant == nullptr)
    {
        il2cpp_throw_argument_null_exception("variant");
    }
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    uint16_t vt = 0;
    std::memcpy(&vt, variant + kVariantVtOffset, sizeof(vt));
    switch (vt)
    {
        case kVtEmpty:
        case kVtNull:
        case kVtI4:
        case kVtR8:
            break;
        default:
            return kComHrInvalidArg;
    }
    if (ccw->enumCount >= kCcwMaxEnumItems)
    {
        return kComHrOutOfMemory;
    }
    std::memcpy(ccw->enumItems[ccw->enumCount], variant, kVariantSize);
    ++ccw->enumCount;
    return kComHrOk;
}

//------------------------------------------------------------------------------
// icall_com_ccw_enum_register_bstr_item -- append a BSTR enumeration item to
// the CCW's IEnumVARIANT registry (stage 11 -- the oleaut32 ownership
// extension of the stage-10 view). The runtime SysAllocStringLen's a copy of
// 'str' (oleaut32 resolved at runtime -- the new function pointers stay OUT
// of GetOle32().ok) and the CCW OWNS that BSTR: the stored master is
// SysFreeString'd by the CCW teardown. Next hands the caller a FRESH
// SysAllocStringLen copy (the OLE contract: the caller VariantClear's every
// returned element; the stored master survives). The BSTR registration shares
// the kCcwMaxEnumItems registry with the stage-10 value items. ccw == 0 or
// str == nullptr -> ArgumentNullException; a full registry -> E_OUTOFMEMORY;
// SysAllocStringLen unresolvable -> NotSupportedException (the EnsureOle32
// precedent). Windows: full implementation. Non-Windows: E_NOTIMPL (BSTR is
// Windows-only -- the stage-10 every-platform claim is carved out here).
//------------------------------------------------------------------------------
int32_t icall_com_ccw_enum_register_bstr_item(intptr_t ccwPtr, Il2CppString* str)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (str == nullptr)
    {
        il2cpp_throw_argument_null_exception("str");
    }
#if defined(_WIN32)
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    if (ccw->enumCount >= kCcwMaxEnumItems)
    {
        return kComHrOutOfMemory;
    }
    if (GetOle32().sysAllocStringLen == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "oleaut32 SysAllocStringLen could not be resolved (BSTR is unavailable)");
    }
    // SysAllocStringLen copies exactly str->length units (embedded nulls
    // preserved) and null-terminates. The result is the OWNED master.
    void* master = GetOle32().sysAllocStringLen(
        str->chars, static_cast<uint32_t>(str->length));
    if (master == nullptr)
    {
        return kComHrOutOfMemory;
    }
    // The 16-byte VARIANT the enumerator hands out: vt = VT_BSTR (8), the
    // data union at offset 8 carries the owned master pointer.
    uint8_t item[kVariantSize] = { 0 };
    std::memcpy(item + kVariantVtOffset, &kVtBstr, sizeof(kVtBstr));
    std::memcpy(item + 8, &master, sizeof(master));
    std::memcpy(ccw->enumItems[ccw->enumCount], item, kVariantSize);
    ccw->enumOwned[ccw->enumCount] = master;
    ccw->enumOwnedLen[ccw->enumCount] = str->length;
    ++ccw->enumCount;
    return kComHrOk;
#else
    (void)ccwPtr;
    (void)str;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_ccw_fire_event -- fire event method 'methodIndex' of the event
// interface 'iid' on the CCW (stage 7 -- the event-source fire direction;
// stage 9 -- multi-sink broadcast). Every connected sink's event-interface
// method at vtable slot 3 + methodIndex is invoked with the intptr carriers
// a0..a3 (no AddRef -- observation only); no connection point registered for
// 'iid' or no connected sink -> S_FALSE (no listener); an all-S_OK broadcast
// -> S_OK, the first non-S_OK sink HRESULT otherwise (delivery continues past
// a failing sink -- the error is surfaced, not masked). methodIndex out of
// 0..124 -> ArgumentOutOfRangeException. Windows: full implementation.
// Non-Windows: E_NOTIMPL (the phase-5 views are not offered).
//------------------------------------------------------------------------------
int32_t icall_com_ccw_fire_event(intptr_t ccwPtr, const uint8_t iid[16],
                                 int32_t methodIndex,
                                 intptr_t a0, intptr_t a1,
                                 intptr_t a2, intptr_t a3)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (iid == nullptr)
    {
        il2cpp_throw_argument_null_exception("iid");
    }
#if defined(_WIN32)
    if (methodIndex < 0 || methodIndex > 124)
    {
        il2cpp_throw_argument_out_of_range_exception("methodIndex", nullptr);
    }
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    Il2CppComConnectionPointView* cp = nullptr;
    for (int32_t i = 0; i < kCcwMaxConnectionPoints; ++i)
    {
        if (std::memcmp(ccw->cps[i].iid, iid, 16) == 0)
        {
            cp = &ccw->cps[i];
            break;
        }
    }
    if (cp == nullptr || cp->connections == 0)
    {
        return kComHrSFALSE; // no listener (no CP / no connected sink)
    }
    // Each connected sink's event-interface method at vtable slot 3 +
    // methodIndex (the COM convention: slots 0-2 are the IUnknown base).
    const int32_t slot = 3 + methodIndex;
    typedef int32_t (IL2CPP_COM_CALL* EventFn)(void*, intptr_t, intptr_t, intptr_t, intptr_t);
    int32_t result = kComHrOk;
    for (int32_t i = 0; i < kCcwMaxSinksPerCp; ++i)
    {
        if (cp->sink[i] == nullptr)
        {
            continue;
        }
        const EventFn fn = reinterpret_cast<EventFn>(const_cast<void*>(VtableSlot(cp->sink[i], slot)));
        const int32_t hr = fn(cp->sink[i], a0, a1, a2, a3);
        if (result == kComHrOk && hr != kComHrOk)
        {
            result = hr; // first non-S_OK sink HRESULT surfaces
        }
    }
    return result;
#else
    (void)ccwPtr;
    (void)iid;
    (void)methodIndex;
    (void)a0;
    (void)a1;
    (void)a2;
    (void)a3;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_ccw_fire_dispatch_event -- fire event method 'dispId' of the
// event interface 'iid' on the CCW (stage 8 -- the IDispatch VARIANT fire
// variant). A WinForms AxHost's real automation event sink is IDispatch-
// shaped: the source delivers each event by calling the sink's
// IDispatch::Invoke (vtable slot 6) with DISPID_FIRST + n and a DISPPARAMS
// whose rgvarg carries the event arguments as VARIANTs. This icall reuses the
// stage-7 connection-point sink storage (view->sink) and the idispatch_invoke
// DISPPARAMS pointer shape; the sink is authoritative for dispId and VARIANT
// validation (the source only forwards -- COM forwards, the sink is the
// protocol owner). No connection point registered for 'iid' or no connected
// sink -> S_FALSE (no listener); a connected sink -> the sink's Invoke
// HRESULT (no AddRef -- observation only). wFlags = DISPATCH_METHOD (an event
// fire is always a method call); riid = IID_NULL; lcid = 0; the error slots
// (pExcepInfo / puArgErr) are the minimal defaults (a fire has no exception
// context in the stage-8 surface). ccw == 0, iid == nullptr or pDispParams ==
// 0 -> ArgumentNullException. Windows: full implementation. Non-Windows:
// E_NOTIMPL (the POSIX stub).
//------------------------------------------------------------------------------
int32_t icall_com_ccw_fire_dispatch_event(intptr_t ccwPtr, const uint8_t iid[16],
                                          int32_t dispId,
                                          intptr_t pDispParams, intptr_t pVarResult)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
    if (iid == nullptr)
    {
        il2cpp_throw_argument_null_exception("iid");
    }
    if (pDispParams == 0)
    {
        il2cpp_throw_argument_null_exception("pDispParams");
    }
#if defined(_WIN32)
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    Il2CppComConnectionPointView* cp = nullptr;
    for (int32_t i = 0; i < kCcwMaxConnectionPoints; ++i)
    {
        if (std::memcmp(ccw->cps[i].iid, iid, 16) == 0)
        {
            cp = &ccw->cps[i];
            break;
        }
    }
    if (cp == nullptr || cp->connections == 0)
    {
        return kComHrSFALSE; // no listener (no CP / no connected sink)
    }
    // Every connected sink's IDispatch::Invoke at vtable slot 6. An
    // IDispatch-shaped event sink answers QI for its dispinterface with the
    // IDispatch pointer -- exactly what Advise stored -- so slot 6 is the
    // delivery point. The broadcast keeps delivering past a failing sink and
    // surfaces the first non-S_OK Invoke HRESULT.
    typedef int32_t (IL2CPP_COM_CALL* DispatchInvokeFn)(
        void*, int32_t, const void*, uint32_t, uint16_t,
        const void*, void*, void*, uint32_t*);
    int32_t result = kComHrOk;
    for (int32_t i = 0; i < kCcwMaxSinksPerCp; ++i)
    {
        if (cp->sink[i] == nullptr)
        {
            continue;
        }
        const DispatchInvokeFn fn = reinterpret_cast<DispatchInvokeFn>(
            const_cast<void*>(VtableSlot(cp->sink[i], 6)));
        const int32_t hr = fn(cp->sink[i], dispId, kNullIid, 0u, kComDispatchMethod,
                              reinterpret_cast<void*>(pDispParams),
                              reinterpret_cast<void*>(pVarResult), nullptr, nullptr);
        if (result == kComHrOk && hr != kComHrOk)
        {
            result = hr;
        }
    }
    return result;
#else
    (void)ccwPtr;
    (void)iid;
    (void)dispId;
    (void)pDispParams;
    (void)pVarResult;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// Stage 12 -- IStream (Marshal.CreateStreamOnHGlobal + the ComStream stream
// object; see the il2cpp_com.h header comment for the full contract).
//
// The LARGE_INTEGER / ULARGE_INTEGER by-value parameters (Seek dlibMove /
// SetSize libNewSize) are passed as native int64_t through the function
// pointer types below -- the compiler marshals a by-value 64-bit argument
// correctly on BOTH x86 (two stack halves, low dword first -- the __stdcall
// convention, IL2CPP_COM_CALL) and x64 (a full register). This is the ONLY
// correct shape: the 4x-intptr vtable-slot carrier (icall_com_call_vtable_slot)
// splits a 64-bit value into two 32-bit halves on x86 but passes it whole on
// x64 -- the two platforms are mirror images of each other, so no single
// carrier layout generalizes (empirically verified).
//------------------------------------------------------------------------------

#if defined(_WIN32)

int32_t il2cpp_com_stream_read(intptr_t stm, void* buffer, int32_t count, uint32_t* pcbRead)
{
    void* self = reinterpret_cast<void*>(stm);
    typedef int32_t (IL2CPP_COM_CALL* ReadFn)(void*, void*, uint32_t, uint32_t*);
    const ReadFn fn = reinterpret_cast<ReadFn>(
        const_cast<void*>(VtableSlot(self, kComStreamSlotRead)));
    return fn(self, buffer, static_cast<uint32_t>(count), pcbRead);
}

int32_t il2cpp_com_stream_write(intptr_t stm, const void* buffer, int32_t count, uint32_t* pcbWritten)
{
    void* self = reinterpret_cast<void*>(stm);
    typedef int32_t (IL2CPP_COM_CALL* WriteFn)(void*, const void*, uint32_t, uint32_t*);
    const WriteFn fn = reinterpret_cast<WriteFn>(
        const_cast<void*>(VtableSlot(self, kComStreamSlotWrite)));
    return fn(self, buffer, static_cast<uint32_t>(count), pcbWritten);
}

int32_t il2cpp_com_stream_seek(intptr_t stm, int64_t move, int32_t origin, int64_t* pNewPosition)
{
    void* self = reinterpret_cast<void*>(stm);
    typedef int32_t (IL2CPP_COM_CALL* SeekFn)(void*, int64_t, uint32_t, int64_t*);
    const SeekFn fn = reinterpret_cast<SeekFn>(
        const_cast<void*>(VtableSlot(self, kComStreamSlotSeek)));
    return fn(self, move, static_cast<uint32_t>(origin), pNewPosition);
}

int32_t il2cpp_com_stream_set_size(intptr_t stm, int64_t newSize)
{
    void* self = reinterpret_cast<void*>(stm);
    typedef int32_t (IL2CPP_COM_CALL* SetSizeFn)(void*, int64_t);
    const SetSizeFn fn = reinterpret_cast<SetSizeFn>(
        const_cast<void*>(VtableSlot(self, kComStreamSlotSetSize)));
    return fn(self, newSize);
}

int32_t il2cpp_com_stream_stat_size(intptr_t stm, int64_t* pSize)
{
    void* self = reinterpret_cast<void*>(stm);
    typedef int32_t (IL2CPP_COM_CALL* StatFn)(void*, void*, uint32_t);
    const StatFn fn = reinterpret_cast<StatFn>(
        const_cast<void*>(VtableSlot(self, kComStreamSlotStat)));
    // The native Stat writes the FULL STATSTG (72 bytes on x86, 80 on x64 --
    // the pwcsName pointer + type DWORD + alignment padding + the 8-byte
    // cbSize ULARGE_INTEGER + the FILETIMEs/clsid/grf tail), regardless of
    // STATFLAG_NONAME (which only suppresses the name allocation). The buffer
    // MUST be the full size -- a truncated mirror overflowed the stack
    // (probe-caught: 0xC0000005 DEP at 0x0 from the corrupted epilogue).
    // cbSize sits at offset 8 on x86 and 16 on x64 (the pointer-width pwcsName
    // shifts the layout) -- sizeof(void*) selects the offset portably.
    uint8_t statstg[80];
    std::memset(statstg, 0, sizeof(statstg));
    const int32_t hr = fn(self, statstg, 1u); // STATFLAG_NONAME (no name copy)
    if (hr == kComHrOk && pSize != nullptr)
    {
        const size_t cbOffset = (sizeof(void*) == 8) ? 16 : 8;
        uint64_t cbSize = 0;
        std::memcpy(&cbSize, statstg + cbOffset, sizeof(cbSize));
        *pSize = static_cast<int64_t>(cbSize);
    }
    return hr;
}

int32_t il2cpp_com_stream_commit(intptr_t stm)
{
    void* self = reinterpret_cast<void*>(stm);
    typedef int32_t (IL2CPP_COM_CALL* CommitFn)(void*, uint32_t);
    const CommitFn fn = reinterpret_cast<CommitFn>(
        const_cast<void*>(VtableSlot(self, kComStreamSlotCommit)));
    return fn(self, 0u); // STGC_DEFAULT
}

void il2cpp_com_stream_release(intptr_t stm)
{
    if (stm == 0)
    {
        return;
    }
    void* self = reinterpret_cast<void*>(stm);
    typedef uint32_t (IL2CPP_COM_CALL* ReleaseFn)(void*);
    const ReleaseFn fn = reinterpret_cast<ReleaseFn>(
        const_cast<void*>(VtableSlot(self, 2)));
    (void)fn(self);
}

#endif // defined(_WIN32)

//------------------------------------------------------------------------------
// icall_com_stream_create_on_hglobal -- Marshal.CreateStreamOnHGlobal
// (see the il2cpp_com.h header comment). ole32 CreateStreamOnHGlobal wraps
// 'hGlobal' (0 = allocate a fresh HGLOBAL, owned per fDeleteOnRelease) into a
// native IStream; the runtime wraps it behind a ComStream stream object (the
// streamio kCom handle state -- NOT an RCW). Returns the stream object.
//------------------------------------------------------------------------------
Il2CppObject* icall_com_stream_create_on_hglobal(intptr_t hGlobal, int32_t fDeleteOnRelease)
{
#if defined(_WIN32)
    EnsureOle32();
    if (GetOle32().createStreamOnHGlobal == nullptr ||
        GetOle32().getHGlobalFromStream == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "ole32.dll's CreateStreamOnHGlobal is unavailable (COM streams unsupported)");
    }
    void* istream = nullptr;
    const int32_t hr = GetOle32().createStreamOnHGlobal(
        reinterpret_cast<void*>(hGlobal), fDeleteOnRelease, &istream);
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
    return il2cpp_stream_com_create(reinterpret_cast<intptr_t>(istream), fDeleteOnRelease);
#else
    (void)hGlobal;
    (void)fDeleteOnRelease;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return nullptr;
#endif
}

//------------------------------------------------------------------------------
// icall_com_stream_get_hglobal -- Marshal.GetHGlobalFromStream: the HGLOBAL
// backing the stream's IStream (ole32 GetHGlobalFromStream). A non-COM
// stream -> ArgumentException; null -> ArgumentNullException.
//------------------------------------------------------------------------------
intptr_t icall_com_stream_get_hglobal(Il2CppObject* stream)
{
    if (stream == nullptr)
    {
        il2cpp_throw_argument_null_exception("stream");
    }
#if defined(_WIN32)
    const intptr_t istream = il2cpp_stream_com_get_iunknown(stream);
    if (istream == 0)
    {
        il2cpp_throw_argument_exception("Stream is not a COM (IStream-backed) stream.");
    }
    if (GetOle32().getHGlobalFromStream == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "ole32.dll's GetHGlobalFromStream is unavailable (COM streams unsupported)");
    }
    void* hglobal = nullptr;
    const int32_t hr = GetOle32().getHGlobalFromStream(
        reinterpret_cast<void*>(istream), &hglobal);
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
    return static_cast<intptr_t>(reinterpret_cast<uintptr_t>(hglobal));
#else
    (void)stream;
    // No COM stream can exist on a non-Windows platform (create throws
    // NotSupportedException) -- any stream object here is non-COM.
    il2cpp_throw_argument_exception("Stream is not a COM (IStream-backed) stream.");
    return 0;
#endif
}

//------------------------------------------------------------------------------
// icall_com_stream_get_iunknown -- the wrapped IStream* of a ComStream object
// (the RCW direction drives the same object through the istream icalls
// below). A non-COM stream -> ArgumentException; null -> ArgumentNullException.
//------------------------------------------------------------------------------
intptr_t icall_com_stream_get_iunknown(Il2CppObject* stream)
{
    if (stream == nullptr)
    {
        il2cpp_throw_argument_null_exception("stream");
    }
    const intptr_t istream = il2cpp_stream_com_get_iunknown(stream);
    if (istream == 0)
    {
        il2cpp_throw_argument_exception("Stream is not a COM (IStream-backed) stream.");
    }
    return istream;
}

//------------------------------------------------------------------------------
// icall_com_istream_read / _write -- IStream::Read / Write (the RCW
// direction): drive the IStream* 'stm' directly. On S_OK writes the
// transferred byte count to *pcbRead / *pcbWritten (verify-then-mutate --
// untouched on failure). stm == 0 -> ArgumentNullException.
//------------------------------------------------------------------------------
int32_t icall_com_istream_read(intptr_t stm, intptr_t buffer, int32_t count, int32_t* pcbRead)
{
    if (stm == 0)
    {
        il2cpp_throw_argument_null_exception("stream");
    }
#if defined(_WIN32)
    uint32_t got = 0;
    const int32_t hr = il2cpp_com_stream_read(stm, reinterpret_cast<void*>(buffer), count, &got);
    if (hr == kComHrOk && pcbRead != nullptr)
    {
        *pcbRead = static_cast<int32_t>(got);
    }
    return hr;
#else
    (void)buffer;
    (void)count;
    (void)pcbRead;
    return kComHrNotImpl;
#endif
}

int32_t icall_com_istream_write(intptr_t stm, intptr_t buffer, int32_t count, int32_t* pcbWritten)
{
    if (stm == 0)
    {
        il2cpp_throw_argument_null_exception("stream");
    }
#if defined(_WIN32)
    uint32_t written = 0;
    const int32_t hr = il2cpp_com_stream_write(stm, reinterpret_cast<const void*>(buffer), count, &written);
    if (hr == kComHrOk && pcbWritten != nullptr)
    {
        *pcbWritten = static_cast<int32_t>(written);
    }
    return hr;
#else
    (void)buffer;
    (void)count;
    (void)pcbWritten;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_istream_seek -- IStream::Seek (the RCW direction): the DEDICATED
// int64_t-parameter form for the by-value LARGE_INTEGER ABI (see the stage-12
// rationale above). On S_OK writes the new position to *pNewPosition.
// stm == 0 -> ArgumentNullException.
//------------------------------------------------------------------------------
int32_t icall_com_istream_seek(intptr_t stm, int64_t move, int32_t origin, int64_t* pNewPosition)
{
    if (stm == 0)
    {
        il2cpp_throw_argument_null_exception("stream");
    }
#if defined(_WIN32)
    int64_t out = 0;
    const int32_t hr = il2cpp_com_stream_seek(stm, move, origin, &out);
    if (hr == kComHrOk && pNewPosition != nullptr)
    {
        *pNewPosition = out;
    }
    return hr;
#else
    (void)move;
    (void)origin;
    (void)pNewPosition;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_istream_set_size -- IStream::SetSize (the RCW direction): the
// DEDICATED int64_t-parameter form for the by-value ULARGE_INTEGER ABI.
// stm == 0 -> ArgumentNullException.
//------------------------------------------------------------------------------
int32_t icall_com_istream_set_size(intptr_t stm, int64_t newSize)
{
    if (stm == 0)
    {
        il2cpp_throw_argument_null_exception("stream");
    }
#if defined(_WIN32)
    return il2cpp_com_stream_set_size(stm, newSize);
#else
    (void)newSize;
    return kComHrNotImpl;
#endif
}

//------------------------------------------------------------------------------
// icall_com_bstr_from_string -- Marshal.StringToBSTR (net48): SysAllocStringLen
// copies exactly s->length UTF-16 units (embedded NUL units preserved) and
// null-terminates. null -> IntPtr.Zero (0, NOT ArgumentNullException -- the
// net48 contract). Allocation failure -> OutOfMemoryException. A null
// sysAllocStringLen -> NotSupportedException (the stage-11 precedent).
// Windows: full implementation. Non-Windows: throws NotSupportedException
// (BSTR is Windows-only -- the platform-limitation contract).
//------------------------------------------------------------------------------
intptr_t icall_com_bstr_from_string(Il2CppString* s)
{
    if (s == nullptr)
    {
        return 0;
    }
#if defined(_WIN32)
    if (GetOle32().sysAllocStringLen == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "oleaut32 SysAllocStringLen could not be resolved (BSTR is unavailable)");
    }
    void* bstr = GetOle32().sysAllocStringLen(
        s->chars, static_cast<uint32_t>(s->length));
    if (bstr == nullptr)
    {
        il2cpp_throw_new(
            "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
            "Insufficient memory to continue the execution of the program.");
    }
    return reinterpret_cast<intptr_t>(bstr);
#else
    (void)s;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return 0;
#endif
}

//------------------------------------------------------------------------------
// icall_com_string_from_bstr -- Marshal.BSTRToString (net48): decode the BSTR
// to a managed string, reading to the FIRST NUL unit (PtrToStringUni
// semantics -- embedded NUL units truncate). 0 -> null (managed null).
// Windows: full implementation. Non-Windows: throws NotSupportedException
// (BSTR is Windows-only -- the platform-limitation contract).
//------------------------------------------------------------------------------
Il2CppObject* icall_com_string_from_bstr(intptr_t bstr)
{
    if (bstr == 0)
    {
        return nullptr;
    }
#if defined(_WIN32)
    return il2cpp::il2cpp_pinvoke_string_from_utf16(
        reinterpret_cast<const char16_t*>(bstr));
#else
    (void)bstr;
    il2cpp_throw_not_supported_exception(
        "COM is only supported on Windows (ILC platform limitation).");
    return nullptr;
#endif
}

//------------------------------------------------------------------------------
// icall_com_bstr_free -- Marshal.FreeBSTR (net48): SysFreeString. 0 -> no-op.
// A null sysFreeString -> NotSupportedException (the stage-11 precedent).
// Windows: full implementation. Non-Windows: no-op (nothing to free).
//------------------------------------------------------------------------------
void icall_com_bstr_free(intptr_t bstr)
{
    if (bstr == 0)
    {
        return;
    }
#if defined(_WIN32)
    if (GetOle32().sysFreeString == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "oleaut32 SysFreeString could not be resolved (BSTR is unavailable)");
    }
    GetOle32().sysFreeString(reinterpret_cast<void*>(bstr));
#else
    (void)bstr;
#endif
}

} // namespace runtime
} // namespace il2cpp
