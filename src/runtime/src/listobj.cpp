#include "listobj.h"
#include "arraysort.h" // il2cpp_array_sort_icomparer (List.Sort default comparer)
#include "il2cpp_exception_ext.h"
#include "il2cpp_runtime.h"
#include "il2cpp_gc.h"
#include "il2cpp_value_semantics.h" // runtime::icall_object_equals (IndexOf/Remove equality)
#include "il2cpp_boxing_ext2.h" // runtime::icall_box_long / icall_unbox_long (G4 Int64 domain)
#include "il2cpp_boxing_ext.h" // runtime::icall_box_double / icall_unbox_double (G4 Double domain)
#include <cstddef>
#include <cstdint>
#include <cstdlib>

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
int32_t NextLen(int32_t oldLen){ int32_t n=oldLen*2; if(n<oldLen||n>kMaxItems) n=oldLen; return n; }
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
void il2cpp_list_obj_add(Il2CppObject* self, Il2CppObject* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); /* nb9 M-7: Grow allocates -- root the element too */ rs.Add(reinterpret_cast<void**>(&v)); EnsureItems(&self); Il2CppListObj* l=AsList(self); if(l->count==l->items->length) Grow(&self); l=AsList(self); Slots(l)[l->count]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&Slots(l)[l->count]), v); ++l->count; }
void il2cpp_list_obj_insert(Il2CppObject* self,int32_t idx,Il2CppObject* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); /* nb9 M-7: Grow allocates -- root the element too */ rs.Add(reinterpret_cast<void**>(&v)); Il2CppListObj* l=AsList(self); ThrowIns(idx,l->count); EnsureItems(&self); l=AsList(self); if(l->count==l->items->length) Grow(&self); l=AsList(self); Il2CppObject** s=Slots(l); for(int32_t i=l->count;i>idx;--i){ s[i]=s[i-1]; il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]); } s[idx]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&s[idx]), v); ++l->count; }
void il2cpp_list_obj_remove_at(Il2CppObject* self,int32_t idx){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); Il2CppObject** s=Slots(l); for(int32_t i=idx;i<l->count-1;++i){ s[i]=s[i+1]; il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]); } s[l->count-1]=nullptr; il2cpp_write_barrier(reinterpret_cast<void**>(&s[l->count-1]), nullptr); --l->count; }
bool il2cpp_list_obj_remove(Il2CppObject* self,Il2CppObject* v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
Il2CppObject* il2cpp_list_obj_get_item(Il2CppObject* self,int32_t idx){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); return Slots(l)[idx]; }
void il2cpp_list_obj_set_item(Il2CppObject* self,int32_t idx,Il2CppObject* v){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); Slots(l)[idx]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&Slots(l)[idx]), v); }
int32_t il2cpp_list_obj_get_count(Il2CppObject* self){ ThrowIfNull(self); return AsList(self)->count; }
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
bool il2cpp_is_list_obj(Il2CppObject* obj){ return obj != nullptr && obj->klass == &g_listObjClass; }
} // namespace il2cpp
