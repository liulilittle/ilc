#pragma once
// listobj.h -- managed System.Collections.Generic.List<object> runtime
// Generic object List fallback for List<T> where T is reference type.
// Layout mirrors listi4 but element array is reference array (elemSize 8, isRef true)
// and element ops use object pointer semantics with write barriers.

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "sortedlist.h"
#include <cstdint>

namespace il2cpp {

struct Il2CppListObj
{
    Il2CppObject obj;
    Il2CppArray* items;
    int32_t count;
};

Il2CppClass* il2cpp_list_obj_class();
Il2CppObject* il2cpp_list_obj_create();
Il2CppObject* il2cpp_list_obj_create_with_capacity(int32_t capacity);
void il2cpp_list_obj_add(Il2CppObject* self, Il2CppObject* value);
void il2cpp_list_obj_insert(Il2CppObject* self, int32_t index, Il2CppObject* value);
void il2cpp_list_obj_remove_at(Il2CppObject* self, int32_t index);
bool il2cpp_list_obj_remove(Il2CppObject* self, Il2CppObject* value);
Il2CppObject* il2cpp_list_obj_get_item(Il2CppObject* self, int32_t index);
void il2cpp_list_obj_set_item(Il2CppObject* self, int32_t index, Il2CppObject* value);
int32_t il2cpp_list_obj_get_count(Il2CppObject* self);
void il2cpp_list_obj_clear(Il2CppObject* self);
int32_t il2cpp_list_obj_index_of(Il2CppObject* self, Il2CppObject* value);
bool il2cpp_list_obj_contains(Il2CppObject* self, Il2CppObject* value);
Il2CppObject* il2cpp_list_obj_to_array(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_get_enumerator(Il2CppObject* self);
void il2cpp_list_obj_sort(Il2CppObject* self);
void il2cpp_list_obj_copy_to(Il2CppObject* self, Il2CppObject* array, int32_t arrayIndex);
int32_t il2cpp_list_obj_enumerator_move_next(Il2CppObject* self);
Il2CppObject* il2cpp_list_obj_enumerator_get_current(Il2CppObject* self);
void il2cpp_list_obj_enumerator_reset(Il2CppObject* self);
void il2cpp_list_obj_enumerator_dispose(Il2CppObject* self);

} // namespace il2cpp
