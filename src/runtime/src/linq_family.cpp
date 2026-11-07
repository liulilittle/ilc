//------------------------------------------------------------------------------
// linq_family.cpp -- the FAMILY-GENERIC LINQ element operators (see
// linq_family.h).
//
// One kernel serves every element family: the caller passes the cell code and
// the loops below read/write the element at its REAL width. This replaces the
// N-per-operator per-family explosion with N-per-cell-size accessors plus ONE
// loop. The narrow 1/2-byte cells are sign- or zero-extended into the callback
// slot exactly like the CLR's narrow-parameter rule.
//
// GC SAFETY follows the linq.cpp / linq_map.cpp contract: every managed
// pointer the loop holds is registered in a RootScope so a compaction (a
// collection fired by an allocation) rewrites the LOCAL itself; the element
// payload is never cached across a callback invocation.
//
// Equality follows EqualityComparer<T>.Default:
//   * numeric primitives -- value equality (float/double via the CLR's
//     Single.Equals / Double.Equals: NaN == NaN, +0.0 == -0.0);
//   * decimal           -- icall_decimal_equals (scale-insensitive);
//   * DateTime/TimeSpan -- ticks equality (DateTime also compares Kind? No:
//     net48 DateTime.Equals compares the INTERNAL TICKS ONLY, kind excluded);
//   * Guid              -- il2cpp_guid_equals (16-byte value equality);
//   * reference         -- Object.Equals: identity first, then string content
//     (ordinal), then a boxed-value comparison; a user class's Equals override
//     is NOT dispatched here because LINQ over an array of user classes goes
//     through the reference cell and net48's EqualityComparer<T>.Default does
//     dispatch T.Equals -- see the documented limitation in the header.
//------------------------------------------------------------------------------

#include "linq_family.h"
#include "linq_list.h"                 // il2cpp_linq_list_snapshot (List<int> inners)
#include "listi4.h"                    // il2cpp_list_i4_class (List<int> inners)

#include "arraysort.h"                 // FindVirtualSlotByNameAndSignature
#include "il2cpp_array.h"              // kElemKind* (b5-gaps section 70 narrow Join)
#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_value_semantics.h"    // icall_object_equals
#include "string_compare.h"            // il2cpp_string_compare_ordinal

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <new>
#include <vector>

namespace il2cpp {

namespace {

class RootScope
{
public:
    RootScope() {}
    ~RootScope()
    {
        for (size_t i = m_slots.size(); i > 0; --i)
            il2cpp_gc_remove_static_root(m_slots[i - 1]);
    }
    void Add(void** slot)
    {
        try
        {
            m_slots.push_back(slot);
        }
        catch (const std::bad_alloc&)
        {
            il2cpp_throw_new(
                "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                "Insufficient memory to continue the execution of the program.");
        }
        il2cpp_gc_add_static_root(slot);
    }

private:
    std::vector<void**> m_slots;
};

Il2CppArray* CheckSource(Il2CppArray* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
    }
    return src;
}

} // namespace

//------------------------------------------------------------------------------
// Cell descriptors.
//------------------------------------------------------------------------------
int32_t LinqCellSize(int32_t cell)
{
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_SBYTE:
        case IL2CPP_LINQ_CELL_BYTE:
        case IL2CPP_LINQ_CELL_BOOL:
            return 1;
        case IL2CPP_LINQ_CELL_SHORT:
        case IL2CPP_LINQ_CELL_USHORT:
        case IL2CPP_LINQ_CELL_CHAR:
            return 2;
        case IL2CPP_LINQ_CELL_UINT:
        case IL2CPP_LINQ_CELL_I4:
        case IL2CPP_LINQ_CELL_F:
            return 4;
        case IL2CPP_LINQ_CELL_I8:
        case IL2CPP_LINQ_CELL_D:
        case IL2CPP_LINQ_CELL_DATETIME:
        case IL2CPP_LINQ_CELL_TIMESPAN:
            return 8;
        case IL2CPP_LINQ_CELL_DECIMAL:
        case IL2CPP_LINQ_CELL_GUID:
        case IL2CPP_LINQ_CELL_DTOFFSET:
            return 16;
        default:
            return static_cast<int32_t>(sizeof(void*));
    }
}

bool LinqCellIsRef(int32_t cell)
{
    return cell == IL2CPP_LINQ_CELL_REF;
}

bool LinqCellIsI4Slot(int32_t cell)
{
    return cell <= IL2CPP_LINQ_CELL_I4;
}

char* LinqCellPtr(Il2CppArray* arr, int32_t index)
{
    return il2cpp_array_get_elem_ptr(arr, index);
}

Il2CppArray* LinqRequireCell(Il2CppArray* arr, int32_t cell)
{
    CheckSource(arr);
    if (arr->isRefElem != LinqCellIsRef(cell) || arr->elemSize != LinqCellSize(cell))
    {
        il2cpp_throw_not_supported_exception(
            "LINQ element family mismatch: the source array's element width does "
            "not match the dispatched cell family.");
    }
    return arr;
}

Il2CppArray* LinqNewArray(int32_t cell, int32_t length)
{
    if (cell == IL2CPP_LINQ_CELL_REF)
    {
        return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, length);
    }
    // The typed allocator records elemKind so downstream element-kind scans
    // (string.Join, Array.Sort) read the cell correctly.
    int32_t kind = 0;
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_I4: kind = 1; break;
        case IL2CPP_LINQ_CELL_I8: kind = 2; break;
        case IL2CPP_LINQ_CELL_F:  kind = 3; break;
        case IL2CPP_LINQ_CELL_D:  kind = 4; break;
        case IL2CPP_LINQ_CELL_UINT: kind = 5; break;
        case IL2CPP_LINQ_CELL_DATETIME: kind = 6; break;
        case IL2CPP_LINQ_CELL_TIMESPAN: kind = 7; break;
        // b5-gaps section 70: narrow families need distinct elemKinds so the
        // generic string.Join<T> formats bool as True/False and Int16/UInt16
        // as DECIMAL text rather than byte/char guesses (char keeps kind 0 --
        // elemSize 2 already formats it as a code unit).
        case IL2CPP_LINQ_CELL_BOOL:  kind = kElemKindBool; break;
        case IL2CPP_LINQ_CELL_SBYTE: kind = kElemKindS1; break;
        case IL2CPP_LINQ_CELL_SHORT: kind = kElemKindS2; break;
        case IL2CPP_LINQ_CELL_USHORT: kind = kElemKindU2; break;
        default: kind = 0; break; // 1/2-byte + decimal + guid: elemSize decides
    }
    return il2cpp_array_new_typed(LinqCellSize(cell), kind, length);
}

//------------------------------------------------------------------------------
// Element reads.
//------------------------------------------------------------------------------
int32_t LinqCellReadI4(Il2CppArray* arr, int32_t index, int32_t cell)
{
    char* p = LinqCellPtr(arr, index);
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_SBYTE:  return static_cast<int32_t>(*reinterpret_cast<int8_t*>(p));
        case IL2CPP_LINQ_CELL_BYTE:   return static_cast<int32_t>(*reinterpret_cast<uint8_t*>(p));
        case IL2CPP_LINQ_CELL_BOOL:   return *reinterpret_cast<uint8_t*>(p) != 0 ? 1 : 0;
        case IL2CPP_LINQ_CELL_SHORT:  return static_cast<int32_t>(*reinterpret_cast<int16_t*>(p));
        case IL2CPP_LINQ_CELL_USHORT: return static_cast<int32_t>(*reinterpret_cast<uint16_t*>(p));
        case IL2CPP_LINQ_CELL_CHAR:   return static_cast<int32_t>(*reinterpret_cast<char16_t*>(p));
        case IL2CPP_LINQ_CELL_UINT:   return static_cast<int32_t>(*reinterpret_cast<uint32_t*>(p));
        default:                      return *reinterpret_cast<int32_t*>(p);
    }
}

int64_t LinqCellReadI8(Il2CppArray* arr, int32_t index, int32_t cell)
{
    if (cell == IL2CPP_LINQ_CELL_I8)
    {
        return *reinterpret_cast<int64_t*>(LinqCellPtr(arr, index));
    }
    if (cell == IL2CPP_LINQ_CELL_TIMESPAN)
    {
        return *reinterpret_cast<int64_t*>(LinqCellPtr(arr, index));
    }
    if (cell == IL2CPP_LINQ_CELL_DATETIME)
    {
        // The array image packs the 2-bit kind into the top of the word; the
        // tick count is the low 62 bits.
        const uint64_t raw = static_cast<uint64_t>(
            *reinterpret_cast<int64_t*>(LinqCellPtr(arr, index)));
        return static_cast<int64_t>(raw & static_cast<uint64_t>(runtime::kDateTimeArrayTickMask));
    }
    if (cell <= IL2CPP_LINQ_CELL_I4)
    {
        return static_cast<int64_t>(LinqCellReadI4(arr, index, cell));
    }
    return static_cast<int64_t>(*reinterpret_cast<double*>(LinqCellPtr(arr, index)));
}

double LinqCellReadD(Il2CppArray* arr, int32_t index, int32_t cell)
{
    char* p = LinqCellPtr(arr, index);
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_F:
            // The F-stack carrier: a float CELL is 4 bytes, but this runtime's
            // own float-result arrays are 8-byte double cells carrying the
            // float's bits in the low 4 bytes. Follow the ARRAY's real width.
            return arr->elemSize == 8 ? *reinterpret_cast<double*>(p)
                                      : static_cast<double>(*reinterpret_cast<float*>(p));
        case IL2CPP_LINQ_CELL_D:
            return *reinterpret_cast<double*>(p);
        case IL2CPP_LINQ_CELL_I8:
        case IL2CPP_LINQ_CELL_TIMESPAN:
            return static_cast<double>(*reinterpret_cast<int64_t*>(p));
        case IL2CPP_LINQ_CELL_DATETIME:
            return static_cast<double>(LinqCellReadI8(arr, index, cell));
        default:
            return static_cast<double>(LinqCellReadI4(arr, index, cell));
    }
}

Il2CppObject* LinqCellReadRef(Il2CppArray* arr, int32_t index, int32_t cell)
{
    (void)cell;
    return *reinterpret_cast<Il2CppObject**>(LinqCellPtr(arr, index));
}

runtime::Il2CppDecimal LinqCellReadDec(Il2CppArray* arr, int32_t index)
{
    runtime::Il2CppDecimal v;
    std::memcpy(&v, LinqCellPtr(arr, index), sizeof(v));
    return v;
}

runtime::Il2CppDateTime LinqCellReadDt(Il2CppArray* arr, int32_t index)
{
    return runtime::il2cpp_datetime_from_array_bits(
        *reinterpret_cast<int64_t*>(LinqCellPtr(arr, index)));
}

runtime::Il2CppTimeSpan LinqCellReadTs(Il2CppArray* arr, int32_t index)
{
    return runtime::Il2CppTimeSpan(*reinterpret_cast<int64_t*>(LinqCellPtr(arr, index)));
}

Il2CppGuid LinqCellReadGuid(Il2CppArray* arr, int32_t index)
{
    Il2CppGuid g;
    std::memcpy(g.b, LinqCellPtr(arr, index), sizeof(g.b));
    return g;
}

runtime::Il2CppDateTimeOffset LinqCellReadDto(Il2CppArray* arr, int32_t index)
{
    runtime::Il2CppDateTimeOffset v;
    std::memcpy(&v, LinqCellPtr(arr, index), sizeof(v));
    return v;
}

void LinqCellDefault(char* dst, int32_t cell)
{
    if (cell == IL2CPP_LINQ_CELL_REF)
    {
        *reinterpret_cast<Il2CppObject**>(dst) = nullptr;
        return;
    }
    std::memset(dst, 0, static_cast<size_t>(LinqCellSize(cell)));
}

void LinqCellCopy(char* dst, Il2CppObject** barrierSlot, Il2CppArray* src,
                  int32_t index, int32_t cell)
{
    const int32_t width = LinqCellSize(cell);
    if (cell == IL2CPP_LINQ_CELL_REF)
    {
        Il2CppObject* v = *reinterpret_cast<Il2CppObject**>(LinqCellPtr(src, index));
        if (barrierSlot != nullptr)
        {
            il2cpp_write_barrier(reinterpret_cast<void**>(barrierSlot), v);
        }
        *reinterpret_cast<Il2CppObject**>(dst) = v;
        return;
    }
    std::memcpy(dst, LinqCellPtr(src, index), static_cast<size_t>(width));
}

//------------------------------------------------------------------------------
// Equality (EqualityComparer<T>.Default).
//------------------------------------------------------------------------------
bool LinqCellEquals(Il2CppArray* a, int32_t ia, Il2CppArray* b, int32_t ib,
                    int32_t cell)
{
    if (LinqCellIsI4Slot(cell))
    {
        return LinqCellReadI4(a, ia, cell) == LinqCellReadI4(b, ib, cell);
    }
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_I8:
            return LinqCellReadI8(a, ia, cell) == LinqCellReadI8(b, ib, cell);
        case IL2CPP_LINQ_CELL_F:
        case IL2CPP_LINQ_CELL_D:
        {
            // net48 Single/Double.Equals: NaN equals NaN, +0.0 equals -0.0.
            const double x = LinqCellReadD(a, ia, cell);
            const double y = LinqCellReadD(b, ib, cell);
            return x == y || (x != x && y != y);
        }
        case IL2CPP_LINQ_CELL_DECIMAL:
            return runtime::icall_decimal_equals(
                reinterpret_cast<const runtime::Il2CppDecimal*>(LinqCellPtr(a, ia)),
                reinterpret_cast<const runtime::Il2CppDecimal*>(LinqCellPtr(b, ib)));
        case IL2CPP_LINQ_CELL_DATETIME:
            // net48 DateTime.Equals compares the internal tick count only (the
            // Kind is not part of equality).
            return LinqCellReadI8(a, ia, cell) == LinqCellReadI8(b, ib, cell);
        case IL2CPP_LINQ_CELL_TIMESPAN:
            return LinqCellReadI8(a, ia, cell) == LinqCellReadI8(b, ib, cell);
        case IL2CPP_LINQ_CELL_GUID:
            return std::memcmp(LinqCellPtr(a, ia), LinqCellPtr(b, ib), 16) == 0;
        case IL2CPP_LINQ_CELL_DTOFFSET:
        {
            // net48 DateTimeOffset.Equals(DateTimeOffset): equal UTC instants
            // (the offsets may differ).
            runtime::Il2CppDateTimeOffset x;
            runtime::Il2CppDateTimeOffset y;
            std::memcpy(&x, LinqCellPtr(a, ia), sizeof(x));
            std::memcpy(&y, LinqCellPtr(b, ib), sizeof(y));
            return runtime::icall_dtoffset_equals(x, y);
        }
        default:
            return runtime::icall_object_equals(
                LinqCellReadRef(a, ia, cell), LinqCellReadRef(b, ib, cell));
    }
}

bool LinqCellEqualsI4(Il2CppArray* a, int32_t ia, int32_t value, int32_t cell)
{
    if (cell == IL2CPP_LINQ_CELL_F || cell == IL2CPP_LINQ_CELL_D)
    {
        return LinqCellEqualsD(a, ia, static_cast<double>(value), cell);
    }
    return LinqCellReadI4(a, ia, cell) == value;
}

bool LinqCellEqualsI8(Il2CppArray* a, int32_t ia, int64_t value, int32_t cell)
{
    if (LinqCellIsI4Slot(cell))
    {
        return static_cast<int64_t>(LinqCellReadI4(a, ia, cell)) == value;
    }
    return LinqCellReadI8(a, ia, cell) == value;
}

bool LinqCellEqualsD(Il2CppArray* a, int32_t ia, double value, int32_t cell)
{
    if (LinqCellIsI4Slot(cell) || cell == IL2CPP_LINQ_CELL_I8)
    {
        return LinqCellReadD(a, ia, cell) == value;
    }
    if (cell == IL2CPP_LINQ_CELL_F || cell == IL2CPP_LINQ_CELL_D)
    {
        const double x = LinqCellReadD(a, ia, cell);
        return x == value || (x != x && value != value);
    }
    return LinqCellReadD(a, ia, cell) == value;
}

bool LinqCellEqualsRef(Il2CppArray* a, int32_t ia, Il2CppObject* value, int32_t cell)
{
    return runtime::icall_object_equals(LinqCellReadRef(a, ia, cell), value);
}

bool LinqCellEqualsDec(Il2CppArray* a, int32_t ia, runtime::Il2CppDecimal value)
{
    return runtime::icall_decimal_equals(
        reinterpret_cast<const runtime::Il2CppDecimal*>(LinqCellPtr(a, ia)), &value);
}

bool LinqCellEqualsDt(Il2CppArray* a, int32_t ia, runtime::Il2CppDateTime value)
{
    return LinqCellReadI8(a, ia, IL2CPP_LINQ_CELL_DATETIME) == value.ticks;
}

bool LinqCellEqualsTs(Il2CppArray* a, int32_t ia, runtime::Il2CppTimeSpan value)
{
    return LinqCellReadI8(a, ia, IL2CPP_LINQ_CELL_TIMESPAN) == value.ticks;
}

bool LinqCellEqualsGuid(Il2CppArray* a, int32_t ia, Il2CppGuid value)
{
    return std::memcmp(LinqCellPtr(a, ia), value.b, sizeof(value.b)) == 0;
}

bool LinqCellEqualsDto(Il2CppArray* a, int32_t ia, runtime::Il2CppDateTimeOffset value)
{
    runtime::Il2CppDateTimeOffset x;
    std::memcpy(&x, LinqCellPtr(a, ia), sizeof(x));
    return runtime::icall_dtoffset_equals(x, value);
}

//------------------------------------------------------------------------------
// Family-generic sequence operators (no callback).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_take_family(Il2CppArray* src, int32_t count, int32_t cell)
{
    LinqRequireCell(src, cell);
    const int32_t n = count < 0 ? 0 : (count > src->length ? src->length : count);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = LinqNewArray(cell, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i)
    {
        char* dst = LinqCellPtr(out, i);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), src, i, cell);
    }
    return out;
}

Il2CppArray* il2cpp_linq_skip_family(Il2CppArray* src, int32_t count, int32_t cell)
{
    LinqRequireCell(src, cell);
    const int32_t start = count <= 0 ? 0 : (count > src->length ? src->length : count);
    const int32_t n = src->length - start;
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = LinqNewArray(cell, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i)
    {
        char* dst = LinqCellPtr(out, i);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), src, start + i, cell);
    }
    return out;
}

