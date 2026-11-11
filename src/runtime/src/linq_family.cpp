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
#include "il2cpp_boxing_ext.h"  // icall_box_double / icall_unbox_double (section 109)
#include "il2cpp_boxing_ext2.h" // icall_box_long (b5-gaps section 106)
#include "il2cpp_nullable.h"    // icall_box_date_time / icall_unbox_date_time (section 112)
#include "linq_list.h"                 // il2cpp_linq_list_snapshot (List<int> inners)
#include "listi4.h"                    // il2cpp_list_i4_class (List<int> inners)
#include "dictgen.h"                   // b5-gaps section 78: the Dictionary`2 wrapper + gen table
#include "linq_map.h"                  // section 81: Il2CppLinqGrouping + il2cpp_linq_grouping_class
#include "listobj.h"                  // b5-gaps section 86: il2cpp_list_obj_create/_add (ToList over the lookup)
#include "sortedlist.h"                // section 81: il2cpp_sortedlist_snapshot_get_enumerator

#include "arraysort.h"                 // FindVirtualSlotByNameAndSignature
#include "il2cpp_array.h"              // kElemKind* (b5-gaps section 70 narrow Join)
#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"            // il2cpp_object_new (the ToDictionary wrapper, section 78)
#include "il2cpp_value_semantics.h"    // icall_object_equals
#include "string_compare.h"            // il2cpp_string_compare_ordinal

#include <algorithm>
#include <cstdio>
#include <cstdlib>
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

// b5-gaps section 94: Append / Prepend. Append(source, element) IS
// Concat(source, [element]) -- the single-element tail rides the SAME cell as
// the source (net48 yields the identical sequence plus exactly one element,
// and the appended reference is the IDENTICAL object). The runtime ships one
// entry per element family; a family with no entry (decimal / DateTime /
// TimeSpan / Guid / DateTimeOffset) stays LOUD in the transpiler instead of
// fabricating a cell write.
Il2CppArray* il2cpp_linq_append_i4(Il2CppArray* first, int32_t element, int32_t cell)
{
    Il2CppArray* tail = il2cpp_linq_repeat_i4(element, 1, cell);
    return il2cpp_linq_concat_family(first, tail, cell);
}

Il2CppArray* il2cpp_linq_prepend_i4(Il2CppArray* first, int32_t element, int32_t cell)
{
    Il2CppArray* head = il2cpp_linq_repeat_i4(element, 1, cell);
    return il2cpp_linq_concat_family(head, first, cell);
}

Il2CppArray* il2cpp_linq_append_i8(Il2CppArray* first, int64_t element, int32_t cell)
{
    Il2CppArray* tail = il2cpp_linq_repeat_i8(element, 1, cell);
    return il2cpp_linq_concat_family(first, tail, cell);
}

Il2CppArray* il2cpp_linq_prepend_i8(Il2CppArray* first, int64_t element, int32_t cell)
{
    Il2CppArray* head = il2cpp_linq_repeat_i8(element, 1, cell);
    return il2cpp_linq_concat_family(head, first, cell);
}

Il2CppArray* il2cpp_linq_append_d(Il2CppArray* first, double element, int32_t cell)
{
    Il2CppArray* tail = il2cpp_linq_repeat_d(element, 1, cell);
    return il2cpp_linq_concat_family(first, tail, cell);
}

Il2CppArray* il2cpp_linq_prepend_d(Il2CppArray* first, double element, int32_t cell)
{
    Il2CppArray* head = il2cpp_linq_repeat_d(element, 1, cell);
    return il2cpp_linq_concat_family(head, first, cell);
}

Il2CppArray* il2cpp_linq_append_ref(Il2CppArray* first, Il2CppObject* element)
{
    Il2CppArray* tail = il2cpp_linq_repeat_ref(element, 1);
    return il2cpp_linq_concat_family(first, tail, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_prepend_ref(Il2CppArray* first, Il2CppObject* element)
{
    Il2CppArray* head = il2cpp_linq_repeat_ref(element, 1);
    return il2cpp_linq_concat_family(head, first, IL2CPP_LINQ_CELL_REF);
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
// b5-gaps section 112: DateTime rides the int64 carrier but crosses as a 16-byte
// Il2CppDateTime struct (ticks in RAX, kind in RDX). Sel2Get<int64_t> reads the
// low 62 bits as ticks; Sel1_dt_dt reconstructs the struct for the delegate call.
#define S2_ARG_T_dt runtime::Il2CppDateTime
#define S2_RT_T_dt runtime::Il2CppDateTime

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
        case IL2CPP_LINQ_CELL_DATETIME:
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

// b5-gaps section 112: DateTime selector ABI. Il2CppDateTime is a 16-byte
// value (ticks + kind) passed/returned in two registers (RAX/RDX on x64).
// Sel1_i8_i8 would pass int64_t in one register and drop the kind register --
// wrong for Func<DateTime,DateTime>. We pass ticks in the int64 carrier and
// kind=0 (net48 DateTime array cells strip the kind), then reconstruct the
// 16-byte struct for the delegate call.
int64_t Sel1_dt_dt(Il2CppDelegate* d, Sel2Arg a1)
{
    runtime::Il2CppDateTime arg(Sel2Get<int64_t>(a1));
    runtime::Il2CppDateTime r = d->target != nullptr
        ? (reinterpret_cast<runtime::Il2CppDateTime (*)(Il2CppObject*,
                              runtime::Il2CppDateTime)>(d->methodPtr))
              (d->target, arg)
        : (reinterpret_cast<runtime::Il2CppDateTime (*)(runtime::Il2CppDateTime)>(
              d->methodPtr))(arg);
    return r.ticks;
}

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

// b5-gaps section 78: Enumerable.ToDictionary over an ARRAY source.
//
// src         -- the source array (element family in srcCell).
// keySel      -- Func<TSource,TKey> (never null; a null THROWS ArgumentNullException).
// elemSel     -- Func<TSource,TElement>, or null (value = the element itself).
// keyKind     -- kGenKind_Int32 (1) = int32 key (inline in entry.keyInt);
//                kGenKind_String (0) = reference key (a managed carrier).
// valueKind   -- kGenKind_Int32 (1) = int32 value (BOXED into a managed object);
//                kGenKind_String (0) = reference value.
// srcCell     -- the source element cell code (LinqCell family).
//
// NET48 CONTRACT: a duplicate key -> ArgumentException (the dictgen add throws
// "Item has already been added.", a Dictionary-default ArgumentException -- the
// probe pins the TYPE, not the message); a null key -> ArgumentNullException
// ("key"), the dictgen null-carrier guard. The enumeration order follows the
// INSERTION order (the gen table's insertion-sequence -> the Keys snapshot the
// instance get_Keys icall serves).
//
// REF-KEY ROOTING (the objective's "ref 閿?rooting"): the dictgen table stores
// managed carriers in a RAW allocation the collector cannot see. The selector
// returns NEW strings (NOT interned literals), so the wrapper owns a managed
// `object[]` root pack (dictgen.h Il2CppDictGenObject::keysRoot, refFieldBitmap
// bit 2): every ref key and every value object is written into it with the
// write barrier as it is inserted, keeping them alive for the dictionary's
// whole lifecycle. int32 keys ride entry.keyInt (no carrier to root); boxed
// int32 values go into the pack like any other managed object.
Il2CppObject* il2cpp_linq_to_dictionary(Il2CppArray* src, Il2CppObject* keySel,
                                        Il2CppObject* elemSel, int32_t keyKind,
                                        int32_t valueKind, int32_t srcCell)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    if (keySel == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    Il2CppClass* cls = reinterpret_cast<Il2CppObject*>(src)->klass;
    if (cls == nullptr || cls->name == nullptr || cls->name[0] != '[')
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToDictionary: the source is not a one-dimensional array "
            "(List/IEnumerable sources stay LOUD, b5-gaps section 78).");
        return nullptr;
    }
    LinqRequireCell(src, srcCell);
    const int32_t srcSlot = Sel2ArgSlotOf(srcCell);
    if (srcSlot < 0)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToDictionary: the source element family has no selector "
            "ABI (b5-gaps section 78).");
        return nullptr;
    }
    if (keyKind != kGenKind_Int32 && keyKind != kGenKind_Int64Key
        && keyKind != kGenKind_String)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToDictionary: this TKey family has no dictgen key kind "
            "(b5-gaps section 78/108).");
        return nullptr;
    }
    if (valueKind != kGenKind_Int32 && valueKind != kGenKind_Int64
        && valueKind != kGenKind_String && valueKind != kGenKind_Double)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToDictionary: this TValue family has no dictgen value "
            "kind (b5-gaps section 78/106/109).");
        return nullptr;
    }
    Il2CppDelegate* kd = reinterpret_cast<Il2CppDelegate*>(keySel);
    Il2CppDelegate* vd = reinterpret_cast<Il2CppDelegate*>(elemSel);
    if (kd == nullptr || kd->methodPtr == nullptr ||
        (vd != nullptr && vd->methodPtr == nullptr))
    {
        il2cpp_throw_argument_null_exception(vd == nullptr ? "keySelector"
                                                           : "elementSelector");
        return nullptr;
    }
    const int32_t keySlot = keyKind == kGenKind_Int32 ? 0
        : keyKind == kGenKind_Int64Key || keyKind == kGenKind_DateTime || keyKind == kGenKind_Double ? 1
        : 3; // i4 / ref (o)
    const int32_t valSlot = valueKind == kGenKind_Int32 ? 0
        : valueKind == kGenKind_Int64 ? 1
        : valueKind == kGenKind_Double ? 2 : 3;
    const Sel1Fn ikey = kSel1[(keySlot * 4 + srcSlot)];
    const Sel1Fn ival = vd != nullptr ? kSel1[(valSlot * 4 + srcSlot)] : nullptr;
    const int32_t n = src->length;

    Il2CppObject* wObj = il2cpp_object_new(il2cpp_dict_gen_object_class());
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));
    roots.Add(reinterpret_cast<void**>(&elemSel));
    roots.Add(reinterpret_cast<void**>(&wObj));
    Il2CppDictGenObject* wrapper =
        reinterpret_cast<Il2CppDictGenObject*>(wObj);
    wrapper->dict = il2cpp_dict_gen_create(n > 0 ? n : 4);
    wrapper->keysRoot = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, n * 2 + 4);
    int32_t packIdx = 0;

    for (int32_t i = 0; i < n; ++i)
    {
        Sel2Arg a;
        switch (srcSlot)
        {
            case 0: a.i4 = LinqCellReadI4(src, i, srcCell); break;
            case 1: a.i8 = LinqCellReadI8(src, i, srcCell); break;
            case 2: a.d  = LinqCellReadD(src, i, srcCell); break;
            default: a.o = LinqCellReadRef(src, i, srcCell); break;
        }
        const int64_t kc = ikey(kd, a);
        int32_t keyInt = 0;
        Il2CppObject* keyRef = nullptr;
        runtime::Il2CppDateTime keyDtVal{};
        double keyDblVal = 0.0;
        if (keyKind == kGenKind_Int32)
        {
            keyInt = static_cast<int32_t>(kc);
        }
        else if (keyKind != kGenKind_Int64Key)
        {
            keyRef = reinterpret_cast<Il2CppObject*>(kc);
        }
        Il2CppObject* value = nullptr;
        if (vd != nullptr)
        {
            const int64_t vc = ival(vd, a);
            if (valueKind == kGenKind_Int32)
            {
                value = il2cpp_dict_gen_box_int32(static_cast<int32_t>(vc));
            }
            else if (valueKind == kGenKind_Int64)
            {
                // b5-gaps section 106: the int64 value rides a boxed int64
                // carrier (the same 8-byte payload slot as the boxed int32).
                value = runtime::icall_box_long(vc);
            }
            else if (valueKind == kGenKind_Double)
            {
                // b5-gaps section 109: the element selector returns a double --
                // its bits arrive as int64 through the Sel1 ABI; re-interpret,
                // never convert (cf. section 107's key-double memcpy).
                double vdVal;
                std::memcpy(&vdVal, &vc, sizeof(vdVal));
                value = runtime::icall_box_double(vdVal);
            }
            else
            {
                value = reinterpret_cast<Il2CppObject*>(vc);
            }
        }
        else if (valueKind == kGenKind_Int32)
        {
            value = il2cpp_dict_gen_box_int32(a.i4);
        }
        else if (valueKind == kGenKind_Int64)
        {
            // b5-gaps section 106: the 2-arg form's value IS the source
            // element -- a long source element is a boxed int64 carrier.
            value = runtime::icall_box_long(a.i8);
        }
        else if (valueKind == kGenKind_Double)
        {
            // b5-gaps section 109: the 2-arg form's value IS the source
            // element -- a double source element is a boxed double carrier.
            value = runtime::icall_box_double(a.d);
        }
        else
        {
            value = a.o;
        }
        // The box / delegate allocations above may compact the heap; re-derive
        // every managed pointer from the roots before the barrier-writes.
        wrapper = reinterpret_cast<Il2CppDictGenObject*>(wObj);
        Il2CppObject** pack = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(wrapper->keysRoot, 0));
        if (keyRef != nullptr)
        {
            pack[packIdx] = keyRef;
            il2cpp_write_barrier(reinterpret_cast<void**>(&pack[packIdx]), keyRef);
            ++packIdx;
        }
        if (value != nullptr)
        {
            pack[packIdx] = value;
            il2cpp_write_barrier(reinterpret_cast<void**>(&pack[packIdx]), value);
            ++packIdx;
        }
        // b5-gaps section 108: the int64 key rides the dedicated table arm.
        if (keyKind == kGenKind_Int64Key)
        {
            il2cpp_dict_gen_add_kl(wrapper->dict, kc, value, keyKind, valueKind);
        }
        else
        {
            il2cpp_dict_gen_add(wrapper->dict, keyInt, keyRef, value, keyKind, valueKind);
        }
    }
    return wObj;
}
// b5-gaps section 83: Enumerable.ToDictionary over a List<T> source (int32 or
// System.String elements). The receiver is a LIST OBJECT -- never a cast of the
// managed List to Il2CppArray* (the items pointer would be read as the array
// length). The list is snapshotted through il2cpp_linq_list_snapshot into a
// fresh element-cell array (int32 cells when srcCell=="7", reference cells when
// srcCell=="15"), then the section-78 array kernel materialises the SAME
// Dictionary`2 wrapper -- get_Item / get_Count / get_Keys / ContainsKey behave
// byte-identically to the array case. The elementSelector is glued in either
// form (null for the 2-arg value-is-element form; the kernel's 3rd param for
// the elementSelector form).
Il2CppObject* il2cpp_linq_list_to_dictionary(Il2CppObject* list, Il2CppObject* keySel,
                                             Il2CppObject* elemSel, int32_t keyKind,
                                             int32_t valueKind, int32_t srcCell)
{
    if (list == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    if (keySel == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    const int32_t snapKind =
        (srcCell == 15) ? IL2CPP_LIST_KIND_REF
        : (srcCell == 8) ? IL2CPP_LIST_KIND_I8
        : IL2CPP_LIST_KIND_I4;
    Il2CppArray* snap = il2cpp_linq_list_snapshot(list, snapKind);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&snap));
    return il2cpp_linq_to_dictionary(snap, keySel, elemSel, keyKind, valueKind, srcCell);
}

