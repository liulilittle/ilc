#include "listobj.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_runtime.h"
#include "il2cpp_gc.h"
#include <cstddef>
#include <cstdint>

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
class RootScope{ public: RootScope():c(0){} ~RootScope(){ while(c>0) il2cpp_gc_remove_static_root(s[--c]); } void Add(void** p){ il2cpp_gc_add_static_root(p); s[c++]=p; } private: void** s[4]; int c; };
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
void il2cpp_list_obj_add(Il2CppObject* self, Il2CppObject* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); EnsureItems(&self); Il2CppListObj* l=AsList(self); if(l->count==l->items->length) Grow(&self); l=AsList(self); Slots(l)[l->count]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&Slots(l)[l->count]), v); ++l->count; }
void il2cpp_list_obj_insert(Il2CppObject* self,int32_t idx,Il2CppObject* v){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); ThrowIns(idx,l->count); EnsureItems(&self); l=AsList(self); if(l->count==l->items->length) Grow(&self); l=AsList(self); Il2CppObject** s=Slots(l); for(int32_t i=l->count;i>idx;--i){ s[i]=s[i-1]; il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]); } s[idx]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&s[idx]), v); ++l->count; }
void il2cpp_list_obj_remove_at(Il2CppObject* self,int32_t idx){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); Il2CppObject** s=Slots(l); for(int32_t i=idx;i<l->count-1;++i){ s[i]=s[i+1]; il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), s[i]); } s[l->count-1]=nullptr; il2cpp_write_barrier(reinterpret_cast<void**>(&s[l->count-1]), nullptr); --l->count; }
bool il2cpp_list_obj_remove(Il2CppObject* self,Il2CppObject* v){ ThrowIfNull(self); int32_t idx=il2cpp_list_obj_index_of(self,v); if(idx<0) return false; il2cpp_list_obj_remove_at(self,idx); return true; }
Il2CppObject* il2cpp_list_obj_get_item(Il2CppObject* self,int32_t idx){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); return Slots(l)[idx]; }
void il2cpp_list_obj_set_item(Il2CppObject* self,int32_t idx,Il2CppObject* v){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); ThrowElem(idx,l->count); Slots(l)[idx]=v; il2cpp_write_barrier(reinterpret_cast<void**>(&Slots(l)[idx]), v); }
int32_t il2cpp_list_obj_get_count(Il2CppObject* self){ ThrowIfNull(self); return AsList(self)->count; }
void il2cpp_list_obj_clear(Il2CppObject* self){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); if(l->items){ Il2CppObject** s=Slots(l); for(int32_t i=0;i<l->count;++i){ s[i]=nullptr; il2cpp_write_barrier(reinterpret_cast<void**>(&s[i]), nullptr); } } l->count=0; }
int32_t il2cpp_list_obj_index_of(Il2CppObject* self,Il2CppObject* v){ ThrowIfNull(self); Il2CppListObj* l=AsList(self); if(l->items){ Il2CppObject** s=Slots(l); for(int32_t i=0;i<l->count;++i) if(s[i]==v) return i; } return -1; }
bool il2cpp_list_obj_contains(Il2CppObject* self,Il2CppObject* v){ return il2cpp_list_obj_index_of(self,v)>=0; }
Il2CppObject* il2cpp_list_obj_to_array(Il2CppObject* self){ ThrowIfNull(self); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* r=AllocItems(l->count); l=AsList(self); Il2CppObject** dst=reinterpret_cast<Il2CppObject**>(r->data); Il2CppObject** src=l->items?Slots(l):nullptr; for(int32_t i=0;i<l->count;++i){ dst[i]=src?src[i]:nullptr; il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]); } return reinterpret_cast<Il2CppObject*>(r); }
Il2CppObject* il2cpp_list_obj_get_enumerator(Il2CppObject* self){ ThrowIfNull(self); EnsureRegistered(); il2cpp_class_register(&System_Collections_SortedList_Enumerator_class); RootScope rs; rs.Add(reinterpret_cast<void**>(&self)); Il2CppListObj* l=AsList(self); Il2CppArray* snap=AllocItems(l->count); l=AsList(self); if(l->count>0&&l->items){ Il2CppObject** src=Slots(l); Il2CppObject** dst=reinterpret_cast<Il2CppObject**>(snap->data); for(int32_t i=0;i<l->count;++i){ dst[i]=src[i]; il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]); } } Il2CppArrayEnumerator* en=reinterpret_cast<Il2CppArrayEnumerator*>(il2cpp_object_new(&System_Collections_SortedList_Enumerator_class)); en->array=snap; il2cpp_write_barrier(reinterpret_cast<void**>(&en->array), reinterpret_cast<Il2CppObject*>(snap)); en->index=-1; return reinterpret_cast<Il2CppObject*>(en); }
void il2cpp_list_obj_sort(Il2CppObject* self){ (void)self; }
void il2cpp_list_obj_copy_to(Il2CppObject* self,Il2CppObject* arr,int32_t idx){ ThrowIfNull(self); if(!arr) il2cpp_throw_argument_null_exception("array"); Il2CppArray* d=reinterpret_cast<Il2CppArray*>(arr); if(idx<0) il2cpp_throw_argument_out_of_range_exception("arrayIndex",kNonNegativeMessage); Il2CppListObj* l=AsList(self); if(static_cast<int64_t>(idx)+l->count > d->length) il2cpp_throw_argument_exception("Destination array was not long enough. Check destIndex and length, and the array's lower bounds."); if(l->count>0&&l->items){ Il2CppObject** src=Slots(l); Il2CppObject** dst=reinterpret_cast<Il2CppObject**>(d->data); for(int32_t i=0;i<l->count;++i){ dst[idx+i]=src[i]; il2cpp_write_barrier(reinterpret_cast<void**>(&dst[idx+i]), src[i]); } } }
int32_t il2cpp_list_obj_enumerator_move_next(Il2CppObject* self){ ThrowIfNull(self); Il2CppArrayEnumerator* en=reinterpret_cast<Il2CppArrayEnumerator*>(self); ++en->index; return en->index < en->array->length ? 1 : 0; }
Il2CppObject* il2cpp_list_obj_enumerator_get_current(Il2CppObject* self){ ThrowIfNull(self); Il2CppArrayEnumerator* en=reinterpret_cast<Il2CppArrayEnumerator*>(self); return reinterpret_cast<Il2CppObject**>(en->array->data)[en->index]; }
void il2cpp_list_obj_enumerator_reset(Il2CppObject* self){ ThrowIfNull(self); reinterpret_cast<Il2CppArrayEnumerator*>(self)->index=-1; }
void il2cpp_list_obj_enumerator_dispose(Il2CppObject* self){ (void)self; }
} // namespace il2cpp
