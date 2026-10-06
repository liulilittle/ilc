//------------------------------------------------------------------------------
// resources.cpp -- RES-1 (iron rule 18): embedded manifest resources.
//
// Implements the runtime half of the resource chain:
//   * the resource registry (single writer: il2cpp_resources_register, called
//     by the generated resource-pool.h fill);
//   * Assembly.GetManifestResourceStream(name) -> byte[] (raw payload);
//   * Assembly.GetManifestResourceNames() -> string[];
//   * ResourceManager.GetString / GetString(name, culture) (R2) -- the
//     .resources binary parsing (Magic 0xBEEFCACE + name/value tables) over
//     the net48 CULTURE FALLBACK CHAIN, receiver = the base name string;
//   * ResourceManager.GetObject / GetStream (R3) -- typed values (String=1,
//     ByteArray=32, Stream=33 -- resgen-verified net48 ResourceTypeCode),
//     including the *_culture chain variants.
//
// Cross-platform: byte arrays are allocated through the managed allocator
// (il2cpp_array_new), strings through il2cpp_string_new_from_utf8, streams
// through il2cpp_stream_memory_from_bytes -- no platform code. All lookups are
// ordinal; a miss returns null, never throws.
//
// Parser net48 parity (audited against reference/netcore ResourceReader.cs):
//   * V2-only file format -- a V1-FILE data entry begins with a type-TABLE
//     index, not a ResourceTypeCode, so V1 data cannot be parsed here; the
//     toolchain emits V2 (.resources from resgen since .NET 2.0), so a V1
//     blob is rejected as unsupported (a miss).
//   * every positional value is bounds-checked with int64 math (no int32
//     overflow / negative-position underflow can reach the byte reads);
//   * dataPos (data-section entry offset) is validated against
//     [0, blobLen - dataSectionOffset) -- net48 FormatException parity
//     (defensive contract collapses that to a miss);
//   * string values are arbitrary length (no fixed buffer cap).
//------------------------------------------------------------------------------

#include "il2cpp_resources.h"

#include "il2cpp_exception_ext.h"
#include "il2cpp_streamio.h"

#include <cstdint>
#include <cstring>
#include <vector>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Registry (D1 single writer -- see the header). The table is a shallow copy
// of the generated g_resPool[] (entries point into the generated static
// arrays; the AOT model keeps those alive for the process lifetime).
//------------------------------------------------------------------------------
const ManifestResourceEntry* g_resEntries = nullptr;
int32_t g_resCount = 0;

constexpr int32_t kMaxNameLen = 256;

// Convert a managed string to an ASCII/UTF-8 C buffer (ordinal compare
// target). Mirrors reflect_base's AsciiFromManagedString contract: ASCII only,
// null / over-long / embedded-NUL / non-ASCII -> false. Resource names are
// ASCII by project convention (generated code is pure ASCII). An embedded NUL
// is rejected (rather than truncating the key and risking a false-positive
// hit on a shorter name -- net48 has no such name, so the lookup is a miss).
bool NameFromManagedString(Il2CppObject* name, char (&buf)[kMaxNameLen])
{
    if (name == nullptr)
    {
        return false;
    }
    auto s = reinterpret_cast<Il2CppString*>(name);
    if (s->length < 0 || s->length >= kMaxNameLen)
    {
        return false;
    }
    for (int32_t i = 0; i < s->length; i++)
    {
        char16_t u = s->chars[i];
        if (u == 0 || u > 0x7F)
        {
            return false;
        }
        buf[i] = static_cast<char>(u);
    }
    buf[s->length] = '\0';
    return true;
}