// b5-gaps section 84: Enumerable.ToLookup with an elementSelector (the
// 3-generic / 4-generic overloads). Mirrors il2cpp_linq_to_lookup EXCEPT:
// each source element is ALSO passed through the element selector, and the
// group snapshot arrays hold the SELECTED values (family elemCell: "7"
// int32 cells / "15" reference-string cells) instead of the source values.
// The selector is called EXACTLY ONCE per source element, so the selected
// values must survive the two-pass materialization: an int selection is
// stored in a raw int32 row buffer, a REF selection into the wrapper's
// elemRoot root pack (slot 5, barrier-written). elemSel may allocate/collect;
// every managed pointer is re-derived from the rooted handle per iteration.
Il2CppObject* il2cpp_linq_to_lookup_sel(Il2CppArray* src, Il2CppObject* keySel,
                                        Il2CppObject* elemSel, Il2CppObject* comparer,
                                        int32_t keyKind, int32_t srcCell,
                                        int32_t elemCell)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    if (keySel == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    if (elemSel == nullptr)
    {
        il2cpp_throw_argument_null_exception("elementSelector");
        return nullptr;
    }
    Il2CppClass* cls = reinterpret_cast<Il2CppObject*>(src)->klass;
    if (cls == nullptr || cls->name == nullptr || cls->name[0] != '[')
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToLookup(elementSelector): the source is not a "
            "one-dimensional array (b5-gaps section 84).");
        return nullptr;
    }
    LinqRequireCell(src, srcCell);
    const int32_t srcSlot = Sel2ArgSlotOf(srcCell);
    // b5-gaps section 105: the source and the SELECTED value may each be an
    // int64 (a long[] source with an x*10 elementSelector) -- the selector ABI
    // table covers the i8 slot (Sel2ArgSlotOf cell 8 -> 1) and the selected
    // value rides the same int64 raw buffer / rooted ref array as the int32
    // family. A float/double cell has no Sel2ArgSlotOf mapping and stays LOUD.
    if (srcSlot < 0
        || (elemCell != IL2CPP_LINQ_CELL_I4 && elemCell != IL2CPP_LINQ_CELL_I8
            && elemCell != IL2CPP_LINQ_CELL_REF))
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToLookup(elementSelector): a source/element family has "
            "no selector cell ABI (int32/int64/System.String, b5-gaps section 105).");
        return nullptr;
    }
    if (keyKind != kGenKind_Int32 && keyKind != kGenKind_Int64
        && keyKind != kGenKind_Double && keyKind != kGenKind_String
        && keyKind != kGenKind_DateTime)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToLookup(elementSelector): this TKey family has no "
            "lookup key kind (b5-gaps section 105/107/112).");
        return nullptr;
    }
    Il2CppDelegate* kd = reinterpret_cast<Il2CppDelegate*>(keySel);
    Il2CppDelegate* ed = reinterpret_cast<Il2CppDelegate*>(elemSel);
    if (kd == nullptr || kd->methodPtr == nullptr || ed->methodPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    const int32_t keySlot = keyKind == kGenKind_Int32 ? 0
        : keyKind == kGenKind_Int64 || keyKind == kGenKind_DateTime ? 1
        : keyKind == kGenKind_Double ? 2
        : 3; // i4 / i8 / dt / f+d / ref (o)
    const int32_t elemSlot = elemCell == IL2CPP_LINQ_CELL_I4 ? 0
        : elemCell == IL2CPP_LINQ_CELL_I8 ? 1 : 3;
    const Sel1Fn ikey = keyKind == kGenKind_DateTime ? Sel1_dt_dt
        : kSel1[(keySlot * 4 + srcSlot)];
    const Sel1Fn iele = kSel1[(elemSlot * 4 + srcSlot)];
    const int32_t n = src->length;

    Il2CppObject* wObj = il2cpp_object_new(il2cpp_lookup_gen_object_class());
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));
    roots.Add(reinterpret_cast<void**>(&elemSel));
    roots.Add(reinterpret_cast<void**>(&comparer));
    roots.Add(reinterpret_cast<void**>(&wObj));
    Il2CppLookupGenObject* wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    wrapper->lookup = il2cpp_lookup_gen_create(n > 0 ? n : 4);
    wrapper->lookup->keyKind = keyKind;
    wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    wrapper->groupingObjs = nullptr; // b5-gaps section 89: lazy cache sentinel
    wrapper->keysRoot = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, n + 4);
    wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    wrapper->groupsRoot = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, n + 4);
    wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    if (elemCell == IL2CPP_LINQ_CELL_REF)
    {
        // REF selections held between the passes: n+4 rooted slots.
        wrapper->elemRoot = il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(void*)), true, n + 4);
        wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    }
    wrapper->comparer = comparer;
    il2cpp_write_barrier(reinterpret_cast<void**>(&wrapper->comparer), comparer);

    Il2CppLookupGen* lk = wrapper->lookup;
    // b5-gaps section 105: the selected values ride a raw int64 buffer for the
    // int64 family (the same discipline as the int32 selRaw) and the rooted
    // elemRoot array for the reference family.
    const bool elemIsRef = elemCell == IL2CPP_LINQ_CELL_REF;
    const bool elemIsI8 = elemCell == IL2CPP_LINQ_CELL_I8;
    int64_t* selRaw = nullptr;
    if (!elemIsRef)
    {
        selRaw = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * (n > 0 ? n : 1)));
    }
    int32_t* srcGroup = static_cast<int32_t*>(
        std::malloc(sizeof(int32_t) * (n > 0 ? n : 1)));
    for (int32_t i = 0; i < n; ++i)
    {
        Sel2Arg a;
        switch (srcSlot)
        {
            case 0: a.i4 = LinqCellReadI4(src, i, srcCell); break;
            case 1: a.i8 = LinqCellReadI8(src, i, srcCell); break;
            case 2: a.d  = LinqCellReadD(src, i, srcCell); break;
            default: a.o = LinqCellReadRef(src, i, srcCell); break;
        }
        const int64_t kc = ikey(kd, a);
        int32_t keyInt = 0;
        Il2CppObject* keyRef = nullptr;
        runtime::Il2CppDateTime keyDtVal{};
        double keyDblVal = 0.0;
        if (keyKind == kGenKind_Int32)
        {
            keyInt = static_cast<int32_t>(kc);
        }
        else if (keyKind == kGenKind_Double)
        {
            // b5-gaps section 107: the double key rides the int64 CARRIER (the
            // selector ABI erases the 64-bit image); passing it to the double
            // parameter would be an int64->double VALUE conversion (cvtsi2sd),
            // so the bits must be re-interpreted, never converted.
            std::memcpy(&keyDblVal, &kc, sizeof(keyDblVal));
        }
        else if (keyKind == kGenKind_DateTime)
        {
            // b5-gaps section 112: the DateTime key rides slot 1 as raw ticks
            // (net48 DateTime array cells pack the kind in the top bits, but
            // LinqCellReadI8 strips them -- the low 62 bits are the ticks).
            keyDtVal = runtime::Il2CppDateTime(kc);
        }
        else
        {
            keyRef = reinterpret_cast<Il2CppObject*>(kc);
        }
        const int64_t ec = iele(ed, a);
        if (!elemIsRef)
        {
            selRaw[i] = ec;
        }
        else
        {
            // re-derive the rooted wrapper before the barrier-write (elemSel
            // may have collected).
            wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
            Il2CppObject** elems = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(wrapper->elemRoot, 0));
            Il2CppObject* v = reinterpret_cast<Il2CppObject*>(ec);
            elems[i] = v;
            il2cpp_write_barrier(reinterpret_cast<void**>(&elems[i]), v);
        }
        // b5-gaps section 107: the double key rides the int64 CARRIER (the
        // selector ABI erases the 64-bit image); passing it to the double
        // parameter would be an int64->double VALUE conversion (cvtsi2sd), so
        // the bits must be re-interpreted, never converted.
        const int32_t group = keyKind == kGenKind_Int64
            ? il2cpp_lookup_gen_find_or_add_i64(wObj, kc, keyKind, comparer)
            : keyKind == kGenKind_Double
                ? il2cpp_lookup_gen_find_or_add_dbl(wObj, keyDblVal, keyKind, comparer)
                : keyKind == kGenKind_DateTime
                    ? il2cpp_lookup_gen_find_or_add_dt(wObj, keyDtVal, keyKind, comparer)
                    : il2cpp_lookup_gen_find_or_add(wObj, keyInt, keyRef, keyKind, comparer);
        srcGroup[i] = group;
        ++lk->groupLen[group];
    }
    // Phase 2: prefix-sum the per-group counts into groupOff, then scatter
    // the collected source indices so each group's block is contiguous and
    // preserves SOURCE order inside the group.
    int32_t run = 0;
    for (int32_t g = 0; g < lk->count; ++g)
    {
        lk->groupOff[g] = run;
        run += lk->groupLen[g];
    }
    int32_t* fill = static_cast<int32_t*>(
        std::malloc(sizeof(int32_t) * (lk->count > 0 ? lk->count : 1)));
    for (int32_t g = 0; g < lk->count; ++g) fill[g] = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t g = srcGroup[i];
        lk->srcIdx[lk->groupOff[g] + fill[g]] = i;
        ++fill[g];
    }
    std::free(fill);
    std::free(srcGroup);
    // Phase 3: materialize each group's SELECTED-value snapshot array.
    const int32_t gcount = lk->count;
    for (int32_t g = 0; g < gcount; ++g)
    {
        const int32_t len = lk->groupLen[g];
        Il2CppArray* arr = LinqNewArray(elemCell, len);
        char* dst = LinqCellPtr(arr, 0);
        for (int32_t k = 0; k < len; ++k)
        {
            const int32_t si = lk->srcIdx[lk->groupOff[g] + k];
            char* dslot = dst + k * static_cast<int64_t>(LinqCellSize(elemCell));
            if (!elemIsRef)
            {
                // b5-gaps section 105: the int32 and int64 families share the
                // raw selRaw buffer (the int64 slot carries both widths).
                if (elemIsI8)
                {
                    *reinterpret_cast<int64_t*>(dslot) = selRaw[si];
                }
                else
                {
                    *reinterpret_cast<int32_t*>(dslot) =
                        static_cast<int32_t>(selRaw[si]);
                }
            }
            else
            {
                wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
                Il2CppObject** elems = reinterpret_cast<Il2CppObject**>(
                    il2cpp_array_get_elem_ptr(wrapper->elemRoot, 0));
                Il2CppObject* v = elems[si];
                Il2CppObject** sslot = reinterpret_cast<Il2CppObject**>(dslot);
                *sslot = v;
                il2cpp_write_barrier(reinterpret_cast<void**>(sslot), v);
            }
        }
        wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
        Il2CppObject** groups = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(wrapper->groupsRoot, 0));
        Il2CppObject* arrObj = reinterpret_cast<Il2CppObject*>(arr);
        groups[g] = arrObj;
        il2cpp_write_barrier(reinterpret_cast<void**>(&groups[g]), arrObj);
    }
    std::free(selRaw);
    return wObj;
}

