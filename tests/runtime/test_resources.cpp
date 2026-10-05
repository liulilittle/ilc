//------------------------------------------------------------------------------
// test_resources.cpp -- RES-1 (iron rule 18): embedded manifest resources.
//
// Verifies the runtime resource surface end-to-end:
//   1. il2cpp_resources_register + Assembly.GetManifestResourceNames() --
//      name enumeration (the exact resx fixture's two resources).
//   2. Assembly.GetManifestResourceStream(name) -- the payload as a read-only
//      MemoryStream OBJECT (batch 90 contract -- net48: the manifest stream IS
//      a Stream, not a raw byte[]): non-null, a Read byte round-trip, and the
//      ToArray() copy. Covers resx.Strings.resources + hello.bin, including
//      the unknown-name -> null error path.
//   3. ResourceManager.GetString minimal -- the .resources binary parser
//      (Magic 0xBEEFCACE + name/value tables): Greeting -> "Hello Resource",
//      AppName -> "ResxFixture", Missing -> null; a malformed blob -> null.
//
// The fixture data is embedded verbatim (the SAME bytes MetadataLoader
// extracts from resx.exe -- the obj/Release/net48/resx.Strings.resources blob
// and the hello.bin payload), so this test is the runtime half of the
// transpile-chain verification (the C# half lives in tests/unit).
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_resources.h"
#include "il2cpp_streamio.h" // Stream object surface (icall_stream_memory_to_array)

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

using namespace il2cpp; // unqualified Il2Cpp* types (test_io.cpp convention)

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

// Content equality of a managed string against an ASCII literal (null -> false).
bool StrEquals(Il2CppObject* result, const char* ascii)
{
    if (result == nullptr)
    {
        return false;
    }
    const Il2CppString* s = reinterpret_cast<const Il2CppString*>(result);
    const size_t len = std::strlen(ascii);
    if (s->length != static_cast<int32_t>(len))
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// Content equality of a byte[] payload against a raw byte buffer.
bool BytesEqual(Il2CppObject* result, const uint8_t* expected, int32_t expectedLen)
{
    if (result == nullptr)
    {
        return false;
    }
    const Il2CppArray* arr = reinterpret_cast<const Il2CppArray*>(result);
    if (arr->length != expectedLen)
    {
        return false;
    }
    return std::memcmp(arr->data, expected, static_cast<size_t>(expectedLen)) == 0;
}

// Content equality of a GetManifestResourceStream result -- batch 90 contract:
// the return is a read-only MemoryStream OBJECT (net48: the manifest stream IS
// a Stream, not a raw byte[]). Verify the full stream surface: non-null, a
// Read byte round-trip, and the ToArray() copy of the payload.
bool StreamEquals(Il2CppObject* stream, const uint8_t* expected, int32_t expectedLen)
{
    if (stream == nullptr)
    {
        return false;
    }
    // Read byte round-trip: Read(buf, 0, len) from position 0 returns the payload.
    Il2CppArray* buf = il2cpp_array_new(nullptr, 1, false, expectedLen);
    if (il2cpp::runtime::icall_stream_read_bytes(stream, buf, 0, expectedLen) != expectedLen)
    {
        return false;
    }
    if (std::memcmp(buf->data, expected, static_cast<size_t>(expectedLen)) != 0)
    {
        return false;
    }
    // ToArray() copy matches the payload too (position-independent).
    return BytesEqual(reinterpret_cast<Il2CppObject*>(il2cpp::runtime::icall_stream_memory_to_array(stream)),
                      expected, expectedLen);
}

// Managed string from an ASCII literal (icall boundary form).
Il2CppObject* Ms(const char* ascii)
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(ascii));
}

