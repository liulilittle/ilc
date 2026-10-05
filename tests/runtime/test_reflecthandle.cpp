//------------------------------------------------------------------------------
// test_reflecthandle.cpp -- reflection HANDLE value-type icall unit tests
// (RuntimeTypeHandle / RuntimeMethodHandle / RuntimeFieldHandle; see
// reflecthandle.h for the contract -- minimal surface: get_value / equals /
// get_hash_code / get_runtime_type, net48 default-handle semantics).
//
// Coverage (normal + edge + error paths):
//   1. rthandle get_value -- real descriptor -> its address (nonzero);
//      null -> 0 (net48: default.Value == 0).
//   2. rthandle equals -- same descriptor -> 1; DISTINCT descriptors with the
//      SAME name -> 1 (type identity by name, the typecheck rule); different
//      names -> 0; null/null -> 1 (net48: default.Equals(default) true);
//      null vs real -> 0 (net48: default.Equals(real) false).
//   3. rthandle get_hash_code -- null -> 0; equal handles (same name, even
//      distinct descriptors) hash EQUAL ("equal keys -> equal hashes");
//      the same descriptor hashes deterministically (call twice -> same).
//   4. rthandle get_runtime_type -- real handle -> the full-name managed
//      string (equals klass->name); null -> nullptr (net48:
//      default.GetRuntimeType() == null, probed -- no throw).
//   5. type roundtrip -- handle -> GetRuntimeType (name string) -> typeinfo
//      registry lookup by that name -> the SAME descriptor (handle identity
//      round-trips through the Type value; the handle->Type direction the
//      typeext GetTypeFromHandle also serves).
//   6. rmethodhandle get_value -- entry address; null -> 0.
//   7. rmethodhandle equals -- same entry -> 1; distinct entries with the
//      SAME (declaringType, name, signature) -> 1 (method identity by
//      content, the corert RuntimeMethodHandle.Equals model); different name
//      -> 0; different signature (overload) -> 0; different declaringType
//      -> 0; null/null -> 1; null vs real -> 0.
//   8. rmethodhandle get_hash_code -- null -> 0; equal method identities
//      hash equal; deterministic on repeat.
//   9. rfieldhandle get_value -- entry address; null -> 0.
//  10. rfieldhandle equals -- same entry -> 1; distinct entries with the SAME
//      (name, typeName) -> 1; different name -> 0; different typeName -> 0;
//      null/null -> 1; null vs real -> 0.
//  11. rfieldhandle get_hash_code -- null -> 0; equal field identities hash
//      equal; deterministic on repeat.
//  12. rthandle degenerate -- a descriptor with a NULL name hashes to 0
//      (deterministic; the Djb2Fold null path) and equals any DISTINCT
//      descriptor via name equality only when both names are non-null
//      (icall_type_equals null-name rule -> 0).
//  13. rmethodhandle degenerate -- entries with a null declaringType or null
//      signature: equals vs any real entry -> 0 (a null identity component
//      is never equal), hash still deterministic (null folds as 0 bytes).
//  14. rfieldhandle degenerate + DOCUMENTED DIVERGENCE pin -- an entry with a
//      null typeName: equals -> 0, hash deterministic; two DISTINCT entries
//      with the SAME (name, typeName) -- a same field name + type in a
//      DIFFERENT declaring type -- compare EQUAL here (Il2CppFieldInfo has
//      no declaring-type dimension; net48 would compare them DIFFERENT, see
//      reflecthandle.h -- the test pins the current model deliberately).
//
// Plain executable + minimal assertion harness (see test_typecheck.cpp).
//------------------------------------------------------------------------------

#include "reflecthandle.h"

#include "typeinfo.h"        // Il2CppMethodInfo / Il2CppFieldInfo fixtures
#include "il2cpp_string.h"   // il2cpp_string_new_from_cs / Il2CppString

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