// b5-gaps section 79: Enumerable.ToLookup over an ARRAY source.
//
// src         -- the source array (element family in srcCell).
// keySel      -- Func<TSource,TKey> (never null; a null THROWS ArgumentNullException).
// keyKind     -- kGenKind_Int32 (1) = int32 key (inline in keyInt[]);
//                kGenKind_String (0) = reference key (managed carriers).
// srcCell     -- the source ELEMENT cell code (LinqCell family); the groups hold
//                TSource elements, so the group snapshot arrays are srcCell arrays.
//
// NET48 CONTRACT (probe-verified): distinct keys appear in FIRST-APPEARANCE order;
// each group's elements preserve SOURCE order. A NULL reference key is a LEGAL
// distinct key (Lookup.Create stores it as its own group -- unlike Dictionary --
// and the probe pins Count incl. the null group + Contains(null) == true). get_Item
// for a MISSING key returns an EMPTY sequence; get_Item(any null key) returns the
// null group. The default key comparer: int32 == / string ORDINAL content.
//
// REF-KEY + GROUP ROOTING (the objective's "ref-key rooting"): the distinct keys
// and the per-key element SNAPSHOT arrays live in TWO managed object[] packs the
// wrapper roots (refFieldBitmap bits 2 and 3). The materializer stores every
// selector-produced reference key into keysRoot and every snapshot array into
// groupsRoot (both through the write barrier). A selector that returns NEW strings
// (t7's GC.Collect-in-selector case) is therefore kept alive for the lookup's
// lifetime. Intermediate buffering is row SOURCE INDICES (raw ints, no managed
// pointers -- the section-62 Join discipline), materialized in a second pass.
Il2CppObject* il2cpp_linq_to_lookup(Il2CppArray* src, Il2CppObject* keySel,
                                    Il2CppObject* comparer, int32_t keyKind,
                                    int32_t srcCell)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    if (keySel == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    Il2CppClass* cls = reinterpret_cast<Il2CppObject*>(src)->klass;
    if (cls == nullptr || cls->name == nullptr || cls->name[0] != '[')
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToLookup: the source is not a one-dimensional array "
            "(List/IEnumerable sources stay LOUD, b5-gaps section 79).");
        return nullptr;
    }
    LinqRequireCell(src, srcCell);
    const int32_t srcSlot = Sel2ArgSlotOf(srcCell);
    if (srcSlot < 0)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToLookup: the source element family has no selector "
            "ABI (b5-gaps section 79).");
        return nullptr;
    }
    if (keyKind != kGenKind_Int32 && keyKind != kGenKind_Int64
        && keyKind != kGenKind_String && keyKind != kGenKind_DateTime)
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.ToLookup: this TKey family has no lookup key kind "
            "(b5-gaps section 79/107/112).");
        return nullptr;
    }
    Il2CppDelegate* kd = reinterpret_cast<Il2CppDelegate*>(keySel);
    if (kd == nullptr || kd->methodPtr == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    const int32_t keySlot = keyKind == kGenKind_Int32 ? 0
        : keyKind == kGenKind_Int64 || keyKind == kGenKind_DateTime ? 1
        : keyKind == kGenKind_Double ? 2
        : 3; // i4 / i8 / dt / f+d / ref (o)
    const Sel1Fn ikey = keyKind == kGenKind_DateTime ? Sel1_dt_dt
        : kSel1[(keySlot * 4 + srcSlot)];
    const int32_t n = src->length;

    Il2CppObject* wObj = il2cpp_object_new(il2cpp_lookup_gen_object_class());
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));
    roots.Add(reinterpret_cast<void**>(&comparer));
    roots.Add(reinterpret_cast<void**>(&wObj));
    Il2CppLookupGenObject* wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    wrapper->lookup = il2cpp_lookup_gen_create(n > 0 ? n : 4);
    wrapper->lookup->keyKind = keyKind;
    // Every managed array_new can collect/move the (rooted) wrapper: re-derive
    // the raw wrapper pointer from wObj before EACH store (section-78 discipline).
    wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    wrapper->groupingObjs = nullptr; // b5-gaps section 89: lazy cache sentinel
    wrapper->keysRoot = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, n + 4);
    wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    wrapper->groupsRoot = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, n + 4);
    wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    wrapper->comparer = comparer;
    il2cpp_write_barrier(reinterpret_cast<void**>(&wrapper->comparer), comparer);

    Il2CppLookupGen* lk = wrapper->lookup;
    int32_t* srcGroup = static_cast<int32_t*>(
        std::malloc(sizeof(int32_t) * (n > 0 ? n : 1)));
    for (int32_t i = 0; i < n; ++i)
    {
        Sel2Arg a;
        switch (srcSlot)
        {
            case 0: a.i4 = LinqCellReadI4(src, i, srcCell); break;
            case 1: a.i8 = LinqCellReadI8(src, i, srcCell); break;
            case 2: a.d  = LinqCellReadD(src, i, srcCell); break;
            default: a.o = LinqCellReadRef(src, i, srcCell); break;
        }
        const int64_t kc = ikey(kd, a);
        int32_t keyInt = 0;
        Il2CppObject* keyRef = nullptr;
        runtime::Il2CppDateTime keyDtVal{};
        double keyDblVal = 0.0;
        if (keyKind == kGenKind_Int32)
        {
            keyInt = static_cast<int32_t>(kc);
        }
        else if (keyKind == kGenKind_Double)
        {
            // b5-gaps section 107: the double key rides the int64 CARRIER (the
            // selector ABI erases the 64-bit image); passing it to the double
            // parameter would be an int64->double VALUE conversion (cvtsi2sd),
            // so the bits must be re-interpreted, never converted.
            std::memcpy(&keyDblVal, &kc, sizeof(keyDblVal));
        }
        else if (keyKind == kGenKind_DateTime)
        {
            // b5-gaps section 112: the DateTime key rides slot 1 as raw ticks
            // (net48 DateTime array cells pack the kind in the top bits, but
            // LinqCellReadI8 strips them -- the low 62 bits are the ticks).
            keyDtVal = runtime::Il2CppDateTime(kc);
        }
        else
        {
            keyRef = reinterpret_cast<Il2CppObject*>(kc);
        }
        // The selector may allocate (t7's GC.Collect), compacting the heap;
        // re-derive every managed pointer from the roots before any barrier-write.
        const int32_t group = keyKind == kGenKind_Int64
            ? il2cpp_lookup_gen_find_or_add_i64(wObj, kc, keyKind, comparer)
            : keyKind == kGenKind_Double
                ? il2cpp_lookup_gen_find_or_add_dbl(wObj, keyDblVal, keyKind, comparer)
                : keyKind == kGenKind_DateTime
                    ? il2cpp_lookup_gen_find_or_add_dt(wObj, keyDtVal, keyKind, comparer)
                    : il2cpp_lookup_gen_find_or_add(wObj, keyInt, keyRef, keyKind, comparer);
        srcGroup[i] = group;
        ++lk->groupLen[group];
    }
    // Phase 2: prefix-sum the per-group counts into groupOff, then scatter
    // the collected source indices so each group's block is contiguous and
    // preserves SOURCE order inside the group.
    int32_t run = 0;
    for (int32_t g = 0; g < lk->count; ++g)
    {
        lk->groupOff[g] = run;
        run += lk->groupLen[g];
    }
    int32_t* fill = static_cast<int32_t*>(
        std::malloc(sizeof(int32_t) * (lk->count > 0 ? lk->count : 1)));
    for (int32_t g = 0; g < lk->count; ++g) fill[g] = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t g = srcGroup[i];
        lk->srcIdx[lk->groupOff[g] + fill[g]] = i;
        ++fill[g];
    }
    std::free(fill);
    std::free(srcGroup);
    // Phase 3: materialize each group's element snapshot array from the
    // collected source indices (exact-size LinqNewArray, no reallocation).
    // NOTE: the groupsRoot pack pointer MUST be re-derived per group AFTER
    // LinqNewArray -- that allocation can collect/move the rooted wrapper, so
    // a pointer captured before the loop goes stale (section-78 discipline).
    const int32_t gcount = lk->count;
    for (int32_t g = 0; g < gcount; ++g)
    {
        const int32_t len = lk->groupLen[g];
        Il2CppArray* arr = LinqNewArray(srcCell, len);
        char* dst = LinqCellPtr(arr, 0);
        for (int32_t k = 0; k < len; ++k)
        {
            const int32_t si = lk->srcIdx[lk->groupOff[g] + k];
            char* dslot = dst + k * static_cast<int64_t>(LinqCellSize(srcCell));
            Il2CppObject** barrierSlot =
                srcCell == IL2CPP_LINQ_CELL_REF
                    ? reinterpret_cast<Il2CppObject**>(dslot)
                    : nullptr;
            LinqCellCopy(dslot, barrierSlot, src, si, srcCell);
        }
        wrapper = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
        Il2CppObject** groups = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(wrapper->groupsRoot, 0));
        Il2CppObject* arrObj = reinterpret_cast<Il2CppObject*>(arr);
        groups[g] = arrObj;
        il2cpp_write_barrier(reinterpret_cast<void**>(&groups[g]), arrObj);
    }
    return wObj;
}