Il2CppArray* il2cpp_linq_reverse_family(Il2CppArray* src, int32_t cell)
{
    LinqRequireCell(src, cell);
    const int32_t n = src->length;
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = LinqNewArray(cell, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i)
    {
        char* dst = LinqCellPtr(out, i);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), src, n - 1 - i, cell);
    }
    return out;
}

Il2CppArray* il2cpp_linq_concat_family(Il2CppArray* first, Il2CppArray* second, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    const int64_t total = static_cast<int64_t>(first->length) + second->length;
    if (total > INT32_MAX)
    {
        il2cpp_throw_overflow_exception("The concatenated array length exceeds Int32.MaxValue.");
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    Il2CppArray* out = LinqNewArray(cell, static_cast<int32_t>(total));
    roots.Add(reinterpret_cast<void**>(&out));
    const int32_t n1 = first->length;
    const int32_t n2 = second->length;
    for (int32_t i = 0; i < n1; ++i)
    {
        char* dst = LinqCellPtr(out, i);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), first, i, cell);
    }
    for (int32_t i = 0; i < n2; ++i)
    {
        char* dst = LinqCellPtr(out, n1 + i);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), second, i, cell);
    }
    return out;
}

namespace {

// The index of the first element in `seen` equal to (arr, index), or -1.
// `seen` is an array of source indices so reference cells stay rooted.
int32_t FindSeen(Il2CppArray* arr, std::vector<int32_t>& seen, int32_t index, int32_t cell)
{
    for (size_t k = 0; k < seen.size(); ++k)
    {
        if (LinqCellEquals(arr, seen[k], arr, index, cell))
        {
            return static_cast<int32_t>(k);
        }
    }
    return -1;
}

// Materialize the elements at `order` (indices into `src`) into a fresh array.
Il2CppArray* MaterializeIndices(Il2CppArray* src, const std::vector<int32_t>& order, int32_t cell)
{
    Il2CppArray* out = LinqNewArray(cell, static_cast<int32_t>(order.size()));
    for (size_t i = 0; i < order.size(); ++i)
    {
        char* dst = LinqCellPtr(out, static_cast<int32_t>(i));
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), src, order[i], cell);
    }
    return out;
}

} // namespace

Il2CppArray* il2cpp_linq_distinct_family(Il2CppArray* src, int32_t cell)
{
    LinqRequireCell(src, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (FindSeen(src, order, i, cell) < 0)
        {
            order.push_back(i);
        }
    }
    Il2CppArray* out = MaterializeIndices(src, order, cell);
    return out;
}

Il2CppArray* il2cpp_linq_except_family(Il2CppArray* first, Il2CppArray* second, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool inSecond = false;
        for (int32_t j = 0; j < second->length; ++j)
        {
            if (LinqCellEquals(first, i, second, j, cell))
            {
                inSecond = true;
                break;
            }
        }
        if (inSecond) continue;
        if (FindSeen(first, order, i, cell) >= 0) continue;
        order.push_back(i);
    }
    Il2CppArray* res = MaterializeIndices(first, order, cell);
    return res;
}

Il2CppArray* il2cpp_linq_union_family(Il2CppArray* first, Il2CppArray* second, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));

    // Union yields first's elements then second's, deduplicated by
    // first-appearance. The order vector carries (source, index) pairs; the
    // two sources are kept apart so each element is read from its own array.
    std::vector<int32_t> fromFirst;
    std::vector<int32_t> fromSecond;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool dup = false;
        for (size_t k = 0; k < fromFirst.size() && !dup; ++k)
            dup = LinqCellEquals(first, fromFirst[k], first, i, cell);
        for (size_t k = 0; k < fromSecond.size() && !dup; ++k)
            dup = LinqCellEquals(second, fromSecond[k], first, i, cell);
        if (!dup) fromFirst.push_back(i);
    }
    for (int32_t i = 0; i < second->length; ++i)
    {
        bool dup = false;
        for (size_t k = 0; k < fromFirst.size() && !dup; ++k)
            dup = LinqCellEquals(first, fromFirst[k], second, i, cell);
        for (size_t k = 0; k < fromSecond.size() && !dup; ++k)
            dup = LinqCellEquals(second, fromSecond[k], second, i, cell);
        if (!dup) fromSecond.push_back(i);
    }

    Il2CppArray* out = LinqNewArray(
        cell, static_cast<int32_t>(fromFirst.size() + fromSecond.size()));
    roots.Add(reinterpret_cast<void**>(&out));
    int32_t w = 0;
    for (size_t k = 0; k < fromFirst.size(); ++k, ++w)
    {
        char* dst = LinqCellPtr(out, w);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), first, fromFirst[k], cell);
    }
    for (size_t k = 0; k < fromSecond.size(); ++k, ++w)
    {
        char* dst = LinqCellPtr(out, w);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), second, fromSecond[k], cell);
    }
    return out;
}

Il2CppArray* il2cpp_linq_intersect_family(Il2CppArray* first, Il2CppArray* second, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool inSecond = false;
        for (int32_t j = 0; j < second->length; ++j)
        {
            if (LinqCellEquals(first, i, second, j, cell))
            {
                inSecond = true;
                break;
            }
        }
        if (!inSecond) continue;
        if (FindSeen(first, order, i, cell) >= 0) continue;
        order.push_back(i);
    }
    return MaterializeIndices(first, order, cell);
}

Il2CppArray* il2cpp_linq_default_if_empty_family(Il2CppArray* src, int32_t cell)
{
    LinqRequireCell(src, cell);
    if (src->length != 0)
    {
        return src;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = LinqNewArray(cell, 1);
    roots.Add(reinterpret_cast<void**>(&out));
    char* dst = LinqCellPtr(out, 0);
    LinqCellDefault(dst, cell);
    return out;
}

int64_t il2cpp_linq_long_count_family(Il2CppArray* src, int32_t cell)
{
    LinqRequireCell(src, cell);
    return static_cast<int64_t>(src->length);
}

bool il2cpp_linq_sequence_equal_family(Il2CppArray* first, Il2CppArray* second, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    if (first->length != second->length)
    {
        return false;
    }
    for (int32_t i = 0; i < first->length; ++i)
    {
        if (!LinqCellEquals(first, i, second, i, cell))
        {
            return false;
        }
    }
    return true;
}

//------------------------------------------------------------------------------
// Contains (the value rides the family's parameter slot).
//------------------------------------------------------------------------------
bool il2cpp_linq_contains_family_i4(Il2CppArray* src, int32_t value, int32_t cell)
{
    LinqRequireCell(src, cell);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsI4(src, i, value, cell)) return true;
    }
    return false;
}

bool il2cpp_linq_contains_family_i8(Il2CppArray* src, int64_t value, int32_t cell)
{
    LinqRequireCell(src, cell);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsI8(src, i, value, cell)) return true;
    }
    return false;
}

bool il2cpp_linq_contains_family_d(Il2CppArray* src, double value, int32_t cell)
{
    LinqRequireCell(src, cell);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsD(src, i, value, cell)) return true;
    }
    return false;
}

bool il2cpp_linq_contains_family_ref(Il2CppArray* src, Il2CppObject* value, int32_t cell)
{
    LinqRequireCell(src, cell);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsRef(src, i, value, cell)) return true;
    }
    return false;
}

bool il2cpp_linq_contains_family_dec(Il2CppArray* src, runtime::Il2CppDecimal value)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_DECIMAL);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsDec(src, i, value)) return true;
    }
    return false;
}

bool il2cpp_linq_contains_family_dt(Il2CppArray* src, runtime::Il2CppDateTime value)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_DATETIME);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsDt(src, i, value)) return true;
    }
    return false;
}

bool il2cpp_linq_contains_family_ts(Il2CppArray* src, runtime::Il2CppTimeSpan value)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_TIMESPAN);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsTs(src, i, value)) return true;
    }
    return false;
}

bool il2cpp_linq_contains_family_guid(Il2CppArray* src, Il2CppGuid value)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_GUID);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsGuid(src, i, value)) return true;
    }
    return false;
}

bool il2cpp_linq_contains_family_dtoffset(Il2CppArray* src,
                                          runtime::Il2CppDateTimeOffset value)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_DTOFFSET);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqCellEqualsDto(src, i, value)) return true;
    }
    return false;
}

//------------------------------------------------------------------------------
// ElementAt / Single / SingleOrDefault (per result ABI).
//
// Each entry's `arg` is the index (ElementAt) or unused (Single); it exists so
// the per-ABI macro keeps ONE signature shape for all three operators.
//------------------------------------------------------------------------------
namespace {

int32_t CheckedIndex(Il2CppArray* src, int32_t index)
{
    if (index < 0 || index >= src->length)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "index", "Index was out of range. Must be non-negative and less than the "
                     "size of the collection.");
    }
    return index;
}

void RequireSingle(Il2CppArray* src)
{
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    if (src->length > 1)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    }
}

void RequireNothing(Il2CppArray* src)
{
    (void)src;
}

void RequireAtMostOne(Il2CppArray* src)
{
    if (src->length > 1)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    }
}

} // namespace

#define IL2CPP_LINQ_DEF_ELEM_ENTRIES(NAME, CHECK, INDEXCODE, EMPTY_RET)                     \
int32_t il2cpp_linq_##NAME##_fam_i4(Il2CppArray* src, int32_t arg, int32_t cell) \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    if (EMPTY_RET && src->length == 0) return 0;                                 \
    return LinqCellReadI4(src, INDEXCODE, cell);                                 \
}                                                                                \
int64_t il2cpp_linq_##NAME##_fam_i8(Il2CppArray* src, int32_t arg, int32_t cell) \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    if (EMPTY_RET && src->length == 0) return 0;                                 \
    return LinqCellReadI8(src, INDEXCODE, cell);                                 \
}                                                                                \
double il2cpp_linq_##NAME##_fam_d(Il2CppArray* src, int32_t arg, int32_t cell)   \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    if (EMPTY_RET && src->length == 0) return 0.0;                              \
    return LinqCellReadD(src, INDEXCODE, cell);                                  \
}                                                                                \
Il2CppObject* il2cpp_linq_##NAME##_fam_ref(Il2CppArray* src, int32_t arg, int32_t cell) \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    if (EMPTY_RET && src->length == 0) return nullptr;                          \
    return LinqCellReadRef(src, INDEXCODE, cell);                                \
}                                                                                \
runtime::Il2CppDecimal il2cpp_linq_##NAME##_fam_dec(Il2CppArray* src, int32_t arg, int32_t cell) \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    runtime::Il2CppDecimal v;                                                    \
    if (EMPTY_RET && src->length == 0) { std::memset(&v, 0, sizeof(v)); return v; } \
    return LinqCellReadDec(src, INDEXCODE);                                      \
}                                                                                \
runtime::Il2CppDateTime il2cpp_linq_##NAME##_fam_dt(Il2CppArray* src, int32_t arg, int32_t cell) \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    if (EMPTY_RET && src->length == 0) return runtime::Il2CppDateTime();        \
    return LinqCellReadDt(src, INDEXCODE);                                       \
}                                                                                \
runtime::Il2CppTimeSpan il2cpp_linq_##NAME##_fam_ts(Il2CppArray* src, int32_t arg, int32_t cell) \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    if (EMPTY_RET && src->length == 0) return runtime::Il2CppTimeSpan();        \
    return LinqCellReadTs(src, INDEXCODE);                                       \
}                                                                                \
Il2CppGuid il2cpp_linq_##NAME##_fam_guid(Il2CppArray* src, int32_t arg, int32_t cell) \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    if (EMPTY_RET && src->length == 0) { Il2CppGuid g; std::memset(g.b, 0, sizeof(g.b)); return g; } \
    return LinqCellReadGuid(src, INDEXCODE);                                     \
}                                                                                \
runtime::Il2CppDateTimeOffset il2cpp_linq_##NAME##_fam_dtoffset(Il2CppArray* src, int32_t arg, \
                                                                int32_t cell)    \
{                                                                                \
    LinqRequireCell(src, cell);                                                  \
    CHECK(src);                                                                  \
    if (EMPTY_RET && src->length == 0) return runtime::Il2CppDateTimeOffset{0, 0}; \
    return LinqCellReadDto(src, INDEXCODE);                                      \
}

// ElementAt has no empty-sequence special case: an index into an empty array
// is out of range (the CHECK throws before the read).
IL2CPP_LINQ_DEF_ELEM_ENTRIES(element_at, RequireNothing, CheckedIndex(src, arg), false)

// Single / SingleOrDefault are source-only (no caller argument), so their
// kernels take (src, cell) only.
#define IL2CPP_LINQ_DEF_ELEM_ENTRIES_NOARG(NAME, CHECK, EMPTY_RET)                        \
int32_t il2cpp_linq_##NAME##_fam_i4(Il2CppArray* src, int32_t cell)            \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    if (EMPTY_RET && src->length == 0) return 0;                               \
    return LinqCellReadI4(src, 0, cell);                                       \
}                                                                              \
int64_t il2cpp_linq_##NAME##_fam_i8(Il2CppArray* src, int32_t cell)            \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    if (EMPTY_RET && src->length == 0) return 0;                               \
    return LinqCellReadI8(src, 0, cell);                                       \
}                                                                              \
double il2cpp_linq_##NAME##_fam_d(Il2CppArray* src, int32_t cell)              \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    if (EMPTY_RET && src->length == 0) return 0.0;                             \
    return LinqCellReadD(src, 0, cell);                                        \
}                                                                              \
Il2CppObject* il2cpp_linq_##NAME##_fam_ref(Il2CppArray* src, int32_t cell)     \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    if (EMPTY_RET && src->length == 0) return nullptr;                         \
    return LinqCellReadRef(src, 0, cell);                                      \
}                                                                              \
runtime::Il2CppDecimal il2cpp_linq_##NAME##_fam_dec(Il2CppArray* src, int32_t cell) \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    runtime::Il2CppDecimal v;                                                  \
    if (EMPTY_RET && src->length == 0) { std::memset(&v, 0, sizeof(v)); return v; } \
    return LinqCellReadDec(src, 0);                                            \
}                                                                              \
runtime::Il2CppDateTime il2cpp_linq_##NAME##_fam_dt(Il2CppArray* src, int32_t cell) \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    if (EMPTY_RET && src->length == 0) return runtime::Il2CppDateTime();       \
    return LinqCellReadDt(src, 0);                                             \
}                                                                              \
runtime::Il2CppTimeSpan il2cpp_linq_##NAME##_fam_ts(Il2CppArray* src, int32_t cell) \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    if (EMPTY_RET && src->length == 0) return runtime::Il2CppTimeSpan();       \
    return LinqCellReadTs(src, 0);                                             \
}                                                                              \
Il2CppGuid il2cpp_linq_##NAME##_fam_guid(Il2CppArray* src, int32_t cell)       \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    if (EMPTY_RET && src->length == 0) { Il2CppGuid g; std::memset(g.b, 0, sizeof(g.b)); return g; } \
    return LinqCellReadGuid(src, 0);                                           \
}                                                                              \
runtime::Il2CppDateTimeOffset il2cpp_linq_##NAME##_fam_dtoffset(Il2CppArray* src, int32_t cell) \
{                                                                              \
    LinqRequireCell(src, cell);                                                \
    CHECK(src);                                                                \
    if (EMPTY_RET && src->length == 0) return runtime::Il2CppDateTimeOffset{0, 0}; \
    return LinqCellReadDto(src, 0);                                            \
}

// Single: exactly one element (empty and >1 both throw).
IL2CPP_LINQ_DEF_ELEM_ENTRIES_NOARG(single, RequireSingle, false)
// SingleOrDefault: 0 or 1 (empty -> default, >1 throws).
IL2CPP_LINQ_DEF_ELEM_ENTRIES_NOARG(single_or_default, RequireAtMostOne, true)

#undef IL2CPP_LINQ_DEF_ELEM_ENTRIES
#undef IL2CPP_LINQ_DEF_ELEM_ENTRIES_NOARG

//------------------------------------------------------------------------------
// b5-gaps section 56: USER IEqualityComparer dispatchers over a REFERENCE
// element / key. `cmp` is nullable: null compares by icall_object_equals
// exactly like the plain kernels, so the default behavior never changes. The
// dispatch order mirrors hashset.cpp ComparerEquals: the StringComparer
// singletons have no typeinfo, so they get a direct weighted string compare;
// everything else rides the reflected virtual Equals(T,T) slot found by the
// "(r:" signature fragment.
//------------------------------------------------------------------------------
bool LinqRefEqualsCmp(Il2CppObject* cmp, Il2CppObject* a, Il2CppObject* b)
{
    if (cmp == nullptr)
    {
        return runtime::icall_object_equals(a, b);
    }
    if (cmp->klass != nullptr && cmp->klass->name != nullptr &&
        std::strncmp(cmp->klass->name, "System.StringComparer.", 22) == 0)
    {
        bool ignoreCase = std::strcmp(cmp->klass->name,
                                      "System.StringComparer.OrdinalIgnoreCase") == 0;
        return ignoreCase
            ? il2cpp_string_equals_ordinal_ignore_case(
                  reinterpret_cast<const Il2CppString*>(a),
                  reinterpret_cast<const Il2CppString*>(b))
            : il2cpp_string_equals_ordinal(
                  reinterpret_cast<const Il2CppString*>(a),
                  reinterpret_cast<const Il2CppString*>(b));
    }
    const int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Equals", "(r:");
    if (slot >= 0 && cmp->klass != nullptr && cmp->klass->vtable != nullptr &&
        slot < cmp->klass->vtableSize && cmp->klass->vtable[slot] != nullptr)
    {
        typedef bool (*EqFn)(Il2CppObject*, Il2CppObject*, Il2CppObject*);
        return reinterpret_cast<EqFn>(cmp->klass->vtable[slot])(cmp, a, b);
    }
    return runtime::icall_object_equals(a, b);
}

//------------------------------------------------------------------------------
// b5-gaps section 56: the user-comparer twins of the reference-element set /
// sequence operators. `cmp` is nullable; the reference cell is validated so a
// misplaced binding cannot silently compare a value family by reference.
//------------------------------------------------------------------------------
static inline bool LinqRefCmpEqCell(Il2CppArray* a, int32_t ia,
                                    Il2CppArray* b, int32_t ib,
                                    Il2CppObject* cmp, int32_t cell)
{
    return LinqRefEqualsCmp(cmp, LinqCellReadRef(a, ia, cell),
                            LinqCellReadRef(b, ib, cell));
}

// b5-gaps section 77: the unified USER-COMPARER cell-pair equality. A
// reference cell rides the section-56 reflected slot dispatch (LinqRefCmpEqCell);
// a 16-BYTE value cell (decimal/DateTime/TimeSpan/Guid/DateTimeOffset) is
// read in FULL via the family reader and compared through the comparer's
// reflected Equals slot with the family's OWN by-value ABI (LinqEqualsFragment16);
// every other value cell rides the section-57 LinqValueEqualsCmpCell dispatch.
// The definition sits below (after LinqValueEqualsCmpCell); the _family_cmp
// kernels forward-declare it so they can serve ANY cell.
bool LinqCmpEqCell(Il2CppObject* cmp, Il2CppArray* a, int32_t ia,
                   Il2CppArray* b, int32_t ib, int32_t cell);

Il2CppArray* il2cpp_linq_distinct_family_cmp(Il2CppArray* src, Il2CppObject* cmp,
                                             int32_t cell)
{
    LinqRequireCell(src, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < src->length; ++i)
    {
        bool seen = false;
        for (size_t k = 0; k < order.size() && !seen; ++k)
        {
            seen = LinqCmpEqCell(cmp, src, static_cast<int32_t>(order[k]), src, i, cell);
        }
        if (!seen) order.push_back(i);
    }
    return MaterializeIndices(src, order, cell);
}

Il2CppArray* il2cpp_linq_except_family_cmp(Il2CppArray* first, Il2CppArray* second,
                                           Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool inSecond = false;
        for (int32_t j = 0; j < second->length && !inSecond; ++j)
        {
            inSecond = LinqCmpEqCell(cmp, first, i, second, j, cell);
        }
        if (inSecond) continue;
        bool seen = false;
        for (size_t k = 0; k < order.size() && !seen; ++k)
        {
            seen = LinqCmpEqCell(cmp, first, static_cast<int32_t>(order[k]), first, i, cell);
        }
        if (!seen) order.push_back(i);
    }
    return MaterializeIndices(first, order, cell);
}

