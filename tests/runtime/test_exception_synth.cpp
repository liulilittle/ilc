//------------------------------------------------------------------------------
// test_exception_synth.cpp -- il2cpp_exception_class_by_name (exception_synth.cpp)
//
// Coverage (normal + edge + error paths):
//   1. A MODELLED exception answers its static descriptor (never a synthetic one).
//   2. An unmodelled type gets a descriptor whose name is the full managed name.
//   3. The base comes from the chain's first segment that is NOT the type itself
//      -- ExceptionChain repeats the thrown type as segment 1, so a naive
//      "chain[1] is the base" read would make the descriptor its own parent and
//      the isinst base walk would never terminate.
//   4. Idempotence: the same name answers the SAME pointer, so a second throw of
//      the same type cannot produce a second descriptor.
//   5. isinst semantics: the synthetic descriptor sits under System.Exception, so
//      il2cpp_is_instance answers true for Exception/SystemException and false
//      for an unrelated modelled exception.
//   6. A deep chain synthesises its unmodelled ancestors first (base-first) and
//      the resulting walk terminates.
//   7. A null name answers nullptr; a null chain still yields a usable
//      descriptor rooted at System.Exception.
//------------------------------------------------------------------------------

#include "il2cpp_runtime.h"
#include "il2cpp_metadata.h"

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

bool NameIs(il2cpp::Il2CppClass* k, const char* expected)
{
    return k != nullptr && k->name != nullptr && std::strcmp(k->name, expected) == 0;
}

// Walk the base chain with an explicit bound: a self-cycle would otherwise hang
// the test instead of failing it. Returns the number of steps to reach 'stopAt',
// or -1 when it is not reached within the bound (cycle or wrong base).
int ChainDepth(il2cpp::Il2CppClass* k, il2cpp::Il2CppClass* stopAt)
{
    int depth = 0;
    for (il2cpp::Il2CppClass* cur = k; cur != nullptr && depth < 64; cur = cur->baseClass)
    {
        if (cur == stopAt) return depth;
        ++depth;
    }
    return -1;
}

} // namespace

int main()
{
    // --- 1. a modelled type keeps its static descriptor -----------------------
    {
        il2cpp::Il2CppClass* k = il2cpp::il2cpp_exception_class_by_name("System.Exception", nullptr);
        Expect(k == &System_Exception_class, "modelled type answers the static descriptor");
        Expect(NameIs(k, "System.Exception"), "modelled type keeps its name");
    }

    // --- 2. an unmodelled type is synthesised with its full managed name ------
    {
        il2cpp::Il2CppClass* k = il2cpp::il2cpp_exception_class_by_name(
            "System.FormatException",
            "System.FormatException,System.FormatException,System.SystemException,System.Exception");
        Expect(k != nullptr, "unknown exception gets a descriptor");
        Expect(NameIs(k, "System.FormatException"), "synthetic descriptor carries the full name");
    }

    // --- 3. the base skips the repeated-type segment --------------------------
    // ExceptionChain repeats the thrown type as segment 1; the parent is the
    // first segment that differs from it.
    {
        il2cpp::Il2CppClass* k = il2cpp::il2cpp_exception_class_by_name(
            "System.FormatException",
            "System.FormatException,System.FormatException,System.SystemException,System.Exception");
        Expect(k != nullptr, "format exception descriptor exists");
        Expect(k->baseClass == &System_SystemException_class,
               "base is SystemException, not the type itself");
    }

    // --- 4. idempotence -------------------------------------------------------
    {
        const char* name = "System.Threading.ThreadStateException";
        const char* chain = "System.Threading.ThreadStateException,"
                            "System.Threading.ThreadStateException,"
                            "System.SystemException,System.Exception";
        il2cpp::Il2CppClass* first = il2cpp::il2cpp_exception_class_by_name(name, chain);
        il2cpp::Il2CppClass* second = il2cpp::il2cpp_exception_class_by_name(name, chain);
        Expect(first != nullptr, "thread state exception descriptor exists");
        Expect(first == second, "second lookup answers the same descriptor");
    }

    // --- 5. isinst answers the exception bases --------------------------------
    {
        il2cpp::Il2CppClass* k = il2cpp::il2cpp_exception_class_by_name(
            "System.IO.IOException",
            "System.IO.IOException,System.IO.IOException,System.SystemException,System.Exception");
        Expect(k != nullptr, "io exception descriptor exists");
        il2cpp::Il2CppObject* obj = il2cpp_object_new(k);
        Expect(obj != nullptr, "instance allocated with the synthetic descriptor");
        Expect(il2cpp_is_instance(obj, &System_Exception_class), "instance is an Exception");
        Expect(il2cpp_is_instance(obj, &System_SystemException_class), "instance is a SystemException");
        Expect(!il2cpp_is_instance(obj, &System_ArgumentException_class),
               "instance is NOT an ArgumentException");
    }

    // --- 6. a chain of unmodelled types synthesises base-first and terminates --
    {
        il2cpp::Il2CppClass* k = il2cpp::il2cpp_exception_class_by_name(
            "Cases.MyDerived",
            "Cases.MyDerived,Cases.MyDerived,Cases.MyBase,System.Exception");
        Expect(k != nullptr, "derived user exception descriptor exists");
        Expect(NameIs(k, "Cases.MyDerived"), "derived keeps its own name");

        il2cpp::Il2CppClass* base = il2cpp::il2cpp_exception_class_by_name(
            "Cases.MyBase", "Cases.MyBase,Cases.MyBase,System.Exception");
        Expect(base != nullptr, "base user exception descriptor exists");
        Expect(k->baseClass == base, "derived points at the synthesised base");

        // A cycle here would hang the process, so the bound is the assertion.
        Expect(ChainDepth(k, &System_Exception_class) >= 0,
               "base walk reaches System.Exception without looping");
    }

    // --- 7. null inputs -------------------------------------------------------
    {
        Expect(il2cpp::il2cpp_exception_class_by_name(nullptr, nullptr) == nullptr,
               "null name answers nullptr");

        il2cpp::Il2CppClass* k = il2cpp::il2cpp_exception_class_by_name("Cases.NoChain", nullptr);
        Expect(k != nullptr, "no-chain type still gets a descriptor");
        Expect(k->baseClass == &System_Exception_class,
               "no-chain type is rooted at System.Exception");
    }

    if (g_failures != 0)
    {
        std::printf("test_exception_synth: %d failure(s)\n", g_failures);
        return 1;
    }
    std::printf("test_exception_synth: all checks passed\n");
    return 0;
}