// instance surface (routed by the tracked pseudo-type "System.Linq.ILookup`2_<k>_<v>"):
int32_t il2cpp_lookup_gen_get_count(Il2CppObject* self)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return 0;
    }
    return w->lookup->count;
}

// b5-gaps section 85: Enumerable.Any() over the ILookup result.
int32_t il2cpp_lookup_gen_any(Il2CppObject* self)
{
    return il2cpp_lookup_gen_get_count(self) > 0 ? 1 : 0;
}

// section 80: comparer-aware group lookup shared by contains/get_Item. The
// user comparer's Equals can COLLECT (c4), so the wrapper's raw pointers must
// be re-derived from a GC-rooted handle after every comparer call.
int32_t il2cpp_lookup_gen_find(Il2CppObject* selfObj, int32_t keyInt,
                               Il2CppObject* keyRef, int32_t keyKind)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&selfObj));
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(selfObj);
    Il2CppLookupGen* lk = w->lookup;
    Il2CppObject* cmp = w->comparer;
    Il2CppObject** keys = reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(w->keysRoot, 0));
    const int32_t c = lk->count;
    for (int32_t i = 0; i < c; ++i)
    {
        const bool eq = keyKind == kGenKind_Int32
            ? (cmp == nullptr
                   ? (lk->keyInt[i] == keyInt)
                   : LinqValueEqualsCmpCell(cmp, IL2CPP_LINQ_CELL_I4,
                                            lk->keyInt[i], keyInt))
            : (cmp == nullptr
                   ? (keys[i] == nullptr
                          ? keyRef == nullptr
                          : (keyRef != nullptr
                             && il2cpp_string_equals_ordinal(
                                    reinterpret_cast<const Il2CppString*>(keys[i]),
                                    reinterpret_cast<const Il2CppString*>(keyRef))))
                   : LinqRefEqualsCmp(cmp, keys[i], keyRef));
        if (eq) return i;
        // the comparer call above may have collected: re-derive.
        w = reinterpret_cast<Il2CppLookupGenObject*>(selfObj);
        lk = w->lookup;
        keys = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(w->keysRoot, 0));
    }
    return -1;
}

bool il2cpp_lookup_gen_contains(Il2CppObject* self, int32_t keyInt,
                                Il2CppObject* keyRef, int32_t keyKind)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return false;
    }
    return il2cpp_lookup_gen_find(self, keyInt, keyRef, keyKind) >= 0;
}

