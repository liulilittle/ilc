//------------------------------------------------------------------------------
// test_arr_conv_str.cpp -- icall_array_convert_all_str_i4 tests
// (Array.ConvertAll<string,int>(string[], Converter<string,int>)).
//
// Coverage (normal + edge + error paths):
//   1. Normal conversion -- {"1","22"} through a static int.Parse-like C
//      function delegate -> {1, 22}; the result is a NEW value-element
//      (int32) array and the source reference array is untouched (same
//      slots, same string content).
//   2. Parse semantics -- optional '-' sign, multi-digit values and zero:
//      {"-7","300","0"} -> {-7, 300, 0}.
//   3. A null string element rides the managed-ref slot into the callback
//      verbatim (the Parse-like leaf maps it to 0 -- no crash, no UB).
//   4. Empty array -- a non-null zero-length int32[] result.
//   5. INSTANCE-BOUND converter (captured-lambda shape): the bound payload
//      (a radix) is read through the `this` slot -- {"ff","10"} with a bound
//      base-16 parser -> {255, 16} (per-binding dispatch, InvokeConvertAll
//      StrI4's ThisFn arm).
//   6. GC probe -- the converter forces a collection on every element; the
//      rooted source (reference array), delegate and result survive a
//      mid-loop compaction and the result stays correct.
//   7. Error paths -- null array / null converter (ArgumentNullException),
//      non-array object (ArgumentException), int32 (non-reference) source
//      array (NotSupportedException), multicast container
//      (InvalidOperationException), non-delegate object (ArgumentException).
//
// Delegate construction follows the test_arrayext2.cpp convention: the
// methodPtr of a single-cast STATIC-method delegate is the plain
// Converter<string,int> ABI (int32_t (*)(Il2CppString*)); an instance-bound
// leaf receives its target in the first (`this`) parameter slot.
//
// Plain executable + minimal assertion harness (see test_array.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_array_ext2.h"
#include "il2cpp_delegate.h"
#include "il2cpp_delegate_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
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

void ExpectI32(int32_t actual, int32_t expected, const char* what)
{
    if (actual != expected)
    {
        std::printf("FAIL: %s (expected %d, got %d)\n", what, expected, actual);
        ++g_failures;
    }
}

// Runs 'fn' expecting a managed DotNetException whose type chain HEAD exactly
// equals 'expectedType' (only the head is asserted, see test_arrayext.cpp).
template <typename F>
void ExpectThrows(const char* expectedType, const char* what, F&& fn)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* thrown = e.type_name();
        const size_t len = std::strlen(expectedType);
        const bool headMatches = std::strncmp(thrown, expectedType, len) == 0 &&
                                 (thrown[len] == ',' || thrown[len] == '\0');
        if (headMatches)
        {
            return;
        }
        std::printf("FAIL: %s (threw %s, expected %s)\n", what, thrown, expectedType);
        ++g_failures;
        return;
    }
    std::printf("FAIL: %s (no exception thrown, expected %s)\n", what, expectedType);
    ++g_failures;
}

// A non-string, non-delegate managed object (for the caller-bug paths).
il2cpp::Il2CppClass g_mockElemClass = {
    "Test.ArrConvStrElem", // name
    24,                    // instanceSize
    0b00000010u,           // refFieldBitmap: slot 1 is a GC reference
    1,                     // refFieldCount
    nullptr,               // baseClass
    nullptr,               // vtable
    0,                     // vtableSize
    nullptr,               // interfaces
    0,                     // interfaceCount
};

// Managed exception type names (exact chain head, as thrown by arrayext2.cpp).
const char kArgNullType[]   = "System.ArgumentNullException";
const char kArgumentType[]  = "System.ArgumentException";
const char kNotSupportedType[] = "System.NotSupportedException";
const char kInvalidOpType[] = "System.InvalidOperationException";

//------------------------------------------------------------------------------
// The int.Parse-like converter leaves (single-cast static-method delegate
// ABI: int32_t (*)(Il2CppString*) -- the element rides a managed-ref slot,
// arrayext2.cpp ConvertAllStrI4Fn).
//------------------------------------------------------------------------------

