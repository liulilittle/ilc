//------------------------------------------------------------------------------
// test_reflect_base.cpp -- reflection base pair unit tests
// (Activator.CreateInstance(string) + Type.GetType(string); see
// reflect_base.h). reflection-status-v2 R-REFL-07/08.
//
// Coverage (normal + edge + error paths; mirrors test_typeinfo.cpp's
// register-first fixture style):
//   1. get_type -- registered type -> the INPUT name string itself
//      (identity: the simplified Type model, no allocation); unregistered
//      -> null; null -> ArgumentNullException (net48 Type.GetType(null),
//      verified against v4.0.30319 -- the query's one throwing case); empty
//      name -> null (net48 verified); whitespace-padded name -> null
//      (net48: no trim, verified); non-ASCII name -> null; over-long name
//      -> null (Type.GetType "not found -> null", never throws on a
//      not-found name).
//   2. create_instance -- register-first ordering: creating an
//      unregistered name throws MissingMethodException; after registration
//      the same name creates a fresh instance (klass == the registered
//      descriptor, zero-init allocation path); two calls yield distinct
//      objects; null name -> ArgumentNullException; a registered type with
//      a null klass descriptor -> MissingMethodException; malformed
//      (non-ASCII / over-long) names -> MissingMethodException.
//
// Plain executable + minimal assertion harness (see test_typeinfo.cpp).
//------------------------------------------------------------------------------

#include "reflect_base.h"

#include "typeinfo.h"           // registry fixtures + lookup
#include "il2cpp_object.h"      // Il2CppObject / klass stamp check
#include "il2cpp_string.h"      // il2cpp_string_new_from_cs
#include "il2cpp_exception.h"   // DotNetException / il2cpp_exception_matches

#include <cstddef>
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