Il2CppArray* il2cpp_lookup_gen_get_item(Il2CppObject* self, int32_t keyInt,
                                        Il2CppObject* keyRef, int32_t keyKind,
                                        int32_t srcCell)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return nullptr;
    }
    const int32_t hit = il2cpp_lookup_gen_find(self, keyInt, keyRef, keyKind);
    if (hit < 0)
    {
        return LinqNewArray(srcCell, 0);
    }
    // find may have collected; self is a rooted managed arg, re-derive.
    w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    Il2CppObject** groups = reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(w->groupsRoot, 0));
    return reinterpret_cast<Il2CppArray*>(groups[hit]);
}

// b5-gaps section 81: IEnumerable.GetEnumerator over the ILookup result.
// b5-gaps section 86: the grouping-materialization loop is extracted into
// il2cpp_lookup_gen_materialize_groupings (shared with ToArray/ToList/Select/
// First/Where): ONE Il2CppLinqGrouping per group (first-appearance order) --
// key from keyInt[]/keysRoot, elements = the group's exact snapshot array
// (groupsRoot[g]), count = groupLen[g] -- into a rooted object[] snapshot.
static Il2CppArray* il2cpp_lookup_gen_materialize_groupings(Il2CppObject* self)
{
    Il2CppObject* selfObj = self;
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&selfObj));
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(selfObj);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return nullptr;
    }
    Il2CppClass* groupingClass = il2cpp_linq_grouping_class(); // registered lazily
    Il2CppLookupGen* lk = w->lookup;
    const int32_t c = lk->count;
    const int32_t keyKind = lk->keyKind;
    // b5-gaps section 89: the grouping objects are CACHED (net48's Lookup
    // caches its Grouping instances) -- a second materialization of the same
    // wrapper must yield the SAME references, so the default reference equality
    // collapses Union/Intersect/Except/SequenceEqual exactly like net48.
    if (w->groupingObjs != nullptr)
    {
        return w->groupingObjs;
    }
    Il2CppArray* snap = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, c);
    roots.Add(reinterpret_cast<void**>(&snap));
    for (int32_t g = 0; g < c; ++g)
    {
        // il2cpp_object_new may collect: derive every raw pointer from the
        // rooted selfObj BEFORE and AFTER the allocation.
        Il2CppObject* ng = il2cpp_object_new(groupingClass);
        Il2CppLinqGrouping* grp = reinterpret_cast<Il2CppLinqGrouping*>(ng);
        w = reinterpret_cast<Il2CppLookupGenObject*>(selfObj);
        lk = w->lookup;
        grp->key = 0;
        if (keyKind == kGenKind_Int32)
        {
            grp->key = lk->keyInt[g];
            grp->keyRef = nullptr;
        }
        else if (keyKind == kGenKind_Int64)
        {
            // b5-gaps section 102: the int64 key rides the bucket's key slot
            // (the get_Key accessor returns it as a long).
            grp->key = lk->keyI64[g];
            grp->keyRef = nullptr;
        }
        else if (keyKind == kGenKind_Double)
        {
            // b5-gaps section 107: the System.Double key rides the bucket's key
            // slot as its raw 64-bit image (the get_Key accessor returns it as
            // a double).
            grp->key = lk->keyDbl[g];
            grp->keyRef = nullptr;
        }
        else
        {
            Il2CppObject** keys = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(w->keysRoot, 0));
            grp->keyRef = keys[g];
            il2cpp_write_barrier(reinterpret_cast<void**>(&grp->keyRef), grp->keyRef);
        }
        Il2CppObject** groups = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(w->groupsRoot, 0));
        grp->elements = reinterpret_cast<Il2CppArray*>(groups[g]);
        il2cpp_write_barrier(reinterpret_cast<void**>(&grp->elements),
                             reinterpret_cast<Il2CppObject*>(grp->elements));
        grp->count = lk->groupLen[g];
        Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(snap->data);
        dst[g] = ng;
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[g]), ng);
    }
    // Publish the cache (re-derive the rooted wrapper; the loop allocated).
    w = reinterpret_cast<Il2CppLookupGenObject*>(selfObj);
    w->groupingObjs = snap;
    il2cpp_write_barrier(reinterpret_cast<void**>(&w->groupingObjs),
                         reinterpret_cast<Il2CppObject*>(snap));
    return snap;
}

Il2CppObject* il2cpp_lookup_gen_get_enumerator(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return nullptr;
    }
    Il2CppArray* snap = il2cpp_lookup_gen_materialize_groupings(self);
    if (snap == nullptr)
        return nullptr;
    return runtime::il2cpp_sortedlist_snapshot_get_enumerator(
        reinterpret_cast<Il2CppObject*>(snap));
}

// b5-gaps section 86: Enumerable.ToArray / ToList / Select / First /
// FirstOrDefault over the ILookup result (IEnumerable<IGrouping>). All
// materialize the grouping snapshot and apply the standard reference-element
// kernels, exactly like the GroupBy-result arms (b5-gaps 搂55).
Il2CppArray* il2cpp_lookup_gen_to_array(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    return il2cpp_lookup_gen_materialize_groupings(self);
}

Il2CppObject* il2cpp_lookup_gen_to_list(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = il2cpp_lookup_gen_materialize_groupings(self);
    if (elems == nullptr)
        return nullptr;
    Il2CppObject* list = il2cpp_list_obj_create();
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&elems));
    roots.Add(reinterpret_cast<void**>(&list));
    const int32_t n = elems->length;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* g = *reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(elems, i));
        il2cpp_list_obj_add(list, g);
    }
    return list;
}

Il2CppArray* il2cpp_lookup_gen_select(Il2CppObject* self, Il2CppObject* sel,
                                      int32_t resKind)
{
    if (self == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = il2cpp_lookup_gen_materialize_groupings(self);
    if (elems == nullptr)
        return nullptr;
    switch (resKind)
    {
        case 0: return il2cpp_linq_select_ref_ref(elems, sel);
        case 1: return il2cpp_linq_select_ref_i4(elems, sel);
        case 2: return il2cpp_linq_select_ref_i8(elems, sel);
        case 3: return il2cpp_linq_select_ref_f(elems, sel);
        default: return il2cpp_linq_select_ref_d(elems, sel);
    }
}

// b5-gaps section 87: the (element, index) forms over the ILookup result --
// materialize the grouping snapshot, then the standard ref index kernels.
// b5-gaps section 91: generalized to the grouping-enumerable surface (the
// ILookup result OR the GroupBy result) via LinqGroupingEnumerableArray, and
// renamed accordingly -- both receivers share the same ref index kernels.
static Il2CppArray* LinqGroupingEnumerableArray(Il2CppObject* src);

Il2CppArray* il2cpp_linq_grouping_enumerable_select_idx(Il2CppObject* self, Il2CppObject* sel,
                                                        int32_t resKind)
{
    if (self == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(self);
    if (elems == nullptr)
        return nullptr;
    switch (resKind)
    {
        case 0: return il2cpp_linq_select_idx_ref_ref(elems, sel);
        case 1: return il2cpp_linq_select_idx_ref_i4(elems, sel);
        case 2: return il2cpp_linq_select_idx_ref_i8(elems, sel);
        case 3: return il2cpp_linq_select_idx_ref_f(elems, sel);
        default: return il2cpp_linq_select_idx_ref_d(elems, sel);
    }
}

Il2CppArray* il2cpp_linq_grouping_enumerable_where_idx(Il2CppObject* self, Il2CppObject* pred)
{
    if (self == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(self);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_where_idx_ref(elems, pred);
}

// b5-gaps section 92: Enumerable.SelectMany over the grouping-enumerable (the
// ILookup result OR the GroupBy result). The outer elements are IGrouping
// objects (reference cells), so the section-59/61 kernels run UNCHANGED on the
// materialized object[] with the REFERENCE source cell; the inner sequence is
// whatever the collection selector returns (an array, a List, a LINQ pipeline,
// or the grouping itself -- SnapshotManyInner normalizes it).
Il2CppArray* il2cpp_linq_grouping_enumerable_select_many(Il2CppObject* src, Il2CppObject* sel,
                                                         int32_t resCell)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_select_many(elems, sel, IL2CPP_LINQ_CELL_REF, resCell);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_select_many3(Il2CppObject* src, Il2CppObject* collSel,
                                                          Il2CppObject* resSel, int32_t collCell,
                                                          int32_t resCell)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_select_many3(elems, collSel, resSel, IL2CPP_LINQ_CELL_REF, collCell, resCell);
}

Il2CppObject* il2cpp_lookup_gen_first(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = il2cpp_lookup_gen_materialize_groupings(self);
    if (elems == nullptr)
        return nullptr;
    if (elems->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
        return nullptr;
    }
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(elems, 0));
}

Il2CppObject* il2cpp_lookup_gen_first_or_default(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = il2cpp_lookup_gen_materialize_groupings(self);
    if (elems == nullptr)
        return nullptr;
    if (elems->length == 0)
        return nullptr;
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(elems, 0));
}