//------------------------------------------------------------------------------
// Fixture data -- the EXACT payloads embedded in tests/fixtures/resx.exe:
//   resx.Strings.resources (265 B): the net48 GenerateResource .resources blob
//   hello.bin (27 B): "HELLO WORLD BINARY RESOURCE"
//------------------------------------------------------------------------------
const uint8_t kStringsResources[] = {
    0xCE,0xCA,0xEF,0xBE,0x01,0x00,0x00,0x00,0x91,0x00,0x00,0x00,
    0x6C,0x53,0x79,0x73,0x74,0x65,0x6D,0x2E,0x52,0x65,0x73,0x6F,0x75,0x72,0x63,0x65,
    0x73,0x2E,0x52,0x65,0x73,0x6F,0x75,0x72,0x63,0x65,0x52,0x65,0x61,0x64,0x65,0x72,
    0x2C,0x20,0x6D,0x73,0x63,0x6F,0x72,0x6C,0x69,0x62,0x2C,0x20,0x56,0x65,0x72,0x73,
    0x69,0x6F,0x6E,0x3D,0x34,0x2E,0x30,0x2E,0x30,0x2E,0x30,0x2C,0x20,0x43,0x75,0x6C,
    0x74,0x75,0x72,0x65,0x3D,0x6E,0x65,0x75,0x74,0x72,0x61,0x6C,0x2C,0x20,0x50,0x75,
    0x62,0x6C,0x69,0x63,0x4B,0x65,0x79,0x54,0x6F,0x6B,0x65,0x6E,0x3D,0x62,0x37,0x37,
    0x61,0x35,0x63,0x35,0x36,0x31,0x39,0x33,0x34,0x65,0x30,0x38,0x39,
    0x23,0x53,0x79,0x73,0x74,0x65,0x6D,0x2E,0x52,0x65,0x73,0x6F,0x75,0x72,0x63,0x65,
    0x73,0x2E,0x52,0x75,0x6E,0x74,0x69,0x6D,0x65,0x52,0x65,0x73,0x6F,0x75,0x72,0x63,
    0x65,0x53,0x65,0x74,
    0x02,0x00,0x00,0x00,0x02,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
    0x50,0x41,0x44,0x50,0x41,0x44,0x50,
    0xA3,0x17,0x3E,0x40,0x64,0xAB,0xCB,0x5A,
    0x00,0x00,0x00,0x00,0x13,0x00,0x00,0x00,0xEC,0x00,0x00,0x00,
    0x0E,0x41,0x00,0x70,0x00,0x70,0x00,0x4E,0x00,0x61,0x00,0x6D,0x00,0x65,0x00,
    0x00,0x00,0x00,0x00,
    0x10,0x47,0x00,0x72,0x00,0x65,0x00,0x65,0x00,0x74,0x00,0x69,0x00,0x6E,0x00,0x67,
    0x00,0x0D,0x00,0x00,0x00,
    0x01,0x0B,0x52,0x65,0x73,0x78,0x46,0x69,0x78,0x74,0x75,0x72,0x65,
    0x01,0x0E,0x48,0x65,0x6C,0x6C,0x6F,0x20,0x52,0x65,0x73,0x6F,0x75,0x72,0x63,0x65,
};
constexpr int32_t kStringsResourcesLen = 265;

const uint8_t kHelloBin[] = {
    0x48,0x45,0x4C,0x4C,0x4F,0x20,0x57,0x4F,0x52,0x4C,0x44,0x20,
    0x42,0x49,0x4E,0x41,0x52,0x59,0x20,0x52,0x45,0x53,0x4F,0x55,0x52,0x43,0x45,
};
constexpr int32_t kHelloBinLen = 27;

// The pool table as the generated resource-pool.h would emit it.
const ManifestResourceEntry kPool[] = {
    { "resx.Strings.resources", kStringsResources, kStringsResourcesLen },
    { "hello.bin", kHelloBin, kHelloBinLen },
};
constexpr int32_t kPoolCount = 2;