const ManifestResourceEntry* FindResource(const char* name)
{
    for (int32_t i = 0; i < g_resCount; i++)
    {
        if (std::strcmp(g_resEntries[i].name, name) == 0)
        {
            return &g_resEntries[i];
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// .resources binary reading helpers (little-endian, bounds-checked).
//
// Positions/lengths are int64 so every additive bounds check is overflow-free:
// a corrupt blob can never wrap a position negative (underflow) or past
// INT32_MAX and reach a byte read at an out-of-range index.
//------------------------------------------------------------------------------
struct BlobReader
{
    const uint8_t* p;
    int64_t len;
    int64_t pos;

    // True when 'count' more bytes are readable at the current position.
    bool HasBytes(int64_t count) const
    {
        return count >= 0 && pos + count <= len;
    }

    // Advance by 'count' bytes (must have been validated with HasBytes).
    void Skip(int64_t count)
    {
        pos += count;
    }

    bool ReadU8(uint8_t& out)
    {
        if (!HasBytes(1))
        {
            return false;
        }
        out = p[static_cast<size_t>(pos)];
        pos += 1;
        return true;
    }

    bool ReadI4(int32_t& out)
    {
        if (!HasBytes(4))
        {
            return false;
        }
        out = static_cast<int32_t>(p[static_cast<size_t>(pos)])
            | (static_cast<int32_t>(p[static_cast<size_t>(pos + 1)]) << 8)
            | (static_cast<int32_t>(p[static_cast<size_t>(pos + 2)]) << 16)
            | (static_cast<int32_t>(p[static_cast<size_t>(pos + 3)]) << 24);
        pos += 4;
        return true;
    }

    // Read an int32 at an absolute offset (used for the namePositions table).
    bool PeekI4(int64_t offset, int32_t& out) const
    {
        if (offset < 0 || offset + 4 > len)
        {
            return false;
        }
        out = static_cast<int32_t>(p[static_cast<size_t>(offset)])
            | (static_cast<int32_t>(p[static_cast<size_t>(offset + 1)]) << 8)
            | (static_cast<int32_t>(p[static_cast<size_t>(offset + 2)]) << 16)
            | (static_cast<int32_t>(p[static_cast<size_t>(offset + 3)]) << 24);
        return true;
    }

    // Read7BitEncodedInt (ECMA-style 7-bit length prefix). Accumulated in
    // int64 (no UB on high shifts); values above INT32_MAX are rejected --
    // every consumer bounds-checks the length against the blob length, which
    // fits int32.
    bool Read7Bit(int32_t& out)
    {
        int64_t result = 0;
        int32_t shift = 0;
        while (shift <= 35)
        {
            uint8_t b;
            if (!ReadU8(b))
            {
                return false;
            }
            result |= static_cast<int64_t>(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
            {
                if (result > INT32_MAX)
                {
                    return false;
                }
                out = static_cast<int32_t>(result);
                return true;
            }
        }
        return false;
    }
};

// Locate the value for 'name' directly inside the blob (no copy). On a name
// match, sets *outTypeCode (the net48 ResourceTypeCode the entry carries),
// *valuePtr/*valueLen over the raw payload for the payload-bearing codes
// (String=1, ByteArray=32, Stream=33 -- all three share the 7bit-len + bytes
// shape); Null(0) and other (primitive/user) types set an empty value. Every
// miss / malformed case returns false.
bool FindResourceValue(const uint8_t* blob, int32_t blobLen, const char* name,
                       int32_t* outTypeCode, const uint8_t** valuePtr, int32_t* valueLen)
{
    BlobReader r{ blob, blobLen, 0 };

    // Magic: 0xBEEFCACE (little-endian bytes CE CA EF BE).
    int32_t magic;
    if (!r.ReadI4(magic) || magic != static_cast<int32_t>(0xBEEFCACE))
    {
        return false;
    }

    int32_t resMgrHeaderVersion;
    int32_t numBytesToSkip;
    if (!r.ReadI4(resMgrHeaderVersion) || !r.ReadI4(numBytesToSkip) ||
        resMgrHeaderVersion < 0 || numBytesToSkip < 0)
    {
        return false;
    }
    if (resMgrHeaderVersion > 1)
    {
        // V2+ ResourceManager header: the whole header (reader-type and
        // resource-set strings) is numBytesToSkip bytes long -- jump past it.
        if (!r.HasBytes(numBytesToSkip))
        {
            return false;
        }
        r.Skip(numBytesToSkip);
    }
    else
    {
        // V1 ResourceManager header: read the reader-type string and skip the
        // resource-set string (both 7bit-len + bytes).
        for (int32_t i = 0; i < 2; i++)
        {
            int32_t sLen;
            if (!r.Read7Bit(sLen) || sLen < 0 || !r.HasBytes(sLen))
            {
                return false;
            }
            r.Skip(sLen);
        }
    }

    // File format version. net48 accepts 1 and 2, but a V1-FILE data entry
    // begins with a type-TABLE index (not a ResourceTypeCode), so the V1 data
    // section cannot be parsed by this port. The toolchain only ever emits V2;
    // a V1 blob is rejected as unsupported rather than silently misparsed.
    int32_t version;
    if (!r.ReadI4(version) || version != 2)
    {
        return false;
    }

    int32_t numResources;
    int32_t numTypes;
    if (!r.ReadI4(numResources) || numResources < 0 ||
        !r.ReadI4(numTypes) || numTypes < 0)
    {
        return false;
    }

    // Skip the numTypes type-name strings (not needed for ordinal lookup).
    for (int32_t i = 0; i < numTypes; i++)
    {
        int32_t tLen;
        if (!r.Read7Bit(tLen) || tLen < 0 || !r.HasBytes(tLen))
        {
            return false;
        }
        r.Skip(tLen);
    }

    // Align the reader position to 8 bytes (the name hashes array alignment --
    // identical to ResourceReader._ReadResources: pad to a multiple of 8).
    const int64_t alignBytes = r.pos & 7;
    if (alignBytes != 0)
    {
        if (!r.HasBytes(8 - alignBytes))
        {
            return false;
        }
        r.Skip(8 - alignBytes);
    }

    // nameHashes[numResources] (not needed for ordinal lookup -- skipped).
    if (!r.HasBytes(static_cast<int64_t>(numResources) * 4))
    {
        return false;
    }
    r.Skip(static_cast<int64_t>(numResources) * 4);

    // namePositions[numResources].
    const int64_t namePosBase = r.pos;
    if (!r.HasBytes(static_cast<int64_t>(numResources) * 4))
    {
        return false;
    }
    r.Skip(static_cast<int64_t>(numResources) * 4);

    int32_t dataSectionOffset;
    if (!r.ReadI4(dataSectionOffset) || dataSectionOffset < 0 || dataSectionOffset > blobLen)
    {
        return false;
    }
    const int64_t nameSectionOffset = r.pos;
    if (dataSectionOffset < nameSectionOffset)
    {
        return false; // corrupt: data section must start at/after the name section
    }

    // Walk the name entries: nameSectionOffset + namePositions[i].
    for (int32_t i = 0; i < numResources; i++)
    {
        int32_t namePos;
        if (!r.PeekI4(namePosBase + static_cast<int64_t>(4) * i, namePos))
        {
            return false;
        }
        if (namePos < 0 || static_cast<int64_t>(namePos) > dataSectionOffset - nameSectionOffset)
        {
            continue;
        }

        BlobReader nr{ blob, blobLen, nameSectionOffset + namePos };
        int32_t byteLen;
        if (!nr.Read7Bit(byteLen) || byteLen < 0 || !nr.HasBytes(static_cast<int64_t>(byteLen) + 4))
        {
            continue;
        }
        // Name bytes are UTF-16 (Unicode); compare ordinally against the
        // ASCII key (name bytes are 2 per char; ASCII keys have high byte 0).
        // Exact-length ordinal compare == FastResourceComparer.CompareOrdinal.
        const uint8_t* nameBytes = nr.p + static_cast<size_t>(nr.pos);
        const int32_t unitCount = byteLen / 2;
        const char* key = name;
        bool match = true;
        for (int32_t u = 0; u < unitCount; u++)
        {
            char16_t unit = static_cast<char16_t>(nameBytes[2 * u] | (nameBytes[2 * u + 1] << 8));
            if (unit > 0x7F || key[u] == '\0' || static_cast<char>(unit) != key[u])
            {
                match = false;
                break;
            }
        }
        if (!match || key[unitCount] != '\0')
        {
            continue;
        }
        nr.Skip(byteLen);
        int32_t dataPos;
        if (!nr.ReadI4(dataPos))
        {
            continue;
        }
        // Data entry offset is relative to the data section. net48 parity
        // (FormatException on violation): dataPos must be a non-negative
        // offset strictly inside the data section span (blobLen -
        // dataSectionOffset). Without this, dataSectionOffset + dataPos could
        // read before the blob (negative dataPos) or overflow int32.
        if (dataPos < 0 || dataPos >= blobLen - dataSectionOffset)
        {
            continue;
        }
        // Data entry: dataSectionOffset + dataPos.
        BlobReader dr{ blob, blobLen, static_cast<int64_t>(dataSectionOffset) + dataPos };
        int32_t typeCode;
        if (!dr.Read7Bit(typeCode))
        {
            continue;
        }
        *outTypeCode = typeCode;
        if (typeCode == 1 || typeCode == 32 || typeCode == 33)
        {
            // String(1): 7bit length + UTF-8 bytes. ByteArray(32)/Stream(33):
            // int32 little-endian length + raw bytes (ResourceWriter writes
            // BinaryWriter.Write(int) for those two -- resgen-verified).
            int32_t vLen;
            bool lenOk = (typeCode == 1)
                ? (dr.Read7Bit(vLen) && vLen >= 0 && dr.HasBytes(vLen))
                : (dr.ReadI4(vLen) && vLen >= 0 && dr.HasBytes(vLen));
            if (!lenOk)
            {
                return false;
            }
            *valuePtr = dr.p + static_cast<size_t>(dr.pos);
            *valueLen = vLen;
        }
        else
        {
            // Null(0) / primitives / user types: no payload to surface.
            *valuePtr = nullptr;
            *valueLen = 0;
        }
        return true;
    }
    return false;
}

// String-only filter over FindResourceValue (the net48 ResourceManager
// GetString contract: String(1) values return the value; every other type
// collapses to a miss -- the error path surfaces as null under the defensive
// contract).
bool FindStringValue(const uint8_t* blob, int32_t blobLen, const char* name,
                     const uint8_t** valuePtr, int32_t* valueLen)
{
    int32_t typeCode;
    if (!FindResourceValue(blob, blobLen, name, &typeCode, valuePtr, valueLen))
    {
        return false;
    }
    return typeCode == 1;
}

} // namespace

//------------------------------------------------------------------------------
// Registry.
//------------------------------------------------------------------------------
void il2cpp_resources_register(const ManifestResourceEntry* entries, int32_t count)
{
    g_resEntries = entries;
    // A null table means "no resources" regardless of the count (defensive:
    // FindResource/names dereference g_resEntries).
    g_resCount = (entries == nullptr) ? 0 : (count < 0 ? 0 : count);
}

//------------------------------------------------------------------------------
// il2cpp_resources_get_blob -- the resource's raw payload as a managed byte[]
// (Il2CppArray, elemSize 1). Unknown name -> null (never throws). The
// ResourceManager ctor path (resourcemanager.cpp) resolves "<BaseName>.
// resources" to THIS blob (the .resources parser reads bytes); the public
// GetManifestResourceStream returns a Stream wrapper over the same payload.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_resources_get_blob(Il2CppObject* name)
{
    char nbuf[kMaxNameLen];
    if (!NameFromManagedString(name, nbuf))
    {
        return nullptr;
    }
    const ManifestResourceEntry* e = FindResource(nbuf);
    if (e == nullptr || e->length < 0)
    {
        return nullptr;
    }
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, e->length);
    if (e->length > 0)
    {
        std::memcpy(il2cpp_array_get_elem_ptr(arr, 0), e->data, static_cast<size_t>(e->length));
    }
    return reinterpret_cast<Il2CppObject*>(arr);
}

//------------------------------------------------------------------------------
// Assembly.GetManifestResourceStream(name) -> a read-only MemoryStream over
// the raw payload (net48: the manifest stream IS a Stream -- the resops e2e
// does s.Read(buf, 0, n) on the result). Unknown name -> null (never throws).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_assembly_get_manifest_resource_stream(Il2CppObject* name)
{
    char nbuf[kMaxNameLen];
    if (!NameFromManagedString(name, nbuf))
    {
        return nullptr;
    }
    const ManifestResourceEntry* e = FindResource(nbuf);
    if (e == nullptr || e->length < 0)
    {
        return nullptr;
    }
    return il2cpp::runtime::il2cpp_stream_memory_from_bytes(e->data, e->length);
}

//------------------------------------------------------------------------------
// Assembly.GetManifestResourceNames() -> string[] (managed string array).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_assembly_get_manifest_resource_names()
{
    Il2CppArray* arr = il2cpp_array_new(nullptr, sizeof(void*), true, g_resCount);
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(arr->data);
    for (int32_t i = 0; i < g_resCount; i++)
    {
        slots[i] = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(g_resEntries[i].name));
    }
    return reinterpret_cast<Il2CppObject*>(arr);
}

//------------------------------------------------------------------------------
// .resources parser (ResourceReader core port). Look up 'name' in a raw
// .resources blob and copy the string value to outBuf (NUL-terminated).
//------------------------------------------------------------------------------
bool il2cpp_resources_parse_get_string(const uint8_t* blob, int32_t blobLen,
                                       const char* name, char* outBuf, int32_t outCap)
{
    if (blob == nullptr || blobLen < 0 || name == nullptr || outBuf == nullptr || outCap <= 0)
    {
        return false;
    }
    const uint8_t* value = nullptr;
    int32_t valueLen = 0;
    if (!FindStringValue(blob, blobLen, name, &value, &valueLen))
    {
        return false;
    }
    if (valueLen >= outCap)
    {
        return false; // buffer too small: treated as a miss
    }
    std::memcpy(outBuf, value, static_cast<size_t>(valueLen));
    outBuf[valueLen] = '\0';
    return true;
}

//------------------------------------------------------------------------------
// R2/R3: ResourceManager typed-value surface.
//
// All five entry points take the BASE NAME string as the receiver slot (the
// ctor handle-gap stores it), the resource KEY, and (for the *_culture family)
// the CULTURE-NAME string (the CultureInfo handle-gap; the invariant culture
// is the empty name). The chain walk resolves "<baseName>.<culture>.resources"
// per culture step; the payload is then shaped by the entry's net48
// ResourceTypeCode:
//   1  = String    -> managed string
//   32 = ByteArray -> managed byte[] (Il2CppArray)
//   33 = Stream    -> read-only MemoryStream object
//   0  / other     -> null (defensive)
//------------------------------------------------------------------------------

namespace {

// Forward declaration -- FindChainValue (the typed culture-chain walk) is
// defined in the RM-CULT section below, together with the chain helpers.
bool FindChainValue(const char* baseName, const char* cultureName, const char* key,
                    int32_t* outTypeCode, const uint8_t** valuePtr, int32_t* valueLen);

// Shared arg guards + typed chain walk. Returns true and fills *outType /
// *valuePtr / *valueLen when a chain step carries the key (any type).
bool ResourceManagerResolve(Il2CppObject* baseName, Il2CppObject* name,
                            Il2CppObject* cultureNameObj,
                            int32_t* outType, const uint8_t** valuePtr, int32_t* valueLen)
{
    char baseBuf[kMaxNameLen];
    if (!NameFromManagedString(baseName, baseBuf))
    {
        return false;
    }
    char keyBuf[kMaxNameLen];
    if (!NameFromManagedString(name, keyBuf))
    {
        return false;
    }
    char cultBuf[kMaxNameLen];
    const char* culture = "";
    if (cultureNameObj != nullptr)
    {
        if (!NameFromManagedString(cultureNameObj, cultBuf))
        {
            return false;
        }
        culture = cultBuf;
    }
    return FindChainValue(baseBuf, culture, keyBuf, outType, valuePtr, valueLen);
}

// Managed string from a raw UTF-8 payload (not NUL-terminated in the blob).
Il2CppObject* Utf8PayloadToString(const uint8_t* value, int32_t valueLen)
{
    std::vector<char> utf8(static_cast<size_t>(valueLen) + 1u);
    if (valueLen > 0)
    {
        std::memcpy(utf8.data(), value, static_cast<size_t>(valueLen));
    }
    utf8[valueLen] = '\0';
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(utf8.data()));
}

} // namespace