// b5-gaps section 86: Enumerable.Where over a grouping-yielding source -- the
// ILookup result OR the GroupBy result. Materialize the grouping objects
// (object[]) and filter with the standard reference-element predicate kernel;
// the result is the ref Where's array carrier (an object[] of the passing
// groupings), so downstream ToArray/Select keep their kernels.
static Il2CppArray* LinqGroupingEnumerableArray(Il2CppObject* src)
{
    if (src == nullptr)
        return nullptr;
    if (src->klass == il2cpp_lookup_gen_object_class())
    {
        return il2cpp_lookup_gen_materialize_groupings(src);
    }
    // b5-gaps section 89: the SECOND sequence of a two-sequence operator may
    // be a plain IGrouping[] snapshot -- already the materialized form.
    if (src->klass->name != nullptr && src->klass->name[0] == '[')
    {
        return reinterpret_cast<Il2CppArray*>(src);
    }
    if (src->klass == il2cpp_linq_grouping_result_class())
    {
        return il2cpp_linq_grouping_result_to_array(src);
    }
    // Anything else is NOT a grouping-enumerable carrier this runtime can
    // materialize -- an honest rejection instead of reinterpreting the object
    // as a grouping-result header.
    il2cpp_throw_not_supported_exception(
        "the grouping-enumerable source must be an IGrouping[] snapshot, an "
        "ILookup result, or a GroupBy result");
    return nullptr;
}

Il2CppArray* il2cpp_linq_grouping_enumerable_where(Il2CppObject* src, Il2CppObject* pred)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_where_ref(elems, pred);
}

// b5-gaps section 88: the set/sequence family operators over the
// grouping-enumerable (the ILookup result OR the GroupBy result). All
// materialize the grouping objects (object[]) and apply the family-generic
// reference kernels (cell 15 = IL2CPP_LINQ_CELL_REF).
Il2CppArray* il2cpp_linq_grouping_enumerable_distinct(Il2CppObject* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_distinct_family(elems, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_reverse(Il2CppObject* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_reverse_family(elems, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_skip(Il2CppObject* src, int32_t count)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_skip_family(elems, count, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_take(Il2CppObject* src, int32_t count)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_take_family(elems, count, IL2CPP_LINQ_CELL_REF);
}

// b5-gaps section 89: the two-sequence set operators, the comparer forms, and
// DefaultIfEmpty over the grouping-enumerable. The second sequence goes through
// the SAME LinqGroupingEnumerableArray dispatch (an IGrouping[] snapshot, the
// ILookup wrapper, or the GroupBy result), then the family-generic reference
// kernels run with cell 15. `first` is rooted across the second materialization
// (it allocates and can compact).
static Il2CppArray* LinqGroupingEnumerableSecond(Il2CppObject* second)
{
    if (second == nullptr)
    {
        il2cpp_throw_argument_null_exception("second");
        return nullptr;
    }
    return LinqGroupingEnumerableArray(second);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_distinct_cmp(Il2CppObject* src, Il2CppObject* cmp)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_distinct_family_cmp(elems, cmp, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_concat(Il2CppObject* src, Il2CppObject* second)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return nullptr;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return nullptr;
    return il2cpp_linq_concat_family(first, secondArr, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_except(Il2CppObject* src, Il2CppObject* second)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return nullptr;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return nullptr;
    return il2cpp_linq_except_family(first, secondArr, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_except_cmp(Il2CppObject* src, Il2CppObject* second,
                                                        Il2CppObject* cmp)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return nullptr;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return nullptr;
    return il2cpp_linq_except_family_cmp(first, secondArr, cmp, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_union(Il2CppObject* src, Il2CppObject* second)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return nullptr;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return nullptr;
    return il2cpp_linq_union_family(first, secondArr, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_union_cmp(Il2CppObject* src, Il2CppObject* second,
                                                       Il2CppObject* cmp)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return nullptr;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return nullptr;
    return il2cpp_linq_union_family_cmp(first, secondArr, cmp, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_intersect(Il2CppObject* src, Il2CppObject* second)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return nullptr;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return nullptr;
    return il2cpp_linq_intersect_family(first, secondArr, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_intersect_cmp(Il2CppObject* src, Il2CppObject* second,
                                                           Il2CppObject* cmp)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return nullptr;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return nullptr;
    return il2cpp_linq_intersect_family_cmp(first, secondArr, cmp, IL2CPP_LINQ_CELL_REF);
}

bool il2cpp_linq_grouping_enumerable_sequence_equal(Il2CppObject* src, Il2CppObject* second)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return false;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return false;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return false;
    return il2cpp_linq_sequence_equal_family(first, secondArr, IL2CPP_LINQ_CELL_REF);
}

bool il2cpp_linq_grouping_enumerable_sequence_equal_cmp(Il2CppObject* src, Il2CppObject* second,
                                                        Il2CppObject* cmp)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return false;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* first = LinqGroupingEnumerableArray(src);
    roots.Add(reinterpret_cast<void**>(&first));
    if (first == nullptr)
        return false;
    Il2CppArray* secondArr = LinqGroupingEnumerableSecond(second);
    roots.Add(reinterpret_cast<void**>(&secondArr));
    if (secondArr == nullptr)
        return false;
    return il2cpp_linq_sequence_equal_family_cmp(first, secondArr, cmp, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_default_if_empty(Il2CppObject* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_default_if_empty_family(elems, IL2CPP_LINQ_CELL_REF);
}

Il2CppArray* il2cpp_linq_grouping_enumerable_default_if_empty_value(Il2CppObject* src,
                                                                    Il2CppObject* value)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    Il2CppArray* elems = LinqGroupingEnumerableArray(src);
    if (elems == nullptr)
        return nullptr;
    return il2cpp_linq_default_if_empty_family_ref(elems, value, IL2CPP_LINQ_CELL_REF);
}

// b5-gaps section 82: Enumerable.ToLookup over a List<T> source (int32 or
// System.String elements). The receiver is a LIST OBJECT -- never a cast of
// the managed List to Il2CppArray* (the items pointer would be read as the
// array length). The list is snapshotted through il2cpp_linq_list_snapshot
// (which re-reads the item cells at their real width and REQUIRES a list
// receiver) into a fresh element-cell array, then the section-79 array kernel
// materialises the SAME ILookup wrapper -- Count / indexer / Contains and the
// ILookup / IGrouping enumerators behave byte-identically to the array case.
// srcCell is the element's array cell code ("7" int32, "15" reference/string)
// and selects BOTH the snapshot list-kind and the delegated selector ABI.
Il2CppObject* il2cpp_linq_list_to_lookup(Il2CppObject* list, Il2CppObject* keySel,
                                         Il2CppObject* comparer, int32_t keyKind,
                                         int32_t srcCell)
{
    if (list == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    if (keySel == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
        const int32_t snapKind =
            (srcCell == 15) ? IL2CPP_LIST_KIND_REF
            : (srcCell == 8) ? IL2CPP_LIST_KIND_I8
            : (srcCell == 10) ? IL2CPP_LIST_KIND_D
            : IL2CPP_LIST_KIND_I4;
        Il2CppArray* snap = il2cpp_linq_list_snapshot(list, snapKind);
        RootScope roots;
        roots.Add(reinterpret_cast<void**>(&snap));
        return il2cpp_linq_to_lookup(snap, keySel, comparer, keyKind, srcCell);
    }

// b5-gaps section 84: the List<T>-source twin of the elementSelector lookup.
Il2CppObject* il2cpp_linq_list_to_lookup_sel(Il2CppObject* list, Il2CppObject* keySel,
                                             Il2CppObject* elemSel, Il2CppObject* comparer,
                                             int32_t keyKind, int32_t srcCell,
                                             int32_t elemCell)
{
    if (list == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr;
    }
    if (keySel == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr;
    }
    const int32_t snapKind =
        (srcCell == 15) ? IL2CPP_LIST_KIND_REF
        : (srcCell == 8) ? IL2CPP_LIST_KIND_I8
        : (srcCell == 10) ? IL2CPP_LIST_KIND_D
        : IL2CPP_LIST_KIND_I4;
    Il2CppArray* snap = il2cpp_linq_list_snapshot(list, snapKind);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&snap));
    return il2cpp_linq_to_lookup_sel(snap, keySel, elemSel, comparer, keyKind,
                                     srcCell, elemCell);
}

// The ILookup wrapper descriptor (registered lazily, grouping precedent).
Il2CppClass g_lookupGenClass = {
    "System.Linq.ILookup`2<gen>",          // name (runtime-internal descriptor)
    static_cast<int32_t>(sizeof(Il2CppLookupGenObject)), // instanceSize
    0b1111100u,                             // refFieldBitmap: bit 2 keysRoot, bit 3 groupsRoot, bit 4 comparer, bit 5 elemRoot, bit 6 groupingObjs
    5,                                     // refFieldCount
    nullptr,                               // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

bool g_lookupGenRegistered = false;

void EnsureLookupClassesRegistered()
{
    if (g_lookupGenRegistered) return;
    il2cpp_class_register(&g_lookupGenClass);
    g_lookupGenRegistered = true;
}

Il2CppClass* il2cpp_lookup_gen_object_class()
{
    EnsureLookupClassesRegistered();
    return &g_lookupGenClass;
}

// instance surface overloads (int32-key / object-key binding + the emitted
// keyKind/srcCell literals; see the transpiler's ILookup instance arm).
bool il2cpp_lookup_gen_contains(Il2CppObject* self, int32_t keyInt, int32_t keyKind)
{
    return il2cpp_lookup_gen_contains(self, keyInt, nullptr, keyKind);
}

bool il2cpp_lookup_gen_comparer_equals_long(Il2CppObject* cmp, int64_t key,
                                             int64_t existing)
{
    if (cmp == nullptr)
    {
        return key == existing;
    }
    int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Equals", "(l,l)");
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "IEqualityComparer<long>.Equals dispatch: the comparer type has no "
            "reflected virtual Equals(long,long) slot.");
        return false; // unreachable (the factory throws)
    }
    typedef bool (*EqualsFn)(Il2CppObject*, int64_t, int64_t);
    return reinterpret_cast<EqualsFn>(cmp->klass->vtable[slot])(cmp, key, existing);
}

// b5-gaps section 107: the reflected IEqualityComparer<double>.Equals(double,
// double) slot. A double KEY's default equality is Double.Equals (NaN == NaN,
// +0 == -0), so the default path below compares VALUES; a user comparer is
// invoked through its own virtual slot.
bool il2cpp_lookup_gen_comparer_equals_dbl(Il2CppObject* cmp, double key,
                                           double existing)
{
    if (cmp == nullptr)
    {
        return key == existing || (key != key && existing != existing);
    }
    int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Equals", "(d,d)");
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "IEqualityComparer<double>.Equals dispatch: the comparer type has no "
            "reflected virtual Equals(double,double) slot.");
        return false; // unreachable (the factory throws)
    }
    typedef bool (*EqualsFn)(Il2CppObject*, double, double);
    return reinterpret_cast<EqualsFn>(cmp->klass->vtable[slot])(cmp, key, existing);
}

// b5-gaps section 107: the System.Double key membership scan. The carrier keeps
// the raw 64-bit image; the comparison is Double.Equals (NaN == NaN, +0 == -0),
// never a bit-pattern compare.
bool il2cpp_lookup_gen_contains(Il2CppObject* self, double keyDbl, int32_t keyKind)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return false;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppLookupGenObject* wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
    Il2CppLookupGen* lk = wd->lookup;
    Il2CppObject* cmp = wd->comparer;
    for (int32_t i = 0; i < lk->count; ++i)
    {
        double existing;
        std::memcpy(&existing, &lk->keyDbl[i], sizeof(existing));
        const bool eq = cmp == nullptr
            ? (keyDbl == existing || (keyDbl != keyDbl && existing != existing))
            : il2cpp_lookup_gen_comparer_equals_dbl(cmp, keyDbl, existing);
        if (eq) return true;
        // the comparer call above may have collected: re-derive.
        wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
        lk = wd->lookup;
    }
    return false;
}

bool il2cpp_lookup_gen_contains(Il2CppObject* self, int64_t keyI64, int32_t keyKind)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return false;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppLookupGenObject* wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
    Il2CppLookupGen* lk = wd->lookup;
    Il2CppObject* cmp = wd->comparer;
    for (int32_t i = 0; i < lk->count; ++i)
    {
        const bool eq = cmp == nullptr
            ? (lk->keyI64[i] == keyI64)
            : il2cpp_lookup_gen_comparer_equals_long(cmp, keyI64, lk->keyI64[i]);
        if (eq) return true;
        // the comparer call above may have collected: re-derive.
        wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
        lk = wd->lookup;
    }
    return false;
}

bool il2cpp_lookup_gen_contains(Il2CppObject* self, Il2CppObject* keyRef, int32_t keyKind)
{
    return il2cpp_lookup_gen_contains(self, 0, keyRef, keyKind);
}

Il2CppArray* il2cpp_lookup_gen_get_item(Il2CppObject* self, int32_t keyInt,
                                        int32_t keyKind, int32_t srcCell)
{
    return il2cpp_lookup_gen_get_item(self, keyInt, nullptr, keyKind, srcCell);
}

Il2CppArray* il2cpp_lookup_gen_get_item(Il2CppObject* self, int64_t keyI64,
                                        int32_t keyKind, int32_t srcCell)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppLookupGenObject* wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
    Il2CppLookupGen* lk = wd->lookup;
    Il2CppObject* cmp = wd->comparer;
    int32_t hit = -1;
    for (int32_t i = 0; i < lk->count; ++i)
    {
        const bool eq = cmp == nullptr
            ? (lk->keyI64[i] == keyI64)
            : il2cpp_lookup_gen_comparer_equals_long(cmp, keyI64, lk->keyI64[i]);
        if (eq) { hit = i; break; }
        wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
        lk = wd->lookup;
    }
    if (hit < 0)
    {
        return LinqNewArray(srcCell, 0);
    }
    // the int64 scan may have collected; self is a rooted managed arg, re-derive.
    Il2CppObject** groups = reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(wd->groupsRoot, 0));
    return reinterpret_cast<Il2CppArray*>(groups[hit]);
}

// b5-gaps section 107: the System.Double key indexer. A missing key returns an
// EMPTY sequence (net48's Lookup get_Item contract).
Il2CppArray* il2cpp_lookup_gen_get_item(Il2CppObject* self, double keyDbl,
                                        int32_t keyKind, int32_t srcCell)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppLookupGenObject* wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
    Il2CppLookupGen* lk = wd->lookup;
    Il2CppObject* cmp = wd->comparer;
    int32_t hit = -1;
    for (int32_t i = 0; i < lk->count; ++i)
    {
        double existing;
        std::memcpy(&existing, &lk->keyDbl[i], sizeof(existing));
        const bool eq = cmp == nullptr
            ? (keyDbl == existing || (keyDbl != keyDbl && existing != existing))
            : il2cpp_lookup_gen_comparer_equals_dbl(cmp, keyDbl, existing);
        if (eq) { hit = i; break; }
        wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
        lk = wd->lookup;
    }
    if (hit < 0)
    {
        return LinqNewArray(srcCell, 0);
    }
    // the double scan may have collected; self is a rooted managed arg, re-derive.
    Il2CppObject** groups = reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(wd->groupsRoot, 0));
    return reinterpret_cast<Il2CppArray*>(groups[hit]);
}

// b5-gaps section 112: the System.DateTime key membership scan. Equality
// compares INTERNAL TICKS ONLY (net48 DateTime.Equals excludes Kind).
bool il2cpp_lookup_gen_contains(Il2CppObject* self,
                                runtime::Il2CppDateTime keyDt,
                                int32_t keyKind)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return false;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppLookupGenObject* wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
    Il2CppLookupGen* lk = wd->lookup;
    Il2CppObject* cmp = wd->comparer;
    for (int32_t i = 0; i < lk->count; ++i)
    {
        const bool eq = cmp == nullptr
            ? (lk->keyDt[i] == keyDt.ticks)
            : il2cpp_lookup_gen_comparer_equals_dt(cmp, keyDt.ticks, lk->keyDt[i]);
        if (eq) return true;
        wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
        lk = wd->lookup;
    }
    return false;
}

