//------------------------------------------------------------------------------
// test_attribute.cpp -- System.Attribute registry unit tests.
//
// Coverage (normal + edge + error paths; see attribute.h for the contract):
//   1. register -> get_custom returns the SAME array pointer, count matches.
//   2. unregistered type -> nullptr + *outCount == 0 (query-empty path).
//   3. multiple types registered independently -> per-type isolation.
//   4. re-register replaces the stored array (new registration wins).
//   5. empty registration (count == 0) -> 1 and creates no entry.
//   6. error inputs: null typeName / null array with count>0 / negative
//      count -> 0; get_custom null klass -> nullptr + *outCount == 0;
//      get_custom null outCount -> nullptr.
//   7. capacity: 32 unique types fill the registry; the 33rd unique
//      registration is rejected (0), while re-registering an EXISTING type
//      still succeeds (replacement never consumes capacity).
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "attribute.h"

#include "il2cpp_metadata.h"
#include "il2cpp_runtime.h"  // il2cpp_class_register (R4 _ex_named klass resolution)
#include "il2cpp_string.h" // il2cpp_string_new_from_utf8 (managed-string ABI)

#include <cstdio>
#include <cstring>

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
// Test fixture: three fake descriptors + attribute objects (klass only --
// the registry never touches the payload).
//------------------------------------------------------------------------------

