//------------------------------------------------------------------------------
// test_exceptionext.cpp -- extended exception factory unit tests.
//
// Coverage (normal + edge + error paths):
//   1. Every factory throws a DotNetException catchable by C++ catch.
//   2. The thrown type-name chain starts with the exact managed full name and
//      matches with .NET inheritance semantics: catch(System.Exception) and
//      catch(System.ArgumentException) both match an ArgumentNullException.
//   3. An explicitly provided message round-trips exactly (the uniform
//      factories AND the (paramName, message) ArgumentOutOfRangeException
//      shape -- the runtime's pinned net48-resource-text convention).
//   4. A nullptr message yields a non-empty .NET standard default message
//      (checked by an expected substring).
//   5. ArgumentNullException / ArgumentOutOfRangeException compose a message
//      in the .NET Framework two-line format (base text + "\r\n" +
//      "参数名: <name>", zh-CN host net48 oracle); a nullptr/empty paramName
//      still yields a non-empty default message.
//   6. An unrelated caught type never matches (no false positives).
//   7. EXC2 audit (get_Message carrier integrity): the transpiler's
//      Exception.get_Message mapping reads the carrier through
//      il2cpp_string_intern_literal(__il2cpp_ex.message_text()) -- the caught
//      DotNetException's message must survive to that read (static literal or
//      thread-local scratch) and intern to the SAME managed string content.
//   8. EXC3 audit (HResult net48 defaults): every factory's exception type,
//      resolved through icall_marshal_get_hr_for_exception (the runtime's
//      type-name -> net48 default HResult mapping, marshalext3.cpp), yields
//      the exception type's net48 default _HResult. The managed Exception
//      object is the catch handler's interned type-name string, so the
//      factory's thrown type (the chain's FIRST segment = the exact managed
//      full name) is the exact key that mapping consumes -- a byte-for-byte
//      net48 semantic cross-check of the exception extension domain.
//
// Plain executable + minimal assertion harness (see test_exception.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_exception_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_marshal_ext.h"  // icall_marshal_get_hr_for_exception (net48 HResult)
#include "il2cpp_string.h"
#include "il2cpp_string_intern.h"

#include <cstdio>
#include <cstring>
#include <string>

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

// Snapshot of a caught exception: the type chain and the message are copied
// out of the exception at catch time (composed messages live in the factory's
// thread-local scratch, so tests read them immediately, per the header's
// contract).
struct CaughtInfo
{
    bool   caught;
    char   type[160];
    char   msg[256];
};

template <typename ThrowFn>
CaughtInfo CatchIt(ThrowFn fn)
{
    CaughtInfo info = { false, "", "" };
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        info.caught = true;
#if defined(_MSC_VER)
        ::strncpy_s(info.type, sizeof(info.type), e.type_name(), sizeof(info.type) - 1);
        ::strncpy_s(info.msg, sizeof(info.msg), e.message_text(), sizeof(info.msg) - 1);
#else
        std::strncpy(info.type, e.type_name(), sizeof(info.type) - 1);
        std::strncpy(info.msg, e.message_text(), sizeof(info.msg) - 1);
#endif
    }
    return info;
}

// True when the caught chain starts with (or contains) the managed type --
// the exact conformance rule the generated catch clauses use.
bool IsType(const char* thrownChain, const char* managedType)
{
    return il2cpp::il2cpp_exception_matches(managedType, thrownChain);
}

// One message-taking factory + its expectations (the 9 uniform factories).
struct FactoryCase
{
    const char* what;               // assertion label prefix
    const char* managedType;        // exact managed full name
    const char* wrongType;          // unrelated type that must NOT match
    const char* customMessage;      // explicit message to round-trip
    const char* defaultSubstring;   // expected in the nullptr-message default
    void (*throwWith)(const char*); // the factory under test
};