// ResourceManager.GetString(baseName, name): the invariant chain (the RM
// receiver is the base name, not a blob). Values are arbitrary length.
Il2CppObject* il2cpp_resource_manager_get_string(Il2CppObject* baseName, Il2CppObject* name)
{
    int32_t typeCode;
    const uint8_t* value = nullptr;
    int32_t valueLen = 0;
    if (!ResourceManagerResolve(baseName, name, nullptr, &typeCode, &value, &valueLen))
    {
        return nullptr;
    }
    if (typeCode != 1)
    {
        return nullptr; // non-string value: GetString collapses to null (defensive)
    }
    return Utf8PayloadToString(value, valueLen);
}

// ResourceManager.GetString(baseName, name, cultureName): the 2-arg culture
// overload (R2) -- GetString(string, CultureInfo), culture arg = the
// CultureInfo handle-gap name string. Same chain as
// il2cpp_resources_find_string_fallback, byte-identical string results.
Il2CppObject* il2cpp_resource_manager_get_string_culture(Il2CppObject* baseName, Il2CppObject* name,
                                                         Il2CppObject* cultureName)
{
    int32_t typeCode;
    const uint8_t* value = nullptr;
    int32_t valueLen = 0;
    if (!ResourceManagerResolve(baseName, name, cultureName, &typeCode, &value, &valueLen))
    {
        return nullptr;
    }
    if (typeCode != 1)
    {
        return nullptr;
    }
    return Utf8PayloadToString(value, valueLen);
}

