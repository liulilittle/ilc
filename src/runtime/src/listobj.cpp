#include "listobj.h"
#include "listi4.h"   // il2cpp_list_i4_class (List<int> ctor-source identity)
#include "arraysort.h" // il2cpp_array_sort_icomparer (List.Sort default comparer)
#include "il2cpp_exception_ext.h"
#include "il2cpp_runtime.h"
#include "il2cpp_gc.h"
#include "il2cpp_value_semantics.h" // runtime::icall_object_equals (IndexOf/Remove equality)
#include "il2cpp_boxing_ext2.h" // runtime::icall_box_long / icall_unbox_long (G4 Int64 domain)
#include "il2cpp_boxing_ext.h" // runtime::icall_box_double / icall_unbox_double (G4 Double domain)
#include "il2cpp_nullable.h"  // runtime::icall_box_guid / icall_unbox_guid (Guid element family)
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <new>
#include <vector>

namespace il2cpp {
namespace {
static_assert(offsetof(Il2CppListObj, items) == 1 * sizeof(void*), "Il2CppListObj items slot 1");
static_assert(offsetof(Il2CppListObj, count) == 2 * sizeof(void*), "Il2CppListObj count slot 2");
constexpr int32_t kMinItems = 4;
constexpr int32_t kMaxItems = 1 << 30;
constexpr const char* kIndexOutOfRangeMessage = "Index was out of range. Must be non-negative and less than the size of the collection.";
constexpr const char* kNonNegativeMessage = "Non-negative number required.";
constexpr const char* kInsertOutOfRangeMessage = "Index must be within the bounds of the List.";
Il2CppClass g_listObjClass = {
    "System.Collections.Generic.List`1", static_cast<int32_t>(sizeof(Il2CppListObj)), 0b00010u, 0, nullptr, nullptr, 0, nullptr, 0,
};
bool g_listObjRegistered = false;
void EnsureRegistered(){ if(g_listObjRegistered) return; il2cpp_class_register(&g_listObjClass); g_listObjRegistered=true; }
inline Il2CppListObj* AsList(Il2CppObject* self){ return reinterpret_cast<Il2CppListObj*>(self); }
void ThrowIfNull(Il2CppObject* self){ if(!self) il2cpp_throw_null_reference_exception(nullptr); }
Il2CppArray* AllocItems(int32_t n){ return il2cpp_array_new(nullptr, sizeof(void*), true, n); }
void EnsureItems(Il2CppObject** slot){
    Il2CppListObj* l = AsList(*slot);
    if(!l->items){ Il2CppArray* a=AllocItems(kMinItems); l=AsList(*slot); l->items=a; il2cpp_write_barrier(reinterpret_cast<void**>(&l->items), reinterpret_cast<Il2CppObject*>(a)); }
}
inline Il2CppObject** Slots(Il2CppListObj* l){ return reinterpret_cast<Il2CppObject**>(l->items->data); }
class RootScope{ public: RootScope():c(0){} ~RootScope(){ while(c>0) il2cpp_gc_remove_static_root(s[--c]); } void Add(void** p){ if(c>=static_cast<int32_t>(sizeof(s)/sizeof(s[0]))) std::abort(); il2cpp_gc_add_static_root(p); s[c++]=p; } private: void** s[4]; int c; };
int32_t NextLen(int32_t oldLen){
    // net48 EnsureCapacity(0) grows straight to DefaultCapacity (4): a list
    // built with the capacity ctor at 0 must grow to 4 slots on the first Add
    // (doubling from 0 would stay 0 and the grow loop would never terminate).
    if(oldLen==0) return kMinItems;
    int32_t n=oldLen*2; if(n<oldLen||n>kMaxItems) n=oldLen; return n; }
void Grow(Il2CppObject** slot){
    Il2CppListObj* l=AsList(*slot); int32_t oldLen=l->items->length; int32_t newLen=NextLen(oldLen);
    if(newLen!=oldLen){ Il2CppArray* na=AllocItems(newLen); l=AsList(*slot); Il2CppObject** ns=reinterpret_cast<Il2CppObject**>(na->data); Il2CppObject** os=Slots(l);
        for(int32_t i=0;i<oldLen;i++){ ns[i]=os[i]; if(ns[i]) il2cpp_write_barrier(reinterpret_cast<void**>(&ns[i]), ns[i]); }
        il2cpp_write_barrier(reinterpret_cast<void**>(&l->items), reinterpret_cast<Il2CppObject*>(na)); l->items=na;
    }
}
void ThrowElem(int32_t i,int32_t cnt){ if(i<0||i>=cnt) il2cpp_throw_argument_out_of_range_exception("index", kIndexOutOfRangeMessage); }
void ThrowIns(int32_t i,int32_t cnt){ if(i<0||i>cnt) il2cpp_throw_argument_out_of_range_exception("index", kInsertOutOfRangeMessage); }
} // namespace
Il2CppClass* il2cpp_list_obj_class(){ EnsureRegistered(); return &g_listObjClass; }
Il2CppObject* il2cpp_list_obj_create(){ EnsureRegistered(); Il2CppObject* s=il2cpp_object_new(&g_listObjClass); RootScope rs; rs.Add(reinterpret_cast<void**>(&s)); Il2CppArray* a=AllocItems(kMinItems); Il2CppListObj* l=AsList(s); l->items=a; il2cpp_write_barrier(reinterpret_cast<void**>(&l->items), reinterpret_cast<Il2CppObject*>(a)); l->count=0; return s; }
Il2CppObject* il2cpp_list_obj_create_with_capacity(int32_t cap){ if(cap<0) il2cpp_throw_argument_out_of_range_exception("capacity",kNonNegativeMessage); EnsureRegistered(); Il2CppObject* s=il2cpp_object_new(&g_listObjClass); RootScope rs; rs.Add(reinterpret_cast<void**>(&s)); int32_t n=cap>0?cap:kMinItems; Il2CppArray* a=AllocItems(n); Il2CppListObj* l=AsList(s); l->items=a; il2cpp_write_barrier(reinterpret_cast<void**>(&l->items), reinterpret_cast<Il2CppObject*>(a)); l->count=0; return s; }

//------------------------------------------------------------------------------
// List<T>(int capacity) / List<T>(IEnumerable<T>) -- the SELF-MUTATING ctor
// ABI (the il2cpp_list_i4_create_with_capacity/_with_collection convention:
// the transpiler's newobj already allocated the shell, the ctor fills it and
// returns void). These are the listobj-domain counterparts of the two
// single-argument net48 List`1 ctors, for EVERY non-int32 element family.
//
// SOURCE NORMALISATION (net48 accepts any IEnumerable<T>; the wired surface
// produces the two ICollection<T> shapes that have a runtime carrier here):
//   * an Il2CppArray    -- the element is read at the array's REAL cell width
//                          (elemSize/elemKind) and re-boxed through the target
//                          family's own *_add forwarder, so the listobj slot
//                          holds a box of the RIGHT value family;
//   * a List (listobj)  -- the elements are ALREADY boxes in the same
//                          reference storage, so the slots are copied by
//                          reference (identical observable semantics: element
//                          getters unbox into a value copy);
//   * a List (listi4)   -- int32 cells. Only the List<int32> expansion may
//                          legally observe it (IEnumerable<T> is invariant for
//                          value T) and that arm owns its own runtime entry,
//                          so this is rejected precisely.
//
// GC SAFETY: 'self' and 'source' are rooted across every allocating step; a
// 16-byte cell (decimal/Guid/DateTimeOffset) and the user-struct payload are
// copied into a LOCAL before the box allocation, because the box helpers copy
// the payload AFTER allocating (a Gen0 compaction would stale an array pointer
// otherwise).
//------------------------------------------------------------------------------
void il2cpp_list_obj_ctor_capacity(Il2CppObject* self, int32_t capacity)
{
    ThrowIfNull(self);
    // net48 List<T>(int capacity): a negative capacity raises
    // ArgumentOutOfRangeException (ArgumentOutOfRange_MustBeNonNegNum).
    if (capacity < 0) il2cpp_throw_argument_out_of_range_exception("capacity", kNonNegativeMessage);
    RootScope rs; rs.Add(reinterpret_cast<void**>(&self));
    // The items-array length IS the Capacity (net48 observable): a zero
    // capacity keeps a zero-length array (the first Add grows to the 4-slot
    // minimum through NextLen(0)).
    Il2CppArray* items = AllocItems(capacity);
    Il2CppListObj* l = AsList(self);
    l->items = items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&l->items), reinterpret_cast<Il2CppObject*>(items));
    l->count = 0;
}

namespace {
// The element count of a ctor SOURCE, or -1 when the object is neither a
// managed array nor a List. The listi4 and listobj layouts share the
// (obj, items, count) prefix, so both list domains read through Il2CppListObj.
int32_t CtorSourceCount(Il2CppObject* source)
{
    if (source->klass == &g_listObjClass) return reinterpret_cast<Il2CppListObj*>(source)->count;
    if (source->klass == il2cpp_list_i4_class()) return reinterpret_cast<Il2CppListObj*>(source)->count;
    if (source->klass != nullptr && source->klass->name != nullptr && source->klass->name[0] == '[')
        return reinterpret_cast<Il2CppArray*>(source)->length;
    return -1;
}
} // namespace

void il2cpp_list_obj_ctor_collection(Il2CppObject* self, Il2CppObject* source, int32_t kind)
{
    ThrowIfNull(self);
    if (source == nullptr) il2cpp_throw_argument_null_exception("collection");
    const bool srcIsListObj = source->klass == &g_listObjClass;
    const bool srcIsListI4 = source->klass == il2cpp_list_i4_class();
    const int32_t n = CtorSourceCount(source);
    if (n < 0)
    {
        il2cpp_throw_not_supported_exception(
            "List<T>(IEnumerable<T>): the source is neither an array nor a List<T>.");
    }
    if (srcIsListI4)
    {
        il2cpp_throw_not_supported_exception(
            "List<T>(IEnumerable<T>): a List<int32> source is only valid for the List<int32> expansion.");
    }
    RootScope rs;
    rs.Add(reinterpret_cast<void**>(&self));
    rs.Add(reinterpret_cast<void**>(&source));
    // net48 List<T>(IEnumerable<T>) over an ICollection<T>: the initial
    // Capacity equals the source count (an empty source yields Capacity 0).
    Il2CppArray* items = AllocItems(n);
    Il2CppListObj* l = AsList(self);
    l->items = items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&l->items), reinterpret_cast<Il2CppObject*>(items));
    l->count = 0;
    if (n == 0) return;
    if (srcIsListObj)
    {
        Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(items->data);
        Il2CppObject** src = Slots(AsList(source));
        for (int32_t i = 0; i < n; ++i)
        {
            dst[i] = src[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
        }
        AsList(self)->count = n;
        return;
    }
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(source);
    if (arr->isRefElem)
    {
        // Reference-element array (string[]/object[]/class[]): the elements
        // are managed references and ride the target's reference slots.
        for (int32_t i = 0; i < n; ++i)
        {
            arr = reinterpret_cast<Il2CppArray*>(source); // re-derive (the add may collect)
            il2cpp_list_obj_add(self, *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(arr, i)));
        }
        return;
    }
    for (int32_t i = 0; i < n; ++i)
    {
        arr = reinterpret_cast<Il2CppArray*>(source); // re-derive (the add may collect)
        const char* p = il2cpp_array_get_elem_ptr(arr, i);
        switch (kind)
        {
            case kListObjCtorLong:   il2cpp_list_obj_add_long(self, *reinterpret_cast<const int64_t*>(p)); break;
            case kListObjCtorDouble: il2cpp_list_obj_add_double(self, *reinterpret_cast<const double*>(p)); break;
            case kListObjCtorFloat:  il2cpp_list_obj_add_float(self, il2cpp_array_get_elem_float_as_double(arr, i)); break;
            case kListObjCtorBool:   il2cpp_list_obj_add_bool(self, *reinterpret_cast<const uint8_t*>(p) != 0 ? 1 : 0); break;
            case kListObjCtorChar:   il2cpp_list_obj_add_char(self, *reinterpret_cast<const uint16_t*>(p)); break;
            case kListObjCtorSbyte:  il2cpp_list_obj_add_sbyte(self, *reinterpret_cast<const int8_t*>(p)); break;
            case kListObjCtorByte:   il2cpp_list_obj_add_byte(self, *reinterpret_cast<const uint8_t*>(p)); break;
            case kListObjCtorShort:  il2cpp_list_obj_add_short(self, *reinterpret_cast<const int16_t*>(p)); break;
            case kListObjCtorUshort: il2cpp_list_obj_add_ushort(self, *reinterpret_cast<const uint16_t*>(p)); break;
            case kListObjCtorUint:   il2cpp_list_obj_add_uint(self, static_cast<int32_t>(*reinterpret_cast<const uint32_t*>(p))); break;
            case kListObjCtorDecimal:
            {
                const runtime::Il2CppDecimal v = *reinterpret_cast<const runtime::Il2CppDecimal*>(p);
                il2cpp_list_obj_add_dec(self, &v);
                break;
            }
            case kListObjCtorDateTime:
                il2cpp_list_obj_add_dt(self, runtime::il2cpp_datetime_from_array_bits(*reinterpret_cast<const int64_t*>(p)));
                break;
            case kListObjCtorTimeSpan:
                il2cpp_list_obj_add_ts(self, runtime::Il2CppTimeSpan(*reinterpret_cast<const int64_t*>(p)));
                break;
            case kListObjCtorGuid:
            {
                const Il2CppGuid g = *reinterpret_cast<const Il2CppGuid*>(p);
                il2cpp_list_obj_add_guid(self, g.b);
                break;
            }
            case kListObjCtorDateTimeOffset:
            {
                const runtime::Il2CppDateTimeOffset v = *reinterpret_cast<const runtime::Il2CppDateTimeOffset*>(p);
                il2cpp_list_obj_add_dtoffset(self, v);
                break;
            }
            default:
                il2cpp_throw_not_supported_exception(
                    "List<T>(IEnumerable<T>): the source value cell does not match the element family.");
        }
    }
}

