//------------------------------------------------------------------------------
// test_object_equals_user_override.cpp -- baudit2 residual: static
// Object.Equals(a, b) MUST be the VIRTUAL a.Equals(b).
//
// net48: List<T>.IndexOf/Contains/Remove + ArrayList.IndexOf/Remove funnel
// through runtime::icall_object_equals; a value-equal distinct user object
// with an Equals(object) override must be FOUND. The pre-fix live runtime
// returns early through BoxedValueEquals ("same-named non-primitive") for
// same-klass receivers, and the name-only slot lookup can pick the
// IEquatable overload's slot (wrong ABI).
//
// Synthetic classes supply the typeinfo + vtable (the transpiler emits one
// per user type); the overloaded class pins the signature-aware lookup.
//------------------------------------------------------------------------------
#include "il2cpp_value_semantics.h"

#include "il2cpp_boxing_ext.h"  // icall_box_int32
#include "il2cpp_boxing_ext2.h" // icall_box_user_struct
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "typeinfo.h"

using il2cpp::Il2CppObject;

#include <cstdint>
#include <cstdio>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

//------------------------------------------------------------------------------
// User CLASS with a reflected Equals(object) override (baudit2 P shape).
//------------------------------------------------------------------------------
struct FakeP { Il2CppObject _obj; int32_t V; };
struct FakeQ { Il2CppObject _obj; int32_t V; }; // same layout, NO override
struct FakeSt { int32_t V; };                   // value type (box payload)

int32_t g_pCalls = 0;

// Forward declarations: the override bodies below compare the OTHER box's klass
extern il2cpp::Il2CppClass g_pClass;
extern il2cpp::Il2CppClass g_stClass;

int32_t FakeP_Equals_Object(Il2CppObject* self, Il2CppObject* other)
{
    ++g_pCalls;
    // net48 `P p = o as P; return p != null && p.V == V;` -- a foreign type
    // answers false, same-type compares the field.
    if (other == nullptr || other->klass != &g_pClass) return 0;
    const int32_t v = *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(self) + 8);
    const int32_t ov = *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(other) + 8);
    return v == ov ? 1 : 0;
}

void* g_pVtable[] = { reinterpret_cast<void*>(&FakeP_Equals_Object) };
il2cpp::Il2CppClass g_pClass = {
    "Cases.P", static_cast<int32_t>(sizeof(FakeP)), 0u, 0,
    &System_Object_class, g_pVtable, 1, nullptr, 0,
};
const il2cpp::Il2CppMethodInfo g_pMethods[] = {
    { "Equals", "b(r:System.Object)", 1, 0x9, 0, "Cases.P",
      reinterpret_cast<void*>(&FakeP_Equals_Object) },
};
il2cpp::Il2CppTypeInfo g_pTypeInfo = {
    "Cases.P", nullptr, nullptr, nullptr, 0, nullptr, 0, g_pMethods, 1, nullptr, 0,
};