// ResourceManager.GetObject(baseName, name): typed value over the invariant chain.
Il2CppObject* il2cpp_resource_manager_get_object(Il2CppObject* baseName, Il2CppObject* name)
{
    int32_t typeCode;
    const uint8_t* value = nullptr;
    int32_t valueLen = 0;
    if (!ResourceManagerResolve(baseName, name, nullptr, &typeCode, &value, &valueLen))
    {
        return nullptr;
    }
    switch (typeCode)
    {
        case 1:  return Utf8PayloadToString(value, valueLen);                    // string
        case 32: // byte[] -- the exact payload (net48 System.Byte[])
        {
            Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, valueLen);
            if (valueLen > 0)
            {
                std::memcpy(il2cpp_array_get_elem_ptr(arr, 0), value, static_cast<size_t>(valueLen));
            }
            return reinterpret_cast<Il2CppObject*>(arr);
        }
        case 33: // stream -- a read-only MemoryStream over the payload
            return il2cpp::runtime::il2cpp_stream_memory_from_bytes(value, valueLen);
        default: // Null / primitives / user types -> null (defensive)
            return nullptr;
    }
}

// ResourceManager.GetObject(baseName, name, cultureName) (R3 culture variant).
Il2CppObject* il2cpp_resource_manager_get_object_culture(Il2CppObject* baseName, Il2CppObject* name,
                                                         Il2CppObject* cultureName)
{
    int32_t typeCode;
    const uint8_t* value = nullptr;
    int32_t valueLen = 0;
    if (!ResourceManagerResolve(baseName, name, cultureName, &typeCode, &value, &valueLen))
    {
        return nullptr;
    }
    switch (typeCode)
    {
        case 1:  return Utf8PayloadToString(value, valueLen);
        case 32:
        {
            Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, valueLen);
            if (valueLen > 0)
            {
                std::memcpy(il2cpp_array_get_elem_ptr(arr, 0), value, static_cast<size_t>(valueLen));
            }
            return reinterpret_cast<Il2CppObject*>(arr);
        }
        case 33:
            return il2cpp::runtime::il2cpp_stream_memory_from_bytes(value, valueLen);
        default:
            return nullptr;
    }
}