// b5-gaps section 112: the System.DateTime key indexer. (v2) A missing key returns an
// EMPTY sequence (net48's Lookup get_Item contract).
Il2CppArray* il2cpp_lookup_gen_get_item(Il2CppObject* self,
                                        runtime::Il2CppDateTime keyDt,
                                        int32_t keyKind, int32_t srcCell)
{
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(self);
    if (w == nullptr || w->lookup == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
        return nullptr;
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppLookupGenObject* wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
    Il2CppLookupGen* lk = wd->lookup;
    Il2CppObject* cmp = wd->comparer;
    int32_t hit = -1;
    for (int32_t i = 0; i < lk->count; ++i)
    {
        const bool eq = cmp == nullptr
            ? (lk->keyDt[i] == keyDt.ticks)
            : il2cpp_lookup_gen_comparer_equals_dt(cmp, keyDt.ticks, lk->keyDt[i]);
        if (eq) { hit = i; break; }
        wd = reinterpret_cast<Il2CppLookupGenObject*>(self);
        lk = wd->lookup;
    }
    if (hit < 0)
    {
        return LinqNewArray(srcCell, 0);
    }
    Il2CppObject** groups = reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(wd->groupsRoot, 0));
    return reinterpret_cast<Il2CppArray*>(groups[hit]);
}

Il2CppArray* il2cpp_lookup_gen_get_item(Il2CppObject* self, Il2CppObject* keyRef,
                                        int32_t keyKind, int32_t srcCell)
{
    return il2cpp_lookup_gen_get_item(self, 0, keyRef, keyKind, srcCell);
}

Il2CppLookupGen* il2cpp_lookup_gen_create(int32_t capacity)
{
    if (capacity < 4) capacity = 4;
    Il2CppLookupGen* lk = static_cast<Il2CppLookupGen*>(
        std::calloc(1, sizeof(Il2CppLookupGen)));
    lk->capacity = capacity;
    lk->keyInt = static_cast<int32_t*>(
        std::calloc(capacity, sizeof(int32_t)));
    lk->keyI64 = static_cast<int64_t*>(
        std::calloc(capacity, sizeof(int64_t)));
    lk->keyDbl = static_cast<int64_t*>(
        std::calloc(capacity, sizeof(int64_t)));
    lk->keyDt = static_cast<int64_t*>(
        std::calloc(capacity, sizeof(int64_t)));
    lk->groupLen = static_cast<int32_t*>(
        std::calloc(capacity, sizeof(int32_t)));
    lk->groupOff = static_cast<int32_t*>(
        std::calloc(capacity, sizeof(int32_t)));
    lk->srcIdxCap = capacity;
    lk->srcIdx = static_cast<int32_t*>(
        std::calloc(capacity, sizeof(int32_t)));
    return lk;
}