Il2CppArray* il2cpp_linq_union_family_cmp(Il2CppArray* first, Il2CppArray* second,
                                          Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    std::vector<int32_t> fromFirst;
    std::vector<int32_t> fromSecond;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool dup = false;
        for (size_t k = 0; k < fromFirst.size() && !dup; ++k)
        {
            dup = LinqCmpEqCell(cmp, first, static_cast<int32_t>(fromFirst[k]), first, i, cell);
        }
        for (size_t k = 0; k < fromSecond.size() && !dup; ++k)
        {
            dup = LinqCmpEqCell(cmp, second, static_cast<int32_t>(fromSecond[k]), first, i, cell);
        }
        if (!dup) fromFirst.push_back(i);
    }
    for (int32_t i = 0; i < second->length; ++i)
    {
        bool dup = false;
        for (size_t k = 0; k < fromFirst.size() && !dup; ++k)
        {
            dup = LinqCmpEqCell(cmp, first, static_cast<int32_t>(fromFirst[k]), second, i, cell);
        }
        for (size_t k = 0; k < fromSecond.size() && !dup; ++k)
        {
            dup = LinqCmpEqCell(cmp, second, static_cast<int32_t>(fromSecond[k]), second, i, cell);
        }
        if (!dup) fromSecond.push_back(i);
    }
    Il2CppArray* out = LinqNewArray(
        cell, static_cast<int32_t>(fromFirst.size() + fromSecond.size()));
    roots.Add(reinterpret_cast<void**>(&out));
    int32_t w = 0;
    for (size_t k = 0; k < fromFirst.size(); ++k, ++w)
    {
        char* dst = LinqCellPtr(out, w);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), first,
                     static_cast<int32_t>(fromFirst[k]), cell);
    }
    for (size_t k = 0; k < fromSecond.size(); ++k, ++w)
    {
        char* dst = LinqCellPtr(out, w);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), second,
                     static_cast<int32_t>(fromSecond[k]), cell);
    }
    return out;
}

Il2CppArray* il2cpp_linq_intersect_family_cmp(Il2CppArray* first, Il2CppArray* second,
                                              Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool inSecond = false;
        for (int32_t j = 0; j < second->length && !inSecond; ++j)
        {
            inSecond = LinqCmpEqCell(cmp, first, i, second, j, cell);
        }
        if (!inSecond) continue;
        bool seen = false;
        for (size_t k = 0; k < order.size() && !seen; ++k)
        {
            seen = LinqCmpEqCell(cmp, first, static_cast<int32_t>(order[k]), first, i, cell);
        }
        if (!seen) order.push_back(i);
    }
    return MaterializeIndices(first, order, cell);
}

bool il2cpp_linq_sequence_equal_family_cmp(Il2CppArray* first, Il2CppArray* second,
                                           Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    if (first->length != second->length) return false;
    for (int32_t i = 0; i < first->length; ++i)
    {
        if (!LinqCmpEqCell(cmp, first, i, second, i, cell)) return false;
    }
    return true;
}

bool il2cpp_linq_contains_family_ref_cmp(Il2CppArray* src, Il2CppObject* value,
                                         Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(src, cell);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqRefEqualsCmp(cmp, LinqCellReadRef(src, i, cell), value)) return true;
    }
    return false;
}

// Forward declaration: the 16-byte cell comparer equality helper is defined
// below inside the section-57 anonymous namespace; the Contains cmp twins
// (outside it) call it.
namespace {
bool LinqValueEqualsCmpCell16(Il2CppObject* cmp, int32_t cell,
                              const char* a, const char* b);
const char* LinqEqualsFragment16(int32_t cell);
} // namespace

// b5-gaps section 77: Contains(value, comparer) over the 16-BYTE value
// families. The value crosses by-value in its family's OWN parameter slot
// (Il2CppDecimal / Il2CppDateTime / Il2CppTimeSpan / Il2CppGuid /
// Il2CppDateTimeOffset); each element is compared through the user comparer's
// reflected Equals slot with the section-77 cell dispatcher.
bool il2cpp_linq_contains_family_cmp_dec(Il2CppArray* src, runtime::Il2CppDecimal value,
                                         Il2CppObject* cmp)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_DECIMAL);
    for (int32_t i = 0; i < src->length; ++i)
    {
        const char* p = LinqCellPtr(src, i);
        if (LinqValueEqualsCmpCell16(cmp, IL2CPP_LINQ_CELL_DECIMAL, p,
                                     reinterpret_cast<const char*>(&value)))
        {
            return true;
        }
    }
    return false;
}

bool il2cpp_linq_contains_family_cmp_dt(Il2CppArray* src, runtime::Il2CppDateTime value,
                                        Il2CppObject* cmp)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_DATETIME);
    const int64_t vb = runtime::il2cpp_datetime_to_array_bits(value);
    for (int32_t i = 0; i < src->length; ++i)
    {
        const char* p = LinqCellPtr(src, i);
        if (LinqValueEqualsCmpCell16(cmp, IL2CPP_LINQ_CELL_DATETIME, p,
                                     reinterpret_cast<const char*>(&vb)))
        {
            return true;
        }
    }
    return false;
}

bool il2cpp_linq_contains_family_cmp_ts(Il2CppArray* src, runtime::Il2CppTimeSpan value,
                                        Il2CppObject* cmp)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_TIMESPAN);
    for (int32_t i = 0; i < src->length; ++i)
    {
        const char* p = LinqCellPtr(src, i);
        if (LinqValueEqualsCmpCell16(cmp, IL2CPP_LINQ_CELL_TIMESPAN, p,
                                     reinterpret_cast<const char*>(&value)))
        {
            return true;
        }
    }
    return false;
}

bool il2cpp_linq_contains_family_cmp_guid(Il2CppArray* src, Il2CppGuid value,
                                          Il2CppObject* cmp)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_GUID);
    for (int32_t i = 0; i < src->length; ++i)
    {
        const char* p = LinqCellPtr(src, i);
        if (LinqValueEqualsCmpCell16(cmp, IL2CPP_LINQ_CELL_GUID, p,
                                     reinterpret_cast<const char*>(&value)))
        {
            return true;
        }
    }
    return false;
}

bool il2cpp_linq_contains_family_cmp_dtoffset(Il2CppArray* src,
                                              runtime::Il2CppDateTimeOffset value,
                                              Il2CppObject* cmp)
{
    LinqRequireCell(src, IL2CPP_LINQ_CELL_DTOFFSET);
    for (int32_t i = 0; i < src->length; ++i)
    {
        const char* p = LinqCellPtr(src, i);
        if (LinqValueEqualsCmpCell16(cmp, IL2CPP_LINQ_CELL_DTOFFSET, p,
                                     reinterpret_cast<const char*>(&value)))
        {
            return true;
        }
    }
    return false;
}

//------------------------------------------------------------------------------
// b5-gaps section 57: user-comparer equality over a VALUE cell (int32/uint /
// narrow / char / bool / int64 / double / float). The reflected
// IEqualityComparer<T>.Equals(T,T) slot is keyed by the element family
// fragment; the narrow kinds all ride the int32 slot ABI (the M1 contract),
// float rides the double ABI exactly like the arraysort comparers.
//------------------------------------------------------------------------------
namespace {

const char* LinqEqualsFragment(int32_t cell)
{
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_SBYTE: return "(i1,i1)";
        case IL2CPP_LINQ_CELL_BYTE:  return "(u1,u1)";
        case IL2CPP_LINQ_CELL_BOOL:  return "(b,b)";
        case IL2CPP_LINQ_CELL_SHORT: return "(i2,i2)";
        case IL2CPP_LINQ_CELL_USHORT: return "(u2,u2)";
        case IL2CPP_LINQ_CELL_CHAR:  return "(c,c)";
        case IL2CPP_LINQ_CELL_UINT:
        case IL2CPP_LINQ_CELL_I4:    return "(i,i)";
        case IL2CPP_LINQ_CELL_I8:    return "(l,l)";
        case IL2CPP_LINQ_CELL_F:     return "(f,f)";
        case IL2CPP_LINQ_CELL_D:     return "(d,d)";
        default:                     return nullptr;
    }
}

bool LinqValueEqualsCmpCell(Il2CppObject* cmp, int32_t cell,
                            int64_t a, int64_t b)
{
    const char* frag = LinqEqualsFragment(cell);
    if (frag == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "a user IEqualityComparer over this value cell family is not wired "
            "(b5-gaps section 57: decimal/DateTime/TimeSpan/Guid/DateTimeOffset "
            "comparers stay LOUD).");
        return false;
    }
    const int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Equals", frag);
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        char message[192];
        std::snprintf(message, sizeof(message),
                      "IEqualityComparer.Equals dispatch: the comparer type has no "
                      "reflected virtual Equals(%s) slot (b5-gaps section 57).",
                      frag);
        il2cpp_throw_not_supported_exception(message);
        return false;
    }
    if (cell == IL2CPP_LINQ_CELL_I8)
    {
        typedef bool (*EqI8Fn)(Il2CppObject*, int64_t, int64_t);
        return reinterpret_cast<EqI8Fn>(cmp->klass->vtable[slot])(cmp, a, b);
    }
    if (cell == IL2CPP_LINQ_CELL_F || cell == IL2CPP_LINQ_CELL_D)
    {
        typedef bool (*EqDFn)(Il2CppObject*, double, double);
        double da, db;
        std::memcpy(&da, &a, sizeof(double));
        std::memcpy(&db, &b, sizeof(double));
        return reinterpret_cast<EqDFn>(cmp->klass->vtable[slot])(cmp, da, db);
    }
    typedef bool (*EqI4Fn)(Il2CppObject*, int32_t, int32_t);
    return reinterpret_cast<EqI4Fn>(cmp->klass->vtable[slot])(
        cmp, static_cast<int32_t>(a), static_cast<int32_t>(b));
}

// b5-gaps section 77: the reflected-Equals signature fragment of the 16-BYTE
// value cell families (decimal / DateTime / TimeSpan / Guid / DateTimeOffset).
// The comparer's Equals(TKey,TKey) vtable signature is encoded from the FULL
// type name (section 76 measured "b(V:System.Decimal,V:System.Decimal)" for
// a decimal IEqualityComparer, etc.). Null for every cell without a fixed
// name (the section-57 cell families answer their own short fragments).
const char* LinqEqualsFragment16(int32_t cell)
{
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_DECIMAL:   return "(V:System.Decimal,V:System.Decimal)";
        case IL2CPP_LINQ_CELL_DATETIME:  return "(V:System.DateTime,V:System.DateTime)";
        case IL2CPP_LINQ_CELL_TIMESPAN:  return "(V:System.TimeSpan,V:System.TimeSpan)";
        case IL2CPP_LINQ_CELL_GUID:      return "(V:System.Guid,V:System.Guid)";
        case IL2CPP_LINQ_CELL_DTOFFSET:  return "(V:System.DateTimeOffset,V:System.DateTimeOffset)";
        default:                         return nullptr;
    }
}

// b5-gaps section 77: the 16-BYTE value-cell USER-COMPARER equality. The two
// AZV cells are read in FULL through the family readers, then the comparer's
// reflected virtual Equals(TKey,TKey) slot is invoked through the family's
// OWN by-value ABI (Il2CppDecimal / Il2CppDateTime / Il2CppTimeSpan /
// Il2CppGuid / Il2CppDateTimeOffset) -- exactly the section-76 key-domain
// engine, generalized to the LINQ cell domain.
bool LinqValueEqualsCmpCell16(Il2CppObject* cmp, int32_t cell,
                              const char* a, const char* b)
{
    const char* frag = LinqEqualsFragment16(cell);
    if (frag == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "a user IEqualityComparer over this 16-byte value cell family is not "
            "wired (b5-gaps section 77).");
        return false;
    }
    const int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Equals", frag);
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        char message[192];
        std::snprintf(message, sizeof(message),
                      "IEqualityComparer.Equals dispatch: the comparer type has no "
                      "reflected virtual Equals(%s) slot (b5-gaps section 77).",
                      frag);
        il2cpp_throw_not_supported_exception(message);
        return false;
    }
    void* fn = cmp->klass->vtable[slot];
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_DECIMAL:
        {
            typedef bool (*Fn)(Il2CppObject*, runtime::Il2CppDecimal, runtime::Il2CppDecimal);
            return reinterpret_cast<Fn>(fn)(cmp, *reinterpret_cast<const runtime::Il2CppDecimal*>(a),
                                            *reinterpret_cast<const runtime::Il2CppDecimal*>(b));
        }
        case IL2CPP_LINQ_CELL_DATETIME:
        {
            typedef bool (*Fn)(Il2CppObject*, runtime::Il2CppDateTime, runtime::Il2CppDateTime);
            const runtime::Il2CppDateTime da = runtime::il2cpp_datetime_from_array_bits(
                *reinterpret_cast<const int64_t*>(a));
            const runtime::Il2CppDateTime db = runtime::il2cpp_datetime_from_array_bits(
                *reinterpret_cast<const int64_t*>(b));
            return reinterpret_cast<Fn>(fn)(cmp, da, db);
        }
        case IL2CPP_LINQ_CELL_TIMESPAN:
        {
            typedef bool (*Fn)(Il2CppObject*, runtime::Il2CppTimeSpan, runtime::Il2CppTimeSpan);
            return reinterpret_cast<Fn>(fn)(cmp,
                runtime::Il2CppTimeSpan(*reinterpret_cast<const int64_t*>(a)),
                runtime::Il2CppTimeSpan(*reinterpret_cast<const int64_t*>(b)));
        }
        case IL2CPP_LINQ_CELL_GUID:
        {
            typedef bool (*Fn)(Il2CppObject*, Il2CppGuid, Il2CppGuid);
            Il2CppGuid ga, gb;
            std::memcpy(&ga, a, sizeof(ga));
            std::memcpy(&gb, b, sizeof(gb));
            return reinterpret_cast<Fn>(fn)(cmp, ga, gb);
        }
        case IL2CPP_LINQ_CELL_DTOFFSET:
        {
            typedef bool (*Fn)(Il2CppObject*, runtime::Il2CppDateTimeOffset, runtime::Il2CppDateTimeOffset);
            runtime::Il2CppDateTimeOffset da, db;
            std::memcpy(&da, a, sizeof(da));
            std::memcpy(&db, b, sizeof(db));
            return reinterpret_cast<Fn>(fn)(cmp, da, db);
        }
        default:
            return false;
    }
}

// The element's VALUE as the 64-bit carrier the comparer dispatch reads back
// per family (int64 for the i8 cell, the double BIT image for float/double,
// the sign/zero-extended int32 for the narrow cells).
int64_t LinqReadCarrier(Il2CppArray* arr, int32_t index, int32_t cell)
{
    if (cell == IL2CPP_LINQ_CELL_I8)
    {
        return LinqCellReadI8(arr, index, cell);
    }
    if (cell == IL2CPP_LINQ_CELL_F || cell == IL2CPP_LINQ_CELL_D)
    {
        const double d = LinqCellReadD(arr, index, cell);
        int64_t carrier = 0;
        std::memcpy(&carrier, &d, sizeof(d));
        return carrier;
    }
    return static_cast<int64_t>(LinqCellReadI4(arr, index, cell));
}

// Element-pair equality under a nullable comparer for ANY cell (reference
// cells ride section 56, value cells the section-57 dispatch).
bool LinqCellEqualsCmp(Il2CppObject* cmp, Il2CppArray* a, int32_t ia,
                       Il2CppArray* b, int32_t ib, int32_t cell)
{
    if (cmp == nullptr) return LinqCellEquals(a, ia, b, ib, cell);
    if (cell == IL2CPP_LINQ_CELL_REF)
    {
        return LinqRefEqualsCmp(cmp, LinqCellReadRef(a, ia, cell),
                                LinqCellReadRef(b, ib, cell));
    }
    if (LinqEqualsFragment16(cell) != nullptr)
    {
        return LinqValueEqualsCmpCell16(cmp, cell,
                                        LinqCellPtr(a, ia), LinqCellPtr(b, ib));
    }
    return LinqValueEqualsCmpCell(cmp, cell, LinqReadCarrier(a, ia, cell),
                                  LinqReadCarrier(b, ib, cell));
}

} // namespace

// b5-gaps section 77: the unified USER-COMPARER cell-pair equality (see the
// forward declaration above). The _family_cmp kernels (set/sequence/contains
// twins) reach ANY cell through this single dispatch. Definition sits OUTSIDE
// the anonymous namespace so its linkage matches the forward declaration; the
// anonymous-namespace helpers above resolve through the unnamed-namespace
// using-directive.
bool LinqCmpEqCell(Il2CppObject* cmp, Il2CppArray* a, int32_t ia,
                   Il2CppArray* b, int32_t ib, int32_t cell)
{
    if (cmp == nullptr) return LinqCellEquals(a, ia, b, ib, cell);
    if (cell == IL2CPP_LINQ_CELL_REF)
    {
        return LinqRefCmpEqCell(a, ia, b, ib, cmp, cell);
    }
    if (LinqEqualsFragment16(cell) != nullptr)
    {
        return LinqValueEqualsCmpCell16(cmp, cell,
                                        LinqCellPtr(a, ia), LinqCellPtr(b, ib));
    }
    return LinqValueEqualsCmpCell(cmp, cell, LinqReadCarrier(a, ia, cell),
                                  LinqReadCarrier(b, ib, cell));
}