// ResourceManager.GetStream(baseName, name): a Stream(33) resource as a
// read-only MemoryStream over the payload. Any other type (incl. ByteArray) is
// null -- net48 ResourceManager.GetStream throws InvalidOperationException for
// a non-stream resource (documented defensive collapse).
Il2CppObject* il2cpp_resource_manager_get_stream(Il2CppObject* baseName, Il2CppObject* name)
{
    int32_t typeCode;
    const uint8_t* value = nullptr;
    int32_t valueLen = 0;
    if (!ResourceManagerResolve(baseName, name, nullptr, &typeCode, &value, &valueLen))
    {
        return nullptr;
    }
    if (typeCode != 33)
    {
        return nullptr;
    }
    return il2cpp::runtime::il2cpp_stream_memory_from_bytes(value, valueLen);
}

// ResourceManager.GetStream(baseName, name, cultureName) (R3 culture variant).
Il2CppObject* il2cpp_resource_manager_get_stream_culture(Il2CppObject* baseName, Il2CppObject* name,
                                                         Il2CppObject* cultureName)
{
    int32_t typeCode;
    const uint8_t* value = nullptr;
    int32_t valueLen = 0;
    if (!ResourceManagerResolve(baseName, name, cultureName, &typeCode, &value, &valueLen))
    {
        return nullptr;
    }
    if (typeCode != 33)
    {
        return nullptr;
    }
    return il2cpp::runtime::il2cpp_stream_memory_from_bytes(value, valueLen);
}