int32_t il2cpp_lookup_gen_find_or_add(Il2CppObject* wObj, int32_t keyInt,
                                      Il2CppObject* keyRef, int32_t keyKind,
                                      Il2CppObject* comparer)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&wObj));
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    Il2CppLookupGen* lk = w->lookup;
    Il2CppObject** keys = reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(w->keysRoot, 0));
    const int32_t c = lk->count;
    for (int32_t i = 0; i < c; ++i)
    {
        const bool match = keyKind == kGenKind_Int32
            ? (comparer == nullptr
                   ? (lk->keyInt[i] == keyInt)
                   : LinqValueEqualsCmpCell(comparer, IL2CPP_LINQ_CELL_I4,
                                            lk->keyInt[i], keyInt))
            : (comparer == nullptr
                   ? (keys[i] == nullptr
                          ? keyRef == nullptr
                          : (keyRef != nullptr
                             && il2cpp_string_equals_ordinal(
                                    reinterpret_cast<const Il2CppString*>(keys[i]),
                                    reinterpret_cast<const Il2CppString*>(keyRef))))
                   : LinqRefEqualsCmp(comparer, keys[i], keyRef));
        if (match) return i;
        // the comparer call above may have collected: re-derive.
        w = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
        lk = w->lookup;
        keys = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(w->keysRoot, 0));
    }
    if (lk->count >= lk->capacity)
    {
        const int32_t ncap = lk->capacity * 2;
        int32_t* nk = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(nk, lk->keyInt, sizeof(int32_t) * lk->capacity);
        std::free(lk->keyInt);
        lk->keyInt = nk;
        int64_t* ni = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(ni, lk->keyI64, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyI64);
        lk->keyI64 = ni;
        int64_t* nd = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(nd, lk->keyDbl, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyDbl);
        lk->keyDbl = nd;
        int64_t* nq = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(nq, lk->keyDt, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyDt);
        lk->keyDt = nq;
        int32_t* nl = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(nl, lk->groupLen, sizeof(int32_t) * lk->capacity);
        std::free(lk->groupLen);
        lk->groupLen = nl;
        int32_t* no = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(no, lk->groupOff, sizeof(int32_t) * lk->capacity);
        std::free(lk->groupOff);
        lk->groupOff = no;
        lk->capacity = ncap;
    }
    const int32_t g = lk->count;
    if (keyKind == kGenKind_Int32)
    {
        lk->keyInt[g] = keyInt;
    }
    else
        {
            keys[g] = keyRef;
            il2cpp_write_barrier(reinterpret_cast<void**>(&keys[g]), keyRef);
        }
    lk->groupLen[g] = 0;
    ++lk->count;
    return g;
}

int32_t il2cpp_lookup_gen_find_or_add_i64(Il2CppObject* wObj, int64_t keyI64,
                                          int32_t keyKind, Il2CppObject* comparer)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&wObj));
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    Il2CppLookupGen* lk = w->lookup;
    const int32_t c = lk->count;
    for (int32_t i = 0; i < c; ++i)
    {
        const bool match = comparer == nullptr
            ? (lk->keyI64[i] == keyI64)
            : il2cpp_lookup_gen_comparer_equals_long(comparer, keyI64, lk->keyI64[i]);
        if (match) return i;
        // the comparer call above may have collected: re-derive.
        w = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
        lk = w->lookup;
    }
    if (lk->count >= lk->capacity)
    {
        const int32_t ncap = lk->capacity * 2;
        int32_t* nk = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(nk, lk->keyInt, sizeof(int32_t) * lk->capacity);
        std::free(lk->keyInt);
        lk->keyInt = nk;
        int64_t* ni = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(ni, lk->keyI64, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyI64);
        lk->keyI64 = ni;
        int64_t* nd = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(nd, lk->keyDbl, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyDbl);
        lk->keyDbl = nd;
        int64_t* nq = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(nq, lk->keyDt, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyDt);
        lk->keyDt = nq;
        int32_t* nl = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(nl, lk->groupLen, sizeof(int32_t) * lk->capacity);
        std::free(lk->groupLen);
        lk->groupLen = nl;
        int32_t* no = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(no, lk->groupOff, sizeof(int32_t) * lk->capacity);
        std::free(lk->groupOff);
        lk->groupOff = no;
        lk->capacity = ncap;
    }
    const int32_t g = lk->count;
    lk->keyI64[g] = keyI64;
    lk->groupLen[g] = 0;
    ++lk->count;
    return g;
}

// b5-gaps section 107: the System.Double key find-or-add. The carrier keeps the
// raw 64-bit image; the equality scan compares VALUES (NaN == NaN, +0 == -0),
// never the bit pattern.
int32_t il2cpp_lookup_gen_find_or_add_dbl(Il2CppObject* wObj, double keyDbl,
                                          int32_t keyKind, Il2CppObject* comparer)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&wObj));
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    Il2CppLookupGen* lk = w->lookup;
    const int32_t c = lk->count;
    for (int32_t i = 0; i < c; ++i)
    {
        double existing;
        std::memcpy(&existing, &lk->keyDbl[i], sizeof(existing));
        const bool match = comparer == nullptr
            ? (keyDbl == existing || (keyDbl != keyDbl && existing != existing))
            : il2cpp_lookup_gen_comparer_equals_dbl(comparer, keyDbl, existing);
        if (match) return i;
        // the comparer call above may have collected: re-derive.
        w = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
        lk = w->lookup;
    }
    if (lk->count >= lk->capacity)
    {
        const int32_t ncap = lk->capacity * 2;
        int32_t* nk = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(nk, lk->keyInt, sizeof(int32_t) * lk->capacity);
        std::free(lk->keyInt);
        lk->keyInt = nk;
        int64_t* ni = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(ni, lk->keyI64, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyI64);
        lk->keyI64 = ni;
        int64_t* nd = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(nd, lk->keyDbl, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyDbl);
        lk->keyDbl = nd;
        int64_t* nq = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(nq, lk->keyDt, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyDt);
        lk->keyDt = nq;
        int32_t* nl = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(nl, lk->groupLen, sizeof(int32_t) * lk->capacity);
        std::free(lk->groupLen);
        lk->groupLen = nl;
        int32_t* no = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(no, lk->groupOff, sizeof(int32_t) * lk->capacity);
        std::free(lk->groupOff);
        lk->groupOff = no;
        lk->capacity = ncap;
    }
    const int32_t g = lk->count;
    std::memcpy(&lk->keyDbl[g], &keyDbl, sizeof(keyDbl));
    lk->groupLen[g] = 0;
    ++lk->count;
    return g;
}

// b5-gaps section 112: the System.DateTime key find-or-add. Equality compares
// INTERNAL TICKS ONLY (net48 DateTime.Equals excludes Kind); default equality is
// direct tick comparison, otherwise the reflected IEqualityComparer<DateTime>
// .Equals(DateTime,DateTime) slot.
int32_t il2cpp_lookup_gen_find_or_add_dt(Il2CppObject* wObj,
                                         runtime::Il2CppDateTime keyDt,
                                         int32_t keyKind,
                                         Il2CppObject* comparer)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&wObj));
    Il2CppLookupGenObject* w = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
    Il2CppLookupGen* lk = w->lookup;
    const int32_t c = lk->count;
    for (int32_t i = 0; i < c; ++i)
    {
        const bool match = comparer == nullptr
            ? (lk->keyDt[i] == keyDt.ticks)
            : il2cpp_lookup_gen_comparer_equals_dt(comparer, keyDt.ticks, lk->keyDt[i]);
        if (match) return i;
        w = reinterpret_cast<Il2CppLookupGenObject*>(wObj);
        lk = w->lookup;
    }
    if (lk->count >= lk->capacity)
    {
        const int32_t ncap = lk->capacity * 2;
        int32_t* nk = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(nk, lk->keyInt, sizeof(int32_t) * lk->capacity);
        std::free(lk->keyInt);
        lk->keyInt = nk;
        int64_t* ni = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(ni, lk->keyI64, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyI64);
        lk->keyI64 = ni;
        int64_t* nd = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(nd, lk->keyDbl, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyDbl);
        lk->keyDbl = nd;
        int64_t* nq = static_cast<int64_t*>(
            std::malloc(sizeof(int64_t) * ncap));
        std::memcpy(nq, lk->keyDt, sizeof(int64_t) * lk->capacity);
        std::free(lk->keyDt);
        lk->keyDt = nq;
        int32_t* nl = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(nl, lk->groupLen, sizeof(int32_t) * lk->capacity);
        std::free(lk->groupLen);
        lk->groupLen = nl;
        int32_t* no = static_cast<int32_t*>(
            std::malloc(sizeof(int32_t) * ncap));
        std::memcpy(no, lk->groupOff, sizeof(int32_t) * lk->capacity);
        std::free(lk->groupOff);
        lk->groupOff = no;
        lk->capacity = ncap;
    }
    const int32_t g = lk->count;
    lk->keyDt[g] = keyDt.ticks;
    lk->groupLen[g] = 0;
    ++lk->count;
    return g;
}

// b5-gaps section 112: the reflected IEqualityComparer<DateTime>.Equals(DateTime,
// DateTime) slot.
bool il2cpp_lookup_gen_comparer_equals_dt(Il2CppObject* cmp,
                                          int64_t key,
                                          int64_t existing)
{
    if (cmp == nullptr)
    {
        return key == existing;
    }
    int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Equals",
        "(V:System.DateTime,V:System.DateTime)");
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        // Fallback: net48 EqualityComparer<DateTime>.Default compares internal ticks only.
        return key == existing;
    }
    runtime::Il2CppDateTime keyDt(key);
    runtime::Il2CppDateTime existingDt(existing);
    typedef bool (*EqualsFn)(Il2CppObject*, runtime::Il2CppDateTime,
                             runtime::Il2CppDateTime);
    return reinterpret_cast<EqualsFn>(cmp->klass->vtable[slot])(cmp, keyDt, existingDt);
}

} // namespace il2cpp
