//------------------------------------------------------------------------------
// test_icallb.cpp -- Core-only icall residual surface unit tests (icallb.h:
// the b23icallb domain -- RuntimeAssembly typeinfo-wired surface + the
// AssemblyLoadContext/MetadataImport/Signature/StubHelpers LOUD rejections).
//
// Classification recap (icallb.h header): the net48-aligned subset is
// IMPLEMENTED (assembly_get_type delegates to the typeinfo registry; the
// is_dynamic / is_collectible probes are AOT constants); every pure-Core
// family and every no-model member is LOUD (System.NotSupportedException).
//
// Coverage (normal + edge + error paths):
//   1. assembly_get_type -- a registered type name resolves to the INPUT
//      name string itself (identity -- the simplified Type model); null
//      name -> ArgumentNullException (net48 Assembly.GetType(null) throws);
//      an unregistered name -> null; a malformed (non-ASCII / over-long)
//      name -> null -- the "not found -> null, never throws" contract of
//      net48 Assembly.GetType (and Type.GetType).
//   2. assembly_get_is_dynamic -- 0 (AOT static assembly, never dynamic).
//   3. assembly_get_is_collectible -- 0 (AOT static assembly, never
//      collectible).
//   4. LOUD assertions -- each of the six reject functions throws a
//      DotNetException whose type-name chain starts with
//      "System.NotSupportedException" (the il2cpp_exception_matches rule:
//      a catch(System.NotSupportedException) matches).
//      - assemblyloadcontext (pure Core, no net48),
//      - metadataimport (22-member internal metadata-import handle),
//      - signature (3-member signature-blob ABI),
//      - stubhelpers (6-member internal interop-stub family),
//      - assembly module/token (no module / metadata-token model),
//      - assembly object family (no Assembly object representation).
//   5. LOUD message self-identification -- every reject message names its
//      family (the "clear message" requirement), and the metadataimport
//      message names its FULL 22-member surface including GetDefaultValue
//      (LOUD-definition conformance: icallb.h enumerates 22 members, so a
//      message claiming "22-member" must enumerate all 22 -- regression
//      guard for the omitted GetDefaultValue audit finding).
//
// Plain executable + minimal assertion harness (see test_typecheck.cpp).
//------------------------------------------------------------------------------

#include "icallb.h"

#include "typeinfo.h"            // Il2CppTypeInfo fixtures + registry
#include "reflect_base.h"        // il2cpp_type_get_type (delegation reference)
#include "il2cpp_string.h"       // il2cpp_string_new_from_cs / Il2CppString
#include "il2cpp_exception.h"    // DotNetException / il2cpp_exception_matches

#include <cstdio>
#include <cstring>

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

// Invoke a void reject function and report whether it threw a
// NotSupportedException (chain conformance -- a catch of the thrown type
// itself must match, so the chain's first walk segment is the type).
bool ThrowsNotSupported(void (*fn)())
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches(
                   "System.NotSupportedException", e.type_name()) != 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Invoke a void reject function and report whether its message text names
// 'needle' -- the "clear self-identifying message" requirement of the LOUD
// disposition (the message must survive as a static literal; DotNetException
// carries the pointer without copying).
bool MessageNames(void (*fn)(), const char* needle)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return e.message_text() != nullptr &&
               std::strstr(e.message_text(), needle) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Invoke an assembly_get_type-style query with a null name and report whether
