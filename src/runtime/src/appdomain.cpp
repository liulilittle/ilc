//------------------------------------------------------------------------------
// appdomain.cpp -- System.AppDomain minimal object closure (see appdomain.h
// for the contract).
//
// The AOT process IS the default AppDomain: get_CurrentDomain returns a
// process-wide singleton, the getters derive from the process exe path or
// return the documented net48 constants, SetData/GetData is a process-level
// table, and CreateInstance* resolves REGISTERED types through the metadata
// registry (il2cpp_class_by_name -- metadata.cpp) and allocates through
// il2cpp_object_new (Stopwatch/ArrayList carrier precedent). net48 byte-exact
// surface is pinned by the p-adb probe oracle (p-adb_adb.txt).
//------------------------------------------------------------------------------

#ifdef _MSC_VER
    #define _CRT_SECURE_NO_WARNINGS
    #ifndef _CRT_NONSTDC_NO_DEPRECATE
    #define _CRT_NONSTDC_NO_DEPRECATE
    #endif
#endif

#include "appdomain.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_* (argument/null/file-not-found/not-supported/null-ref)
#include "il2cpp_runtime.h"       // il2cpp_object_new / il2cpp_class_register
#include "il2cpp_gc.h"            // il2cpp_gc_add_static_root (singletons/table must survive)
#include "il2cpp_delegate_ext.h"  // icall_delegate_combine / icall_delegate_remove
#include "il2cpp_stringbuilder.h" // icall_stringbuilder_ctor_default (CreateInstance default ctor)
#include "string_compare.h"       // il2cpp_string_equals_ordinal (GetData key compare)
#include "il2cpp_gcroot.h"        // il2cpp::GcRoot (RAII root across intra-icall allocations)

#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #ifndef NOMINMAX
    #define NOMINMAX
    #endif
    #include <windows.h>
#else
    #include <unistd.h>
#endif

//------------------------------------------------------------------------------
// Descriptors (GLOBAL scope -- the transpiler emits &System_AppDomain_class
// for isinst/castclass; &System_AppDomainSetup_class and
// &System_Runtime_Remoting_ObjectHandle_class for the SetupInformation /
// CreateInstance results). All three are registered lazily through
// il2cpp_appdomain_class() (see below).
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_AppDomain_class = {
    "System.AppDomain",                    // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppAppDomainObject)), // instanceSize
    0u,                                    // refFieldBitmap: zero-state carrier
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass (an AppDomain IS-A object)
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

il2cpp::Il2CppClass System_AppDomainSetup_class = {
    "System.AppDomainSetup",               // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppAppDomainSetupObject)), // instanceSize
    0u,                                    // refFieldBitmap: zero-state carrier
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

il2cpp::Il2CppClass System_Runtime_Remoting_ObjectHandle_class = {
    "System.Runtime.Remoting.ObjectHandle", // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObjectHandleObject)), // instanceSize
    1u << 1,                               // refFieldBitmap: slot 1 is the wrapped object
    1,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

namespace il2cpp {

namespace {

bool g_appdomainClassesRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_appdomain_class -- the System.AppDomain descriptor (lazily
// registered on first use; also registers the AppDomainSetup + ObjectHandle
// descriptors -- they share one lazy registration). Idempotent:
// il2cpp_class_register dedupes by pointer.
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_appdomain_class()
{
    if (!g_appdomainClassesRegistered)
    {
        il2cpp_class_register(&System_AppDomain_class);
        il2cpp_class_register(&System_AppDomainSetup_class);
        il2cpp_class_register(&System_Runtime_Remoting_ObjectHandle_class);
        g_appdomainClassesRegistered = true;
    }
    return &System_AppDomain_class;
}

//------------------------------------------------------------------------------
// The process-wide singletons + the UnhandledException event chain. Every
// pointer is a PERMANENT static GC root (the collector scans roots; a native
// icall local is never a root, so each live process object must be registered
// -- the appcontext switch-table / exception-message precedent).
//------------------------------------------------------------------------------
Il2CppObject* g_singleton = nullptr;
Il2CppObject* g_setupSingleton = nullptr;
Il2CppObject* g_unhandledChain = nullptr;

void EnsureStaticRoots()
{
    static bool s_rootsRegistered = false;
    if (s_rootsRegistered)
    {
        return;
    }
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_singleton));
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_setupSingleton));
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_unhandledChain));
    s_rootsRegistered = true;
}