//------------------------------------------------------------------------------
// Synthetic .resources blob builder (the V2 resgen layout the toolchain emits)
// -- used to exercise parser edge cases a real fixture cannot: long values,
// V1-file rejection, corrupt offsets, non-string/null types.
//------------------------------------------------------------------------------
struct ResBlobBuilder
{
    struct NameEntry
    {
        const char* name;
        int32_t dataPos;
    };
    struct DataEntry
    {
        uint8_t typeCode;         // 1 = String; 0 = Null; other = typed value
        std::string value;        // UTF-8 value for String entries
        std::vector<uint8_t> raw; // payload for non-String entries
    };

    std::vector<NameEntry> names;
    std::vector<DataEntry> data;
    int32_t dataCursor = 0;
    size_t dataSecOffFieldPos = 0; // Finish() records the dataSectionOffset field

    static void AppendI4(std::vector<uint8_t>& v, int32_t x)
    {
        const uint32_t u = static_cast<uint32_t>(x);
        for (int i = 0; i < 4; ++i)
        {
            v.push_back(static_cast<uint8_t>((u >> (8 * i)) & 0xFFu));
        }
    }
    static int32_t SevenBitSize(int32_t v)
    {
        int32_t n = 1;
        uint32_t u = static_cast<uint32_t>(v);
        while (u >= 0x80u)
        {
            u >>= 7;
            ++n;
        }
        return n;
    }
    static void Append7Bit(std::vector<uint8_t>& v, int32_t x)
    {
        uint32_t u = static_cast<uint32_t>(x);
        while (u >= 0x80u)
        {
            v.push_back(static_cast<uint8_t>((u & 0x7Fu) | 0x80u));
            u >>= 7;
        }
        v.push_back(static_cast<uint8_t>(u));
    }
    static void AppendStr(std::vector<uint8_t>& v, const char* s)
    {
        const int32_t n = static_cast<int32_t>(std::strlen(s));
        Append7Bit(v, n);
        v.insert(v.end(), reinterpret_cast<const uint8_t*>(s),
                 reinterpret_cast<const uint8_t*>(s) + n);
    }

    // Wire 'name' to the NEXT data entry, recording the dataPos cursor.
    void AddString(const char* name, const char* utf8Value)
    {
        const int32_t n = static_cast<int32_t>(std::strlen(utf8Value));
        names.push_back({ name, dataCursor });
        data.push_back({ 1, utf8Value, {} });
        dataCursor += 1 + SevenBitSize(n) + n;
    }
    void AddTyped(const char* name, uint8_t typeCode, const std::vector<uint8_t>& payload)
    {
        names.push_back({ name, dataCursor });
        data.push_back({ typeCode, std::string(), payload });
        dataCursor += 1 + static_cast<int32_t>(payload.size());
    }
    void AddNull(const char* name)
    {
        names.push_back({ name, dataCursor });
        data.push_back({ 0, std::string(), {} });
        dataCursor += 1;
    }
    void AddNameRaw(const char* name, int32_t dataPos)
    {
        names.push_back({ name, dataPos });
    }
    void AddStringValueOnly(const char* utf8Value)
    {
        const int32_t n = static_cast<int32_t>(std::strlen(utf8Value));
        data.push_back({ 1, utf8Value, {} });
        dataCursor += 1 + SevenBitSize(n) + n;
    }