// Catch an icall throw and report whether it was a MissingMethodException /
// ArgumentNullException (chain conformance -- the same rule the generated
// catch clauses use).
bool ThrowsManaged(il2cpp::Il2CppObject* (*fn)(il2cpp::Il2CppObject*),
                   il2cpp::Il2CppObject* arg, const char* managedType)
{
    try
    {
        fn(arg);
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches(managedType, e.type_name()) != 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

} // namespace

//------------------------------------------------------------------------------
// Fixture: two registrable types + one klass-less entry (a "reflected but
// not instantiable" shape), mirroring the static const tables TypeEmitter
// emits per reflected type (design 2.1, same shape as test_typeinfo.cpp).
//------------------------------------------------------------------------------

// The raudit2 activator contract: a registered type is instantiated by
// RUNNING its parameterless .ctor (the TypeEmitter-generated method table
// carries the .ctor entry with fnPtr). The fixture must therefore expose the
// same shape, or CreateInstance correctly throws MissingMethodException.
void WidgetCtor(il2cpp::Il2CppObject*) {}

il2cpp::Il2CppMethodInfo widgetMethods[1] = {
    {".ctor", "V:System.Void", 0, 4, -1, "T.Widget", reinterpret_cast<void*>(&WidgetCtor)},
};

il2cpp::Il2CppClass widgetClass = {
    "T.Widget", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppTypeInfo widgetInfo = {
    "T.Widget", &widgetClass, nullptr, nullptr, 0,
    nullptr, 0, widgetMethods, 1, nullptr, 0,
};

// Registered but with NO class descriptor (klass == nullptr) -- the
// registry accepts the table, but instantiation must fail loudly.
il2cpp::Il2CppTypeInfo noKlassInfo = {
    "T.NoKlass", nullptr, nullptr, nullptr, 0,
    nullptr, 0, nullptr, 0, nullptr, 0,
};

// A NESTED-type key (net48 FullName convention: the declaring type and the
// nested type are joined by '+', e.g. "Outer+Inner"). The registry stores
// the key verbatim, so NormalizeTypeName must PRESERVE the '+' and never
// confuse it with the '.' of a namespace (net48: GetType("Outer+Inner")
// resolves, GetType("Outer.Inner") does NOT -- probe-verified).
il2cpp::Il2CppClass nestedClass = {
    "T.Outer+Inner", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppTypeInfo nestedInfo = {
    "T.Outer+Inner", &nestedClass, nullptr, nullptr, 0,
    nullptr, 0, nullptr, 0, nullptr, 0,
};

int main()
{
    using namespace il2cpp;

    auto str = [](const char* ascii) {
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(ascii));
    };

    // --- 1. Type.GetType(string) -- lookup semantics ---
    // Unregistered BEFORE registration: "not found" -> null (no throw).
    Expect(il2cpp_type_get_type(str("T.Widget")) == nullptr,
           "get_type before register -> null (not found)");

    // Register first (the generated startup ordering), then query.
    il2cpp_typeinfo_register(&widgetInfo);
    il2cpp_typeinfo_register(&noKlassInfo);

    Il2CppObject* widgetName = str("T.Widget");
    Expect(il2cpp_type_get_type(widgetName) == widgetName,
           "get_type registered -> the INPUT string itself (identity)");
    Expect(il2cpp_type_get_type(str("T.NoKlass")) != nullptr,
           "get_type registered klass-less type -> the name (Type exists)");
    Expect(il2cpp_type_get_type(str("T.Never")) == nullptr,
           "get_type unregistered -> null");
    Expect(ThrowsManaged(il2cpp_type_get_type, nullptr,
                         "System.ArgumentNullException"),
           "get_type null -> ArgumentNullException (net48 GetType(null))");

    // net48-verified edge names: "" -> null (not found, no throw);
    // whitespace-padded -> null (no trim); both escape to "not found".
    Expect(il2cpp_type_get_type(str("")) == nullptr,
           "get_type empty name -> null (net48: not found)");
    Expect(il2cpp_type_get_type(str("  T.Widget  ")) == nullptr,
           "get_type whitespace-padded name -> null (net48: no trim)");

    // Malformed inputs escape to "not found" (transcoding convention).
    {
        Il2CppString* mixed = il2cpp_string_new_from_cs("T.Widget");
        mixed->chars[1] = 0x4E2D; // inject a non-ASCII char
        Expect(il2cpp_type_get_type(reinterpret_cast<Il2CppObject*>(mixed)) == nullptr,
               "get_type non-ASCII name -> null");
    }
    {
        char longName[300];
        for (int i = 0; i < 299; ++i)
        {
            longName[i] = 'A';
        }
        longName[299] = '\0';
        Expect(il2cpp_type_get_type(str(longName)) == nullptr,
               "get_type over-long name -> null");
    }

    // --- 1b. NormalizeTypeName -- assembly-qualified name resolution
    //    (reflection mode C, plan §3.4 / C3; net48 semantics probe-verified).
    //    Registration is idempotent, so the extra fixture entry is safe.
    il2cpp_typeinfo_register(&nestedInfo);

    // Strip the assembly qualifier at the FIRST top-level comma:
    // "Ns.Type, Assembly" -> "Ns.Type"; a full assembly name (more commas)
    // still resolves by the type-name part only.
    Expect(il2cpp_type_get_type(str("T.Widget, SomeAsm")) != nullptr,
           "get_type assembly-qualified user type resolves (qualifier stripped)");
    Expect(il2cpp_type_get_type(str("T.Widget, SomeAsm, Version=1.0.0.0, "
                                    "Culture=neutral, PublicKeyToken=null")) != nullptr,
           "get_type full assembly name resolves by the type-name part");
    Expect(il2cpp_type_get_type(str("No.Such.Type, AnyAsm")) == nullptr,
           "get_type assembly-qualified UNKNOWN type -> null");
    Expect(il2cpp_type_get_type(str(",mscorlib")) == nullptr,
           "get_type empty type-name part (leading comma) -> null");
    Expect(il2cpp_type_get_type(str("T.Widget,")) == nullptr,
           "get_type empty assembly part (trailing comma) -> null");
    Expect(il2cpp_type_get_type(str("T.Widget, ")) == nullptr,
           "get_type whitespace-only assembly part -> null");
    Expect(il2cpp_type_get_type(str("T.Widget, , ,")) == nullptr,
           "get_type empty assembly segments (', , ,') -> null (net48)");
    Expect(il2cpp_type_get_type(str("   ")) == nullptr,
           "get_type whitespace-only name -> null (no type-name part, net48)");

    // Nested '+': the '+' separator is PRESERVED (net48: GetType("Outer+Inner")
    // resolves, GetType("Outer.Inner") does NOT -- the dot form is a miss).
    Expect(il2cpp_type_get_type(str("T.Outer+Inner")) != nullptr,
           "get_type nested '+' name resolves (separator preserved)");
    Expect(il2cpp_type_get_type(str("T.Outer.Inner")) == nullptr,
           "get_type nested DOT name -> null (net48: '+' is not a '.' alias)");
    Expect(il2cpp_type_get_type(str("T.Outer+Inner, SomeAsm")) != nullptr,
           "get_type nested '+' + assembly qualifier resolves");

    // Quotes: a quoted ASSEMBLY name is accepted (net48: GetType("Ns.T, \"A\"")
    // resolves); quotes are discarded with the assembly part.
    Expect(il2cpp_type_get_type(str("T.Widget, \"SomeAsm\"")) != nullptr,
           "get_type quoted assembly name resolves");
    Expect(il2cpp_type_get_type(str("T.Widget, \"SomeAsm, v1\"")) != nullptr,
           "get_type quoted assembly with internal comma resolves");

    // Leading whitespace is trimmed (net48: " System.Int32" resolves);
    // trailing whitespace is NOT (net48: "System.Int32 " -> null).
    Expect(il2cpp_type_get_type(str(" T.Widget")) != nullptr,
           "get_type leading-space name resolves (whitespace trimmed)");
    Expect(il2cpp_type_get_type(str("T.Widget ")) == nullptr,
           "get_type trailing-space name -> null (trailing ws preserved)");

    // BCL fallback: the builtin class registry resolves System.* types even
    // without a typeinfo table (gettype_bcl contract: Type.GetType
    // ("System.Int32") is non-null, exactly like net48's mscorlib lookup).
    Expect(il2cpp_type_get_type(str("System.Int32")) != nullptr,
           "get_type BCL fallback: System.Int32 resolves");
    Expect(il2cpp_type_get_type(str("System.Int32, mscorlib")) != nullptr,
           "get_type BCL fallback with assembly qualifier resolves");
    Expect(il2cpp_type_get_type(str("System.String")) != nullptr,
           "get_type BCL fallback: System.String resolves");
    Expect(il2cpp_type_get_type(str("System.NoSuch")) == nullptr,
           "get_type unknown System.* -> null (no builtin descriptor)");

    // --- 2. Activator.CreateInstance(string) -- instantiation semantics ---
    // Unregistered BEFORE registration: MissingMethodException.
    Expect(ThrowsManaged(il2cpp_activator_create_instance, str("T.Never"),
                         "System.MissingMethodException"),
           "create unregistered -> MissingMethodException");
    Expect(ThrowsManaged(il2cpp_activator_create_instance, nullptr,
                         "System.ArgumentNullException"),
           "create null -> ArgumentNullException");

    // Register FIRST, then create (the generated startup ordering).
    il2cpp_typeinfo_register(&widgetInfo); // duplicate -- idempotent no-op

    Il2CppObject* a = il2cpp_activator_create_instance(str("T.Widget"));
    Expect(a != nullptr && a->klass == &widgetClass,
           "create registered -> instance stamped with the registered klass");
    Il2CppObject* b = il2cpp_activator_create_instance(str("T.Widget"));
    Expect(b != nullptr && b != a,
           "create twice -> two distinct instances");
    Expect(il2cpp_activator_create_instance(widgetName) != nullptr &&
               il2cpp_activator_create_instance(widgetName)->klass == &widgetClass,
           "create after duplicate register still resolves (idempotent)");

    // Registered but klass-less: the registry knows the name, but there is
    // no descriptor to instantiate -> MissingMethodException.
    Expect(ThrowsManaged(il2cpp_activator_create_instance, str("T.NoKlass"),
                         "System.MissingMethodException"),
           "create klass-less registered type -> MissingMethodException");

    // Malformed names escape to "not found" -> MissingMethodException.
    {
        Il2CppString* mixed = il2cpp_string_new_from_cs("T.Widget");
        mixed->chars[1] = 0x4E2D;
        Expect(ThrowsManaged(il2cpp_activator_create_instance,
                             reinterpret_cast<Il2CppObject*>(mixed),
                             "System.MissingMethodException"),
               "create non-ASCII name -> MissingMethodException");
    }
    {
        char longName[300];
        for (int i = 0; i < 299; ++i)
        {
            longName[i] = 'A';
        }
        longName[299] = '\0';
        Expect(ThrowsManaged(il2cpp_activator_create_instance, str(longName),
                             "System.MissingMethodException"),
               "create over-long name -> MissingMethodException");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d reflect_base assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime reflect_base tests passed\n");
    return 0;
}
