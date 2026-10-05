//------------------------------------------------------------------------------
// test_ipaddr.cpp -- IPAddress / Dns basic-domain unit tests (ipaddr.h).
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in ipaddr.h:
//   1. il2cpp_ipaddress_parse: dotted-quad round-trip for 127.0.0.1 /
//      255.255.255.255 / 0.0.0.0 / 10.20.30.40 / 192.168.1.1; the net48
//      LEGACY leniency grammar (1-4 parts, hex "0x"/"0X" prefixes, leading
//      zeros, wider trailing field -- "1.2.3" -> 1.2.0.3, "1.2" -> 1.0.0.2,
//      "256" -> 0.0.1.0); malformed input (empty, wrong segment count,
//      empty segment, non-digit, signs, whitespace, trailing garbage,
//      out-of-range) THROWS FormatException; null text throws
//      ArgumentNullException (net48 Parse semantics -- NOT the TryParse 1/0
//      form). A null output buffer returns 0 defensively.
//   2. il2cpp_ipaddress_to_string: exact dotted-quad rebuild; null buffer
//      yields the empty string.
//   3. il2cpp_ipaddress_get_address_bytes: managed byte[4] with the exact
//      payload; null buffer yields nullptr.
//   4. il2cpp_dns_get_host_addresses: "localhost" resolves and includes
//      127.0.0.1 (Windows hosts file / POSIX /etc/hosts loopback guarantee);
//      the numeric literal "127.0.0.1" resolves to itself; null / empty
//      host yields nullptr. Every element is a byte[4] managed array and the
//      outer array is an object array (isRefElem true).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "ipaddr.h"

#include "il2cpp_exception.h" // DotNetException / il2cpp_exception_matches
#include "il2cpp_string.h" // il2cpp_string_new_from_utf8 (test inputs)

#include <cstdint>
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

il2cpp::Il2CppString* MakeString(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_utf8(ascii);
}

bool StringEquals(il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return ascii == nullptr;
    }
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(s->length) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

int32_t ParseInto(const char* text, uint8_t out4[4])
{
    return il2cpp::runtime::il2cpp_ipaddress_parse(MakeString(text), out4);
}

// net48 Parse semantics: malformed text THROWS FormatException (the 1/0
// return is the TryParse variant, not wired). Returns true when the call
// throws a DotNetException whose chain matches FormatException.
bool ThrowsFormatException(const char* text)
{
    uint8_t out[4];
    try
    {
        il2cpp::runtime::il2cpp_ipaddress_parse(MakeString(text), out);
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches("System.FormatException", e.type_name());
    }
}

// null text throws ArgumentNullException (net48 Parse contract).
bool ThrowsArgumentNull(il2cpp::Il2CppString* text)
{
    uint8_t out[4];
    try
    {
        il2cpp::runtime::il2cpp_ipaddress_parse(text, out);
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches("System.ArgumentNullException", e.type_name());
    }
}

bool ExpectBytes4(const uint8_t actual[4], const uint8_t expected[4])
{
    return actual[0] == expected[0] && actual[1] == expected[1]
        && actual[2] == expected[2] && actual[3] == expected[3];
}

// Element 'index' of the DNS result array (an object array) as a byte[4].
il2cpp::Il2CppArray* DnsElement(il2cpp::Il2CppArray* addresses, int32_t index)
{
    il2cpp::Il2CppObject** slot = reinterpret_cast<il2cpp::Il2CppObject**>(
        il2cpp_array_get_elem_ptr(addresses, index));
    return reinterpret_cast<il2cpp::Il2CppArray*>(*slot);
}

// Does the DNS result contain an element equal to the expected 4 bytes?
bool HasAddress(il2cpp::Il2CppArray* addresses, const uint8_t expected[4])
{
    if (addresses == nullptr)
    {
        return false;
    }
    for (int32_t i = 0; i < addresses->length; ++i)
    {
        il2cpp::Il2CppArray* element = DnsElement(addresses, i);
        if (element == nullptr || element->length != 4 || element->elemSize != 1)
        {
            continue;
        }
        const uint8_t* bytes =
            reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(element, 0));
        if (ExpectBytes4(bytes, expected))
        {
            return true;
        }
    }
    return false;
}

} // namespace

