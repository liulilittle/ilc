//------------------------------------------------------------------------------
// string.cpp -- managed string creation (see il2cpp_string.h for the layout).
//
// The string descriptor is the runtime's built-in System.String type
// (System_String_class, metadata.cpp): a GC class with no reference fields.
// Its fixed instanceSize covers the header + length slot; every allocation
// goes through il2cpp_gc_alloc_size with the true header+payload extent, so
// the collector's block bookkeeping records the real boundary (sweep stays
// exact -- the string's payload bytes are never mistaken for another block).
//
// Creation paths:
//   * new_from_cs(ascii)    -- generated-code ldstr path: compile-time ASCII
//                              literal -> UTF-16 string.
//   * new_from_utf8(utf8)   -- native/icall boundary path.
//   * new(chars, length)    -- explicit UTF-16 payload.
//
// M3 scope: creation only. String OPERATIONS (Concat/Substring/Compare/...)
// are BCL methods transpiled against this layout in the BCL milestone; the
// runtime exposes the raw primitives here.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"

#include "il2cpp_allocator.h"
#include "il2cpp_array.h"
#include "il2cpp_encoding.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_runtime.h"

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>

namespace il2cpp {

namespace {

// The System.String descriptor is the BUILTIN System_String_class
// (metadata.cpp, extern in il2cpp_metadata.h) -- the SAME descriptor the
// transpiler's generated isinst/castclass code compares against
// (&System_String_class) and the registered descriptor that
// il2cpp_class_by_name("System.String") resolves. A file-local duplicate
// descriptor would make every string isinst/castclass miss (the generated
// code never references it). instanceSize = header + length slot, aligned to
// the 8-byte grid (sizeof(Il2CppString) already includes the trailing
// chars[1] placeholder; the allocator treats it as the fixed base).
// Registered (with all builtin System.* descriptors) through
// il2cpp_ensure_builtin_system_classes(), lazily on the first string
// allocation (constraint #12: no global-constructor allocation).
void EnsureStringRegistered()
{
    // Idempotent: registers System.Object/String/Int32/Boolean/Char/Double/
    // IConvertible/IComparable/IFormattable exactly once.
    il2cpp_ensure_builtin_system_classes();
}

// Total allocation size for a string of 'length' units:
// header + length slot + length units, pointer-aligned. The chars[1]
// placeholder accounts for one unit, so the extra bytes are (length-1)*2.
// Total allocation size: the true payload extent -- offsetof(chars) +
// length * sizeof(char16_t). sizeof(Il2CppString) is NOT a safe base: the
// struct rounds up to alignment (char16_t chars[1] stub at non-zero offset),
// which under-allocates and would let the block bookkeeping record a smaller
// boundary than the caller believes (mirrors the array.cpp C4 fix).
size_t StringAllocSize(int32_t length)
{
    const size_t base = offsetof(Il2CppString, chars);
    const size_t extra = static_cast<size_t>(length) * sizeof(char16_t);
    return base + extra;
}

//------------------------------------------------------------------------------
// UTF-8 decoding with .NET Framework replacement-fallback semantics
// (verified against a net48 reference run, 2026-08-03).
//
// Valid sequences: 2-byte C2-DF, 3-byte E0-EF, 4-byte F0-F4. The lead byte
// constrains its first continuation: E0 requires 0xA0-0xBF, ED requires
// 0x80-0x9F (excludes the surrogate range), F0 requires 0x90-0xBF, F4
// requires 0x80-0x8F (caps at U+10FFFF); C0/C1 and F5-FF are never valid
// leads, and a lone continuation byte is invalid.
//
// Fallback counts (net48, NOT the byte-per-byte behavior of newer runtimes):
//   * lead + out-of-range continuation   -> ONE U+FFFD, consumes 2 bytes
//   * lead + non-continuation byte       -> ONE U+FFFD, consumes 1 byte
//   * truncated at end of input          -> ONE U+FFFD for the whole sequence
//   * every other invalid byte           -> ONE U+FFFD per byte
//------------------------------------------------------------------------------

// Is 'c' a UTF-8 continuation byte (10xxxxxx)?
bool IsContinuationByte(unsigned char c)
{
    return (c & 0xC0) == 0x80;
}

// Decode one UTF-8 sequence at data[i] into 'units'. Returns the number of
// input bytes consumed; invalid sequences append exactly one U+FFFD.
size_t DecodeUtf8Sequence(const char* data, size_t length, size_t i,
                          std::vector<char16_t>& units)
{
    const unsigned char c = static_cast<unsigned char>(data[i]);
    if (c < 0x80)
    {
        units.push_back(static_cast<char16_t>(c));
        return 1;
    }

    size_t seqLen = 0; // expected sequence length from the lead byte
    if (c >= 0xC2 && c <= 0xDF)
    {
        seqLen = 2;
    }
    else if (c >= 0xE0 && c <= 0xEF)
    {
        seqLen = 3;
    }
    else if (c >= 0xF0 && c <= 0xF4)
    {
        seqLen = 4;
    }
    if (seqLen == 0)
    {
        units.push_back(0xFFFD); // lone continuation / invalid lead (C0,C1,F5-FF)
        return 1;
    }

    const unsigned char b1 = (i + 1 < length)
        ? static_cast<unsigned char>(data[i + 1]) : 0x00;
    if (i + 1 >= length || !IsContinuationByte(b1))
    {
        units.push_back(0xFFFD); // lead truncated or followed by a non-continuation
        return 1;
    }
    if ((c == 0xE0 && b1 < 0xA0) || (c == 0xED && b1 > 0x9F)
        || (c == 0xF0 && b1 < 0x90) || (c == 0xF4 && b1 > 0x8F))
    {
        units.push_back(0xFFFD); // overlong / surrogate / out-of-range second byte
        return 2;
    }

    size_t consumed = 2; // lead + b1 are valid so far
    for (size_t k = 2; k < seqLen; ++k)
    {
        if (i + k >= length
            || !IsContinuationByte(static_cast<unsigned char>(data[i + k])))
        {
            units.push_back(0xFFFD); // truncated / invalid continuation
            return consumed;
        }
        ++consumed;
    }

    if (seqLen == 2)
    {
        units.push_back(static_cast<char16_t>(
            ((c & 0x1Fu) << 6) | (b1 & 0x3Fu)));
        return 2;
    }
    const unsigned char b2 = static_cast<unsigned char>(data[i + 2]);
    if (seqLen == 3)
    {
        units.push_back(static_cast<char16_t>(
            ((c & 0x0Fu) << 12) | ((b1 & 0x3Fu) << 6) | (b2 & 0x3Fu)));
        return 3;
    }
    const unsigned char b3 = static_cast<unsigned char>(data[i + 3]);
    const uint32_t cp = (static_cast<uint32_t>(c & 0x07u) << 18)
        | (static_cast<uint32_t>(b1 & 0x3Fu) << 12)
        | (static_cast<uint32_t>(b2 & 0x3Fu) << 6)
        | static_cast<uint32_t>(b3 & 0x3Fu);
    const uint32_t v = cp - 0x10000u;
    units.push_back(static_cast<char16_t>(0xD800u + (v >> 10)));
    units.push_back(static_cast<char16_t>(0xDC00u + (v & 0x3FFu)));
    return 4;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_string_new -- explicit UTF-16 payload (see header for contract).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_string_new(const char16_t* chars, int32_t length)
{
    EnsureStringRegistered();
    if (length < 0)
    {
        length = 0; // defensive: negative length is a caller bug
    }
    Il2CppString* s = reinterpret_cast<Il2CppString*>(il2cpp_gc_alloc_size(&System_String_class, StringAllocSize(length)));
    s->length = length;
    if (length > 0 && chars != nullptr)
    {
        std::memcpy(s->chars, chars, static_cast<size_t>(length) * sizeof(char16_t));
    }
    return s;
}

//------------------------------------------------------------------------------
// il2cpp_string_new_from_utf8 -- UTF-8 -> managed string (see header).
//
//   Full UTF-8 decoding (1-4 byte sequences), aligned with the .NET
//   Framework UTF8Encoding replacement fallback (see DecodeUtf8Sequence):
//   valid BMP sequences become one unit, astral sequences a surrogate pair,
//   and invalid sequences one or more U+FFFD per the net48 reference. The
//   input must be null-terminated.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_string_new_from_utf8_n(const char* utf8, int32_t byteLen)
{
    EnsureStringRegistered();
    if (utf8 == nullptr || byteLen <= 0)
    {
        return il2cpp_string_new(nullptr, 0);
    }
    const size_t n = static_cast<size_t>(byteLen);
    std::vector<char16_t> units;
    units.reserve(n); // upper bound: one unit per ASCII byte
    size_t i = 0;
    while (i < n)
    {
        i += DecodeUtf8Sequence(utf8, n, i, units);
    }
    return il2cpp_string_new(units.data(), static_cast<int32_t>(units.size()));
}

Il2CppString* il2cpp_string_new_from_utf8(const char* utf8)
{
    if (utf8 == nullptr)
    {
        EnsureStringRegistered();
        return il2cpp_string_new(nullptr, 0);
    }
    return il2cpp_string_new_from_utf8_n(utf8, static_cast<int32_t>(std::strlen(utf8)));
}

//------------------------------------------------------------------------------
// il2cpp_string_new_from_cs -- ASCII literal -> managed string (generated
// code path). Each byte 0x00-0x7F becomes one UTF-16 unit; bytes >= 0x80 are
// skipped defensively (the transpiler emits ASCII-only literals).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_string_new_from_cs(const char* ascii)
{
    EnsureStringRegistered();
    if (ascii == nullptr)
    {
        return il2cpp_string_new(nullptr, 0);
    }
    const size_t byteLen = std::strlen(ascii);
    int32_t unitCount = 0;
    for (size_t i = 0; i < byteLen; ++i)
    {
        if (static_cast<unsigned char>(ascii[i]) < 0x80) ++unitCount;
    }
    Il2CppString* s = reinterpret_cast<Il2CppString*>(
        il2cpp_gc_alloc_size(&System_String_class, StringAllocSize(unitCount)));
    s->length = unitCount;
    int32_t o = 0;
    for (size_t i = 0; i < byteLen; ++i)
    {
        const unsigned char c = static_cast<unsigned char>(ascii[i]);
        if (c < 0x80) s->chars[o++] = static_cast<char16_t>(c);
    }
    return s;
}

//------------------------------------------------------------------------------
// il2cpp_string_class -- the System.String descriptor (lazily registered).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_string_class()
{
    EnsureStringRegistered();
    return &System_String_class;
}

} // namespace il2cpp

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// String allocation roots (see il2cpp_string.h -- Core FCall gStringFuncs).
// Every function follows the create-style icall convention (a discarded
// newobj receiver + the same single-allocation discipline as stringext2.cpp):
// managed payloads are staged into NON-GC scratch BEFORE the one
// il2cpp_string_new, so a mid-call collection can never reclaim the source.
//------------------------------------------------------------------------------

namespace {

// Stage 'count' UTF-16 units read from a char[] payload into non-GC scratch
// and return the fresh string (the .ctor(char[]) / .ctor(char[],int,int)
// content path). 'base' is the array payload address, 'elemSize' its stride.
Il2CppString* StringFromCharArray(const char* base, int32_t elemSize,
                                  int32_t count, int32_t startIndex)
{
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(static_cast<size_t>(count) * sizeof(char16_t)));
    for (int32_t i = 0; i < count; ++i)
    {
        const char* slot = base
            + static_cast<size_t>(startIndex + i) * static_cast<size_t>(elemSize);
        scratch[i] = *reinterpret_cast<const char16_t*>(slot);
    }
    Il2CppString* result = il2cpp_string_new(scratch, count);
    alloc::il2cpp_free_fast(scratch);
    return result;
}

} // namespace

// FastAllocateString(int32_t length): allocate 'length' UTF-16 units with NO
// content fill (the collector zero-initializes the payload). The managed
// allocation root every string ctor builds on; a negative length is clamped
// to 0 (defensive, matching il2cpp_string_new).
Il2CppObject* icall_string_fast_allocate(int32_t length)
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, length));
}

