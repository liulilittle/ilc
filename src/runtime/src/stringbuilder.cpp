//------------------------------------------------------------------------------
// stringbuilder.cpp -- System.Text.StringBuilder runtime semantics (see
// il2cpp_stringbuilder.h for the layout, GC contract and semantics list).
//
// The builder is a fixed-size managed object (header + capacity + length +
// buffer pointer). Its UTF-16 content lives in a managed ARRAY (elemSize 2,
// non-reference elements), so growth = allocate a bigger array and replace
// the buffer field -- the object identity never changes (observable through
// reference equality, exactly like .NET).
//

#include "il2cpp_boxing_ext.h"    // P25g: icall_box_double / _bool / _char
#include "il2cpp_boxing_ext2.h"   // P25g: icall_box_long
#include "il2cpp_metadata.h"      // P25g: System_Int32_class (int32 box class)
#include "il2cpp_format_ext.h"    // P25g: icall_format_composite_item (net48 composite)//
// Growth policies (net48 BASELINE, empirically verified by the e2e
// reference run and targeted probes):
//   * Append: Grow(required) = max(required, max(Length, oldCapacity) * 2)
//     -- e.g. 16+17 -> 32, 4+10 -> 10, capacity 4 + "abc"+"def"+"ghi" -> 16.
//   * Insert: the reference runtime appends a chunk of the value's length,
//     so the observable capacity becomes oldCapacity + value.Length -- e.g.
//     2 + "abcde" -> 7, 4 + "abcdefghij" -> 14.
//   * ctor(string): preallocates max(DefaultCapacity, value.Length) -- the
//     value never travels through the Append growth path (20 -> 20, not 32).
//
// Exception transport follows the "{Type},{Type},{Base}..." chain contract
// of exception.cpp / il2cpp_exception_ext.h:23-28: the matcher walks the
// chain starting AFTER the first comma, so a catch of the thrown type itself
// only matches through a chain segment -- the type is therefore repeated as
// the first walk segment, followed by the .NET base classes (e.g. "...,
// System.ArgumentException,System.SystemException,System.Exception").
//
// GC notes (see the header contract):
//   * The collector COMPACTS surviving Gen0 objects (gc.cpp, M3 Task 4): a
//     collection can MOVE the builder itself and every buffer it references.
//     The collector rewrites roots and the reference fields of surviving
//     objects (Phase C), but NEVER raw C++ locals/parameters. The safe
//     pattern (RootScope, below) is therefore:
//       (a) root every managed pointer that lives across an allocation
//           (self, string values, the format string) -- the root slot IS the
//           local's storage, so Phase C rewrites the local automatically;
//       (b) re-derive the builder pointer from its rooted slot AFTER every
//           allocation, before ANY write to builder fields;
//       (c) pointers INTO a MARKED object (e.g. a captured chars payload)
//           stay content-valid across a collection: a marked block's old
//           copy is never freed or written (compaction copies it verbatim),
//           so stale reads are safe -- but stale WRITES are lost updates
//           (they land on the orphaned copy).
//   * Every buffer replacement stores the fresh array into sb->chars
//     IMMEDIATELY after allocation (no second allocation in between), so no
//     collection can sweep it between allocation and the field store -- the
//     store itself goes through the re-derived builder.
//------------------------------------------------------------------------------

#include "il2cpp_stringbuilder.h"

#include "il2cpp_exception.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_format.h"       // icall_int64_to_string / icall_bool_to_string / icall_double_to_string
#include "il2cpp_format_ext.h" // icall_format_composite_item (composite engine)
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "il2cpp_value_semantics.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

// The System.Text.StringBuilder runtime descriptor. Registered lazily
// (constraint #12: no global-constructor allocation). Layout on 8-byte
// pointers: slot 0 = klass header, slot 1 = capacity+length (value data),
// slot 2 = chars (GC reference) -> refFieldBitmap bit 2.
Il2CppClass g_stringBuilderClass = {
    "System.Text.StringBuilder",     // name
    static_cast<int32_t>(sizeof(Il2CppStringBuilder)), // instanceSize (24 on 64-bit)
    0b00000100u,                     // refFieldBitmap: bit 2 = the chars field
    1,                               // refFieldCount: one reference field
    nullptr,                         // baseClass
    nullptr,                         // vtable
    0,                               // vtableSize
    nullptr,                         // interfaces
    0,                               // interfaceCount
};

bool g_stringBuilderRegistered = false;

void EnsureStringBuilderRegistered()
{
    if (g_stringBuilderRegistered) return;
    il2cpp_class_register(&g_stringBuilderClass);
    g_stringBuilderRegistered = true;
}

// The default capacity (net48 DefaultCapacity, empirically verified).
constexpr int32_t kDefaultCapacity = 16;

// Cast a builder parameter (Il2CppObject*) to its payload view.
Il2CppStringBuilder* AsBuilder(Il2CppObject* obj)
{
    return reinterpret_cast<Il2CppStringBuilder*>(obj);
}

// Null-receiver guard: a .NET instance call on null is a
// NullReferenceException. Generated code null-checks before the call
// (il2cpp_null_check); this is the icall's defensive contract.
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
}

// ArgumentOutOfRangeException helper (exceptionext chain convention).
void ThrowAoore(const char* paramName)
{
    il2cpp_throw_new("System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception",
                     paramName != nullptr ? paramName : "Argument is out of range.");
}

// OutOfMemoryException helper: growth beyond the int32 unit domain.
void ThrowOom()
{
    il2cpp_throw_new("System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                     "Insufficient memory to continue the execution of the program.");
}

// Allocate a managed char16_t buffer holding 'capacity' units. The array is
// a non-reference managed array (elemSize 2, isRefElem false): its payload
// is pure value data, never scanned as references by the collector.
Il2CppArray* NewCharBuffer(int32_t capacity)
{
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(char16_t)), false, capacity);
}

// Replace the builder's char buffer with a fresh 'newCapacity'-unit array,
// copying the current content. 'selfSlot' is the ROOTED address of the
// builder pointer (see the file-top GC notes): the allocation can move the
// builder (Gen0 compaction), so the builder pointer is re-derived from the
// rooted slot after the allocation, and every field write goes through the
// re-derived pointer. The captured 'oldChars' is only READ afterwards: the
// old buffer is marked (reachable from the rooted builder), so its old copy
// is never freed or rewritten -- stale reads are content-valid.
void ReplaceBuffer(Il2CppObject** selfSlot, int32_t newCapacity)
{
    Il2CppStringBuilder* sb = AsBuilder(*selfSlot);
    Il2CppArray* oldChars = sb->chars;
    Il2CppArray* newChars = NewCharBuffer(newCapacity);
    sb = AsBuilder(*selfSlot);
    il2cpp_write_barrier(reinterpret_cast<void**>(&sb->chars),
                         reinterpret_cast<Il2CppObject*>(newChars));
    sb->chars = newChars;
    sb->capacity = newCapacity;
    if (sb->length > 0 && oldChars != nullptr)
    {
        std::memcpy(sb->chars->data, oldChars->data,
                    static_cast<size_t>(sb->length) * sizeof(char16_t));
    }
}

