//------------------------------------------------------------------------------
// ipaddr.cpp -- IPv4 address parsing/formatting + host resolution (ipaddr.h).
//
// Platform divergence (socket.cpp precedent): this translation unit is the
// self-contained Windows/POSIX dual branch behind ipaddr.h.
//
//   Windows: winsock2. WSAStartup(2,2) is lazy and runs exactly once per
//            process (std::call_once; never WSACleanup). WSAStartup is
//            reference-counted by the OS, so socket.cpp's independent
//            once-guard and this one coexist safely. The ws2_32 link
//            dependency is self-contained through #pragma comment(lib,
//            "ws2_32.lib") -- and already PUBLIC via src/runtime/CMakeLists.
//   POSIX:   getaddrinfo from libc (BSD sockets headers); no extra link.
//
// Both branches share one resolution path: getaddrinfo with AF_INET hints
// (IPv4-only simplified model, net-domain.md section 1.4-1; the same shape
// as socket.cpp's connect host-resolution path).
//------------------------------------------------------------------------------

#include "ipaddr.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_format_exception / argument_null
#include "il2cpp_gc.h"     // il2cpp_gc_add_static_root / remove (RootScope)
#include "il2cpp_string.h" // Il2CppString / il2cpp_string_new_from_utf8

#include <cstdint>
#include <cstring>
#include <string>

#if defined(_WIN32)
    #if !defined(_WIN32_WINNT)
        #define _WIN32_WINNT 0x0600 // Vista+ winsock (getaddrinfo/inet_ntop)
    #endif
    #define WIN32_LEAN_AND_MEAN
    #define NOMINMAX
    #include <winsock2.h>
    #include <ws2tcpip.h>
    // MSVC and gcc-mingw honor #pragma comment(lib) (self-contained link);
    // the llvm-mingw clang driver warns "unknown pragma" instead, so clang
    // builds link with an explicit -lws2_32.
    #if defined(_MSC_VER) || (defined(__MINGW32__) && !defined(__clang__))
        #pragma comment(lib, "ws2_32.lib")
    #endif
    #include <mutex>
#else
    #include <sys/socket.h>
    #include <netinet/in.h>
    #include <netdb.h>
#endif