int main()
{
    using il2cpp::runtime::il2cpp_dns_get_host_addresses;
    using il2cpp::runtime::il2cpp_ipaddress_get_address_bytes;
    using il2cpp::runtime::il2cpp_ipaddress_parse;
    using il2cpp::runtime::il2cpp_ipaddress_to_string;

    // --- 1a. Parse: valid dotted quads ---
    {
        const uint8_t expected[] = {127, 0, 0, 1};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("127.0.0.1", out) == 1
               && ExpectBytes4(out, expected),
               "parse(\"127.0.0.1\") == 127.0.0.1");
    }
    {
        const uint8_t expected[] = {255, 255, 255, 255};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("255.255.255.255", out) == 1
               && ExpectBytes4(out, expected),
               "parse(\"255.255.255.255\") == 255.255.255.255");
    }
    {
        const uint8_t expected[] = {0, 0, 0, 0};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("0.0.0.0", out) == 1
               && ExpectBytes4(out, expected),
               "parse(\"0.0.0.0\") == 0.0.0.0");
    }
    {
        const uint8_t expected[] = {10, 20, 30, 40};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("10.20.30.40", out) == 1
               && ExpectBytes4(out, expected),
               "parse(\"10.20.30.40\") == 10.20.30.40");
    }
    {
        const uint8_t expected[] = {192, 168, 1, 1};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("192.168.1.1", out) == 1
               && ExpectBytes4(out, expected),
               "parse(\"192.168.1.1\") == 192.168.1.1");
    }

    // --- 1b. net48 legacy leniency: shorter forms / hex / leading zeros ---
    // The net48 Parse grammar (csc probe oracle) accepts 1-4 parts; the LAST
    // part occupies the trailing bytes as a wider field (3-part tail is
    // 16-bit: "1.2.3" -> 1.2.0.3; 2-part tail is 24-bit: "1.2" -> 1.0.0.2;
    // 1-part is the 32-bit value big-endian: "256" -> 0.0.1.0). Hex
    // "0x"/"0X" prefixes and leading zeros are plain leniency.
    {
        const uint8_t expected[] = {1, 2, 0, 3};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("1.2.3", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"1.2.3\") == 1.2.0.3 (3-part, tail is 16-bit)");
    }
    {
        const uint8_t expected[] = {1, 0, 0, 2};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("1.2", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"1.2\") == 1.0.0.2 (2-part, tail is 24-bit)");
    }
    {
        const uint8_t expected[] = {0, 0, 1, 0};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("256", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"256\") == 0.0.1.0 (1-part 32-bit big-endian)");
    }
    {
        const uint8_t expected[] = {127, 0, 0, 1};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("0x7f.0.0.1", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"0x7f.0.0.1\") == 127.0.0.1 (hex segment)");
    }
    {
        const uint8_t expected[] = {255, 0, 0, 1};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("0Xff.0.0.1", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"0Xff.0.0.1\") == 255.0.0.1 (upper-case hex prefix)");
    }
    {
        const uint8_t expected[] = {1, 2, 16, 3};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("1.2.0x10.3", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"1.2.0x10.3\") == 1.2.16.3 (hex in a middle segment)");
    }
    {
        const uint8_t expected[] = {1, 2, 3, 4};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("01.2.3.4", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"01.2.3.4\") == 1.2.3.4 (leading zero)");
    }
    {
        const uint8_t expected[] = {1, 2, 3, 4};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("1.02.3.4", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"1.02.3.4\") == 1.2.3.4 (leading zero)");
    }
    {
        const uint8_t expected[] = {1, 2, 3, 4};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("1.2.3.004", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"1.2.3.004\") == 1.2.3.4 (leading zero)");
    }
    {
        const uint8_t expected[] = {1, 2, 255, 255};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("1.2.65535", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"1.2.65535\") == 1.2.255.255 (3-part 16-bit max)");
    }
    {
        const uint8_t expected[] = {1, 255, 255, 255};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("1.16777215", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"1.16777215\") == 1.255.255.255 (2-part 24-bit max)");
    }
    {
        const uint8_t expected[] = {255, 255, 255, 255};
        uint8_t out[4] = {0, 0, 0, 0};
        Expect(ParseInto("4294967295", out) == 1 && ExpectBytes4(out, expected),
               "parse(\"4294967295\") == 255.255.255.255 (1-part 32-bit max)");
    }

    // --- 1c. net48 Parse THROWS FormatException on malformed input ---
    Expect(ThrowsFormatException(""), "parse(\"\") throws FormatException");
    Expect(ThrowsFormatException("a.b.c.d"), "parse(\"a.b.c.d\") throws FormatException");
    Expect(ThrowsFormatException("1.2.3.4.5"), "parse(\"1.2.3.4.5\") throws FormatException (5 parts)");
    Expect(ThrowsFormatException("1.2.3.256"), "parse(\"1.2.3.256\") throws FormatException");
    Expect(ThrowsFormatException("1.2.3.999"), "parse(\"1.2.3.999\") throws FormatException");
    Expect(ThrowsFormatException("255.255.255.256"), "parse(\"255.255.255.256\") throws FormatException");
    Expect(ThrowsFormatException("1.2.3.-4"), "parse(\"1.2.3.-4\") throws FormatException (sign)");
    Expect(ThrowsFormatException("1.2.3.+4"), "parse(\"1.2.3.+4\") throws FormatException (sign)");
    Expect(ThrowsFormatException("1..2.3"), "parse(\"1..2.3\") throws FormatException (empty segment)");
    Expect(ThrowsFormatException(".1.2.3"), "parse(\".1.2.3\") throws FormatException (leading dot)");
    Expect(ThrowsFormatException("1.2.3."), "parse(\"1.2.3.\") throws FormatException (trailing dot)");
    Expect(ThrowsFormatException(" 1.2.3.4"), "parse(\" 1.2.3.4\") throws FormatException (space)");
    Expect(ThrowsFormatException("1.2.3.4 "), "parse(\"1.2.3.4 \") throws FormatException (trailing space)");
    Expect(ThrowsFormatException("1.2.3.4a"), "parse(\"1.2.3.4a\") throws FormatException (trailing garbage)");
    Expect(ThrowsFormatException("1.2.3.4444"), "parse(\"1.2.3.4444\") throws FormatException (4 digits)");
    Expect(ThrowsFormatException("1.2.3.4.5.6"), "parse(\"1.2.3.4.5.6\") throws FormatException (6 segments)");
    Expect(ThrowsFormatException("0x100.0.0.1"), "parse(\"0x100.0.0.1\") throws FormatException (hex > 0xff)");
    Expect(ThrowsFormatException("1.2.65536"), "parse(\"1.2.65536\") throws FormatException (3-part tail > 16-bit)");
    Expect(ThrowsFormatException("1.16777216"), "parse(\"1.16777216\") throws FormatException (2-part tail > 24-bit)");
    Expect(ThrowsFormatException("4294967296"), "parse(\"4294967296\") throws FormatException (1-part > 32-bit)");
    Expect(ThrowsFormatException("1.2.3. 4"), "parse(\"1.2.3. 4\") throws FormatException (internal space)");
    Expect(ThrowsFormatException("+1.2.3.4"), "parse(\"+1.2.3.4\") throws FormatException (plus sign)");
    Expect(ThrowsFormatException("-1.2.3.4"), "parse(\"-1.2.3.4\") throws FormatException (minus sign)");
    Expect(ThrowsFormatException("1.2.3.4\t"), "parse(\"1.2.3.4\\t\") throws FormatException (tab)");
    Expect(il2cpp::runtime::il2cpp_ipaddress_parse(MakeString("bad"), nullptr) == 0,
           "parse(text, null buffer) == 0 (defensive)");
    Expect(ThrowsArgumentNull(nullptr),
           "parse(null) throws ArgumentNullException (net48)");
    {
        // net48 Parse THROWS on malformed input; a failed parse leaves the
        // caller's buffer untouched (the FormatException unwinds before any
        // write -- the write only happens on a fully-validated address).
        uint8_t sentinel[4] = {42, 43, 44, 45};
        bool threw = false;
        try
        {
            il2cpp::runtime::il2cpp_ipaddress_parse(MakeString("bad"), sentinel);
        }
        catch (const il2cpp::DotNetException&)
        {
            threw = true;
        }
        Expect(threw && sentinel[0] == 42 && sentinel[1] == 43
               && sentinel[2] == 44 && sentinel[3] == 45,
               "failed parse throws and leaves buffer untouched");
    }

    // --- 2. ToString: exact dotted-quad rebuild ---
    {
        const uint8_t bytes[] = {127, 0, 0, 1};
        Expect(StringEquals(il2cpp_ipaddress_to_string(bytes), "127.0.0.1"),
               "to_string(127.0.0.1) == \"127.0.0.1\"");
    }
    {
        const uint8_t bytes[] = {255, 255, 255, 255};
        Expect(StringEquals(il2cpp_ipaddress_to_string(bytes), "255.255.255.255"),
               "to_string(255.255.255.255) == \"255.255.255.255\"");
    }
    {
        const uint8_t bytes[] = {0, 0, 0, 0};
        Expect(StringEquals(il2cpp_ipaddress_to_string(bytes), "0.0.0.0"),
               "to_string(0.0.0.0) == \"0.0.0.0\"");
    }
    {
        const uint8_t bytes[] = {192, 168, 1, 1};
        Expect(StringEquals(il2cpp_ipaddress_to_string(bytes), "192.168.1.1"),
               "to_string(192.168.1.1) == \"192.168.1.1\"");
    }
    {
        il2cpp::Il2CppString* empty = il2cpp_ipaddress_to_string(nullptr);
        Expect(empty != nullptr && empty->length == 0,
               "to_string(null) == \"\" (empty managed string)");
    }

    // --- 3. Parse -> ToString round trip over every accepted form ---
    {
        const char* forms[] = {"127.0.0.1", "255.255.255.255", "0.0.0.0",
                               "10.20.30.40", "192.168.1.1", "8.8.8.8"};
        for (const char* form : forms)
        {
            uint8_t bytes[4];
            Expect(ParseInto(form, bytes) == 1
                   && StringEquals(il2cpp_ipaddress_to_string(bytes), form),
                   "parse/to_string round-trip");
        }
    }

    // --- 4. GetAddressBytes: managed byte[4] with the exact payload ---
    {
        const uint8_t bytes[] = {127, 0, 0, 1};
        il2cpp::Il2CppArray* arr = il2cpp_ipaddress_get_address_bytes(bytes);
        Expect(arr != nullptr && arr->length == 4 && arr->elemSize == 1,
               "get_address_bytes: byte[4] primitive array");
        Expect(ExpectBytes4(reinterpret_cast<const uint8_t*>(
                                il2cpp_array_get_elem_ptr(arr, 0)),
                            bytes),
               "get_address_bytes payload == 127.0.0.1");
    }
    {
        const uint8_t bytes[] = {255, 255, 255, 255};
        il2cpp::Il2CppArray* arr = il2cpp_ipaddress_get_address_bytes(bytes);
        Expect(arr != nullptr && arr->length == 4
               && ExpectBytes4(reinterpret_cast<const uint8_t*>(
                                   il2cpp_array_get_elem_ptr(arr, 0)),
                               bytes),
               "get_address_bytes payload == 255.255.255.255");
    }
    Expect(il2cpp_ipaddress_get_address_bytes(nullptr) == nullptr,
           "get_address_bytes(null) == nullptr");

    // --- 5. Dns.GetHostAddresses ---
    Expect(il2cpp_dns_get_host_addresses(nullptr) == nullptr,
           "dns(null) == nullptr");
    Expect(il2cpp_dns_get_host_addresses(MakeString("")) == nullptr,
           "dns(\"\") == nullptr");
    {
        const uint8_t loopback[] = {127, 0, 0, 1};
        il2cpp::Il2CppArray* addresses =
            il2cpp_dns_get_host_addresses(MakeString("localhost"));
        Expect(addresses != nullptr && addresses->length >= 1,
               "dns(\"localhost\") resolves to >= 1 address");
        Expect(addresses != nullptr && addresses->isRefElem
               && addresses->elemSize == static_cast<int32_t>(sizeof(void*)),
               "dns result is an object array (isRefElem, ptr elemSize)");
        Expect(HasAddress(addresses, loopback),
               "dns(\"localhost\") includes 127.0.0.1");
        if (addresses != nullptr)
        {
            for (int32_t i = 0; i < addresses->length; ++i)
            {
                il2cpp::Il2CppArray* element = DnsElement(addresses, i);
                Expect(element != nullptr && element->length == 4
                       && element->elemSize == 1,
                       "dns element is a byte[4]");
            }
        }
    }
    {
        const uint8_t loopback[] = {127, 0, 0, 1};
        il2cpp::Il2CppArray* addresses =
            il2cpp_dns_get_host_addresses(MakeString("127.0.0.1"));
        Expect(addresses != nullptr && addresses->length >= 1,
               "dns(\"127.0.0.1\") resolves to >= 1 address");
        Expect(HasAddress(addresses, loopback),
               "dns(\"127.0.0.1\") includes 127.0.0.1");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d ipaddr assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime ipaddr tests passed\n");
    return 0;
}