// Grow the buffer so it holds at least 'required' units, using the net48
// Grow policy (empirically verified): newCapacity = max(required,
// max(Length, oldCapacity) * 2) -- the old capacity doubles, the required
// count wins when it exceeds the doubled capacity (e.g. 16 + 17 chars ->
// 32; 4 + 10 chars -> 10). 'required' is computed in 64-bit so an int32
// overflow can never wrap the check; growth beyond INT32_MAX units raises
// OutOfMemoryException. 'selfSlot' is the rooted builder-pointer address
// (see the file-top GC notes).
void GrowBuffer(Il2CppObject** selfSlot, int64_t required)
{
    Il2CppStringBuilder* sb = AsBuilder(*selfSlot);
    if (required <= sb->capacity)
    {
        return;
    }
    int64_t newCapacity = static_cast<int64_t>(sb->capacity) * 2;
    if (required > newCapacity)
    {
        newCapacity = required;
    }
    if (newCapacity > INT32_MAX)
    {
        if (required > INT32_MAX)
        {
            ThrowOom();
        }
        newCapacity = required; // clamp: the required count fits, the doubling does not
    }
    ReplaceBuffer(selfSlot, static_cast<int32_t>(newCapacity));
}

// Grow the buffer for Insert, using the net48 observable policy
// (empirically verified): the new capacity is the OLD capacity plus the
// inserted value's length (the reference runtime appends a chunk of the
// value's length; e.g. capacity 2 + Insert("abcde") -> 7, capacity 4 +
// Insert("abcdefghij") -> 14). Callers invoke this only when the value does
// not fit; oldCapacity + valueLength >= length + valueLength always holds,
// so the insert always fits afterwards. 'selfSlot' is the rooted
// builder-pointer address (see the file-top GC notes).
void GrowBufferForInsert(Il2CppObject** selfSlot, int32_t valueLength)
{
    int64_t newCapacity = static_cast<int64_t>(AsBuilder(*selfSlot)->capacity) + valueLength;
    if (newCapacity > INT32_MAX)
    {
        ThrowOom();
    }
    ReplaceBuffer(selfSlot, static_cast<int32_t>(newCapacity));
}

// The shared append core: append 'units'/'count' at the tail (null units
// allowed for count == 0 -- a no-op when count == 0). Grows via the net48
// Grow policy. 'selfSlot' is the rooted builder-pointer address: GrowBuffer
// can move the builder, so the builder pointer is re-derived afterwards and
// every write (including the re-read of the chars field, which Phase C
// rewrites when the buffer moved) goes through the fresh pointer.
void AppendUnits(Il2CppObject** selfSlot, const char16_t* units, int32_t count)
{
    if (count <= 0)
    {
        return;
    }
    GrowBuffer(selfSlot, static_cast<int64_t>(AsBuilder(*selfSlot)->length) + count);
    Il2CppStringBuilder* sb = AsBuilder(*selfSlot);
    std::memcpy(sb->chars->data + static_cast<size_t>(sb->length) * sizeof(char16_t),
                units, static_cast<size_t>(count) * sizeof(char16_t));
    sb->length += count;
}

// The shared insert core (batch 170): bounds-check 'index' (0 <= index <=
// length -- net48 validates the index even when the value is null/empty),
// then insert 'units'/'count' before it: the tail [index, length) shifts
// right and the value is spliced in (memmove: the regions overlap by
// construction). A count <= 0 is a no-op AFTER the bounds check. Grows via
// the net48 Insert chunk policy (GrowBufferForInsert: new capacity = OLD
// capacity + the inserted count). 'selfSlot' is the rooted builder-pointer
// address (see the file-top GC notes): the growth allocation can move the
// builder, so the pointer is re-derived and every write goes through the
// fresh pointer. 'units' points either into a ROOTED managed string (marked
// old copies stay content-valid across a collection -- the existing insert
// contract) or at a C++ stack buffer (the numeric format kernels) -- never a
// dangling pointer.
void InsertUnits(Il2CppObject** selfSlot, int32_t index, const char16_t* units, int32_t count)
{
    Il2CppStringBuilder* sb = AsBuilder(*selfSlot);
    if (index < 0 || static_cast<int64_t>(index) > sb->length)
    {
        ThrowAoore("index");
    }
    if (count <= 0)
    {
        return;
    }
    if (static_cast<int64_t>(sb->length) + count > sb->capacity)
    {
        GrowBufferForInsert(selfSlot, count);
        sb = AsBuilder(*selfSlot); // re-derive: the allocation may have moved the builder
    }
    char* data = sb->chars->data;
    // Shift the tail [index, length) right by the unit count, then splice the
    // value in. memmove: the regions overlap by construction.
    std::memmove(data + static_cast<size_t>(index + count) * sizeof(char16_t),
                 data + static_cast<size_t>(index) * sizeof(char16_t),
                 static_cast<size_t>(sb->length - index) * sizeof(char16_t));
    std::memcpy(data + static_cast<size_t>(index) * sizeof(char16_t),
                units, static_cast<size_t>(count) * sizeof(char16_t));
    sb->length += count;
}

// Format 'value' as its invariant decimal representation into 'out'
// (capacity 12 covers "-2147483648" + NUL). Returns the unit count.
int32_t FormatInt32(int32_t value, char16_t* out)
{
    const bool negative = (value < 0);
    // 0u - value turns int.MinValue into 2147483648u without UB.
    uint32_t mag = negative ? (0u - static_cast<uint32_t>(value)) : static_cast<uint32_t>(value);
    char16_t digits[11]; // "2147483648" is 10 digits max
    int32_t digitCount = 0;
    do
    {
        digits[digitCount++] = static_cast<char16_t>(u'0' + (mag % 10u));
        mag /= 10u;
    } while (mag != 0);
    int32_t o = 0;
    if (negative)
    {
        out[o++] = u'-';
    }
    for (int32_t i = digitCount - 1; i >= 0; --i)
    {
        out[o++] = digits[i];
    }
    return o;
}

// The Windows newline AppendLine appends (net48 Environment.NewLine).
constexpr char16_t kNewLine[2] = { u'\r', u'\n' };

// Rooting scope (ipaddr.cpp:80-101 / linq_map.cpp:142-171 precedent): the
// runtime stack is NOT a GC root set, so a freshly allocated managed object
// held only in a C++ local (the temporary builder and the ToString results
// icall_string_format creates) would be swept by a collection triggered
// inside a later allocation. Registering the local's address as a static
// root keeps the object alive; the collector rewrites every root slot after
// a Gen0 compaction, so the registered LOCAL automatically tracks a moved
// object -- callers MUST re-derive pointers from the rooted slot after any
// allocation. RAII removal makes throwing paths (the FormatException
// family) leak-free. Slots: self, string value, format string, transient.
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
    void** m_slots[4]; // builder + value + format + one transient
    int32_t m_count;
};