namespace il2cpp { namespace runtime {

namespace {

#if defined(_WIN32)
// WSAStartup is reference-counted by the OS (socket.cpp:76 precedent), so
// the runtime's other once-guard and this one can both run; each runs
// exactly once per process.
std::once_flag g_wsa_startup_flag;
bool g_wsa_ok = false;

void WsaStartupOnce()
{
    WSADATA wsaData;
    g_wsa_ok = (::WSAStartup(MAKEWORD(2, 2), &wsaData) == 0);
}

bool EnsureWsaStarted()
{
    std::call_once(g_wsa_startup_flag, WsaStartupOnce);
    return g_wsa_ok;
}
#endif

//------------------------------------------------------------------------------
// Rooting scope (linq_map.cpp:142-171 precedent): registers managed-pointer
// locals as static-root slots so the compacting collector rewrites the
// LOCALS when objects move; removal is RAII so a throwing allocation cannot
// leak a root slot. Slots are re-read through the registered variables.
//------------------------------------------------------------------------------
class RootScope
{
public:
    RootScope() : m_count(0) {}
    ~RootScope()
    {
        while (m_count > 0)
        {
            il2cpp_gc_remove_static_root(m_slots[m_count - 1]);
            --m_count;
        }
    }
    void Add(void** slot)
    {
        il2cpp_gc_add_static_root(slot);
        m_slots[m_count++] = slot;
    }

private:
    void** m_slots[4]; // max 2 used today (the result array per call)
    int32_t m_count;
};

// Convert a managed string's UTF-16 payload into ASCII text. The IPv4 /
// host-name grammar is pure ASCII (IDN is out of scope); the FIRST
// non-ASCII unit makes the whole input invalid (returns false).
bool AsciiFromManaged(Il2CppString* input, std::string* out)
{
    out->clear();
    out->reserve(static_cast<size_t>(input->length));
    for (int32_t i = 0; i < input->length; ++i)
    {
        const char16_t u = input->chars[i];
        if (u > 0x7F)
        {
            return false;
        }
        out->push_back(static_cast<char>(u));
    }
    return true;
}

//------------------------------------------------------------------------------
// Dotted-quad grammar (net48 legacy Parse, see ipaddr.h): 1-4 '.'-separated
// parts. Each part is a decimal or "0x"/"0X"-prefixed hexadecimal number;
// leading zeros are plain decimal ("01" -> 1). The LAST part occupies the
// trailing bytes as a wider field (the net48 inet_addr-style layout, probes
// verified 2026-08-09):
//   4 parts: each part a byte (0..255)                      "1.2.3.4"
//   3 parts: a.b.c -> {a, b, c>>8, c&0xff}  (c is 16-bit)   "1.2.3" -> 1.2.0.3
//   2 parts: a.b   -> {a, b>>16, b>>8, b}   (b is 24-bit)   "1.2"   -> 1.0.0.2
//   1 part :        -> the 32-bit value big-endian          "256"   -> 0.0.1.0
// Rejected: more than 4 parts, empty string/segment, signs, whitespace,
// trailing garbage, and a value exceeding its field width. The value is
// accumulated with an overflow check BEFORE each multiply, so no
// intermediate overflow is possible. Error path first: any violation
// returns 0 with 'out4' untouched (TryParse rule; the caller translates
// the 0 into the net48 FormatException -- Parse semantics).
//------------------------------------------------------------------------------
int32_t ParseDottedQuad(const char* text, uint8_t* out4)
{
    if (text == nullptr || out4 == nullptr)
    {
        return 0;
    }
    const char* p = text;
    uint64_t parts[4];
    int32_t nparts = 0;
    if (*p == '\0')
    {
        return 0; // empty string
    }
    while (true)
    {
        if (nparts == 4)
        {
            return 0; // more than four parts (or trailing garbage past four)
        }
        // Parse one part: "0x"/"0X" hex prefix or plain decimal.
        uint64_t value = 0;
        bool anyDigit = false;
        if (p[0] == '0' && (p[1] == 'x' || p[1] == 'X'))
        {
            p += 2;
            while ((*p >= '0' && *p <= '9') || (*p >= 'a' && *p <= 'f') || (*p >= 'A' && *p <= 'F'))
            {
                const uint64_t d = *p <= '9' ? static_cast<uint64_t>(*p - '0')
                    : (*p >= 'A' && *p <= 'F') ? static_cast<uint64_t>(*p - 'A' + 10)
                                               : static_cast<uint64_t>(*p - 'a' + 10);
                if (value > (0xFFFFFFFFull - d) / 16)
                {
                    return 0; // hex overflow past 32 bits
                }
                value = value * 16 + d;
                ++p;
                anyDigit = true;
            }
        }
        else
        {
            while (*p >= '0' && *p <= '9')
            {
                const uint64_t d = static_cast<uint64_t>(*p - '0');
                if (value > (0xFFFFFFFFull - d) / 10)
                {
                    return 0; // decimal overflow past 32 bits
                }
                value = value * 10 + d;
                ++p;
                anyDigit = true;
            }
        }
        if (!anyDigit)
        {
            return 0; // empty segment or non-digit start
        }
        parts[nparts] = value;
        ++nparts;
        if (*p == '\0')
        {
            break;
        }
        if (*p != '.')
        {
            return 0; // missing separator or trailing garbage
        }
        ++p;
        if (*p == '\0')
        {
            return 0; // trailing separator ("1.2.3.4." form cut short)
        }
    }
    // Pack per net48 field widths (probes: 3-part c is 16-bit, 2-part b is
    // 24-bit, 1-part is the whole 32-bit value big-endian).
    switch (nparts)
    {
        case 4:
            for (int32_t i = 0; i < 4; ++i)
            {
                if (parts[i] > 255)
                {
                    return 0; // byte segment out of 0..255
                }
                out4[i] = static_cast<uint8_t>(parts[i]);
            }
            return 1;
        case 3:
            if (parts[0] > 255 || parts[1] > 255 || parts[2] > 65535)
            {
                return 0; // 16-bit tail or byte head out of range
            }
            out4[0] = static_cast<uint8_t>(parts[0]);
            out4[1] = static_cast<uint8_t>(parts[1]);
            out4[2] = static_cast<uint8_t>((parts[2] >> 8) & 0xFF);
            out4[3] = static_cast<uint8_t>(parts[2] & 0xFF);
            return 1;
        case 2:
            if (parts[0] > 255 || parts[1] > 16777215)
            {
                return 0; // 24-bit tail or byte head out of range
            }
            out4[0] = static_cast<uint8_t>(parts[0]);
            out4[1] = static_cast<uint8_t>((parts[1] >> 16) & 0xFF);
            out4[2] = static_cast<uint8_t>((parts[1] >> 8) & 0xFF);
            out4[3] = static_cast<uint8_t>(parts[1] & 0xFF);
            return 1;
        default: // 1 part
            if (parts[0] > 0xFFFFFFFFull)
            {
                return 0; // 32-bit single value out of range
            }
            out4[0] = static_cast<uint8_t>((parts[0] >> 24) & 0xFF);
            out4[1] = static_cast<uint8_t>((parts[0] >> 16) & 0xFF);
            out4[2] = static_cast<uint8_t>((parts[0] >> 8) & 0xFF);
            out4[3] = static_cast<uint8_t>(parts[0] & 0xFF);
            return 1;
    }
}

// Build a managed byte[4] holding the address bytes (the GetAddressBytes
// shape -- primitive array: elemSize 1, isRefElement false, the
// il2cpp_array_new(nullptr, 1, false, n) convention).
Il2CppArray* AddressBytesToArray(const uint8_t* bytes4)
{
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false, 4);
    std::memcpy(arr->data, bytes4, 4);
    return arr;
}

} // namespace

//------------------------------------------------------------------------------
// IPAddress.Parse: managed string -> the four address bytes (host order).
// net48 semantics (Parse, not TryParse): 1 on success; null text throws
// ArgumentNullException, malformed text throws FormatException (the netcase
// e2e and the transpiler's Parse mapping rely on these throws -- the throws
// are the Parse contract, net-domain.md section 2.5). A null 'out4' output
// buffer is a C-API caller bug: returns 0 defensively (no managed analogue).
//------------------------------------------------------------------------------
int32_t il2cpp_ipaddress_parse(Il2CppString* text, uint8_t out4[4])
{
    if (text == nullptr)
    {
        // net48 IPAddress.Parse(null) throws ArgumentNullException.
        il2cpp_throw_argument_null_exception("ipString");
    }
    if (out4 == nullptr)
    {
        return 0; // defensive C-API: no output buffer (no managed analogue)
    }
    std::string ascii;
    if (!AsciiFromManaged(text, &ascii))
    {
        il2cpp_throw_format_exception(nullptr);
    }
    if (!ParseDottedQuad(ascii.c_str(), out4))
    {
        // net48 IPAddress.Parse THROWS FormatException on malformed input
        // (the TryParse form is the non-throwing 1/0 variant -- netcase
        // IsFormatException probes catch this). The exception factories
        // never return (C++ exception), so the trailing return is defensive.
        il2cpp_throw_format_exception(nullptr);
    }
    return 1;
}

//------------------------------------------------------------------------------
// IPAddress.ToString: the four address bytes (host order) -> the managed
// dotted-quad string. A null buffer yields the empty string (defensive).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_ipaddress_to_string(const uint8_t bytes4[4])
{
    if (bytes4 == nullptr)
    {
        return il2cpp_string_new_from_utf8("");
    }
    std::string text;
    text.reserve(15); // "255.255.255.255"
    for (int32_t i = 0; i < 4; ++i)
    {
        if (i != 0)
        {
            text.push_back('.');
        }
        text += std::to_string(bytes4[i]);
    }
    return il2cpp_string_new_from_utf8(text.c_str());
}

//------------------------------------------------------------------------------
// IPAddress.GetAddressBytes: the four address bytes -> managed byte[4].
// A null buffer yields nullptr (defensive).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_ipaddress_get_address_bytes(const uint8_t bytes4[4])
{
    if (bytes4 == nullptr)
    {
        return nullptr;
    }
    return AddressBytesToArray(bytes4);
}

//------------------------------------------------------------------------------
// Dns.GetHostAddresses: resolve 'host' (name or dotted-quad literal) to its
// IPv4 addresses via getaddrinfo(AF_INET). The result is an object array
// (isRefElement true, elemSize sizeof(void*)) whose elements are byte[4]
// managed arrays -- one per IPv4 addrinfo entry. Returns nullptr on null /
// empty / non-ASCII host or when no IPv4 address resolves (the icall layer
// maps that to SocketException).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_dns_get_host_addresses(Il2CppString* host)
{
    if (host == nullptr)
    {
        return nullptr;
    }
    std::string ascii;
    if (!AsciiFromManaged(host, &ascii) || ascii.empty())
    {
        return nullptr;
    }

#if defined(_WIN32)
    if (!EnsureWsaStarted())
    {
        return nullptr;
    }
#endif

    struct addrinfo hints;
    std::memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_INET; // IPv4-only simplified model
    hints.ai_socktype = SOCK_STREAM;

    struct addrinfo* result = nullptr;
    if (::getaddrinfo(ascii.c_str(), nullptr, &hints, &result) != 0
        || result == nullptr)
    {
        if (result != nullptr)
        {
            ::freeaddrinfo(result);
        }
        return nullptr;
    }

    int32_t count = 0;
    for (const struct addrinfo* ai = result; ai != nullptr; ai = ai->ai_next)
    {
        if (ai->ai_family == AF_INET)
        {
            ++count;
        }
    }
    if (count == 0)
    {
        ::freeaddrinfo(result);
        return nullptr;
    }

    Il2CppArray* arr = il2cpp_array_new(nullptr,
                                        static_cast<int32_t>(sizeof(void*)),
                                        true, count);
    // The outer array is a C++ local -- root it while it is filled with
    // freshly-allocated elements (regex_api.cpp:376-396 precedent), so a
    // GC-triggering allocation between element stores cannot collect it.
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&arr));

    int32_t slot = 0;
    for (const struct addrinfo* ai = result; ai != nullptr; ai = ai->ai_next)
    {
        if (ai->ai_family != AF_INET)
        {
            continue;
        }
        const struct sockaddr_in* sa =
            reinterpret_cast<const struct sockaddr_in*>(ai->ai_addr);
        // in_addr's memory layout IS the dotted-quad byte order on every
        // platform (inet_ntop reads it directly); copy the four bytes.
        const uint8_t* bytes =
            reinterpret_cast<const uint8_t*>(&sa->sin_addr);
        Il2CppArray* element = AddressBytesToArray(bytes);
        // Store immediately after the allocation: the slot is scanned
        // (isRefElement) and the element was rooted until this store.
        Il2CppObject** outSlot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(arr, slot));
        *outSlot = reinterpret_cast<Il2CppObject*>(element);
        ++slot;
    }
    ::freeaddrinfo(result);
    return arr;
}

}} // namespace il2cpp::runtime