// it threw ArgumentNullException (net48 Assembly.GetType(null) contract).
bool ThrowsArgNull(il2cpp::Il2CppObject* (*fn)(il2cpp::Il2CppObject*),
                   il2cpp::Il2CppObject* arg)
{
    try
    {
        fn(arg);
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches(
                   "System.ArgumentNullException", e.type_name()) != 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Compare a managed string against an ASCII literal (names are ASCII by
// project convention).
bool ManagedEqualsAscii(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    auto str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(str->length) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

} // namespace

//------------------------------------------------------------------------------
// Fixture: one registrable type (mirrors test_reflect_base.cpp's shape --
// a static const Il2CppTypeInfo the transpiler emits per reflected type).
//------------------------------------------------------------------------------

il2cpp::Il2CppClass g_widgetClass = {
    "T.Widget", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppTypeInfo g_widgetInfo = {
    "T.Widget", &g_widgetClass, nullptr, nullptr, 0,
    nullptr, 0, nullptr, 0, nullptr, 0,
};

int main()
{
    using namespace il2cpp;

    il2cpp_typeinfo_register(&g_widgetInfo);

    // --- 1. assembly_get_type (typeinfo-wired, net48 GetType semantics) ---
    Il2CppObject* registered = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Widget"));
    Il2CppObject* hit = il2cpp_assembly_get_type(registered);
    Expect(hit != nullptr, "assembly_get_type(registered) -> non-null");
    Expect(hit == registered, "assembly_get_type(registered) -> the INPUT name string (Type-model identity)");
    Expect(ManagedEqualsAscii(hit, "T.Widget"), "assembly_get_type(registered) value == \"T.Widget\"");

    Il2CppObject* absent = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.DoesNotExist"));
    Expect(il2cpp_assembly_get_type(absent) == nullptr,
           "assembly_get_type(unregistered) -> null (not found, never throws)");

    // null name -> ArgumentNullException (net48 oracle: Assembly.GetType(null)
    // throws ArgumentNullException -- verified against v4.0.30319; the
    // delegation mirrors Type.GetType's null contract, so it throws too).
    Expect(ThrowsArgNull(il2cpp_assembly_get_type, nullptr),
           "assembly_get_type(null) -> ArgumentNullException");

    // Malformed names (over-long / non-ASCII) escape to "not found" -- the
    // typeinfo transcoding convention (typeinfo.cpp:199, the runtime's
    // 256-byte name buffer), matching the Type.GetType null-on-malformed
    // contract.
    char longName[300] = { 0 };
    for (int i = 0; i < 299; ++i)
    {
        longName[i] = 'A';
    }
    Il2CppObject* overLong = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(longName));
    Expect(il2cpp_assembly_get_type(overLong) == nullptr,
           "assembly_get_type(over-long name) -> null");

    const char16_t nonAscii[] = { 0x4E2D, 0x6587, 0 }; // "zhong wen" (BMP, non-ASCII)
    Il2CppObject* hanzi = reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nonAscii, 2));
    Expect(il2cpp_assembly_get_type(hanzi) == nullptr,
           "assembly_get_type(non-ASCII name) -> null");

    // The delegation is to the SAME registry as Type.GetType: identical input
    // produces the identical result through either entry point.
    Expect(il2cpp_type_get_type(registered) == il2cpp_assembly_get_type(registered),
           "assembly_get_type == type_get_type on a registered name (single registry)");

    // --- 2/3. AOT constants ---
    Expect(il2cpp_assembly_get_is_dynamic() == 0,
           "assembly_get_is_dynamic() == 0 (AOT static assembly)");
    Expect(il2cpp_assembly_get_is_collectible() == 0,
           "assembly_get_is_collectible() == 0 (AOT static assembly)");

    // --- 4. LOUD rejections (NotSupportedException, self-identifying) ---
    Expect(ThrowsNotSupported(il2cpp_icallb_reject_assemblyloadcontext),
           "reject_assemblyloadcontext -> NotSupportedException (pure Core, no net48)");
    Expect(ThrowsNotSupported(il2cpp_icallb_reject_metadataimport),
           "reject_metadataimport -> NotSupportedException (22-member metadata-import handle)");
    Expect(ThrowsNotSupported(il2cpp_icallb_reject_signature),
           "reject_signature -> NotSupportedException (signature-blob ABI)");
    Expect(ThrowsNotSupported(il2cpp_icallb_reject_stubhelpers),
           "reject_stubhelpers -> NotSupportedException (internal interop-stub family)");
    Expect(ThrowsNotSupported(il2cpp_icallb_reject_assembly_module_token),
           "reject_assembly_module_token -> NotSupportedException (no module/token model)");
    Expect(ThrowsNotSupported(il2cpp_icallb_reject_assembly_object),
           "reject_assembly_object -> NotSupportedException (no Assembly object)");

    // The rejection message self-identifies the family (contains the domain
    // name) -- the "clear message" requirement of the LOUD disposition. Every
    // reject names its family, and the metadataimport message names its FULL
    // 22-member surface (LOUD-definition conformance: icallb.h enumerates 22
    // members including GetDefaultValue, so the message must, too).
    Expect(MessageNames(il2cpp_icallb_reject_assemblyloadcontext, "AssemblyLoadContext"),
           "reject_assemblyloadcontext message names the domain (self-identifying)");
    Expect(MessageNames(il2cpp_icallb_reject_metadataimport, "MetadataImport"),
           "reject_metadataimport message names the domain (self-identifying)");
    Expect(MessageNames(il2cpp_icallb_reject_metadataimport, "GetDefaultValue"),
           "reject_metadataimport message names the 22-member surface incl GetDefaultValue (LOUD conformance)");
    Expect(MessageNames(il2cpp_icallb_reject_signature, "Signature"),
           "reject_signature message names the domain (self-identifying)");
    Expect(MessageNames(il2cpp_icallb_reject_stubhelpers, "StubHelpers"),
           "reject_stubhelpers message names the domain (self-identifying)");
    Expect(MessageNames(il2cpp_icallb_reject_assembly_module_token, "GetManifestModule"),
           "reject_assembly_module_token message names the domain (self-identifying)");
    Expect(MessageNames(il2cpp_icallb_reject_assembly_object, "Assembly"),
           "reject_assembly_object message names the domain (self-identifying)");

    if (g_failures == 0)
    {
        std::printf("all runtime icallb tests passed\n");
        return 0;
    }
    std::printf("%d icallb test failure(s)\n", g_failures);
    return 1;
}