// Append one composite-format argument (see AppendFormatCore): a string
// appends its payload, a boxed Int32 appends its invariant decimal
// representation, null appends nothing (net48: String.Empty); every other
// object appends its net48 ToString() text via icall_object_to_string
// (valuesemantics.cpp: boxed Int64/Double/Boolean/Char/... format their
// value, other objects print their type's full name -- the same dispatch
// String.Format uses). The ToString conversion ALLOCATES, so 'arg' and the
// resulting text are rooted for the append (AppendUnits may grow the buffer
// and trigger a collection that moves them); 'selfSlot' is the rooted
// builder-pointer address (see the file-top GC notes).
void AppendFormatArg(Il2CppObject** selfSlot, Il2CppObject* arg)
{
    if (arg == nullptr)
    {
        return;
    }
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&arg));
    if (arg->klass == il2cpp_string_class())
    {
        const Il2CppString* s = reinterpret_cast<const Il2CppString*>(arg);
        AppendUnits(selfSlot, s->chars, s->length);
        return;
    }
    if (arg->klass != nullptr && std::strcmp(arg->klass->name, "System.Int32") == 0)
    {
        // Boxed Int32 payload: one 8-byte-aligned slot after the object
        // header (identical layout to valuesemantics.cpp's BoxedInt32Value).
        const int32_t value = *reinterpret_cast<const int32_t*>(
            reinterpret_cast<const char*>(arg) + 8);
        char16_t buffer[12];
        const int32_t count = FormatInt32(value, buffer);
        AppendUnits(selfSlot, buffer, count);
        return;
    }
    Il2CppString* text = runtime::icall_object_to_string(arg);
    scope.Add(reinterpret_cast<void**>(&text));
    AppendUnits(selfSlot, text->chars, text->length);
}

// Composite-format expansion core shared by the AppendFormat overloads (see
// the header semantics): "{N[,alignment][:formatString]}" placeholders,
// "{{"/"}}" escaping. Null format -> ArgumentNullException; a malformed
// placeholder (unclosed brace, trailing brace, non-digit index) or an index
// >= the arg count -> FormatException; the ALIGNMENT / FORMAT-STRING
// components delegate to the composite-format engine (formatext.cpp
// icall_format_composite_item -- net48 alignment/min-width semantics).
// 'selfSlot' is the rooted builder-pointer address (see the file-top GC
// notes); the callers root 'format' so its payload stays content-valid across
// the append allocations. 'provider' (saudit4) is the IFormatProvider of the
// enclosing String.Format / AppendFormat call; it is threaded to the
// composite engine so invariant-culture formatting works inside placeholders
// (nullptr -- every provider-less caller -- keeps the zh-CN host layout).
void AppendFormatCore(Il2CppObject** selfSlot, const Il2CppString* format,
                      Il2CppObject* const* args, int32_t argCount,
                      Il2CppObject* provider)
{
    if (format == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception",
                         "Value cannot be null. (Parameter 'format')");
    }
    const char16_t* units = format->chars;
    const int32_t len = format->length;
    int32_t i = 0;
    while (i < len)
    {
        const char16_t c = units[i];
        if (c == u'{')
        {
            if (i + 1 < len && units[i + 1] == u'{')
            {
                AppendUnits(selfSlot, &units[i], 1); // "{{" -> one literal brace
                i += 2;
                continue;
            }
            // "{N[,alignment][:fmt]}" placeholder: scan the digit run, then the
            // optional ",alignment" and ":formatString" components.
            int32_t index = 0;
            int32_t j = i + 1;
            bool overflowed = false;
            while (j < len && units[j] >= u'0' && units[j] <= u'9')
            {
                const int32_t digit = units[j] - u'0';
                if (index > (INT32_MAX - digit) / 10)
                {
                    overflowed = true;
                }
                else if (!overflowed)
                {
                    index = index * 10 + digit;
                }
                ++j;
            }
            if (overflowed)
            {
                index = INT32_MAX;
            }
            if (j == i + 1)
            {
                // Empty index ("{}", "{,5}", "{:f2}") -- net48 FormatException.
                il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                                 "Input string was not in a correct format.");
            }
            // Optional ",alignment" (net48: an optional '-' sign then digits;
            // the width is a MINIMUM field width, negative = left-align).
            int32_t alignment = 0;
            if (j < len && units[j] == u',')
            {
                ++j;
                bool negative = false;
                if (j < len && units[j] == u'-')
                {
                    negative = true;
                    ++j;
                }
                if (j < len && units[j] == u'+')
                {
                    // net48 rejects a '+' alignment sign.
                    il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                                     "Input string was not in a correct format.");
                }
                const int32_t widthStart = j;
                int64_t width = 0;
                bool alignOverflow = false;
                while (j < len && units[j] >= u'0' && units[j] <= u'9')
                {
                    width = width * 10 + (units[j] - u'0');
                    if (width > INT32_MAX)
                    {
                        alignOverflow = true;
                    }
                    ++j;
                }
                if (alignOverflow)
                {
                    // net48: an alignment beyond int32 -> FormatException.
                    il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                                     "Input string was not in a correct format.");
                }
                if (j == widthStart)
                {
                    // "{0,}" / "{0,-}" -- no alignment digits.
                    il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                                     "Input string was not in a correct format.");
                }
                alignment = negative ? -static_cast<int32_t>(width) : static_cast<int32_t>(width);
            }
            // Optional ":formatString" -- every unit up to the closing '}'.
            int32_t fmtStart = j;
            int32_t fmtLen = 0;
            if (j < len && units[j] == u':')
            {
                ++j;
                fmtStart = j;
                while (j < len && units[j] != u'}')
                {
                    ++j;
                }
                if (j >= len)
                {
                    // Unclosed format string.
                    il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                                     "Input string was not in a correct format.");
                }
                fmtLen = j - fmtStart;
            }
            if (j >= len || units[j] != u'}')
            {
                // Non-digit content after '{' ("{x}"), or a missing '}'.
                il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                                 "Input string was not in a correct format.");
            }
            if (index >= argCount)
            {
                il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                                 "Input string was not in a correct format.");
            }
            if (fmtLen == 0 && alignment == 0)
            {
                AppendFormatArg(selfSlot, args[index]);
            }
            else
            {
                // Specifier and/or alignment: build the format-string component
                // (a fresh managed string, rooted) and delegate to the composite
                // engine; root the returned text across the append allocation.
                RootScope scope;
                Il2CppObject* fmtObj = nullptr;
                if (fmtLen > 0)
                {
                    fmtObj = reinterpret_cast<Il2CppObject*>(il2cpp_string_new(units + fmtStart, fmtLen));
                    scope.Add(reinterpret_cast<void**>(&fmtObj));
                }
                Il2CppObject* text = runtime::icall_format_composite_item(args[index], fmtObj, alignment, provider);
                scope.Add(reinterpret_cast<void**>(&text));
                const Il2CppString* s = reinterpret_cast<const Il2CppString*>(text);
                AppendUnits(selfSlot, s->chars, s->length);
            }
            i = j + 1;
            continue;
        }
        if (c == u'}')
        {
            if (i + 1 < len && units[i + 1] == u'}')
            {
                AppendUnits(selfSlot, &units[i], 1); // "}}" -> one literal brace
                i += 2;
                continue;
            }
            il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                             "Input string was not in a correct format.");
        }
        AppendUnits(selfSlot, &units[i], 1); // ordinary literal unit
        ++i;
    }
}

