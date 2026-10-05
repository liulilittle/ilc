#pragma once
//------------------------------------------------------------------------------
// il2cpp_resources.h -- RES-1 (iron rule 18): embedded manifest resources.
//
// Surface (net48-aligned, minimal -- resource-embedding domain):
//   * Assembly.GetManifestResourceStream(name) -> Stream  (raw payload as a
//     read-only MemoryStream OBJECT -- batch 90; net48: the manifest stream
//     IS a Stream)
//   * Assembly.GetManifestResourceNames()       -> string[]
//   * ResourceManager.GetString(name) minimal -- .resources binary parsing
//     (Magic 0xBEEFCACE + name/value tables, ResourceReader core port).
//
// Resource data flow (the transpile chain):
//   MetadataLoader.LoadManifestResources reads the ManifestResource table and
//   the transpiler emits include/internal/resource-pool.h (T3 g_strPool
//   pattern): one static byte array per payload + a g_resPool[] entry table.
//   il2cpp_resource_pool_fill() (emitted in that header) hands the table to
//   il2cpp_resources_register -- the single writer into the runtime registry
//   (D1). The Assembly surface then queries THAT registry.
//
//   The CodeGenerator-side wiring (generated.h #include + the fill call in
//   il2cpp_runtime_init + the MethodMap entries for
//   Assembly.GetManifestResourceStream / GetManifestResourceNames /
//   ResourceManager.GetString) is documented in the b23res wiring patch list.
//
// Byte[]/string[] returns are managed objects (il2cpp_array_new / 
// il2cpp_string_new_from_utf8). A missing resource returns null (never
// throws); a corrupted .resources blob returns null for the key (defensive,
// BadImageFormat semantics are LOUD-only -- documented).
//------------------------------------------------------------------------------

#include "il2cpp_runtime.h"

#include <cstdint>

// System.Resources.ResourceManager / System.Globalization.CultureInfo runtime
// descriptors (GLOBAL scope -- the transpiler emits &System_Resources_
// ResourceManager_class / &System_Globalization_CultureInfo_class for newobj/
// isinst/castclass; the same contract as System_Version_class / System_Uri_class
// in versionuri.cpp). Defined in metadata.cpp, header-only (handle-gap: the RM
// ctor stores the resolved blob INTO the receiver local; a CultureInfo object
// is only ever the DROPPED culture argument of GetString(name, culture)).
extern il2cpp::Il2CppClass System_Resources_ResourceManager_class;
extern il2cpp::Il2CppClass System_Globalization_CultureInfo_class;

