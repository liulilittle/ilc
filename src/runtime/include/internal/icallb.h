#pragma once
//------------------------------------------------------------------------------
// icallb.h -- Core-only icall residual surface (icall-inventory-core.md
// section 8.2, b23icallb work order).
//
// CLASSIFICATION (net48 alignment -- the three-way disposition rule):
//   1. net48-aligned surface  -> IMPLEMENTED (wired into the typeinfo
//      surface): RuntimeAssembly.GetType resolves a type name through the
//      SAME typeinfo registry Type.GetType uses (reflect_base.h) -- a
//      single-assembly AOT model has exactly one assembly, so the
//      assembly-scoped lookup IS the registry lookup, and the dynamic/
//      collectible probes are AOT constants (an AOT static assembly is never
//      either). net48 equivalents: Assembly.GetType / Assembly.IsDynamic
//      (net48 inventory row 334 RuntimeTypeHandle.GetAssembly and the
//      Assembly public surface).
//   2. pure-Core, no net48    -> LOUD rejections (System.NotSupported-
//      Exception, clear self-identifying message): AssemblyLoadContext
//      native surface (IsTracingEnabled + AssemblyNative 39) -- net48 has no
//      AssemblyLoadContext (AppDomain model); MetadataImport (22 members) --
//      an internal Core metadata-import handle with no net48 icall and no
//      ILC metadata-import model; Signature (3 members) -- internal
//      signature-blob ABI with no ILC equivalent; StubHelpers (6+2) -- net48
//      has the class but every member is an internal nonpublic interop-stub
//      helper never callable from user code (ILC's pinvoke/marshal path is
//      self-contained, pinvoke.cpp / marshalext*).
//   3. runtime object-model gaps -> LOUD: RuntimeAssembly GetManifestModule /
//      GetTokenInternal (no module / metadata-token object model) and the
//      Assembly-object family (GetExecutingAssembly / GetAssembly(Type) --
//      no Assembly object exists in ILC).
//
// The LOUD reject functions below are defense-in-depth: the PRIMARY gate is
// the transpiler MethodMap throw at resolve time (wiring patch report), and
// if a member is ever wired to one of these the runtime fails loudly with a
// self-identifying message instead of silently misbehaving.
//------------------------------------------------------------------------------

#include "il2cpp_object.h" // Il2CppObject (managed-string ABI)

#include <cstdint>

namespace il2cpp {

//------------------------------------------------------------------------------
// IMPLEMENTED -- net48-aligned, wired into the typeinfo surface.
//------------------------------------------------------------------------------

// Assembly.GetType(string) -> the type's full-name string (the simplified
// Type model: a Type VALUE is its full name, il2cpp_typeext.h) on a registry
// hit, nullptr on a miss / malformed name (net48 Assembly.GetType
// "not found -> null", no throw), and ArgumentNullException on a null name
// (net48 Assembly.GetType(null) throws -- the one throwing case of the
// query). Delegates to the existing typeinfo lookup (reflect_base.h
// il2cpp_type_get_type) -- single-assembly AOT: the registry IS this
// assembly's types, so the assembly-scoped query is the registry query
// (DRY, never re-implemented).
Il2CppObject* il2cpp_assembly_get_type(Il2CppObject* typeName);

// Assembly.IsDynamic -> 0 (an AOT static assembly is never dynamic; net48
// Assembly.IsDynamic is false for non-dynamic assemblies).
int32_t il2cpp_assembly_get_is_dynamic();

// Assembly.IsCollectible -> 0 (an AOT static assembly is not collectible;
// net48 has no collectible assemblies and this property is false on every
// real assembly).
int32_t il2cpp_assembly_get_is_collectible();

//------------------------------------------------------------------------------
// LOUD -- rejections (each throws System.NotSupportedException with a clear
// self-identifying message; see the classification above).
//------------------------------------------------------------------------------

// AssemblyLoadContext native surface (System.Runtime.Loader) --
// IsTracingEnabled (FCall gAssemblyLoadContextFuncs) + the AssemblyNative
// 39 QCall surface. Pure Core, no net48 equivalent.
void il2cpp_icallb_reject_assemblyloadcontext();

// MetadataImport (System.Reflection) -- the 22-member FCall group
// (gMetaDataImport): GetMetadataImport, GetDefaultValue, GetName,
// GetUserString, GetScopeProps, GetClassLayout, GetSignatureFromToken,
// GetNamespace, GetEventProps, GetFieldDefProps, GetPropertyProps,
// GetParentToken, GetParamDefProps, GetGenericParamProps, GetMemberRefProps,
// GetCustomAttributeProps, GetFieldOffset, GetSigOfFieldDef,
// GetSigOfMethodDef, GetFieldMarshal, GetPInvokeMap, IsValidToken.
void il2cpp_icallb_reject_metadataimport();

// Signature (System) -- the 3-member FCall group (gSignatureNative):
// GetParameterOffsetInternal, GetTypeParameterOffsetInternal,
// GetCallingConventionFromFunctionPointerAtOffsetInternal.
void il2cpp_icallb_reject_signature();

// StubHelpers (System.StubHelpers) -- the 6-member FCall group
// (gStubHelperFuncs): GetDelegateTarget, SetLastError, ClearLastError,
// CalcVaListSize, LogPinnedArgument (+ FEATURE_COMINTEROP
// GetComInterfaceFromMethodDesc, GetCOMIPFromRCW). Internal nonpublic
// interop-stub helpers; ILC's pinvoke/marshal path is self-contained.
void il2cpp_icallb_reject_stubhelpers();

// RuntimeAssembly.GetManifestModule / GetTokenInternal -- the runtime has no
// module object and no metadata-token model.
void il2cpp_icallb_reject_assembly_module_token();

// Assembly object family -- Assembly.GetExecutingAssembly /
// Assembly.GetAssembly(Type) / typeof(T).Assembly return an Assembly OBJECT,
// which ILC has no representation for (the Type model is a name string; the
// single assembly has no handle value).
void il2cpp_icallb_reject_assembly_object();

} // namespace il2cpp
