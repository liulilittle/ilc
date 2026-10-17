//------------------------------------------------------------------------------
// regex_obj.cpp -- System.Text.RegularExpressions Match/Group/GroupCollection
// object model (OBJ-1, see regex_obj.h). Builds the managed object graph for
// the static Regex.Match result and owns the property icalls.
//
// GC SAFETY: the builder allocates through the GC (strings, the Group[] array,
// the Group/GroupCollection/Match objects); the moving collector may relocate
// them at every allocation, so in-flight objects are pinned with temporary
// static roots (il2cpp_gc_add_static_root / remove -- the regex_api.cpp
// RootScope precedent). The descriptors carry a refFieldBitmap so the
// collector scans the objects' reference fields once they are linked.
//------------------------------------------------------------------------------

#include "regex_obj.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_out_of_range_exception
#include "il2cpp_gc.h"            // il2cpp_gc_add_static_root / remove
#include "il2cpp_runtime.h"       // il2cpp_class_register, il2cpp_object_new

#include <cstdint>
#include <vector>

//------------------------------------------------------------------------------
// Class descriptors (GLOBAL scope -- the transpiler emits
// &System_Text_RegularExpressions_*_class for any isinst/castclass). Layouts
// mirror the structs in regex_obj.h: the GC reference fields sit in slots
// 1..k (bit i marks the field at offset i*sizeof(void*)), so the bitmap is
// pointer-size independent (the dictsi.h convention).
//   Match           : input (slot 1), groups (slot 2)    -> 0b110 = 0x06, 2 refs
//   Group           : value (slot 1), name (slot 2)       -> 0b110 = 0x06, 2 refs
//   GroupCollection : items (slot 1)                      -> 0b010 = 0x02, 1 ref
//------------------------------------------------------------------------------
#define REGEX_OBJ_CLASS(SYM, DOTNAME, LAYOUT, BITMAP, REFCOUNT) \
    ::il2cpp::Il2CppClass SYM = {                              \
        DOTNAME,                                              \
        static_cast<int32_t>(sizeof(::il2cpp::runtime::LAYOUT)), \
        BITMAP,                                               \
        REFCOUNT,                                             \
        &System_Object_class,                                 \
        nullptr, 0, nullptr, 0,                               \
    }

REGEX_OBJ_CLASS(System_Text_RegularExpressions_Match_class,
                "System.Text.RegularExpressions.Match",
                Il2CppRegexMatch, 0x06u, 2);
REGEX_OBJ_CLASS(System_Text_RegularExpressions_Group_class,
                "System.Text.RegularExpressions.Group",
                Il2CppRegexGroup, 0x06u, 2);
REGEX_OBJ_CLASS(System_Text_RegularExpressions_GroupCollection_class,
                "System.Text.RegularExpressions.GroupCollection",
                Il2CppRegexGroupCollection, 0x02u, 1);

#undef REGEX_OBJ_CLASS

namespace il2cpp { namespace runtime {

namespace {

bool g_regexObjClassesRegistered = false;

// Temporary GC root set (rewritten by the collector when an object moves;
// RAII removal so a throwing allocation cannot leak a root slot).
class RootScope
{
public:
    ~RootScope()
    {
        for (size_t i = 0; i < m_slots.size(); ++i)
        {
            il2cpp_gc_remove_static_root(m_slots[i]);
        }
    }
    void Add(void** slot)
    {
        il2cpp_gc_add_static_root(slot);
        m_slots.push_back(slot);
    }
    void Remove(void** slot)
    {
        for (size_t i = m_slots.size(); i > 0; --i)
        {
            if (m_slots[i - 1] == slot)
            {
                il2cpp_gc_remove_static_root(slot);
                m_slots.erase(m_slots.begin() + static_cast<ptrdiff_t>(i - 1));
                return;
            }
        }
    }

private:
    std::vector<void**> m_slots;
};

// Non-negative integer -> decimal char16_t text (group names are "0".."N").
int32_t FormatGroupName(int32_t value, char16_t* out)
{
    if (value <= 0)
    {
        out[0] = u'0';
        return 1;
    }
    char16_t tmp[12];
    int32_t n = 0;
    int32_t v = value;
    while (v > 0)
    {
        tmp[n++] = static_cast<char16_t>(u'0' + (v % 10));
        v /= 10;
    }
    for (int32_t i = 0; i < n; ++i)
    {
        out[i] = tmp[n - 1 - i];
    }
    return n;
}

} // namespace

void il2cpp_regex_obj_register_classes()
{
    if (g_regexObjClassesRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Text_RegularExpressions_Match_class);
    il2cpp_class_register(&System_Text_RegularExpressions_Group_class);
    il2cpp_class_register(&System_Text_RegularExpressions_GroupCollection_class);
    g_regexObjClassesRegistered = true;
}

//------------------------------------------------------------------------------
// Match construction (il2cpp_regex_match result).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_regex_build_match(const char16_t* input, int32_t inputLength,
                                       const MatchResult& result, int32_t groupCount)
{
    il2cpp_regex_obj_register_classes();

    const int32_t total = groupCount + 1; // item 0 = whole match
    RootScope roots;

    Il2CppString* inputStr = il2cpp_string_new(input, inputLength);
    roots.Add(reinterpret_cast<void**>(&inputStr));

    Il2CppArray* items = il2cpp_array_new(&System_Text_RegularExpressions_Group_class,
                                          static_cast<int32_t>(sizeof(void*)), true, total);
    roots.Add(reinterpret_cast<void**>(&items));

    for (int32_t i = 0; i < total; ++i)
    {
        int32_t gIndex;
        int32_t gLength;
        int32_t gSuccess;
        if (i == 0)
        {
            gSuccess = result.success ? 1 : 0;
            gIndex = result.success ? result.start : 0;
            gLength = result.success ? result.matchedLength : 0;
        }
        else
        {
            const RegexGroupCapture* g =
                (i - 1 < static_cast<int32_t>(result.groups.size())) ? &result.groups[i - 1] : nullptr;
            if (g != nullptr && g->start >= 0)
            {
                gSuccess = 1;
                gIndex = g->start;
                gLength = g->length;
            }
            else
            {
                gSuccess = 0;
                gIndex = 0;
                gLength = 0;
            }
        }

        Il2CppRegexGroup* grp =
            reinterpret_cast<Il2CppRegexGroup*>(il2cpp_object_new(&System_Text_RegularExpressions_Group_class));
        roots.Add(reinterpret_cast<void**>(&grp));
        grp->index = gIndex;
        grp->length = gLength;
        grp->success = gSuccess;
        // Non-participating groups carry an empty value (net48 Group.Value == "").
        grp->value = il2cpp_string_new(input + gIndex, gLength);
        char16_t nameBuf[12];
        const int32_t nameLen = FormatGroupName(i, nameBuf);
        grp->name = il2cpp_string_new(nameBuf, nameLen);

        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(items, i));
        *slot = reinterpret_cast<Il2CppObject*>(grp);
        roots.Remove(reinterpret_cast<void**>(&grp));
    }