// Digit parser over the UTF-16 payload: optional leading '-', then digits in
// 'radix' (0-9 / a-f / A-F). A null or empty string parses to 0 (the icall
// passes null elements through verbatim; int.Parse's exception behavior for
// malformed input is out of scope here -- the C leaf cannot throw managed
// exceptions).
int32_t ParseDigits(const char16_t* chars, int32_t length, int32_t radix)
{
    if (chars == nullptr || length <= 0)
    {
        return 0;
    }
    int32_t i = 0;
    bool neg = false;
    if (chars[0] == u'-')
    {
        neg = true;
        i = 1;
    }
    int32_t v = 0;
    for (; i < length; ++i)
    {
        const char16_t c = chars[i];
        int32_t d;
        if (c >= '0' && c <= '9')
        {
            d = c - '0';
        }
        else if (c >= 'a' && c <= 'f')
        {
            d = c - 'a' + 10;
        }
        else if (c >= 'A' && c <= 'F')
        {
            d = c - 'A' + 10;
        }
        else
        {
            break; // stop at the first non-digit (defensive)
        }
        v = v * radix + d;
    }
    return neg ? -v : v;
}

// Static decimal leaf ("int.Parse" shape): reads only the string payload.
int32_t CsParseDec(il2cpp::Il2CppString* s)
{
    if (s == nullptr)
    {
        return 0;
    }
    return ParseDigits(s->chars, s->length, 10);
}

// GC probe leaf: force a compaction BEFORE converting (the converter may
// allocate in net48 semantics; the rooted source/result must survive).
int32_t CsParseDecGc(il2cpp::Il2CppString* s)
{
    il2cpp::il2cpp_gc_collect();
    return CsParseDec(s);
}

//------------------------------------------------------------------------------
// Instance-bound converter (the captured-lambda delegate shape): the bound
// payload is the RADIX the instance method parses with -- proves the target
// arrives in the `this` slot and its payload is readable (arrayext2.cpp
// ConvertAllStrI4ThisFn dispatch arm).
//------------------------------------------------------------------------------
struct CsConvInst
{
    il2cpp::Il2CppObject obj; // object header
    int32_t radix;            // instance payload the bound method reads
};

il2cpp::Il2CppClass g_csConvClass = {
    "ArrConvStrTest.CsConvInst",           // name
    static_cast<int32_t>(sizeof(CsConvInst)), // instanceSize
    0u,                                    // refFieldBitmap: no reference fields
    0,                                     // refFieldCount
    nullptr,                               // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

bool g_csConvRegistered = false;

CsConvInst* NewCsConv(int32_t radix)
{
    if (!g_csConvRegistered)
    {
        il2cpp::il2cpp_class_register(&g_csConvClass);
        g_csConvRegistered = true;
    }
    CsConvInst* t = reinterpret_cast<CsConvInst*>(il2cpp::il2cpp_gc_alloc(&g_csConvClass));
    t->radix = radix;
    return t;
}

// Instance method with the generated-code instance ABI: arg0 = this.
int32_t CsConvInstParse(il2cpp::Il2CppObject* this_, il2cpp::Il2CppString* s)
{
    const int32_t radix = reinterpret_cast<CsConvInst*>(this_)->radix;
    return s != nullptr ? ParseDigits(s->chars, s->length, radix) : 0;
}

// Wrap a method pointer into a single-cast static delegate (generated ldftn +
// newobj DelegateCtor shape).
il2cpp::Il2CppDelegate* CsDel(void* fn)
{
    return il2cpp::il2cpp_delegate_new(fn);
}

// Wrap a method pointer + target into an instance-bound delegate (mirrors the
// transpiler's 2-arg emission: il2cpp_delegate_new(&M, obj)).
il2cpp::Il2CppDelegate* CsInstDel(void* fn, il2cpp::Il2CppObject* target)
{
    return il2cpp::il2cpp_delegate_new(fn, target);
}

// A string[] (reference array) holding the given ASCII literals; a nullptr
// entry in 'values' becomes a null element.
il2cpp::Il2CppArray* NewStringArray(const char* const* values, int32_t count)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 8, true, count);
    il2cpp::Il2CppObject** slots = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        slots[i] = values[i] != nullptr
                       ? reinterpret_cast<il2cpp::Il2CppObject*>(
                             il2cpp::il2cpp_string_new_from_cs(values[i]))
                       : nullptr;
    }
    return arr;
}

il2cpp::Il2CppArray* NewInt32Array(int32_t length)
{
    return il2cpp::il2cpp_array_new(nullptr, 4, false, length);
}

// Asserts that the result holds exactly the expected int32 content.
void ExpectInt32Content(il2cpp::Il2CppArray* arr, const int32_t* expected,
                        int32_t count, const char* what)
{
    const int32_t* data = reinterpret_cast<const int32_t*>(arr->data);
    bool ok = true;
    for (int32_t i = 0; i < count; ++i)
    {
        if (data[i] != expected[i])
        {
            ok = false;
            break;
        }
    }
    if (!ok)
    {
        std::printf("FAIL: %s (content: ", what);
        for (int32_t i = 0; i < count; ++i)
        {
            std::printf("%s%d", i > 0 ? " " : "", data[i]);
        }
        std::printf(")\n");
        ++g_failures;
    }
}