    std::vector<uint8_t> Finish(int32_t version = 2)
    {
        std::vector<uint8_t> b;
        AppendI4(b, static_cast<int32_t>(0xBEEFCACE));
        AppendI4(b, 1); // resMgrHeaderVersion == 1: two strings follow
        AppendI4(b, 0); // numBytesToSkip (unused for header v1)
        AppendStr(b, "System.Resources.ResourceReader, mscorlib");
        AppendStr(b, "System.Resources.RuntimeResourceSet");
        AppendI4(b, version);
        AppendI4(b, static_cast<int32_t>(names.size()));
        AppendI4(b, 0); // numTypes
        while ((b.size() & 7) != 0)
        {
            b.push_back(0x50); // pad to 8 bytes ("P", like resgen)
        }
        for (size_t i = 0; i < names.size(); ++i)
        {
            AppendI4(b, 0); // name hashes (the parser skips them)
        }
        size_t cursor = 0;
        for (size_t i = 0; i < names.size(); ++i)
        {
            AppendI4(b, static_cast<int32_t>(cursor)); // namePositions[i]
            cursor += 1; // 7-bit byteLen prefix
            cursor += 2 * std::strlen(names[i].name); // UTF-16 name bytes
            cursor += 4; // dataPos
        }
        dataSecOffFieldPos = b.size();
        AppendI4(b, 0); // dataSectionOffset placeholder -- patched below
        for (size_t i = 0; i < names.size(); ++i)
        {
            Append7Bit(b, static_cast<int32_t>(2 * std::strlen(names[i].name)));
            for (const char* c = names[i].name; *c != '\0'; ++c)
            {
                b.push_back(static_cast<uint8_t>(*c));
                b.push_back(0);
            }
            AppendI4(b, names[i].dataPos);
        }
        const uint32_t dataSectionOffset = static_cast<uint32_t>(b.size());
        for (const DataEntry& e : data)
        {
            b.push_back(e.typeCode);
            if (e.typeCode == 1)
            {
                Append7Bit(b, static_cast<int32_t>(e.value.size()));
                b.insert(b.end(), e.value.begin(), e.value.end());
            }
            else
            {
                b.insert(b.end(), e.raw.begin(), e.raw.end());
            }
        }
        for (int i = 0; i < 4; ++i)
        {
            b[dataSecOffFieldPos + i] =
                static_cast<uint8_t>((dataSectionOffset >> (8 * i)) & 0xFFu);
        }
        return b;
    }
};

} // namespace