// Whether the builder's content matches 'pattern' at 'pos' (the caller
// guarantees pos + patternLen <= length).
bool UnitsMatchAt(const Il2CppStringBuilder* sb, int32_t pos,
                  const char16_t* pattern, int32_t patternLen)
{
    const char16_t* data = reinterpret_cast<const char16_t*>(sb->chars->data);
    for (int32_t k = 0; k < patternLen; ++k)
    {
        if (data[pos + k] != pattern[k])
        {
            return false;
        }
    }
    return true;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_stringbuilder_class -- the System.Text.StringBuilder descriptor
// (lazily registered).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_stringbuilder_class()
{
    EnsureStringBuilderRegistered();
    return &g_stringBuilderClass;
}

namespace runtime {

//------------------------------------------------------------------------------
// icall_stringbuilder_ctor_default -- StringBuilder() (see header).
//------------------------------------------------------------------------------
void icall_stringbuilder_ctor_default(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppStringBuilder* sb = AsBuilder(self);
    sb->capacity = kDefaultCapacity;
    sb->length = 0;
    Il2CppArray* chars = NewCharBuffer(kDefaultCapacity);
    sb = AsBuilder(self); // re-derive: the allocation may have moved the builder
    il2cpp_write_barrier(reinterpret_cast<void**>(&sb->chars),
                         reinterpret_cast<Il2CppObject*>(chars));
    sb->chars = chars;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_ctor_capacity -- StringBuilder(int) (see header).
// net48 baseline: negative throws, 0 falls back to the default capacity.
//------------------------------------------------------------------------------
void icall_stringbuilder_ctor_capacity(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNullReceiver(self);
    if (capacity < 0)
    {
        ThrowAoore("capacity");
    }
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppStringBuilder* sb = AsBuilder(self);
    sb->capacity = (capacity == 0) ? kDefaultCapacity : capacity;
    sb->length = 0;
    Il2CppArray* chars = NewCharBuffer(sb->capacity);
    sb = AsBuilder(self); // re-derive: the allocation may have moved the builder
    il2cpp_write_barrier(reinterpret_cast<void**>(&sb->chars),
                         reinterpret_cast<Il2CppObject*>(chars));
    sb->chars = chars;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_ctor_string -- StringBuilder(string) (see header).
// net48 baseline (empirically verified): the buffer is preallocated to
// max(DefaultCapacity, value.Length) -- the value NEVER travels through the
// Append growth path (a 20-unit value lands in a 20-unit buffer, not a
// 32-unit one).
//------------------------------------------------------------------------------
void icall_stringbuilder_ctor_string(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&value));
    Il2CppStringBuilder* sb = AsBuilder(self);
    const int32_t valueLength = (value != nullptr)
        ? reinterpret_cast<const Il2CppString*>(value)->length : 0;
    sb->capacity = (valueLength > kDefaultCapacity) ? valueLength : kDefaultCapacity;
    sb->length = 0;
    Il2CppArray* chars = NewCharBuffer(sb->capacity);
    sb = AsBuilder(self); // re-derive: the allocation may have moved the builder
    il2cpp_write_barrier(reinterpret_cast<void**>(&sb->chars),
                         reinterpret_cast<Il2CppObject*>(chars));
    sb->chars = chars;
    if (value != nullptr)
    {
        const Il2CppString* v = reinterpret_cast<const Il2CppString*>(value);
        std::memcpy(sb->chars->data, v->chars,
                    static_cast<size_t>(v->length) * sizeof(char16_t));
        sb->length = v->length;
    }
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_s -- Append(string) (see header). Null is a
// no-op returning the same builder.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_s(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&value));
    if (value != nullptr)
    {
        const Il2CppString* v = reinterpret_cast<const Il2CppString*>(value);
        AppendUnits(&self, v->chars, v->length);
    }
    return self; // the rooted local tracks the builder across any collection
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_c -- Append(char) (see header).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_c(Il2CppObject* self, char16_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    AppendUnits(&self, &value, 1);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_l -- Append(long) (batch 151): the invariant
// decimal text via icall_int64_to_string (net48 Append(long) semantics).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_l(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppString* text = icall_int64_to_string(value);
    scope.Add(reinterpret_cast<void**>(&text));
    const Il2CppString* v = reinterpret_cast<const Il2CppString*>(text);
    AppendUnits(&self, v->chars, v->length);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_d -- Append(double) (batch 151): the net48
// "G15" text via icall_double_to_string.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_d(Il2CppObject* self, double value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppString* text = icall_double_to_string(value);
    scope.Add(reinterpret_cast<void**>(&text));
    const Il2CppString* v = reinterpret_cast<const Il2CppString*>(text);
    AppendUnits(&self, v->chars, v->length);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_b -- Append(bool) (batch 151): "True"/"False"
// via icall_bool_to_string.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_b(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppString* text = icall_bool_to_string(value != 0);
    scope.Add(reinterpret_cast<void**>(&text));
    const Il2CppString* v = reinterpret_cast<const Il2CppString*>(text);
    AppendUnits(&self, v->chars, v->length);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_i -- Append(int) (see header). Invariant
// decimal formatting; int.MinValue is handled without signed overflow.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_i(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    char16_t buffer[12];
    const int32_t count = FormatInt32(value, buffer);
    AppendUnits(&self, buffer, count);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_o -- Append(object) (see header). net48:
// Append(object value) = Append(value.ToString()) -- null is a no-op, a
// string appends its payload, a boxed Int32 appends its invariant decimal,
// and any other managed object appends its ToString text via the
// icall_object_to_string dispatch (valuesemantics.cpp): boxed primitives
// format their value, any other object prints its type's full name. The
// append kernel is AppendFormatArg's -- the same ToString dispatch String.
// Format / AppendFormat use.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_o(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&value));
    AppendFormatArg(&self, value); // null -> no-op; string -> payload; Int32 -> decimal; else ToString
    return self; // the rooted local tracks the builder across any collection
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_line -- AppendLine() (see header). Appends the
// Windows newline "\r\n" (net48 Environment.NewLine).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_line(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    AppendUnits(&self, kNewLine, 2);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_line_s -- AppendLine(string) (see header). The
// value's units followed by the newline; a null value appends the newline
// only.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_line_s(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&value));
    if (value != nullptr)
    {
        const Il2CppString* v = reinterpret_cast<const Il2CppString*>(value);
        AppendUnits(&self, v->chars, v->length);
    }
    AppendUnits(&self, kNewLine, 2);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_format_1/2/3 -- AppendFormat(string, object[,
// object[, object]]) (see header). Composite-format expansion with the
// documented arg subset (string / boxed Int32 / null).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_format_1(Il2CppObject* self, Il2CppObject* format, Il2CppObject* arg0)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&format));
    Il2CppObject* args[1] = { arg0 };
    AppendFormatCore(&self, reinterpret_cast<const Il2CppString*>(format), args, 1, nullptr);
    return self;
}

Il2CppObject* icall_stringbuilder_append_format_2(Il2CppObject* self, Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&format));
    Il2CppObject* args[2] = { arg0, arg1 };
    AppendFormatCore(&self, reinterpret_cast<const Il2CppString*>(format), args, 2, nullptr);
    return self;
}

Il2CppObject* icall_stringbuilder_append_format_3(Il2CppObject* self, Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1, Il2CppObject* arg2)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&format));
    Il2CppObject* args[3] = { arg0, arg1, arg2 };
    AppendFormatCore(&self, reinterpret_cast<const Il2CppString*>(format), args, 3, nullptr);
    return self;
}

