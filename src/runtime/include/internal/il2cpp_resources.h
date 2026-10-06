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
// GetString(baseName, name): baseName is the ResourceManager's BASE NAME
// string (the ctor handle-gap stores it into the receiver local -- NOT the
// byte[] blob of the pre-R2 model), name the resource key. The runtime walks
// the INVARIANT culture chain "<baseName>.resources" (the R2 culture-aware
// fallback with the empty culture). Returns the value as a managed string, or
// null when the key is absent / the blob is malformed. String values are
// arbitrary length (net48 parity -- no fixed buffer cap).
//
//   CULTURE FALLBACK (R2 -- now wired): net48
//   ResourceManager.GetString(name, culture) walks the ResourceFallbackManager
//   chain -- [culture, its parents..., neutral culture, invariant culture] --
//   resolving "<BaseName>.<culture>.resources" from the manifest at each step.
//   The chain is implemented by il2cpp_resources_find_string_fallback (below);
//   il2cpp_resource_manager_get_string_culture is the OBJECT-level wrapper the
//   transpiled GetString(string, CultureInfo) calls (the culture argument is
//   the CultureInfo handle-gap name string). A Null-typed resource returns
//   null exactly like net48; a non-string value also returns null (net48
//   ResourceSet.GetString throws InvalidOperationException -- documented
//   defensive deviation).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_resource_manager_get_string(Il2CppObject* baseName, Il2CppObject* name);

// R2: ResourceManager.GetString(name, culture) -- the culture-aware overload
// over the SAME chain. baseName/key/cultureName are managed strings (the
// CultureInfo ctor handle-gap makes the culture argument a culture-name
// string; the invariant culture is the EMPTY name).
Il2CppObject* il2cpp_resource_manager_get_string_culture(Il2CppObject* baseName, Il2CppObject* name,
                                                         Il2CppObject* cultureName);

// R3: ResourceManager.GetObject/GetStream -- typed resource values.
//
// net48 ResourceTypeCode (resgen-verified, 2026-10-07 -- the real .resources
// data-section type codes, NOT the small enum indexes some ports assume):
//   1 = String, 32 (0x20) = ByteArray, 33 (0x21) = Stream, 0 = Null; other
//   values are primitives / user types. Payload framing: String(1) is
//   7bit-length + bytes; ByteArray(32)/Stream(33) are int32 little-endian
//   length + bytes (ResourceWriter BinaryWriter.Write(int); the 7-bit length
//   of the dynamic-resource string convention does NOT apply to them).
//
// GetObject(baseName, name) walks the culture chain and returns:
//   String(1)    -> the value as a managed string
//   ByteArray(32)-> the value as a managed byte[] (Il2CppArray)
//   Stream(33)   -> a read-only MemoryStream object over the payload
//   Null/other   -> null (never throws)
// GetStream(baseName, name) returns the MemoryStream object for Stream(33)
// only; any other type (incl. ByteArray) is null under the defensive contract
// (net48 ResourceManager.GetStream throws InvalidOperationException for a
// non-stream resource -- documented defensive deviation). A missing key is
// null on both surfaces. The *_culture overloads take the culture-name string
// and walk the same net48 fallback chain.
Il2CppObject* il2cpp_resource_manager_get_object(Il2CppObject* baseName, Il2CppObject* name);
Il2CppObject* il2cpp_resource_manager_get_object_culture(Il2CppObject* baseName, Il2CppObject* name,
                                                         Il2CppObject* cultureName);
Il2CppObject* il2cpp_resource_manager_get_stream(Il2CppObject* baseName, Il2CppObject* name);
Il2CppObject* il2cpp_resource_manager_get_stream_culture(Il2CppObject* baseName, Il2CppObject* name,
                                                         Il2CppObject* cultureName);

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
// RM-CTOR (resmgmt/resops, b23 + R2): ResourceManager..ctor(string baseName,
// Assembly assembly) -- R2: the receiver stores the BASE NAME as a managed
// string (the assembled "<BaseName>.<culture>.resources" names are resolved by
// the culture chain at GetString/GetObject/GetStream time). The single-assembly
// AOT model pins the invariant culture, so the chain terminator IS the registry
// entry "<baseName>.resources"; the Assembly argument (assemblies) is DROPPED
// (the registry IS the manifest -- D1 single writer). Returns null when the
// base name is absent / over-long / non-ASCII (a ctor receiver with no usable
// base name makes every lookup a null result -- net48 defensive contract).
//
// The transpiler stores the result INTO the receiver local (ResultIsReceiver
// handle-gap -- the XmlDocument.LoadXml precedent), so every subsequent
// GetString/GetObject/GetStream(receiver, key[, culture]) passes the base name
// string as its first slot.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_resource_manager_ctor_resolve(Il2CppObject* baseName, Il2CppObject* assembly);

// R2: new CultureInfo(string) handle-gap -- the transpiled ctor stores the
// CULTURE-NAME string into the receiver local (the CultureInfo object IS its
// name, the MethodMap.CultureInfo.cs get_InvariantCulture convention). The
// culture-aware GetString/GetObject/GetStream surface consumes the receiver as
// the cultureName slot. null -> null (defensive).
Il2CppObject* il2cpp_resource_manager_culture_from_name(Il2CppObject* name);

} // namespace il2cpp
