//------------------------------------------------------------------------------
// test_boxed_user_struct_equals.cpp -- caudit2 boxed user-struct equality.
//
// `((object)a).Equals((object)b)` for a USER struct with an Equals(object)
// override must dispatch the OVERRIDE through the reflected vtable slot
// (net48 callvirt semantics). Pre-fix, icall_boxed_equals_ext2 delegates
// non-primitive same-klass boxes to boxingext.cpp's icall_boxed_equals
// (reference equality) -> false for two equal boxes.
//
// The override is a black box the runtime must not second-guess: the fake
// overrides below return constants and read the receiver payload, so a
// payload-byte comparison fallback would produce DIFFERENT results than the
// override for at least one case. The override receives the box PAYLOAD
// address as `this` (value-type ABI, the il2cpp_unbox_user_struct contract).
//------------------------------------------------------------------------------
#include "il2cpp_boxing_ext2.h"

#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "typeinfo.h"

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

struct FakeSt { int32_t V; };
struct FakePlain { int32_t V; };

int32_t g_alwaysTrueCalls = 0;
int32_t g_alwaysFalseCalls = 0;
int32_t g_payloadCalls = 0;

// The generated value-type override ABI: (payload address, object).
int32_t FakeAlwaysTrue_Equals_Object(il2cpp::Il2CppObject* self, il2cpp::Il2CppObject* other)
{
    ++g_alwaysTrueCalls;
    (void)self;
    (void)other;
    return 1; // a payload compare of 7 vs 8 would say false
}

int32_t FakeAlwaysFalse_Equals_Object(il2cpp::Il2CppObject* self, il2cpp::Il2CppObject* other)
{
    ++g_alwaysFalseCalls;
    (void)self;
    (void)other;
    return 0; // a payload compare of 7 vs 7 would say true
}

int32_t FakePayload_Equals_Object(il2cpp::Il2CppObject* self, il2cpp::Il2CppObject* other)
{
    ++g_payloadCalls;
    (void)other;
    // Proves the runtime passed the PAYLOAD address: V lives at offset 0 of
    // the value; the object-header pointer would misread the klass pointer.
    const int32_t v = *reinterpret_cast<const int32_t*>(self);
    return v == 7 ? 1 : 0;
}

#define FAKE_STRUCT(NAME, FN)                                                      \
    void* g_##NAME##Vtable[] = { reinterpret_cast<void*>(&FN) };                   \
    il2cpp::Il2CppClass g_##NAME##Class = {                                        \
        "Cases." #NAME, static_cast<int32_t>(sizeof(FakeSt)), 0u, 0,               \
        &System_ValueType_class, g_##NAME##Vtable, 1, nullptr, 0,                  \
    };                                                                             \
    const il2cpp::Il2CppMethodInfo g_##NAME##Methods[] = {                         \
        { "Equals", "b(r:System.Object)", 1, 0x9, 0, "Cases." #NAME,               \
          reinterpret_cast<void*>(&FN) },                                          \
    };                                                                             \
    il2cpp::Il2CppTypeInfo g_##NAME##TypeInfo = {                                  \
        "Cases." #NAME, nullptr, nullptr, nullptr, 0, nullptr, 0,                  \
        g_##NAME##Methods, 1, nullptr, 0,                                          \
    }

FAKE_STRUCT(FakeAlwaysTrue, FakeAlwaysTrue_Equals_Object);
FAKE_STRUCT(FakeAlwaysFalse, FakeAlwaysFalse_Equals_Object);
FAKE_STRUCT(FakePayload, FakePayload_Equals_Object);

