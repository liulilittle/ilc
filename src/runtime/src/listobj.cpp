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
int32_t il2cpp_list_obj_index_of_dt(Il2CppObject* self,runtime::Il2CppDateTime v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_long(runtime::il2cpp_datetime_to_array_bits(v)); return il2cpp_list_obj_index_of(self,b); }
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
int32_t il2cpp_list_obj_index_of_struct(Il2CppObject* self,const void* v,Il2CppClass* klass,int32_t payloadSize){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppObject* b=runtime::icall_box_user_struct(klass,v,static_cast<size_t>(payloadSize)); return il2cpp_list_obj_index_of(self,b); }
bool il2cpp_list_obj_contains_struct(Il2CppObject* self,const void* v,Il2CppClass* klass,int32_t payloadSize){ return il2cpp_list_obj_index_of_struct(self,v,klass,payloadSize)>=0; }
bool il2cpp_list_obj_remove_struct(Il2CppObject* self,const void* v,Il2CppClass* klass,int32_t payloadSize){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of_struct(self,v,klass,payloadSize); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
void il2cpp_list_obj_get_item_struct(void* result,Il2CppObject* self,int32_t idx,int32_t payloadSize){ const void* p=runtime::il2cpp_unbox_user_struct(il2cpp_list_obj_get_item(self,idx)); std::memcpy(result,p,static_cast<size_t>(payloadSize)); }
void il2cpp_list_obj_enumerator_get_current_struct(void* result,Il2CppObject* self,int32_t payloadSize){ const void* p=runtime::il2cpp_unbox_user_struct(il2cpp_list_obj_enumerator_get_current(self)); std::memcpy(result,p,static_cast<size_t>(payloadSize)); }
Il2CppObject* il2cpp_list_obj_to_array_struct(Il2CppObject* self,int32_t elemSize){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=il2cpp_array_new(nullptr,elemSize,false,l->count); rs.Add(reinterpret_cast<void**>(&r)); l=AsList(self); char* dst=reinterpret_cast<char*>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i){ if(src){ const void* p=runtime::il2cpp_unbox_user_struct(src[i]); std::memcpy(dst+static_cast<size_t>(i)*static_cast<size_t>(elemSize),p,static_cast<size_t>(elemSize)); } else { std::memset(dst+static_cast<size_t>(i)*static_cast<size_t>(elemSize),0,static_cast<size_t>(elemSize)); } } return reinterpret_cast<Il2CppObject*>(r); }
bool il2cpp_is_list_obj(Il2CppObject* obj){ return obj != nullptr && obj->klass == &g_listObjClass; }
} // namespace il2cpp