Il2CppArray* il2cpp_linq_distinct_value_cmp(Il2CppArray* src, Il2CppObject* cmp,
                                            int32_t cell)
{
    LinqRequireCell(src, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < src->length; ++i)
    {
        bool seen = false;
        for (size_t k = 0; k < order.size() && !seen; ++k)
        {
            seen = LinqCellEqualsCmp(cmp, src, static_cast<int32_t>(order[k]), src, i, cell);
        }
        if (!seen) order.push_back(i);
    }
    return MaterializeIndices(src, order, cell);
}

Il2CppArray* il2cpp_linq_except_value_cmp(Il2CppArray* first, Il2CppArray* second,
                                          Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool inSecond = false;
        for (int32_t j = 0; j < second->length && !inSecond; ++j)
        {
            inSecond = LinqCellEqualsCmp(cmp, first, i, second, j, cell);
        }
        if (inSecond) continue;
        bool seen = false;
        for (size_t k = 0; k < order.size() && !seen; ++k)
        {
            seen = LinqCellEqualsCmp(cmp, first, static_cast<int32_t>(order[k]), first, i, cell);
        }
        if (!seen) order.push_back(i);
    }
    return MaterializeIndices(first, order, cell);
}

Il2CppArray* il2cpp_linq_union_value_cmp(Il2CppArray* first, Il2CppArray* second,
                                         Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    std::vector<int32_t> fromFirst;
    std::vector<int32_t> fromSecond;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool dup = false;
        for (size_t k = 0; k < fromFirst.size() && !dup; ++k)
        {
            dup = LinqCellEqualsCmp(cmp, first, static_cast<int32_t>(fromFirst[k]), first, i, cell);
        }
        for (size_t k = 0; k < fromSecond.size() && !dup; ++k)
        {
            dup = LinqCellEqualsCmp(cmp, second, static_cast<int32_t>(fromSecond[k]), first, i, cell);
        }
        if (!dup) fromFirst.push_back(i);
    }
    for (int32_t i = 0; i < second->length; ++i)
    {
        bool dup = false;
        for (size_t k = 0; k < fromFirst.size() && !dup; ++k)
        {
            dup = LinqCellEqualsCmp(cmp, first, static_cast<int32_t>(fromFirst[k]), second, i, cell);
        }
        for (size_t k = 0; k < fromSecond.size() && !dup; ++k)
        {
            dup = LinqCellEqualsCmp(cmp, second, static_cast<int32_t>(fromSecond[k]), second, i, cell);
        }
        if (!dup) fromSecond.push_back(i);
    }
    Il2CppArray* out = LinqNewArray(
        cell, static_cast<int32_t>(fromFirst.size() + fromSecond.size()));
    roots.Add(reinterpret_cast<void**>(&out));
    int32_t w = 0;
    for (size_t k = 0; k < fromFirst.size(); ++k, ++w)
    {
        char* dst = LinqCellPtr(out, w);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), first,
                     static_cast<int32_t>(fromFirst[k]), cell);
    }
    for (size_t k = 0; k < fromSecond.size(); ++k, ++w)
    {
        char* dst = LinqCellPtr(out, w);
        LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), second,
                     static_cast<int32_t>(fromSecond[k]), cell);
    }
    return out;
}

Il2CppArray* il2cpp_linq_intersect_value_cmp(Il2CppArray* first, Il2CppArray* second,
                                             Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));
    std::vector<int32_t> order;
    for (int32_t i = 0; i < first->length; ++i)
    {
        bool inSecond = false;
        for (int32_t j = 0; j < second->length && !inSecond; ++j)
        {
            inSecond = LinqCellEqualsCmp(cmp, first, i, second, j, cell);
        }
        if (!inSecond) continue;
        bool seen = false;
        for (size_t k = 0; k < order.size() && !seen; ++k)
        {
            seen = LinqCellEqualsCmp(cmp, first, static_cast<int32_t>(order[k]), first, i, cell);
        }
        if (!seen) order.push_back(i);
    }
    return MaterializeIndices(first, order, cell);
}

bool il2cpp_linq_sequence_equal_value_cmp(Il2CppArray* first, Il2CppArray* second,
                                          Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(first, cell);
    LinqRequireCell(second, cell);
    if (first->length != second->length) return false;
    for (int32_t i = 0; i < first->length; ++i)
    {
        if (!LinqCellEqualsCmp(cmp, first, i, second, i, cell)) return false;
    }
    return true;
}

bool il2cpp_linq_contains_value_cmp_i4(Il2CppArray* src, int32_t value,
                                       Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(src, cell);
    if (cmp == nullptr)
    {
        for (int32_t i = 0; i < src->length; ++i)
        {
            if (LinqCellEqualsI4(src, i, value, cell)) return true;
        }
        return false;
    }
    const int64_t carrier = value;
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqValueEqualsCmpCell(cmp, cell, LinqReadCarrier(src, i, cell), carrier))
        {
            return true;
        }
    }
    return false;
}

bool il2cpp_linq_contains_value_cmp_i8(Il2CppArray* src, int64_t value,
                                       Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(src, cell);
    if (cmp == nullptr)
    {
        for (int32_t i = 0; i < src->length; ++i)
        {
            if (LinqCellEqualsI8(src, i, value, cell)) return true;
        }
        return false;
    }
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqValueEqualsCmpCell(cmp, cell, LinqReadCarrier(src, i, cell), value))
        {
            return true;
        }
    }
    return false;
}

bool il2cpp_linq_contains_value_cmp_d(Il2CppArray* src, double value,
                                      Il2CppObject* cmp, int32_t cell)
{
    LinqRequireCell(src, cell);
    int64_t carrier = 0;
    std::memcpy(&carrier, &value, sizeof(value));
    if (cmp == nullptr)
    {
        for (int32_t i = 0; i < src->length; ++i)
        {
            if (LinqCellEqualsD(src, i, value, cell)) return true;
        }
        return false;
    }
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (LinqValueEqualsCmpCell(cmp, cell, LinqReadCarrier(src, i, cell), carrier))
        {
            return true;
        }
    }
    return false;
}

//------------------------------------------------------------------------------
// b5-gaps section 59: Enumerable.SelectMany(source, collectionSelector).
// The selector returns the INNER collection as Il2CppObject* regardless of
// the source family; the source-element ABI is per-cell (int32 slot for the
// narrow/i4 cells, int64 for i8, double for float/double, object for ref).
//------------------------------------------------------------------------------
namespace {

typedef Il2CppObject* (*SelI4ReqFn)(int32_t);
typedef Il2CppObject* (*SelI4ReqThisFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*SelI8ReqFn)(int64_t);
typedef Il2CppObject* (*SelI8ReqThisFn)(Il2CppObject*, int64_t);
typedef Il2CppObject* (*SelDReqFn)(double);
typedef Il2CppObject* (*SelDReqThisFn)(Il2CppObject*, double);
typedef Il2CppObject* (*SelRefReqFn)(Il2CppObject*);
typedef Il2CppObject* (*SelRefReqThisFn)(Il2CppObject*, Il2CppObject*);

Il2CppObject* InvokeSelManyI4(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4ReqThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4ReqFn>(d->methodPtr)(v);
}

Il2CppObject* InvokeSelManyI8(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI8ReqThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI8ReqFn>(d->methodPtr)(v);
}

Il2CppObject* InvokeSelManyD(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDReqThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDReqFn>(d->methodPtr)(v);
}

Il2CppObject* InvokeSelManyRef(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefReqThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefReqFn>(d->methodPtr)(o);
}

} // namespace

//------------------------------------------------------------------------------
// b5-gaps section 67: SnapshotManyInner -- normalize a SelectMany INNER
// sequence into its element array. An array inner returns itself; any other
// IEnumerable (a List<T>, a `yield` iterator, a LINQ pipeline result, ...)
// is enumerated through the shared il2cpp_ienum_* surface and materialized
// into a fresh LINQ cell array. The element cell selects the get_Current
// carrier; the enumerator object is the GC root of every current value during
// the loop (the state machine holds its Current field), and the ref carriers
// are only written into the out array AFTER the loop ends, so no allocation
// can move them while held. Grid cells without a ienum carrier stay LOUD.
//------------------------------------------------------------------------------
Il2CppArray* SnapshotManyInner(Il2CppObject* inner, int32_t cell)
{
    if (inner == nullptr)
    {
        il2cpp_throw_argument_null_exception("inner");
        return nullptr;
    }
    if (inner->klass != nullptr && inner->klass->name != nullptr &&
        inner->klass->name[0] == '[')
    {
        return reinterpret_cast<Il2CppArray*>(inner);
    }
    // b5-gaps section 67: the int32 List domain has its own unboxed storage, so
    // snapshot it directly (a shared to-array accessor for that class).
    if (inner->klass == il2cpp_list_i4_class())
    {
        if (cell != IL2CPP_LINQ_CELL_I4 && cell != IL2CPP_LINQ_CELL_UINT)
        {
            il2cpp_throw_not_supported_exception(
                "SelectMany: an int List inner was bound to a non-int element "
                "cell (b5-gaps section 67).");
            return nullptr;
        }
        return il2cpp_linq_list_snapshot(inner, IL2CPP_LIST_KIND_I4);
    }
    // b5-gaps section 70: the 16-byte cells (decimal / DateTime / TimeSpan /
    // Guid / DateTimeOffset) and the DateTimeOffset cell have no ienum carrier;
    // every other element family (narrow 0..5, uint/i4/i8, float/double and
    // ref) enumerates below.
    if (cell == IL2CPP_LINQ_CELL_DECIMAL || cell == IL2CPP_LINQ_CELL_DATETIME ||
        cell == IL2CPP_LINQ_CELL_TIMESPAN || cell == IL2CPP_LINQ_CELL_GUID ||
        cell == IL2CPP_LINQ_CELL_DTOFFSET)
    {
        il2cpp_throw_not_supported_exception(
            "SelectMany: a non-array IEnumerable inner of the 16-byte element "
            "family is not wired (decimal/DateTime/TimeSpan/Guid/DateTimeOffset "
            "IEnumerable inners stay LOUD, b5-gaps section 70).");
        return nullptr;
    }

    Il2CppObject* en = il2cpp_ienum_get_enumerator(inner);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&inner));
    roots.Add(reinterpret_cast<void**>(&en));
    std::vector<int64_t> carriers;
    std::vector<Il2CppObject*> refs;
    while (il2cpp_ienum_move_next(en))
    {
        if (cell == IL2CPP_LINQ_CELL_REF)
        {
            refs.push_back(il2cpp_ienum_get_current_ref(en));
        }
        else if (cell == IL2CPP_LINQ_CELL_I8 || cell == IL2CPP_LINQ_CELL_D ||
                 cell == IL2CPP_LINQ_CELL_F)
        {
            const double dv = il2cpp_ienum_get_current_d(en);
            int64_t c = 0;
            std::memcpy(&c, &dv, sizeof(c));
            carriers.push_back(c);
        }
        else
        {
            carriers.push_back(static_cast<int64_t>(il2cpp_ienum_get_current_i4(en)));
        }
    }
    il2cpp_ienum_dispose(en);

    Il2CppArray* out = LinqNewArray(
        cell, static_cast<int32_t>(carriers.size() + refs.size()));
    if (cell == IL2CPP_LINQ_CELL_REF)
    {
        for (size_t i = 0; i < refs.size(); ++i)
        {
            char* dst = LinqCellPtr(out, static_cast<int32_t>(i));
            Il2CppObject* v = refs[i];
            *reinterpret_cast<Il2CppObject**>(dst) = v;
            il2cpp_write_barrier(reinterpret_cast<void**>(dst), v);
        }
    }
    else if (cell == IL2CPP_LINQ_CELL_I8 || cell == IL2CPP_LINQ_CELL_D)
    {
        for (size_t i = 0; i < carriers.size(); ++i)
        {
            std::memcpy(LinqCellPtr(out, static_cast<int32_t>(i)),
                        &carriers[i], 8);
        }
    }
    else if (cell == IL2CPP_LINQ_CELL_F)
    {
        for (size_t i = 0; i < carriers.size(); ++i)
        {
            double dv = 0.0;
            std::memcpy(&dv, &carriers[i], sizeof(dv));
            const float fv = static_cast<float>(dv);
            std::memcpy(LinqCellPtr(out, static_cast<int32_t>(i)), &fv, sizeof(float));
        }
    }
    else if (cell == IL2CPP_LINQ_CELL_SBYTE || cell == IL2CPP_LINQ_CELL_BYTE ||
             cell == IL2CPP_LINQ_CELL_BOOL || cell == IL2CPP_LINQ_CELL_SHORT ||
             cell == IL2CPP_LINQ_CELL_USHORT || cell == IL2CPP_LINQ_CELL_CHAR)
    {
        // b5-gaps section 70: a narrow/char/bool enumerable inner snapshots
        // into its own cell width (a 4-byte write would corrupt the next
        // cell); net48's IEnumerator<T>.Current returns the narrowed value in
        // an int32 register slot, so the narrowed bits round-trip exactly.
        for (size_t i = 0; i < carriers.size(); ++i)
        {
            const int32_t iv = static_cast<int32_t>(carriers[i]);
            char* dst = LinqCellPtr(out, static_cast<int32_t>(i));
            switch (cell)
            {
            case IL2CPP_LINQ_CELL_SBYTE:
                *reinterpret_cast<int8_t*>(dst) = static_cast<int8_t>(iv);
                break;
            case IL2CPP_LINQ_CELL_BYTE:
                *reinterpret_cast<uint8_t*>(dst) = static_cast<uint8_t>(iv);
                break;
            case IL2CPP_LINQ_CELL_BOOL:
                *reinterpret_cast<uint8_t*>(dst) = iv != 0 ? static_cast<uint8_t>(1) : static_cast<uint8_t>(0);
                break;
            case IL2CPP_LINQ_CELL_SHORT:
                *reinterpret_cast<int16_t*>(dst) = static_cast<int16_t>(iv);
                break;
            default: // USHORT / CHAR
                *reinterpret_cast<uint16_t*>(dst) = static_cast<uint16_t>(iv);
                break;
            }
        }
    }
    else
    {
        for (size_t i = 0; i < carriers.size(); ++i)
        {
            const int32_t iv = static_cast<int32_t>(carriers[i]);
            std::memcpy(LinqCellPtr(out, static_cast<int32_t>(i)), &iv, sizeof(int32_t));
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// b5-gaps section 68: Enumerable.Range(start, count). net48 validates up
// front (count < 0, or (long)start + count - 1 > int.MaxValue) and answers a
// lazy iterator; materializing into an int32[] yields the exact same
// sequence with zero side effects and feeds every array source below.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_range(int32_t start, int32_t count)
{
    if (count < 0 || (static_cast<int64_t>(start) + count - 1) > static_cast<int64_t>(INT32_MAX))
    {
        il2cpp_throw_argument_out_of_range_exception("count", nullptr);
        return nullptr;
    }
    Il2CppArray* arr = LinqNewArray(IL2CPP_LINQ_CELL_I4, count);
    for (int32_t i = 0; i < count; ++i)
    {
        const int32_t v = start + i;
        std::memcpy(LinqCellPtr(arr, i), &v, sizeof(int32_t));
    }
    return arr;
}

//------------------------------------------------------------------------------
// b5-gaps section 69: Enumerable.Empty<T>() and Enumerable.Repeat(element,
// count). net48 validates count up front (count < 0 throws
// ArgumentOutOfRangeException("count")) and yields lazily; eager arrays give
// the exact same sequence (no side effects), and a REFERENCE element is
// stored as the SAME pointer in every slot via write barriers.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_empty(int32_t cell)
{
    return LinqNewArray(cell, 0);
}

Il2CppArray* il2cpp_linq_repeat_i4(int32_t element, int32_t count, int32_t cell)
{
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", nullptr);
        return nullptr;
    }
    Il2CppArray* arr = LinqNewArray(cell, count);
    for (int32_t i = 0; i < count; ++i)
    {
        char* dst = LinqCellPtr(arr, i);
        switch (cell)
        {
            case IL2CPP_LINQ_CELL_SBYTE:
                *reinterpret_cast<int8_t*>(dst) = static_cast<int8_t>(element);
                break;
            case IL2CPP_LINQ_CELL_BYTE:
            case IL2CPP_LINQ_CELL_BOOL:
                *reinterpret_cast<uint8_t*>(dst) = element != 0 ? 1u : 0u;
                break;
            case IL2CPP_LINQ_CELL_SHORT:
                *reinterpret_cast<int16_t*>(dst) = static_cast<int16_t>(element);
                break;
            case IL2CPP_LINQ_CELL_USHORT:
            case IL2CPP_LINQ_CELL_CHAR:
                *reinterpret_cast<uint16_t*>(dst) = static_cast<uint16_t>(element);
                break;
            case IL2CPP_LINQ_CELL_UINT:
                *reinterpret_cast<uint32_t*>(dst) = static_cast<uint32_t>(element);
                break;
            default:
                std::memcpy(dst, &element, sizeof(int32_t));
                break;
        }
    }
    return arr;
}

Il2CppArray* il2cpp_linq_repeat_i8(int64_t element, int32_t count, int32_t cell)
{
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", nullptr);
        return nullptr;
    }
    Il2CppArray* arr = LinqNewArray(cell, count);
    for (int32_t i = 0; i < count; ++i)
    {
        std::memcpy(LinqCellPtr(arr, i), &element, sizeof(int64_t));
    }
    return arr;
}

Il2CppArray* il2cpp_linq_repeat_d(double element, int32_t count, int32_t cell)
{
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", nullptr);
        return nullptr;
    }
    Il2CppArray* arr = LinqNewArray(cell, count);
    for (int32_t i = 0; i < count; ++i)
    {
        char* dst = LinqCellPtr(arr, i);
        if (cell == IL2CPP_LINQ_CELL_F)
        {
            const float fv = static_cast<float>(element);
            std::memcpy(dst, &fv, sizeof(float));
        }
        else
        {
            std::memcpy(dst, &element, sizeof(double));
        }
    }
    return arr;
}