//------------------------------------------------------------------------------
// icall_string_format family -- System.String.Format overloads (net48
// semantics, kernel reuse: AppendFormatCore). Each overload expands the
// composite format through a FRESH temporary builder and returns the result
// string -- .NET's Format is exactly "create builder, append format, take
// the string" (the AppendFormat kernel IS the shared expansion engine).
//
// The temporary builder is a freshly allocated managed object held only in
// C++ locals: the RootScope keeps it alive across AppendFormatCore's
// allocations (buffer growth) and the final ToString allocation. The args
// array / fixed args arrive from generated code, which roots them (the
// frame-root contract); fixed-arity args are staged into a stack array.
// 'format' null -> ArgumentNullException; malformed format / out-of-range
// index -> FormatException (AppendFormatCore, unchanged). Format(string,
// object[]) with a null ARRAY -> ArgumentNullException("args") (net48); an
// empty array is legal (the format must then carry no index). 'provider'
// (saudit4) threads the IFormatProvider of String.Format(provider, ...)
// through to the composite engine (nullptr for the provider-less overloads).
//------------------------------------------------------------------------------
Il2CppObject* FormatToString(const Il2CppString* format, Il2CppObject* const* args, int32_t argCount,
                             Il2CppObject* provider)
{
    Il2CppObject* sb = il2cpp_object_new(il2cpp_stringbuilder_class());
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&sb));
    Il2CppObject* fmtSlot = const_cast<Il2CppObject*>(reinterpret_cast<const Il2CppObject*>(format));
    scope.Add(reinterpret_cast<void**>(&fmtSlot));
    icall_stringbuilder_ctor_default(sb);
    AppendFormatCore(&sb, format, args, argCount, provider);
    return icall_stringbuilder_to_string(sb);
}

Il2CppObject* icall_string_format_0(Il2CppObject* format)
{
    return FormatToString(reinterpret_cast<const Il2CppString*>(format), nullptr, 0, nullptr);
}

// Provider-prefixed wrappers (D5 envnf, saudit4): String.Format(IFormatProvider,
// ...) -- the provider is threaded into the composite engine; nullptr /
// non-empty names keep the zh-CN host layout, the invariant name switches the
// numeric C/P layouts inside placeholders (net48 oracle-pinned).
Il2CppObject* icall_string_format_0_provider(Il2CppObject* provider, Il2CppObject* format)
{
    return FormatToString(reinterpret_cast<const Il2CppString*>(format), nullptr, 0, provider);
}

Il2CppObject* icall_string_format_1(Il2CppObject* format, Il2CppObject* arg0)
{
    Il2CppObject* args[1] = { arg0 };
    return FormatToString(reinterpret_cast<const Il2CppString*>(format), args, 1, nullptr);
}

Il2CppObject* icall_string_format_1_provider(Il2CppObject* provider, Il2CppObject* format, Il2CppObject* arg0)
{
    Il2CppObject* args[1] = { arg0 };
    return FormatToString(reinterpret_cast<const Il2CppString*>(format), args, 1, provider);
}

Il2CppObject* icall_string_format_2(Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1)
{
    Il2CppObject* args[2] = { arg0, arg1 };
    return FormatToString(reinterpret_cast<const Il2CppString*>(format), args, 2, nullptr);
}

Il2CppObject* icall_string_format_2_provider(Il2CppObject* provider, Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1)
{
    Il2CppObject* args[2] = { arg0, arg1 };
    return FormatToString(reinterpret_cast<const Il2CppString*>(format), args, 2, provider);
}

Il2CppObject* icall_string_format_3(Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1, Il2CppObject* arg2)
{
    Il2CppObject* args[3] = { arg0, arg1, arg2 };
    return FormatToString(reinterpret_cast<const Il2CppString*>(format), args, 3, nullptr);
}

Il2CppObject* icall_string_format_3_provider(Il2CppObject* provider, Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1, Il2CppObject* arg2)
{
    Il2CppObject* args[3] = { arg0, arg1, arg2 };
    return FormatToString(reinterpret_cast<const Il2CppString*>(format), args, 3, provider);
}

Il2CppObject* icall_string_format(Il2CppObject* format, Il2CppObject* argsArray)
{
    if (argsArray == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception",
                         "Value cannot be null. (Parameter 'args')");
    }
    const Il2CppArray* arr = reinterpret_cast<const Il2CppArray*>(argsArray);
    // object[] elements are consecutive Il2CppObject* slots -- pass the
    // payload directly (zero copy); AppendFormatCore bounds-checks every
    // index against arr->length before touching an element.
    return FormatToString(reinterpret_cast<const Il2CppString*>(format),
                          reinterpret_cast<Il2CppObject* const*>(arr->data),
                          arr->length, nullptr);
}

// String.Format(IFormatProvider, string, params object[]) -- provider threaded
// through (saudit4; see the composite engine contract).
Il2CppObject* icall_string_format_provider(Il2CppObject* provider, Il2CppObject* format, Il2CppObject* argsArray)
{
    if (argsArray == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception",
                         "Value cannot be null. (Parameter 'args')");
    }
    const Il2CppArray* arr = reinterpret_cast<const Il2CppArray*>(argsArray);
    return FormatToString(reinterpret_cast<const Il2CppString*>(format),
                          reinterpret_cast<Il2CppObject* const*>(arr->data),
                          arr->length, provider);
}

//------------------------------------------------------------------------------
// icall_stringbuilder_insert -- Insert(int, string) (see header). Bounds:
// 0 <= index <= Length (64-bit checked); null value is a no-op; the buffer
// grows exactly when needed. The insert core is InsertUnits (shared by the
// batch 170 numeric / count overloads below).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_insert(Il2CppObject* self, int32_t index, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&value));
    if (value != nullptr)
    {
        const Il2CppString* v = reinterpret_cast<const Il2CppString*>(value);
        InsertUnits(&self, index, v->chars, v->length);
    }
    else
    {
        InsertUnits(&self, index, nullptr, 0); // .NET: Insert(any, null) no-ops after the index bounds check
    }
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_insert_i -- Insert(int, int) (batch 170): the invariant
// decimal text (FormatInt32 -- the Append(int) kernel) is inserted, not the
// raw int (net48 ToString-then-insert). The format buffer is a C++ stack
// array, so it stays content-valid across the insert growth allocation.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_insert_i(Il2CppObject* self, int32_t index, int32_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    char16_t buffer[12];
    const int32_t count = FormatInt32(value, buffer);
    InsertUnits(&self, index, buffer, count);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_insert_l -- Insert(int, long) (batch 170): the
// invariant decimal text via icall_int64_to_string (the Append(long) kernel).
// The result string is rooted; its marked old copy stays content-valid across
// the insert growth allocation.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_insert_l(Il2CppObject* self, int32_t index, int64_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppString* text = icall_int64_to_string(value);
    scope.Add(reinterpret_cast<void**>(&text));
    const Il2CppString* v = reinterpret_cast<const Il2CppString*>(text);
    InsertUnits(&self, index, v->chars, v->length);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_insert_d -- Insert(int, double) (batch 170): the net48
// "G15" text via icall_double_to_string (the Append(double) kernel).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_insert_d(Il2CppObject* self, int32_t index, double value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppString* text = icall_double_to_string(value);
    scope.Add(reinterpret_cast<void**>(&text));
    const Il2CppString* v = reinterpret_cast<const Il2CppString*>(text);
    InsertUnits(&self, index, v->chars, v->length);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_insert_b -- Insert(int, bool) (batch 170): "True"/