//------------------------------------------------------------------------------
// RM-CULT: net48 ResourceManager culture fallback chain over the registry.
//
// The registry is the single-assembly manifest (D1): every embedded resource
// lives there under its logical name. net48 satellite-assembly lookup
// collapses to a name search: for a culture chain step, resolve
// "<baseName>.<culture>.resources" (invariant -> "<baseName>.resources"),
// find the blob in the registry, and look the key up ordinally. The first
// chain step that HAS the key wins (net48 GetString). A step whose blob is
// missing or malformed is SKIPPED (provider fault recovery -- net48 groveler
// returns null for a missing satellite; a corrupt blob collapses to a miss
// under the defensive contract). Every step misses -> false.
//------------------------------------------------------------------------------

namespace {

// Append "<baseName>[.<culture>].resources" to 'name' (capacity 'cap').
// Returns the length, or -1 when it does not fit (never truncates).
int BuildCultureResourceName(const char* baseName, const char* culture,
                             char* name, int32_t cap)
{
    int32_t len = 0;
    auto append = [&](const char* s) -> bool {
        while (*s != '\0')
        {
            if (len + 1 >= cap)
            {
                return false;
            }
            name[len++] = *s++;
        }
        return true;
    };
    if (!append(baseName))
    {
        return -1;
    }
    if (culture != nullptr && culture[0] != '\0')
    {
        if (!append(".") || !append(culture))
        {
            return -1;
        }
    }
    if (!append(".resources"))
    {
        return -1;
    }
    name[len] = '\0';
    return len;
}

// Advance 'culture' one step up the fallback chain (truncation model -- see
// the header): "en-US" -> "en" -> "" -> (no more). Returns false when the
// chain is exhausted (the invariant step has already been visited).
bool StepParentCulture(char* culture)
{
    if (culture[0] == '\0')
    {
        return false; // invariant already visited; chain done
    }
    const int32_t len = static_cast<int32_t>(std::strlen(culture));
    int32_t lastDash = -1;
    for (int32_t i = 0; i < len; i++)
    {
        if (culture[i] == '-')
        {
            lastDash = i;
        }
    }
    if (lastDash >= 0)
    {
        culture[lastDash] = '\0';
    }
    else
    {
        culture[0] = '\0'; // neutral -> invariant
    }
    return true;
}

//------------------------------------------------------------------------------
// R3: TYPED chain walk (net48 GetObject/GetStream semantics). The walk is the
// il2cpp_resources_find_string_fallback loop, generalized to return ANY value
// the first chain step whose blob CONTAINS the key carries (the first
// ResourceSet that has the key wins -- net48 GetObject). A step whose blob
// misses the key or is malformed is skipped (provider fault recovery). On a
// hit, sets *outTypeCode (net48 ResourceTypeCode: 1/32/33 carry *valuePtr/
// *valueLen; 0 and primitives carry an empty value). cultureName null == the
// invariant chain; an over-long culture name is a miss (no registered name can
// match).
//------------------------------------------------------------------------------
bool FindChainValue(const char* baseName, const char* cultureName, const char* key,
                    int32_t* outTypeCode, const uint8_t** valuePtr, int32_t* valueLen)
{
    if (baseName == nullptr || key == nullptr)
    {
        return false;
    }
    char culture[64];
    if (cultureName == nullptr || cultureName[0] == '\0')
    {
        culture[0] = '\0';
    }
    else
    {
        const int32_t cultureLen = static_cast<int32_t>(std::strlen(cultureName));
        if (cultureLen >= static_cast<int32_t>(sizeof(culture)))
        {
            return false;
        }
        std::memcpy(culture, cultureName, static_cast<size_t>(cultureLen) + 1);
    }

    char resName[512];
    for (;;)
    {
        if (BuildCultureResourceName(baseName, culture, resName,
                                     static_cast<int32_t>(sizeof(resName))) < 0)
        {
            return false; // base name too long: cannot be a registered name
        }
        const ManifestResourceEntry* e = FindResource(resName);
        if (e != nullptr && e->length > 0)
        {
            if (FindResourceValue(e->data, e->length, key, outTypeCode, valuePtr, valueLen))
            {
                return true; // the first chain step that HAS the key wins
            }
        }
        if (!StepParentCulture(culture))
        {
            break;
        }
    }
    return false;
}

} // namespace