int main()
{
    // --- 1. Registry + name enumeration -----------------------------------
    il2cpp_resources_register(kPool, kPoolCount);

    Il2CppObject* names = il2cpp_assembly_get_manifest_resource_names();
    Expect(names != nullptr, "GetManifestResourceNames returns a non-null string[]");
    Il2CppArray* namesArr = reinterpret_cast<Il2CppArray*>(names);
    Expect(namesArr->length == 2, "GetManifestResourceNames lists both resources");
    Il2CppObject** nameSlots = reinterpret_cast<Il2CppObject**>(namesArr->data);
    Expect(StrEquals(nameSlots[0], "resx.Strings.resources"), "name[0] is resx.Strings.resources");
    Expect(StrEquals(nameSlots[1], "hello.bin"), "name[1] is hello.bin");

    // Empty pool -> empty names array (never null).
    il2cpp_resources_register(nullptr, 0);
    Il2CppObject* emptyNames = il2cpp_assembly_get_manifest_resource_names();
    Expect(reinterpret_cast<Il2CppArray*>(emptyNames)->length == 0,
           "empty pool -> zero-length names array");
    il2cpp_resources_register(kPool, kPoolCount);

    // register(nullptr, positive count) clamps to an empty registry (no crash).
    il2cpp_resources_register(nullptr, 5);
    Il2CppObject* clampedNames = il2cpp_assembly_get_manifest_resource_names();
    Expect(reinterpret_cast<Il2CppArray*>(clampedNames)->length == 0,
           "register(null, 5) clamps to an empty registry");
    il2cpp_resources_register(kPool, kPoolCount);

    // --- 2. Stream content (Assembly.GetManifestResourceStream) ------------
    // batch 90: the manifest stream is a read-only MemoryStream OBJECT (net48
    // -- s.Read(buf, 0, n) on the result), NOT a raw byte[].
    Il2CppObject* stringsBlob = il2cpp_assembly_get_manifest_resource_stream(Ms("resx.Strings.resources"));
    Expect(StreamEquals(stringsBlob, kStringsResources, kStringsResourcesLen),
           "stream(resx.Strings.resources) is a non-null Stream whose Read/ToArray return the exact 265-byte .resources payload");

    Il2CppObject* helloBlob = il2cpp_assembly_get_manifest_resource_stream(Ms("hello.bin"));
    Expect(StreamEquals(helloBlob, kHelloBin, kHelloBinLen),
           "stream(hello.bin) is a non-null Stream whose Read/ToArray return the exact 27-byte payload");

    Expect(il2cpp_assembly_get_manifest_resource_stream(Ms("NoSuch.resource")) == nullptr,
           "stream(unknown name) returns null (no throw)");
    Expect(il2cpp_assembly_get_manifest_resource_stream(nullptr) == nullptr,
           "stream(null) returns null (no throw)");

    // --- 3. ResourceManager.GetString minimal (.resources parser) ----------
    // The RM path takes the raw BLOB (a separate icall -- the .resources
    // parser reads bytes), NOT the stream wrapper: il2cpp_resources_get_blob.
    Il2CppObject* rmBlob = il2cpp_resources_get_blob(Ms("resx.Strings.resources"));
    Expect(rmBlob != nullptr, "get_blob(resx.Strings.resources) returns a non-null byte[]");

    Expect(StrEquals(il2cpp_resource_manager_get_string(rmBlob, Ms("Greeting")), "Hello Resource"),
           "GetString(Greeting) == \"Hello Resource\"");
    Expect(StrEquals(il2cpp_resource_manager_get_string(rmBlob, Ms("AppName")), "ResxFixture"),
           "GetString(AppName) == \"ResxFixture\"");
    Expect(il2cpp_resource_manager_get_string(rmBlob, Ms("Missing")) == nullptr,
           "GetString(Missing) returns null (key absent)");
    Expect(il2cpp_resource_manager_get_string(nullptr, Ms("Greeting")) == nullptr,
           "GetString(null blob) returns null (no throw)");
    Expect(il2cpp_resource_manager_get_string(rmBlob, nullptr) == nullptr,
           "GetString(null key) returns null (no throw)");

    // Malformed blob (bad magic) -> null, no throw.
    const uint8_t badMagic[] = { 0x00, 0x01, 0x02, 0x03, 0x01, 0x00, 0x00, 0x00 };
    Il2CppArray* badArr = il2cpp_array_new(nullptr, 1, false, static_cast<int32_t>(sizeof(badMagic)));
    std::memcpy(badArr->data, badMagic, sizeof(badMagic));
    Expect(il2cpp_resource_manager_get_string(reinterpret_cast<Il2CppObject*>(badArr), Ms("Greeting")) == nullptr,
           "GetString(malformed magic) returns null (no throw)");

    // Low-level parser direct (the ResourceReader core port).
    char valueBuf[256];
    Expect(il2cpp_resources_parse_get_string(kStringsResources, kStringsResourcesLen,
                                             "Greeting", valueBuf, sizeof(valueBuf))
               && std::strcmp(valueBuf, "Hello Resource") == 0,
           "parse_get_string(Greeting) == \"Hello Resource\"");
    Expect(!il2cpp_resources_parse_get_string(kStringsResources, kStringsResourcesLen,
                                              "Missing", valueBuf, sizeof(valueBuf)),
           "parse_get_string(Missing) is a miss");
    Expect(!il2cpp_resources_parse_get_string(badMagic, static_cast<int32_t>(sizeof(badMagic)),
                                              "Greeting", valueBuf, sizeof(valueBuf)),
           "parse_get_string(bad magic) is a miss");

    // --- 4. Synthetic-blob parser edge cases (net48 parity) -----------------
    // Values are arbitrary length: a >512-byte value must round-trip in full
    // (the former fixed 512-byte buffer would have returned null).
    {
        ResBlobBuilder b;
        char longValue[600];
        std::memset(longValue, 'A', sizeof(longValue) - 1);
        longValue[sizeof(longValue) - 1] = '\0';
        b.AddString("LongKey", longValue);
        std::vector<uint8_t> blob = b.Finish();

        Il2CppArray* blobArr = il2cpp_array_new(nullptr, 1, false,
                                                static_cast<int32_t>(blob.size()));
        std::memcpy(blobArr->data, blob.data(), blob.size());
        Il2CppObject* rm = reinterpret_cast<Il2CppObject*>(blobArr);
        Expect(StrEquals(il2cpp_resource_manager_get_string(rm, Ms("LongKey")), longValue),
               "GetString returns a >512-byte value in full (no length cap)");

        char wideBuf[1024];
        Expect(il2cpp_resources_parse_get_string(blob.data(), static_cast<int32_t>(blob.size()),
                                                 "LongKey", wideBuf, sizeof(wideBuf))
                   && std::strcmp(wideBuf, longValue) == 0,
               "parse_get_string returns a >512-byte value in full");
    }

    // V1-FILE format is rejected as unsupported (never silently misparsed:
    // V1 data entries carry a type-table index, not a ResourceTypeCode).
    {
        ResBlobBuilder b;
        b.AddString("K", "v");
        std::vector<uint8_t> blob = b.Finish(1); // file version 1
        char smallBuf[64];
        Expect(!il2cpp_resources_parse_get_string(blob.data(), static_cast<int32_t>(blob.size()),
                                                  "K", smallBuf, sizeof(smallBuf)),
               "V1-file blob is rejected (miss, never misparsed)");
    }

    // Corrupt dataPos values must be a miss -- never an OOB/underflow read.
    {
        ResBlobBuilder b;
        b.AddStringValueOnly("x");
        b.AddNameRaw("Neg", -5);
        std::vector<uint8_t> blob = b.Finish();
        char smallBuf[64];
        Expect(!il2cpp_resources_parse_get_string(blob.data(), static_cast<int32_t>(blob.size()),
                                                  "Neg", smallBuf, sizeof(smallBuf)),
               "negative dataPos -> miss (no OOB read)");
    }
    {
        ResBlobBuilder b;
        b.AddStringValueOnly("x");
        b.AddNameRaw("Huge", 0x7FFFFFFF);
        std::vector<uint8_t> blob = b.Finish();
        char smallBuf[64];
        Expect(!il2cpp_resources_parse_get_string(blob.data(), static_cast<int32_t>(blob.size()),
                                                  "Huge", smallBuf, sizeof(smallBuf)),
               "overflowing dataPos -> miss (no OOB read)");
    }

    // dataSectionOffset < nameSectionOffset is corrupt (net48 throws) -> miss.
    {
        ResBlobBuilder b;
        b.AddString("K", "v");
        std::vector<uint8_t> blob = b.Finish();
        for (int i = 0; i < 4; ++i)
        {
            blob[b.dataSecOffFieldPos + i] = 0; // force dataSectionOffset = 0
        }
        char smallBuf[64];
        Expect(!il2cpp_resources_parse_get_string(blob.data(), static_cast<int32_t>(blob.size()),
                                                  "K", smallBuf, sizeof(smallBuf)),
               "dataSectionOffset < nameSectionOffset -> miss");
    }

    // An embedded-NUL key must not be truncated to a shorter name (false
    // positive) -- the name is invalid and the lookup is a miss.
    {
        ResBlobBuilder b;
        b.AddString("Greet", "Hi");
        std::vector<uint8_t> blob = b.Finish();
        Il2CppArray* blobArr = il2cpp_array_new(nullptr, 1, false,
                                                static_cast<int32_t>(blob.size()));
        std::memcpy(blobArr->data, blob.data(), blob.size());
        Il2CppObject* rm = reinterpret_cast<Il2CppObject*>(blobArr);

        Expect(StrEquals(il2cpp_resource_manager_get_string(rm, Ms("Greet")), "Hi"),
               "GetString(Greet) == \"Hi\" (synthetic blob)");
        const char16_t nulKey[] = { 'G', 'r', 'e', 'e', 't', 0, 'i', 'n', 'g' };
        Il2CppObject* nulName = reinterpret_cast<Il2CppObject*>(il2cpp_string_new(
            nulKey, static_cast<int32_t>(sizeof(nulKey) / sizeof(nulKey[0]))));
        Expect(il2cpp_resource_manager_get_string(rm, nulName) == nullptr,
               "embedded-NUL key -> null (no truncation false-positive)");
    }

    // Non-string (Int32) and Null-typed resources -> null (net48 GetString).
    {
        ResBlobBuilder b;
        const uint8_t int32Payload[] = { 0x05, 0x00, 0x00, 0x00 };
        b.AddTyped("Num", 3, std::vector<uint8_t>(int32Payload, int32Payload + 4));
        b.AddNull("Empty");
        std::vector<uint8_t> blob = b.Finish();
        Il2CppArray* blobArr = il2cpp_array_new(nullptr, 1, false,
                                                static_cast<int32_t>(blob.size()));
        std::memcpy(blobArr->data, blob.data(), blob.size());
        Il2CppObject* rm = reinterpret_cast<Il2CppObject*>(blobArr);
        Expect(il2cpp_resource_manager_get_string(rm, Ms("Num")) == nullptr,
               "non-string (Int32) resource -> null");
        Expect(il2cpp_resource_manager_get_string(rm, Ms("Empty")) == nullptr,
               "Null-typed resource -> null (net48 GetString parity)");
    }

    // --- 5. RM-CULT: net48 culture fallback chain ---------------------------
    // A single-assembly culture resource pool (satellite assemblies collapse
    // to culture-suffixed manifest names -- see il2cpp_resources.h RM-CULT):
    //   resx.Strings.resources      (invariant): Greeting=Hello Resource, OnlyNeutral
    //   resx.Strings.en.resources   (en):        OnlyEn
    //   resx.Strings.en-US.resources(en-US):     OnlyUS
    //   resx.Strings.zh.resources   (zh):        Greeting=Hello zh
    //   resx.Strings.en-US-BAD.resources (en-US-BAD): MALFORMED blob (bad magic)
    {
        ResBlobBuilder invB;
        invB.AddString("Greeting", "Hello Resource");
        invB.AddString("OnlyNeutral", "Neutral Value");
        std::vector<uint8_t> invBlob = invB.Finish();

        ResBlobBuilder enB;
        enB.AddString("OnlyEn", "English Only");
        std::vector<uint8_t> enBlob = enB.Finish();

        ResBlobBuilder enUsB;
        enUsB.AddString("OnlyUS", "US Only");
        std::vector<uint8_t> enUsBlob = enUsB.Finish();

        ResBlobBuilder zhB;
        zhB.AddString("Greeting", "Hello zh");
        std::vector<uint8_t> zhBlob = zhB.Finish();

        // A corrupt culture blob (bad magic) to prove provider fault recovery:
        // a step whose blob is malformed must be SKIPPED, not fail the chain.
        // 'en-US-BAD' sits mid-chain (parents en-US -> en -> invariant) so the
        // chain must fall THROUGH it to a parent that has the key.
        ResBlobBuilder badB;
        badB.AddString("Greeting", "corrupt");
        std::vector<uint8_t> badBlob = badB.Finish();
        badBlob[0] = 0xFF; // break the 0xBEEFCACE magic

        const ManifestResourceEntry kCulturePool[] = {
            { "resx.Strings.resources", invBlob.data(), static_cast<int32_t>(invBlob.size()) },
            { "resx.Strings.en.resources", enBlob.data(), static_cast<int32_t>(enBlob.size()) },
            { "resx.Strings.en-US.resources", enUsBlob.data(), static_cast<int32_t>(enUsBlob.size()) },
            { "resx.Strings.zh.resources", zhBlob.data(), static_cast<int32_t>(zhBlob.size()) },
            { "resx.Strings.en-US-BAD.resources", badBlob.data(), static_cast<int32_t>(badBlob.size()) },
        };
        constexpr int32_t kCulturePoolCount = 5;
        il2cpp_resources_register(kCulturePool, kCulturePoolCount);

        char fb[256];

        // Specific culture hit: en-US has the key -> wins directly.
        Expect(il2cpp_resources_find_string_fallback("resx.Strings", "en-US", "OnlyUS",
                                                     fb, sizeof(fb))
                   && std::strcmp(fb, "US Only") == 0,
               "fallback(en-US, OnlyUS) == 'US Only' (specific-culture hit)");

        // Parent-chain hit: en-US lacks 'OnlyEn' -> falls to en.
        Expect(il2cpp_resources_find_string_fallback("resx.Strings", "en-US", "OnlyEn",
                                                     fb, sizeof(fb))
                   && std::strcmp(fb, "English Only") == 0,
               "fallback(en-US, OnlyEn) == 'English Only' (parent-culture hit)");

        // Invariant hit: en-US lacks 'Greeting' -> en lacks it too -> invariant.
        Expect(il2cpp_resources_find_string_fallback("resx.Strings", "en-US", "Greeting",
                                                     fb, sizeof(fb))
                   && std::strcmp(fb, "Hello Resource") == 0,
               "fallback(en-US, Greeting) == 'Hello Resource' (invariant hit)");

        // Key absent in every chain step -> false (never throws).
        Expect(!il2cpp_resources_find_string_fallback("resx.Strings", "en-US", "Missing",
                                                      fb, sizeof(fb)),
               "fallback(en-US, Missing) is a miss");

        // zh chain: specific zh has the key.
        Expect(il2cpp_resources_find_string_fallback("resx.Strings", "zh-CN", "Greeting",
                                                     fb, sizeof(fb))
                   && std::strcmp(fb, "Hello zh") == 0,
               "fallback(zh-CN, Greeting) == 'Hello zh' (parent culture zh)");

        // Invariant-only pool miss for an unknown base name -> false.
        Expect(!il2cpp_resources_find_string_fallback("resx.None", "en-US", "Greeting",
                                                      fb, sizeof(fb)),
               "fallback(resx.None, en-US, Greeting) is a miss (no base blob)");

        // Provider fault recovery: 'en-US-BAD' culture's blob is malformed (bad
        // magic) -> the step is SKIPPED and the chain falls through en-US (key
        // absent there) to en, which has the key.
        Expect(il2cpp_resources_find_string_fallback("resx.Strings", "en-US-BAD", "OnlyEn",
                                                     fb, sizeof(fb))
                   && std::strcmp(fb, "English Only") == 0,
               "fallback(en-US-BAD, OnlyEn) == 'English Only' (malformed blob skipped)");

        // Guard clauses: null args / empty buffers -> false, no crash.
        Expect(!il2cpp_resources_find_string_fallback(nullptr, "en-US", "OnlyUS",
                                                      fb, sizeof(fb)),
               "fallback(null base) is a miss");
        Expect(!il2cpp_resources_find_string_fallback("resx.Strings", "en-US", "OnlyUS",
                                                      nullptr, 0),
               "fallback(null outBuf) is a miss");
        Expect(!il2cpp_resources_find_string_fallback("resx.Strings", "en-US", nullptr,
                                                      fb, sizeof(fb)),
               "fallback(null key) is a miss");

        il2cpp_resources_register(kPool, kPoolCount); // restore the fixture pool
    }

    if (g_failures == 0)
    {
        std::printf("all runtime resource tests passed\n");
        return 0;
    }
    std::printf("%d resource test(s) FAILED\n", g_failures);
    return 1;
}
