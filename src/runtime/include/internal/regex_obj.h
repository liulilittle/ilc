#pragma once
//------------------------------------------------------------------------------
// regex_obj.h -- System.Text.RegularExpressions Match/Group/GroupCollection
// object model (OBJ-1): the managed object graph returned by the static
// Regex.Match entry point (regex_api.cpp il2cpp_regex_match) -- replacing the
// legacy string[] result shape (regex.h v1 documented simplification).
//
// OBJECT MODEL (the Il2CppObject header + GC reference fields contract, see
// il2cpp_metadata.h refFieldBitmap):
//
//   Match            : header + input (string ref) + index/length/success +
//                      Groups (GroupCollection ref).
//   Group            : header + index/length/success + value (string ref) +
//                      Name (string ref). Group 0 = the whole match; group i
//                      (i>=1) mirrors MatchResult.groups[i-1] (index -1 means
//                      the group did not participate -> Success false, 0/0/"").
//   GroupCollection  : header + Count + items (Group[] reference array).
//
// The three descriptors are GLOBAL-scope Il2CppClass statics (the sync_lite /
// stopwatch contract) so the GC can scan the objects' reference fields by the
// refFieldBitmap and the runtime can allocate through il2cpp_object_new.
// There is NO public ctor in .NET (Regex.Match is the only factory), so the
// transpiler emits no newobj allocation for these types -- the descriptors are
// registered lazily by the builder below.
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48). Documented subset: named
// groups / GroupCollection[string] / MatchCollection (Regex.Matches) are NOT
// in this batch (OBJ-2/OBJ-3); the whole-match and numbered unnamed groups are
// byte-exact.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)
#include "il2cpp_string.h"   // Il2CppString
#include "il2cpp_array.h"    // Il2CppArray (Group[] items)
#include "regex_match.h"     // MatchResult (builder input)

#include <cstdint>

// System.Text.RegularExpressions runtime descriptors (GLOBAL scope -- the
// transpiler's isinst/castclass/newobj fallback emits
// &System_Text_RegularExpressions_*_class; defined in regex_obj.cpp).
extern il2cpp::Il2CppClass System_Text_RegularExpressions_Match_class;
extern il2cpp::Il2CppClass System_Text_RegularExpressions_Group_class;
extern il2cpp::Il2CppClass System_Text_RegularExpressions_GroupCollection_class;

namespace il2cpp { namespace runtime {

// Managed System.Text.RegularExpressions.Match layout. input is the FULL input
// string (Value derives from input + index/length). groups is the
// GroupCollection (Count = groupCount + 1; item 0 = the whole match, item i =
// capture group i). The GC reference fields come FIRST (slots 1/2) so the
// refFieldBitmap bits are pointer-size independent.
struct Il2CppRegexMatch
{
    Il2CppObject obj;     // object header (klass pointer, offset 0)
    Il2CppString* input;  // full input text (GC reference -- slot 1)
    Il2CppObject* groups; // GroupCollection (GC reference -- slot 2)
    int32_t index;        // whole-match start (0 when !success)
    int32_t length;       // whole-match length (0 when !success)
    int32_t success;      // 1 = the pattern matched
};

// Managed System.Text.RegularExpressions.Group layout. value/name are GC
// reference slots 1/2. A non-participating group carries success 0, index 0,
// length 0 and an EMPTY value string (net48 Group.Value == "").
struct Il2CppRegexGroup
{
    Il2CppObject obj;     // object header
    Il2CppString* value;  // captured text (GC reference -- slot 1)
    Il2CppString* name;   // group name/number string (GC reference -- slot 2)
    int32_t index;        // group start (0 when !success)
    int32_t length;       // group length (0 when !success)
    int32_t success;      // 1 = the group participated
};

// Managed System.Text.RegularExpressions.GroupCollection layout. items is a
// Group[] GC reference array (slot 1).
struct Il2CppRegexGroupCollection
{
    Il2CppObject obj;    // object header
    Il2CppArray* items;  // Group[] (GC reference -- slot 1)
    int32_t count;       // element count (groupCount + 1)
};

// Register the Match/Group/GroupCollection descriptors once (idempotent;
// il2cpp_class_register dedupes by pointer). Called defensively by the builder
// and by the out-of-range empty-group accessor.
void il2cpp_regex_obj_register_classes();

// Build the Match object graph for a match result. 'input' is the FULL input
// text (inputLength UTF-16 units); 'result' is the T2 engine result (success
// false = no match); 'groupCount' is RegexAst::groupCount (capture groups
// excluding the whole match). Always returns a non-null Match: on success
// Groups.Count == groupCount + 1; on no match it is net48 Match.Empty with
// Groups.Count == 1 (a single non-participating group 0). Registers the
// descriptors on first use. Allocates through the GC; the result is safe once
// returned (the caller keeps it reachable).
Il2CppObject* il2cpp_regex_build_match(const char16_t* input, int32_t inputLength,
                                       const MatchResult& result, int32_t groupCount);

// --- Match property icalls (receiver = Il2CppObject* self) -------------------

// bool Success: the pattern matched.
bool icall_regex_match_get_success(Il2CppObject* self);
// int Index / int Length: whole-match position/length (0/0 when no match).
int32_t icall_regex_match_get_index(Il2CppObject* self);
int32_t icall_regex_match_get_length(Il2CppObject* self);
// string Value / string ToString(): the matched substring ("" when no match).
Il2CppString* icall_regex_match_get_value(Il2CppObject* self);
Il2CppString* icall_regex_match_to_string(Il2CppObject* self);
// GroupCollection Groups.
Il2CppObject* icall_regex_match_get_groups(Il2CppObject* self);

// --- Group property icalls ---------------------------------------------------

bool icall_regex_group_get_success(Il2CppObject* self);
int32_t icall_regex_group_get_index(Il2CppObject* self);
int32_t icall_regex_group_get_length(Il2CppObject* self);
Il2CppString* icall_regex_group_get_value(Il2CppObject* self);
Il2CppString* icall_regex_group_get_name(Il2CppObject* self);

// --- GroupCollection property icalls -----------------------------------------

// int Count.
int32_t icall_regex_gc_get_count(Il2CppObject* self);
// Group this[int index]: item 0 = whole match, item i = capture group i. An
// out-of-range (or negative) index returns the shared empty Group
// (Success false / 0 / 0 / "" / "") -- net48 Match.Groups[oob], never a throw.
Il2CppObject* icall_regex_gc_get_item_int(Il2CppObject* self, int32_t index);

}} // namespace il2cpp::runtime