//------------------------------------------------------------------------------
// il2cpp_appdomain_singleton -- the net48 default domain, lazily allocated
// and GC-rooted. The SAME pointer on every call (refeq across
// CurrentDomain/GetDefaultDomain arms). Declared in namespace il2cpp
// (appdomain.h); stubs.cpp's icall_appdomain_get_default_domain calls it via
// il2cpp::il2cpp_appdomain_singleton.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_appdomain_singleton()
{
    if (g_singleton != nullptr)
    {
        return g_singleton;
    }
    il2cpp_appdomain_class();
    EnsureStaticRoots();
    g_singleton = il2cpp_object_new(&System_AppDomain_class);
    return g_singleton;
}

Il2CppObject* il2cpp_appdomain_setup_singleton()
{
    if (g_setupSingleton != nullptr)
    {
        return g_setupSingleton;
    }
    il2cpp_appdomain_class();
    EnsureStaticRoots();
    g_setupSingleton = il2cpp_object_new(&System_AppDomainSetup_class);
    return g_setupSingleton;
}

} // namespace il2cpp

namespace il2cpp { namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Process exe path (computed once). Windows: GetModuleFileNameW -- the
// canonical host-exe query. POSIX: /proc/self/exe readlink (Linux); on a
// failure both fall back to a fixed "unknown" name with a "." separator so
// the derived strings stay non-null and separator-terminated (the p-adb
// probe asserts shapes, not absolute values).
//------------------------------------------------------------------------------
struct ExePath
{
    std::wstring dir;  // directory with a trailing separator, e.g. L"C:\x\"
    std::wstring file; // base name with extension, e.g. L"probe.exe"
};

std::wstring TrailingSeparator(const std::wstring& dir)
{
    return dir.empty() ? L"."
                       : dir; // GetModuleFileNameW already yields the trailing separator below
}

ExePath GetExePath()
{
    static ExePath s_cache;
    static bool s_cached = false;
    if (s_cached)
    {
        return s_cache;
    }
#if defined(_WIN32)
    wchar_t buf[32768];
    const DWORD n = ::GetModuleFileNameW(nullptr, buf, 32768);
    const std::wstring path = (n == 0 || n >= 32768) ? L"" : std::wstring(buf, n);
#else
    std::wstring path;
    char link[4096];
    const ssize_t got = ::readlink("/proc/self/exe", link, sizeof(link) - 1);
    if (got > 0)
    {
        link[got] = '\0';
        std::string u8(link);
        path.clear();
        for (char c : u8)
        {
            path.push_back(static_cast<wchar_t>(static_cast<uint8_t>(c)));
        }
    }
#endif
    if (path.empty())
    {
        s_cache.dir = L"./";
        s_cache.file = L"unknown.exe";
        s_cached = true;
        return s_cache;
    }
    const size_t cut = path.find_last_of(L"\\/");
    s_cache.dir = (cut == std::wstring::npos) ? L"" : path.substr(0, cut + 1);
    s_cache.file = (cut == std::wstring::npos) ? path : path.substr(cut + 1);
    if (s_cache.dir.empty())
    {
        s_cache.dir = L"./";
    }
    s_cached = true;
    return s_cache;
}

// UTF-16 -> UTF-8 (the registry/store keys and type names are ASCII by
// convention; non-ASCII units pass through a correct UTF-8 conversion so the
// comparison helpers stay sound).
std::string ToUtf8(const std::wstring& w)
{
    std::string out;
    for (wchar_t wc : w)
    {
        uint32_t cp = static_cast<uint32_t>(wc);
        if (cp < 0x80)
        {
            out.push_back(static_cast<char>(cp));
        }
        else if (cp < 0x800)
        {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
        else
        {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }
    return out;
}

// Managed string -> UTF-8 bytes (ASCII fast path; the registry names are
// ASCII).
std::string StrToUtf8(Il2CppObject* s)
{
    auto str = reinterpret_cast<Il2CppString*>(s);
    std::string out;
    if (str == nullptr)
    {
        return out;
    }
    for (int32_t i = 0; i < str->length; ++i)
    {
        char16_t c = str->chars[i];
        if (c < 0x80)
        {
            out.push_back(static_cast<char>(c));
        }
        else if (c < 0x800)
        {
            out.push_back(static_cast<char>(0xC0 | (c >> 6)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        }
        else
        {
            out.push_back(static_cast<char>(0xE0 | (c >> 12)));
            out.push_back(static_cast<char>(0x80 | ((c >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        }
    }
    return out;
}

// ASCII key compare without allocating a managed literal (the derived-key
// lookups APPBASE / APP_CONFIG_FILE run per GetData).
bool KeyEquals(const Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(s->length) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<uint8_t>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// ASCII case-insensitive token compare (the assembly-name gate: "mscorlib").
bool AsciiEqualsIgnoreCase(const std::string& a, const char* b)
{
    for (size_t i = 0; i < a.size(); ++i)
    {
        if (b[i] == '\0')
        {
            return false;
        }
        char ca = a[i];
        char cb = b[i];
        if (ca >= 'A' && ca <= 'Z')
        {
            ca = static_cast<char>(ca - 'A' + 'a');
        }
        if (cb >= 'A' && cb <= 'Z')
        {
            cb = static_cast<char>(cb - 'A' + 'a');
        }
        if (ca != cb)
        {
            return false;
        }
    }
    return b[a.size()] == '\0';
}

//------------------------------------------------------------------------------
// Process-level GetData/SetData key -> object table (single-domain model: no
// second domain can exist, so the net48 per-domain isolation is
// unobservable and a Do/process-local table is byte-equivalent -- the p-adb
// "Do/process-local" criterion).
//
// NB1 (adm1 review): the table is rooted as ONE root RANGE over the whole
// fixed slot array (il2cpp_gc_add_root_range -- 2 contiguous reference slots
// per entry), NOT one il2cpp_gc_add_static_root per key/value. Per-slot
// static roots are counted against kMaxStaticRoots=128 (gc.cpp): 63 entries
// already exceeded it (129 roots incl. the 3 domain roots) and aborted
// FailLoud; the range registration is a separate fixed table (kMaxRootRanges
// = 64) and always uses exactly 1 slot regardless of how many keys are
// stored. Capacity is 256 (covers the NB1 probe's 70+ keys and any realistic
// net48 surface) and overflow FAILS LOUD (never silently drops, unlike
// net48's effectively unbounded map -- a documented AOT bound).
//------------------------------------------------------------------------------
struct DataEntry
{
    Il2CppString* key;   // GC reference (scanned through the table root range)
    Il2CppObject* value; // GC reference (scanned through the table root range)
};

constexpr int32_t kMaxDataEntries = 256;
DataEntry g_dataEntries[kMaxDataEntries] = {};
int32_t g_dataCount = 0;

void EnsureDataTableRooted()
{
    static bool s_rooted = false;
    if (s_rooted)
    {
        return;
    }
    il2cpp_gc_add_root_range(
        reinterpret_cast<void**>(&g_dataEntries[0].key),
        kMaxDataEntries * 2); // key + value per entry, contiguous
    s_rooted = true;
}

int32_t FindDataEntry(Il2CppString* key)
{
    for (int32_t i = 0; i < g_dataCount; ++i)
    {
        if (g_dataEntries[i].key != nullptr && il2cpp_string_equals_ordinal(g_dataEntries[i].key, key))
        {
            return i;
        }
    }
    return -1;
}

// Defensive null-receiver guard: generated code null-checks first; the icall
// keeps the same contract (stopwatch.cpp pattern).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// BaseDirectory: exe dir + trailing separator (net48 AppDomain.BaseDirectory).
Il2CppString* BaseDirectoryString()
{
    return il2cpp_string_new_from_utf8(ToUtf8(GetExePath().dir).c_str());
}

// FriendlyName: exe base name incl. extension (net48 AppDomain.FriendlyName).
Il2CppString* FriendlyNameString()
{
    return il2cpp_string_new_from_utf8(ToUtf8(GetExePath().file).c_str());
}

// ConfigurationFile: exe dir + exe base name + ".Config"
// (net48 AppDomainSetup.ConfigurationFile -- "<exe>.exe.Config").
Il2CppString* ConfigurationFileString()
{
    const ExePath& p = GetExePath();
    return il2cpp_string_new_from_utf8(
        (ToUtf8(p.dir) + ToUtf8(p.file) + ".Config").c_str());
}

Il2CppString* GetDataDerived(Il2CppString* key)
{
    if (KeyEquals(key, "APPBASE"))
    {
        return BaseDirectoryString();
    }
    if (KeyEquals(key, "APP_CONFIG_FILE"))
    {
        return ConfigurationFileString();
    }
    return nullptr;
}

Il2CppObject* CreateInstanceCore(Il2CppObject* assemblyName, Il2CppObject* typeName)
{
    if (assemblyName == nullptr)
    {
        il2cpp_throw_argument_null_exception("assemblyName");
    }
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("typeName");
    }
    const std::string asmUtf8 = StrToUtf8(assemblyName);
    // The single-assembly runtime knows NO loader: only the BCL ("mscorlib")
    // is a loadable assembly on the probed net48 surface. Any other assembly
    // name (net48 would attempt a fusion load) -> FileNotFoundException.
    if (!AsciiEqualsIgnoreCase(asmUtf8, "mscorlib"))
    {
        il2cpp_throw_file_not_found_exception("Could not load file or assembly.");
    }
    const std::string typeUtf8 = StrToUtf8(typeName);
    il2cpp::Il2CppClass* klass = il2cpp_class_by_name(typeUtf8.c_str());
    if (klass == nullptr && typeUtf8 == "System.Text.StringBuilder")
    {
        // StringBuilder's descriptor registers lazily on FIRST allocation
        // (constraint #12); CreateInstance is itself a first allocation, so
        // register it here (p-adb / p-adb-gc reach it with no prior
        // StringBuilder). All other registered types stay reachability-gated
        // (the documented W7 boundary).
        klass = il2cpp_stringbuilder_class();
    }
    if (klass == nullptr)
    {
        // Unregistered type: no runtime type loader exists (W7 boundary).
        il2cpp_throw_not_supported_exception(
            "AppDomain.CreateInstance: the requested type is not registered in this runtime.");
    }
    il2cpp::Il2CppObject* obj = il2cpp_object_new(klass);
    // D1 (adm1 review): root the local BEFORE the ctor's internal allocation.
    // icall_stringbuilder_ctor_default allocates the char buffer
    // (NewCharBuffer) and the compacting collector (256KB collection
    // threshold) can MOVE the just-created builder mid-call; the ctor roots
    // ITS OWN copy of self, but this stack local would keep the stale address.
    // GcRoot re-reads the (possibly rewritten) slot after the allocation.
    il2cpp::GcRoot<il2cpp::Il2CppObject*> objRoot(obj, "appdomain_create_core");
    // Run the type's default ctor where the zero-state differs from the
    // ctor state. StringBuilder needs its buffer (capacity 16) before any
    // Append; other registered types' ctors are out of scope (W7 boundary).
    if (klass == il2cpp_stringbuilder_class())
    {
        icall_stringbuilder_ctor_default(objRoot.Get());
        obj = objRoot.Get();
    }
    return obj;
}

} // anonymous namespace

//------------------------------------------------------------------------------
// Public icalls.
//------------------------------------------------------------------------------

Il2CppObject* icall_appdomain_get_current_domain()
{
    return il2cpp_appdomain_singleton();
}

int32_t icall_appdomain_get_id_public(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return 1; // net48 default-domain id
}

Il2CppObject* icall_appdomain_get_friendly_name(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(FriendlyNameString());
}

Il2CppObject* icall_appdomain_get_base_directory(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(BaseDirectoryString());
}

Il2CppObject* icall_appdomain_get_relative_search_path(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return nullptr; // default host has no private search path
}

Il2CppObject* icall_appdomain_get_domain_manager(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return nullptr; // default host; W7: host-injected manager not implemented
}

Il2CppObject* icall_appdomain_get_setup_information(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_appdomain_setup_singleton();
}

Il2CppObject* icall_appdomain_get_data(Il2CppObject* self, Il2CppObject* name)
{
    ThrowIfNullReceiver(self);
    if (name == nullptr)
    {
        il2cpp_throw_argument_null_exception("name");
    }
    Il2CppString* key = reinterpret_cast<Il2CppString*>(name);
    const int32_t idx = FindDataEntry(key);
    if (idx >= 0)
    {
        return g_dataEntries[idx].value;
    }
    Il2CppString* derived = GetDataDerived(key);
    if (derived != nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(derived);
    }
    return nullptr;
}

void icall_appdomain_set_data(Il2CppObject* self, Il2CppObject* name, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    if (name == nullptr)
    {
        il2cpp_throw_argument_null_exception("name");
    }
    Il2CppString* key = reinterpret_cast<Il2CppString*>(name);
    const int32_t idx = FindDataEntry(key);
    if (idx >= 0)
    {
        g_dataEntries[idx].value = value; // overwrite (net48)
        return;
    }
    EnsureDataTableRooted(); // the whole table is ONE root range (NB1)
    if (g_dataCount >= kMaxDataEntries)
    {
        // NB1: an overflow FAILS LOUD -- a silent drop would diverge from
        // net48's effectively unbounded data map. (AOT fixed-table bound:
        // 256 keys, far above any probed/realistic surface.)
        il2cpp_throw_invalid_operation_exception(
            "AppDomain.SetData exceeded the process data-table capacity (256 keys).");
    }
    const int32_t slot = g_dataCount++;
    g_dataEntries[slot].key = key;
    g_dataEntries[slot].value = value;
}

Il2CppObject* icall_appdomain_create_instance(Il2CppObject* self, Il2CppObject* assemblyName,
                                              Il2CppObject* typeName)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* inner = CreateInstanceCore(assemblyName, typeName);
    // D1 (adm1 review): root the inner object across the ObjectHandle
    // allocation below -- il2cpp_object_new can trigger a compacting
    // collection that MOVES inner while this local still holds the old
    // address. Re-read after the allocation.
    il2cpp::GcRoot<il2cpp::Il2CppObject*> innerRoot(inner, "appdomain_create_instance");
    // Wrap in a System.Runtime.Remoting.ObjectHandle carrier (slot 1).
    Il2CppObjectHandleObject* handle = reinterpret_cast<Il2CppObjectHandleObject*>(
        il2cpp_object_new(&System_Runtime_Remoting_ObjectHandle_class));
    inner = innerRoot.Get();
    handle->value = inner;
    il2cpp_write_barrier(reinterpret_cast<void**>(&handle->value), inner);
    return reinterpret_cast<Il2CppObject*>(handle);
}

Il2CppObject* icall_appdomain_create_instance_and_unwrap(Il2CppObject* self, Il2CppObject* assemblyName,
                                                         Il2CppObject* typeName)
{
    ThrowIfNullReceiver(self);
    return CreateInstanceCore(assemblyName, typeName);
}

int32_t icall_appdomain_is_fully_trusted(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return 1; // default host: fully trusted
}

int32_t icall_appdomain_is_homogenous(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return 1; // default host: homogeneous
}

int32_t icall_appdomain_is_default_app_domain(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return 1; // the AOT process IS the default domain
}

void icall_appdomain_add_unhandled_exception(Il2CppObject* self, Il2CppObject* handler)
{
    ThrowIfNullReceiver(self);
    EnsureStaticRoots();
    Il2CppObject* combined = icall_delegate_combine(g_unhandledChain, handler);
    g_unhandledChain = combined;
    il2cpp_write_barrier(reinterpret_cast<void**>(&g_unhandledChain), combined);
}

void icall_appdomain_remove_unhandled_exception(Il2CppObject* self, Il2CppObject* handler)
{
    ThrowIfNullReceiver(self);
    EnsureStaticRoots();
    Il2CppObject* removed = icall_delegate_remove(g_unhandledChain, handler);
    g_unhandledChain = removed;
    il2cpp_write_barrier(reinterpret_cast<void**>(&g_unhandledChain), removed);
}

Il2CppObject* icall_appdomain_setup_get_application_base(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(BaseDirectoryString());
}

Il2CppObject* icall_appdomain_setup_get_configuration_file(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(ConfigurationFileString());
}

Il2CppObject* icall_objecthandle_unwrap(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObjectHandleObject*>(self)->value;
}

//------------------------------------------------------------------------------
// LOUD strains -- runtime NotSupportedException (no assembly loader / CAS
// object model; the mapping makes the single-source difftest probe
// compilable+linkable, see appdomain.h).
//------------------------------------------------------------------------------

int32_t icall_appdomain_execute_assembly(Il2CppObject* self, Il2CppObject* assemblyFile)
{
    ThrowIfNullReceiver(self);
    (void)assemblyFile;
    il2cpp_throw_not_supported_exception(
        "AppDomain.ExecuteAssembly requires a runtime assembly loader (not present in this AOT runtime).");
    return -1; // unreachable (il2cpp_throw_not_supported_exception is noreturn)
}

Il2CppObject* icall_appdomain_get_permission_set(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_throw_not_supported_exception(
        "AppDomain.PermissionSet requires the CAS object model (not present in this runtime).");
    return nullptr; // unreachable
}

Il2CppObject* icall_appdomain_get_evidence(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_throw_not_supported_exception(
        "AppDomain.Evidence requires the CAS object model (not present in this runtime).");
    return nullptr; // unreachable
}

Il2CppObject* icall_appdomain_get_application_trust(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_throw_not_supported_exception(
        "AppDomain.ApplicationTrust requires the CAS object model (not present in this runtime).");
    return nullptr; // unreachable
}

Il2CppObject* icall_appdomain_get_assemblies(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_throw_not_supported_exception(
        "AppDomain.GetAssemblies requires the multi-assembly load model (the AOT runtime compiles a single program).");
    return nullptr; // unreachable
}

Il2CppObject* icall_appdomain_to_string(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    il2cpp_throw_not_supported_exception(
        "AppDomain.ToString requires a culture-localized name (W7: the net48 zh-CN text is not implemented).");
    return nullptr; // unreachable
}

}} // namespace il2cpp::runtime