// Asserts that the source reference slots still hold their original string
// content (element-wise ASCII compare; a nullptr expectation = null slot).
void ExpectSourceUntouched(il2cpp::Il2CppArray* arr, const char* const* expected,
                           int32_t count, const char* what)
{
    const il2cpp::Il2CppObject* const* slots =
        reinterpret_cast<const il2cpp::Il2CppObject* const*>(arr->data);
    for (int32_t i = 0; i < count; ++i)
    {
        const il2cpp::Il2CppString* s =
            reinterpret_cast<const il2cpp::Il2CppString*>(slots[i]);
        if (expected[i] == nullptr)
        {
            if (s != nullptr)
            {
                std::printf("FAIL: %s (element %d: expected null slot)\n", what, i);
                ++g_failures;
            }
            continue;
        }
        if (s == nullptr || s->length != static_cast<int32_t>(std::strlen(expected[i])))
        {
            std::printf("FAIL: %s (element %d: length mismatch)\n", what, i);
            ++g_failures;
            continue;
        }
        bool same = true;
        for (int32_t u = 0; u < s->length; ++u)
        {
            if (static_cast<char>(s->chars[u]) != expected[i][u])
            {
                same = false;
                break;
            }
        }
        if (!same)
        {
            std::printf("FAIL: %s (element %d: content mismatch)\n", what, i);
            ++g_failures;
        }
    }
}

} // namespace