Il2CppArray* il2cpp_linq_repeat_ref(Il2CppObject* element, int32_t count)
{
    if (count < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("count", nullptr);
        return nullptr;
    }
    Il2CppArray* arr = LinqNewArray(IL2CPP_LINQ_CELL_REF, count);
    for (int32_t i = 0; i < count; ++i)
    {
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(LinqCellPtr(arr, i));
        *slot = element;
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), element);
    }
    return arr;
}

Il2CppArray* il2cpp_linq_select_many(Il2CppArray* src, Il2CppObject* sel,
                                     int32_t srcCell, int32_t resCell)
{
    LinqRequireCell(src, srcCell);
    Il2CppDelegate* d = reinterpret_cast<Il2CppDelegate*>(sel);
    if (d == nullptr || d->methodPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("selector");
        return nullptr;
    }
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&d));

    if (n == 0)
    {
        return LinqNewArray(resCell, 0);
    }

    // Pass 1: cache each inner collection in a RESERVED vector and root every
    // slot (a later selector call may allocate and compact/relocate the
    // earlier inners). The reserve pins the vector buffer, so the per-slot
    // roots point at stable addresses.
    std::vector<Il2CppObject*> inners;
    try
    {
        inners.reserve(static_cast<size_t>(n));
    }
    catch (const std::bad_alloc&)
    {
        il2cpp_throw_new(
            "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
            "Insufficient memory to continue the execution of the program.");
        return nullptr;
    }
    std::vector<int32_t> lens(static_cast<size_t>(n));
    int64_t total = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* inner = nullptr;
        switch (srcCell)
        {
            case IL2CPP_LINQ_CELL_SBYTE:
            case IL2CPP_LINQ_CELL_BYTE:
            case IL2CPP_LINQ_CELL_BOOL:
            case IL2CPP_LINQ_CELL_SHORT:
            case IL2CPP_LINQ_CELL_USHORT:
            case IL2CPP_LINQ_CELL_CHAR:
            case IL2CPP_LINQ_CELL_UINT:
            case IL2CPP_LINQ_CELL_I4:
                inner = InvokeSelManyI4(d, LinqCellReadI4(src, i, srcCell));
                break;
            case IL2CPP_LINQ_CELL_I8:
                inner = InvokeSelManyI8(d, LinqCellReadI8(src, i, srcCell));
                break;
            case IL2CPP_LINQ_CELL_F:
            case IL2CPP_LINQ_CELL_D:
                inner = InvokeSelManyD(d, LinqCellReadD(src, i, srcCell));
                break;
            case IL2CPP_LINQ_CELL_REF:
                inner = InvokeSelManyRef(d, LinqCellReadRef(src, i, srcCell));
                break;
            default:
                il2cpp_throw_not_supported_exception(
                    "SelectMany: the source element family has no selector ABI "
                    "(b5-gaps section 59).");
                return nullptr;
        }
        if (inner == nullptr)
        {
            il2cpp_throw_argument_null_exception(
                "The collectionSelector returned a null sequence (b5-gaps section 59).");
            return nullptr;
        }
        // b5-gaps section 67: normalize the inner into its element array -- an
        // array inner passes through, any other IEnumerable is enumerated and
        // materialized by SnapshotManyInner (the element cell is resCell: the
        // 2-argument TResult is the inner element).
        const int32_t innerCell = resCell;
        Il2CppArray* ia = SnapshotManyInner(inner, innerCell);
        if (ia == nullptr)
        {
            return nullptr;
        }
        inners.push_back(reinterpret_cast<Il2CppObject*>(ia));
        roots.Add(reinterpret_cast<void**>(&inners[inners.size() - 1]));
        lens[i] = ia->length;
        total += ia->length;
    }

    Il2CppArray* out = LinqNewArray(resCell, static_cast<int32_t>(total));
    roots.Add(reinterpret_cast<void**>(&out));
    int32_t w = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppArray* ia = reinterpret_cast<Il2CppArray*>(inners[i]);
        for (int32_t j = 0; j < lens[i]; ++j)
        {
            char* dst = LinqCellPtr(out, w);
            LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), ia, j, resCell);
            ++w;
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// b5-gaps section 61: Enumerable.SelectMany(source, collectionSelector,
// resultSelector). The TWO-ARGUMENT result selector crosses the SOURCE cell
// family (arg1) with the INNER element family (arg2) and returns into the
// TResult slot width (int32 for the narrow/i4 cells, int64, double for
// float/double, object for a reference). Every combination shares ONE erased
// signature int64_t(Il2CppDelegate*, Sel2Arg, Sel2Arg); a double result rides
// its raw bit pattern so the width-exact value write is lossless.
//------------------------------------------------------------------------------
namespace {

union Sel2Arg
{
    int32_t       i4;
    int64_t       i8;
    double        d;
    Il2CppObject* o;
};

typedef int64_t (*Sel2Fn)(Il2CppDelegate*, Sel2Arg, Sel2Arg);

template<typename T>
inline T Sel2Get(const Sel2Arg& a);
template<> inline int32_t       Sel2Get<int32_t>(const Sel2Arg& a) { return a.i4; }
template<> inline int64_t       Sel2Get<int64_t>(const Sel2Arg& a) { return a.i8; }
template<> inline double        Sel2Get<double>(const Sel2Arg& a) { return a.d; }
template<> inline Il2CppObject* Sel2Get<Il2CppObject*>(const Sel2Arg& a) { return a.o; }

template<typename T>
inline int64_t Sel2Erase(T v);
template<> inline int64_t Sel2Erase<int32_t>(int32_t v) { return v; }
template<> inline int64_t Sel2Erase<int64_t>(int64_t v) { return v; }
template<> inline int64_t Sel2Erase<double>(double v)
{
    int64_t b = 0;
    std::memcpy(&b, &v, sizeof(v));
    return b;
}
template<> inline int64_t Sel2Erase<Il2CppObject*>(Il2CppObject* v)
{
    return reinterpret_cast<int64_t>(v);
}

#define S2_ARG_T_i4 int32_t
#define S2_ARG_T_i8 int64_t
#define S2_ARG_T_d double
#define S2_ARG_T_o Il2CppObject*
#define S2_RT_T_i4 int32_t
#define S2_RT_T_i8 int64_t
#define S2_RT_T_d double
#define S2_RT_T_o Il2CppObject*

#define IL2CPP_SEL2_IMPL(RT, A1, A2)                                                \
    int64_t Sel2_##RT##_##A1##_##A2(Il2CppDelegate* d, Sel2Arg a1, Sel2Arg a2)      \
    {                                                                                \
        S2_RT_T_##RT r = d->target != nullptr                                 \
            ? (reinterpret_cast<S2_RT_T_##RT (*)(Il2CppObject*, S2_ARG_T_##A1,       \
                                                 S2_ARG_T_##A2)>(d->methodPtr))     \
                  (d->target, Sel2Get<S2_ARG_T_##A1>(a1),                           \
                              Sel2Get<S2_ARG_T_##A2>(a2))                          \
            : (reinterpret_cast<S2_RT_T_##RT (*)(S2_ARG_T_##A1, S2_ARG_T_##A2)>(     \
                  d->methodPtr))                                                    \
                  (Sel2Get<S2_ARG_T_##A1>(a1), Sel2Get<S2_ARG_T_##A2>(a2));         \
        return Sel2Erase(r);                                                         \
    }

#define IL2CPP_SEL2_16(RT)                                                            \
    IL2CPP_SEL2_IMPL(RT, i4, i4) IL2CPP_SEL2_IMPL(RT, i4, i8)                         \
    IL2CPP_SEL2_IMPL(RT, i4, d)  IL2CPP_SEL2_IMPL(RT, i4, o)                         \
    IL2CPP_SEL2_IMPL(RT, i8, i4) IL2CPP_SEL2_IMPL(RT, i8, i8)                         \
    IL2CPP_SEL2_IMPL(RT, i8, d)  IL2CPP_SEL2_IMPL(RT, i8, o)                         \
    IL2CPP_SEL2_IMPL(RT, d, i4)  IL2CPP_SEL2_IMPL(RT, d, i8)                         \
    IL2CPP_SEL2_IMPL(RT, d, d)   IL2CPP_SEL2_IMPL(RT, d, o)                          \
    IL2CPP_SEL2_IMPL(RT, o, i4)  IL2CPP_SEL2_IMPL(RT, o, i8)                         \
    IL2CPP_SEL2_IMPL(RT, o, d)   IL2CPP_SEL2_IMPL(RT, o, o)

IL2CPP_SEL2_16(i4)
IL2CPP_SEL2_16(i8)
IL2CPP_SEL2_16(d)
IL2CPP_SEL2_16(o)

#undef IL2CPP_SEL2_IMPL
#undef IL2CPP_SEL2_16

#define S2K(RT, A1, A2) Sel2_##RT##_##A1##_##A2,
#define S2R(RT)                                    \
    S2K(RT, i4, i4) S2K(RT, i4, i8) S2K(RT, i4, d)  \
    S2K(RT, i4, o)  S2K(RT, i8, i4) S2K(RT, i8, i8) \
    S2K(RT, i8, d)  S2K(RT, i8, o)  S2K(RT, d, i4)  \
    S2K(RT, d, i8)  S2K(RT, d, d)   S2K(RT, d, o)   \
    S2K(RT, o, i4)  S2K(RT, o, i8)  S2K(RT, o, d)   \
    S2K(RT, o, o)

static const Sel2Fn kSel2[64] = {
    S2R(i4)
    S2R(i8)
    S2R(d)
    S2R(o)
};

#undef S2K
#undef S2R
#undef S2_ARG_T_i4
#undef S2_ARG_T_i8
#undef S2_ARG_T_d
#undef S2_ARG_T_o
#undef S2_RT_T_i4
#undef S2_RT_T_i8
#undef S2_RT_T_d
#undef S2_RT_T_o

// Slot codes: 0 int32, 1 int64, 2 double, 3 reference.
int32_t Sel2ArgSlotOf(int32_t cell)
{
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_SBYTE:
        case IL2CPP_LINQ_CELL_BYTE:
        case IL2CPP_LINQ_CELL_BOOL:
        case IL2CPP_LINQ_CELL_SHORT:
        case IL2CPP_LINQ_CELL_USHORT:
        case IL2CPP_LINQ_CELL_CHAR:
        case IL2CPP_LINQ_CELL_UINT:
        case IL2CPP_LINQ_CELL_I4:
            return 0;
        case IL2CPP_LINQ_CELL_I8:
            return 1;
        case IL2CPP_LINQ_CELL_F:
        case IL2CPP_LINQ_CELL_D:
            return 2;
        case IL2CPP_LINQ_CELL_REF:
            return 3;
        default:
            return -1;
    }
}

} // namespace

Il2CppArray* il2cpp_linq_select_many3(Il2CppArray* src, Il2CppObject* collSel,
                                      Il2CppObject* resSel, int32_t srcCell,
                                      int32_t collCell, int32_t resCell)
{
    // D-2 guard: no arg2Type hint reaches the transpiler for the 3-argument
    // form, so the emitter casts the source unconditionally -- a List here
    // would read its _items pointer as a length. Reject non-array descriptors
    // BEFORE any array-shaped read.
    if (src == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "SelectMany(source, collectionSelector, resultSelector): the source "
            "is not a one-dimensional array (List/IEnumerable sources stay LOUD, "
            "b5-gaps section 61).");
        return nullptr;
    }
    Il2CppClass* srcClass = reinterpret_cast<Il2CppObject*>(src)->klass;
    if (srcClass == nullptr || srcClass->name == nullptr || srcClass->name[0] != '[')
    {
        il2cpp_throw_not_supported_exception(
            "SelectMany(source, collectionSelector, resultSelector): the source "
            "is not a one-dimensional array (List/IEnumerable sources stay LOUD, "
            "b5-gaps section 61).");
        return nullptr;
    }
    LinqRequireCell(src, srcCell);
    const int32_t k1 = Sel2ArgSlotOf(srcCell);
    const int32_t k2 = Sel2ArgSlotOf(collCell);
    const int32_t ret = Sel2ArgSlotOf(resCell);
    if (k1 < 0 || k2 < 0 || ret < 0)
    {
        il2cpp_throw_not_supported_exception(
            "SelectMany(source, collectionSelector, resultSelector): a "
            "source / inner / result cell has no line-2 selector ABI "
            "(b5-gaps section 61).");
        return nullptr;
    }
    const Sel2Fn pick = kSel2[(ret * 4 + k1) * 4 + k2];
    Il2CppDelegate* cd = reinterpret_cast<Il2CppDelegate*>(collSel);
    Il2CppDelegate* rd = reinterpret_cast<Il2CppDelegate*>(resSel);
    if (cd == nullptr || cd->methodPtr == nullptr || rd == nullptr ||
        rd->methodPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("selector");
        return nullptr;
    }
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&cd));
    roots.Add(reinterpret_cast<void**>(&rd));

    if (n == 0)
    {
        return LinqNewArray(resCell, 0);
    }

    // Pass 1: cache the inner arrays (reserved vector + per-slot root).
    std::vector<Il2CppObject*> inners;
    try
    {
        inners.reserve(static_cast<size_t>(n));
    }
    catch (const std::bad_alloc&)
    {
        il2cpp_throw_new(
            "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
            "Insufficient memory to continue the execution of the program.");
        return nullptr;
    }
    std::vector<int32_t> lens(static_cast<size_t>(n));
    int64_t pairs = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* inner = nullptr;
        switch (srcCell)
        {
            case IL2CPP_LINQ_CELL_SBYTE:
            case IL2CPP_LINQ_CELL_BYTE:
            case IL2CPP_LINQ_CELL_BOOL:
            case IL2CPP_LINQ_CELL_SHORT:
            case IL2CPP_LINQ_CELL_USHORT:
            case IL2CPP_LINQ_CELL_CHAR:
            case IL2CPP_LINQ_CELL_UINT:
            case IL2CPP_LINQ_CELL_I4:
                inner = InvokeSelManyI4(cd, LinqCellReadI4(src, i, srcCell));
                break;
            case IL2CPP_LINQ_CELL_I8:
                inner = InvokeSelManyI8(cd, LinqCellReadI8(src, i, srcCell));
                break;
            case IL2CPP_LINQ_CELL_F:
            case IL2CPP_LINQ_CELL_D:
                inner = InvokeSelManyD(cd, LinqCellReadD(src, i, srcCell));
                break;
            case IL2CPP_LINQ_CELL_REF:
                inner = InvokeSelManyRef(cd, LinqCellReadRef(src, i, srcCell));
                break;
            default:
                il2cpp_throw_not_supported_exception(
                    "SelectMany(source, collectionSelector, resultSelector): "
                    "the source element family has no collection-selector ABI "
                    "(b5-gaps section 61).");
                return nullptr;
        }
        if (inner == nullptr)
        {
            il2cpp_throw_argument_null_exception(
                "The collectionSelector returned a null sequence (b5-gaps section 61).");
            return nullptr;
        }
        // b5-gaps section 67: normalize the inner into its element array via
        // the collection element cell (the 3-argument form separates the inner
        // element cell from the TResult cell).
        Il2CppArray* ia = SnapshotManyInner(inner, collCell);
        if (ia == nullptr)
        {
            return nullptr;
        }
        inners.push_back(reinterpret_cast<Il2CppObject*>(ia));
        roots.Add(reinterpret_cast<void**>(&inners[inners.size() - 1]));
        lens[i] = ia->length;
        pairs += ia->length;
    }
    (void)pairs;

    // Pass 2: collect the per-pair results (rooted vector for a reference
    // result; int64 bit-pattern carriers otherwise).
    std::vector<int64_t> carriers;
    std::vector<Il2CppObject*> refs;
    if (ret == 3)
    {
        try { refs.reserve(static_cast<size_t>(pairs)); }
        catch (const std::bad_alloc&)
        {
            il2cpp_throw_new(
                "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                "Insufficient memory to continue the execution of the program.");
            return nullptr;
        }
    }
    else
    {
        try { carriers.reserve(static_cast<size_t>(pairs)); }
        catch (const std::bad_alloc&)
        {
            il2cpp_throw_new(
                "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                "Insufficient memory to continue the execution of the program.");
            return nullptr;
        }
    }
    for (int32_t i = 0; i < n; ++i)
    {
        Sel2Arg o1;
        switch (k1)
        {
            case 0: o1.i4 = LinqCellReadI4(src, i, srcCell); break;
            case 1: o1.i8 = LinqCellReadI8(src, i, srcCell); break;
            case 2: o1.d = LinqCellReadD(src, i, srcCell); break;
            default: o1.o = LinqCellReadRef(src, i, srcCell); break;
        }
        Il2CppArray* ia = reinterpret_cast<Il2CppArray*>(inners[i]);
        for (int32_t j = 0; j < lens[i]; ++j)
        {
            Sel2Arg o2;
            switch (k2)
            {
                case 0: o2.i4 = LinqCellReadI4(ia, j, collCell); break;
                case 1: o2.i8 = LinqCellReadI8(ia, j, collCell); break;
                case 2: o2.d = LinqCellReadD(ia, j, collCell); break;
                default: o2.o = LinqCellReadRef(ia, j, collCell); break;
            }
            const int64_t r = pick(rd, o1, o2);
            if (ret == 3)
            {
                Il2CppObject* result = reinterpret_cast<Il2CppObject*>(r);
                refs.push_back(result);
                roots.Add(reinterpret_cast<void**>(&refs[refs.size() - 1]));
            }
            else
            {
                carriers.push_back(r);
            }
        }
    }

    // Pass 3: materialize the exact-size result array.
    const size_t count = ret == 3 ? refs.size() : carriers.size();
    Il2CppArray* out = LinqNewArray(resCell, static_cast<int32_t>(count));
    roots.Add(reinterpret_cast<void**>(&out));
    if (ret == 3)
    {
        for (size_t w = 0; w < refs.size(); ++w)
        {
            char* dst = LinqCellPtr(out, static_cast<int32_t>(w));
            Il2CppObject* v = refs[w];
            *reinterpret_cast<Il2CppObject**>(dst) = v;
            il2cpp_write_barrier(reinterpret_cast<void**>(dst), v);
        }
        return out;
    }
    for (size_t w = 0; w < carriers.size(); ++w)
    {
        char* dst = LinqCellPtr(out, static_cast<int32_t>(w));
        const int64_t v = carriers[w];
        switch (resCell)
        {
            case IL2CPP_LINQ_CELL_SBYTE:
            case IL2CPP_LINQ_CELL_BYTE:
            case IL2CPP_LINQ_CELL_BOOL:
            case IL2CPP_LINQ_CELL_SHORT:
            case IL2CPP_LINQ_CELL_USHORT:
            case IL2CPP_LINQ_CELL_CHAR:
            case IL2CPP_LINQ_CELL_UINT:
            case IL2CPP_LINQ_CELL_I4:
            {
                const int32_t i4 = static_cast<int32_t>(v);
                std::memcpy(dst, &i4, sizeof(i4));
                break;
            }
            case IL2CPP_LINQ_CELL_I8:
                std::memcpy(dst, &v, sizeof(int64_t));
                break;
            case IL2CPP_LINQ_CELL_F:
            {
                double dv = 0.0;
                std::memcpy(&dv, &v, sizeof(dv));
                const float fv = static_cast<float>(dv);
                std::memcpy(dst, &fv, sizeof(fv));
                break;
            }
            case IL2CPP_LINQ_CELL_D:
                std::memcpy(dst, &v, sizeof(double));
                break;
            default:
                break;
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// b5-gaps section 62: Enumerable.Join(outer, inner, outerKeySelector,
// innerKeySelector, resultSelector) over ARRAY sources (outer element cell,
// inner element cell, TKey cell, TResult cell as trailing literals). The
// TKey carries ride the single-argument erased table below (key selectors);
// the TWO-ARGUMENT result selector reuses the section-61 kSel2 table. The
// matched pairs are collected as PURE int indices (no managed pointers), so
// the pair vector never needs rooting; reference TKeys only live for the
// duration of the call and are held by the per-slot rooted key vector.
//------------------------------------------------------------------------------
namespace {

struct JoinSlot
{
    int32_t       next;     // bucket-chain successor (-1 at tail)
    int64_t       key;      // value-key carrier (ref keys: unused)
    Il2CppObject* keyRef;   // reference key (null for value keys)
    int32_t       innerIndex;
};

#define S3_ARG_T_i4 int32_t
#define S3_ARG_T_i8 int64_t
#define S3_ARG_T_d double
#define S3_ARG_T_o Il2CppObject*
#define S3_RT_T_i4 int32_t
#define S3_RT_T_i8 int64_t
#define S3_RT_T_d double
#define S3_RT_T_o Il2CppObject*

typedef int64_t (*Sel1Fn)(Il2CppDelegate*, Sel2Arg);

#define IL2CPP_SEL1_IMPL(RT, A1)                                                \
    int64_t Sel1_##RT##_##A1(Il2CppDelegate* d, Sel2Arg a1)                     \
    {                                                                            \
        S3_RT_T_##RT r = d->target != nullptr                                   \
            ? (reinterpret_cast<S3_RT_T_##RT (*)(Il2CppObject*,                 \
                                                 S3_ARG_T_##A1)>(d->methodPtr)) \
                  (d->target, Sel2Get<S3_ARG_T_##A1>(a1))                      \
            : (reinterpret_cast<S3_RT_T_##RT (*)(S3_ARG_T_##A1)>(d->methodPtr)) \
                  (Sel2Get<S3_ARG_T_##A1>(a1));                                 \
        return Sel2Erase(r);                                                     \
    }

#define IL2CPP_SEL1_4(RT)                   \
    IL2CPP_SEL1_IMPL(RT, i4)                \
    IL2CPP_SEL1_IMPL(RT, i8)                \
    IL2CPP_SEL1_IMPL(RT, d)                 \
    IL2CPP_SEL1_IMPL(RT, o)

IL2CPP_SEL1_4(i4)
IL2CPP_SEL1_4(i8)
IL2CPP_SEL1_4(d)
IL2CPP_SEL1_4(o)

#undef IL2CPP_SEL1_IMPL
#undef IL2CPP_SEL1_4

#define S1K(RT, A1) Sel1_##RT##_##A1,
#define S1R(RT)                \
    S1K(RT, i4) S1K(RT, i8)    \
    S1K(RT, d)  S1K(RT, o)

static const Sel1Fn kSel1[16] = {
    S1R(i4)
    S1R(i8)
    S1R(d)
    S1R(o)
};

#undef S1K
#undef S1R
#undef S3_ARG_T_i4
#undef S3_ARG_T_i8
#undef S3_ARG_T_d
#undef S3_ARG_T_o
#undef S3_RT_T_i4
#undef S3_RT_T_i8
#undef S3_RT_T_d
#undef S3_RT_T_o

int32_t JoinRefHash(Il2CppObject* o)
{
    if (o == nullptr)
    {
        return 0;
    }
    if (o->klass != nullptr && o->klass->name != nullptr &&
        std::strcmp(o->klass->name, "System.String") == 0)
    {
        const Il2CppString* s = reinterpret_cast<const Il2CppString*>(o);
        uint32_t h = 17u;
        for (int32_t i = 0; i < s->length; ++i)
        {
            h = h * 33u + static_cast<uint16_t>(s->chars[i]);
        }
        return static_cast<int32_t>(h & 0x7FFFFFFFu);
    }
    return static_cast<int32_t>(
        (reinterpret_cast<uintptr_t>(o) >> 4) & 0x7FFFFFFFu);
}

bool JoinRefEquals(Il2CppObject* a, Il2CppObject* b)
{
    if (a == b)
    {
        return true;
    }
    if (a == nullptr || b == nullptr)
    {
        return false;
    }
    const bool aStr = a->klass != nullptr && a->klass->name != nullptr &&
        std::strcmp(a->klass->name, "System.String") == 0;
    const bool bStr = b->klass != nullptr && b->klass->name != nullptr &&
        std::strcmp(b->klass->name, "System.String") == 0;
    if (aStr && bStr)
    {
        return il2cpp_string_equals_ordinal(
            reinterpret_cast<Il2CppString*>(a),
            reinterpret_cast<Il2CppString*>(b));
    }
    return false;
}

int32_t JoinKeyHash(int64_t carrier, Il2CppObject* keyRef, int32_t keyCell)
{
    switch (keyCell)
    {
        case IL2CPP_LINQ_CELL_SBYTE:
        case IL2CPP_LINQ_CELL_BYTE:
        case IL2CPP_LINQ_CELL_BOOL:
        case IL2CPP_LINQ_CELL_SHORT:
        case IL2CPP_LINQ_CELL_USHORT:
        case IL2CPP_LINQ_CELL_CHAR:
        case IL2CPP_LINQ_CELL_UINT:
        case IL2CPP_LINQ_CELL_I4:
            return static_cast<int32_t>(carrier) & 0x7FFFFFFF;
        case IL2CPP_LINQ_CELL_I8:
        {
            const uint64_t u = static_cast<uint64_t>(carrier);
            return static_cast<int32_t>((u ^ (u >> 33)) & 0x7FFFFFFFULL);
        }
        case IL2CPP_LINQ_CELL_F:
        case IL2CPP_LINQ_CELL_D:
        {
            double dv = 0.0;
            std::memcpy(&dv, &carrier, sizeof(dv));
            uint64_t u = 0;
            std::memcpy(&u, &dv, sizeof(u));
            return static_cast<int32_t>((u ^ (u >> 33)) & 0x7FFFFFFFULL);
        }
        case IL2CPP_LINQ_CELL_REF:
            return JoinRefHash(keyRef);
        default:
            return 0;
    }
}

bool JoinKeyEquals(int64_t ca, Il2CppObject* ra, int64_t cb, Il2CppObject* rb,
                   int32_t keyCell)
{
    switch (keyCell)
    {
        case IL2CPP_LINQ_CELL_SBYTE:
        case IL2CPP_LINQ_CELL_BYTE:
        case IL2CPP_LINQ_CELL_BOOL:
        case IL2CPP_LINQ_CELL_SHORT:
        case IL2CPP_LINQ_CELL_USHORT:
        case IL2CPP_LINQ_CELL_CHAR:
        case IL2CPP_LINQ_CELL_UINT:
        case IL2CPP_LINQ_CELL_I4:
            return ca == cb;
        case IL2CPP_LINQ_CELL_I8:
            return ca == cb;
        case IL2CPP_LINQ_CELL_F:
        case IL2CPP_LINQ_CELL_D:
        {
            double da = 0.0;
            double db = 0.0;
            std::memcpy(&da, &ca, sizeof(da));
            std::memcpy(&db, &cb, sizeof(cb));
            return da == db;
        }
        case IL2CPP_LINQ_CELL_REF:
            return JoinRefEquals(ra, rb);
        default:
            return false;
    }
}

int32_t RoundUpPow2(int32_t v)
{
    int32_t p = 1;
    while (p < v)
    {
        p <<= 1;
    }
    return p;
}

} // namespace

Il2CppArray* il2cpp_linq_join(Il2CppArray* outer, Il2CppArray* inner,
                              Il2CppObject* outerKeySel, Il2CppObject* innerKeySel,
                              Il2CppObject* resSel, int32_t outerCell,
                              int32_t innerCell, int32_t keyCell, int32_t resCell)
{
    if (outer == nullptr || inner == nullptr)
    {
        il2cpp_throw_argument_null_exception("outer");
        return nullptr;
    }
    Il2CppClass* oCls = reinterpret_cast<Il2CppObject*>(outer)->klass;
    Il2CppClass* iCls = reinterpret_cast<Il2CppObject*>(inner)->klass;
    if (oCls == nullptr || oCls->name == nullptr || oCls->name[0] != '[' ||
        iCls == nullptr || iCls->name == nullptr || iCls->name[0] != '[')
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.Join: a source is not a one-dimensional array "
            "(List/IEnumerable sources stay LOUD, b5-gaps section 62).");
        return nullptr;
    }
    LinqRequireCell(outer, outerCell);
    LinqRequireCell(inner, innerCell);
    const int32_t k1o = Sel2ArgSlotOf(outerCell);
    const int32_t k2i = Sel2ArgSlotOf(innerCell);
    const int32_t kkey = Sel2ArgSlotOf(keyCell);
    const int32_t rres = Sel2ArgSlotOf(resCell);
    if (k1o < 0 || k2i < 0 || kkey < 0 || rres < 0)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.Join: a source / key / result cell has no line-2 "
            "selector ABI (b5-gaps section 62).");
        return nullptr;
    }
    Il2CppDelegate* ok = reinterpret_cast<Il2CppDelegate*>(outerKeySel);
    Il2CppDelegate* ik = reinterpret_cast<Il2CppDelegate*>(innerKeySel);
    Il2CppDelegate* rs = reinterpret_cast<Il2CppDelegate*>(resSel);
    if (ok == nullptr || ok->methodPtr == nullptr || ik == nullptr ||
        ik->methodPtr == nullptr || rs == nullptr || rs->methodPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    const int32_t on = outer->length;
    const int32_t inn = inner->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&outer));
    roots.Add(reinterpret_cast<void**>(&inner));
    roots.Add(reinterpret_cast<void**>(&ok));
    roots.Add(reinterpret_cast<void**>(&ik));
    roots.Add(reinterpret_cast<void**>(&rs));

    // Pass 1: index the inner sequence by TKey.
    const bool refKey = kkey == 3;
    size_t bucketCount = 0;
    std::vector<int32_t> buckets;
    std::vector<JoinSlot> slots;
    std::vector<Il2CppObject*> keyRoots;
    if (inn > 0)
    {
        bucketCount = static_cast<size_t>(RoundUpPow2(inn * 2));
        if (bucketCount < 8)
        {
            bucketCount = 8;
        }
        try
        {
            buckets.assign(bucketCount, -1);
            slots.reserve(static_cast<size_t>(inn));
            if (refKey)
            {
                keyRoots.reserve(static_cast<size_t>(inn));
            }
        }
        catch (const std::bad_alloc&)
        {
            il2cpp_throw_new(
                "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                "Insufficient memory to continue the execution of the program.");
            return nullptr;
        }
        const Sel1Fn ike1 = kSel1[(kkey * 4 + k2i)];
        for (int32_t j = 0; j < inn; ++j)
        {
            Sel2Arg a2;
            switch (k2i)
            {
                case 0: a2.i4 = LinqCellReadI4(inner, j, innerCell); break;
                case 1: a2.i8 = LinqCellReadI8(inner, j, innerCell); break;
                case 2: a2.d = LinqCellReadD(inner, j, innerCell); break;
                default: a2.o = LinqCellReadRef(inner, j, innerCell); break;
            }
            const int64_t key = ike1(ik, a2);
            Il2CppObject* keyRef = refKey ? reinterpret_cast<Il2CppObject*>(key) : nullptr;
            const int32_t h = JoinKeyHash(key, keyRef, keyCell) & (static_cast<int32_t>(bucketCount) - 1);
            const size_t slotIdx = slots.size();
            JoinSlot slot;
            slot.next = buckets[h];
            slot.key = key;
            slot.keyRef = keyRef;
            slot.innerIndex = j;
            try
            {
                slots.push_back(slot);
                if (refKey)
                {
                    keyRoots.push_back(keyRef);
                }
            }
            catch (const std::bad_alloc&)
            {
                il2cpp_throw_new(
                    "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                    "Insufficient memory to continue the execution of the program.");
                return nullptr;
            }
            if (refKey)
            {
                roots.Add(reinterpret_cast<void**>(&keyRoots[keyRoots.size() - 1]));
            }
            buckets[h] = static_cast<int32_t>(slotIdx);
        }
    }

    // Pass 2: walk the outer sequence, collect (outer, inner) match pairs as
    // pure int indices (the inner ordering of the chain is preserved by
    // walking the head chain and emitting in reverse).
    std::vector<int32_t> pairs;
    if (inn > 0)
    {
        try { pairs.reserve(static_cast<size_t>(on)); }
        catch (const std::bad_alloc&) { /* fall through to grow on push */ }
        const Sel1Fn oke1 = kSel1[(kkey * 4 + k1o)];
        std::vector<int32_t> chain;
        for (int32_t i = 0; i < on; ++i)
        {
            Sel2Arg a1;
            switch (k1o)
            {
                case 0: a1.i4 = LinqCellReadI4(outer, i, outerCell); break;
                case 1: a1.i8 = LinqCellReadI8(outer, i, outerCell); break;
                case 2: a1.d = LinqCellReadD(outer, i, outerCell); break;
                default: a1.o = LinqCellReadRef(outer, i, outerCell); break;
            }
            const int64_t key = oke1(ok, a1);
            Il2CppObject* keyRef = refKey ? reinterpret_cast<Il2CppObject*>(key) : nullptr;
            const int32_t h = JoinKeyHash(key, keyRef, keyCell) & (static_cast<int32_t>(bucketCount) - 1);
            chain.clear();
            for (int32_t s = buckets[h]; s >= 0; s = slots[s].next)
            {
                if (JoinKeyEquals(key, keyRef, slots[s].key, slots[s].keyRef, keyCell))
                {
                    chain.push_back(slots[s].innerIndex);
                }
            }
            for (size_t c = chain.size(); c > 0; --c)
            {
                pairs.push_back(i);
                pairs.push_back(chain[c - 1]);
            }
        }
    }

    // Pass 3: materialize the result array (result selector invoked once per
    // matched pair, straight into the preallocated exact-size array).
    const size_t pairCount = pairs.size() / 2;
    Il2CppArray* out = LinqNewArray(resCell, static_cast<int32_t>(pairCount));
    roots.Add(reinterpret_cast<void**>(&out));
    const Sel2Fn pick = kSel2[(rres * 4 + k1o) * 4 + k2i];
    for (size_t p = 0; p < pairCount; ++p)
    {
        const int32_t o = pairs[p * 2];
        const int32_t i = pairs[p * 2 + 1];
        Sel2Arg a1;
        switch (k1o)
        {
            case 0: a1.i4 = LinqCellReadI4(outer, o, outerCell); break;
            case 1: a1.i8 = LinqCellReadI8(outer, o, outerCell); break;
            case 2: a1.d = LinqCellReadD(outer, o, outerCell); break;
            default: a1.o = LinqCellReadRef(outer, o, outerCell); break;
        }
        Sel2Arg a2;
        switch (k2i)
        {
            case 0: a2.i4 = LinqCellReadI4(inner, i, innerCell); break;
            case 1: a2.i8 = LinqCellReadI8(inner, i, innerCell); break;
            case 2: a2.d = LinqCellReadD(inner, i, innerCell); break;
            default: a2.o = LinqCellReadRef(inner, i, innerCell); break;
        }
        const int64_t r = pick(rs, a1, a2);
        char* dst = LinqCellPtr(out, static_cast<int32_t>(p));
        if (rres == 3)
        {
            Il2CppObject* v = reinterpret_cast<Il2CppObject*>(r);
            *reinterpret_cast<Il2CppObject**>(dst) = v;
            il2cpp_write_barrier(reinterpret_cast<void**>(dst), v);
        }
        else
        {
            switch (resCell)
            {
                case IL2CPP_LINQ_CELL_SBYTE:
                case IL2CPP_LINQ_CELL_BYTE:
                case IL2CPP_LINQ_CELL_BOOL:
                case IL2CPP_LINQ_CELL_SHORT:
                case IL2CPP_LINQ_CELL_USHORT:
                case IL2CPP_LINQ_CELL_CHAR:
                case IL2CPP_LINQ_CELL_UINT:
                case IL2CPP_LINQ_CELL_I4:
                {
                    const int32_t i4 = static_cast<int32_t>(r);
                    std::memcpy(dst, &i4, sizeof(i4));
                    break;
                }
                case IL2CPP_LINQ_CELL_I8:
                    std::memcpy(dst, &r, sizeof(int64_t));
                    break;
                case IL2CPP_LINQ_CELL_F:
                {
                    double dv = 0.0;
                    std::memcpy(&dv, &r, sizeof(dv));
                    const float fv = static_cast<float>(dv);
                    std::memcpy(dst, &fv, sizeof(fv));
                    break;
                }
                case IL2CPP_LINQ_CELL_D:
                    std::memcpy(dst, &r, sizeof(double));
                    break;
                default:
                    break;
            }
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// b5-gaps section 63: Enumerable.GroupJoin -- the LEFT-JOIN shape. The pass-1
// TKey index and the carrier/hash/equality helpers are the section-62 ones;
// the result array is EXACTLY outer-length (every outer element yields one
// group, even an empty one), so pass 2 writes each result straight into the
// preallocated array. Every outer element materializes a fresh innerCell
// typed GROUP array (matches in inner insertion order) which is rooted for
// the result-selector call; the TKey/reference tables never outlive the call.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_group_join(Il2CppArray* outer, Il2CppArray* inner,
                                    Il2CppObject* outerKeySel, Il2CppObject* innerKeySel,
                                    Il2CppObject* resSel, int32_t outerCell,
                                    int32_t innerCell, int32_t keyCell, int32_t resCell)
{
    if (outer == nullptr || inner == nullptr)
    {
        il2cpp_throw_argument_null_exception("outer");
        return nullptr;
    }
    Il2CppClass* oCls = reinterpret_cast<Il2CppObject*>(outer)->klass;
    Il2CppClass* iCls = reinterpret_cast<Il2CppObject*>(inner)->klass;
    if (oCls == nullptr || oCls->name == nullptr || oCls->name[0] != '[' ||
        iCls == nullptr || iCls->name == nullptr || iCls->name[0] != '[')
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.GroupJoin: a source is not a one-dimensional array "
            "(List/IEnumerable sources stay LOUD, b5-gaps section 63).");
        return nullptr;
    }
    LinqRequireCell(outer, outerCell);
    LinqRequireCell(inner, innerCell);
    const int32_t k1o = Sel2ArgSlotOf(outerCell);
    const int32_t k2i = Sel2ArgSlotOf(innerCell);
    const int32_t kkey = Sel2ArgSlotOf(keyCell);
    const int32_t rres = Sel2ArgSlotOf(resCell);
    if (k1o < 0 || k2i < 0 || kkey < 0 || rres < 0)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.GroupJoin: a source / key / result cell has no line-2 "
            "selector ABI (b5-gaps section 63).");
        return nullptr;
    }
    Il2CppDelegate* ok = reinterpret_cast<Il2CppDelegate*>(outerKeySel);
    Il2CppDelegate* ik = reinterpret_cast<Il2CppDelegate*>(innerKeySel);
    Il2CppDelegate* rs = reinterpret_cast<Il2CppDelegate*>(resSel);
    if (ok == nullptr || ok->methodPtr == nullptr || ik == nullptr ||
        ik->methodPtr == nullptr || rs == nullptr || rs->methodPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    const int32_t on = outer->length;
    const int32_t inn = inner->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&outer));
    roots.Add(reinterpret_cast<void**>(&inner));
    roots.Add(reinterpret_cast<void**>(&ok));
    roots.Add(reinterpret_cast<void**>(&ik));
    roots.Add(reinterpret_cast<void**>(&rs));

    const bool refKey = kkey == 3;
    size_t bucketCount = 0;
    std::vector<int32_t> buckets;
    std::vector<JoinSlot> slots;
    std::vector<Il2CppObject*> keyRoots;
    if (inn > 0)
    {
        bucketCount = static_cast<size_t>(RoundUpPow2(inn * 2));
        if (bucketCount < 8)
        {
            bucketCount = 8;
        }
        try
        {
            buckets.assign(bucketCount, -1);
            slots.reserve(static_cast<size_t>(inn));
            if (refKey)
            {
                keyRoots.reserve(static_cast<size_t>(inn));
            }
        }
        catch (const std::bad_alloc&)
        {
            il2cpp_throw_new(
                "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                "Insufficient memory to continue the execution of the program.");
            return nullptr;
        }
        const Sel1Fn ike1 = kSel1[(kkey * 4 + k2i)];
        for (int32_t j = 0; j < inn; ++j)
        {
            Sel2Arg a2;
            switch (k2i)
            {
                case 0: a2.i4 = LinqCellReadI4(inner, j, innerCell); break;
                case 1: a2.i8 = LinqCellReadI8(inner, j, innerCell); break;
                case 2: a2.d = LinqCellReadD(inner, j, innerCell); break;
                default: a2.o = LinqCellReadRef(inner, j, innerCell); break;
            }
            const int64_t key = ike1(ik, a2);
            Il2CppObject* keyRef = refKey ? reinterpret_cast<Il2CppObject*>(key) : nullptr;
            const int32_t h = JoinKeyHash(key, keyRef, keyCell) & (static_cast<int32_t>(bucketCount) - 1);
            const size_t slotIdx = slots.size();
            JoinSlot slot;
            slot.next = buckets[h];
            slot.key = key;
            slot.keyRef = keyRef;
            slot.innerIndex = j;
            try
            {
                slots.push_back(slot);
                if (refKey)
                {
                    keyRoots.push_back(keyRef);
                }
            }
            catch (const std::bad_alloc&)
            {
                il2cpp_throw_new(
                    "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                    "Insufficient memory to continue the execution of the program.");
                return nullptr;
            }
            if (refKey)
            {
                roots.Add(reinterpret_cast<void**>(&keyRoots[keyRoots.size() - 1]));
            }
            buckets[h] = static_cast<int32_t>(slotIdx);
        }
    }

    Il2CppArray* out = LinqNewArray(resCell, on);
    roots.Add(reinterpret_cast<void**>(&out));
    const Sel2Fn pick = kSel2[(rres * 4 + k1o) * 4 + 3];
    const Sel1Fn oke1 = inn > 0 ? kSel1[(kkey * 4 + k1o)] : nullptr;
    std::vector<int32_t> chain;
    for (int32_t i = 0; i < on; ++i)
    {
        std::vector<int32_t> matched;
        if (oke1 != nullptr)
        {
            Sel2Arg a1;
            switch (k1o)
            {
                case 0: a1.i4 = LinqCellReadI4(outer, i, outerCell); break;
                case 1: a1.i8 = LinqCellReadI8(outer, i, outerCell); break;
                case 2: a1.d = LinqCellReadD(outer, i, outerCell); break;
                default: a1.o = LinqCellReadRef(outer, i, outerCell); break;
            }
            const int64_t key = oke1(ok, a1);
            Il2CppObject* keyRef = refKey ? reinterpret_cast<Il2CppObject*>(key) : nullptr;
            const int32_t h = JoinKeyHash(key, keyRef, keyCell) & (static_cast<int32_t>(bucketCount) - 1);
            chain.clear();
            for (int32_t s = buckets[h]; s >= 0; s = slots[s].next)
            {
                if (JoinKeyEquals(key, keyRef, slots[s].key, slots[s].keyRef, keyCell))
                {
                    chain.push_back(slots[s].innerIndex);
                }
            }
            matched.reserve(chain.size());
            for (size_t c = chain.size(); c > 0; --c)
            {
                matched.push_back(chain[c - 1]);
            }
        }

        Il2CppArray* group = LinqNewArray(innerCell, static_cast<int32_t>(matched.size()));
        roots.Add(reinterpret_cast<void**>(&group));
        for (size_t k = 0; k < matched.size(); ++k)
        {
            char* dst = LinqCellPtr(group, static_cast<int32_t>(k));
            LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), inner,
                         matched[k], innerCell);
        }

        Sel2Arg a1;
        switch (k1o)
        {
            case 0: a1.i4 = LinqCellReadI4(outer, i, outerCell); break;
            case 1: a1.i8 = LinqCellReadI8(outer, i, outerCell); break;
            case 2: a1.d = LinqCellReadD(outer, i, outerCell); break;
            default: a1.o = LinqCellReadRef(outer, i, outerCell); break;
        }
        Sel2Arg a2;
        a2.o = reinterpret_cast<Il2CppObject*>(group);
        const int64_t r = pick(rs, a1, a2);
        char* dst = LinqCellPtr(out, i);
        if (rres == 3)
        {
            Il2CppObject* v = reinterpret_cast<Il2CppObject*>(r);
            *reinterpret_cast<Il2CppObject**>(dst) = v;
            il2cpp_write_barrier(reinterpret_cast<void**>(dst), v);
        }
        else
        {
            switch (resCell)
            {
                case IL2CPP_LINQ_CELL_SBYTE:
                case IL2CPP_LINQ_CELL_BYTE:
                case IL2CPP_LINQ_CELL_BOOL:
                case IL2CPP_LINQ_CELL_SHORT:
                case IL2CPP_LINQ_CELL_USHORT:
                case IL2CPP_LINQ_CELL_CHAR:
                case IL2CPP_LINQ_CELL_UINT:
                case IL2CPP_LINQ_CELL_I4:
                {
                    const int32_t i4 = static_cast<int32_t>(r);
                    std::memcpy(dst, &i4, sizeof(i4));
                    break;
                }
                case IL2CPP_LINQ_CELL_I8:
                    std::memcpy(dst, &r, sizeof(int64_t));
                    break;
                case IL2CPP_LINQ_CELL_F:
                {
                    double dv = 0.0;
                    std::memcpy(&dv, &r, sizeof(dv));
                    const float fv = static_cast<float>(dv);
                    std::memcpy(dst, &fv, sizeof(fv));
                    break;
                }
                case IL2CPP_LINQ_CELL_D:
                    std::memcpy(dst, &r, sizeof(double));
                    break;
                default:
                    break;
            }
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// b5-gaps section 64: Enumerable.Join(..., IEqualityComparer<TKey>) over
// ARRAY sources with REFERENCE TKeys. The comparer hashes and compares keys
// through a per-comparer ops pair: the StringComparer singletons take a
// direct weighted fold (ordinal / ordinal-ignore-case), every other comparer
// rides the reflected virtual GetHashCode/Equals slots (the section-56
// machinery), and a NULL comparer falls back to the section-62 default
// carrier/hash/equality. The indexing/collection skeleton mirrors section 62.
//------------------------------------------------------------------------------
namespace {

int32_t JoinCmpHashDefault(Il2CppObject*, Il2CppObject* key)
{
    return JoinRefHash(key);
}

int32_t JoinCmpHashIgnoreCase(Il2CppObject*, Il2CppObject* key)
{
    if (key == nullptr || key->klass == nullptr || key->klass->name == nullptr ||
        std::strcmp(key->klass->name, "System.String") != 0)
    {
        return JoinRefHash(key);
    }
    const Il2CppString* s = reinterpret_cast<const Il2CppString*>(key);
    uint32_t h = 17u;
    for (int32_t i = 0; i < s->length; ++i)
    {
        uint16_t c = static_cast<uint16_t>(s->chars[i]);
        if (c >= 'a' && c <= 'z')
        {
            c = static_cast<uint16_t>(c - ('a' - 'A'));
        }
        h = h * 33u + c;
    }
    return static_cast<int32_t>(h & 0x7FFFFFFFu);
}

int32_t JoinCmpHashSlot(Il2CppObject* cmp, Il2CppObject* key)
{
    const int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "GetHashCode", "(r:");
    if (slot >= 0 && cmp->klass != nullptr && cmp->klass->vtable != nullptr &&
        slot < cmp->klass->vtableSize && cmp->klass->vtable[slot] != nullptr)
    {
        typedef int32_t (*HFn)(Il2CppObject*, Il2CppObject*);
        return reinterpret_cast<HFn>(cmp->klass->vtable[slot])(cmp, key) & 0x7FFFFFFF;
    }
    return JoinRefHash(key);
}

// The single-argument GetHashCode(T) slot fragment for a VALUE key cell
// (mirrors LinqEqualsFragment in the section-57 value comparer dispatcher;
// the key equality and the key hash MUST be same-source paired, section 64).
const char* JoinValueHashFragment(int32_t keyCell)
{
    switch (keyCell)
    {
        case IL2CPP_LINQ_CELL_SBYTE:   return "(i1)";
        case IL2CPP_LINQ_CELL_BYTE:    return "(u1)";
        case IL2CPP_LINQ_CELL_BOOL:    return "(b)";
        case IL2CPP_LINQ_CELL_SHORT:   return "(i2)";
        case IL2CPP_LINQ_CELL_USHORT:  return "(u2)";
        case IL2CPP_LINQ_CELL_CHAR:    return "(c)";
        case IL2CPP_LINQ_CELL_UINT:
        case IL2CPP_LINQ_CELL_I4:      return "(i)";
        case IL2CPP_LINQ_CELL_I8:      return "(l)";
        case IL2CPP_LINQ_CELL_F:       return "(f)";
        case IL2CPP_LINQ_CELL_D:       return "(d)";
        default:                       return nullptr;
    }
}

// The comparer-mediated hash of a VALUE key carrier: calls the reflected
// GetHashCode(T) slot with the per-family ABI (int64 for i8, double for
// float/double, int32 everywhere else), so equal-under-the-comparer keys are
// ALWAYS bucketed together. A comparer with no such slot is a loud error
// (b5-gaps section 66: a silent bucket fallback could still be paired only
// if the equality also fell back -- the section-64 rule).
int32_t JoinCmpValueHashSlot(Il2CppObject* cmp, int32_t keyCell, int64_t carrier)
{
    const char* frag = JoinValueHashFragment(keyCell);
    if (frag == nullptr)
    {
        char message[160];
        std::snprintf(message, sizeof(message),
                      "a user IEqualityComparer GetHashCode over this value key "
                      "cell family is not wired (b5-gaps section 66).");
        il2cpp_throw_not_supported_exception(message);
        return 0;
    }
    const int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "GetHashCode", frag);
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        char message[160];
        std::snprintf(message, sizeof(message),
                      "IEqualityComparer.GetHashCode dispatch: the comparer type "
                      "has no reflected virtual GetHashCode(%s) slot (b5-gaps "
                      "section 66).", frag);
        il2cpp_throw_not_supported_exception(message);
        return 0;
    }
    if (keyCell == IL2CPP_LINQ_CELL_I8)
    {
        typedef int32_t (*HFn8)(Il2CppObject*, int64_t);
        return reinterpret_cast<HFn8>(cmp->klass->vtable[slot])(cmp, carrier);
    }
    if (keyCell == IL2CPP_LINQ_CELL_F || keyCell == IL2CPP_LINQ_CELL_D)
    {
        typedef int32_t (*HFnD)(Il2CppObject*, double);
        double dv = 0.0;
        std::memcpy(&dv, &carrier, sizeof(dv));
        return reinterpret_cast<HFnD>(cmp->klass->vtable[slot])(cmp, dv);
    }
    typedef int32_t (*HFn4)(Il2CppObject*, int32_t);
    return reinterpret_cast<HFn4>(cmp->klass->vtable[slot])(
        cmp, static_cast<int32_t>(carrier));
}

bool JoinCmpEqDefault(Il2CppObject*, Il2CppObject* a, Il2CppObject* b)
{
    return JoinRefEquals(a, b);
}

bool JoinCmpEqIgnoreCase(Il2CppObject*, Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr || b == nullptr)
    {
        return a == b;
    }
    if (a->klass == nullptr || a->klass->name == nullptr ||
        b->klass == nullptr || b->klass->name == nullptr)
    {
        return a == b;
    }
    if (std::strcmp(a->klass->name, "System.String") == 0 &&
        std::strcmp(b->klass->name, "System.String") == 0)
    {
        return il2cpp_string_equals_ordinal_ignore_case(
            reinterpret_cast<const Il2CppString*>(a),
            reinterpret_cast<const Il2CppString*>(b));
    }
    return a == b;
}

struct JoinCmpOps
{
    Il2CppObject* cmp;
    int32_t (*hash)(Il2CppObject*, Il2CppObject*);
    bool (*eq)(Il2CppObject*, Il2CppObject*, Il2CppObject*);
};

JoinCmpOps JoinCmpOpsOf(Il2CppObject* cmp)
{
    JoinCmpOps ops;
    ops.cmp = cmp;
    if (cmp == nullptr)
    {
        ops.hash = JoinCmpHashDefault;
        ops.eq = JoinCmpEqDefault;
        return ops;
    }
    if (cmp->klass != nullptr && cmp->klass->name != nullptr &&
        std::strncmp(cmp->klass->name, "System.StringComparer.", 22) == 0)
    {
        if (std::strcmp(cmp->klass->name,
                        "System.StringComparer.OrdinalIgnoreCase") == 0)
        {
            ops.hash = JoinCmpHashIgnoreCase;
            ops.eq = JoinCmpEqIgnoreCase;
        }
        else
        {
            ops.hash = JoinCmpHashDefault;
            ops.eq = JoinCmpEqDefault;
        }
        return ops;
    }
    ops.hash = JoinCmpHashSlot;
    ops.eq = LinqRefEqualsCmp;
    return ops;
}

} // namespace

Il2CppArray* il2cpp_linq_join_cmp(Il2CppArray* outer, Il2CppArray* inner,
                                  Il2CppObject* outerKeySel, Il2CppObject* innerKeySel,
                                  Il2CppObject* resSel, Il2CppObject* cmp,
                                  int32_t outerCell, int32_t innerCell,
                                  int32_t keyCell, int32_t resCell)
{
    if (outer == nullptr || inner == nullptr)
    {
        il2cpp_throw_argument_null_exception("outer");
        return nullptr;
    }
    Il2CppClass* oCls = reinterpret_cast<Il2CppObject*>(outer)->klass;
    Il2CppClass* iCls = reinterpret_cast<Il2CppObject*>(inner)->klass;
    if (oCls == nullptr || oCls->name == nullptr || oCls->name[0] != '[' ||
        iCls == nullptr || iCls->name == nullptr || iCls->name[0] != '[')
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.Join(comparer): a source is not a one-dimensional "
            "array (List/IEnumerable sources stay LOUD, b5-gaps section 64).");
        return nullptr;
    }
    LinqRequireCell(outer, outerCell);
    LinqRequireCell(inner, innerCell);
    const int32_t k1o = Sel2ArgSlotOf(outerCell);
    const int32_t k2i = Sel2ArgSlotOf(innerCell);
    const int32_t rres = Sel2ArgSlotOf(resCell);
    if (k1o < 0 || k2i < 0 || rres < 0)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.Join(comparer): a source / result cell has no line-2 "
            "selector ABI (b5-gaps section 66).");
        return nullptr;
    }
    // b5-gaps section 66: the comparer overload now also binds VALUE TKey
    // cells (int32 / narrow / int64 / float / double). The key selectors
    // return their value through the key-cell ABI slot, the comparer's
    // reflected GetHashCode(T) bucket every equal-under-the-comparer key
    // together, and the section-64 carrier/equality defaults serve a null
    // comparer exactly like the section-62 plain kernels.
    const int32_t kkey = Sel2ArgSlotOf(keyCell);
    if (kkey < 0 || (keyCell != IL2CPP_LINQ_CELL_REF && JoinValueHashFragment(keyCell) == nullptr))
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.Join(comparer): this TKey cell has no comparer "
            "GetHashCode wiring (decimal/DateTime/TimeSpan/Guid/DateTimeOffset "
            "comparer overloads stay LOUD, b5-gaps section 66).");
        return nullptr;
    }
    Il2CppDelegate* ok = reinterpret_cast<Il2CppDelegate*>(outerKeySel);
    Il2CppDelegate* ik = reinterpret_cast<Il2CppDelegate*>(innerKeySel);
    Il2CppDelegate* rs = reinterpret_cast<Il2CppDelegate*>(resSel);
    if (ok == nullptr || ok->methodPtr == nullptr || ik == nullptr ||
        ik->methodPtr == nullptr || rs == nullptr || rs->methodPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    const JoinCmpOps ops = JoinCmpOpsOf(cmp);
    const bool refKey = keyCell == IL2CPP_LINQ_CELL_REF;
    const int32_t on = outer->length;
    const int32_t inn = inner->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&outer));
    roots.Add(reinterpret_cast<void**>(&inner));
    roots.Add(reinterpret_cast<void**>(&ok));
    roots.Add(reinterpret_cast<void**>(&ik));
    roots.Add(reinterpret_cast<void**>(&rs));
    roots.Add(reinterpret_cast<void**>(&cmp));

    // b5-gaps section 66: a VALUE-key comparer buckets through the comparer's
    // own GetHashCode(T) slot; equal-under-the-comparer keys are then
    // guaranteed to share a bucket (the section-64 same-source-pair rule), and
    // the equality check calls the comparer's Equals(T,T) slot. A null
    // comparer keeps the section-62 carrier semantics untouched.
    const Sel1Fn ike1 = kSel1[(kkey * 4 + k2i)];
    const Sel1Fn oke1 = kSel1[(kkey * 4 + k1o)];

    size_t bucketCount = 0;
    std::vector<int32_t> buckets;
    std::vector<JoinSlot> slots;
    std::vector<Il2CppObject*> keyRoots;
    if (inn > 0)
    {
        bucketCount = static_cast<size_t>(RoundUpPow2(inn * 2));
        if (bucketCount < 8)
        {
            bucketCount = 8;
        }
        try
        {
            buckets.assign(bucketCount, -1);
            slots.reserve(static_cast<size_t>(inn));
            keyRoots.reserve(static_cast<size_t>(inn));
        }
        catch (const std::bad_alloc&)
        {
            il2cpp_throw_new(
                "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                "Insufficient memory to continue the execution of the program.");
            return nullptr;
        }
        for (int32_t j = 0; j < inn; ++j)
        {
            Sel2Arg a2;
            switch (k2i)
            {
                case 0: a2.i4 = LinqCellReadI4(inner, j, innerCell); break;
                case 1: a2.i8 = LinqCellReadI8(inner, j, innerCell); break;
                case 2: a2.d = LinqCellReadD(inner, j, innerCell); break;
                default: a2.o = LinqCellReadRef(inner, j, innerCell); break;
            }
            const int64_t keyCarrier = ike1(ik, a2);
            Il2CppObject* keyRef = refKey ? reinterpret_cast<Il2CppObject*>(keyCarrier) : nullptr;
            const int32_t h = (refKey
                    ? ops.hash(ops.cmp, keyRef)
                    : (ops.cmp != nullptr
                           ? JoinCmpValueHashSlot(ops.cmp, keyCell, keyCarrier)
                           : JoinKeyHash(keyCarrier, nullptr, keyCell)))
                & (static_cast<int32_t>(bucketCount) - 1);
            const size_t slotIdx = slots.size();
            JoinSlot slot;
            slot.next = buckets[h];
            slot.key = keyCarrier;
            slot.keyRef = keyRef;
            slot.innerIndex = j;
            try
            {
                slots.push_back(slot);
                if (refKey)
                {
                    keyRoots.push_back(keyRef);
                }
            }
            catch (const std::bad_alloc&)
            {
                il2cpp_throw_new(
                    "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                    "Insufficient memory to continue the execution of the program.");
                return nullptr;
            }
            if (refKey)
            {
                roots.Add(reinterpret_cast<void**>(&keyRoots[keyRoots.size() - 1]));
            }
            buckets[h] = static_cast<int32_t>(slotIdx);
        }
    }

    std::vector<int32_t> pairs;
    if (inn > 0)
    {
        std::vector<int32_t> chain;
        for (int32_t i = 0; i < on; ++i)
        {
            Sel2Arg a1;
            switch (k1o)
            {
                case 0: a1.i4 = LinqCellReadI4(outer, i, outerCell); break;
                case 1: a1.i8 = LinqCellReadI8(outer, i, outerCell); break;
                case 2: a1.d = LinqCellReadD(outer, i, outerCell); break;
                default: a1.o = LinqCellReadRef(outer, i, outerCell); break;
            }
            const int64_t keyCarrier = oke1(ok, a1);
            Il2CppObject* keyRef = refKey ? reinterpret_cast<Il2CppObject*>(keyCarrier) : nullptr;
            // The pairing guarantee mirrors pass 1 exactly: same hash source,
            // same equality source, so the outer key finds exactly the inner
            // keys the comparer says are equal.
            const int32_t h = (refKey
                    ? ops.hash(ops.cmp, keyRef)
                    : (ops.cmp != nullptr
                           ? JoinCmpValueHashSlot(ops.cmp, keyCell, keyCarrier)
                           : JoinKeyHash(keyCarrier, nullptr, keyCell)))
                & (static_cast<int32_t>(bucketCount) - 1);
            chain.clear();
            for (int32_t s = buckets[h]; s >= 0; s = slots[s].next)
            {
                bool eq = refKey
                    ? ops.eq(ops.cmp, keyRef, slots[s].keyRef)
                    : (ops.cmp != nullptr
                           ? LinqValueEqualsCmpCell(ops.cmp, keyCell, keyCarrier, slots[s].key)
                           : JoinKeyEquals(keyCarrier, nullptr, slots[s].key, nullptr, keyCell));
                if (eq)
                {
                    chain.push_back(slots[s].innerIndex);
                }
            }
            for (size_t c = chain.size(); c > 0; --c)
            {
                pairs.push_back(i);
                pairs.push_back(chain[c - 1]);
            }
        }
    }

    const size_t pairCount = pairs.size() / 2;
    Il2CppArray* out = LinqNewArray(resCell, static_cast<int32_t>(pairCount));
    roots.Add(reinterpret_cast<void**>(&out));
    const Sel2Fn pick = kSel2[(rres * 4 + k1o) * 4 + k2i];
    for (size_t p = 0; p < pairCount; ++p)
    {
        const int32_t o = pairs[p * 2];
        const int32_t i = pairs[p * 2 + 1];
        Sel2Arg a1;
        switch (k1o)
        {
            case 0: a1.i4 = LinqCellReadI4(outer, o, outerCell); break;
            case 1: a1.i8 = LinqCellReadI8(outer, o, outerCell); break;
            case 2: a1.d = LinqCellReadD(outer, o, outerCell); break;
            default: a1.o = LinqCellReadRef(outer, o, outerCell); break;
        }
        Sel2Arg a2;
        switch (k2i)
        {
            case 0: a2.i4 = LinqCellReadI4(inner, i, innerCell); break;
            case 1: a2.i8 = LinqCellReadI8(inner, i, innerCell); break;
            case 2: a2.d = LinqCellReadD(inner, i, innerCell); break;
            default: a2.o = LinqCellReadRef(inner, i, innerCell); break;
        }
        const int64_t r = pick(rs, a1, a2);
        char* dst = LinqCellPtr(out, static_cast<int32_t>(p));
        if (rres == 3)
        {
            Il2CppObject* v = reinterpret_cast<Il2CppObject*>(r);
            *reinterpret_cast<Il2CppObject**>(dst) = v;
            il2cpp_write_barrier(reinterpret_cast<void**>(dst), v);
        }
        else
        {
            switch (resCell)
            {
                case IL2CPP_LINQ_CELL_SBYTE:
                case IL2CPP_LINQ_CELL_BYTE:
                case IL2CPP_LINQ_CELL_BOOL:
                case IL2CPP_LINQ_CELL_SHORT:
                case IL2CPP_LINQ_CELL_USHORT:
                case IL2CPP_LINQ_CELL_CHAR:
                case IL2CPP_LINQ_CELL_UINT:
                case IL2CPP_LINQ_CELL_I4:
                {
                    const int32_t i4 = static_cast<int32_t>(r);
                    std::memcpy(dst, &i4, sizeof(i4));
                    break;
                }
                case IL2CPP_LINQ_CELL_I8:
                    std::memcpy(dst, &r, sizeof(int64_t));
                    break;
                case IL2CPP_LINQ_CELL_F:
                {
                    double dv = 0.0;
                    std::memcpy(&dv, &r, sizeof(dv));
                    const float fv = static_cast<float>(dv);
                    std::memcpy(dst, &fv, sizeof(fv));
                    break;
                }
                case IL2CPP_LINQ_CELL_D:
                    std::memcpy(dst, &r, sizeof(double));
                    break;
                default:
                    break;
            }
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// b5-gaps section 65: Enumerable.GroupJoin(..., IEqualityComparer<TKey>)
// over ARRAY sources with REFERENCE TKeys -- the comparer-supported twin of
// section 63. The comparer ops pair (section 64) drives the TKey hashing and
// equality; the group materialization writes each result straight into the
// preallocated outer-length array.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_group_join_cmp(Il2CppArray* outer, Il2CppArray* inner,
                                        Il2CppObject* outerKeySel, Il2CppObject* innerKeySel,
                                        Il2CppObject* resSel, Il2CppObject* cmp,
                                        int32_t outerCell, int32_t innerCell,
                                        int32_t keyCell, int32_t resCell)
{
    if (outer == nullptr || inner == nullptr)
    {
        il2cpp_throw_argument_null_exception("outer");
        return nullptr;
    }
    Il2CppClass* oCls = reinterpret_cast<Il2CppObject*>(outer)->klass;
    Il2CppClass* iCls = reinterpret_cast<Il2CppObject*>(inner)->klass;
    if (oCls == nullptr || oCls->name == nullptr || oCls->name[0] != '[' ||
        iCls == nullptr || iCls->name == nullptr || iCls->name[0] != '[')
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.GroupJoin(comparer): a source is not a one-dimensional "
            "array (List/IEnumerable sources stay LOUD, b5-gaps section 65).");
        return nullptr;
    }
    LinqRequireCell(outer, outerCell);
    LinqRequireCell(inner, innerCell);
    const int32_t k1o = Sel2ArgSlotOf(outerCell);
    const int32_t k2i = Sel2ArgSlotOf(innerCell);
    const int32_t rres = Sel2ArgSlotOf(resCell);
    // b5-gaps section 66: the comparer overload now also binds VALUE TKey
    // cells (see the section-66 Join(comparer) notes -- same pairing rule).
    const int32_t gjKey = Sel2ArgSlotOf(keyCell);
    if (k1o < 0 || k2i < 0 || rres < 0 || gjKey < 0 ||
        (keyCell != IL2CPP_LINQ_CELL_REF && JoinValueHashFragment(keyCell) == nullptr))
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.GroupJoin(comparer): this TKey cell has no comparer "
            "GetHashCode wiring (decimal/DateTime/TimeSpan/Guid/DateTimeOffset "
            "comparer overloads stay LOUD, b5-gaps section 66).");
        return nullptr;
    }
    Il2CppDelegate* ok = reinterpret_cast<Il2CppDelegate*>(outerKeySel);
    Il2CppDelegate* ik = reinterpret_cast<Il2CppDelegate*>(innerKeySel);
    Il2CppDelegate* rs = reinterpret_cast<Il2CppDelegate*>(resSel);
    if (ok == nullptr || ok->methodPtr == nullptr || ik == nullptr ||
        ik->methodPtr == nullptr || rs == nullptr || rs->methodPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    const JoinCmpOps ops = JoinCmpOpsOf(cmp);
    const bool refKey = keyCell == IL2CPP_LINQ_CELL_REF;
    const int32_t on = outer->length;
    const int32_t inn = inner->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&outer));
    roots.Add(reinterpret_cast<void**>(&inner));
    roots.Add(reinterpret_cast<void**>(&ok));
    roots.Add(reinterpret_cast<void**>(&ik));
    roots.Add(reinterpret_cast<void**>(&rs));
    roots.Add(reinterpret_cast<void**>(&cmp));

    size_t bucketCount = 0;
    std::vector<int32_t> buckets;
    std::vector<JoinSlot> slots;
    std::vector<Il2CppObject*> keyRoots;
    if (inn > 0)
    {
        bucketCount = static_cast<size_t>(RoundUpPow2(inn * 2));
        if (bucketCount < 8)
        {
            bucketCount = 8;
        }
        try
        {
            buckets.assign(bucketCount, -1);
            slots.reserve(static_cast<size_t>(inn));
            keyRoots.reserve(static_cast<size_t>(inn));
        }
        catch (const std::bad_alloc&)
        {
            il2cpp_throw_new(
                "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                "Insufficient memory to continue the execution of the program.");
            return nullptr;
        }
        const Sel1Fn ike1 = kSel1[(gjKey * 4 + k2i)];
        for (int32_t j = 0; j < inn; ++j)
        {
            Sel2Arg a2;
            switch (k2i)
            {
                case 0: a2.i4 = LinqCellReadI4(inner, j, innerCell); break;
                case 1: a2.i8 = LinqCellReadI8(inner, j, innerCell); break;
                case 2: a2.d = LinqCellReadD(inner, j, innerCell); break;
                default: a2.o = LinqCellReadRef(inner, j, innerCell); break;
            }
            const int64_t keyCarrier = ike1(ik, a2);
            Il2CppObject* keyRef = refKey ? reinterpret_cast<Il2CppObject*>(keyCarrier) : nullptr;
            const int32_t h = (refKey
                    ? ops.hash(ops.cmp, keyRef)
                    : (ops.cmp != nullptr
                           ? JoinCmpValueHashSlot(ops.cmp, keyCell, keyCarrier)
                           : JoinKeyHash(keyCarrier, nullptr, keyCell)))
                & (static_cast<int32_t>(bucketCount) - 1);
            const size_t slotIdx = slots.size();
            JoinSlot slot;
            slot.next = buckets[h];
            slot.key = keyCarrier;
            slot.keyRef = keyRef;
            slot.innerIndex = j;
            try
            {
                slots.push_back(slot);
                if (refKey)
                {
                    keyRoots.push_back(keyRef);
                }
            }
            catch (const std::bad_alloc&)
            {
                il2cpp_throw_new(
                    "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                    "Insufficient memory to continue the execution of the program.");
                return nullptr;
            }
            if (refKey)
            {
                roots.Add(reinterpret_cast<void**>(&keyRoots[keyRoots.size() - 1]));
            }
            buckets[h] = static_cast<int32_t>(slotIdx);
        }
    }

    Il2CppArray* out = LinqNewArray(resCell, on);
    roots.Add(reinterpret_cast<void**>(&out));
    const Sel2Fn pick = kSel2[(rres * 4 + k1o) * 4 + 3];
    const Sel1Fn oke1 = inn > 0 ? kSel1[(gjKey * 4 + k1o)] : nullptr;
    std::vector<int32_t> chain;
    for (int32_t i = 0; i < on; ++i)
    {
        std::vector<int32_t> matched;
        if (oke1 != nullptr)
        {
            Sel2Arg a1;
            switch (k1o)
            {
                case 0: a1.i4 = LinqCellReadI4(outer, i, outerCell); break;
                case 1: a1.i8 = LinqCellReadI8(outer, i, outerCell); break;
                case 2: a1.d = LinqCellReadD(outer, i, outerCell); break;
                default: a1.o = LinqCellReadRef(outer, i, outerCell); break;
            }
            const int64_t keyCarrier = oke1(ok, a1);
            Il2CppObject* keyRef = refKey ? reinterpret_cast<Il2CppObject*>(keyCarrier) : nullptr;
            const int32_t h = (refKey
                    ? ops.hash(ops.cmp, keyRef)
                    : (ops.cmp != nullptr
                           ? JoinCmpValueHashSlot(ops.cmp, keyCell, keyCarrier)
                           : JoinKeyHash(keyCarrier, nullptr, keyCell)))
                & (static_cast<int32_t>(bucketCount) - 1);
            chain.clear();
            for (int32_t s = buckets[h]; s >= 0; s = slots[s].next)
            {
                bool eq = refKey
                    ? ops.eq(ops.cmp, keyRef, slots[s].keyRef)
                    : (ops.cmp != nullptr
                           ? LinqValueEqualsCmpCell(ops.cmp, keyCell, keyCarrier, slots[s].key)
                           : JoinKeyEquals(keyCarrier, nullptr, slots[s].key, nullptr, keyCell));
                if (eq)
                {
                    chain.push_back(slots[s].innerIndex);
                }
            }
            matched.reserve(chain.size());
            for (size_t c = chain.size(); c > 0; --c)
            {
                matched.push_back(chain[c - 1]);
            }
        }

        Il2CppArray* group = LinqNewArray(innerCell, static_cast<int32_t>(matched.size()));
        roots.Add(reinterpret_cast<void**>(&group));
        for (size_t k = 0; k < matched.size(); ++k)
        {
            char* dst = LinqCellPtr(group, static_cast<int32_t>(k));
            LinqCellCopy(dst, reinterpret_cast<Il2CppObject**>(dst), inner,
                         matched[k], innerCell);
        }

        Sel2Arg a1;
        switch (k1o)
        {
            case 0: a1.i4 = LinqCellReadI4(outer, i, outerCell); break;
            case 1: a1.i8 = LinqCellReadI8(outer, i, outerCell); break;
            case 2: a1.d = LinqCellReadD(outer, i, outerCell); break;
            default: a1.o = LinqCellReadRef(outer, i, outerCell); break;
        }
        Sel2Arg a2;
        a2.o = reinterpret_cast<Il2CppObject*>(group);
        const int64_t r = pick(rs, a1, a2);
        char* dst = LinqCellPtr(out, i);
        if (rres == 3)
        {
            Il2CppObject* v = reinterpret_cast<Il2CppObject*>(r);
            *reinterpret_cast<Il2CppObject**>(dst) = v;
            il2cpp_write_barrier(reinterpret_cast<void**>(dst), v);
        }
        else
        {
            switch (resCell)
            {
                case IL2CPP_LINQ_CELL_SBYTE:
                case IL2CPP_LINQ_CELL_BYTE:
                case IL2CPP_LINQ_CELL_BOOL:
                case IL2CPP_LINQ_CELL_SHORT:
                case IL2CPP_LINQ_CELL_USHORT:
                case IL2CPP_LINQ_CELL_CHAR:
                case IL2CPP_LINQ_CELL_UINT:
                case IL2CPP_LINQ_CELL_I4:
                {
                    const int32_t i4 = static_cast<int32_t>(r);
                    std::memcpy(dst, &i4, sizeof(i4));
                    break;
                }
                case IL2CPP_LINQ_CELL_I8:
                    std::memcpy(dst, &r, sizeof(int64_t));
                    break;
                case IL2CPP_LINQ_CELL_F:
                {
                    double dv = 0.0;
                    std::memcpy(&dv, &r, sizeof(dv));
                    const float fv = static_cast<float>(dv);
                    std::memcpy(dst, &fv, sizeof(fv));
                    break;
                }
                case IL2CPP_LINQ_CELL_D:
                    std::memcpy(dst, &r, sizeof(double));
                    break;
                default:
                    break;
            }
        }
    }
    return out;
}

} // namespace il2cpp
