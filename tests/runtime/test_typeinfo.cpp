//------------------------------------------------------------------------------
// test_typeinfo.cpp -- runtime reflection metadata table unit tests
// (Il2CppTypeInfo registry + simplified member queries; see typeinfo.h).
//
// Coverage (normal + edge + error paths):
//   1. Registry -- il2cpp_typeinfo_register followed by
//      il2cpp_typeinfo_lookup by full type name (string key -> table);
//      unknown name -> null; null name -> null; duplicate registration is
//      idempotent (lookup still resolves to the same table).
//   2. get_property -- own table hit; inherited hit through the baseType
//      chain (with indexer entry); miss -> null; null info / null name
//      -> null.
//   3. get_method -- own table hit; inherited hit through the chain;
//      most-derived declaration wins over a same-named base method
//      (.NET shadowing); a trimmed method (fnPtr == null) is still found;
//      miss -> null.
//   4. get_field -- own table hit; inherited hit; static field entry
//      (offset -1 + staticAddr) surfaces as stored; miss -> null.
//   5. TypeInfo layout landing -- interfaces table / baseType chain /
//      klass back-pointer are reachable as drafted (design section 2.1).
//
// Plain executable + minimal assertion harness (see test_typeext.cpp).
//------------------------------------------------------------------------------

#include "typeinfo.h"

#include "il2cpp_array.h"     // il2cpp_array_new / il2cpp_array_get_elem_ptr
#include "il2cpp_exception.h" // DotNetException / il2cpp_exception_matches
#include "il2cpp_object.h"    // Il2CppObject (managed-string query tests)
#include "il2cpp_runtime.h"   // il2cpp_box_int32 / il2cpp_unbox_int32
#include "il2cpp_boxing_ext2.h" // icall_box_long / icall_unbox_long
#include "il2cpp_string.h"    // il2cpp_string_new_from_cs

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

