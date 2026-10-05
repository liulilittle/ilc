//------------------------------------------------------------------------------
// icallb.cpp -- Core-only icall residual surface (see icallb.h for the
// classification). Three-way disposition:
//   * IMPLEMENTED -- RuntimeAssembly.GetType wired into the existing typeinfo
//     surface (delegates to il2cpp_type_get_type, reflect_base.cpp:129 -- the
//     assembly-scoped query IS the registry query in a single-assembly AOT
//     model), plus the IsDynamic / IsCollectible AOT constants.
//   * LOUD -- AssemblyLoadContext / MetadataImport / Signature / StubHelpers
//     families and the no-model RuntimeAssembly members throw
//     System.NotSupportedException with clear self-identifying messages.
//     The transpiler MethodMap is the PRIMARY rejection gate (wiring patch
//     report); these runtime functions are defense-in-depth so a wired
//     member fails loudly, never silently.
//
// Message contract: every message below is a STATIC LITERAL (DotNetException
// carries the pointer without copying -- il2cpp_exception.h), so the text
// stays valid for the exception's lifetime.
//------------------------------------------------------------------------------

#include "icallb.h"

#include "reflect_base.h"          // il2cpp_type_get_type (delegated typeinfo lookup)
#include "il2cpp_exception_ext.h"  // il2cpp_throw_not_supported_exception

namespace il2cpp {

//------------------------------------------------------------------------------
// IMPLEMENTED surface (net48-aligned, typeinfo-wired).
//------------------------------------------------------------------------------

Il2CppObject* il2cpp_assembly_get_type(Il2CppObject* typeName)
{
    // Delegated: in the single-assembly AOT model Assembly.GetType(name) and
    // Type.GetType(name) resolve through the SAME typeinfo registry (the
    // registry IS this assembly's types). reflect_base's il2cpp_type_get_type
    // already implements the "registry hit -> the name string (Type model),
    // miss -> null, no throw" contract -- never re-implemented here.
    return il2cpp_type_get_type(typeName);
}

int32_t il2cpp_assembly_get_is_dynamic()
{
    return 0; // AOT static assembly: never dynamic (net48 Assembly.IsDynamic)
}

int32_t il2cpp_assembly_get_is_collectible()
{
    return 0; // AOT static assembly: never collectible
}

//------------------------------------------------------------------------------
// LOUD surface (pure-Core / no model) -- System.NotSupportedException.
//------------------------------------------------------------------------------

void il2cpp_icallb_reject_assemblyloadcontext()
{
    il2cpp_throw_not_supported_exception(
        "System.Runtime.Loader.AssemblyLoadContext is not wired: AssemblyLoadContext is a "
        ".NET Core-only load-context mechanism with no net48 equivalent (net48 uses the "
        "AppDomain model) and the ILC runtime has no load-context object -- the native "
        "surface (IsTracingEnabled + the AssemblyNative 39-entry QCall family) is LOUD-rejected");
}

void il2cpp_icallb_reject_metadataimport()
{
    il2cpp_throw_not_supported_exception(
        "System.Reflection.MetadataImport is not wired: the 22-member metadata-import "
        "handle surface (GetMetadataImport / GetDefaultValue / GetName / GetUserString / GetScopeProps / "
        "GetClassLayout / GetSignatureFromToken / GetNamespace / GetEventProps / "
        "GetFieldDefProps / GetPropertyProps / GetParentToken / GetParamDefProps / "
        "GetGenericParamProps / GetMemberRefProps / GetCustomAttributeProps / GetFieldOffset / "
        "GetSigOfFieldDef / GetSigOfMethodDef / GetFieldMarshal / GetPInvokeMap / IsValidToken "
        "and friends) is an internal Core metadata-import handle with no net48 icall and no "
        "ILC metadata-import model -- LOUD-rejected");
}

void il2cpp_icallb_reject_signature()
{
    il2cpp_throw_not_supported_exception(
        "System.Signature is not wired: the internal signature-blob ABI "
        "(GetParameterOffsetInternal / GetTypeParameterOffsetInternal / "
        "GetCallingConventionFromFunctionPointerAtOffsetInternal) is a Core-only signature-"
        "walking helper with no net48 icall and no ILC signature-blob model -- LOUD-rejected");
}

void il2cpp_icallb_reject_stubhelpers()
{
    il2cpp_throw_not_supported_exception(
        "System.StubHelpers.StubHelpers is not wired: every member (GetDelegateTarget / "
        "SetLastError / ClearLastError / CalcVaListSize / LogPinnedArgument / "
        "GetComInterfaceFromMethodDesc / GetCOMIPFromRCW) is an internal nonpublic "
        "interop-stub helper that user code can never call, and ILC's pinvoke/marshal path "
        "is self-contained (pinvoke.cpp / marshalext*) -- LOUD-rejected");
}

void il2cpp_icallb_reject_assembly_module_token()
{
    il2cpp_throw_not_supported_exception(
        "RuntimeAssembly.GetManifestModule / GetTokenInternal are not wired: the runtime has "
        "no module object and no metadata-token model (the M3 reflection surface is the "
        "typeinfo tables, not metadata tokens) -- LOUD-rejected");
}

void il2cpp_icallb_reject_assembly_object()
{
    il2cpp_throw_not_supported_exception(
        "The Assembly OBJECT family (Assembly.GetExecutingAssembly / Assembly.GetAssembly(Type) / "
        "typeof(T).Assembly) is not wired: ILC has no Assembly object representation (the Type "
        "model is a full-name string and the single AOT assembly has no handle value) -- "
        "LOUD-rejected");
}

} // namespace il2cpp