// No-override class: reference-equality fallback must stay.
il2cpp::Il2CppClass g_qClass = {
    "Cases.Q", static_cast<int32_t>(sizeof(FakeQ)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppTypeInfo g_qTypeInfo = {
    "Cases.Q", nullptr, nullptr, nullptr, 0, nullptr, 0, nullptr, 0, nullptr, 0,
};

//------------------------------------------------------------------------------
// Overloaded class: Equals(T) declared FIRST in the method table (slot 0) and
// Equals(object) SECOND (slot 1) -- the IEquatable<T> + override shape the
// transpiler emits. A NAME-ONLY lookup picks slot 0 (wrong ABI); the
// signature-aware lookup must pick slot 1.
//------------------------------------------------------------------------------
int32_t g_poisonCalls = 0;
int32_t g_objectCalls = 0;

int32_t FakeO_Equals_T(Il2CppObject* self, Il2CppObject* other)
{
    ++g_poisonCalls; // must NEVER be reached by the reflected-equality path
    (void)self;
    (void)other;
    return 0;
}

int32_t FakeO_Equals_Object(Il2CppObject* self, Il2CppObject* other)
{
    ++g_objectCalls;
    (void)other;
    const int32_t v = *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(self) + 8);
    return v == 7 ? 1 : 0;
}

void* g_oVtable[] = {
    reinterpret_cast<void*>(&FakeO_Equals_T),      // slot 0: Equals(O)
    reinterpret_cast<void*>(&FakeO_Equals_Object), // slot 1: Equals(object)
};
il2cpp::Il2CppClass g_oClass = {
    "Cases.O", static_cast<int32_t>(sizeof(FakeP)), 0u, 0,
    &System_Object_class, g_oVtable, 2, nullptr, 0,
};
const il2cpp::Il2CppMethodInfo g_oMethods[] = {
    { "Equals", "b(V:Cases.O)", 1, 0x9, 0, "Cases.O",
      reinterpret_cast<void*>(&FakeO_Equals_T) },
    { "Equals", "b(r:System.Object)", 1, 0x9, 1, "Cases.O",
      reinterpret_cast<void*>(&FakeO_Equals_Object) },
};
il2cpp::Il2CppTypeInfo g_oTypeInfo = {
    "Cases.O", nullptr, nullptr, nullptr, 0, nullptr, 0, g_oMethods, 2, nullptr, 0,
};

//------------------------------------------------------------------------------
// User VALUE type with an Equals(object) override -- the receiver passed to
// the dispatch MUST be the box PAYLOAD address (value-type ABI).
//------------------------------------------------------------------------------
int32_t g_stCalls = 0;

int32_t FakeSt_Equals_Object(Il2CppObject* self, Il2CppObject* other)
{
    ++g_stCalls;
    // `Equals(object o) => o is St s && s.V == V` -- reads SELF through the
    // PAYLOAD address (a wrong object-pointer ABI would misread the klass).
    if (other == nullptr || other->klass != &g_stClass) return 0;
    const int32_t v = *reinterpret_cast<const int32_t*>(self);
    const int32_t ov = *reinterpret_cast<const int32_t*>(reinterpret_cast<const char*>(other) + 8);
    return v == ov ? 1 : 0;
}

void* g_stVtable[] = { reinterpret_cast<void*>(&FakeSt_Equals_Object) };
il2cpp::Il2CppClass g_stClass = {
    "Cases.ValueSt", static_cast<int32_t>(sizeof(FakeSt)), 0u, 0,
    &System_ValueType_class, g_stVtable, 1, nullptr, 0,
};
const il2cpp::Il2CppMethodInfo g_stMethods[] = {
    { "Equals", "b(r:System.Object)", 1, 0x9, 0, "Cases.ValueSt",
      reinterpret_cast<void*>(&FakeSt_Equals_Object) },
};
il2cpp::Il2CppTypeInfo g_stTypeInfo = {
    "Cases.ValueSt", nullptr, nullptr, nullptr, 0, nullptr, 0, g_stMethods, 1, nullptr, 0,
};

il2cpp::Il2CppObject* NewP(il2cpp::Il2CppClass* klass, int32_t v)
{
    auto* obj = reinterpret_cast<uint8_t*>(il2cpp::il2cpp_object_new(klass));
    *reinterpret_cast<int32_t*>(obj + 8) = v;
    return reinterpret_cast<il2cpp::Il2CppObject*>(obj);
}

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    il2cpp_class_register(&g_pClass);
    il2cpp_typeinfo_register(&g_pTypeInfo);
    il2cpp_class_register(&g_qClass);
    il2cpp_typeinfo_register(&g_qTypeInfo);
    il2cpp_class_register(&g_oClass);
    il2cpp_typeinfo_register(&g_oTypeInfo);
    il2cpp_class_register(&g_stClass);
    il2cpp_typeinfo_register(&g_stTypeInfo);

    Il2CppObject* p7 = NewP(&g_pClass, 7);
    Il2CppObject* p7b = NewP(&g_pClass, 7);
    Il2CppObject* p8 = NewP(&g_pClass, 8);
    Il2CppObject* q1 = NewP(&g_qClass, 5);
    Il2CppObject* q2 = NewP(&g_qClass, 5);
    Il2CppObject* o7 = NewP(&g_oClass, 7);
    Il2CppObject* o7b = NewP(&g_oClass, 7);

    // 1. same-klass user class with override: the override runs (net48 true).
    Expect(icall_object_equals(p7, p7b), "user class override: equal V -> true");
    Expect(g_pCalls == 1, "user class override dispatched exactly once");

    // 2. override answers false for a different V.
    Expect(!icall_object_equals(p7, p8), "user class override: different V -> false");
    Expect(g_pCalls == 2, "user class override dispatched for the false case");

    // 3. identical reference short-circuit (override not re-entered).
    Expect(icall_object_equals(p7, p7), "identical reference -> true");
    Expect(g_pCalls == 2, "identical reference skips the override");

    // 4. cross-klass receiver: a's virtual override still wins (net48).
    Expect(!icall_object_equals(p7, q1), "cross-klass: a's override rejects a foreign type -> false");
    Expect(g_pCalls == 3, "cross-klass override dispatched");

    // 5. overloaded Equals(T)/Equals(object): the OBJECT override slot must
    //    be selected by signature, never the IEquatable slot.
    Expect(icall_object_equals(o7, o7b), "overloaded class: object override -> true");
    Expect(g_objectCalls == 1, "overloaded class: object override dispatched once");
    Expect(g_poisonCalls == 0, "overloaded class: Equals(T) slot NEVER invoked by Object.Equals");

    // 6. no-override class: reference equality fallback (unchanged).
    Expect(!icall_object_equals(q1, q2), "no-override class: distinct boxes -> false");
    Expect(icall_object_equals(q1, q1), "no-override class: identical -> true");

    // 7. boxed value type with override: payload receiver ABI.
    FakeSt seven{7};
    FakeSt seven2{7};
    FakeSt eight{8};
    Il2CppObject* b7 = icall_box_user_struct(&g_stClass, &seven, sizeof(FakeSt));
    Il2CppObject* b7b = icall_box_user_struct(&g_stClass, &seven2, sizeof(FakeSt));
    Il2CppObject* b8 = icall_box_user_struct(&g_stClass, &eight, sizeof(FakeSt));
    Expect(icall_object_equals(b7, b7b), "boxed value type override: payload 7 -> true");
    Expect(!icall_object_equals(b7, b8), "boxed value type override: payload 8 -> false");
    Expect(g_stCalls == 2, "boxed value type override dispatched twice");

    // 8. builtin fast paths unchanged.
    Expect(icall_object_equals(il2cpp::il2cpp_box_int32(&System_Int32_class, 5), il2cpp::il2cpp_box_int32(&System_Int32_class, 5)),
           "boxed int32 equal -> true");
    Expect(!icall_object_equals(il2cpp::il2cpp_box_int32(&System_Int32_class, 5), il2cpp::il2cpp_box_int32(&System_Int32_class, 6)),
           "boxed int32 different -> false");
    Il2CppObject* s1 = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("ab"));
    Il2CppObject* s2 = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("ab"));
    Expect(icall_object_equals(s1, s2), "equal strings (distinct refs) -> true");

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("FAILURES: %d\n", g_failures);
    return 1;
}