// Run 'fn' and report whether it threw a DotNetException matching the
// managed type name (chain conformance -- the same rule the generated catch
// clauses use; see test_reflect_base.cpp).
template <typename Fn>
bool ThrowsManaged(Fn&& fn, const char* managedType)
{
    try
    {
        fn();
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

void DummyMethod() {}
int AddTwoInts(int a, int b) { return a + b; }

// GAP-5 (P1): generated ctor ABIs -- void(C* this, args...) writes the field.
struct CtorHostStorage { void* hdr; int32_t value; };
static void CtorWriteInt(void* self, int32_t v) { reinterpret_cast<CtorHostStorage*>(self)->value = v; }
static void CtorWriteSeven(void* self) { reinterpret_cast<CtorHostStorage*>(self)->value = 7; }

// RF-1 instance-'this' AOT shapes: the transpiler emits INSTANCE methods with
// arg0 = this (RootSlotPass:40), so the reflected entry's fnPtr reads the
// instance as its first argument and the field lives after the klass header.
struct InvokeInstanceLayout
{
    il2cpp::Il2CppObject hdr;
    int32_t fld;
};
int InstAddTo(il2cpp::Il2CppObject* self, int a, int b)
{
    return reinterpret_cast<InvokeInstanceLayout*>(self)->fld + a + b;
}
int InstRead(il2cpp::Il2CppObject* self)
{
    return reinterpret_cast<InvokeInstanceLayout*>(self)->fld;
}
void InstBump(il2cpp::Il2CppObject* self)
{
    reinterpret_cast<InvokeInstanceLayout*>(self)->fld += 1;
}

// Managed string content equality against an ASCII literal (project
// convention: names are ASCII) -- the shape test_typeext.cpp uses.
bool ManagedEqualsAscii(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    auto str = reinterpret_cast<il2cpp::Il2CppString*>(s);    const size_t len = std::strlen(ascii);
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
// Fixture: a 2-level hierarchy + one interface, mirroring what TypeEmitter
// would generate per reflected type (static const tables, design 2.1).
//
//   System.Object  (root -- baseType chain terminates here)
//   T.IFoo         (interface type info; referenced from T.Derived)
//   T.Base         (methods: Ctor/Add/Foo; fields: x/_s; properties:
//                    Value/Item(indexer))
//   T.Derived      (derives T.Base; methods: Foo (shadows base Foo)/
//                    Trimmed (fnPtr null); fields: y; properties: Name)
//------------------------------------------------------------------------------

il2cpp::Il2CppClass objectClass = {
    "System.Object", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass baseClass = {
    "T.Base", 16, 0u, 0, &objectClass, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass derivedClass = {
    "T.Derived", 16, 0u, 0, &baseClass, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass ifaceClass = {
    "T.IFoo", 0, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppObject* g_baseStaticStringSlot = nullptr;

il2cpp::Il2CppMethodInfo baseMethods[3] = {
    {"Ctor", "V:System.Void", 0, 5, -1, "T.Base", reinterpret_cast<void*>(&DummyMethod)},
    {"Add", "I:i:i", 2, 1, -1, "T.Base", reinterpret_cast<void*>(&DummyMethod)},
    {"Foo", "V:System.Void", 0, 9, 0, "T.Base", reinterpret_cast<void*>(&DummyMethod)},
};

il2cpp::Il2CppFieldInfo baseFields[2] = {
    {"x", "System.Int32", 0, 0, nullptr, 1},
    {"_s", "System.String", -1, 1, &g_baseStaticStringSlot, 3}, // public|static (raudit3 visibility filter)
};

il2cpp::Il2CppPropertyInfo baseProperties[2] = {
    {"Value", "System.Int32", "get_Value", "set_Value", 0},
    {"Item", "System.Int32", "get_Item", "set_Item", 1},
};

il2cpp::Il2CppMethodInfo derivedMethods[2] = {
    {"Foo", "V:System.Void", 0, 9, 0, "T.Derived", reinterpret_cast<void*>(&DummyMethod)},
    {"Trimmed", "V:System.Void", 0, 1, -1, "T.Derived", nullptr},
};

il2cpp::Il2CppFieldInfo derivedFields[1] = {
    {"y", "System.Int32", 0, 0, nullptr, 1},
};

il2cpp::Il2CppPropertyInfo derivedProperties[1] = {
    {"Name", "System.String", "get_Name", nullptr, 0},
};

// C3-1 / RF-N1: the event sub-tables (Il2CppEventInfo). baseInfo carries a
// base-declared event (inherited by T.Derived), derivedInfo an own event --
// the enumeration walks BOTH tables through the base chain.
il2cpp::Il2CppEventInfo baseEvents[1] = {
    {"ValueChanged",
     {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
      0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}, // the kComIidIUnknown bytes
     "add_ValueChanged", "remove_ValueChanged"},
};

il2cpp::Il2CppEventInfo derivedEvents[1] = {
    {"Changed", {0}, "add_Changed", "remove_Changed"},
};

// System.Object's own (empty) type info -- root of the baseType chain.
il2cpp::Il2CppTypeInfo objectInfoInstance = {
    "System.Object", &objectClass, nullptr, nullptr, 0,
    nullptr, 0, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppTypeInfo baseInfo = {
    "T.Base", &baseClass, &objectInfoInstance, nullptr, 0,
    baseFields, 2, baseMethods, 3, baseProperties, 2, baseEvents, 1,
};

il2cpp::Il2CppTypeInfo derivedInfo = {
    "T.Derived", &derivedClass, &baseInfo, nullptr, 0,
    derivedFields, 1, derivedMethods, 2, derivedProperties, 1, derivedEvents, 1,
};

il2cpp::Il2CppTypeInfo ifaceInfo = {
    "T.IFoo", &ifaceClass, nullptr, nullptr, 0,
    nullptr, 0, nullptr, 0, nullptr, 0,
};

// DEFECT-F2 (audit dual-review): every fixture REGISTERED into the runtime
// registry must be FILE-SCOPE static -- block-scoped objects dangle after
// their `{}` exits and the registry holds THEIR ADDRESS, so a later lookup/
// scan reads freed/reused stack memory (Release -O3 -DNDEBUG optimization-
// sensitive: three of three runs hit 0xC0000005 in the Finder/FieldInfo
// walks; Debug -O0 and printf-interposed builds masked the UB). This region
// hoists the four block-registered fixtures (R2 FieldHost, R3 InvokeHost,
// RF-1 InstHost, GAP-3 AccHost) exactly as the GAP-1 g_flagsInfo / GAP-5
// ctorHostInfo fixture convention already does. Only live stack objects
// (target instances the tests construct from these descriptors) stay
// block-local -- they are never registered.
namespace {

struct FieldHost { void* hdr; int32_t i32; int64_t i64; void* refSlot; };

il2cpp::Il2CppClass hostClass = {
    "T.FieldHost", static_cast<int32_t>(sizeof(FieldHost)), 0u, 0,
    &objectClass, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppObject* g_hostStaticString = nullptr;
int64_t g_hostStaticI64 = 0;
int32_t g_hostStaticSingle = 0; // static Single storage is int32_t (no wide slot)
int32_t g_hostStaticDouble = 0; // static Double storage is int32_t (no wide slot)
il2cpp::Il2CppFieldInfo hostFields[7] = {
    {"fi32", "System.Int32", 8, 0, nullptr, 1},
    {"fi64", "System.Int64", 16, 0, nullptr, 1},
    {"fref", "System.String", 24, 1, nullptr, 1},
    {"fstat", "System.String", -1, 1, &g_hostStaticString, 3},
    {"fstat64", "System.Int64", -1, 0, &g_hostStaticI64, 3},
    {"fstatf", "System.Single", -1, 0, &g_hostStaticSingle, 3},
    {"fstatd", "System.Double", -1, 0, &g_hostStaticDouble, 3},
};
il2cpp::Il2CppTypeInfo hostInfo = {
    "T.FieldHost", &hostClass, &objectInfoInstance, nullptr, 0,
    hostFields, 7, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass invokeClass = {
    "T.InvokeHost", 16, 0u, 0, &objectClass, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppMethodInfo invokeMethods[2] = {
    {"AddTwo", "I:i,i", 2, 1 | 2, -1, "T.InvokeHost", reinterpret_cast<void*>(&AddTwoInts)},
    {"Nope",   "I:i",   1, 1, -1, "T.InvokeHost", nullptr},
};
il2cpp::Il2CppTypeInfo invokeInfo = {
    "T.InvokeHost", &invokeClass, &objectInfoInstance, nullptr, 0,
    nullptr, 0, invokeMethods, 2, nullptr, 0,
};

il2cpp::Il2CppClass instClass = {
    "T.InstHost", 16, 0u, 0, &objectClass, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppMethodInfo instMethods[3] = {
    {"AddTo", "I:i,i", 2, 1, -1, "T.InstHost", reinterpret_cast<void*>(&InstAddTo)},
    {"Read",  "I",     0, 1, -1, "T.InstHost", reinterpret_cast<void*>(&InstRead)},
    {"Bump",  "V",     0, 1, -1, "T.InstHost", reinterpret_cast<void*>(&InstBump)},
};
il2cpp::Il2CppTypeInfo instInfo = {
    "T.InstHost", &instClass, &objectInfoInstance, nullptr, 0,
    nullptr, 0, instMethods, 3, nullptr, 0,
};

il2cpp::Il2CppMethodInfo accMethods[2] = {
    {"get_Acc", "i()", 0, 1, -1, "T.AccHost", reinterpret_cast<void*>(&AddTwoInts)},
    {"set_Acc", "v(i)", 1, 1, -1, "T.AccHost", reinterpret_cast<void*>(&AddTwoInts)},
};
il2cpp::Il2CppPropertyInfo accProps[1] = {
    {"Acc", "System.Int32", "get_Acc", "set_Acc", 0},
};
il2cpp::Il2CppTypeInfo accInfo = {
    "T.AccHost", &baseClass, &objectInfoInstance, nullptr, 0,
    nullptr, 0, accMethods, 2, accProps, 1,
};

} // namespace

int main()
{
    using namespace il2cpp;

    const Il2CppTypeInfo* ifaces[] = { &ifaceInfo };
    derivedInfo.interfaces = ifaces;
    derivedInfo.interfaceCount = 1;

    // --- 1. Registry ---
    il2cpp_typeinfo_register(&objectInfoInstance);
    il2cpp_typeinfo_register(&baseInfo);
    il2cpp_typeinfo_register(&derivedInfo);
    il2cpp_typeinfo_register(&ifaceInfo);
    il2cpp_typeinfo_register(&baseInfo); // duplicate -- must be a no-op

    Expect(il2cpp_typeinfo_lookup("T.Base") == &baseInfo,
           "lookup \"T.Base\" -> baseInfo");
    Expect(il2cpp_typeinfo_lookup("T.Derived") == &derivedInfo,
           "lookup \"T.Derived\" -> derivedInfo");
    Expect(il2cpp_typeinfo_lookup("T.IFoo") == &ifaceInfo,
           "lookup \"T.IFoo\" -> ifaceInfo");
    Expect(il2cpp_typeinfo_lookup("System.Object") == &objectInfoInstance,
           "lookup \"System.Object\" -> objectInfoInstance");
    Expect(il2cpp_typeinfo_lookup("T.Never") == nullptr,
           "lookup unregistered name -> null");
    Expect(il2cpp_typeinfo_lookup(nullptr) == nullptr,
           "lookup null name -> null");
    Expect(il2cpp_typeinfo_lookup("T.Base") == &baseInfo,
           "lookup after duplicate register still resolves (idempotent)");

    // --- 2. get_property ---
    const Il2CppPropertyInfo* p = il2cpp_type_get_property(&derivedInfo, "Name");
    Expect(p == &derivedProperties[0], "get_property derived own \"Name\"");
    Expect(p != nullptr && p->getterName != nullptr &&
               std::strcmp(p->getterName, "get_Name") == 0,
           "get_property own entry carries getter name");
    p = il2cpp_type_get_property(&derivedInfo, "Value");
    Expect(p == &baseProperties[0],
           "get_property inherited \"Value\" through base chain");
    p = il2cpp_type_get_property(&derivedInfo, "Item");
    Expect(p == &baseProperties[1],
           "get_property inherited indexer \"Item\" through base chain");
    Expect(p != nullptr && p->indexParams == 1,
           "indexer entry carries indexParams == 1");
    Expect(il2cpp_type_get_property(&derivedInfo, "Nope") == nullptr,
           "get_property missing -> null");
    Expect(il2cpp_type_get_property(&objectInfoInstance, "Value") == nullptr,
           "get_property on empty-table root -> null");
    Expect(ThrowsManaged([&] { (void)il2cpp_type_get_property(nullptr, "Name"); },
                         "System.NullReferenceException"),
           "get_property null info -> NullReferenceException (net48 null receiver)");
    Expect(ThrowsManaged([&] { (void)il2cpp_type_get_property(&derivedInfo, nullptr); },
                         "System.ArgumentNullException"),
           "get_property null name -> ArgumentNullException (net48)");

    // --- 3. get_method ---
    const Il2CppMethodInfo* m = il2cpp_type_get_method(&derivedInfo, "Add");
    Expect(m == &baseMethods[1],
           "get_method inherited \"Add\" through base chain");
    Expect(m != nullptr && std::strcmp(m->declaringType, "T.Base") == 0,
           "inherited method carries declaringType \"T.Base\"");
    m = il2cpp_type_get_method(&derivedInfo, "Foo");
    Expect(m == &derivedMethods[0],
           "get_method most-derived \"Foo\" wins over base shadow");
    Expect(m != nullptr && std::strcmp(m->declaringType, "T.Derived") == 0,
           "shadowing method carries declaringType \"T.Derived\"");
    m = il2cpp_type_get_method(&derivedInfo, "Trimmed");
    Expect(m == &derivedMethods[1],
           "get_method trimmed entry (fnPtr null) still found by name");
    Expect(m != nullptr && m->fnPtr == nullptr,
           "trimmed entry fnPtr is null as stored");
    Expect(il2cpp_type_get_method(&derivedInfo, "Nope") == nullptr,
           "get_method missing -> null");
    Expect(il2cpp_type_get_method(&objectInfoInstance, "Add") == nullptr,
           "get_method on empty-table root -> null");
    Expect(ThrowsManaged([&] { (void)il2cpp_type_get_method(nullptr, "Add"); },
                         "System.NullReferenceException"),
           "get_method null info -> NullReferenceException (net48 null receiver)");
    Expect(ThrowsManaged([&] { (void)il2cpp_type_get_method(&derivedInfo, nullptr); },
                         "System.ArgumentNullException"),
           "get_method null name -> ArgumentNullException (net48)");

    // --- 4. get_field ---
    const Il2CppFieldInfo* f = il2cpp_type_get_field(&derivedInfo, "y");
    Expect(f == &derivedFields[0], "get_field derived own \"y\"");
    f = il2cpp_type_get_field(&derivedInfo, "x");
    Expect(f == &baseFields[0],
           "get_field inherited \"x\" through base chain");
    f = il2cpp_type_get_field(&derivedInfo, "_s");
    Expect(f == &baseFields[1], "get_field inherited static \"_s\"");
    Expect(f != nullptr && f->offset == -1 && f->staticAddr == &g_baseStaticStringSlot,
           "static field entry carries offset -1 + staticAddr");
    Expect(il2cpp_type_get_field(&derivedInfo, "Nope") == nullptr,
           "get_field missing -> null");
    Expect(ThrowsManaged([&] { (void)il2cpp_type_get_field(nullptr, "x"); },
                         "System.NullReferenceException"),
           "get_field null info -> NullReferenceException (net48 null receiver)");
    Expect(ThrowsManaged([&] { (void)il2cpp_type_get_field(&derivedInfo, nullptr); },
                         "System.ArgumentNullException"),
           "get_field null name -> ArgumentNullException (net48, empty ParamName)");

    // --- 5. TypeInfo layout landing (design 2.1 fields reachable) ---
    Expect(derivedInfo.baseType == &baseInfo,
           "derived baseType points at baseInfo");
    Expect(derivedInfo.klass == &derivedClass,
           "derived klass back-pointer reaches the class descriptor");
    Expect(derivedInfo.interfaceCount == 1 && derivedInfo.interfaces[0] == &ifaceInfo,
           "derived interfaces table + count landed");
    Expect(std::strcmp(derivedInfo.name, "T.Derived") == 0,
           "derived name matches its class descriptor name");

    // --- 6. Managed-string ABI queries (the transpiler icall surface:
    //    Type.GetProperty/GetMethod/GetField with the simplified Type model
    //    -- a Type VALUE is the full name string, see il2cpp_typeext.h) ---
    Il2CppObject* hit = il2cpp_typeinfo_get_property(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Name")));
    Expect(hit != nullptr && ManagedEqualsAscii(hit, "Name"),
           "string-ABI get_property own \"Name\" -> member-name string");
    hit = il2cpp_typeinfo_get_property(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Value")));
    Expect(hit != nullptr && ManagedEqualsAscii(hit, "Value"),
           "string-ABI get_property inherited \"Value\" through base chain");
    hit = il2cpp_typeinfo_get_property(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Nope")));
    Expect(hit == nullptr,
           "string-ABI get_property missing -> null");
    hit = il2cpp_typeinfo_get_property(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Never")),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Name")));
    Expect(hit == nullptr,
           "string-ABI get_property unregistered type -> null");
    Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_get_property(nullptr,
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Name"))); },
        "System.NullReferenceException"),
        "string-ABI get_property null typeName -> NullReferenceException (net48)");
    Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_get_property(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        nullptr); },
        "System.ArgumentNullException"),
        "string-ABI get_property null memberName -> ArgumentNullException (net48)");

    hit = il2cpp_typeinfo_get_method(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Add")));
    Expect(hit != nullptr && ManagedEqualsAscii(hit, "Add"),
           "string-ABI get_method inherited \"Add\" -> member-name string");
    hit = il2cpp_typeinfo_get_method(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Nope")));
    Expect(hit == nullptr, "string-ABI get_method missing -> null");
    Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_get_method(nullptr,
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Add"))); },
        "System.NullReferenceException"),
        "string-ABI get_method null typeName -> NullReferenceException (net48)");
    Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_get_method(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        nullptr); },
        "System.ArgumentNullException"),
        "string-ABI get_method null memberName -> ArgumentNullException (net48)");

    hit = il2cpp_typeinfo_get_field(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("x")));
    Expect(hit != nullptr && ManagedEqualsAscii(hit, "x"),
           "string-ABI get_field inherited \"x\" -> member-name string");
    hit = il2cpp_typeinfo_get_field(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Nope")));
    Expect(hit == nullptr, "string-ABI get_field missing -> null");
    Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_get_field(nullptr,
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("x"))); },
        "System.NullReferenceException"),
        "string-ABI get_field null typeName -> NullReferenceException (net48)");
    Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_get_field(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
        nullptr); },
        "System.ArgumentNullException"),
        "string-ABI get_field null memberName -> ArgumentNullException (net48)");

    // --- R2 (B2): member ENUMERATION icalls (the transpiler
    //    Type.GetMethods/GetFields/GetProperties/GetConstructors/
    //    GetInterfaces surface -- each returns an Il2CppArray* of member-NAME
    //    managed strings, own table first then the baseType chain, public
    //    only). The fixture hierarchy: T.Derived owns Foo/Trimmed methods,
    //    y field, Name property; T.Base owns Ctor/Add/Foo methods, x/_s
    //    fields, Value/Item properties; T.IFoo is the implemented interface.
    //    Public-only visibility (raudit3 net48 default binding) is asserted
    //    via the _s static public field and the ctor entries.
    {
        auto names = il2cpp_typeinfo_get_methods(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")));
        Expect(names != nullptr, "R2 get_methods returns a non-null array");
        Expect(names != nullptr && names->length == 4,
               "GAP-4 get_methods own+base public NON-ctor methods count (Derived Foo/Trimmed + Base Add/Foo -- net48 GetMethods excludes ctors)");
        bool hasFoo = false, hasAdd = false, hasCtor = false, hasTrimmed = false;
        if (names != nullptr)
        {
            for (int32_t i = 0; i < names->length; ++i)
            {
                auto e = *reinterpret_cast<Il2CppObject**>(
                    il2cpp::il2cpp_array_get_elem_ptr(names, i));
                if (ManagedEqualsAscii(e, "Foo")) hasFoo = true;
                if (ManagedEqualsAscii(e, "Add")) hasAdd = true;
                if (ManagedEqualsAscii(e, "Ctor")) hasCtor = true;
                if (ManagedEqualsAscii(e, "Trimmed")) hasTrimmed = true;
            }
        }
        Expect(hasFoo && hasAdd && hasTrimmed && !hasCtor,
               "GAP-4 get_methods covers public non-ctor names and EXCLUDES the ctor (net48 GetMethods doctrine)");

        auto fields = il2cpp_typeinfo_get_fields(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")));
        Expect(fields != nullptr && fields->length == 3,
               "R2 get_fields own+base public fields count (Derived y + Base x/_s)");
        bool hasX = false, hasS = false, hasY = false;
        if (fields != nullptr)
        {
            for (int32_t i = 0; i < fields->length; ++i)
            {
                auto e = *reinterpret_cast<Il2CppObject**>(
                    il2cpp::il2cpp_array_get_elem_ptr(fields, i));
                if (ManagedEqualsAscii(e, "x")) hasX = true;
                if (ManagedEqualsAscii(e, "_s")) hasS = true;
                if (ManagedEqualsAscii(e, "y")) hasY = true;
            }
        }
        Expect(hasX && hasS && hasY,
               "R2 get_fields names cover own+base public fields");

        auto props = il2cpp_typeinfo_get_properties(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")));
        Expect(props != nullptr && props->length == 3,
               "R2 get_properties own+base count (Derived Name + Base Value/Item)");
        bool hasName = false, hasValue = false, hasItem = false;
        if (props != nullptr)
        {
            for (int32_t i = 0; i < props->length; ++i)
            {
                auto e = *reinterpret_cast<Il2CppObject**>(
                    il2cpp::il2cpp_array_get_elem_ptr(props, i));
                if (ManagedEqualsAscii(e, "Name")) hasName = true;
                if (ManagedEqualsAscii(e, "Value")) hasValue = true;
                if (ManagedEqualsAscii(e, "Item")) hasItem = true;
            }
        }
        Expect(hasName && hasValue && hasItem,
               "R2 get_properties names cover own+base properties");

        // C3-1 / RF-N1: GetEvents -- own + base-chain event name array.
        auto events = il2cpp_typeinfo_get_events(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")));
        Expect(events != nullptr && events->length == 2,
               "C3-1 get_events own+base count (Derived Changed + Base ValueChanged)");
        bool hasChanged = false, hasValueChanged = false;
        if (events != nullptr)
        {
            for (int32_t i = 0; i < events->length; ++i)
            {
                auto e = *reinterpret_cast<Il2CppObject**>(
                    il2cpp::il2cpp_array_get_elem_ptr(events, i));
                if (ManagedEqualsAscii(e, "Changed")) hasChanged = true;
                if (ManagedEqualsAscii(e, "ValueChanged")) hasValueChanged = true;
            }
        }
        Expect(hasChanged && hasValueChanged,
               "C3-1 get_events names cover own+base events");

        // C3-1: GetEvent(name) -- the single-event query (own + base chain).
        const Il2CppEventInfo* ev = il2cpp_type_get_event(&derivedInfo, "Changed");
        Expect(ev == &derivedEvents[0], "C3-1 get_event derived own \"Changed\"");
        ev = il2cpp_type_get_event(&derivedInfo, "ValueChanged");
        Expect(ev == &baseEvents[0], "C3-1 get_event inherited \"ValueChanged\"");
        Expect(ev != nullptr && ev->addName != nullptr &&
                   std::strcmp(ev->addName, "add_ValueChanged") == 0,
               "C3-1 inherited event carries the add accessor name");
        Expect(il2cpp_type_get_event(&derivedInfo, "Nope") == nullptr,
               "C3-1 get_event missing -> null");
        Expect(ThrowsManaged([&] { (void)il2cpp_type_get_event(nullptr, "Changed"); },
                             "System.NullReferenceException"),
               "C3-1 get_event null info -> NullReferenceException (net48)");
        Expect(ThrowsManaged([&] { (void)il2cpp_type_get_event(&derivedInfo, nullptr); },
                             "System.ArgumentNullException"),
               "C3-1 get_event null name -> ArgumentNullException (net48)");

        // C3-1: the managed-string GetEvent(name) icall returns the event-NAME
        // string on a hit, null on a miss (the simplified member model).
        auto evHit = il2cpp_typeinfo_get_event(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Changed")));
        Expect(ManagedEqualsAscii(evHit, "Changed"),
               "C3-1 typeinfo_get_event returns the event name string on a hit");
        auto evMiss = il2cpp_typeinfo_get_event(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")),
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Nope")));
        Expect(evMiss == nullptr,
               "C3-1 typeinfo_get_event missing -> null");

        auto evEmpty = il2cpp_typeinfo_get_events(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Never")));
        Expect(evEmpty != nullptr && evEmpty->length == 0,
               "C3-1 get_events unregistered type -> empty array (never null)");

        auto ctors = il2cpp_typeinfo_get_constructors(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")));
        Expect(ctors != nullptr && ctors->length == 1,
               "R2 get_constructors public ctor count (Base Ctor only)");

        auto ifaces = il2cpp_typeinfo_get_interfaces(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Derived")));
        Expect(ifaces != nullptr && ifaces->length == 1,
               "R2 get_interfaces own+base count (T.IFoo)");
        if (ifaces != nullptr && ifaces->length == 1)
        {
            auto e = *reinterpret_cast<Il2CppObject**>(
                il2cpp::il2cpp_array_get_elem_ptr(ifaces, 0));
            Expect(ManagedEqualsAscii(e, "T.IFoo"),
                   "R2 get_interfaces element is the interface full name");
        }
        // Unregistered / malformed typeName -> EMPTY array, never null (the
        // R2 enumeration contract -- net48 returns an empty array, never
        // null, for the no-members / unreflected type).
        auto none = il2cpp_typeinfo_get_methods(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.Never")));
        Expect(none != nullptr && none->length == 0,
               "R2 get_methods unregistered type -> empty array");
        auto nullArr = il2cpp_typeinfo_get_methods(nullptr);
        Expect(nullArr != nullptr && nullArr->length == 0,
               "R2 get_methods null typeName -> empty array (never null)");
    }

    // --- R2 (B2): FieldInfo.GetValue/SetValue narrow set (int32/string/
    //    object slots; the field's typeName dispatches the slot width). The
    //    receiver is the FIELD-NAME string; the declaring type is joined from
    //    the target object's klass. net48 GetValue boxes value-type fields
    //    (int32 -> System.Int32 box, int64 -> System.Int64 box) and returns
    //    the reference itself for ref fields; SetValue writes through the box
    //    payload / reference slot. Unknown / private fields -> nullptr
    //    (defensive query convention). DEFECT-F1 (audit 2A): instances of the
    //    WIDE kinds (Int64/UInt64/Single/Double) are transpiler-COLLAPSED to
    //    int32_t slots and LOUD-reject (NO silent 8-byte overrun into the
    //    neighbor field); only STATIC Int64/UInt64 keep the real 8B carrier
    //    (static Single/Double have no wide slot either -- LOUD too).
    {
        // A dedicated reflected type whose FIELD TABLE offsets match a real
        // instance layout (header slot 0; i32 at 8; i64 at 16; ref slot at
        // 24; the static string slot is separate storage). The fixture data
        // (FieldHost / hostClass / hostFields / hostInfo / the static slots)
        // is FILE-SCOPE static (DEFECT-F2): hostInfo is registered into the
        // runtime registry and later GAP-2/F1 scans read it -- block-scoped
        // arrays would dangle after this `{}` exits (Release -O3 AV).
        il2cpp_typeinfo_register(&hostInfo);

        FieldHost inst = { &hostClass, 0, 0, nullptr };
        auto target = reinterpret_cast<Il2CppObject*>(&inst);
        auto nameI32 = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("fi32"));

        inst.i32 = 42;
        Il2CppObject* val = il2cpp_typeinfo_field_get_value(nameI32, target);
        Expect(val != nullptr && il2cpp_unbox_int32(val) == 42,
               "R2 field_get_value int32 field reads the boxed slot value");
        Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_field_get_value(nameI32, nullptr); },
                             "System.NullReferenceException"),
               "R2 field_get_value null target -> NullReferenceException (net48)");
        Expect(il2cpp_typeinfo_field_get_value(
                   reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("nope")),
                   target) == nullptr,
               "R2 field_get_value unknown field -> null (defensive)");

        // SetValue(int32): write through the box payload.
        auto boxed = il2cpp_box_int32(nullptr, 7);
        il2cpp_typeinfo_field_set_value(nameI32, target, boxed);
        Expect(inst.i32 == 7, "R2 field_set_value int32 writes through the box");

        // int64: box/unbox via the long carriers (slot width by typeName).
        // DEFECT-F1 (audit 2A): an INSTANCE Int64 field per the transpiler
        // model is COLLAPSED to an int32_t slot (TypeEmitter layout: value
        // fields are "int32_t at slot n*pointerSize") -- the a16afce4
        // MetadataLoader widening made the field table carry the real
        // "System.Int64" name, so the 8-byte read/write would silently
        // overrun into the neighbor field. F1: a non-static Int64 field is
        // LOUD-rejected (explicit NotSupportedException), never a silent
        // overread/overwrite.
        auto nameI64 = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("fi64"));
        Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_field_get_value(nameI64, target); },
                             "System.NotSupportedException"),
               "F1 instance Int64 field GetValue LOUD-rejects (int32 slot, no silent overrun)");
        Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_field_set_value(
                                       nameI64, target, runtime::icall_box_long(99LL)); },
                             "System.NotSupportedException"),
               "F1 instance Int64 field SetValue LOUD-rejects (int32 slot, no silent overrun)");

        // The static Int64 field (flags bit1 static) keeps the 8-byte path:
        // its staticAddr points at a REAL int64_t static slot (F1 preserves
        // the working 8B static carrier).
        auto nameStat64 = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("fstat64"));
        g_hostStaticI64 = 0x1122334455667788LL;
        auto s64val = il2cpp_typeinfo_field_get_value(nameStat64, nullptr);
        Expect(s64val != nullptr && runtime::icall_unbox_long(s64val) == 0x1122334455667788LL,
               "F1 static Int64 field GetValue(null) keeps the 8-byte path");
        il2cpp_typeinfo_field_set_value(nameStat64, nullptr, runtime::icall_box_long(99LL));
        Expect(g_hostStaticI64 == 99, "F1 static Int64 field SetValue(null) keeps the 8-byte path");

        // Static Single/Double: StaticFieldSemantics has NO Single/Double arm,
        // so the static storage is the int32_t default -- the 8-byte carrier
        // is absent. F1: LOUD-reject like the instance wide fields.
        auto nameStatF = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("fstatf"));
        auto nameStatD = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("fstatd"));
        Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_field_get_value(nameStatF, nullptr); },
                             "System.NotSupportedException"),
               "F1 static Single field GetValue(null) LOUD-rejects (no wide static slot)");
        Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_field_set_value(
                                       nameStatF, nullptr, runtime::icall_box_float(1.5)); },
                             "System.NotSupportedException"),
               "F1 static Single field SetValue(null) LOUD-rejects (no wide static slot)");
        Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_field_get_value(nameStatD, nullptr); },
                             "System.NotSupportedException"),
               "F1 static Double field GetValue(null) LOUD-rejects (no wide static slot)");
        Expect(ThrowsManaged([&] { (void)il2cpp_typeinfo_field_set_value(
                                       nameStatD, nullptr, runtime::icall_box_float(2.5)); },
                             "System.NotSupportedException"),
               "F1 static Double field SetValue(null) LOUD-rejects (no wide static slot)");

        // Reference field: returned/stored as the reference itself.
        auto nameRef = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("fref"));
        auto refVal = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("hello"));
        inst.refSlot = refVal;
        auto rval = il2cpp_typeinfo_field_get_value(nameRef, target);
        Expect(rval == refVal && ManagedEqualsAscii(rval, "hello"),
               "R2 field_get_value ref field returns the reference itself");
        auto refVal2 = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("world"));
        il2cpp_typeinfo_field_set_value(nameRef, target, refVal2);
        Expect(inst.refSlot == refVal2, "R2 field_set_value ref field writes the reference");

        // Static field: stored through staticAddr (offset -1).
        auto nameStat = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("fstat"));
        auto statVal = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("stat"));
        il2cpp_typeinfo_field_set_value(nameStat, target, statVal);
        auto statRead = il2cpp_typeinfo_field_get_value(nameStat, target);
        Expect(statRead == statVal && ManagedEqualsAscii(statRead, "stat"),
               "R2 field_set/get static reference field through staticAddr");

        // GAP-2 (P1): a STATIC field's GetValue(null)/SetValue(null) is legal
        // on net48 -- the declaring type is joined by the registry scan (no
        // target klass). The singleton static "fstat" resolves unambiguously.
        auto statNullRead = il2cpp_typeinfo_field_get_value(nameStat, nullptr);
        Expect(statNullRead == statVal && ManagedEqualsAscii(statNullRead, "stat"),
               "GAP-2 static field GetValue(null) resolves by registry scan (net48)");
        auto statNullVal = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("nullset"));
        il2cpp_typeinfo_field_set_value(nameStat, nullptr, statNullVal);
        Expect(g_hostStaticString == statNullVal && ManagedEqualsAscii(g_hostStaticString, "nullset"),
               "GAP-2 static field SetValue(null, v) writes through staticAddr (net48)");
    }

    // --- R3 (B4): MethodInfo.Invoke -- il2cpp_methodinfo_invoke(memberName,
    //    target, object[] args) joins the declaring type from the target's
    //    klass, resolves the Il2CppMethodInfo by name (+ paramCount
    //    disambiguation), and dispatches through il2cpp_invoker_invoke. The
    //    result is boxed by the return kind (int32/int64/ref; void -> null).
    //    A trimmed (fnPtr null) method is runtime-unreachable -> LOUD.
    //    RF-1: the committed probe is a FREE function (no 'this') -- it is
    //    registered with the STATIC flag (bit1 == 1) so the no-this path is
    //    preserved; INSTANCE methods (static bit clear) now get the target
    //    injected as the 'this' slot (verified in the second block below).
    {
        // GAP-1 (P0): the BindingFlags-aware member query primitives. A
        // dedicated fixture with a public/private method, field, property pair
        // plus a static member -- the flags filter (Public=16, NonPublic=32,
        // Instance=4, Static=8) matches net48 single-member queries.
        // GAP-1 flags fixture data is FILE-SCOPE static: it is registered into the
    // runtime registry and later member scans (GAP-3's property accessor walk)
    // read it -- block-scoped arrays would dangle after their `{}` exits.
    static il2cpp::Il2CppClass g_flagsClass = {
        "T.FlagsHost", 16, 0u, 0, &objectClass, nullptr, 0, nullptr, 0,
    };
    static il2cpp::Il2CppMethodInfo g_flagsMethods[5] = {
        {"Pub", "i()", 0, 1, -1, "T.FlagsHost", reinterpret_cast<void*>(&AddTwoInts)},
        {"Priv", "i()", 0, 0, -1, "T.FlagsHost", reinterpret_cast<void*>(&AddTwoInts)},
        {"Stat", "i()", 0, 1 | 2, -1, "T.FlagsHost", reinterpret_cast<void*>(&AddTwoInts)},
        // the property accessors (public) -- resolved UNFILTERED by the
        // property-visibility walk, then the public filter applies.
        {"get_pubP", "i()", 0, 1, -1, "T.FlagsHost", reinterpret_cast<void*>(&AddTwoInts)},
        {"set_pubP", "v(i)", 1, 1, -1, "T.FlagsHost", reinterpret_cast<void*>(&AddTwoInts)},
    };
    static il2cpp::Il2CppFieldInfo g_flagsFields[2] = {
        {"pubF", "System.Int32", 8, 0, nullptr, 1},
        {"privF", "System.Int32", 12, 0, nullptr, 0},
    };
    static il2cpp::Il2CppPropertyInfo g_flagsProps[1] = {
        {"pubP", "System.Int32", "get_pubP", "set_pubP", 0},
    };
    static il2cpp::Il2CppTypeInfo g_flagsInfo = {
        "T.FlagsHost", &g_flagsClass, &objectInfoInstance, nullptr, 0,
        g_flagsFields, 2, g_flagsMethods, 5, g_flagsProps, 1,
    };
    il2cpp_typeinfo_register(&g_flagsInfo);
    const Il2CppTypeInfo* flagsInfoResolved = il2cpp_typeinfo_lookup("T.FlagsHost");

        // GetMethod: default (Public|Instance|Static) binds only public.
        Expect(il2cpp_type_get_method_flags(flagsInfoResolved, "Pub", 16 | 4 | 8) != nullptr,
               "GAP-1 get_method_flags public method found under default binding");
        Expect(il2cpp_type_get_method_flags(flagsInfoResolved, "Priv", 16 | 4 | 8) == nullptr,
               "GAP-1 get_method_flags private method hidden under default binding");
        Expect(il2cpp_type_get_method_flags(flagsInfoResolved, "Priv", 32 | 4) != nullptr,
               "GAP-1 get_method_flags NonPublic|Instance finds the private method");
        Expect(il2cpp_type_get_method_flags(flagsInfoResolved, "Stat", 16 | 8) != nullptr,
               "GAP-1 get_method_flags Public|Static finds the static method");
        Expect(il2cpp_type_get_method_flags(flagsInfoResolved, "Stat", 16 | 4) == nullptr,
               "GAP-1 get_method_flags Public|Instance hides the static method");

        // GetField: public-only default; NonPublic admits the private field.
        Expect(il2cpp_type_get_field_flags(flagsInfoResolved, "pubF", 16 | 4 | 8) != nullptr,
               "GAP-1 get_field_flags public field under default binding");
        Expect(il2cpp_type_get_field_flags(flagsInfoResolved, "privF", 16 | 4 | 8) == nullptr,
               "GAP-1 get_field_flags private field hidden under default binding");
        Expect(il2cpp_type_get_field_flags(flagsInfoResolved, "privF", 32 | 4) != nullptr,
               "GAP-1 get_field_flags NonPublic|Instance finds the private field");

        // GetProperty: visibility derives from the accessor methods (get_pubP
        // is public -> the property is visible under the default binding).
        Expect(il2cpp_type_get_property_flags(flagsInfoResolved, "pubP", 16 | 4 | 8) != nullptr,
               "GAP-1 get_property_flags public-accessor property visible by default");
    }
    {
        // Fixture data (invokeClass / invokeMethods / invokeInfo) is FILE-SCOPE
        // static -- DEFECT-F2: invokeInfo is registered into the runtime
        // registry, block-scoped tables would dangle after this `{}`.
        il2cpp_typeinfo_register(&invokeInfo);

        // object[] { box(2), box(3) }
        auto args = il2cpp_array_new(nullptr, sizeof(void*), true, 2);
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(args, 0)) =
            il2cpp_box_int32(nullptr, 2);
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(args, 1)) =
            il2cpp_box_int32(nullptr, 3);
        auto target2 = reinterpret_cast<Il2CppObject*>(&invokeClass);

        auto memberName = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("AddTwo"));
        Il2CppObject* result = il2cpp_methodinfo_invoke(memberName, target2, args);
        Expect(result != nullptr && il2cpp_unbox_int32(result) == 5,
               "R3 methodinfo_invoke dispatches int32 AddTwo(2,3) -> boxed 5");

        // Unknown method -> nullptr (defensive query convention).
        auto unknown = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("NoSuch"));
        Expect(il2cpp_methodinfo_invoke(unknown, target2, args) == nullptr,
               "R3 methodinfo_invoke unknown member -> null");

        // Null target -> NullReferenceException (net48 instance doctrine).
        Expect(ThrowsManaged([&] { (void)il2cpp_methodinfo_invoke(memberName, nullptr, args); },
                             "System.NullReferenceException"),
               "R3 methodinfo_invoke null target -> NullReferenceException");

        // A trimmed method (fnPtr null) is runtime-unreachable -> LOUD.
        auto trimmed = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Nope"));
        Expect(ThrowsManaged([&] { (void)il2cpp_methodinfo_invoke(trimmed, target2, args); },
                             "System.NotSupportedException"),
               "R3 methodinfo_invoke trimmed fnPtr -> LOUD");
    }

    // --- RF-1: INSTANCE MethodInfo.Invoke 'this' injection. An instance
    //    method (static bit clear) must have the TARGET injected as argbuf
    //    slot 0 -- the AOT function reads the instance as arg0 -- while the
    //    object[] maps the EXPLICIT params. Zero-explicit-arg and void
    //    instance methods exercise the pure-'this' dispatch shapes ("I"/"V"
    //    -> "I:r"/"V:r").
    {
        // Fixture data (instClass / instMethods / instInfo) is FILE-SCOPE
        // static -- DEFECT-F2: instInfo is registered into the runtime
        // registry, block-scoped tables would dangle after this `{}`.
        il2cpp_typeinfo_register(&instInfo);

        // A fake IL2CPP instance: klass header + an int32 field at offset 0.
        InvokeInstanceLayout inst = { { &instClass }, 25 };
        auto tgt = reinterpret_cast<Il2CppObject*>(&inst);

        // object[] { box(3), box(4) } -- instance AddTo(3,4) reads its own
        // field: 25 + 3 + 4 == 32 (a missing 'this' yields garbage / crash).
        auto iargs = il2cpp_array_new(nullptr, sizeof(void*), true, 2);
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(iargs, 0)) =
            il2cpp_box_int32(nullptr, 3);
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(iargs, 1)) =
            il2cpp_box_int32(nullptr, 4);
        auto nameAdd = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("AddTo"));
        Il2CppObject* ir = il2cpp_methodinfo_invoke(nameAdd, tgt, iargs);
        Expect(ir != nullptr && il2cpp_unbox_int32(ir) == 32,
               "RF-1 methodinfo_invoke injects 'this' (instance AddTo reads its own field)");

        // Zero-explicit-arg instance method: signature "I" -> "I:r", the
        // argbuf carries ONLY the injected 'this'.
        auto emptyArgs = il2cpp_array_new(nullptr, sizeof(void*), true, 0);
        auto nameRead = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Read"));
        Il2CppObject* rr = il2cpp_methodinfo_invoke(nameRead, tgt, emptyArgs);
        Expect(rr != nullptr && il2cpp_unbox_int32(rr) == 25,
               "RF-1 'this' injection with zero explicit args (Read -> 25)");

        // Void instance method mutates the real target's state.
        auto nameBump = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Bump"));
        (void)il2cpp_methodinfo_invoke(nameBump, tgt, emptyArgs);
        Expect(inst.fld == 26, "RF-1 void instance method writes the real target state");
    }

    // --- GAP-3 (P1): PropertyInfo.GetGetMethod()/GetSetMethod() ---
    //    il2cpp_typeinfo_property_get_accessor resolves the property by the
    //    deepest-declaring-type registry scan and returns the PUBLIC accessor
    //    NAME string (a MethodInfo VALUE) or null (missing accessor). The
    //    fixture's property table carries getterName/setterName and the OWN
    //    method table carries the public accessors (the single source of
    //    truth the visibility walk resolves through).
    {
        // Fixture data (accMethods / accProps / accInfo) is FILE-SCOPE static
        // -- DEFECT-F2: accInfo is registered into the runtime registry and
        // later property-accessor scans read it; block-scoped tables would
        // dangle after this `{}`.
        il2cpp_typeinfo_register(&accInfo);

        auto getter = il2cpp_typeinfo_property_get_accessor(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Acc")), 0);
        Expect(getter != nullptr && ManagedEqualsAscii(getter, "get_Acc"),
               "GAP-3 property_get_accessor(Acc, 0) -> the public getter name");
        auto setter = il2cpp_typeinfo_property_get_accessor(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Acc")), 1);
        Expect(setter != nullptr && ManagedEqualsAscii(setter, "set_Acc"),
               "GAP-3 property_get_accessor(Acc, 1) -> the public setter name");
        auto unknown = il2cpp_typeinfo_property_get_accessor(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("Nope")), 0);
        Expect(unknown == nullptr,
               "GAP-3 property_get_accessor unknown property -> null (defensive)");
    }

    // --- GAP-5 (P1): ConstructorInfo -- Type.GetConstructor(Type[]) +
    //    ConstructorInfo.Invoke(object[]). The ctor member VALUE is the
    //    disambiguated "<FullName>::.ctor" carrier; get_constructor returns it
    //    on a PUBLIC-ctor arity match and ctor_invoke allocates + runs the
    //    ctor on a fresh instance through il2cpp_methodinfo_invoke's 'this'
    //    injection. The fixture data is FILE-SCOPE static (registered into the
    //    runtime registry; block-scoped arrays would dangle -- the GAP-1
    //    lesson).
    {
        static il2cpp::Il2CppClass ctorHostClass = {
            "T.CtorHost", static_cast<int32_t>(sizeof(CtorHostStorage)), 0u, 0,
            &objectClass, nullptr, 0, nullptr, 0,
        };
        static il2cpp::Il2CppMethodInfo ctorMethods[3] = {
            {".ctor", "V:i", 1, 1 | 4, -1, "T.CtorHost", reinterpret_cast<void*>(&CtorWriteInt)},
            {".ctor", "V", 0, 1 | 4, -1, "T.CtorHost", reinterpret_cast<void*>(&CtorWriteSeven)},
            {"Sum", "I", 0, 1, -1, "T.CtorHost", reinterpret_cast<void*>(&AddTwoInts)},
        };
        static il2cpp::Il2CppTypeInfo ctorHostInfo = {
            "T.CtorHost", &ctorHostClass, &objectInfoInstance, nullptr, 0,
            nullptr, 0, ctorMethods, 3, nullptr, 0,
        };
        il2cpp_typeinfo_register(&ctorHostInfo);

        auto empty = il2cpp_array_new(nullptr, sizeof(void*), true, 0);
        auto one = il2cpp_array_new(nullptr, sizeof(void*), true, 1);
        *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(one, 0)) =
            il2cpp_box_int32(nullptr, 55);

        // GetConstructor(Type[]) with an empty Type[] -> the parameterless ctor.
        auto gc0 = il2cpp_typeinfo_get_constructor(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.CtorHost")), empty);
        Expect(gc0 != nullptr && ManagedEqualsAscii(gc0, "T.CtorHost::.ctor"),
               "GAP-5 get_constructor(empty) -> the disambiguated ctor member value");
        auto gc1 = il2cpp_typeinfo_get_constructor(
            reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("T.CtorHost")), one);
        Expect(gc1 != nullptr && ManagedEqualsAscii(gc1, "T.CtorHost::.ctor"),
               "GAP-5 get_constructor(one int32) -> the arity-matched ctor value");

        // Invoke: allocates + runs the ctor on a fresh instance; the returned
        // object carries the ctor-written state.
        auto obj0 = il2cpp_constructorinfo_invoke(gc0, empty);
        Expect(obj0 != nullptr && reinterpret_cast<CtorHostStorage*>(obj0)->value == 7,
               "GAP-5 ctor_invoke runs the parameterless ctor on a fresh instance");
        auto obj1 = il2cpp_constructorinfo_invoke(gc1, one);
        Expect(obj1 != nullptr && reinterpret_cast<CtorHostStorage*>(obj1)->value == 55,
               "GAP-5 ctor_invoke runs the (int) ctor with the boxed arg");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d typeinfo assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime typeinfo tests passed\n");
    return 0;
}