il2cpp::Il2CppClass annotatedClass = {
    "T.Annotated", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass otherClass = {
    "T.Plain", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass thirdClass = {
    "T.Third", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

// Fake attribute objects (any klass pointer satisfies the registry).
il2cpp::Il2CppObject attrA = {&annotatedClass};
il2cpp::Il2CppObject attrB = {&annotatedClass};
il2cpp::Il2CppObject attrC = {&otherClass};

il2cpp::Il2CppObject* annotatedAttrs[2] = {&attrA, &attrB};
il2cpp::Il2CppObject* thirdAttrs[1] = {&attrC};

//------------------------------------------------------------------------------
// R4 (B3) fixture: a base-derived CHAIN (the inheritance walk target) plus two
// attribute classes (the attributeType filter + AttributeUsage keys). The
// _ex_named primitives resolve names via il2cpp_class_by_name, so each
// descriptor is class-REGISTERED in main(). Layout:
//
//   T.Tagged  (attribute class -- 'Tagged' attribute instances)
//   T.DerivedAttr (attribute class deriving T.Tagged -- filter assignability)
//   T.Base    (annotated: baseAttrs = {baseTagInstance})
//   T.Derived (derives T.Base; annotated: derivedAttrs = {derivedTagInstance})
//------------------------------------------------------------------------------
il2cpp::Il2CppClass taggedClass = {
    "T.Tagged", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass derivedAttrClass = {
    "T.DerivedAttr", 16, 0u, 0, &taggedClass, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass baseChainClass = {
    "T.Base", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppClass derivedChainClass = {
    "T.Derived", 16, 0u, 0, &baseChainClass, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppObject baseTag = {&taggedClass};
il2cpp::Il2CppObject derivedTag = {&derivedAttrClass};
il2cpp::Il2CppObject* baseAttrs[1] = {&baseTag};
il2cpp::Il2CppObject* derivedAttrs[1] = {&derivedTag};

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // --- 1. register -> get_custom (normal path) ---
    Expect(il2cpp_attribute_register("T.Annotated", annotatedAttrs, 2) == 1,
           "register annotated type -> 1");
    {
        int32_t count = -1;
        Il2CppObject** attrs = il2cpp_attribute_get_custom(&annotatedClass, &count);
        Expect(count == 2, "get_custom count == 2");
        Expect(attrs == annotatedAttrs, "get_custom returns the same array pointer");
        Expect(attrs != nullptr && attrs[0] == &attrA && attrs[1] == &attrB,
               "get_custom elements in declaration order");
    }

    // --- 1b. D-ATTR-1 managed-string ABI (get_custom_named / get_single_named):
    // the primitives the TRANSPILER emits. Registered type -> the array holds
    // the registered objects in declaration order; unregistered -> EMPTY array
    // (never null, net48); singular -> first object or null.
    {
        il2cpp::Il2CppString* msAnnotated = il2cpp::il2cpp_string_new_from_utf8("T.Annotated");
        il2cpp::Il2CppArray* named = il2cpp_attribute_get_custom_named(
            reinterpret_cast<Il2CppObject*>(msAnnotated));
        Expect(named != nullptr && named->length == 2,
               "get_custom_named registered type -> array of length 2");
        Expect(named != nullptr &&
                   *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(named, 0)) == &attrA &&
                   *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(named, 1)) == &attrB,
               "get_custom_named elements in declaration order");

        il2cpp::Il2CppString* msPlain = il2cpp::il2cpp_string_new_from_utf8("T.Plain");
        Il2CppArray* none = il2cpp_attribute_get_custom_named(
            reinterpret_cast<Il2CppObject*>(msPlain));
        Expect(none != nullptr && none->length == 0,
               "get_custom_named unregistered type -> EMPTY array (never null, net48)");

        il2cpp::Il2CppString* msNull = nullptr;
        Il2CppArray* nullArr = il2cpp_attribute_get_custom_named(
            reinterpret_cast<Il2CppObject*>(msNull));
        Expect(nullArr != nullptr && nullArr->length == 0,
               "get_custom_named null typeName -> EMPTY array");

        Expect(il2cpp_attribute_get_single_named(reinterpret_cast<Il2CppObject*>(msAnnotated)) == &attrA,
               "get_single_named registered type -> first attribute");
        Expect(il2cpp_attribute_get_single_named(reinterpret_cast<Il2CppObject*>(msPlain)) == nullptr,
               "get_single_named unregistered type -> nullptr");
        Expect(il2cpp_attribute_get_single_named(reinterpret_cast<Il2CppObject*>(msNull)) == nullptr,
               "get_single_named null typeName -> nullptr");
    }

    // --- R4 (B3): the net48-semantics _ex_named primitives (attributeType
    // filter + inherit inheritance walk + AttributeUsage rules). The names
    // resolve through il2cpp_class_by_name, so the chain descriptors are
    // class-registered first.
    il2cpp::il2cpp_class_register(&taggedClass);
    il2cpp::il2cpp_class_register(&derivedAttrClass);
    il2cpp::il2cpp_class_register(&baseChainClass);
    il2cpp::il2cpp_class_register(&derivedChainClass);
    il2cpp::runtime::il2cpp_attribute_register("T.Base", baseAttrs, 1);
    il2cpp::runtime::il2cpp_attribute_register("T.Derived", derivedAttrs, 1);
    {
        auto msDerived = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_utf8("T.Derived"));
        auto msBase = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_utf8("T.Base"));
        auto msTagged = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_utf8("T.Tagged"));
        auto msDerivedAttr = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_utf8("T.DerivedAttr"));
        auto msUnknown = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_utf8("T.Never"));

        // Own type only (inherit = 0): the derived's own attribute.
        Il2CppArray* ownOnly = il2cpp::runtime::il2cpp_attribute_get_custom_ex_named(
            msDerived, nullptr, 0);
        Expect(ownOnly != nullptr && ownOnly->length == 1 &&
                   *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(ownOnly, 0)) == &derivedTag,
               "R4 get_custom_ex_named inherit=0 own type only");

        // Inherit = 1: the base's attribute joins (most-derived first).
        Il2CppArray* inherited = il2cpp::runtime::il2cpp_attribute_get_custom_ex_named(
            msDerived, nullptr, 1);
        Expect(inherited != nullptr && inherited->length == 2 &&
                   *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(inherited, 0)) == &derivedTag &&
                   *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(inherited, 1)) == &baseTag,
               "R4 get_custom_ex_named inherit=1 walks the base chain (most-derived first)");

        // Base type's own query: only the base attribute.
        Il2CppArray* baseOnly = il2cpp::runtime::il2cpp_attribute_get_custom_ex_named(
            msBase, nullptr, 1);
        Expect(baseOnly != nullptr && baseOnly->length == 1 &&
                   *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(baseOnly, 0)) == &baseTag,
               "R4 get_custom_ex_named base type own attribute only");

        // attributeType filter: T.DerivedAttr matches the DERIVED attribute
        // (assignability -- derivedAttr derives tagged), and the BASE attr
        // (a T.Tagged instance) does NOT match T.DerivedAttr.
        Il2CppArray* filtered = il2cpp::runtime::il2cpp_attribute_get_custom_ex_named(
            msDerived, msDerivedAttr, 1);
        Expect(filtered != nullptr && filtered->length == 1 &&
                   *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(filtered, 0)) == &derivedTag,
               "R4 attributeType filter keeps only assignable matches");

        // attributeType filter by base: T.Tagged matches BOTH (baseTag is a
        // T.Tagged instance, derivedTag derives T.Tagged).
        Il2CppArray* taggedFiltered = il2cpp::runtime::il2cpp_attribute_get_custom_ex_named(
            msDerived, msTagged, 1);
        Expect(taggedFiltered != nullptr && taggedFiltered->length == 2,
               "R4 attributeType base filter keeps derived + base instances");

        // Unresolvable attributeType -> empty array (matches nothing).
        Il2CppArray* unknownFilter = il2cpp::runtime::il2cpp_attribute_get_custom_ex_named(
            msDerived, msUnknown, 1);
        Expect(unknownFilter != nullptr && unknownFilter->length == 0,
               "R4 unresolvable attributeType -> empty array");

        // Unregistered element type -> empty array (never null).
        Il2CppArray* unregElem = il2cpp::runtime::il2cpp_attribute_get_custom_ex_named(
            msUnknown, nullptr, 1);
        Expect(unregElem != nullptr && unregElem->length == 0,
               "R4 unregistered element type -> empty array");

        // Null element -> ArgumentNullException (net48).
        bool nre = false;
        try
        {
            (void)il2cpp::runtime::il2cpp_attribute_get_custom_ex_named(nullptr, msTagged, 1);
        }
        catch (const il2cpp::DotNetException& e)
        {
            nre = il2cpp::il2cpp_exception_matches("System.ArgumentNullException", e.type_name()) != 0;
        }
        Expect(nre, "R4 get_custom_ex_named null element -> ArgumentNullException");

        // Singular mirror: first match most-derived-first; filtered result.
        Expect(il2cpp::runtime::il2cpp_attribute_get_single_ex_named(msDerived, nullptr, 0) == &derivedTag,
               "R4 get_single_ex_named inherit=0 -> own first");
        Expect(il2cpp::runtime::il2cpp_attribute_get_single_ex_named(msDerived, msDerivedAttr, 1) == &derivedTag,
               "R4 get_single_ex_named filtered -> the only match");
        Expect(il2cpp::runtime::il2cpp_attribute_get_single_ex_named(msDerived, msUnknown, 1) == nullptr,
               "R4 get_single_ex_named unresolvable filter -> null");
        Expect(il2cpp::runtime::il2cpp_attribute_get_single_ex_named(msBase, msDerivedAttr, 1) == nullptr,
               "R4 get_single_ex_named base with derived filter -> null (no match)");
    }

    // --- 2. unregistered type -> empty query ---
    {
        int32_t count = -1;
        Il2CppObject** attrs = il2cpp_attribute_get_custom(&otherClass, &count);
        Expect(attrs == nullptr, "unregistered type -> nullptr");
        Expect(count == 0, "unregistered type -> *outCount == 0");
    }

    // --- 3. per-type isolation ---
    Expect(il2cpp_attribute_register("T.Third", thirdAttrs, 1) == 1,
           "register third type -> 1");
    {
        int32_t count = -1;
        Il2CppObject** attrs = il2cpp_attribute_get_custom(&thirdClass, &count);
        Expect(count == 1 && attrs == thirdAttrs, "third type isolated (count 1)");
    }
    {
        int32_t count = -1;
        Il2CppObject** attrs = il2cpp_attribute_get_custom(&annotatedClass, &count);
        Expect(count == 2 && attrs == annotatedAttrs,
               "annotated type unaffected by other registrations");
    }
    {
        int32_t count = -1;
        Expect(il2cpp_attribute_get_custom(&otherClass, &count) == nullptr,
               "plain type still unregistered");
    }

    // --- 4. re-register replaces the stored array ---
    il2cpp::Il2CppObject* replacedAttrs[1] = {&attrC};
    Expect(il2cpp_attribute_register("T.Annotated", replacedAttrs, 1) == 1,
           "re-register existing type -> 1");
    {
        int32_t count = -1;
        Il2CppObject** attrs = il2cpp_attribute_get_custom(&annotatedClass, &count);
        Expect(count == 1 && attrs == replacedAttrs,
               "re-registration replaces the stored array");
    }

    // --- 5. empty registration is a no-op ---
    Expect(il2cpp_attribute_register("T.Plain", nullptr, 0) == 1,
           "register count == 0 -> 1");
    {
        int32_t count = -1;
        Expect(il2cpp_attribute_get_custom(&otherClass, &count) == nullptr,
               "empty registration creates no entry");
        Expect(count == 0, "empty registration -> *outCount == 0");
    }

    // --- 6. error inputs ---
    Expect(il2cpp_attribute_register(nullptr, annotatedAttrs, 2) == 0,
           "register null typeName -> 0");
    Expect(il2cpp_attribute_register("T.NoArr", nullptr, 2) == 0,
           "register null array with count > 0 -> 0");
    Expect(il2cpp_attribute_register("T.Neg", annotatedAttrs, -1) == 0,
           "register negative count -> 0");
    {
        int32_t count = -1;
        Expect(il2cpp_attribute_get_custom(nullptr, &count) == nullptr,
               "get_custom null klass -> nullptr");
        Expect(count == 0, "get_custom null klass -> *outCount == 0");
        Expect(il2cpp_attribute_get_custom(&annotatedClass, nullptr) == nullptr,
               "get_custom null outCount -> nullptr");
    }

    // --- 7. capacity bound (2 entries from sections 1-4 are live: the
    // registry has room for 30 more) ---
    {
        static char capNames[32][16];
        il2cpp::Il2CppClass capClasses[32];
        il2cpp::Il2CppObject capAttr[32];
        il2cpp::Il2CppObject* capAttrPtrs[32];
        for (int32_t i = 0; i < 32; ++i)
        {
            std::snprintf(capNames[i], sizeof(capNames[i]), "T.Cap%02d", i);
            capClasses[i].name = capNames[i];
            capAttr[i].klass = &capClasses[i];
            capAttrPtrs[i] = &capAttr[i];
        }
        // Fill the registry to capacity (28 fills the 28 free slots -- the
        // live entries are T.Annotated/T.Third + the R4 T.Base/T.Derived).
        int32_t ok = 1;
        for (int32_t i = 0; i < 28; ++i)
        {
            ok = il2cpp_attribute_register(capNames[i], &capAttrPtrs[i], 1);
            if (ok != 1)
            {
                break;
            }
        }
        Expect(ok == 1, "28 unique registrations fill the remaining capacity");
        Expect(il2cpp_attribute_register(capNames[28], &capAttrPtrs[28], 1) == 0,
               "29th unique registration rejected at capacity -> 0");
        Expect(il2cpp_attribute_register(capNames[29], &capAttrPtrs[29], 1) == 0,
               "30th unique registration rejected at capacity -> 0");
        // Re-registering an existing entry still succeeds at capacity
        // (replacement consumes no new slot).
        Expect(il2cpp_attribute_register(capNames[0], &capAttrPtrs[0], 1) == 1,
               "re-register existing type at capacity -> 1");
        {
            int32_t count = -1;
            Expect(il2cpp_attribute_get_custom(&capClasses[27], &count) != nullptr &&
                       count == 1,
                   "last accepted entry still queryable");
            Expect(il2cpp_attribute_get_custom(&capClasses[28], &count) == nullptr,
                   "rejected 29th entry not queryable");
        }
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d attribute assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime attribute tests passed\n");
    return 0;
}
