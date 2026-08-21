//------------------------------------------------------------------------------
// api.cpp -- the ILC runtime's public C API (see il2cpp_api.h).
//
// The C API is the external boundary: every call is wrapped in try/catch so
// a managed exception never crosses the C ABI (iron rule: C++ exceptions do
// not pass through extern "C" boundaries). The last exception message is
// captured into a stable buffer for il2cpp_get_last_exception.
//
// il2cpp_method_invoke_i4 resolves the method by (type, name): the name is
// the transpiled C++ function name; the runtime walks the type registry for
// the descriptor and then matches the method by scanning the type's methods.
// M4 scope: static int32(int32,int32,int32,int32) methods.
//------------------------------------------------------------------------------

#include "../include/il2cpp_api.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <exception>

namespace il2cpp {

namespace {

// The type registry exposes descriptors by name (see metadata.cpp).
// Method lookup: the transpiler emits methods as free functions named
// {Type}_{Method}_{hash}; the registry does not hold method pointers yet
// (reflection metadata is a later milestone), so M4 resolves the method by
// matching a small set of KNOWN exported method names registered by the
// transpiler at startup (il2cpp_runtime_register_export).
//
// Export table: { typeName, methodName, fn } -- registered by generated
// startup code (entry.cpp) for every [UnmanagedExport]/C-API-exposed method.

struct ExportEntry
{
    const char* typeName;
    const char* methodName;
    int32_t (*fn)(int32_t, int32_t, int32_t, int32_t);
};

constexpr int32_t kMaxExports = 256;
ExportEntry g_exports[kMaxExports];
int32_t      g_exportCount = 0;

// Last exception message (stable buffer; valid until the next API call).
char g_lastExceptionMessage[1024];

} // namespace

//------------------------------------------------------------------------------
// il2cpp_runtime_register_export -- generated startup code registers each
// C-API-exposed static method here (M4: invoked by entry.cpp).
//------------------------------------------------------------------------------
void il2cpp_runtime_register_export(const char* typeName, const char* methodName,
    int32_t (*fn)(int32_t, int32_t, int32_t, int32_t))
{
    if (typeName == nullptr || methodName == nullptr)
    {
        std::fprintf(stderr, "il2cpp_runtime_register_export: null type/method name\n");
        return;
    }
    // Idempotent: a repeated il2cpp_runtime_init (the C API contract,
    // capi/main.cpp) re-registers the same exports; a duplicate
    // (type, method) pair is a no-op, keeping the table free of duplicates.
    for (int32_t i = 0; i < g_exportCount; ++i)
    {
        if (std::strcmp(g_exports[i].typeName, typeName) == 0
            && std::strcmp(g_exports[i].methodName, methodName) == 0)
        {
            return;
        }
    }
    if (g_exportCount >= kMaxExports)
    {
        // Fail loud -- fixed-capacity violation, matching the runtime's
        // established style (metadata/gc/pinvoke/delegateext all abort on
        // table overflow). A silently dropped export would make a later
        // il2cpp_method_invoke_i4 return -1 for a registered method.
        std::fprintf(stderr, "il2cpp_runtime_register_export: export table overflow (%d entries)\n", kMaxExports);
        std::abort();
    }
    g_exports[g_exportCount].typeName = typeName;
    g_exports[g_exportCount].methodName = methodName;
    g_exports[g_exportCount].fn = fn;
    ++g_exportCount;
}

} // namespace il2cpp

//------------------------------------------------------------------------------
// C API implementations. NOTE: the public il2cpp_runtime_init is generated
// by the transpiler (it must also register the translated types/exports);
// the runtime provides il2cpp_runtime_bootstrap as its internal half.
//------------------------------------------------------------------------------

void il2cpp_runtime_bootstrap(void)
{
    il2cpp::il2cpp_gc_init();
    // The C API's il2cpp_type_from_name must be able to resolve the core
    // System.* types immediately after il2cpp_runtime_init (an external host
    // may ask for "System.String" before any managed allocation forced the
    // lazy builtin registration). Idempotent: class registration dedupes.
    il2cpp::il2cpp_ensure_builtin_system_classes();
}

void il2cpp_gc_collect(void)
{
    il2cpp::il2cpp_gc_collect();
}

void* il2cpp_type_from_name(const char* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    return il2cpp::il2cpp_class_by_name(name);
}

int32_t il2cpp_method_invoke_i4(void* typeHandle, const char* methodName, int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    // The last-exception message is valid only until the next API call and
    // MUST read as an empty string when no exception is pending (il2cpp_api.h
    // contract). Clear it up front so a successful invoke leaves no stale
    // message behind; the catch handlers below overwrite it on failure.
    il2cpp::g_lastExceptionMessage[0] = '\0';
    if (typeHandle == nullptr || methodName == nullptr)
    {
        return -1;
    }
    try
    {
        for (int32_t i = 0; i < il2cpp::g_exportCount; ++i)
        {
            const auto& e = il2cpp::g_exports[i];
            if (std::strcmp(e.typeName, static_cast<il2cpp::Il2CppClass*>(typeHandle)->name) == 0
                && std::strcmp(e.methodName, methodName) == 0)
            {
                return e.fn(a0, a1, a2, a3);
            }
        }
        return -1; // method not found
    }
    catch (const il2cpp::DotNetException& ex)
    {
        std::snprintf(il2cpp::g_lastExceptionMessage, sizeof(il2cpp::g_lastExceptionMessage), "%s", ex.message_text());
        return 0;
    }
    catch (const std::exception& ex)
    {
        // Iron rule: C++ exceptions do not pass through extern "C" boundaries.
        // A non-managed native exception (e.g. std::bad_alloc from the export
        // body) is reported as a generic boundary error, never escaped.
        (void)ex;
        std::snprintf(il2cpp::g_lastExceptionMessage, sizeof(il2cpp::g_lastExceptionMessage),
                      "unhandled native exception in il2cpp_method_invoke_i4");
        return 0;
    }
    catch (...)
    {
        // Catch-all: any exception shape must be contained at the C ABI.
        std::snprintf(il2cpp::g_lastExceptionMessage, sizeof(il2cpp::g_lastExceptionMessage),
                      "unknown exception in il2cpp_method_invoke_i4");
        return 0;
    }
}

const char* il2cpp_get_last_exception(void)
{
    return il2cpp::g_lastExceptionMessage;
}