bool il2cpp_resources_find_string_fallback(const char* baseName, const char* cultureName,
                                           const char* key, char* outBuf, int32_t outCap)
{
    if (baseName == nullptr || cultureName == nullptr || key == nullptr ||
        outBuf == nullptr || outCap <= 0)
    {
        return false;
    }
    // The chain is [culture, parents..., invariant]. Walk it; the first step
    // whose blob CONTAINS the key returns the value.
    char culture[64];
    const int32_t cultureLen = static_cast<int32_t>(std::strlen(cultureName));
    if (cultureLen >= static_cast<int32_t>(sizeof(culture)))
    {
        return false; // over-long culture name: no registered resource can match
    }
    std::memcpy(culture, cultureName, static_cast<size_t>(cultureLen) + 1);

    char resName[512];
    for (;;)
    {
        if (BuildCultureResourceName(baseName, culture, resName,
                                     static_cast<int32_t>(sizeof(resName))) < 0)
        {
            return false; // base name too long: cannot be a registered name
        }
        const ManifestResourceEntry* e = FindResource(resName);
        if (e != nullptr && e->length > 0)
        {
            const uint8_t* value = nullptr;
            int32_t valueLen = 0;
            // Key found in THIS culture's set -> win (net48 GetString returns
            // the first value found walking the chain).
            if (FindStringValue(e->data, e->length, key, &value, &valueLen))
            {
                if (valueLen >= outCap)
                {
                    return false; // buffer too small: treated as a miss
                }
                std::memcpy(outBuf, value, static_cast<size_t>(valueLen));
                outBuf[valueLen] = '\0';
                return true;
            }
            // Blob exists but the key is absent (or non-string/Null-typed
            // value -- FindStringValue returns false for typeCode != 1) ->
            // continue to the parent culture (net48 chain continuation).
        }
        // Missing or malformed blob -> skip this step (provider fault
        // recovery) and continue to the parent.
        if (!StepParentCulture(culture))
        {
            break;
        }
    }
    return false;
}

} // namespace il2cpp