namespace il2cpp {

// One embedded resource: logical name (UTF-8) + raw payload bytes. The table
// array lives in the generated resource-pool.h; registration copies it into
// the runtime registry.
struct ManifestResourceEntry
{
    const char* name;
    const uint8_t* data;
    int32_t length;
};

//------------------------------------------------------------------------------
// Registry (single writer, D1): the generated resource-pool.h fill hands the
// pool table here. Re-registration replaces the previous table (idempotent --
// the single-assembly AOT model has one manifest).
//------------------------------------------------------------------------------
void il2cpp_resources_register(const ManifestResourceEntry* entries, int32_t count);

// Assembly.GetManifestResourceStream(name): the resource's raw payload as a
// read-only MemoryStream OBJECT (batch 90 -- net48: the manifest stream IS a
// Stream; the resops e2e does s.Read(buf, 0, n) on the result). Unknown name
// -> null.
Il2CppObject* il2cpp_assembly_get_manifest_resource_stream(Il2CppObject* name);

// The resource's raw payload as a managed byte[] (Il2CppArray, elemSize 1).
// Unknown name -> null. The ResourceManager ctor path needs the raw BLOB (not
// the Stream wrapper above -- the .resources parser reads bytes); this is the
// blob half of GetManifestResourceStream.
Il2CppObject* il2cpp_resources_get_blob(Il2CppObject* name);

// Assembly.GetManifestResourceNames(): the registered resource names as a
// managed string[] (Il2CppArray of Il2CppObject*), in table order. Empty pool
// -> a zero-length array (never null).
Il2CppObject* il2cpp_assembly_get_manifest_resource_names();

//------------------------------------------------------------------------------
// ResourceManager minimal (ResourceReader core port -- the .resources binary
// format of the net48 fixture, verified against reference/netcore
// ResourceReader.cs + a .NET 8 probe):
//
//   header: magic i4 0xBEEFCACE | resMgrHeaderVersion i4 | numBytesToSkip i4
//           (version > 1: skip numBytesToSkip; version == 1: read readerType
//            string + skip resourceSet string -- both 7bit-len + bytes)
//   i4 file version (V2 only -- see below) | i4 numResources | i4 numTypes
//   skip numTypes type-name strings (7bit-len + bytes)
//   align pos to 8 bytes
//   i4 nameHashes[numResources] | i4 namePositions[numResources]
//   i4 dataSectionOffset (must be >= the name-section start; net48
//       BadImageFormatException parity) | (name section begins here)
//   name entry i: nameSectionOffset + namePositions[i] ->
//       7bit byteLen + byteLen UTF-16 (Unicode) name bytes + i4 dataPos
//       (dataPos must be a non-negative offset < blobLen - dataSectionOffset)
//   data entry: dataSectionOffset + dataPos -> 7bit typeCode; String(1) =
//       7bit byteLen + UTF-8 value bytes (arbitrary length -- no cap)
//
//   File-format VERSION: net48 ResourceReader accepts 1 and 2, but a V1-FILE
//   data entry begins with a TYPE-TABLE index (not a ResourceTypeCode), so V1
//   data cannot be parsed by this port. The toolchain emits V2 only (.resources
//   from resgen / GenerateResource since .NET 2.0); a V1 blob is REJECTED as
//   unsupported (a miss), never silently misparsed.
//
// GetString(resourcesBlob, name): resourcesBlob is the byte[] payload of the
// "<BaseName>.resources" manifest resource (the ResourceManager wiring
// resolves it), name the resource key. Returns the value as a managed string,
// or null when the key is absent / the blob is malformed. String values are
// arbitrary length (net48 parity).
//
//   CULTURE FALLBACK (assessed, documented-not-done): net48
//   ResourceManager.GetString(name, culture) walks the ResourceFallbackManager
//   chain -- [culture, its parents..., neutral culture, invariant culture] --
//   resolving "<BaseName>.<culture>.resources" from the manifest at each step.
//   This surface takes ONE pre-resolved blob and does a single ordinal lookup;
//   the culture chain must be resolved by the wiring layer (which culture's
//   blob to pass). A Null-typed (ResourceTypeCode.Null) resource returns null
//   exactly like net48 (treated as a miss here -- observable behavior is
//   identical); a non-string value also returns null (net48 ResourceSet throws
//   InvalidOperationException -- documented defensive deviation).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_resource_manager_get_string(Il2CppObject* resourcesBlob, Il2CppObject* name);

//------------------------------------------------------------------------------
// Low-level parser (exposed for the runtime test + the ResourceManager
// wrapper): look up 'name' (ASCII/UTF-8) in a raw .resources blob and copy
// the string value into 'outBuf' (NUL-terminated, outCap max incl. terminator).
// Returns true on a hit, false on a miss / malformed blob. The name is
// compared against the UTF-16 name section case-sensitively (ordinal -- .NET
// ResourceManager.GetString key lookup is ordinal; the manifest stream name
// lookup is the case-insensitive step, handled by the stream resolver).
//------------------------------------------------------------------------------
bool il2cpp_resources_parse_get_string(const uint8_t* blob, int32_t blobLen,
                                       const char* name, char* outBuf, int32_t outCap);

//------------------------------------------------------------------------------
// RM-CULT (net48 ResourceManager culture fallback chain): resolve a string
// resource across the CULTURE FALLBACK CHAIN over the registry.
//
// net48 GetString(name, culture) semantics (audited 2026-08-09 against
// reference/netcore System.Resources.ResourceManager + ResourceFallbackManager
// + ManifestBasedResourceGroveler):
//   * the fallback chain is [culture, culture.Parent..., neutral culture,
//     invariant culture]; with no NeutralResourcesLanguageAttribute the
//     neutral culture IS the invariant culture (ResourceFallbackManager).
//   * each step resolves GetResourceFileName(culture): "<baseName>.resources"
//     for the invariant culture, else "<baseName>.<culture>.resources"
//     (ResourceManager.GetResourceFileName).
//   * the single-assembly AOT model has NO satellite assemblies -- the
//     culture-suffixed manifest resource plays the satellite role; a registry
//     miss for a culture's blob equals net48's null satellite.
//   * the FIRST chain step whose blob CONTAINS the key wins (net48 GetString
//     returns the value from the first ResourceSet that has the key; a key
//     absent from a found set continues to the parent).
//   * provider fault recovery: a culture whose blob is MISSING or MALFORMED
//     is SKIPPED -- the chain continues to the parent (net48 groveler returns
//     null for a missing stream; a corrupt blob collapses to a miss under the
//     defensive contract). Every step misses -> false (never throws).
//
// DIVERGENCE (documented): the parent chain is generated by TRUNCATION (strip
// the last '-' segment -- en-US -> en -> invariant). The full .NET CLDR parent
// table (e.g. zh-CN -> zh-Hans -> zh) is a long-term item (roadmap M8 CLDR);
// truncation matches the common specific -> neutral -> invariant cases.
//
// ASCII only (baseName/cultureName/key): a non-ASCII name can never match a
// registered entry and is a miss. Same outBuf/outCap contract as
// il2cpp_resources_parse_get_string.
//------------------------------------------------------------------------------
bool il2cpp_resources_find_string_fallback(const char* baseName, const char* cultureName,
                                           const char* key, char* outBuf, int32_t outCap);

//------------------------------------------------------------------------------
// RM-CTOR (resmgmt/resops, b23): ResourceManager..ctor(string baseName,
// Assembly assembly) -- resolve the INVARIANT "<BaseName>.resources" blob from
// the registry and return it as a managed byte[] (the GetString receiver
// slot). The single-assembly AOT model pins the invariant culture, so the
// ctor's base name maps directly to the registry entry; the Assembly argument
// (assemblies) is DROPPED (the registry IS the manifest -- D1 single writer).
// Returns null when the base name is absent / over-long / non-ASCII (a ctor
// receiver with no blob makes every GetString a null result -- net48
// defensive contract).
//
// The transpiler stores the result INTO the receiver local (ResultIsReceiver
// handle-gap -- the XmlDocument.LoadXml precedent), so every subsequent
// GetString(receiver, key) passes the blob as its resourcesBlob slot.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_resource_manager_ctor_resolve(Il2CppObject* baseName, Il2CppObject* assembly);

} // namespace il2cpp