// Compare a managed string (returned by get_runtime_type) with an ASCII
// literal (type names are ASCII by project convention).
bool ManagedStringEqualsAscii(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    auto str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(str->length) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

} // namespace

//------------------------------------------------------------------------------
// Fixture: two type descriptors + a method/field table, mirroring the static
// const tables TypeEmitter emits per reflected type (design 2.1).
//------------------------------------------------------------------------------

il2cpp::Il2CppClass typeAClass = {
    "T.TypeA", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass typeBClass = {
    "T.TypeB", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppMethodInfo methodEntries[3] = {
    {"Add", "I:i:i", 2, 1, -1, "T.TypeA", reinterpret_cast<void*>(0x1)},
    {"Sub", "I:i:i", 2, 1, -1, "T.TypeA", reinterpret_cast<void*>(0x2)},
    {"Add", "I:i:i", 2, 1, -1, "T.TypeB", reinterpret_cast<void*>(0x3)},
};

il2cpp::Il2CppFieldInfo fieldEntries[3] = {
    {"x", "System.Int32", 0, 0, nullptr, 1},
    {"y", "System.Int32", 4, 0, nullptr, 1},
    {"x", "System.String", 8, 1, nullptr, 1},
};

// TypeInfo fixture for the roundtrip (registry -> klass back-pointer). The
// klass back-pointer is const by contract (typeinfo.h), so the handle icalls
// that take a mutable descriptor receive a const_cast in the test -- the
// descriptor itself is non-const in generated code.
il2cpp::Il2CppTypeInfo typeAInfo = {
    "T.TypeA", &typeAClass, nullptr, nullptr, 0,
    nullptr, 0, nullptr, 0, nullptr, 0,
};

int main()
{
    using namespace il2cpp;

    // --- 1. rthandle get_value ---
    Expect(il2cpp_rthandle_get_value(&typeAClass) ==
               reinterpret_cast<intptr_t>(&typeAClass),
           "rthandle get_value -> the descriptor address");
    Expect(il2cpp_rthandle_get_value(nullptr) == 0,
           "rthandle get_value null -> 0 (default handle)");

    // --- 2. rthandle equals ---
    Expect(il2cpp_rthandle_equals(&typeAClass, &typeAClass) == 1,
           "rthandle equals same descriptor -> 1");
    {
        // Distinct descriptors, identical name -> the SAME type (the
        // typecheck name-equality rule: two TUs emit the same name literal).
        Il2CppClass aliasOfTypeA = {
            "T.TypeA", 8, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
        };
        Expect(il2cpp_rthandle_equals(&typeAClass, &aliasOfTypeA) == 1,
               "rthandle equals distinct descriptors same name -> 1");
    }
    Expect(il2cpp_rthandle_equals(&typeAClass, &typeBClass) == 0,
           "rthandle equals different types -> 0");
    Expect(il2cpp_rthandle_equals(nullptr, nullptr) == 1,
           "rthandle equals null/null -> 1 (net48 default.Equals(default))");
    Expect(il2cpp_rthandle_equals(&typeAClass, nullptr) == 0,
           "rthandle equals real vs null -> 0");
    Expect(il2cpp_rthandle_equals(nullptr, &typeBClass) == 0,
           "rthandle equals null vs real -> 0");

    // --- 3. rthandle get_hash_code ---
    Expect(il2cpp_rthandle_get_hash_code(nullptr) == 0,
           "rthandle hash null -> 0");
    Expect(il2cpp_rthandle_get_hash_code(&typeAClass) ==
               il2cpp_rthandle_get_hash_code(&typeAClass),
           "rthandle hash deterministic (same handle twice)");
    {
        Il2CppClass aliasOfTypeA = {
            "T.TypeA", 8, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
        };
        Expect(il2cpp_rthandle_get_hash_code(&typeAClass) ==
                   il2cpp_rthandle_get_hash_code(&aliasOfTypeA),
               "rthandle hash equal handles (same name) hash equal");
    }

    // --- 4. rthandle get_runtime_type ---
    Expect(ManagedStringEqualsAscii(
               il2cpp_rthandle_get_runtime_type(&typeAClass), "T.TypeA"),
           "rthandle get_runtime_type -> \"T.TypeA\"");
    Expect(il2cpp_rthandle_get_runtime_type(nullptr) == nullptr,
           "rthandle get_runtime_type null -> nullptr (net48 default)");

    // --- 5. type roundtrip: handle -> name string -> registry -> descriptor
    //    (the same descriptor identity comes back -- the handle->Type
    //    direction round-trips through the Type value) ---
    {
        il2cpp_typeinfo_register(&typeAInfo);
        const Il2CppTypeInfo* info = il2cpp_typeinfo_lookup("T.TypeA");
        Expect(info != nullptr && info->klass == &typeAClass,
               "roundtrip registry lookup resolves the descriptor");
        Expect(info != nullptr &&
                   il2cpp_rthandle_equals(const_cast<Il2CppClass*>(info->klass),
                                          &typeAClass) == 1,
               "roundtrip handle equality after registry round-trip");
        Il2CppObject* typeValue = il2cpp_rthandle_get_runtime_type(&typeAClass);
        Expect(ManagedStringEqualsAscii(typeValue, "T.TypeA"),
               "roundtrip GetRuntimeType output is the full-name string");
    }

    // --- 6. rmethodhandle get_value ---
    Expect(il2cpp_rmethodhandle_get_value(&methodEntries[0]) ==
               reinterpret_cast<intptr_t>(&methodEntries[0]),
           "rmethodhandle get_value -> the entry address");
    Expect(il2cpp_rmethodhandle_get_value(nullptr) == 0,
           "rmethodhandle get_value null -> 0");

    // --- 7. rmethodhandle equals ---
    Expect(il2cpp_rmethodhandle_equals(&methodEntries[0], &methodEntries[0]) == 1,
           "rmethodhandle equals same entry -> 1");
    {
        // Distinct entry, identical (declaringType, name, signature) -> the
        // SAME method (content identity, the corert Equals model).
        Il2CppMethodInfo aliasOfAdd = {
            "Add", "I:i:i", 2, 1, -1, "T.TypeA", reinterpret_cast<void*>(0x9),
        };
        Expect(il2cpp_rmethodhandle_equals(&methodEntries[0], &aliasOfAdd) == 1,
               "rmethodhandle equals distinct entries same identity -> 1");
    }
    Expect(il2cpp_rmethodhandle_equals(&methodEntries[0], &methodEntries[1]) == 0,
           "rmethodhandle equals different name -> 0");
    {
        // Overload: same declaringType + name, different signature.
        Il2CppMethodInfo addOverload = {
            "Add", "I:i", 1, 1, -1, "T.TypeA", reinterpret_cast<void*>(0x4),
        };
        Expect(il2cpp_rmethodhandle_equals(&methodEntries[0], &addOverload) == 0,
               "rmethodhandle equals overload (different signature) -> 0");
    }
    Expect(il2cpp_rmethodhandle_equals(&methodEntries[0], &methodEntries[2]) == 0,
           "rmethodhandle equals different declaringType -> 0");
    Expect(il2cpp_rmethodhandle_equals(nullptr, nullptr) == 1,
           "rmethodhandle equals null/null -> 1");
    Expect(il2cpp_rmethodhandle_equals(&methodEntries[0], nullptr) == 0,
           "rmethodhandle equals real vs null -> 0");
    Expect(il2cpp_rmethodhandle_equals(nullptr, &methodEntries[1]) == 0,
           "rmethodhandle equals null vs real -> 0");

    // --- 8. rmethodhandle get_hash_code ---
    Expect(il2cpp_rmethodhandle_get_hash_code(nullptr) == 0,
           "rmethodhandle hash null -> 0");
    Expect(il2cpp_rmethodhandle_get_hash_code(&methodEntries[0]) ==
               il2cpp_rmethodhandle_get_hash_code(&methodEntries[0]),
           "rmethodhandle hash deterministic");
    {
        Il2CppMethodInfo aliasOfAdd = {
            "Add", "I:i:i", 2, 1, -1, "T.TypeA", reinterpret_cast<void*>(0x9),
        };
        Expect(il2cpp_rmethodhandle_get_hash_code(&methodEntries[0]) ==
                   il2cpp_rmethodhandle_get_hash_code(&aliasOfAdd),
               "rmethodhandle hash equal identities hash equal");
    }

    // --- 9. rfieldhandle get_value ---
    Expect(il2cpp_rfieldhandle_get_value(&fieldEntries[0]) ==
               reinterpret_cast<intptr_t>(&fieldEntries[0]),
           "rfieldhandle get_value -> the entry address");
    Expect(il2cpp_rfieldhandle_get_value(nullptr) == 0,
           "rfieldhandle get_value null -> 0");

    // --- 10. rfieldhandle equals ---
    Expect(il2cpp_rfieldhandle_equals(&fieldEntries[0], &fieldEntries[0]) == 1,
           "rfieldhandle equals same entry -> 1");
    {
        // Distinct entry, identical (name, typeName) -> the SAME field
        // (content identity).
        Il2CppFieldInfo aliasOfX = {
            "x", "System.Int32", 8, 0, nullptr, 1,
        };
        Expect(il2cpp_rfieldhandle_equals(&fieldEntries[0], &aliasOfX) == 1,
               "rfieldhandle equals distinct entries same identity -> 1");
    }
    Expect(il2cpp_rfieldhandle_equals(&fieldEntries[0], &fieldEntries[1]) == 0,
           "rfieldhandle equals different name -> 0");
    Expect(il2cpp_rfieldhandle_equals(&fieldEntries[0], &fieldEntries[2]) == 0,
           "rfieldhandle equals different typeName -> 0");
    Expect(il2cpp_rfieldhandle_equals(nullptr, nullptr) == 1,
           "rfieldhandle equals null/null -> 1");
    Expect(il2cpp_rfieldhandle_equals(&fieldEntries[0], nullptr) == 0,
           "rfieldhandle equals real vs null -> 0");
    Expect(il2cpp_rfieldhandle_equals(nullptr, &fieldEntries[1]) == 0,
           "rfieldhandle equals null vs real -> 0");

    // --- 11. rfieldhandle get_hash_code ---
    Expect(il2cpp_rfieldhandle_get_hash_code(nullptr) == 0,
           "rfieldhandle hash null -> 0");
    Expect(il2cpp_rfieldhandle_get_hash_code(&fieldEntries[0]) ==
               il2cpp_rfieldhandle_get_hash_code(&fieldEntries[0]),
           "rfieldhandle hash deterministic");
    {
        Il2CppFieldInfo aliasOfX = {
            "x", "System.Int32", 8, 0, nullptr, 1,
        };
        Expect(il2cpp_rfieldhandle_get_hash_code(&fieldEntries[0]) ==
                   il2cpp_rfieldhandle_get_hash_code(&aliasOfX),
               "rfieldhandle hash equal identities hash equal");
    }

    // --- 12. rthandle degenerate: null-named descriptor ---
    {
        // A descriptor with a NULL name folds to 0 (the Djb2Fold null path) --
        // deterministic, matching the null-handle hash (0). The name-equality
        // rule of icall_type_equals requires NON-null names, so a null-named
        // descriptor is never equal to a distinct descriptor.
        Il2CppClass nullNamedA = {
            nullptr, 8, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
        };
        Il2CppClass nullNamedB = {
            nullptr, 8, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
        };
        Expect(il2cpp_rthandle_get_hash_code(&nullNamedA) == 0,
               "rthandle hash null-named descriptor -> 0 (Djb2Fold null path)");
        Expect(il2cpp_rthandle_get_hash_code(&nullNamedA) ==
                   il2cpp_rthandle_get_hash_code(&nullNamedA),
               "rthandle hash null-named descriptor deterministic");
        Expect(il2cpp_rthandle_equals(&nullNamedA, &nullNamedB) == 0,
               "rthandle equals two distinct null-named descriptors -> 0 (name rule)");
        Expect(il2cpp_rthandle_equals(&nullNamedA, &typeAClass) == 0,
               "rthandle equals null-named vs named -> 0");
    }

    // --- 13. rmethodhandle degenerate: null identity components ---
    {
        Il2CppMethodInfo noDecl = {
            "Add", "I:i:i", 2, 1, -1, nullptr, reinterpret_cast<void*>(0x7),
        };
        Il2CppMethodInfo noSig = {
            "Add", nullptr, 0, 1, -1, "T.TypeA", reinterpret_cast<void*>(0x8),
        };
        // A null identity component is never equal to a real entry (equals
        // requires all three components non-null).
        Expect(il2cpp_rmethodhandle_equals(&noDecl, &methodEntries[0]) == 0,
               "rmethodhandle equals null declaringType vs real -> 0");
        Expect(il2cpp_rmethodhandle_equals(&methodEntries[0], &noSig) == 0,
               "rmethodhandle equals real vs null signature -> 0");
        // The hash still folds deterministically (null folds as 0 bytes --
        // the Djb2Fold null path) -- never a crash, repeatable.
        Expect(il2cpp_rmethodhandle_get_hash_code(&noDecl) ==
                   il2cpp_rmethodhandle_get_hash_code(&noDecl),
               "rmethodhandle hash null-declaringType entry deterministic");
        Expect(il2cpp_rmethodhandle_get_hash_code(&noSig) ==
                   il2cpp_rmethodhandle_get_hash_code(&noSig),
               "rmethodhandle hash null-signature entry deterministic");
    }

    // --- 14. rfieldhandle degenerate + DOCUMENTED DIVERGENCE pin ---
    {
        Il2CppFieldInfo noType = {
            "x", nullptr, 0, 0, nullptr, 1,
        };
        Expect(il2cpp_rfieldhandle_equals(&noType, &fieldEntries[0]) == 0,
               "rfieldhandle equals null typeName vs real -> 0");
        Expect(il2cpp_rfieldhandle_get_hash_code(&noType) ==
                   il2cpp_rfieldhandle_get_hash_code(&noType),
               "rfieldhandle hash null-typeName entry deterministic");
        // DOCUMENTED DIVERGENCE (reflecthandle.h): Il2CppFieldInfo carries no
        // declaring-type dimension, so a same-named same-typed field entry
        // from a DIFFERENT declaring type compares EQUAL here. net48 field
        // handles compare (declaringType, fieldName) and would call the two
        // DIFFERENT (probe: typeof(string).GetField("Empty") vs
        // typeof(Guid).GetField("Empty") are not equal). The entry below is a
        // second "x" of "System.Int32" -- the fixture models a field of the
        // same name+type on another class. The pin keeps the divergence
        // explicit: flipping it requires Il2CppFieldInfo.declaringType
        // (transpiler emission) and a deliberate semantic change.
        Il2CppFieldInfo otherClassX = {
            "x", "System.Int32", 16, 0, nullptr, 1,
        };
        Expect(il2cpp_rfieldhandle_equals(&fieldEntries[0], &otherClassX) == 1,
               "rfieldhandle equals same name+typeName across declaring types -> 1 (DOCUMENTED DIVERGENCE)");
        Expect(il2cpp_rfieldhandle_get_hash_code(&fieldEntries[0]) ==
                   il2cpp_rfieldhandle_get_hash_code(&otherClassX),
               "rfieldhandle hash equal (name, typeName) across declaring types -> equal hash");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d reflecthandle assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime reflecthandle tests passed\n");
    return 0;
}
