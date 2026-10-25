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

#include "arraysort.h"                 // FindVirtualSlotByNameAndSignature
#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_value_semantics.h"    // icall_object_equals
#include "string_compare.h"            // il2cpp_string_compare_ordinal

#include <algorithm>
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
}

// Single: exactly one element (empty and >1 both throw).
IL2CPP_LINQ_DEF_ELEM_ENTRIES_NOARG(single, RequireSingle, false)
// SingleOrDefault: 0 or 1 (empty -> default, >1 throws).
IL2CPP_LINQ_DEF_ELEM_ENTRIES_NOARG(single_or_default, RequireAtMostOne, true)

#undef IL2CPP_LINQ_DEF_ELEM_ENTRIES
#undef IL2CPP_LINQ_DEF_ELEM_ENTRIES_NOARG

} // namespace il2cpp