void il2cpp_list_obj_ctor_collection_struct(Il2CppObject* self, Il2CppObject* source,
                                           Il2CppClass* klass, int32_t payloadSize)
{
    ThrowIfNull(self);
    if (source == nullptr) il2cpp_throw_argument_null_exception("collection");
    if (klass == nullptr || payloadSize <= 0)
        il2cpp_throw_not_supported_exception(
            "List<T>(IEnumerable<T>): the struct element descriptor is missing.");
    const bool srcIsListObj = source->klass == &g_listObjClass;
    const int32_t n = CtorSourceCount(source);
    if (n < 0)
        il2cpp_throw_not_supported_exception(
            "List<T>(IEnumerable<T>): the source is neither an array nor a List<T>.");
    if (source->klass == il2cpp_list_i4_class())
        il2cpp_throw_not_supported_exception(
            "List<T>(IEnumerable<T>): a List<int32> source is only valid for the List<int32> expansion.");
    RootScope rs;
    rs.Add(reinterpret_cast<void**>(&self));
    rs.Add(reinterpret_cast<void**>(&source));
    Il2CppArray* items = AllocItems(n);
    Il2CppListObj* l = AsList(self);
    l->items = items;
    il2cpp_write_barrier(reinterpret_cast<void**>(&l->items), reinterpret_cast<Il2CppObject*>(items));
    l->count = 0;
    if (n == 0) return;
    if (srcIsListObj)
    {
        Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(items->data);
        Il2CppObject** src = Slots(AsList(source));
        for (int32_t i = 0; i < n; ++i)
        {
            dst[i] = src[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
        }
        AsList(self)->count = n;
        return;
    }
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(source);
    if (arr->isRefElem)
        il2cpp_throw_not_supported_exception(
            "List<struct>(IEnumerable<struct>): the source is a reference array, not a value array.");
    // The payload is copied out of the (possibly moving) array BEFORE the box
    // allocation, exactly like the 16-byte scalar families above.
    std::vector<char> cell;
    try { cell.resize(static_cast<size_t>(payloadSize)); }
    catch (const std::bad_alloc&)
    {
        il2cpp_throw_new(
            "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
            "Insufficient memory to continue the execution of the program.");
    }
    for (int32_t i = 0; i < n; ++i)
    {
        arr = reinterpret_cast<Il2CppArray*>(source);
        std::memcpy(cell.data(), il2cpp_array_get_elem_ptr(arr, i), static_cast<size_t>(payloadSize));
        il2cpp_list_obj_add_struct(self, cell.data(), klass, payloadSize);
    }
}
void il2cpp_list_obj_add(Il2CppObject* self, Il2CppObject* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); /* nb9 M-7: Grow allocates -- root the element too */ rs.Add(reinterpret_cast<void**>(&v)); EnsureItems(&self); Il2CppListObj* l=AsList(self); if(l->count==l->items->length) Grow(&self); l=AsList(self); Slots(l)[l->count]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&Slots(l)[l->count]), v); ++l->count; }
void il2cpp_list_obj_insert(Il2CppObject* self,int32_t idx,Il2CppObject* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); /* nb9 M-7: Grow allocates -- root the element too */ rs.Add(reinterpret_cast<void**>(&v)); Il2CppListObj* l=AsList(self); ThrowIns(idx,l->count); EnsureItems(&self); l=AsList(self); if(l->count==l->items->length) Grow(&self); l=AsList(self); Il2CppObject** s=Slots(l); for(int32_t i=l->count;i>idx;--i){ s[i]=s[i-1]; il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]); } s[idx]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&s[idx]), v); ++l->count; }
void il2cpp_list_obj_remove_at(Il2CppObject* self,int32_t idx){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); Il2CppObject** s=Slots(l); for(int32_t i=idx;i<l->count-1;++i){ s[i]=s[i+1]; il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]); } s[l->count-1]=nullptr; il2cpp_write_barrier(reinterpret_cast<void**>(&s[l->count-1]), nullptr); --l->count; }
bool il2cpp_list_obj_remove(Il2CppObject* self,Il2CppObject* v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
Il2CppObject* il2cpp_list_obj_get_item(Il2CppObject* self,int32_t idx){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); return Slots(l)[idx]; }
void il2cpp_list_obj_set_item(Il2CppObject* self,int32_t idx,Il2CppObject* v){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); Slots(l)[idx]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&Slots(l)[idx]), v); }
int32_t il2cpp_list_obj_get_count(Il2CppObject* self){ ThrowIfNull(self); return AsList(self)->count; }
int32_t il2cpp_list_obj_get_capacity(Il2CppObject* self){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); return l->items?l->items->length:0; }
void il2cpp_list_obj_set_capacity(Il2CppObject* self,int32_t value){ ThrowIfNull(self); if(value<0) il2cpp_throw_argument_out_of_range_exception("value",kNonNegativeMessage); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); if(value<l->count) il2cpp_throw_argument_out_of_range_exception("value","value was less than the current Count."); int32_t oldLen=l->items?l->items->length:0; if(value==oldLen) return; Il2CppArray* fresh=AllocItems(value); l=AsList(self); int32_t copyLen=l->count<value?l->count:value; if(copyLen>0&&l->items){ Il2CppObject** src=Slots(l); Il2CppObject** dst=reinterpret_cast<Il2CppObject**>(fresh->data); for(int32_t i=0;i<copyLen;++i){ dst[i]=src[i]; il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]); } } il2cpp_write_barrier(reinterpret_cast<void**>(&l->items), reinterpret_cast<Il2CppObject*>(fresh)); l->items=fresh; }
void il2cpp_list_obj_clear(Il2CppObject* self){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); if(l->items){ Il2CppObject** s=Slots(l); for(int32_t i=0;i<l->count;++i){ s[i]=nullptr; il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), nullptr); } } l->count=0; }
// net48 List<T>.IndexOf uses EqualityComparer<T>.Default: value equality, not
// pointer identity. runtime::icall_object_equals is the runtime's equality
// authority (reference identity first; strings ordinal; same-klass boxes by
// payload -- the arraylist.cpp precedent), so a value-equal-but-distinct
// element is found exactly when .NET would find it.
int32_t il2cpp_list_obj_index_of(Il2CppObject* self,Il2CppObject* v){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); if(l->items){ Il2CppObject** s=Slots(l); for(int32_t i=0;i<l->count;++i) if(runtime::icall_object_equals(s[i],v)) return i; } return -1; }
bool il2cpp_list_obj_contains(Il2CppObject* self,Il2CppObject* v){ return il2cpp_list_obj_index_of(self,v)>=0; }
Il2CppObject* il2cpp_list_obj_to_array(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=AllocItems(l->count); l=AsList(self); Il2CppObject** dst=reinterpret_cast<Il2CppObject**>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i){ dst[i]=src?src[i]:nullptr; il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]); } return reinterpret_cast<Il2CppObject*>(r); }
Il2CppObject* il2cpp_list_obj_get_enumerator(Il2CppObject* self){ ThrowIfNull(self); EnsureRegistered(); il2cpp_class_register(&System_Collections_SortedList_Enumerator_class); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* snap=AllocItems(l->count); l=AsList(self); if(l->count>0&&l->items){ Il2CppObject** src=Slots(l); Il2CppObject** dst=reinterpret_cast<Il2CppObject**>(snap->data); for(int32_t i=0;i<l->count;++i){ dst[i]=src[i]; il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]); } } Il2CppArrayEnumerator* en=reinterpret_cast<Il2CppArrayEnumerator*>(il2cpp_object_new(&System_Collections_SortedList_Enumerator_class)); en->array=snap; il2cpp_write_barrier(reinterpret_cast<void**>(&en->array), reinterpret_cast<Il2CppObject*>(snap)); en->index=-1; return reinterpret_cast<Il2CppObject*>(en); }
// net48 List<T>.Sort() with no comparer uses Comparer<T>.Default. The
// runtime's default comparer is il2cpp_array_sort_icomparer(arr, nullptr)
// (arraysort.cpp ComparerCompareCore: null-first, strings ordinal, builtin
// boxes by value, IComparable fallback), so a count-sized snapshot is sorted
// with that authority and copied back through write barriers. The list and
// the snapshot are rooted across the allocating sort.
void il2cpp_list_obj_sort(Il2CppObject* self){
    ThrowIfNull(self);
    RootScope rs; rs.Add(reinterpret_cast<void**>(&self));
    Il2CppListObj* l=AsList(self);
    if(l->count<=1) return;
    Il2CppArray* snap=AllocItems(l->count); rs.Add(reinterpret_cast<void**>(&snap));
    l=AsList(self);
    Il2CppObject** dst=reinterpret_cast<Il2CppObject**>(snap->data);
    Il2CppObject** src=Slots(l);
    for(int32_t i=0;i<l->count;++i){ dst[i]=src[i]; il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]); }
    il2cpp_array_sort_icomparer(snap,nullptr);
    l=AsList(self);
    src=Slots(l);
    dst=reinterpret_cast<Il2CppObject**>(snap->data);
    for(int32_t i=0;i<l->count;++i){ src[i]=dst[i]; il2cpp_write_barrier(reinterpret_cast<void**>(&src[i]), src[i]); }
}
void il2cpp_list_obj_copy_to(Il2CppObject* self,Il2CppObject* arr,int32_t idx){ ThrowIfNull(self); if(!arr) il2cpp_throw_argument_null_exception("array"); Il2CppArray* d=reinterpret_cast<Il2CppArray*>(arr); if(idx<0) il2cpp_throw_argument_out_of_range_exception("arrayIndex",kNonNegativeMessage); Il2CppListObj* l=AsList(self); if(static_cast<int64_t>(idx)+l->count > d->length) il2cpp_throw_argument_exception("Destination array was not long enough. Check destIndex and length, and the array's lower bounds."); if(l->count>0&&l->items){ Il2CppObject** src=Slots(l); Il2CppObject** dst=reinterpret_cast<Il2CppObject**>(d->data); for(int32_t i=0;i<l->count;++i){ dst[idx+i]=src[i]; il2cpp_write_barrier(reinterpret_cast<void**>(&dst[idx+i]), src[i]); } } }
int32_t il2cpp_list_obj_enumerator_move_next(Il2CppObject* self){ ThrowIfNull(self); Il2CppArrayEnumerator* en=reinterpret_cast<Il2CppArrayEnumerator*>(self); ++en->index; return en->index < en->array->length ? 1 : 0; }
Il2CppObject* il2cpp_list_obj_enumerator_get_current(Il2CppObject* self){ ThrowIfNull(self); Il2CppArrayEnumerator* en=reinterpret_cast<Il2CppArrayEnumerator*>(self); return reinterpret_cast<Il2CppObject**>(en->array->data)[en->index]; }
void il2cpp_list_obj_enumerator_reset(Il2CppObject* self){ ThrowIfNull(self); reinterpret_cast<Il2CppArrayEnumerator*>(self)->index=-1; }
void il2cpp_list_obj_enumerator_dispose(Il2CppObject* self){ (void)self; }
// ---- G4: System.Int64 element expansion. Values box on entry and unbox on
// exit through the runtime Int64 box identity; the storage domain is the
// shared listobj reference array, so IndexOf/Contains/Remove and Sort reuse
// the value-equal / net48-comparer paths above (BoxedValueEquals reads the
// Int64 payload). 'self' is rooted across the boxing allocation -- the GC may
// move it before the delegated call re-derives its own root.
void il2cpp_list_obj_add_long(Il2CppObject* self,int64_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_long(Il2CppObject* self,int32_t idx,int64_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_long(Il2CppObject* self,int32_t idx,int64_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_long(Il2CppObject* self,int64_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_long(Il2CppObject* self,int64_t v){ return il2cpp_list_obj_index_of_long(self,v)>=0; }
bool il2cpp_list_obj_remove_long(Il2CppObject* self,int64_t v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_long(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
int64_t il2cpp_list_obj_get_item_long(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_long(il2cpp_list_obj_get_item(self,idx)); }
int64_t il2cpp_list_obj_enumerator_get_current_long(Il2CppObject* self){ return runtime::icall_unbox_long(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_long(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new_typed(sizeof(int64_t),2,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); int64_t* dst=reinterpret_cast<int64_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?runtime::icall_unbox_long(src[i]):0; return reinterpret_cast<Il2CppObject*>(r); }
void il2cpp_list_obj_add_double(Il2CppObject* self,double v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_double(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_double(Il2CppObject* self,int32_t idx,double v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_double(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_double(Il2CppObject* self,int32_t idx,double v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_double(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_double(Il2CppObject* self,double v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_double(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_double(Il2CppObject* self,double v){ return il2cpp_list_obj_index_of_double(self,v)>=0; }
bool il2cpp_list_obj_remove_double(Il2CppObject* self,double v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_double(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
double il2cpp_list_obj_get_item_double(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_double(il2cpp_list_obj_get_item(self,idx)); }
double il2cpp_list_obj_enumerator_get_current_double(Il2CppObject* self){ return runtime::icall_unbox_double(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_double(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new_typed(sizeof(double),4,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); double* dst=reinterpret_cast<double*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?runtime::icall_unbox_double(src[i]):0.0; return reinterpret_cast<Il2CppObject*>(r); }
void il2cpp_list_obj_add_bool(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_bool(v!=0); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_bool(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_bool(v!=0); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_bool(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_bool(v!=0); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_bool(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_bool(v!=0); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_bool(Il2CppObject* self,int32_t v){ return il2cpp_list_obj_index_of_bool(self,v)>=0; }
bool il2cpp_list_obj_remove_bool(Il2CppObject* self,int32_t v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_bool(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
int32_t il2cpp_list_obj_get_item_bool(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_bool(il2cpp_list_obj_get_item(self,idx))?1:0; }
int32_t il2cpp_list_obj_enumerator_get_current_bool(Il2CppObject* self){ return runtime::icall_unbox_bool(il2cpp_list_obj_enumerator_get_current(self))?1:0; }
Il2CppObject* il2cpp_list_obj_to_array_bool(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(bool),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); bool* dst=reinterpret_cast<bool*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?runtime::icall_unbox_bool(src[i]):false; return reinterpret_cast<Il2CppObject*>(r); }
void il2cpp_list_obj_add_char(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_char(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_char(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_char(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_char(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_char(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_char(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_char(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_char(Il2CppObject* self,int32_t v){ return il2cpp_list_obj_index_of_char(self,v)>=0; }
bool il2cpp_list_obj_remove_char(Il2CppObject* self,int32_t v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_char(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
int32_t il2cpp_list_obj_get_item_char(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_char(il2cpp_list_obj_get_item(self,idx)); }
int32_t il2cpp_list_obj_enumerator_get_current_char(Il2CppObject* self){ return runtime::icall_unbox_char(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_char(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(uint16_t),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); uint16_t* dst=reinterpret_cast<uint16_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?static_cast<uint16_t>(runtime::icall_unbox_char(src[i])):0; return reinterpret_cast<Il2CppObject*>(r); }
void il2cpp_list_obj_add_sbyte(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_sbyte(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_sbyte(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_sbyte(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_sbyte(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_sbyte(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_sbyte(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_sbyte(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_sbyte(Il2CppObject* self,int32_t v){ return il2cpp_list_obj_index_of_sbyte(self,v)>=0; }
bool il2cpp_list_obj_remove_sbyte(Il2CppObject* self,int32_t v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_sbyte(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
int32_t il2cpp_list_obj_get_item_sbyte(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_sbyte(il2cpp_list_obj_get_item(self,idx)); }
int32_t il2cpp_list_obj_enumerator_get_current_sbyte(Il2CppObject* self){ return runtime::icall_unbox_sbyte(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_sbyte(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(int8_t),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); int8_t* dst=reinterpret_cast<int8_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?static_cast<int8_t>(runtime::icall_unbox_sbyte(src[i])):0; return reinterpret_cast<Il2CppObject*>(r); }
void il2cpp_list_obj_add_byte(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_byte(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_byte(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_byte(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_byte(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_byte(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_byte(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_byte(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_byte(Il2CppObject* self,int32_t v){ return il2cpp_list_obj_index_of_byte(self,v)>=0; }
bool il2cpp_list_obj_remove_byte(Il2CppObject* self,int32_t v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_byte(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
int32_t il2cpp_list_obj_get_item_byte(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_byte(il2cpp_list_obj_get_item(self,idx)); }
int32_t il2cpp_list_obj_enumerator_get_current_byte(Il2CppObject* self){ return runtime::icall_unbox_byte(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_byte(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(uint8_t),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); uint8_t* dst=reinterpret_cast<uint8_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?static_cast<uint8_t>(runtime::icall_unbox_byte(src[i])):0; return reinterpret_cast<Il2CppObject*>(r); }
void il2cpp_list_obj_add_short(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_short(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_short(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_short(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_short(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_short(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_short(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_short(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_short(Il2CppObject* self,int32_t v){ return il2cpp_list_obj_index_of_short(self,v)>=0; }
bool il2cpp_list_obj_remove_short(Il2CppObject* self,int32_t v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_short(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
int32_t il2cpp_list_obj_get_item_short(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_short(il2cpp_list_obj_get_item(self,idx)); }
int32_t il2cpp_list_obj_enumerator_get_current_short(Il2CppObject* self){ return runtime::icall_unbox_short(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_short(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(int16_t),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); int16_t* dst=reinterpret_cast<int16_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?static_cast<int16_t>(runtime::icall_unbox_short(src[i])):0; return reinterpret_cast<Il2CppObject*>(r); }
void il2cpp_list_obj_add_ushort(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_ushort(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_ushort(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_ushort(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_ushort(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_ushort(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_ushort(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_ushort(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_ushort(Il2CppObject* self,int32_t v){ return il2cpp_list_obj_index_of_ushort(self,v)>=0; }
bool il2cpp_list_obj_remove_ushort(Il2CppObject* self,int32_t v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_ushort(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
int32_t il2cpp_list_obj_get_item_ushort(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_ushort(il2cpp_list_obj_get_item(self,idx)); }
int32_t il2cpp_list_obj_enumerator_get_current_ushort(Il2CppObject* self){ return runtime::icall_unbox_ushort(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_ushort(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(uint16_t),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); uint16_t* dst=reinterpret_cast<uint16_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?static_cast<uint16_t>(runtime::icall_unbox_ushort(src[i])):0; return reinterpret_cast<Il2CppObject*>(r); }
void il2cpp_list_obj_add_uint(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_uint(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_uint(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_uint(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_uint(Il2CppObject* self,int32_t idx,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_uint(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_uint(Il2CppObject* self,int32_t v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_uint(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_uint(Il2CppObject* self,int32_t v){ return il2cpp_list_obj_index_of_uint(self,v)>=0; }
bool il2cpp_list_obj_remove_uint(Il2CppObject* self,int32_t v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_uint(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
int32_t il2cpp_list_obj_get_item_uint(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_uint(il2cpp_list_obj_get_item(self,idx)); }
int32_t il2cpp_list_obj_enumerator_get_current_uint(Il2CppObject* self){ return runtime::icall_unbox_uint(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_uint(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new_typed(sizeof(uint32_t),5,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); uint32_t* dst=reinterpret_cast<uint32_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?static_cast<uint32_t>(runtime::icall_unbox_uint(src[i])):0; return reinterpret_cast<Il2CppObject*>(r); }
// ---- W5 (T2): small-value element expansions. Same box-on-entry /
// unbox-on-exit discipline as the G4 families above; 'self' is rooted
// across the boxing allocation. ToArray narrows the int32/double carrier
// into the typed value array (sizeof(T) stride).
void il2cpp_list_obj_add_float(Il2CppObject* self,double v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_float(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_float(Il2CppObject* self,int32_t idx,double v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_float(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_float(Il2CppObject* self,int32_t idx,double v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_float(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_float(Il2CppObject* self,double v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_float(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_float(Il2CppObject* self,double v){ return il2cpp_list_obj_index_of_float(self,v)>=0; }
bool il2cpp_list_obj_remove_float(Il2CppObject* self,double v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_float(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
double il2cpp_list_obj_get_item_float(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_float(il2cpp_list_obj_get_item(self,idx)); }
double il2cpp_list_obj_enumerator_get_current_float(Il2CppObject* self){ return runtime::icall_unbox_float(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_float(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new_typed(sizeof(float),3,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); float* dst=reinterpret_cast<float*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?static_cast<float>(runtime::icall_unbox_float(src[i])):0.0f; return reinterpret_cast<Il2CppObject*>(r); }

//------------------------------------------------------------------------------
// p0linq4 (D1) -- decimal / DateTime / TimeSpan / user-struct elements.
//
// The storage domain is the SHARED listobj reference array, so a 16-byte
// decimal (and every other value element) crosses the boundary as a BOX:
//   * decimal  -> icall_box_decimal / icall_unbox_decimal (System.Decimal box,
//                 16-byte payload at offset 8);
//   * DateTime -> the Int64 box carrying the CLR ARRAY image
//                 (il2cpp_datetime_to_array_bits: ticks + 2-bit kind), so a
//                 List<DateTime> element observes exactly the value a managed
//                 DateTime[] cell holds;
//   * TimeSpan -> the Int64 box carrying the raw ticks;
//   * user struct -> icall_box_user_struct with the struct's OWN generated
//                 descriptor (a byte-exact payload copy).
// The element getters mirror the runtime's value-return contracts: decimal
// writes through an explicit RESULT SLOT (16 bytes cannot ride a register),
// DateTime/TimeSpan return the runtime struct BY VALUE, and the user-struct
// getter returns the box PAYLOAD ADDRESS (the caller copies it out --
// il2cpp_unbox_user_struct contract).
//
// GC SAFETY: identical to the families above -- 'self' is rooted across the
// boxing allocation, and every read re-derives the slot from the rooted list.
//------------------------------------------------------------------------------
void il2cpp_list_obj_add_dec(Il2CppObject* self, const runtime::Il2CppDecimal* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_decimal(v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_dec(Il2CppObject* self,int32_t idx,const runtime::Il2CppDecimal* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_decimal(v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_dec(Il2CppObject* self,int32_t idx,const runtime::Il2CppDecimal* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_decimal(v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_dec(Il2CppObject* self,const runtime::Il2CppDecimal* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_decimal(v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_dec(Il2CppObject* self,const runtime::Il2CppDecimal* v){ return il2cpp_list_obj_index_of_dec(self,v)>=0; }
bool il2cpp_list_obj_remove_dec(Il2CppObject* self,const runtime::Il2CppDecimal* v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_dec(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
void il2cpp_list_obj_get_item_dec(runtime::Il2CppDecimal* result,Il2CppObject* self,int32_t idx){ *result = runtime::icall_unbox_decimal(il2cpp_list_obj_get_item(self,idx)); }
void il2cpp_list_obj_enumerator_get_current_dec(runtime::Il2CppDecimal* result,Il2CppObject* self){ *result = runtime::icall_unbox_decimal(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_dec(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(runtime::Il2CppDecimal),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); char* dst=reinterpret_cast<char*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i){ const runtime::Il2CppDecimal v=src?runtime::icall_unbox_decimal(src[i]):runtime::Il2CppDecimal(); std::memcpy(dst+static_cast<size_t>(i)*sizeof(runtime::Il2CppDecimal),&v,sizeof(runtime::Il2CppDecimal)); } return reinterpret_cast<Il2CppObject*>(r); }

//------------------------------------------------------------------------------
// Guid element family (16-byte value cell). The boxed carrier is
// icall_box_guid (il2cpp_nullable.h); the same BoxedValueEquals scan as the
// decimal family compares the payload by value, so IndexOf/Contains/Remove
// reuse the shared listobj scan through the boxed value.
// GC SAFETY: identical to the decimal family -- 'self' is rooted across the
// boxing allocation and every read re-derives the slot from the rooted list.
//------------------------------------------------------------------------------
void il2cpp_list_obj_add_guid(Il2CppObject* self, const uint8_t* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_guid(reinterpret_cast<const Il2CppGuid*>(v)); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_guid(Il2CppObject* self,int32_t idx,const uint8_t* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_guid(reinterpret_cast<const Il2CppGuid*>(v)); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_guid(Il2CppObject* self,int32_t idx,const uint8_t* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_guid(reinterpret_cast<const Il2CppGuid*>(v)); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_guid(Il2CppObject* self,const uint8_t* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_guid(reinterpret_cast<const Il2CppGuid*>(v)); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_guid(Il2CppObject* self,const uint8_t* v){ return il2cpp_list_obj_index_of_guid(self,v)>=0; }
bool il2cpp_list_obj_remove_guid(Il2CppObject* self,const uint8_t* v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_guid(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
void il2cpp_list_obj_get_item_guid(Il2CppObject* self,int32_t idx,uint8_t* result){ const Il2CppGuid g=runtime::icall_unbox_guid(il2cpp_list_obj_get_item(self,idx)); std::memcpy(result,&g,sizeof(g)); }
void il2cpp_list_obj_enumerator_get_current_guid(Il2CppObject* self,uint8_t* result){ const Il2CppGuid g=runtime::icall_unbox_guid(il2cpp_list_obj_enumerator_get_current(self)); std::memcpy(result,&g,sizeof(g)); }
Il2CppObject* il2cpp_list_obj_to_array_guid(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(Il2CppGuid),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); char* dst=reinterpret_cast<char*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i){ const Il2CppGuid g=src?runtime::icall_unbox_guid(src[i]):Il2CppGuid(); std::memcpy(dst+static_cast<size_t>(i)*sizeof(Il2CppGuid),&g,sizeof(Il2CppGuid)); } return reinterpret_cast<Il2CppObject*>(r); }

void il2cpp_list_obj_add_dt(Il2CppObject* self, runtime::Il2CppDateTime v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(runtime::il2cpp_datetime_to_array_bits(v)); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_dt(Il2CppObject* self,int32_t idx,runtime::Il2CppDateTime v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(runtime::il2cpp_datetime_to_array_bits(v)); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_dt(Il2CppObject* self,int32_t idx,runtime::Il2CppDateTime v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(runtime::il2cpp_datetime_to_array_bits(v)); il2cpp_list_obj_set_item(self,idx,b); }
// DATEKINDFIX (impl-listrange): net48 DateTime.Equals compares InternalTicks
// only -- DateTimeKind is NOT part of equality. The list stores the 62-bit
// ticks + 2-bit kind ARRAY image inside an Int64 box, so the generic boxed
// scan (runtime::icall_object_equals -> the Int64 payload compare) let the
// Kind bits decide whether two values were "equal" -- a SILENT WRONG VALUE
// (probe: two DateTimes with identical ticks but Unspecified vs Utc answered
// "not found"). The scan below masks the ticks out of each element instead.
static int64_t LrDtTicks(Il2CppObject* box){ return runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(box)).ticks; }
int32_t il2cpp_list_obj_index_of_dt(Il2CppObject* self,runtime::Il2CppDateTime v){ ThrowIfNull(self); const int64_t ticks=v.ticks; Il2CppListObj* l=AsList(self); if(l->items){ Il2CppObject** s=Slots(l); for(int32_t i=0;i<l->count;++i) if(LrDtTicks(s[i])==ticks) return i; } return -1; }
bool il2cpp_list_obj_contains_dt(Il2CppObject* self,runtime::Il2CppDateTime v){ return il2cpp_list_obj_index_of_dt(self,v)>=0; }
bool il2cpp_list_obj_remove_dt(Il2CppObject* self,runtime::Il2CppDateTime v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_dt(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
runtime::Il2CppDateTime il2cpp_list_obj_get_item_dt(Il2CppObject* self,int32_t idx){ return runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(il2cpp_list_obj_get_item(self,idx))); }
runtime::Il2CppDateTime il2cpp_list_obj_enumerator_get_current_dt(Il2CppObject* self){ return runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(il2cpp_list_obj_enumerator_get_current(self))); }
Il2CppObject* il2cpp_list_obj_to_array_dt(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new_typed(sizeof(int64_t),kElemKindDateTime,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); int64_t* dst=reinterpret_cast<int64_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?runtime::icall_unbox_long(src[i]):0; return reinterpret_cast<Il2CppObject*>(r); }

void il2cpp_list_obj_add_ts(Il2CppObject* self, runtime::Il2CppTimeSpan v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(v.ticks); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_ts(Il2CppObject* self,int32_t idx,runtime::Il2CppTimeSpan v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(v.ticks); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_ts(Il2CppObject* self,int32_t idx,runtime::Il2CppTimeSpan v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(v.ticks); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_ts(Il2CppObject* self,runtime::Il2CppTimeSpan v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(v.ticks); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_ts(Il2CppObject* self,runtime::Il2CppTimeSpan v){ return il2cpp_list_obj_index_of_ts(self,v)>=0; }
bool il2cpp_list_obj_remove_ts(Il2CppObject* self,runtime::Il2CppTimeSpan v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_ts(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
runtime::Il2CppTimeSpan il2cpp_list_obj_get_item_ts(Il2CppObject* self,int32_t idx){ return runtime::Il2CppTimeSpan(runtime::icall_unbox_long(il2cpp_list_obj_get_item(self,idx))); }
runtime::Il2CppTimeSpan il2cpp_list_obj_enumerator_get_current_ts(Il2CppObject* self){ return runtime::Il2CppTimeSpan(runtime::icall_unbox_long(il2cpp_list_obj_enumerator_get_current(self))); }
Il2CppObject* il2cpp_list_obj_to_array_ts(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new_typed(sizeof(int64_t),kElemKindTimeSpan,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); int64_t* dst=reinterpret_cast<int64_t*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i) dst[i]=src?runtime::icall_unbox_long(src[i]):0; return reinterpret_cast<Il2CppObject*>(r); }

//------------------------------------------------------------------------------
// dtoffset (delta 2): the System.DateTimeOffset element family (16-byte value
// cell). The boxed carrier is icall_box_date_time_offset (il2cpp_nullable.h);
// the getters return the runtime value struct BY VALUE ("dto" result kind).
// GC SAFETY: identical to the Guid family -- 'self' is rooted across the
// boxing allocation and every read re-derives the slot from the rooted list.
//------------------------------------------------------------------------------
void il2cpp_list_obj_add_dtoffset(Il2CppObject* self, runtime::Il2CppDateTimeOffset v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_date_time_offset(&v); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_dtoffset(Il2CppObject* self,int32_t idx,runtime::Il2CppDateTimeOffset v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_date_time_offset(&v); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_dtoffset(Il2CppObject* self,int32_t idx,runtime::Il2CppDateTimeOffset v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_date_time_offset(&v); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_dtoffset(Il2CppObject* self,runtime::Il2CppDateTimeOffset v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_date_time_offset(&v); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_dtoffset(Il2CppObject* self,runtime::Il2CppDateTimeOffset v){ return il2cpp_list_obj_index_of_dtoffset(self,v)>=0; }
bool il2cpp_list_obj_remove_dtoffset(Il2CppObject* self,runtime::Il2CppDateTimeOffset v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_dtoffset(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
runtime::Il2CppDateTimeOffset il2cpp_list_obj_get_item_dtoffset(Il2CppObject* self,int32_t idx){ return runtime::icall_unbox_date_time_offset(il2cpp_list_obj_get_item(self,idx)); }
runtime::Il2CppDateTimeOffset il2cpp_list_obj_enumerator_get_current_dtoffset(Il2CppObject* self){ return runtime::icall_unbox_date_time_offset(il2cpp_list_obj_enumerator_get_current(self)); }
Il2CppObject* il2cpp_list_obj_to_array_dtoffset(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,sizeof(runtime::Il2CppDateTimeOffset),false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); char* dst=reinterpret_cast<char*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i){ const runtime::Il2CppDateTimeOffset v=src?runtime::icall_unbox_date_time_offset(src[i]):runtime::Il2CppDateTimeOffset(); std::memcpy(dst+static_cast<size_t>(i)*sizeof(runtime::Il2CppDateTimeOffset),&v,sizeof(runtime::Il2CppDateTimeOffset)); } return reinterpret_cast<Il2CppObject*>(r); }

void il2cpp_list_obj_add_struct(Il2CppObject* self, const void* v, Il2CppClass* klass, int32_t payloadSize){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_user_struct(klass,v,static_cast<size_t>(payloadSize)); il2cpp_list_obj_add(self,b); }
void il2cpp_list_obj_insert_struct(Il2CppObject* self,int32_t idx,const void* v,Il2CppClass* klass,int32_t payloadSize){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_user_struct(klass,v,static_cast<size_t>(payloadSize)); il2cpp_list_obj_insert(self,idx,b); }
void il2cpp_list_obj_set_item_struct(Il2CppObject* self,int32_t idx,const void* v,Il2CppClass* klass,int32_t payloadSize){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_user_struct(klass,v,static_cast<size_t>(payloadSize)); il2cpp_list_obj_set_item(self,idx,b); }
int32_t il2cpp_list_obj_index_of_struct(Il2CppObject* self,const void* v,Il2CppClass* klass,int32_t payloadSize){ // net48 List<T>.IndexOf uses EqualityComparer<T>.Default: for a user value
    // struct with no Equals override that is ValueType.Equals -- a field-wise
    // (blittable: byte-wise) compare, NOT reference identity of the freshly
    // made box. Routing through icall_object_equals fell through to `false`
    // (BoxedValueEquals has no arm for a user struct), so IndexOf/Contains/
    // Remove never matched (probe: pt.indexof expected 0, got -1). The _ex
    // kernel is the same memcmp scan the range overloads use.
    return il2cpp_list_obj_index_of_struct_ex(self,v,klass,payloadSize,0,(-2147483647 - 1)); }
bool il2cpp_list_obj_contains_struct(Il2CppObject* self,const void* v,Il2CppClass* klass,int32_t payloadSize){ return il2cpp_list_obj_index_of_struct(self,v,klass,payloadSize)>=0; }
bool il2cpp_list_obj_remove_struct(Il2CppObject* self,const void* v,Il2CppClass* klass,int32_t payloadSize){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_struct(self,v,klass,payloadSize); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
void il2cpp_list_obj_get_item_struct(void* result,Il2CppObject* self,int32_t idx,int32_t payloadSize){ const void* p=runtime::il2cpp_unbox_user_struct(il2cpp_list_obj_get_item(self,idx)); std::memcpy(result,p,static_cast<size_t>(payloadSize)); }
void il2cpp_list_obj_enumerator_get_current_struct(void* result,Il2CppObject* self,int32_t payloadSize){ const void* p=runtime::il2cpp_unbox_user_struct(il2cpp_list_obj_enumerator_get_current(self)); std::memcpy(result,p,static_cast<size_t>(payloadSize)); }
Il2CppObject* il2cpp_list_obj_to_array_struct(Il2CppObject* self,int32_t elemSize){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,elemSize,false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); char* dst=reinterpret_cast<char*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i){ if(src){ const void* p=runtime::il2cpp_unbox_user_struct(src[i]); std::memcpy(dst+static_cast<size_t>(i)*static_cast<size_t>(elemSize),p,static_cast<size_t>(elemSize)); } else { std::memset(dst+static_cast<size_t>(i)*static_cast<size_t>(elemSize),0,static_cast<size_t>(elemSize)); } } return reinterpret_cast<Il2CppObject*>(r); }
bool il2cpp_is_list_obj(Il2CppObject* obj){ return obj != nullptr && obj->klass == &g_listObjClass; }

//------------------------------------------------------------------------------
// impl-listrange: List<T> range / search / predicate operators for the listobj
// reference storage (every non-int32 element family).
//
// The element storage is a reference array of BOXED values, so the slot moves
// (Reverse / RemoveRange / GetRange / FindAll / the range copy) are
// family-agnostic, while the delegate / compare entries take the
// IL2CPP_LIST_KIND_* element code and cross the value in the family's own ABI.
//
// DateTime equality / ordering uses TICKS ONLY (net48 DateTime.Equals and
// CompareTo ignore DateTimeKind): the list stores the 62-bit ticks + 2-bit
// kind ARRAY image in an Int64 box, so a raw payload compare would let the
// Kind bits decide -- a silent wrong value. The array surface already has the
// dedicated ticks-only kernels (il2cpp_array_*_datetime).
//------------------------------------------------------------------------------
namespace {

constexpr int32_t kLrI4 = 0;
constexpr int32_t kLrRef = 1;
constexpr int32_t kLrI8 = 2;
constexpr int32_t kLrF = 3;
constexpr int32_t kLrD = 4;
constexpr int32_t kLrSByte = 5;
constexpr int32_t kLrByte = 6;
constexpr int32_t kLrBool = 7;
constexpr int32_t kLrShort = 8;
constexpr int32_t kLrUShort = 9;
constexpr int32_t kLrChar = 10;
constexpr int32_t kLrUInt = 11;
constexpr int32_t kLrDec = 12;
constexpr int32_t kLrDt = 13;
constexpr int32_t kLrTs = 14;
constexpr int32_t kLrGuid = 15;
constexpr int32_t kLrDto = 16;
// "through the end" / "through the beginning" range marker. The transpiler
// passes it for the 1/2-argument overloads, whose real bound is the live
// Count. (A caller count of INT32_MIN is treated as the marker.)
constexpr int32_t kLrToBound = (-2147483647 - 1);
constexpr const char* kLrOffsetMessage =
    "Offset and length were out of bounds for the array or count is greater than the number of elements from index to end.";
// net48 ArgumentOutOfRange_Count -- the IndexOf/LastIndexOf count bound.
constexpr const char* kLrCountMessage = "Count must be positive and count must refer to a location within the string/array/collection.";

class LrRoots
{
public:
    LrRoots() : m_count(0) {}
    ~LrRoots()
    {
        while (m_count > 0)
            il2cpp_gc_remove_static_root(m_slots[--m_count]);
    }
    void Add(void** slot)
    {
        if (m_count >= static_cast<int32_t>(sizeof(m_slots) / sizeof(m_slots[0])))
            std::abort();
        il2cpp_gc_add_static_root(slot);
        m_slots[m_count++] = slot;
    }

private:
    void** m_slots[8];
    int32_t m_count;
};

Il2CppDelegate* LrRequireCallback(Il2CppObject* cb, const char* paramName)
{
    if (cb == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
        return nullptr;
    }
    const char* name = cb->klass != nullptr ? cb->klass->name : nullptr;
    if (name == nullptr || std::strcmp(name, "System.MulticastDelegate") == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "A multicast delegate cannot be used as a list callback.");
        return nullptr;
    }
    if (std::strcmp(name, "System.Delegate") != 0)
    {
        il2cpp_throw_argument_exception("Object is not a delegate.");
        return nullptr;
    }
    return reinterpret_cast<Il2CppDelegate*>(cb);
}

int32_t LrUnboxI4(Il2CppObject* box, int32_t kind)
{
    switch (kind)
    {
        case kLrSByte:  return runtime::icall_unbox_sbyte(box);
        case kLrByte:   return runtime::icall_unbox_byte(box);
        case kLrBool:   return runtime::icall_unbox_bool(box) ? 1 : 0;
        case kLrShort:  return runtime::icall_unbox_short(box);
        case kLrUShort: return runtime::icall_unbox_ushort(box);
        case kLrChar:   return runtime::icall_unbox_char(box);
        case kLrUInt:   return runtime::icall_unbox_uint(box);
        default:        return runtime::icall_unbox_int32(box);
    }
}

int64_t LrDateTimeTicks(Il2CppObject* box)
{
    return runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(box)).ticks;
}

// The byte width of one SOURCE-ARRAY cell for the element kind.
int32_t LrCellSize(int32_t kind)
{
    switch (kind)
    {
        case kLrRef:                          return static_cast<int32_t>(sizeof(void*));
        case kLrI8: case kLrD: case kLrDt:
        case kLrTs:                           return 8;
        case kLrF: case kLrI4: case kLrUInt:  return 4;
        case kLrSByte: case kLrByte: case kLrBool: return 1;
        case kLrShort: case kLrUShort: case kLrChar: return 2;
        case kLrDec: case kLrGuid: case kLrDto: return 16;
        default:                              return 0;
    }
}

Il2CppObject* LrBoxI4Family(int32_t v, int32_t kind)
{
    switch (kind)
    {
        case kLrSByte:  return runtime::icall_box_sbyte(v);
        case kLrByte:   return runtime::icall_box_byte(v);
        case kLrBool:   return runtime::icall_box_bool(v != 0);
        case kLrShort:  return runtime::icall_box_short(v);
        case kLrUShort: return runtime::icall_box_ushort(v);
        case kLrChar:   return runtime::icall_box_char(v);
        case kLrUInt:   return runtime::icall_box_uint(v);
        default:
            il2cpp_throw_not_supported_exception(
                "Unsupported List element kind for the int32-slot boxing path.");
            return nullptr;
    }
}

// Box the VALUE of one source-array cell at the family's real cell width.
Il2CppObject* LrBoxCell(const char* p, int32_t kind)
{
    switch (kind)
    {
        case kLrRef:  return *reinterpret_cast<Il2CppObject* const*>(p);
        case kLrI8:   return runtime::icall_box_long(*reinterpret_cast<const int64_t*>(p));
        case kLrF:    return runtime::icall_box_float(*reinterpret_cast<const float*>(p));
        case kLrD:    return runtime::icall_box_double(*reinterpret_cast<const double*>(p));
        case kLrSByte: return runtime::icall_box_sbyte(*reinterpret_cast<const int8_t*>(p));
        case kLrByte: return runtime::icall_box_byte(*reinterpret_cast<const uint8_t*>(p));
        case kLrBool: return runtime::icall_box_bool(*reinterpret_cast<const uint8_t*>(p) != 0);
        case kLrShort: return runtime::icall_box_short(*reinterpret_cast<const int16_t*>(p));
        case kLrUShort: return runtime::icall_box_ushort(*reinterpret_cast<const uint16_t*>(p));
        case kLrChar: return runtime::icall_box_char(*reinterpret_cast<const uint16_t*>(p));
        case kLrUInt:
            return runtime::icall_box_uint(
                static_cast<int32_t>(*reinterpret_cast<const uint32_t*>(p)));
        case kLrDec:
            return runtime::icall_box_decimal(
                reinterpret_cast<const runtime::Il2CppDecimal*>(p));
        case kLrDt:   return runtime::icall_box_long(*reinterpret_cast<const int64_t*>(p));
        case kLrTs:   return runtime::icall_box_long(*reinterpret_cast<const int64_t*>(p));
        case kLrGuid:
            return runtime::icall_box_guid(reinterpret_cast<const Il2CppGuid*>(p));
        case kLrDto:
            return runtime::icall_box_date_time_offset(
                reinterpret_cast<const runtime::Il2CppDateTimeOffset*>(p));
        default:
            il2cpp_throw_not_supported_exception(
                "Unsupported List element kind for the range element copy.");
            return nullptr;
    }
}

// The 16-byte DateTimeOffset enumerator carrier (the shared snapshot array
// enumerator; any other iterator shape has no get_Current carrier in the
// runtime and is rejected rather than mis-read).
Il2CppObject* LrBoxDtoFromEnumerator(Il2CppObject* en)
{
    if (en->klass == &System_Collections_SortedList_Enumerator_class)
    {
        Il2CppArrayEnumerator* ae = reinterpret_cast<Il2CppArrayEnumerator*>(en);
        Il2CppArray* arr = ae->array;
        const int32_t idx = ae->index;
        if (arr == nullptr || idx < 0 || idx >= arr->length)
        {
            il2cpp_throw_argument_out_of_range_exception("index", kIndexOutOfRangeMessage);
            return nullptr;
        }
        if (arr->isRefElem)
            return reinterpret_cast<Il2CppObject* const*>(arr->data)[idx];
        if (arr->elemSize == static_cast<int32_t>(sizeof(runtime::Il2CppDateTimeOffset)))
        {
            runtime::Il2CppDateTimeOffset v;
            std::memcpy(&v, arr->data + static_cast<size_t>(idx) * static_cast<size_t>(arr->elemSize),
                        sizeof(v));
            return runtime::icall_box_date_time_offset(&v);
        }
    }
    il2cpp_throw_not_supported_exception(
        "List<System.DateTimeOffset>.AddRange: the source enumerator has no "
        "DateTimeOffset get_Current carrier.");
    return nullptr;
}

Il2CppObject* LrBoxEnumCurrent(Il2CppObject* en, int32_t kind)
{
    switch (kind)
    {
        case kLrRef:  return il2cpp_ienum_get_current_ref(en);
        case kLrI8:   return runtime::icall_box_long(il2cpp_ienum_get_current_i8(en));
        case kLrF:    return runtime::icall_box_float(il2cpp_ienum_get_current_d(en));
        case kLrD:    return runtime::icall_box_double(il2cpp_ienum_get_current_d(en));
        case kLrSByte: case kLrByte: case kLrBool: case kLrShort: case kLrUShort:
        case kLrChar: case kLrUInt:
            return LrBoxI4Family(il2cpp_ienum_get_current_i4(en), kind);
        case kLrDec:
        {
            runtime::Il2CppDecimal v;
            il2cpp_ienum_get_current_dec(&v, en);
            return runtime::icall_box_decimal(&v);
        }
        case kLrDt:
            return runtime::icall_box_long(runtime::il2cpp_datetime_to_array_bits(
                il2cpp_ienum_get_current_dt(en)));
        case kLrTs:
            return runtime::icall_box_long(il2cpp_ienum_get_current_i8(en));
        case kLrGuid:
        {
            uint8_t bytes[16];
            il2cpp_ienum_get_current_guid(en, bytes);
            return runtime::icall_box_guid(reinterpret_cast<const Il2CppGuid*>(bytes));
        }
        case kLrDto:
            return LrBoxDtoFromEnumerator(en);
        default:
            il2cpp_throw_not_supported_exception(
                "Unsupported List element kind for the enumerator element copy.");
            return nullptr;
    }
}

typedef bool (*LrPredI4)(int32_t);
typedef bool (*LrPredI4This)(Il2CppObject*, int32_t);
typedef bool (*LrPredI8)(int64_t);
typedef bool (*LrPredI8This)(Il2CppObject*, int64_t);
typedef bool (*LrPredD)(double);
typedef bool (*LrPredDThis)(Il2CppObject*, double);
typedef bool (*LrPredRef)(Il2CppObject*);
typedef bool (*LrPredRefThis)(Il2CppObject*, Il2CppObject*);
typedef bool (*LrPredDec)(runtime::Il2CppDecimal);
typedef bool (*LrPredDecThis)(Il2CppObject*, runtime::Il2CppDecimal);
typedef bool (*LrPredDt)(runtime::Il2CppDateTime);
typedef bool (*LrPredDtThis)(Il2CppObject*, runtime::Il2CppDateTime);
typedef bool (*LrPredTs)(runtime::Il2CppTimeSpan);
typedef bool (*LrPredTsThis)(Il2CppObject*, runtime::Il2CppTimeSpan);
typedef bool (*LrPredGuid)(Il2CppGuid);
typedef bool (*LrPredGuidThis)(Il2CppObject*, Il2CppGuid);
typedef bool (*LrPredDto)(runtime::Il2CppDateTimeOffset);
typedef bool (*LrPredDtoThis)(Il2CppObject*, runtime::Il2CppDateTimeOffset);
typedef void (*LrActI4)(int32_t);
typedef void (*LrActI4This)(Il2CppObject*, int32_t);
typedef void (*LrActI8)(int64_t);
typedef void (*LrActI8This)(Il2CppObject*, int64_t);
typedef void (*LrActD)(double);
typedef void (*LrActDThis)(Il2CppObject*, double);
typedef void (*LrActRef)(Il2CppObject*);
typedef void (*LrActRefThis)(Il2CppObject*, Il2CppObject*);
typedef void (*LrActDec)(runtime::Il2CppDecimal);
typedef void (*LrActDecThis)(Il2CppObject*, runtime::Il2CppDecimal);
typedef void (*LrActDt)(runtime::Il2CppDateTime);
typedef void (*LrActDtThis)(Il2CppObject*, runtime::Il2CppDateTime);
typedef void (*LrActTs)(runtime::Il2CppTimeSpan);
typedef void (*LrActTsThis)(Il2CppObject*, runtime::Il2CppTimeSpan);
typedef void (*LrActGuid)(Il2CppGuid);
typedef void (*LrActGuidThis)(Il2CppObject*, Il2CppGuid);
typedef void (*LrActDto)(runtime::Il2CppDateTimeOffset);
typedef void (*LrActDtoThis)(Il2CppObject*, runtime::Il2CppDateTimeOffset);

// impl-listrange (B): the Comparison<T> delegate ABI, one pair per family. The
// element values cross exactly like the Predicate<T> ABI above (narrow cells
// widen to int32, float widens to double, the BCL value structs and a user
// struct cross BY VALUE).
typedef int32_t (*LrCmpI4)(int32_t, int32_t);
typedef int32_t (*LrCmpI4This)(Il2CppObject*, int32_t, int32_t);
typedef int32_t (*LrCmpI8)(int64_t, int64_t);
typedef int32_t (*LrCmpI8This)(Il2CppObject*, int64_t, int64_t);
typedef int32_t (*LrCmpD)(double, double);
typedef int32_t (*LrCmpDThis)(Il2CppObject*, double, double);
typedef int32_t (*LrCmpRef)(Il2CppObject*, Il2CppObject*);
typedef int32_t (*LrCmpRefThis)(Il2CppObject*, Il2CppObject*, Il2CppObject*);
typedef int32_t (*LrCmpDec)(runtime::Il2CppDecimal, runtime::Il2CppDecimal);
typedef int32_t (*LrCmpDecThis)(Il2CppObject*, runtime::Il2CppDecimal,
                                runtime::Il2CppDecimal);
typedef int32_t (*LrCmpDt)(runtime::Il2CppDateTime, runtime::Il2CppDateTime);
typedef int32_t (*LrCmpDtThis)(Il2CppObject*, runtime::Il2CppDateTime,
                               runtime::Il2CppDateTime);
typedef int32_t (*LrCmpTs)(runtime::Il2CppTimeSpan, runtime::Il2CppTimeSpan);
typedef int32_t (*LrCmpTsThis)(Il2CppObject*, runtime::Il2CppTimeSpan,
                               runtime::Il2CppTimeSpan);
typedef int32_t (*LrCmpGuid)(Il2CppGuid, Il2CppGuid);
typedef int32_t (*LrCmpGuidThis)(Il2CppObject*, Il2CppGuid, Il2CppGuid);
typedef int32_t (*LrCmpDto)(runtime::Il2CppDateTimeOffset,
                            runtime::Il2CppDateTimeOffset);
typedef int32_t (*LrCmpDtoThis)(Il2CppObject*, runtime::Il2CppDateTimeOffset,
                                runtime::Il2CppDateTimeOffset);

bool LrInvokePred(Il2CppDelegate* d, Il2CppObject* box, int32_t kind)
{
    switch (kind)
    {
        case kLrRef:
            return d->target != nullptr
                ? reinterpret_cast<LrPredRefThis>(d->methodPtr)(d->target, box)
                : reinterpret_cast<LrPredRef>(d->methodPtr)(box);
        case kLrI8:
        {
            const int64_t v = runtime::icall_unbox_long(box);
            return d->target != nullptr
                ? reinterpret_cast<LrPredI8This>(d->methodPtr)(d->target, v)
                : reinterpret_cast<LrPredI8>(d->methodPtr)(v);
        }
        case kLrF:
        case kLrD:
        {
            const double v = kind == kLrF ? runtime::icall_unbox_float(box)
                                          : runtime::icall_unbox_double(box);
            return d->target != nullptr
                ? reinterpret_cast<LrPredDThis>(d->methodPtr)(d->target, v)
                : reinterpret_cast<LrPredD>(d->methodPtr)(v);
        }
        case kLrDec:
        {
            const runtime::Il2CppDecimal v = runtime::icall_unbox_decimal(box);
            return d->target != nullptr
                ? reinterpret_cast<LrPredDecThis>(d->methodPtr)(d->target, v)
                : reinterpret_cast<LrPredDec>(d->methodPtr)(v);
        }
        case kLrDt:
        {
            const runtime::Il2CppDateTime v =
                runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(box));
            return d->target != nullptr
                ? reinterpret_cast<LrPredDtThis>(d->methodPtr)(d->target, v)
                : reinterpret_cast<LrPredDt>(d->methodPtr)(v);
        }
        case kLrTs:
        {
            const runtime::Il2CppTimeSpan v(runtime::icall_unbox_long(box));
            return d->target != nullptr
                ? reinterpret_cast<LrPredTsThis>(d->methodPtr)(d->target, v)
                : reinterpret_cast<LrPredTs>(d->methodPtr)(v);
        }
        case kLrGuid:
        {
            const Il2CppGuid v = runtime::icall_unbox_guid(box);
            return d->target != nullptr
                ? reinterpret_cast<LrPredGuidThis>(d->methodPtr)(d->target, v)
                : reinterpret_cast<LrPredGuid>(d->methodPtr)(v);
        }
        case kLrDto:
        {
            const runtime::Il2CppDateTimeOffset v =
                runtime::icall_unbox_date_time_offset(box);
            return d->target != nullptr
                ? reinterpret_cast<LrPredDtoThis>(d->methodPtr)(d->target, v)
                : reinterpret_cast<LrPredDto>(d->methodPtr)(v);
        }
        default:
        {
            const int32_t v = LrUnboxI4(box, kind);
            return d->target != nullptr
                ? reinterpret_cast<LrPredI4This>(d->methodPtr)(d->target, v)
                : reinterpret_cast<LrPredI4>(d->methodPtr)(v);
        }
    }
}

void LrInvokeAct(Il2CppDelegate* d, Il2CppObject* box, int32_t kind)
{
    switch (kind)
    {
        case kLrRef:
            if (d->target != nullptr)
                reinterpret_cast<LrActRefThis>(d->methodPtr)(d->target, box);
            else
                reinterpret_cast<LrActRef>(d->methodPtr)(box);
            return;
        case kLrI8:
        {
            const int64_t v = runtime::icall_unbox_long(box);
            if (d->target != nullptr)
                reinterpret_cast<LrActI8This>(d->methodPtr)(d->target, v);
            else
                reinterpret_cast<LrActI8>(d->methodPtr)(v);
            return;
        }
        case kLrF:
        case kLrD:
        {
            const double v = kind == kLrF ? runtime::icall_unbox_float(box)
                                          : runtime::icall_unbox_double(box);
            if (d->target != nullptr)
                reinterpret_cast<LrActDThis>(d->methodPtr)(d->target, v);
            else
                reinterpret_cast<LrActD>(d->methodPtr)(v);
            return;
        }
        case kLrDec:
        {
            const runtime::Il2CppDecimal v = runtime::icall_unbox_decimal(box);
            if (d->target != nullptr)
                reinterpret_cast<LrActDecThis>(d->methodPtr)(d->target, v);
            else
                reinterpret_cast<LrActDec>(d->methodPtr)(v);
            return;
        }
        case kLrDt:
        {
            const runtime::Il2CppDateTime v =
                runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(box));
            if (d->target != nullptr)
                reinterpret_cast<LrActDtThis>(d->methodPtr)(d->target, v);
            else
                reinterpret_cast<LrActDt>(d->methodPtr)(v);
            return;
        }
        case kLrTs:
        {
            const runtime::Il2CppTimeSpan v(runtime::icall_unbox_long(box));
            if (d->target != nullptr)
                reinterpret_cast<LrActTsThis>(d->methodPtr)(d->target, v);
            else
                reinterpret_cast<LrActTs>(d->methodPtr)(v);
            return;
        }
        case kLrGuid:
        {
            const Il2CppGuid v = runtime::icall_unbox_guid(box);
            if (d->target != nullptr)
                reinterpret_cast<LrActGuidThis>(d->methodPtr)(d->target, v);
            else
                reinterpret_cast<LrActGuid>(d->methodPtr)(v);
            return;
        }
        case kLrDto:
        {
            const runtime::Il2CppDateTimeOffset v =
                runtime::icall_unbox_date_time_offset(box);
            if (d->target != nullptr)
                reinterpret_cast<LrActDtoThis>(d->methodPtr)(d->target, v);
            else
                reinterpret_cast<LrActDto>(d->methodPtr)(v);
            return;
        }
        default:
        {
            const int32_t v = LrUnboxI4(box, kind);
            if (d->target != nullptr)
                reinterpret_cast<LrActI4This>(d->methodPtr)(d->target, v);
            else
                reinterpret_cast<LrActI4>(d->methodPtr)(v);
            return;
        }
    }
}

// impl-listrange (B): invoke the Comparison<T> delegate over two element boxes.
int32_t LrInvokeCmp(Il2CppDelegate* d, Il2CppObject* a, Il2CppObject* b, int32_t kind)
{
    switch (kind)
    {
        case kLrRef:
            return d->target != nullptr
                ? reinterpret_cast<LrCmpRefThis>(d->methodPtr)(d->target, a, b)
                : reinterpret_cast<LrCmpRef>(d->methodPtr)(a, b);
        case kLrI8:
        {
            const int64_t x = runtime::icall_unbox_long(a);
            const int64_t y = runtime::icall_unbox_long(b);
            return d->target != nullptr
                ? reinterpret_cast<LrCmpI8This>(d->methodPtr)(d->target, x, y)
                : reinterpret_cast<LrCmpI8>(d->methodPtr)(x, y);
        }
        case kLrF:
        case kLrD:
        {
            const double x = kind == kLrF ? runtime::icall_unbox_float(a)
                                          : runtime::icall_unbox_double(a);
            const double y = kind == kLrF ? runtime::icall_unbox_float(b)
                                          : runtime::icall_unbox_double(b);
            return d->target != nullptr
                ? reinterpret_cast<LrCmpDThis>(d->methodPtr)(d->target, x, y)
                : reinterpret_cast<LrCmpD>(d->methodPtr)(x, y);
        }
        case kLrDec:
        {
            const runtime::Il2CppDecimal x = runtime::icall_unbox_decimal(a);
            const runtime::Il2CppDecimal y = runtime::icall_unbox_decimal(b);
            return d->target != nullptr
                ? reinterpret_cast<LrCmpDecThis>(d->methodPtr)(d->target, x, y)
                : reinterpret_cast<LrCmpDec>(d->methodPtr)(x, y);
        }
        case kLrDt:
        {
            const runtime::Il2CppDateTime x =
                runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(a));
            const runtime::Il2CppDateTime y =
                runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(b));
            return d->target != nullptr
                ? reinterpret_cast<LrCmpDtThis>(d->methodPtr)(d->target, x, y)
                : reinterpret_cast<LrCmpDt>(d->methodPtr)(x, y);
        }
        case kLrTs:
        {
            const runtime::Il2CppTimeSpan x(runtime::icall_unbox_long(a));
            const runtime::Il2CppTimeSpan y(runtime::icall_unbox_long(b));
            return d->target != nullptr
                ? reinterpret_cast<LrCmpTsThis>(d->methodPtr)(d->target, x, y)
                : reinterpret_cast<LrCmpTs>(d->methodPtr)(x, y);
        }
        case kLrGuid:
        {
            const Il2CppGuid x = runtime::icall_unbox_guid(a);
            const Il2CppGuid y = runtime::icall_unbox_guid(b);
            return d->target != nullptr
                ? reinterpret_cast<LrCmpGuidThis>(d->methodPtr)(d->target, x, y)
                : reinterpret_cast<LrCmpGuid>(d->methodPtr)(x, y);
        }
        case kLrDto:
        {
            const runtime::Il2CppDateTimeOffset x =
                runtime::icall_unbox_date_time_offset(a);
            const runtime::Il2CppDateTimeOffset y =
                runtime::icall_unbox_date_time_offset(b);
            return d->target != nullptr
                ? reinterpret_cast<LrCmpDtoThis>(d->methodPtr)(d->target, x, y)
                : reinterpret_cast<LrCmpDto>(d->methodPtr)(x, y);
        }
        default:
        {
            const int32_t x = LrUnboxI4(a, kind);
            const int32_t y = LrUnboxI4(b, kind);
            return d->target != nullptr
                ? reinterpret_cast<LrCmpI4This>(d->methodPtr)(d->target, x, y)
                : reinterpret_cast<LrCmpI4>(d->methodPtr)(x, y);
        }
    }
}

bool LrElemEquals(int32_t kind, Il2CppObject* a, Il2CppObject* b)
{
    switch (kind)
    {
        case kLrDt:
            // net48 DateTime.Equals compares InternalTicks only.
            return LrDateTimeTicks(a) == LrDateTimeTicks(b);
        case kLrTs:
            return runtime::icall_unbox_long(a) == runtime::icall_unbox_long(b);
        case kLrDec:
        {
            const runtime::Il2CppDecimal x = runtime::icall_unbox_decimal(a);
            const runtime::Il2CppDecimal y = runtime::icall_unbox_decimal(b);
            return runtime::icall_decimal_equals(&x, &y);
        }
        case kLrGuid:
        {
            const Il2CppGuid x = runtime::icall_unbox_guid(a);
            const Il2CppGuid y = runtime::icall_unbox_guid(b);
            return std::memcmp(x.b, y.b, sizeof(x.b)) == 0;
        }
        case kLrDto:
            return runtime::icall_dtoffset_equals(
                runtime::icall_unbox_date_time_offset(a),
                runtime::icall_unbox_date_time_offset(b));
        case kLrF:
        case kLrD:
        {
            const double x = kind == kLrF ? runtime::icall_unbox_float(a)
                                          : runtime::icall_unbox_double(a);
            const double y = kind == kLrF ? runtime::icall_unbox_float(b)
                                          : runtime::icall_unbox_double(b);
            return x == y || (x != x && y != y);
        }
        case kLrRef:
            return runtime::icall_object_equals(a, b);
        case kLrI8:
            return runtime::icall_unbox_long(a) == runtime::icall_unbox_long(b);
        default:
            return LrUnboxI4(a, kind) == LrUnboxI4(b, kind);
    }
}

int32_t LrCompareDouble(double x, double y)
{
    const bool xn = (x != x);
    const bool yn = (y != y);
    if (xn) return yn ? 0 : -1;
    if (yn) return 1;
    if (x < y) return -1;
    if (x > y) return 1;
    return 0;
}

int32_t LrCompare(int32_t kind, Il2CppObject* a, Il2CppObject* b, Il2CppObject* comparer)
{
    if (comparer != nullptr && comparer != il2cpp_comparer_default())
        return il2cpp_icomparer_compare(comparer, a, b);
    switch (kind)
    {
        case kLrRef:
            // The DEFAULT comparer's Compare(object, object): the receiver MUST
            // be the Comparer.Default singleton -- il2cpp_comparer_compare
            // throws NullReferenceException for a null receiver (a null
            // IComparer is a legal List<T>.BinarySearch/Sort argument meaning
            // Comparer<T>.Default, so passing nullptr here turned every
            // reference-family BinarySearch/Sort into a spurious NRE).
            return il2cpp_comparer_compare(il2cpp_comparer_default(), a, b);
        case kLrI8:
        {
            const int64_t x = runtime::icall_unbox_long(a);
            const int64_t y = runtime::icall_unbox_long(b);
            return x < y ? -1 : (x > y ? 1 : 0);
        }
        case kLrF:
            return LrCompareDouble(runtime::icall_unbox_float(a),
                                   runtime::icall_unbox_float(b));
        case kLrD:
            return LrCompareDouble(runtime::icall_unbox_double(a),
                                   runtime::icall_unbox_double(b));
        case kLrDec:
        {
            const runtime::Il2CppDecimal x = runtime::icall_unbox_decimal(a);
            const runtime::Il2CppDecimal y = runtime::icall_unbox_decimal(b);
            return runtime::icall_decimal_compare(&x, &y);
        }
        case kLrDt:
        {
            const int64_t x = LrDateTimeTicks(a);
            const int64_t y = LrDateTimeTicks(b);
            return x < y ? -1 : (x > y ? 1 : 0);
        }
        case kLrTs:
        {
            const int64_t x = runtime::icall_unbox_long(a);
            const int64_t y = runtime::icall_unbox_long(b);
            return x < y ? -1 : (x > y ? 1 : 0);
        }
        case kLrGuid:
        {
            const Il2CppGuid x = runtime::icall_unbox_guid(a);
            const Il2CppGuid y = runtime::icall_unbox_guid(b);
            return il2cpp_guid_compare(x.b, y.b);
        }
        case kLrDto:
            return runtime::icall_dtoffset_compare(
                runtime::icall_unbox_date_time_offset(a),
                runtime::icall_unbox_date_time_offset(b));
        case kLrByte:
        case kLrUShort:
        case kLrChar:
        case kLrUInt:
        {
            const uint32_t x = static_cast<uint32_t>(LrUnboxI4(a, kind));
            const uint32_t y = static_cast<uint32_t>(LrUnboxI4(b, kind));
            return x < y ? -1 : (x > y ? 1 : 0);
        }
        default:
        {
            const int32_t x = LrUnboxI4(a, kind);
            const int32_t y = LrUnboxI4(b, kind);
            return x < y ? -1 : (x > y ? 1 : 0);
        }
    }
}

// Append/insert the source elements at `at`. The source may be a listobj (its
// BOXED slots are copied verbatim), an array (each cell is boxed at the
// family's real width), or any other IEnumerable<T> (the shared enumerator
// dispatcher with the family's own get_Current carrier).
void LrInsertSource(Il2CppObject* self, int32_t at, Il2CppObject* collection, int32_t kind)
{
    ThrowIfNull(self);
    if (collection == nullptr)
        il2cpp_throw_argument_null_exception("collection");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&collection));
    if (il2cpp_is_list_obj(collection))
    {
        int32_t n = AsList(collection)->count;
        Il2CppArray* snap = reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array(collection));
        roots.Add(reinterpret_cast<void**>(&snap));
        Il2CppObject** src = reinterpret_cast<Il2CppObject**>(snap->data);
        int32_t pos = at;
        for (int32_t i = 0; i < n; ++i)
        {
            il2cpp_list_obj_insert(self, pos, src[i]);
            ++pos;
        }
        return;
    }
    if (collection->klass != nullptr && collection->klass->name != nullptr
        && collection->klass->name[0] == '[')
    {
        Il2CppArray* src = reinterpret_cast<Il2CppArray*>(collection);
        roots.Add(reinterpret_cast<void**>(&src));
        const int32_t len = src->length;
        int32_t pos = at;
        if (kind == kLrRef)
        {
            if (!src->isRefElem)
                il2cpp_throw_argument_exception("Object is not a reference array.");
            Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(src->data);
            for (int32_t i = 0; i < len; ++i)
            {
                il2cpp_list_obj_insert(self, pos, slots[i]);
                ++pos;
            }
            return;
        }
        if (src->elemSize != LrCellSize(kind))
            il2cpp_throw_argument_exception(
                "The source array element type does not match the list element type.");
        for (int32_t i = 0; i < len; ++i)
        {
            il2cpp_list_obj_insert(self, pos, LrBoxCell(il2cpp_array_get_elem_ptr(src, i), kind));
            ++pos;
        }
        return;
    }
    Il2CppObject* en = il2cpp_ienum_get_enumerator(collection);
    roots.Add(reinterpret_cast<void**>(&en));
    int32_t pos = at;
    while (il2cpp_ienum_move_next(en))
    {
        il2cpp_list_obj_insert(self, pos, LrBoxEnumCurrent(en, kind));
        ++pos;
    }
    il2cpp_ienum_dispose(en);
}

} // namespace

void il2cpp_list_obj_reverse(Il2CppObject* self)
{
    ThrowIfNull(self);
    Il2CppListObj* l = AsList(self);
    Il2CppObject** s = Slots(l);
    for (int32_t i = 0, j = l->count - 1; i < j; ++i, --j)
    {
        Il2CppObject* t = s[i];
        s[i] = s[j];
        il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]);
        s[j] = t;
        il2cpp_write_barrier(reinterpret_cast<void**>(&s[j]), s[j]);
    }
}

void il2cpp_list_obj_reverse_range(Il2CppObject* self, int32_t index, int32_t count)
{
    ThrowIfNull(self);
    if (index < 0)
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    if (count < 0)
        il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
    Il2CppListObj* l = AsList(self);
    if (static_cast<int64_t>(index) + static_cast<int64_t>(count) > l->count)
        il2cpp_throw_argument_exception(kLrOffsetMessage);
    Il2CppObject** s = Slots(l);
    for (int32_t i = index, j = index + count - 1; i < j; ++i, --j)
    {
        Il2CppObject* t = s[i];
        s[i] = s[j];
        il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]);
        s[j] = t;
        il2cpp_write_barrier(reinterpret_cast<void**>(&s[j]), s[j]);
    }
}

void il2cpp_list_obj_remove_range(Il2CppObject* self, int32_t index, int32_t count)
{
    ThrowIfNull(self);
    if (index < 0)
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    if (count < 0)
        il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
    Il2CppListObj* l = AsList(self);
    if (static_cast<int64_t>(index) + static_cast<int64_t>(count) > l->count)
        il2cpp_throw_argument_exception(kLrOffsetMessage);
    Il2CppObject** s = Slots(l);
    for (int32_t i = index; i < l->count - count; ++i)
    {
        s[i] = s[i + count];
        il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]);
    }
    for (int32_t i = l->count - count; i < l->count; ++i)
    {
        s[i] = nullptr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), nullptr);
    }
    l->count -= count;
}

Il2CppObject* il2cpp_list_obj_get_range(Il2CppObject* self, int32_t index, int32_t count)
{
    ThrowIfNull(self);
    if (index < 0)
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    if (count < 0)
        il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
    Il2CppListObj* l = AsList(self);
    if (static_cast<int64_t>(index) + static_cast<int64_t>(count) > l->count)
        il2cpp_throw_argument_exception(kLrOffsetMessage);
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppObject* result = il2cpp_list_obj_create_with_capacity(count);
    roots.Add(reinterpret_cast<void**>(&result));
    l = AsList(self);
    Il2CppListObj* dst = AsList(result);
    Il2CppObject** src = Slots(l);
    Il2CppObject** out = Slots(dst);
    for (int32_t i = 0; i < count; ++i)
    {
        out[i] = src[index + i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&out[i]), out[i]);
    }
    dst->count = count;
    return result;
}

void il2cpp_list_obj_add_range(Il2CppObject* self, Il2CppObject* collection, int32_t kind)
{
    ThrowIfNull(self);
    const int32_t at = AsList(self)->count;
    LrInsertSource(self, at, collection, kind);
}

void il2cpp_list_obj_insert_range(Il2CppObject* self, int32_t index, Il2CppObject* collection,
                                  int32_t kind)
{
    ThrowIfNull(self);
    ThrowIns(index, AsList(self)->count);
    LrInsertSource(self, index, collection, kind);
}

int32_t il2cpp_list_obj_remove_all(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    ThrowIfNull(self);
    Il2CppDelegate* d = LrRequireCallback(match, "match");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    const int32_t n = AsList(self)->count;
    int32_t wi = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* box = Slots(AsList(self))[i];
        if (LrInvokePred(d, box, kind)) continue;
        Il2CppObject** s = Slots(AsList(self));
        if (wi != i)
        {
            s[wi] = box;
            il2cpp_write_barrier(reinterpret_cast<void**>(&s[wi]), s[wi]);
        }
        ++wi;
    }
    Il2CppListObj* l = AsList(self);
    Il2CppObject** s = Slots(l);
    for (int32_t i = wi; i < l->count; ++i)
    {
        s[i] = nullptr;
        il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), nullptr);
    }
    const int32_t removed = l->count - wi;
    l->count = wi;
    return removed;
}

int32_t il2cpp_list_obj_find_index(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    ThrowIfNull(self);
    Il2CppDelegate* d = LrRequireCallback(match, "match");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    const int32_t n = AsList(self)->count;
    for (int32_t i = 0; i < n; ++i)
    {
        if (LrInvokePred(d, Slots(AsList(self))[i], kind)) return i;
    }
    return -1;
}

int32_t il2cpp_list_obj_find_last_index(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    ThrowIfNull(self);
    Il2CppDelegate* d = LrRequireCallback(match, "match");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    const int32_t n = AsList(self)->count;
    for (int32_t i = n - 1; i >= 0; --i)
    {
        if (LrInvokePred(d, Slots(AsList(self))[i], kind)) return i;
    }
    return -1;
}

int32_t il2cpp_list_obj_exists(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    return il2cpp_list_obj_find_index(self, match, kind) >= 0 ? 1 : 0;
}

int32_t il2cpp_list_obj_true_for_all(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    ThrowIfNull(self);
    Il2CppDelegate* d = LrRequireCallback(match, "match");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    const int32_t n = AsList(self)->count;
    for (int32_t i = 0; i < n; ++i)
    {
        if (!LrInvokePred(d, Slots(AsList(self))[i], kind)) return 0;
    }
    return 1;
}

void il2cpp_list_obj_for_each(Il2CppObject* self, Il2CppObject* action, int32_t kind)
{
    ThrowIfNull(self);
    Il2CppDelegate* d = LrRequireCallback(action, "action");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    const int32_t n = AsList(self)->count;
    for (int32_t i = 0; i < n; ++i)
    {
        LrInvokeAct(d, Slots(AsList(self))[i], kind);
    }
}

Il2CppObject* il2cpp_list_obj_find_all(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    ThrowIfNull(self);
    Il2CppDelegate* d = LrRequireCallback(match, "match");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    const int32_t n = AsList(self)->count;
    Il2CppObject* result = il2cpp_list_obj_create_with_capacity(n);
    roots.Add(reinterpret_cast<void**>(&result));
    Il2CppListObj* dst = AsList(result);
    int32_t wi = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* box = Slots(AsList(self))[i];
        if (!LrInvokePred(d, box, kind)) continue;
        Il2CppObject** out = Slots(AsList(result));
        out[wi] = box;
        il2cpp_write_barrier(reinterpret_cast<void**>(&out[wi]), out[wi]);
        ++wi;
    }
    dst = AsList(result);
    dst->count = wi;
    return result;
}

Il2CppObject* il2cpp_list_obj_find_box(Il2CppObject* self, Il2CppObject* match, int32_t kind,
                                       int32_t fromEnd)
{
    ThrowIfNull(self);
    Il2CppDelegate* d = LrRequireCallback(match, "match");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    const int32_t n = AsList(self)->count;
    if (fromEnd != 0)
    {
        for (int32_t i = n - 1; i >= 0; --i)
        {
            Il2CppObject* box = Slots(AsList(self))[i];
            if (LrInvokePred(d, box, kind)) return box;
        }
    }
    else
    {
        for (int32_t i = 0; i < n; ++i)
        {
            Il2CppObject* box = Slots(AsList(self))[i];
            if (LrInvokePred(d, box, kind)) return box;
        }
    }
    return nullptr;
}

int32_t il2cpp_list_obj_find_i4(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 0);
    return box != nullptr ? LrUnboxI4(box, kind) : 0;
}

int64_t il2cpp_list_obj_find_i8(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 0);
    return box != nullptr ? runtime::icall_unbox_long(box) : 0;
}

double il2cpp_list_obj_find_d(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 0);
    if (box == nullptr) return 0.0;
    return kind == kLrF ? runtime::icall_unbox_float(box) : runtime::icall_unbox_double(box);
}

Il2CppObject* il2cpp_list_obj_find_ref(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    return il2cpp_list_obj_find_box(self, match, kind, 0);
}

void il2cpp_list_obj_find_dec(runtime::Il2CppDecimal* result, Il2CppObject* self,
                              Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 0);
    *result = box != nullptr ? runtime::icall_unbox_decimal(box) : runtime::Il2CppDecimal();
}

runtime::Il2CppDateTime il2cpp_list_obj_find_dt(Il2CppObject* self, Il2CppObject* match,
                                                int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 0);
    if (box == nullptr) return runtime::Il2CppDateTime();
    return runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(box));
}

runtime::Il2CppTimeSpan il2cpp_list_obj_find_ts(Il2CppObject* self, Il2CppObject* match,
                                                int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 0);
    if (box == nullptr) return runtime::Il2CppTimeSpan();
    return runtime::Il2CppTimeSpan(runtime::icall_unbox_long(box));
}

void il2cpp_list_obj_find_guid(Il2CppObject* self, Il2CppObject* match, int32_t kind,
                               uint8_t* result)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 0);
    if (box == nullptr)
    {
        std::memset(result, 0, 16);
        return;
    }
    const Il2CppGuid v = runtime::icall_unbox_guid(box);
    std::memcpy(result, &v, sizeof(v));
}

runtime::Il2CppDateTimeOffset il2cpp_list_obj_find_dto(Il2CppObject* self, Il2CppObject* match,
                                                       int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 0);
    if (box == nullptr) return runtime::Il2CppDateTimeOffset();
    return runtime::icall_unbox_date_time_offset(box);
}

int32_t il2cpp_list_obj_find_last_i4(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 1);
    return box != nullptr ? LrUnboxI4(box, kind) : 0;
}

int64_t il2cpp_list_obj_find_last_i8(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 1);
    return box != nullptr ? runtime::icall_unbox_long(box) : 0;
}

double il2cpp_list_obj_find_last_d(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 1);
    if (box == nullptr) return 0.0;
    return kind == kLrF ? runtime::icall_unbox_float(box) : runtime::icall_unbox_double(box);
}

Il2CppObject* il2cpp_list_obj_find_last_ref(Il2CppObject* self, Il2CppObject* match, int32_t kind)
{
    return il2cpp_list_obj_find_box(self, match, kind, 1);
}

void il2cpp_list_obj_find_last_dec(runtime::Il2CppDecimal* result, Il2CppObject* self,
                                   Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 1);
    *result = box != nullptr ? runtime::icall_unbox_decimal(box) : runtime::Il2CppDecimal();
}

runtime::Il2CppDateTime il2cpp_list_obj_find_last_dt(Il2CppObject* self, Il2CppObject* match,
                                                     int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 1);
    if (box == nullptr) return runtime::Il2CppDateTime();
    return runtime::il2cpp_datetime_from_array_bits(runtime::icall_unbox_long(box));
}

runtime::Il2CppTimeSpan il2cpp_list_obj_find_last_ts(Il2CppObject* self, Il2CppObject* match,
                                                     int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 1);
    if (box == nullptr) return runtime::Il2CppTimeSpan();
    return runtime::Il2CppTimeSpan(runtime::icall_unbox_long(box));
}

void il2cpp_list_obj_find_last_guid(Il2CppObject* self, Il2CppObject* match, int32_t kind,
                                    uint8_t* result)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 1);
    if (box == nullptr)
    {
        std::memset(result, 0, 16);
        return;
    }
    const Il2CppGuid v = runtime::icall_unbox_guid(box);
    std::memcpy(result, &v, sizeof(v));
}

runtime::Il2CppDateTimeOffset il2cpp_list_obj_find_last_dto(Il2CppObject* self,
                                                            Il2CppObject* match, int32_t kind)
{
    Il2CppObject* box = il2cpp_list_obj_find_box(self, match, kind, 1);
    if (box == nullptr) return runtime::Il2CppDateTimeOffset();
    return runtime::icall_unbox_date_time_offset(box);
}

//------------------------------------------------------------------------------
// Comparison cores (net48 IndexOf / LastIndexOf / BinarySearch over the boxed
// scan). The value is boxed once by the carrier below; the scan compares
// through the element family's own authority.
//------------------------------------------------------------------------------
int32_t il2cpp_list_obj_index_of_box_range(Il2CppObject* self, Il2CppObject* valueBox,
                                           int32_t start, int32_t count, int32_t kind)
{
    ThrowIfNull(self);
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&valueBox));
    const int32_t n = AsList(self)->count;
    if (start < 0 || start > n)
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    if (count == kLrToBound)
    {
        count = n - start;
    }
    else
    {
        if (count < 0)
            il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
        // net48 List<T>.IndexOf(value, index, count) validates
        // count > Count - index with ArgumentOutOfRangeException (NOT the
        // ArgumentException Array.Copy raises) -- probe-verified by
        // edge.indexof_count_oob.
        if (static_cast<int64_t>(start) + static_cast<int64_t>(count) > n)
            il2cpp_throw_argument_out_of_range_exception("count", kLrCountMessage);
    }
    for (int32_t i = start; i < start + count; ++i)
    {
        if (LrElemEquals(kind, Slots(AsList(self))[i], valueBox)) return i;
    }
    return -1;
}

int32_t il2cpp_list_obj_last_index_of_box_range(Il2CppObject* self, Il2CppObject* valueBox,
                                                int32_t index, int32_t count, int32_t kind)
{
    ThrowIfNull(self);
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&valueBox));
    const int32_t n = AsList(self)->count;
    int32_t hi;
    int32_t lo;
    if (index == kLrToBound && count == kLrToBound)
    {
        // The 0-argument overload: net48 LastIndexOf(item) returns -1 for an
        // EMPTY list before validating anything.
        if (n == 0) return -1;
        hi = n - 1;
        lo = 0;
    }
    else
    {
        if (index < 0 || index >= n)
            il2cpp_throw_argument_out_of_range_exception("index", kIndexOutOfRangeMessage);
        if (count == kLrToBound)
        {
            count = index + 1;
        }
        else
        {
            if (count < 0)
                il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
            if (count > index + 1)
                il2cpp_throw_argument_out_of_range_exception("count", kIndexOutOfRangeMessage);
        }
        hi = index;
        lo = index - count + 1;
    }
    for (int32_t i = hi; i >= lo; --i)
    {
        if (LrElemEquals(kind, Slots(AsList(self))[i], valueBox)) return i;
    }
    return -1;
}

int32_t il2cpp_list_obj_binary_search_box(Il2CppObject* self, Il2CppObject* valueBox,
                                          Il2CppObject* comparer, int32_t start, int32_t count,
                                          int32_t kind)
{
    ThrowIfNull(self);
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&valueBox));
    roots.Add(reinterpret_cast<void**>(&comparer));
    const int32_t n = AsList(self)->count;
    if (start < 0)
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    if (count == kLrToBound)
    {
        count = n - start;
    }
    else
    {
        if (count < 0)
            il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
        if (static_cast<int64_t>(start) + static_cast<int64_t>(count) > n)
            il2cpp_throw_argument_exception(kLrOffsetMessage);
    }
    int32_t lo = start;
    int32_t hi = start + count - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + ((hi - lo) >> 1);
        const int32_t cmp = LrCompare(kind, Slots(AsList(self))[mid], valueBox, comparer);
        if (cmp == 0) return mid;
        if (cmp < 0)
            lo = mid + 1;
        else
            hi = mid - 1;
    }
    return ~lo;
}

#define IL2CPP_LR_SEARCH(SFX, CTYPE, BOXEXPR)                                              \
    int32_t il2cpp_list_obj_index_of_range_##SFX(Il2CppObject* self, CTYPE value,          \
                                                 int32_t start, int32_t count,            \
                                                 int32_t kind)                            \
    {                                                                                      \
        Il2CppObject* box = (BOXEXPR);                                                     \
        return il2cpp_list_obj_index_of_box_range(self, box, start, count, kind);          \
    }                                                                                      \
    int32_t il2cpp_list_obj_last_index_of_range_##SFX(Il2CppObject* self, CTYPE value,     \
                                                      int32_t index, int32_t count,        \
                                                      int32_t kind)                        \
    {                                                                                      \
        Il2CppObject* box = (BOXEXPR);                                                     \
        return il2cpp_list_obj_last_index_of_box_range(self, box, index, count, kind);     \
    }                                                                                      \
    int32_t il2cpp_list_obj_binary_search_##SFX(Il2CppObject* self, CTYPE value,           \
                                                Il2CppObject* comparer, int32_t start,     \
                                                int32_t count, int32_t kind)               \
    {                                                                                      \
        Il2CppObject* box = (BOXEXPR);                                                     \
        return il2cpp_list_obj_binary_search_box(self, box, comparer, start, count, kind); \
    }

IL2CPP_LR_SEARCH(i4, int32_t, LrBoxI4Family(value, kind))
IL2CPP_LR_SEARCH(i8, int64_t, runtime::icall_box_long(value))
// impl-listrange: float widens onto the double CARRIER, but the box identity
// must stay System.Single -- icall_unbox_float REJECTS a Double box, so a
// double box here would throw InvalidCastException on the first comparison.
IL2CPP_LR_SEARCH(d, double,
                 kind == kLrF ? runtime::icall_box_float(static_cast<float>(value))
                              : runtime::icall_box_double(value))
IL2CPP_LR_SEARCH(ref, Il2CppObject*, value)
IL2CPP_LR_SEARCH(dec, const runtime::Il2CppDecimal*, runtime::icall_box_decimal(value))
IL2CPP_LR_SEARCH(dt, runtime::Il2CppDateTime,
                 runtime::icall_box_long(runtime::il2cpp_datetime_to_array_bits(value)))
IL2CPP_LR_SEARCH(ts, runtime::Il2CppTimeSpan, runtime::icall_box_long(value.ticks))
IL2CPP_LR_SEARCH(guid, const uint8_t*,
                 runtime::icall_box_guid(reinterpret_cast<const Il2CppGuid*>(value)))
IL2CPP_LR_SEARCH(dto, runtime::Il2CppDateTimeOffset,
                 runtime::icall_box_date_time_offset(&value))

#undef IL2CPP_LR_SEARCH

//------------------------------------------------------------------------------
// impl-listrange (B): CopyTo(T[], int) for the family's own destination cell
// width. net48 validation order: null array, negative arrayIndex, destination
// capacity. The element image is unboxed straight into the destination cell
// (DateTime keeps its packed kind|ticks ARRAY image -- the box payload is
// already in that form).
//------------------------------------------------------------------------------
void il2cpp_list_obj_copy_to_kind(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex,
                                  int32_t kind)
{
    ThrowIfNull(self);
    if (array == nullptr)
        il2cpp_throw_argument_null_exception("array");
    Il2CppArray* dst = reinterpret_cast<Il2CppArray*>(array);
    if (arrayIndex < 0)
        il2cpp_throw_argument_out_of_range_exception("arrayIndex", kNonNegativeMessage);
    Il2CppListObj* l = AsList(self);
    if (static_cast<int64_t>(arrayIndex) + static_cast<int64_t>(l->count) > dst->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    if (l->count <= 0 || l->items == nullptr) return;
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&array));
    const int32_t n = AsList(self)->count;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject** s = Slots(AsList(self));
        Il2CppObject* box = s[i];
        char* cell = il2cpp_array_get_elem_ptr(dst, arrayIndex + i);
        switch (kind)
        {
            case kLrRef:
            {
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(cell);
                *slot = box;
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), box);
                break;
            }
            case kLrI8: case kLrDt: case kLrTs:
                *reinterpret_cast<int64_t*>(cell) = runtime::icall_unbox_long(box);
                break;
            case kLrD:
                *reinterpret_cast<double*>(cell) = runtime::icall_unbox_double(box);
                break;
            case kLrF:
                *reinterpret_cast<float*>(cell) =
                    static_cast<float>(runtime::icall_unbox_float(box));
                break;
            case kLrDec:
            {
                const runtime::Il2CppDecimal v = runtime::icall_unbox_decimal(box);
                std::memcpy(cell, &v, sizeof(v));
                break;
            }
            case kLrGuid:
            {
                const Il2CppGuid v = runtime::icall_unbox_guid(box);
                std::memcpy(cell, &v, sizeof(v));
                break;
            }
            case kLrDto:
            {
                const runtime::Il2CppDateTimeOffset v =
                    runtime::icall_unbox_date_time_offset(box);
                std::memcpy(cell, &v, sizeof(v));
                break;
            }
            case kLrSByte:
                *reinterpret_cast<int8_t*>(cell) =
                    static_cast<int8_t>(LrUnboxI4(box, kind));
                break;
            case kLrByte:
                *reinterpret_cast<uint8_t*>(cell) =
                    static_cast<uint8_t>(LrUnboxI4(box, kind));
                break;
            case kLrBool:
                *reinterpret_cast<uint8_t*>(cell) = LrUnboxI4(box, kind) != 0 ? 1 : 0;
                break;
            case kLrShort:
                *reinterpret_cast<int16_t*>(cell) =
                    static_cast<int16_t>(LrUnboxI4(box, kind));
                break;
            case kLrUShort:
                *reinterpret_cast<uint16_t*>(cell) =
                    static_cast<uint16_t>(LrUnboxI4(box, kind));
                break;
            case kLrChar:
                *reinterpret_cast<uint16_t*>(cell) =
                    static_cast<uint16_t>(LrUnboxI4(box, kind));
                break;
            case kLrUInt:
                *reinterpret_cast<uint32_t*>(cell) =
                    static_cast<uint32_t>(LrUnboxI4(box, kind));
                break;
            default:
                *reinterpret_cast<int32_t*>(cell) = LrUnboxI4(box, kind);
                break;
        }
    }
}

void il2cpp_list_obj_copy_to_struct(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex,
                                    int32_t payloadSize)
{
    ThrowIfNull(self);
    if (array == nullptr)
        il2cpp_throw_argument_null_exception("array");
    Il2CppArray* dst = reinterpret_cast<Il2CppArray*>(array);
    if (arrayIndex < 0)
        il2cpp_throw_argument_out_of_range_exception("arrayIndex", kNonNegativeMessage);
    Il2CppListObj* l = AsList(self);
    if (static_cast<int64_t>(arrayIndex) + static_cast<int64_t>(l->count) > dst->length)
        il2cpp_throw_argument_exception(
            "Destination array was not long enough. Check destIndex and length, and the array's lower bounds.");
    if (l->count <= 0 || l->items == nullptr) return;
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&array));
    const int32_t n = AsList(self)->count;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject** s = Slots(AsList(self));
        const void* p = runtime::il2cpp_unbox_user_struct(s[i]);
        std::memcpy(il2cpp_array_get_elem_ptr(dst, arrayIndex + i), p,
                    static_cast<size_t>(payloadSize));
    }
}

//------------------------------------------------------------------------------
// impl-listrange (B): Sort(Comparison<T>). A Hoare-partition quicksort over
// the list slots; every element read/write re-derives the slot array from the
// ROOTED receiver, so a compaction triggered by the managed comparison picks
// up the moved backing array (the arraysort.cpp QuickSortRefs contract). The
// comparison delegate is likewise re-read from its rooted slot.
//------------------------------------------------------------------------------
namespace {

void LrQuickSortSlots(Il2CppObject** selfSlot, Il2CppObject** delSlot, int32_t kind,
                      int32_t lo, int32_t hi)
{
    while (lo < hi)
    {
        Il2CppObject* pivot = Slots(AsList(*selfSlot))[lo + (hi - lo) / 2];
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (LrInvokeCmp(reinterpret_cast<Il2CppDelegate*>(*delSlot),
                               Slots(AsList(*selfSlot))[i], pivot, kind) < 0)
                ++i;
            while (LrInvokeCmp(reinterpret_cast<Il2CppDelegate*>(*delSlot),
                               Slots(AsList(*selfSlot))[j], pivot, kind) > 0)
                --j;
            if (i <= j)
            {
                Il2CppObject** s = Slots(AsList(*selfSlot));
                Il2CppObject* tmp = s[i];
                s[i] = s[j];
                il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]);
                s[j] = tmp;
                il2cpp_write_barrier(reinterpret_cast<void**>(&s[j]), s[j]);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            LrQuickSortSlots(selfSlot, delSlot, kind, lo, j);
            lo = i;
        }
        else
        {
            LrQuickSortSlots(selfSlot, delSlot, kind, i, hi);
            hi = j;
        }
    }
}

void LrQuickSortStruct(Il2CppObject** selfSlot, Il2CppObject** delSlot,
                       Il2CppListObjStructCmpFn cmp, int32_t lo, int32_t hi)
{
    while (lo < hi)
    {
        Il2CppObject* pivot = Slots(AsList(*selfSlot))[lo + (hi - lo) / 2];
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (cmp(*delSlot, runtime::il2cpp_unbox_user_struct(
                                        Slots(AsList(*selfSlot))[i]),
                       runtime::il2cpp_unbox_user_struct(pivot)) < 0)
                ++i;
            while (cmp(*delSlot, runtime::il2cpp_unbox_user_struct(
                                        Slots(AsList(*selfSlot))[j]),
                       runtime::il2cpp_unbox_user_struct(pivot)) > 0)
                --j;
            if (i <= j)
            {
                Il2CppObject** s = Slots(AsList(*selfSlot));
                Il2CppObject* tmp = s[i];
                s[i] = s[j];
                il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]);
                s[j] = tmp;
                il2cpp_write_barrier(reinterpret_cast<void**>(&s[j]), s[j]);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            LrQuickSortStruct(selfSlot, delSlot, cmp, lo, j);
            lo = i;
        }
        else
        {
            LrQuickSortStruct(selfSlot, delSlot, cmp, i, hi);
            hi = j;
        }
    }
}

} // namespace

void il2cpp_list_obj_sort_comparison(Il2CppObject* self, Il2CppObject* comparison, int32_t kind)
{
    ThrowIfNull(self);
    LrRequireCallback(comparison, "comparison");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&comparison));
    const int32_t n = AsList(self)->count;
    if (n <= 1) return;
    LrQuickSortSlots(&self, &comparison, kind, 0, n - 1);
}

void il2cpp_list_obj_sort_comparison_struct(Il2CppObject* self, Il2CppObject* comparison,
                                            Il2CppListObjStructCmpFn cmp)
{
    ThrowIfNull(self);
    LrRequireCallback(comparison, "comparison");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&comparison));
    const int32_t n = AsList(self)->count;
    if (n <= 1) return;
    LrQuickSortStruct(&self, &comparison, cmp, 0, n - 1);
}

//------------------------------------------------------------------------------
// impl-listrange (B): Sort(index, count, IComparer<T>). The range is validated
// first (index >= 0, count >= 0, index + count <= Count), then the comparer
// domain -- a null / Comparer<T>.Default comparer orders through the family's
// own default ordering (LrCompare), anything else through IComparer.Compare.
//------------------------------------------------------------------------------
namespace {

void LrQuickSortRange(Il2CppObject** selfSlot, Il2CppObject** cmpSlot, int32_t kind,
                      int32_t lo, int32_t hi)
{
    while (lo < hi)
    {
        Il2CppObject* pivot = Slots(AsList(*selfSlot))[lo + (hi - lo) / 2];
        int32_t i = lo;
        int32_t j = hi;
        while (i <= j)
        {
            while (LrCompare(kind, Slots(AsList(*selfSlot))[i], pivot, *cmpSlot) < 0)
                ++i;
            while (LrCompare(kind, Slots(AsList(*selfSlot))[j], pivot, *cmpSlot) > 0)
                --j;
            if (i <= j)
            {
                Il2CppObject** s = Slots(AsList(*selfSlot));
                Il2CppObject* tmp = s[i];
                s[i] = s[j];
                il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]);
                s[j] = tmp;
                il2cpp_write_barrier(reinterpret_cast<void**>(&s[j]), s[j]);
                ++i;
                --j;
            }
        }
        if (j - lo < hi - i)
        {
            LrQuickSortRange(selfSlot, cmpSlot, kind, lo, j);
            lo = i;
        }
        else
        {
            LrQuickSortRange(selfSlot, cmpSlot, kind, i, hi);
            hi = j;
        }
    }
}

} // namespace

void il2cpp_list_obj_sort_range_comparer(Il2CppObject* self, int32_t index, int32_t count,
                                         Il2CppObject* comparer, int32_t kind)
{
    ThrowIfNull(self);
    if (index < 0)
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    if (count < 0)
        il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
    Il2CppListObj* l = AsList(self);
    if (static_cast<int64_t>(index) + static_cast<int64_t>(count) > l->count)
        il2cpp_throw_argument_exception(kLrOffsetMessage);
    if (count <= 1) return;
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&comparer));
    LrQuickSortRange(&self, &comparer, kind, index, index + count - 1);
}

//------------------------------------------------------------------------------
// USER-STRUCT / TUPLE element forms. The element is a BOX whose payload is the
// struct value, and the generated delegate leaf takes the struct BY VALUE, so
// the payload address is handed to the caller-supplied invoker (the header
// templates instantiate it with the concrete C++ type).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_list_obj_find_struct_core(Il2CppObject* self, Il2CppObject* match,
                                               Il2CppListObjStructPredFn pred, int32_t fromEnd)
{
    ThrowIfNull(self);
    LrRequireCallback(match, "match");
    const int32_t n = AsList(self)->count;
    if (fromEnd != 0)
    {
        for (int32_t i = n - 1; i >= 0; --i)
        {
            Il2CppObject* box = Slots(AsList(self))[i];
            if (pred(match, runtime::il2cpp_unbox_user_struct(box), i)) return box;
        }
    }
    else
    {
        for (int32_t i = 0; i < n; ++i)
        {
            Il2CppObject* box = Slots(AsList(self))[i];
            if (pred(match, runtime::il2cpp_unbox_user_struct(box), i)) return box;
        }
    }
    return nullptr;
}

int32_t il2cpp_list_obj_scan_struct_core(Il2CppObject* self, Il2CppObject* match,
                                         Il2CppListObjStructPredFn pred, int32_t mode)
{
    ThrowIfNull(self);
    LrRequireCallback(match, "match");
    const int32_t n = AsList(self)->count;
    if (mode == IL2CPP_LIST_OBJ_SCAN_REMOVE_ALL)
    {
        int32_t wi = 0;
        for (int32_t i = 0; i < n; ++i)
        {
            Il2CppObject* box = Slots(AsList(self))[i];
            if (pred(match, runtime::il2cpp_unbox_user_struct(box), i)) continue;
            Il2CppObject** s = Slots(AsList(self));
            if (wi != i)
            {
                s[wi] = box;
                il2cpp_write_barrier(reinterpret_cast<void**>(&s[wi]), s[wi]);
            }
            ++wi;
        }
        Il2CppListObj* l = AsList(self);
        Il2CppObject** s = Slots(l);
        for (int32_t i = wi; i < l->count; ++i)
        {
            s[i] = nullptr;
            il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), nullptr);
        }
        const int32_t removed = l->count - wi;
        l->count = wi;
        return removed;
    }
    if (mode == IL2CPP_LIST_OBJ_SCAN_FIND_LAST_INDEX)
    {
        for (int32_t i = n - 1; i >= 0; --i)
        {
            if (pred(match, runtime::il2cpp_unbox_user_struct(Slots(AsList(self))[i]), i))
                return i;
        }
        return -1;
    }
    if (mode == IL2CPP_LIST_OBJ_SCAN_TRUE_FOR_ALL)
    {
        for (int32_t i = 0; i < n; ++i)
        {
            if (!pred(match, runtime::il2cpp_unbox_user_struct(Slots(AsList(self))[i]), i))
                return 0;
        }
        return 1;
    }
    if (mode == IL2CPP_LIST_OBJ_SCAN_EXISTS)
    {
        for (int32_t i = 0; i < n; ++i)
        {
            if (pred(match, runtime::il2cpp_unbox_user_struct(Slots(AsList(self))[i]), i))
                return 1;
        }
        return 0;
    }
    for (int32_t i = 0; i < n; ++i)
    {
        if (pred(match, runtime::il2cpp_unbox_user_struct(Slots(AsList(self))[i]), i)) return i;
    }
    return -1;
}

Il2CppObject* il2cpp_list_obj_find_all_struct_core(Il2CppObject* self, Il2CppObject* match,
                                                  Il2CppListObjStructPredFn pred)
{
    ThrowIfNull(self);
    LrRequireCallback(match, "match");
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    const int32_t n = AsList(self)->count;
    Il2CppObject* result = il2cpp_list_obj_create_with_capacity(n);
    roots.Add(reinterpret_cast<void**>(&result));
    int32_t wi = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* box = Slots(AsList(self))[i];
        if (!pred(match, runtime::il2cpp_unbox_user_struct(box), i)) continue;
        Il2CppObject** out = Slots(AsList(result));
        out[wi] = box;
        il2cpp_write_barrier(reinterpret_cast<void**>(&out[wi]), out[wi]);
        ++wi;
    }
    AsList(result)->count = wi;
    return result;
}

void il2cpp_list_obj_for_each_struct_core(Il2CppObject* self, Il2CppObject* action,
                                          Il2CppListObjStructActionFn fn)
{
    ThrowIfNull(self);
    LrRequireCallback(action, "action");
    const int32_t n = AsList(self)->count;
    for (int32_t i = 0; i < n; ++i)
    {
        fn(action, runtime::il2cpp_unbox_user_struct(Slots(AsList(self))[i]), i);
    }
}

void il2cpp_list_obj_add_range_struct(Il2CppObject* self, Il2CppObject* collection,
                                      Il2CppClass* klass, int32_t payloadSize)
{
    ThrowIfNull(self);
    il2cpp_list_obj_insert_range_struct(self, AsList(self)->count, collection, klass, payloadSize);
}

void il2cpp_list_obj_insert_range_struct(Il2CppObject* self, int32_t index,
                                         Il2CppObject* collection, Il2CppClass* klass,
                                         int32_t payloadSize)
{
    ThrowIfNull(self);
    if (collection == nullptr)
        il2cpp_throw_argument_null_exception("collection");
    ThrowIns(index, AsList(self)->count);
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    roots.Add(reinterpret_cast<void**>(&collection));
    if (il2cpp_is_list_obj(collection))
    {
        int32_t n = AsList(collection)->count;
        Il2CppArray* snap = reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array(collection));
        roots.Add(reinterpret_cast<void**>(&snap));
        Il2CppObject** src = reinterpret_cast<Il2CppObject**>(snap->data);
        int32_t pos = index;
        for (int32_t i = 0; i < n; ++i)
        {
            il2cpp_list_obj_insert(self, pos, src[i]);
            ++pos;
        }
        return;
    }
    if (collection->klass != nullptr && collection->klass->name != nullptr
        && collection->klass->name[0] == '[')
    {
        Il2CppArray* src = reinterpret_cast<Il2CppArray*>(collection);
        roots.Add(reinterpret_cast<void**>(&src));
        const int32_t len = src->length;
        if (src->elemSize != payloadSize)
            il2cpp_throw_argument_exception(
                "The source array element type does not match the list element type.");
        int32_t pos = index;
        for (int32_t i = 0; i < len; ++i)
        {
            il2cpp_list_obj_insert(
                self, pos,
                runtime::icall_box_user_struct(
                    klass, il2cpp_array_get_elem_ptr(src, i),
                    static_cast<size_t>(payloadSize)));
            ++pos;
        }
        return;
    }
    il2cpp_throw_not_supported_exception(
        "List<T>.AddRange/InsertRange over a struct element: only a List<T> or a T[] "
        "source is supported (the runtime has no boxed user-struct enumerator carrier).");
}

// The struct ElementEquals: the box payloads are compared with memcmp when the
// payload sizes match. A user struct's net48 Equals is its own override; the
// runtime has no reflected slot here, and a value-type box compare is
// memcmp-equivalent for a POD payload. Registered as a known limitation in
// docs/audit/b5-gaps.md (IndexCustom: only value-identity via memcmp).
int32_t il2cpp_list_obj_index_of_struct_ex(Il2CppObject* self, const void* value,
                                           Il2CppClass* klass, int32_t payloadSize,
                                           int32_t start, int32_t count)
{
    ThrowIfNull(self);
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppObject* box =
        runtime::icall_box_user_struct(klass, value, static_cast<size_t>(payloadSize));
    roots.Add(reinterpret_cast<void**>(&box));
    const int32_t n = AsList(self)->count;
    if (start < 0 || start > n)
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    if (count == kLrToBound)
    {
        count = n - start;
    }
    else
    {
        if (count < 0)
            il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
        if (static_cast<int64_t>(start) + static_cast<int64_t>(count) > n)
            il2cpp_throw_argument_exception(kLrOffsetMessage);
    }
    for (int32_t i = start; i < start + count; ++i)
    {
        Il2CppObject* cur = Slots(AsList(self))[i];
        if (cur == box) return i;
        if (cur != nullptr && std::memcmp(runtime::il2cpp_unbox_user_struct(cur), value,
                                         static_cast<size_t>(payloadSize)) == 0)
            return i;
        // A user struct that OVERRIDES Equals makes EqualityComparer<T>.Default
        // an ObjectEqualityComparer<T> (it calls the override), not
        // ValueType.Equals -- so a byte-difference is not conclusive. Fall back
        // to the runtime's equality authority, which dispatches a reflected
        // override (icall_object_equals -> TryReflectedEqualsDispatch) and only
        // reports false when the struct has none.
        if (cur != nullptr && runtime::icall_object_equals(cur, box)) return i;
    }
    return -1;
}

int32_t il2cpp_list_obj_last_index_of_struct_ex(Il2CppObject* self, const void* value,
                                                Il2CppClass* klass, int32_t payloadSize,
                                                int32_t index, int32_t count)
{
    ThrowIfNull(self);
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppObject* box =
        runtime::icall_box_user_struct(klass, value, static_cast<size_t>(payloadSize));
    roots.Add(reinterpret_cast<void**>(&box));
    const int32_t n = AsList(self)->count;
    int32_t hi;
    int32_t lo;
    if (index == kLrToBound && count == kLrToBound)
    {
        // The 0-argument overload: net48 LastIndexOf(item) returns -1 for an
        // EMPTY list before validating anything.
        if (n == 0) return -1;
        hi = n - 1;
        lo = 0;
    }
    else
    {
        if (index < 0 || index >= n)
            il2cpp_throw_argument_out_of_range_exception("index", kIndexOutOfRangeMessage);
        if (count == kLrToBound)
        {
            count = index + 1;
        }
        else
        {
            if (count < 0)
                il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
            if (count > index + 1)
                il2cpp_throw_argument_out_of_range_exception("count", kIndexOutOfRangeMessage);
        }
        hi = index;
        lo = index - count + 1;
    }
    for (int32_t i = hi; i >= lo; --i)
    {
        Il2CppObject* cur = Slots(AsList(self))[i];
        if (cur == box) return i;
        if (cur != nullptr && std::memcmp(runtime::il2cpp_unbox_user_struct(cur), value,
                                         static_cast<size_t>(payloadSize)) == 0)
            return i;
    }
    return -1;
}

int32_t il2cpp_list_obj_binary_search_struct_ex(Il2CppObject* self, const void* value,
                                                Il2CppClass* klass, int32_t payloadSize,
                                                Il2CppObject* comparer, int32_t start,
                                                int32_t count)
{
    ThrowIfNull(self);
    LrRoots roots;
    roots.Add(reinterpret_cast<void**>(&self));
    Il2CppObject* box =
        runtime::icall_box_user_struct(klass, value, static_cast<size_t>(payloadSize));
    roots.Add(reinterpret_cast<void**>(&box));
    roots.Add(reinterpret_cast<void**>(&comparer));
    const int32_t n = AsList(self)->count;
    if (start < 0)
        il2cpp_throw_argument_out_of_range_exception("index", kNonNegativeMessage);
    if (count == kLrToBound)
    {
        count = n - start;
    }
    else
    {
        if (count < 0)
            il2cpp_throw_argument_out_of_range_exception("count", kNonNegativeMessage);
        if (static_cast<int64_t>(start) + static_cast<int64_t>(count) > n)
            il2cpp_throw_argument_exception(kLrOffsetMessage);
    }
    int32_t lo = start;
    int32_t hi = start + count - 1;
    while (lo <= hi)
    {
        const int32_t mid = lo + ((hi - lo) >> 1);
        const int32_t cmp = LrCompare(kLrRef, Slots(AsList(self))[mid], box, comparer);
        if (cmp == 0) return mid;
        if (cmp < 0)
            lo = mid + 1;
        else
            hi = mid - 1;
    }
    return ~lo;
}

} // namespace il2cpp