// A struct WITHOUT an Equals(object) override: no typeinfo method entry --
// the legacy reference-equality fallback must remain in effect.
il2cpp::Il2CppClass g_plainClass = {
    "Cases.FakePlain", static_cast<int32_t>(sizeof(FakePlain)), 0u, 0,
    &System_ValueType_class, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppTypeInfo g_plainTypeInfo = {
    "Cases.FakePlain", nullptr, nullptr, nullptr, 0, nullptr, 0,
    nullptr, 0, nullptr, 0,
};

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    il2cpp_class_register(&g_FakeAlwaysTrueClass);
    il2cpp_typeinfo_register(&g_FakeAlwaysTrueTypeInfo);
    il2cpp_class_register(&g_FakeAlwaysFalseClass);
    il2cpp_typeinfo_register(&g_FakeAlwaysFalseTypeInfo);
    il2cpp_class_register(&g_FakePayloadClass);
    il2cpp_typeinfo_register(&g_FakePayloadTypeInfo);
    il2cpp_class_register(&g_plainClass);
    il2cpp_typeinfo_register(&g_plainTypeInfo);

    FakeSt seven{7};
    FakeSt seven2{7};
    FakeSt eight{8};
    Il2CppObject* aSeven = icall_box_user_struct(&g_FakeAlwaysTrueClass, &seven, sizeof(FakeSt));
    Il2CppObject* aSeven2 = icall_box_user_struct(&g_FakeAlwaysTrueClass, &seven2, sizeof(FakeSt));
    Il2CppObject* aEight = icall_box_user_struct(&g_FakeAlwaysTrueClass, &eight, sizeof(FakeSt));
    Il2CppObject* fSeven = icall_box_user_struct(&g_FakeAlwaysFalseClass, &seven, sizeof(FakeSt));
    Il2CppObject* fSeven2 = icall_box_user_struct(&g_FakeAlwaysFalseClass, &seven2, sizeof(FakeSt));
    Il2CppObject* pSeven = icall_box_user_struct(&g_FakePayloadClass, &seven, sizeof(FakeSt));
    Il2CppObject* pSeven2 = icall_box_user_struct(&g_FakePayloadClass, &seven2, sizeof(FakeSt));
    Il2CppObject* pEight = icall_box_user_struct(&g_FakePayloadClass, &eight, sizeof(FakeSt));
    Il2CppObject* pEight2 = icall_box_user_struct(&g_FakePayloadClass, &eight, sizeof(FakeSt));

    // 1. always-true override: distinct same-value boxes -> true, and the
    //    override really ran (dispatch, not payload compare).
    Expect(icall_boxed_equals_ext2(aSeven, aSeven2), "override dispatch: always-true override -> true");
    Expect(g_alwaysTrueCalls == 1, "always-true override dispatched exactly once");

    // 2. always-true override on DIFFERENT values still answers true: a
    //    payload value-compare would answer false here (dispatch proof).
    Expect(icall_boxed_equals_ext2(aSeven, aEight), "always-true override: different payloads -> still true");
    Expect(g_alwaysTrueCalls == 2, "always-true override dispatched for the different-payload case");

    // 3. identical-reference short-circuit (override not re-entered).
    Expect(icall_boxed_equals_ext2(aSeven, aSeven), "identical box reference -> true");
    Expect(g_alwaysTrueCalls == 2, "identical-reference short-circuit skips the override");

    // 4. always-false override: same-value distinct boxes -> false (a payload
    //    value-compare would answer true here -- the other dispatch proof).
    Expect(!icall_boxed_equals_ext2(fSeven, fSeven2), "always-false override: same payloads -> false");
    Expect(g_alwaysFalseCalls == 1, "always-false override dispatched exactly once");

    // 5. payload-reading override: the runtime passed the PAYLOAD address
    //    (V=7 -> true, V=8 -> false, same klass both sides).
    Expect(icall_boxed_equals_ext2(pSeven, pSeven2), "payload override reads receiver payload 7 -> true");
    Expect(!icall_boxed_equals_ext2(pEight, pEight2), "payload override reads receiver payload 8 -> false");
    Expect(g_payloadCalls == 2, "payload override dispatched twice");

    // 6. no-override struct: reference equality fallback (unchanged).
    FakePlain p1{5};
    FakePlain p2{5};
    Il2CppObject* bp1 = icall_box_user_struct(&g_plainClass, &p1, sizeof(FakePlain));
    Il2CppObject* bp2 = icall_box_user_struct(&g_plainClass, &p2, sizeof(FakePlain));
    Expect(!icall_boxed_equals_ext2(bp1, bp2), "no-override same-value boxes stay reference-unequal (legacy fallback)");
    Expect(icall_boxed_equals_ext2(bp1, bp1), "no-override identical box -> true");

    // 7. null handling unchanged.
    Expect(!icall_boxed_equals_ext2(nullptr, bp1), "null vs box -> false");
    Expect(!icall_boxed_equals_ext2(bp1, nullptr), "box vs null -> false");
    Expect(icall_boxed_equals_ext2(nullptr, nullptr), "null vs null -> true");

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("FAILURES: %d\n", g_failures);
    return 1;
}