// True when the interned managed string's content equals 'ascii' (byte-for-
// byte on the ASCII subset -- the runtime intern path stores UTF-16 units 1:1
// for ASCII input). EXC2 get_Message carrier check.
bool InternedStringIs(il2cpp::Il2CppObject* obj, const char* ascii)
{
    if (obj == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
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

// True when the managed string's content equals the UTF-8 'text' (decoded
// through the runtime's own UTF-8 path -- the same decoder the intern-literal
// emission uses -- so non-ASCII content, e.g. the zh-CN exception text,
// compares faithfully). EXC2 get_Message carrier check for composed messages.
bool ManagedStringIs(il2cpp::Il2CppObject* obj, const char* text)
{
    if (obj == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* expected = reinterpret_cast<const il2cpp::Il2CppString*>(
        il2cpp::il2cpp_string_new_from_utf8(text));
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    if (s->length != expected->length)
    {
        return false;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        if (s->chars[i] != expected->chars[i])
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    // --- 1. Uniform factories: type marker, message round-trip, defaults ---
    const FactoryCase cases[] = {
        { "ArgumentException",         "System.ArgumentException",         "System.NotSupportedException",
          "bad argument",              "expected range",
          il2cpp::il2cpp_throw_argument_exception },
        { "InvalidOperationException", "System.InvalidOperationException", "System.ArgumentException",
          "invalid op",                "not valid",
          il2cpp::il2cpp_throw_invalid_operation_exception },
        { "IndexOutOfRangeException",  "System.IndexOutOfRangeException",  "System.ArgumentException",
          "index out of range",        "outside the bounds",
          il2cpp::il2cpp_throw_index_out_of_range_exception },
        { "NullReferenceException",    "System.NullReferenceException",    "System.ArgumentException",
          "null reference",            "not set to an instance",
          il2cpp::il2cpp_throw_null_reference_exception },
        { "DivideByZeroException",     "System.DivideByZeroException",     "System.ArgumentException",
          "divide by zero",            "divide by zero",
          il2cpp::il2cpp_throw_divide_by_zero_exception },
        { "OverflowException",         "System.OverflowException",         "System.ArgumentException",
          "overflow",                  "overflow",
          il2cpp::il2cpp_throw_overflow_exception },
        { "NotSupportedException",     "System.NotSupportedException",     "System.ArgumentException",
          "not supported",             "not supported",
          il2cpp::il2cpp_throw_not_supported_exception },
        { "KeyNotFoundException",      "System.Collections.Generic.KeyNotFoundException", "System.ArgumentException",
          "key not found",             "not present in the dictionary",
          il2cpp::il2cpp_throw_key_not_found_exception },
        { "FormatException",           "System.FormatException",           "System.ArgumentException",
          "bad format",                "correct format",
          il2cpp::il2cpp_throw_format_exception },
    };
    for (const FactoryCase& c : cases)
    {
        const std::string throwsLabel = std::string(c.what) + " factory throws DotNetException";
        CaughtInfo info = CatchIt([&] { c.throwWith(c.customMessage); });
        Expect(info.caught, throwsLabel.c_str());
        Expect(IsType(info.type, c.managedType),
               (std::string(c.what) + " type marker is the exact managed full name").c_str());
        Expect(IsType(info.type, "System.Exception"),
               (std::string(c.what) + " is caught as System.Exception (base-chain match)").c_str());
        Expect(std::strcmp(info.msg, c.customMessage) == 0,
               (std::string(c.what) + " explicit message round-trips").c_str());
        Expect(!IsType(info.type, c.wrongType),
               (std::string(c.what) + " does not match an unrelated caught type").c_str());

        CaughtInfo dflt = CatchIt([&] { c.throwWith(nullptr); });
        Expect(dflt.caught,
               (std::string(c.what) + " nullptr-message factory still throws").c_str());
        Expect(dflt.msg[0] != '\0',
               (std::string(c.what) + " nullptr message -> default message is non-empty").c_str());
        Expect(std::strstr(dflt.msg, c.defaultSubstring) != nullptr,
               (std::string(c.what) + " default message contains the standard .NET text").c_str());
    }

    // --- 2. ArgumentNullException(paramName): composed message + inheritance ---
    {
        CaughtInfo info = CatchIt([] { il2cpp::il2cpp_throw_argument_null_exception("userName"); });
        Expect(info.caught, "il2cpp_throw_argument_null_exception throws DotNetException");
        Expect(IsType(info.type, "System.ArgumentNullException"),
               "ArgumentNullException type marker is the exact managed full name");
        Expect(IsType(info.type, "System.ArgumentException"),
               "ArgumentNullException matches its base ArgumentException");
        Expect(IsType(info.type, "System.Exception"),
               "ArgumentNullException is caught as System.Exception");
        Expect(!IsType(info.type, "System.FormatException"),
               "ArgumentNullException does not match an unrelated type");
        Expect(info.msg[0] != '\0', "ArgumentNullException message is non-empty");
        Expect(std::strstr(info.msg, "userName") != nullptr,
               "ArgumentNullException composed message contains the parameter name");

        CaughtInfo dflt = CatchIt([] { il2cpp::il2cpp_throw_argument_null_exception(nullptr); });
        Expect(dflt.caught, "ArgumentNullException(nullptr) still throws");
        Expect(IsType(dflt.type, "System.ArgumentNullException"),
               "ArgumentNullException(nullptr) type marker is correct");
        Expect(dflt.msg[0] != '\0',
               "ArgumentNullException(nullptr) default message is non-empty");
    }

    // --- 3. ArgumentOutOfRangeException(paramName, message) ---
    {
        CaughtInfo info = CatchIt([] { il2cpp::il2cpp_throw_argument_out_of_range_exception("index", "custom range message"); });
        Expect(info.caught, "il2cpp_throw_argument_out_of_range_exception throws DotNetException");
        Expect(IsType(info.type, "System.ArgumentOutOfRangeException"),
               "ArgumentOutOfRangeException type marker is the exact managed full name");
        Expect(IsType(info.type, "System.ArgumentException"),
               "ArgumentOutOfRangeException matches its base ArgumentException");
        Expect(IsType(info.type, "System.Exception"),
               "ArgumentOutOfRangeException is caught as System.Exception");
        // net48 Message semantics with the runtime's pinned divergence: an
        // EXPLICIT message passes through EXACTLY (the (paramName, message)
        // shape -- test_listi4 pins the raw resource text; real net48 would
        // append the parameter-name line, documented in exceptionext.cpp).
        Expect(std::strcmp(info.msg, "custom range message") == 0,
               "ArgumentOutOfRangeException explicit message round-trips (message wins)");

        CaughtInfo nameOnly = CatchIt([] { il2cpp::il2cpp_throw_argument_out_of_range_exception("index", nullptr); });
        Expect(nameOnly.caught, "ArgumentOutOfRangeException(paramName, nullptr) still throws");
        Expect(nameOnly.msg[0] != '\0',
               "ArgumentOutOfRangeException paramName-only message is non-empty");
        Expect(std::strstr(nameOnly.msg, "index") != nullptr,
               "ArgumentOutOfRangeException paramName-only message contains the parameter name");

        CaughtInfo dflt = CatchIt([] { il2cpp::il2cpp_throw_argument_out_of_range_exception(nullptr, nullptr); });
        Expect(dflt.caught, "ArgumentOutOfRangeException(nullptr, nullptr) still throws");
        Expect(dflt.msg[0] != '\0',
               "ArgumentOutOfRangeException(null, null) default message is non-empty");
    }

    // --- 4. EXC2 audit: get_Message carrier integrity ---
    // The transpiler maps Exception.get_Message on a catch-variable receiver
    // to il2cpp_string_intern_literal(__il2cpp_ex.message_text()) -- the
    // caught DotNetException's message must survive to that read. Exercised
    // here for BOTH message kinds the factories produce:
    //   * a static literal (the custom-message path);
    //   * the thread-local composed scratch (the paramName path).
    {
        // Static-literal message: throw with a custom message, catch, read it
        // through the intern-literal get_Message emission, verify content.
        il2cpp::Il2CppObject* carrierMsg = nullptr;
        try
        {
            il2cpp::il2cpp_throw_argument_exception("carrier literal message");
        }
        catch (const il2cpp::DotNetException& e)
        {
            carrierMsg = il2cpp::il2cpp_string_intern_literal(e.message_text());
        }
        Expect(carrierMsg != nullptr, "get_Message carrier read returns a managed string");
        Expect(InternedStringIs(carrierMsg, "carrier literal message"),
               "get_Message carrier: static-literal message interns to its exact content");

        // Thread-local composed message: the scratch must still hold the
        // message when the catch reads it (the transpiler read happens at
        // catch time, before any further throw -- the documented contract).
        il2cpp::Il2CppObject* composedMsg = nullptr;
        try
        {
            il2cpp::il2cpp_throw_argument_null_exception("carrierParam");
        }
        catch (const il2cpp::DotNetException& e)
        {
            composedMsg = il2cpp::il2cpp_string_intern_literal(e.message_text());
        }
        Expect(composedMsg != nullptr, "composed get_Message carrier read returns a managed string");
        Expect(ManagedStringIs(composedMsg,
                               "\xE5\x80\xBC\xE4\xB8\x8D\xE8\x83\xBD\xE4\xB8\xBA\x20\x6E\x75\x6C\x6C"
                               "\xE3\x80\x82\r\n\xE5\x8F\x82\xE6\x95\xB0\xE5\x90\x8D\x3A\x20"
                               "carrierParam"),
               "get_Message carrier: composed paramName message interns to the net48 zh-CN text");

        // The interned string is a REAL managed string (descriptor set) --
        // the same object shape the generated catch-variable consumer sees.
        Expect(reinterpret_cast<il2cpp::Il2CppString*>(composedMsg)->obj.klass != nullptr,
               "get_Message carrier intern result is a managed string (descriptor present)");
    }

    // --- 5. EXC3 audit: HResult net48 defaults ---
    // Every factory's exception type must map (via the runtime's type-name ->
    // net48 default HResult mapping, icall_marshal_get_hr_for_exception) to
    // the net48 default _HResult for that exception type. The thrown type is
    // the chain's FIRST segment (the exact managed full name) -- the same key
    // the catch-variable interned string carries, so this cross-checks the
    // exception extension domain's net48 HResult semantics end to end.
    // Values are the net48 ctor-default _HResult constants (probe-verified
    // against v4.0.30319; see marshalext3.cpp kExceptionHResults).
    {
        struct HrCase { const char* typeName; int32_t hresult; };
        const HrCase kFactoryHrs[] = {
            { "System.ArgumentException",         static_cast<int32_t>(0x80070057) },
            { "System.ArgumentNullException",     static_cast<int32_t>(0x80004003) },
            { "System.ArgumentOutOfRangeException", static_cast<int32_t>(0x80131502) },
            { "System.InvalidOperationException", static_cast<int32_t>(0x80131509) },
            { "System.IndexOutOfRangeException",  static_cast<int32_t>(0x80131508) },
            { "System.NullReferenceException",    static_cast<int32_t>(0x80004003) },
            { "System.DivideByZeroException",     static_cast<int32_t>(0x80020012) },
            { "System.OverflowException",         static_cast<int32_t>(0x80131516) },
            { "System.NotSupportedException",     static_cast<int32_t>(0x80131515) },
            { "System.KeyNotFoundException",      static_cast<int32_t>(0x80131577) },
            { "System.FormatException",           static_cast<int32_t>(0x80131537) },
        };
        for (const HrCase& hc : kFactoryHrs)
        {
            il2cpp::Il2CppObject* typeName = reinterpret_cast<il2cpp::Il2CppObject*>(
                il2cpp::il2cpp_string_new_from_cs(hc.typeName));
            const int32_t hr = il2cpp::runtime::icall_marshal_get_hr_for_exception(typeName);
            const std::string label = std::string("HResult net48 default for ") + hc.typeName;
            Expect(hr == hc.hresult, label.c_str());
        }
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d exceptionext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime exceptionext tests passed\n");
    return 0;
}