// System.String..ctor(char[]): copy the whole array. null/empty -> the empty
// string (net48 probe: carr_null and carr_empty both RET:"").
Il2CppObject* icall_string_ctor_char_array(Il2CppObject* this_, Il2CppObject* value)
{
    (void)this_; // the newobj placeholder is a discarded allocation
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(value);
    if (arr == nullptr || arr->length == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    Il2CppString* result = StringFromCharArray(arr->data, arr->elemSize,
                                               arr->length, 0);
    return reinterpret_cast<Il2CppObject*>(result);
}

// System.String..ctor(char[], int32_t, int32_t): copy a window. net48 order
// (probe-verified): null -> ANE; startIndex < 0 / length < 0 -> AOO;
// startIndex > value.Length - length -> AOO; length == 0 -> empty.
Il2CppObject* icall_string_ctor_char_array_range(Il2CppObject* this_,
    Il2CppObject* value, int32_t startIndex, int32_t length)
{
    (void)this_;
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(value);
    if (startIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("length", nullptr);
    }
    if (startIndex > arr->length - length)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (length == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    Il2CppString* result = StringFromCharArray(arr->data, arr->elemSize,
                                               length, startIndex);
    return reinterpret_cast<Il2CppObject*>(result);
}

// System.String..ctor(char*): wcslen-style copy of a null-terminated UTF-16
// buffer. null -> empty (probe cptr_null RET:"").
Il2CppObject* icall_string_ctor_char_ptr(Il2CppObject* this_, const char16_t* value)
{
    (void)this_;
    if (value == nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    int32_t count = 0;
    while (value[count] != 0)
    {
        ++count;
    }
    if (count == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(value, count));
}

// System.String..ctor(char*, int32_t, int32_t): copy 'length' units from
// value+startIndex (NO null scan -- net48 copies exactly the window).
// net48 order (probe-verified): length < 0 -> AOO; startIndex < 0 -> AOO;
// address wrap -> AOO; length == 0 -> empty (checked BEFORE the null test);
// null value -> AOO("value").
Il2CppObject* icall_string_ctor_char_ptr_range(Il2CppObject* this_,
    const char16_t* value, int32_t startIndex, int32_t length)
{
    (void)this_;
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("length", nullptr);
    }
    if (startIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (value != nullptr && value + startIndex < value)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (length == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    if (value == nullptr)
    {
        il2cpp_throw_argument_out_of_range_exception("value", nullptr);
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(value + startIndex, length));
}

// System.String..ctor(sbyte*): decode a null-terminated byte string as UTF-8
// (net48 uses the ANSI code page -- documented deviation). null -> empty.
Il2CppObject* icall_string_ctor_sbyte_ptr(Il2CppObject* this_, const char* value)
{
    (void)this_;
    if (value == nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    const size_t byteLen = std::strlen(value);
    if (byteLen == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_encoding_get_chars(
        reinterpret_cast<const uint8_t*>(value), static_cast<int32_t>(byteLen),
        il2cpp_encoding_code_page::utf8));
}

// System.String..ctor(sbyte*, int32_t, int32_t): decode 'length' bytes from
// value+startIndex as UTF-8. net48 order (probe-verified): startIndex < 0 ->
// AOO; length < 0 -> AOO; value == null -> ANE UNCONDITIONALLY (even for
// length 0 -- differs from the char* form); address wrap -> AOO; length == 0
// -> empty.
Il2CppObject* icall_string_ctor_sbyte_ptr_range(Il2CppObject* this_,
    const char* value, int32_t startIndex, int32_t length)
{
    (void)this_;
    if (startIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("length", nullptr);
    }
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    if (value + startIndex < value)
    {
        il2cpp_throw_argument_out_of_range_exception("value", nullptr);
    }
    if (length == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_encoding_get_chars(
        reinterpret_cast<const uint8_t*>(value + startIndex), length,
        il2cpp_encoding_code_page::utf8));
}

// System.String..ctor(sbyte*, int32_t, int32_t, Encoding): decode 'length'
// bytes from value+startIndex with an explicit code page. encoding == 0
// (null Encoding object) delegates to the 3-arg form. net48 order (non-null
// encoding, probe-verified): length < 0 -> AOO; startIndex < 0 -> AOO;
// value == null -> length == 0 ? empty : AOO("value"); address wrap -> AOO;
// then decode via il2cpp_encoding_get_chars (unknown page -> ArgumentException).
Il2CppObject* icall_string_ctor_sbyte_ptr_range_encoding(Il2CppObject* this_,
    const char* value, int32_t startIndex, int32_t length, int32_t encoding)
{
    (void)this_;
    if (encoding == 0)
    {
        return icall_string_ctor_sbyte_ptr_range(this_, value, startIndex, length);
    }
    if (length < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("length", nullptr);
    }
    if (startIndex < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (value == nullptr)
    {
        if (length == 0)
        {
            return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
        }
        il2cpp_throw_argument_out_of_range_exception("value", nullptr);
    }
    if (value + startIndex < value)
    {
        il2cpp_throw_argument_out_of_range_exception("startIndex", nullptr);
    }
    if (length == 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_encoding_get_chars(
        reinterpret_cast<const uint8_t*>(value + startIndex), length,
        static_cast<il2cpp_encoding_code_page>(encoding)));
}

//------------------------------------------------------------------------------
// icall_string_ctor_char_span -- new string(ReadOnlySpan<char>) (the net5+
// span ctor; raw sig 20-01-01-15-11-<token>-01-03). The span rides as a
// caller SLOT with the standard two-field local-storage layout
// { const char16_t* ptr; int32_t length } (the ValueTuple-style value-slot
// model). Create-style: the receiver is dropped. null/empty -> the empty
// string (the .NET default-span contract).
//------------------------------------------------------------------------------
Il2CppObject* icall_string_ctor_char_span(Il2CppObject* this_, const void* span)
{
    (void)this_;
    if (span == nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    const char16_t* ptr = nullptr;
    int32_t length = 0;
    std::memcpy(&ptr, span, sizeof(ptr));
    std::memcpy(&length, reinterpret_cast<const uint8_t*>(span) + sizeof(void*), sizeof(length));
    if (ptr == nullptr || length <= 0)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0));
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new(ptr, length));
}

} // namespace runtime
} // namespace il2cpp