// "False" via icall_bool_to_string (the Append(bool) kernel).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_insert_b(Il2CppObject* self, int32_t index, int32_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    Il2CppString* text = icall_bool_to_string(value != 0);
    scope.Add(reinterpret_cast<void**>(&text));
    const Il2CppString* v = reinterpret_cast<const Il2CppString*>(text);
    InsertUnits(&self, index, v->chars, v->length);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_insert_c -- Insert(int, char) (batch 170): inserts the
// single unit. The unit lives on the C++ stack (the value parameter), so it
// stays content-valid across the insert growth allocation.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_insert_c(Il2CppObject* self, int32_t index, char16_t value)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    InsertUnits(&self, index, &value, 1);
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_insert_s_count -- Insert(int, string, int) (batch 170,
// net48 semantics). count < 0 -> ArgumentOutOfRangeException("count");
// index < 0 or index > Length -> ArgumentOutOfRangeException("index");
// a null/empty value or count == 0 is a no-op (index bounds still checked);
// the total repeated length (count * value.Length) must fit int32 or the call
// throws ArgumentOutOfRangeException("count"). Growth follows the net48
// Insert chunk policy with the TOTAL inserted length. The value string is
// rooted; its marked old copy stays content-valid across the growth
// allocation.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_insert_s_count(Il2CppObject* self, int32_t index, Il2CppObject* value, int32_t count)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&value));
    if (count < 0)
    {
        ThrowAoore("count");
    }
    Il2CppStringBuilder* sb = AsBuilder(self);
    if (index < 0 || static_cast<int64_t>(index) > sb->length)
    {
        ThrowAoore("index");
    }
    if (value == nullptr || count == 0)
    {
        return self; // .NET: Insert(index, null, count) / count == 0 no-ops
    }
    const Il2CppString* v = reinterpret_cast<const Il2CppString*>(value);
    const int32_t vLen = v->length;
    if (vLen == 0)
    {
        return self; // inserting the empty string changes nothing
    }
    const int64_t total64 = static_cast<int64_t>(vLen) * count;
    if (total64 > INT32_MAX - sb->length)
    {
        ThrowAoore("count"); // net48: the repeated value does not fit
    }
    const int32_t total = static_cast<int32_t>(total64);
    if (static_cast<int64_t>(sb->length) + total > sb->capacity)
    {
        GrowBufferForInsert(&self, total);
        sb = AsBuilder(self); // re-derive: the allocation may have moved the builder
    }
    char* data = sb->chars->data;
    // Shift the tail [index, length) right by the total, then splice 'value'
    // repeated 'count' times at successive offsets (net48's ReplaceInPlaceAt
    // loop shape). memmove: the regions overlap by construction.
    std::memmove(data + static_cast<size_t>(index + total) * sizeof(char16_t),
                 data + static_cast<size_t>(index) * sizeof(char16_t),
                 static_cast<size_t>(sb->length - index) * sizeof(char16_t));
    int32_t dst = index;
    for (int32_t k = 0; k < count; ++k)
    {
        std::memcpy(data + static_cast<size_t>(dst) * sizeof(char16_t),
                    v->chars, static_cast<size_t>(vLen) * sizeof(char16_t));
        dst += vLen;
    }
    sb->length += total;
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_remove -- Remove(int, int) (see header). Bounds are
// checked with a 64-bit subtraction so the .NET violation shapes all throw:
// startIndex < 0, startIndex > Length, length < 0, startIndex > Length -
// length.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_remove(Il2CppObject* self, int32_t startIndex, int32_t length)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* sb = AsBuilder(self);
    if (startIndex < 0 || static_cast<int64_t>(startIndex) > sb->length)
    {
        ThrowAoore("startIndex");
    }
    if (length < 0 || static_cast<int64_t>(startIndex) > static_cast<int64_t>(sb->length) - length)
    {
        ThrowAoore("length");
    }
    if (length == 0)
    {
        return self; // removing nothing changes nothing
    }
    // Shift the tail [startIndex + length, Length) left into [startIndex, ...).
    char* data = sb->chars->data;
    std::memmove(data + static_cast<size_t>(startIndex) * sizeof(char16_t),
                 data + static_cast<size_t>(startIndex + length) * sizeof(char16_t),
                 static_cast<size_t>(sb->length - startIndex - length) * sizeof(char16_t));
    sb->length -= length;
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_clear -- Clear() (the netcore21+ audit surface): the
// used length drops to 0 (the buffer stays allocated -- the .NET contract
// only requires Length == 0 and the next Append to reuse capacity). Returns
// self (the fluent StringBuilder).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* sb = AsBuilder(self);
    sb->length = 0;
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_replace_s_s -- Replace(string, string) (see header).
// Replaces EVERY non-overlapping occurrence of oldValue (greedy left-to-right
// scan, like net48) with newValue; a null newValue removes the occurrences.
// Null oldValue -> ArgumentNullException; empty oldValue -> ArgumentException.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_replace_s_s(Il2CppObject* self, Il2CppObject* oldValue, Il2CppObject* newValue)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&oldValue));
    scope.Add(reinterpret_cast<void**>(&newValue));
    if (oldValue == nullptr)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception",
                         "Value cannot be null. (Parameter 'oldValue')");
    }
    const Il2CppString* oldS = reinterpret_cast<const Il2CppString*>(oldValue);
    if (oldS->length == 0)
    {
        il2cpp_throw_new("System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception",
                         "String cannot be of zero length.");
    }
    const Il2CppString* newS = (newValue != nullptr)
        ? reinterpret_cast<const Il2CppString*>(newValue) : nullptr;
    Il2CppStringBuilder* sb = AsBuilder(self);
    const int32_t oldLen = oldS->length;
    const int32_t newLen = newS ? newS->length : 0;
    // Pass 1: count the non-overlapping occurrences (greedy left-to-right,
    // which is the maximum-match scan -- the backward scan in pass 2 finds
    // the same count) and compute the result length in 64-bit.
    int32_t occurrenceCount = 0;
    int32_t pos = 0;
    while (pos <= sb->length - oldLen)
    {
        if (UnitsMatchAt(sb, pos, oldS->chars, oldLen))
        {
            ++occurrenceCount;
            pos += oldLen;
        }
        else
        {
            ++pos;
        }
    }
    if (occurrenceCount == 0)
    {
        return self;
    }
    const int64_t delta64 = static_cast<int64_t>(newLen) - oldLen;
    const int64_t totalDelta = delta64 * occurrenceCount;
    const int64_t resultLength64 = static_cast<int64_t>(sb->length) + totalDelta;
    if (resultLength64 > INT32_MAX)
    {
        ThrowOom();
    }
    const int32_t resultLength = static_cast<int32_t>(resultLength64);
    // net48 Replace growth (empirically verified against the reference
    // runtime): the new capacity is the OLD capacity plus the total delta
    // (newValue.Length - oldValue.Length) * occurrenceCount -- the reference
    // appends a chunk of exactly the needed room (e.g. capacity 32 +
    // "ab"->"efg" x10 -> 42; capacity 4 + "ab"->"abcdefghij" -> 12; capacity
    // 4 + "ab"->"abc" -> 5). This is NOT the Append doubling policy. A
    // shrink or same-width replace keeps the current buffer (net48 may
    // shrink through chunk merging; keeping it matches Remove's "does not
    // reduce the capacity" contract). oldCapacity >= oldLength (a runtime
    // invariant), so newCapacity = oldCapacity + totalDelta >= resultLength
    // always -- the result always fits the freshly sized buffer.
    if (totalDelta > 0)
    {
        const int64_t newCapacity64 = static_cast<int64_t>(sb->capacity) + totalDelta;
        if (newCapacity64 > INT32_MAX)
        {
            ThrowOom();
        }
        ReplaceBuffer(&self, static_cast<int32_t>(newCapacity64));
        sb = AsBuilder(self); // re-derive: the allocation may have moved the builder
    }
    char* data = sb->chars->data;
    if (newLen <= oldLen)
    {
        // Forward scan: each match shrinks or keeps the write-read gap, so
        // the unread tail is never overwritten.
        int32_t read = 0;
        int32_t write = 0;
        while (read <= sb->length - oldLen)
        {
            if (UnitsMatchAt(sb, read, oldS->chars, oldLen))
            {
                if (newLen > 0)
                {
                    std::memcpy(data + static_cast<size_t>(write) * sizeof(char16_t),
                                newS->chars, static_cast<size_t>(newLen) * sizeof(char16_t));
                    write += newLen;
                }
                read += oldLen;
            }
            else
            {
                data[static_cast<size_t>(write) * sizeof(char16_t)] =
                    data[static_cast<size_t>(read) * sizeof(char16_t)];
                ++read;
                ++write;
            }
        }
        // Tail beyond the last possible match start: no further matches.
        const int32_t remaining = sb->length - read;
        if (remaining > 0)
        {
            std::memmove(data + static_cast<size_t>(write) * sizeof(char16_t),
                         data + static_cast<size_t>(read) * sizeof(char16_t),
                         static_cast<size_t>(remaining) * sizeof(char16_t));
        }
        sb->length = write + remaining;
    }
    else
    {
        // Backward scan: the write cursor stays at or ahead of the read
        // cursor (each match grows the gap), so the unread head is never
        // overwritten. The backward greedy scan finds the same maximum
        // match count as the forward pass.
        int32_t readEnd = sb->length;
        int32_t writeEnd = resultLength;
        while (readEnd > 0)
        {
            if (readEnd >= oldLen &&
                UnitsMatchAt(sb, readEnd - oldLen, oldS->chars, oldLen))
            {
                if (newLen > 0)
                {
                    std::memcpy(data + static_cast<size_t>(writeEnd - newLen) * sizeof(char16_t),
                                newS->chars, static_cast<size_t>(newLen) * sizeof(char16_t));
                }
                writeEnd -= newLen;
                readEnd -= oldLen;
            }
            else
            {
                data[static_cast<size_t>(writeEnd - 1) * sizeof(char16_t)] =
                    data[static_cast<size_t>(readEnd - 1) * sizeof(char16_t)];
                --writeEnd;
                --readEnd;
            }
        }
        sb->length = resultLength;
    }
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_replace_c_c -- Replace(char, char) (see header).
// Replaces every matching unit in place. Returns the SAME builder.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_replace_c_c(Il2CppObject* self, char16_t oldChar, char16_t newChar)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* sb = AsBuilder(self);
    char16_t* data = reinterpret_cast<char16_t*>(sb->chars->data);
    for (int32_t i = 0; i < sb->length; ++i)
    {
        if (data[i] == oldChar)
        {
            data[i] = newChar;
        }
    }
    return self;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_to_string -- ToString() (see header). A fresh copy:
// il2cpp_string_new copies the payload, so later mutations never change an
// earlier result.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_to_string(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    const Il2CppStringBuilder* sb = AsBuilder(self);
    // Read the payload pointer/length BEFORE the allocation: rooting self
    // keeps the builder and its buffer marked, so a collection inside
    // il2cpp_string_new cannot sweep them (the marked old copies stay
    // content-valid even when compaction moves them).
    Il2CppString* result = il2cpp_string_new(
        reinterpret_cast<const char16_t*>(sb->chars->data), sb->length);
    return reinterpret_cast<Il2CppObject*>(result);
}

//------------------------------------------------------------------------------
// icall_stringbuilder_get_length -- Length getter (see header).
//------------------------------------------------------------------------------
int32_t icall_stringbuilder_get_length(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsBuilder(self)->length;
}

//------------------------------------------------------------------------------
// get_MaxCapacity -- StringBuilder.MaxCapacity (D5 stsdeep): every builder the
// runtime creates (StringBuilder(), (int), (string), (string,int)...) gets the
// net48 MONO_DEFAULT_MAX_CAPACITY (int.MaxValue); the managed side never
// changes it for these ctors, so the read is a constant contract, not a field
// chase across a chunk the runtime does not track.
//------------------------------------------------------------------------------
int32_t icall_stringbuilder_get_max_capacity(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return int32_t(0x7FFFFFFF);
}

//------------------------------------------------------------------------------
// get_Chars(index) -- the indexer. Reads the UTF-16 unit at 'index' from the
// managed char buffer; out-of-range -> IndexOutOfRangeException (the .NET
// contract; the array helper's bounds check provides it).
//------------------------------------------------------------------------------
int32_t icall_stringbuilder_get_chars(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* sb = AsBuilder(self);
    if (index < 0 || index >= sb->length)
    {
        il2cpp_throw_index_out_of_range_exception("index");
    }
    const void* elem = il2cpp_array_get_elem_ptr(sb->chars, index);
    return *reinterpret_cast<const uint16_t*>(elem);
}

//------------------------------------------------------------------------------
// set_Chars(index, utf16) -- the indexer WRITER (P26 bcldeep): the net8
// Chars[index] setter is Managed+InternalCall; default in .NET Core 8 to a
// pure-managed body, so the transpiler needs the icall. Same bounds contract
// as get_Chars (IndexOutOfRangeException); writes one UTF-16 unit.
//------------------------------------------------------------------------------
void icall_stringbuilder_set_chars(Il2CppObject* self, int32_t index, int32_t utf16)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* sb = AsBuilder(self);
    if (index < 0 || index >= sb->length)
    {
        il2cpp_throw_index_out_of_range_exception("index");
    }
    void* elem = il2cpp_array_get_elem_ptr(sb->chars, index);
    *reinterpret_cast<uint16_t*>(elem) = static_cast<uint16_t>(utf16);
}

//------------------------------------------------------------------------------
// P6: ReplaceBufferInternal/ReplaceBufferAnsiInternal -- net48 internal helpers
// that replace the entire StringBuilder buffer contents.
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_replace_buffer_internal(Il2CppObject* self, const char16_t* buffer)
{
    ThrowIfNullReceiver(self);
    auto* sb = AsBuilder(self);
    if (buffer == nullptr)
    {
        sb->length = 0;
        return self;
    }
    int32_t len = 0;
    while (buffer[len] != u'\0') len++;
    // Ensure capacity: if too small, grow via the existing GrowBuffer path.
    if (len > sb->capacity)
    {
        Il2CppObject* selfObj = self;
        GrowBuffer(&selfObj, len);
        sb = AsBuilder(selfObj);
    }
    auto* dst = reinterpret_cast<char16_t*>(sb->chars->data);
    for (int32_t i = 0; i < len; i++)
        dst[i] = buffer[i];
    sb->length = len;
    return self;
}