    Il2CppRegexGroupCollection* gc = reinterpret_cast<Il2CppRegexGroupCollection*>(
        il2cpp_object_new(&System_Text_RegularExpressions_GroupCollection_class));
    roots.Add(reinterpret_cast<void**>(&gc));
    gc->count = total;
    gc->items = items;

    Il2CppRegexMatch* match =
        reinterpret_cast<Il2CppRegexMatch*>(il2cpp_object_new(&System_Text_RegularExpressions_Match_class));
    match->input = inputStr;
    match->index = result.success ? result.start : 0;
    match->length = result.success ? result.matchedLength : 0;
    match->success = result.success ? 1 : 0;
    match->groups = reinterpret_cast<Il2CppObject*>(gc);
    return reinterpret_cast<Il2CppObject*>(match);
}

//------------------------------------------------------------------------------
// Match property icalls.
//------------------------------------------------------------------------------
bool icall_regex_match_get_success(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexMatch*>(self)->success != 0;
}

int32_t icall_regex_match_get_index(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexMatch*>(self)->index;
}

int32_t icall_regex_match_get_length(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexMatch*>(self)->length;
}

Il2CppString* icall_regex_match_get_value(Il2CppObject* self)
{
    Il2CppRegexMatch* m = reinterpret_cast<Il2CppRegexMatch*>(self);
    return il2cpp_string_new(m->input->chars + m->index, m->length);
}

Il2CppString* icall_regex_match_to_string(Il2CppObject* self)
{
    return icall_regex_match_get_value(self);
}

Il2CppObject* icall_regex_match_get_groups(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexMatch*>(self)->groups;
}

//------------------------------------------------------------------------------
// Group property icalls.
//------------------------------------------------------------------------------
bool icall_regex_group_get_success(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexGroup*>(self)->success != 0;
}

int32_t icall_regex_group_get_index(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexGroup*>(self)->index;
}

int32_t icall_regex_group_get_length(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexGroup*>(self)->length;
}

Il2CppString* icall_regex_group_get_value(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexGroup*>(self)->value;
}

Il2CppString* icall_regex_group_get_name(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexGroup*>(self)->name;
}

//------------------------------------------------------------------------------
// GroupCollection property icalls.
//------------------------------------------------------------------------------
int32_t icall_regex_gc_get_count(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppRegexGroupCollection*>(self)->count;
}

Il2CppObject* icall_regex_gc_get_item_int(Il2CppObject* self, int32_t index)
{
    Il2CppRegexGroupCollection* gc = reinterpret_cast<Il2CppRegexGroupCollection*>(self);
    if (index < 0 || index >= gc->count)
    {
        // net48 GroupCollection[int] out-of-range -> ArgumentOutOfRangeException.
        il2cpp_throw_argument_out_of_range_exception(
            "groupnum", "Index was out of range. Must be non-negative and less than the size of the collection.");
    }
    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(gc->items, index));
    return *slot;
}

}} // namespace il2cpp::runtime
