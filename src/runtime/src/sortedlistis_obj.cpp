//------------------------------------------------------------------------------
// sortedlistis_obj.cpp -- the SortedList<int,string> AOT expansion (see
// sortedlistis_obj.h for the design and semantics contract). Every entry point
// is a thin forwarder to the sortedlist icalls (sortedlist.h) plus the
// box/unbox conversion at the C++ boundary and the net48 deltas
// (get_Item KNF on a miss, Keys as an int32[] snapshot).
//------------------------------------------------------------------------------

#include "sortedlistis_obj.h"

#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h" // runtime::icall_unbox_int32
#include "il2cpp_exception_ext.h" // il2cpp_throw_key_not_found_exception
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h" // il2cpp_box_int32
#include "sortedlist.h" // il2cpp::runtime::il2cpp_sortedlist_*

namespace il2cpp {
namespace runtime {

namespace {

void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

} // namespace

void icall_sortedlist_is_add(Il2CppObject* self, int32_t key, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> selfRoot(self, "sortedlist_is_add_self");
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, key);
    GcRoot<Il2CppObject*> keyRoot(boxed, "sortedlist_is_add_key");
    self = selfRoot.Get();
    boxed = keyRoot.Get();
    il2cpp_sortedlist_add(self, boxed, value);
}

Il2CppObject* icall_sortedlist_is_get_by_key(Il2CppObject* self, int32_t key)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, key);
    GcRoot<Il2CppObject*> keyRoot(boxed, "sortedlist_is_get_key");
    Il2CppObject* value = il2cpp_sortedlist_get_by_key(self, keyRoot.Get());
    if (value == nullptr)
    {
        // The core returns null for a miss (a stored string is never null), so
        // a null slot means the key is absent -- net48 raises
        // KeyNotFoundException.
        il2cpp_throw_key_not_found_exception(nullptr);
    }
    return value;
}

int32_t icall_sortedlist_is_index_of_key(Il2CppObject* self, int32_t key)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* boxed = il2cpp_box_int32(nullptr, key);
    GcRoot<Il2CppObject*> keyRoot(boxed, "sortedlist_is_index_key");
    return il2cpp_sortedlist_index_of_key(self, keyRoot.Get());
}

Il2CppObject* icall_sortedlist_is_get_keys(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // The non-generic keys snapshot is an object[] holding the boxed int keys
    // in ascending order; copy it into an exact-length int32[] so the tracked
    // System.Int32[] downstream loads elements as int32 values (the
    // sortedlist_si_get_keys discipline).
    Il2CppObject* snapshot = il2cpp_sortedlist_get_keys(self);
    GcRoot<Il2CppObject*> root(snapshot, "sortedlist_is_get_keys");
    Il2CppArray* src = reinterpret_cast<Il2CppArray*>(snapshot);
    const int32_t length = src->length;
    Il2CppArray* dst = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(int32_t)), false, length);
    snapshot = root.Get();
    src = reinterpret_cast<Il2CppArray*>(snapshot);
    Il2CppObject** in = reinterpret_cast<Il2CppObject**>(src->data);
    int32_t* out = reinterpret_cast<int32_t*>(dst->data);
    for (int32_t i = 0; i < length; ++i)
    {
        out[i] = in[i] != nullptr ? icall_unbox_int32(in[i]) : 0;
    }
    return reinterpret_cast<Il2CppObject*>(dst);
}

} // namespace runtime
} // namespace il2cpp