int main()
{
    // --- 1. Normal conversion: {"1","22"} through a static Parse-like leaf ---
    {
        const char* const src[] = { "1", "22" };
        il2cpp::Il2CppArray* a = NewStringArray(src, 2);
        il2cpp::Il2CppObject* conv =
            reinterpret_cast<il2cpp::Il2CppObject*>(CsDel(reinterpret_cast<void*>(&CsParseDec)));
        il2cpp::Il2CppArray* out = il2cpp::runtime::icall_array_convert_all_str_i4(a, conv);
        if (out == nullptr)
        {
            std::printf("FAIL: convert_all_str_i4 returns a non-null result\n");
            ++g_failures;
        }
        else
        {
            const int32_t expected[2] = { 1, 22 };
            ExpectInt32Content(out, expected, 2,
                               "convert_all_str_i4 maps {\"1\",\"22\"} through the Parse-like converter");
            // The result is a NEW value-element (int32) array.
            Expect(out != a, "convert_all_str_i4 allocates a fresh result array");
            ExpectI32(out->isRefElem ? 1 : 0, 0, "convert_all_str_i4 result is a value-element array");
            ExpectI32(out->elemSize, 4, "convert_all_str_i4 result elements are int32-sized");
            ExpectI32(out->length, 2, "convert_all_str_i4 result has the source length");
        }
        // The source reference array is never modified.
        ExpectSourceUntouched(a, src, 2, "convert_all_str_i4 leaves the source untouched");
    }

    // --- 2. Parse semantics: sign, multi-digit, zero ---
    {
        const char* const src[] = { "-7", "300", "0" };
        il2cpp::Il2CppArray* a = NewStringArray(src, 3);
        il2cpp::Il2CppObject* conv =
            reinterpret_cast<il2cpp::Il2CppObject*>(CsDel(reinterpret_cast<void*>(&CsParseDec)));
        il2cpp::Il2CppArray* out = il2cpp::runtime::icall_array_convert_all_str_i4(a, conv);
        const int32_t expected[3] = { -7, 300, 0 };
        ExpectInt32Content(out, expected, 3,
                           "convert_all_str_i4 handles sign, multi-digit and zero values");
    }

    // --- 3. A null string element rides the ref slot into the callback ---
    {
        const char* const src[] = { "42", nullptr };
        il2cpp::Il2CppArray* a = NewStringArray(src, 2);
        il2cpp::Il2CppObject* conv =
            reinterpret_cast<il2cpp::Il2CppObject*>(CsDel(reinterpret_cast<void*>(&CsParseDec)));
        il2cpp::Il2CppArray* out = il2cpp::runtime::icall_array_convert_all_str_i4(a, conv);
        const int32_t expected[2] = { 42, 0 };
        ExpectInt32Content(out, expected, 2,
                           "convert_all_str_i4 passes a null element to the converter without crashing");
    }

    // --- 4. Empty array ---
    {
        il2cpp::Il2CppArray* empty = NewStringArray(nullptr, 0);
        il2cpp::Il2CppObject* conv =
            reinterpret_cast<il2cpp::Il2CppObject*>(CsDel(reinterpret_cast<void*>(&CsParseDec)));
        il2cpp::Il2CppArray* out =
            il2cpp::runtime::icall_array_convert_all_str_i4(empty, conv);
        Expect(out != nullptr, "convert_all_str_i4 of an empty array returns a non-null result");
        ExpectI32(out != nullptr ? out->length : -1, 0,
                  "convert_all_str_i4 of an empty array returns a zero-length result");
    }

    // --- 5. Instance-bound converter: the bound radix rides the this slot ---
    {
        const char* const src[] = { "ff", "10" };
        il2cpp::Il2CppArray* a = NewStringArray(src, 2);
        CsConvInst* inst = NewCsConv(16); // bound payload: parse in base 16
        il2cpp::Il2CppObject* conv = reinterpret_cast<il2cpp::Il2CppObject*>(
            CsInstDel(reinterpret_cast<void*>(&CsConvInstParse),
                      reinterpret_cast<il2cpp::Il2CppObject*>(inst)));
        il2cpp::Il2CppArray* out = il2cpp::runtime::icall_array_convert_all_str_i4(a, conv);
        const int32_t expected[2] = { 255, 16 };
        ExpectInt32Content(out, expected, 2,
                           "convert_all_str_i4 instance-bound converter reads the bound radix from the this slot");
    }

    // --- 6. GC probe: a collection fires inside every converter call ---
    {
        const char* const src[] = { "5", "66" };
        il2cpp::Il2CppArray* a = NewStringArray(src, 2);
        il2cpp::Il2CppObject* conv =
            reinterpret_cast<il2cpp::Il2CppObject*>(CsDel(reinterpret_cast<void*>(&CsParseDecGc)));
        il2cpp::Il2CppArray* out = il2cpp::runtime::icall_array_convert_all_str_i4(a, conv);
        const int32_t expected[2] = { 5, 66 };
        ExpectInt32Content(out, expected, 2,
                           "convert_all_str_i4 survives a mid-loop collection (rooted source/result)");
        ExpectSourceUntouched(a, src, 2,
                              "convert_all_str_i4 source content survives the mid-loop collection");
    }

    // --- 7. Error paths ---
    {
        const char* const src[] = { "1", "22" };
        il2cpp::Il2CppArray* a = NewStringArray(src, 2);
        il2cpp::Il2CppObject* conv =
            reinterpret_cast<il2cpp::Il2CppObject*>(CsDel(reinterpret_cast<void*>(&CsParseDec)));

        // net48 validation order: array null first, then converter null.
        ExpectThrows(kArgNullType, "convert_all_str_i4 null array throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_convert_all_str_i4(nullptr, conv); });
        ExpectThrows(kArgNullType, "convert_all_str_i4 null converter throws ArgumentNullException",
                     [&] { il2cpp::runtime::icall_array_convert_all_str_i4(a, nullptr); });

        // A non-array object as the source is a caller bug -> ArgumentException.
        il2cpp::Il2CppObject* notAnArray = il2cpp::il2cpp_gc_alloc(&g_mockElemClass);
        ExpectThrows(kArgumentType, "convert_all_str_i4 non-array source throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_convert_all_str_i4(
                               reinterpret_cast<il2cpp::Il2CppArray*>(notAnArray), conv); });

        // An int32 (non-reference) source array -> NotSupportedException.
        il2cpp::Il2CppArray* i32Arr = NewInt32Array(2);
        ExpectThrows(kNotSupportedType, "convert_all_str_i4 on an int32 array throws NotSupportedException",
                     [&] { il2cpp::runtime::icall_array_convert_all_str_i4(i32Arr, conv); });

        // A multicast container is loudly rejected (its `first` field sits at
        // the methodPtr offset -- invoking it would call data as code).
        il2cpp::Il2CppObject* mc = il2cpp::runtime::icall_delegate_combine(
            reinterpret_cast<il2cpp::Il2CppObject*>(CsDel(reinterpret_cast<void*>(&CsParseDec))),
            reinterpret_cast<il2cpp::Il2CppObject*>(CsDel(reinterpret_cast<void*>(&CsParseDec))));
        ExpectThrows(kInvalidOpType, "convert_all_str_i4 multicast converter throws InvalidOperationException",
                     [&] { il2cpp::runtime::icall_array_convert_all_str_i4(a, mc); });

        // A non-delegate object as the converter is a caller bug -> ArgumentException.
        ExpectThrows(kArgumentType, "convert_all_str_i4 non-delegate converter throws ArgumentException",
                     [&] { il2cpp::runtime::icall_array_convert_all_str_i4(a, notAnArray); });
    }

    if (g_failures > 0)
    {
        std::printf("%d FAILURE(S)\n", g_failures);
        return 1;
    }
    std::printf("test_arr_conv_str: all checks passed\n");
    return 0;
}