Il2CppObject* icall_stringbuilder_replace_buffer_ansi_internal(Il2CppObject* self, const int8_t* buffer)
{
    ThrowIfNullReceiver(self);
    auto* sb = AsBuilder(self);
    if (buffer == nullptr)
    {
        sb->length = 0;
        return self;
    }
    int32_t len = 0;
    while (buffer[len] != 0) len++;
    if (len > sb->capacity)
    {
        Il2CppObject* selfObj = self;
        GrowBuffer(&selfObj, len);
        sb = AsBuilder(selfObj);
    }
    auto* dst = reinterpret_cast<char16_t*>(sb->chars->data);
    for (int32_t i = 0; i < len; i++)
        dst[i] = static_cast<char16_t>(static_cast<uint8_t>(buffer[i]));
    sb->length = len;
    return self;
}

//------------------------------------------------------------------------------
// icall_interp_handler_ctor -- DefaultInterpolatedStringHandler::.ctor (P25f
// dhprobe). The C# `$"...{x}..."` net6+ lowering address-takes the handler
// VALUE-type local (ldloca) and calls the ctor with (literalLength,
// formattedCount) -- compile-time capacity hints, IGNORED here. The handler is
// modeled as a StringBuilder object stored INTO the slot (a GC frame root --
// &V_N is registered in the method's frame-references by the transpiler).
// literalLength / formattedCount exist so the guard arity matches the IL.
//------------------------------------------------------------------------------
void icall_interp_handler_ctor(Il2CppObject** slot, int32_t /*literalLength*/, int32_t /*formattedCount*/)
{
    Il2CppObject* sb = il2cpp_object_new(il2cpp_stringbuilder_class());
    icall_stringbuilder_ctor_default(sb);
    *slot = sb;
}

//------------------------------------------------------------------------------
// icall_stringbuilder_append_{i,l,d,b,c,s}_{w,f,wf} -- P25g (dhprobe): the
// DefaultInterpolatedStringHandler.AppendFormatted<T> ALIGNMENT and
// FORMAT-STRING overloads ('{x,5}' / '{x:D3}' / '{x,5:D3}'). One trio per
// value type delegates to icall_format_composite_item (the net48 composite
// engine -- il2cpp_format_ext.h) with the BOXED value; the resulting string
// is appended to the builder. NULL format = default text; width = the net48
// min-width alignment (positive right-align, negative left-align).
//------------------------------------------------------------------------------
static Il2CppObject* InterpAppendFormatted(Il2CppObject* self, Il2CppObject* boxed,
                                           int32_t width, Il2CppObject* format)
{
    ThrowIfNullReceiver(self);
    RootScope scope;
    scope.Add(reinterpret_cast<void**>(&self));
    scope.Add(reinterpret_cast<void**>(&boxed));
    Il2CppObject* text = icall_format_composite_item(boxed, format, width);
    scope.Add(reinterpret_cast<void**>(&text));
    const Il2CppString* s = reinterpret_cast<const Il2CppString*>(text);
    AppendUnits(&self, s->chars, s->length);
    return self;
}

Il2CppObject* icall_stringbuilder_append_i_w(Il2CppObject* self, int32_t value, int32_t width)
{
    return InterpAppendFormatted(self, il2cpp_box_int32(&System_Int32_class, value), width, nullptr);
}
Il2CppObject* icall_stringbuilder_append_i_f(Il2CppObject* self, int32_t value, Il2CppObject* format)
{
    return InterpAppendFormatted(self, il2cpp_box_int32(&System_Int32_class, value), 0, format);
}
Il2CppObject* icall_stringbuilder_append_i_wf(Il2CppObject* self, int32_t value, int32_t width, Il2CppObject* format)
{
    return InterpAppendFormatted(self, il2cpp_box_int32(&System_Int32_class, value), width, format);
}
Il2CppObject* icall_stringbuilder_append_l_w(Il2CppObject* self, int64_t value, int32_t width)
{
    return InterpAppendFormatted(self, icall_box_long(value), width, nullptr);
}
Il2CppObject* icall_stringbuilder_append_l_f(Il2CppObject* self, int64_t value, Il2CppObject* format)
{
    return InterpAppendFormatted(self, icall_box_long(value), 0, format);
}
Il2CppObject* icall_stringbuilder_append_l_wf(Il2CppObject* self, int64_t value, int32_t width, Il2CppObject* format)
{
    return InterpAppendFormatted(self, icall_box_long(value), width, format);
}
Il2CppObject* icall_stringbuilder_append_d_w(Il2CppObject* self, double value, int32_t width)
{
    return InterpAppendFormatted(self, icall_box_double(value), width, nullptr);
}
Il2CppObject* icall_stringbuilder_append_d_f(Il2CppObject* self, double value, Il2CppObject* format)
{
    return InterpAppendFormatted(self, icall_box_double(value), 0, format);
}
Il2CppObject* icall_stringbuilder_append_d_wf(Il2CppObject* self, double value, int32_t width, Il2CppObject* format)
{
    return InterpAppendFormatted(self, icall_box_double(value), width, format);
}
Il2CppObject* icall_stringbuilder_append_b_w(Il2CppObject* self, bool value, int32_t width)
{
    return InterpAppendFormatted(self, icall_box_bool(value), width, nullptr);
}
Il2CppObject* icall_stringbuilder_append_b_f(Il2CppObject* self, bool value, Il2CppObject* format)
{
    return InterpAppendFormatted(self, icall_box_bool(value), 0, format);
}
Il2CppObject* icall_stringbuilder_append_b_wf(Il2CppObject* self, bool value, int32_t width, Il2CppObject* format)
{
    return InterpAppendFormatted(self, icall_box_bool(value), width, format);
}
Il2CppObject* icall_stringbuilder_append_c_w(Il2CppObject* self, int32_t utf16, int32_t width)
{
    return InterpAppendFormatted(self, icall_box_char(utf16), width, nullptr);
}
Il2CppObject* icall_stringbuilder_append_c_f(Il2CppObject* self, int32_t utf16, Il2CppObject* format)
{
    return InterpAppendFormatted(self, icall_box_char(utf16), 0, format);
}
Il2CppObject* icall_stringbuilder_append_c_wf(Il2CppObject* self, int32_t utf16, int32_t width, Il2CppObject* format)
{
    return InterpAppendFormatted(self, icall_box_char(utf16), width, format);
}
Il2CppObject* icall_stringbuilder_append_s_w(Il2CppObject* self, Il2CppObject* value, int32_t width)
{
    return InterpAppendFormatted(self, value, width, nullptr);
}
Il2CppObject* icall_stringbuilder_append_s_f(Il2CppObject* self, Il2CppObject* value, Il2CppObject* format)
{
    return InterpAppendFormatted(self, value, 0, format);
}
Il2CppObject* icall_stringbuilder_append_s_wf(Il2CppObject* self, Il2CppObject* value, int32_t width, Il2CppObject* format)
{
    return InterpAppendFormatted(self, value, width, format);
}

} // namespace runtime
} // namespace il2cpp
