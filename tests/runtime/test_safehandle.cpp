//------------------------------------------------------------------------------
// test_safehandle.cpp -- System.Runtime.InteropServices.SafeHandle minimal
// reference-counting unit tests.
//
// Coverage (normal + edge + error paths):
//   1. A fresh DangerousAddRef writes success = true and the count grows:
//      AddRef -> Release -> Release releases (zero ref count) -> a further
//      Release throws ObjectDisposedException (unbalanced).
//   2. DangerousAddRef on a released (closed) handle throws
//      ObjectDisposedException and never writes the success slot.
//   3. SetHandleAsInvalid marks the handle closed: a following AddRef throws.
//   4. SetHandleAsInvalid is idempotent.
//   5. A fresh handle's implicit construction reference can be released once
//      (Release straight after first touch releases the handle).
//   6. Distinct receivers are tracked independently.
//   7. A null receiver throws NullReferenceException.
//   8. A balanced AddRef/Release pair keeps the handle open (no early close).
//   9. The thrown ObjectDisposedException carries the full net48 inheritance
//      chain (ObjectDisposedException -> InvalidOperationException ->
//      SystemException -> Exception), so generated catch clauses follow .NET
//      base-class matching.
//   10. SetHandleAsInvalid leaves the ref count usable: a following Release is
//       balanced (decrements, does not throw) -- only a Release past zero
//       throws, exactly net48's "closed bit only" behavior.
//
// The receivers are opaque object addresses (any Il2CppObject* -- the icalls
// never dereference them, only use them as table keys); managed strings serve
// as convenient objects.
//------------------------------------------------------------------------------

#include "safehandle.h"
#include "il2cpp_exception.h"
#include "il2cpp_string.h"

#include <cstdio>
#include <string>

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

// Capturing-lambda friendly throw probes (the test_marshalext ThrowsDotNet
// template precedent -- a void(*)() signature cannot capture the handle).
template <typename Fn>
bool ThrowsObjectDisposed(Fn&& fn)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::string(e.type_name()).find("System.ObjectDisposedException") != std::string::npos;
    }
}

// Capture the thrown exception's full type-name chain ("" when nothing threw).
// The chain is what generated catch clauses match against, so the tests can
// pin net48 base-class conformance (il2cpp_exception_matches) directly.
template <typename Fn>
std::string ObjectDisposedChainOf(Fn&& fn)
{
    try
    {
        fn();
        return "";
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::string(e.type_name());
    }
}

template <typename Fn>
bool ThrowsNullReference(Fn&& fn)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::string(e.type_name()).find("System.NullReferenceException") != std::string::npos;
    }
}

} // namespace

int main()
{
    using il2cpp::runtime::icall_safehandle_dangerous_add_ref;
    using il2cpp::runtime::icall_safehandle_dangerous_release;
    using il2cpp::runtime::icall_safehandle_set_handle_as_invalid;
    using il2cpp::il2cpp_exception_matches;

    // --- 1. AddRef -> Release -> Release releases -> Release throws ---
    {
        il2cpp::Il2CppObject* h = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("h1"));
        int32_t success = 0;
        icall_safehandle_dangerous_add_ref(h, &success);
        Expect(success == 1, "fresh AddRef writes success = true");
        icall_safehandle_dangerous_release(h);
        icall_safehandle_dangerous_release(h); // zero ref count -> released
        Expect(ThrowsObjectDisposed([&] { icall_safehandle_dangerous_release(h); }),
               "a release past zero throws ObjectDisposedException");
    }

    // --- 2. AddRef on a released (closed) handle throws; success untouched ---
    {
        il2cpp::Il2CppObject* h = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("h2"));
        icall_safehandle_dangerous_release(h); // release the construction reference -> closed
        int32_t success = 0;
        Expect(ThrowsObjectDisposed([&] { icall_safehandle_dangerous_add_ref(h, &success); }),
               "AddRef on a released handle throws ObjectDisposedException");
        Expect(success == 0, "the success slot is never written on the closed path");
    }

    // --- 3. SetHandleAsInvalid -> AddRef throws ---
    {
        il2cpp::Il2CppObject* h = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("h3"));
        icall_safehandle_set_handle_as_invalid(h);
        int32_t success = 0;
        Expect(ThrowsObjectDisposed([&] { icall_safehandle_dangerous_add_ref(h, &success); }),
               "AddRef after SetHandleAsInvalid throws ObjectDisposedException");
    }

    // --- 4. SetHandleAsInvalid is idempotent ---
    {
        il2cpp::Il2CppObject* h = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("h4"));
        icall_safehandle_set_handle_as_invalid(h);
        icall_safehandle_set_handle_as_invalid(h);
        Expect(true, "SetHandleAsInvalid is idempotent");
    }

    // --- 5. the implicit construction reference can be released once ---
    {
        il2cpp::Il2CppObject* h = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("h5"));
        icall_safehandle_dangerous_release(h); // releases the construction reference
        int32_t success = 0;
        Expect(ThrowsObjectDisposed([&] { icall_safehandle_dangerous_add_ref(h, &success); }),
               "a released construction reference blocks further AddRef");
    }

    // --- 6. distinct receivers are tracked independently ---
    {
        il2cpp::Il2CppObject* a = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("ha"));
        il2cpp::Il2CppObject* b = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("hb"));
        icall_safehandle_set_handle_as_invalid(a); // a closed, b untouched
        int32_t success = 0;
        icall_safehandle_dangerous_add_ref(b, &success);
        Expect(success == 1, "an open handle is unaffected by another handle's close");
        icall_safehandle_dangerous_release(b);
        icall_safehandle_dangerous_release(b);
        Expect(ThrowsObjectDisposed([&] { icall_safehandle_dangerous_add_ref(a, &success); }),
               "the closed handle stays closed while the other is tracked");
    }

    // --- 7. null receiver throws NullReferenceException ---
    {
        int32_t success = 0;
        Expect(ThrowsNullReference([&] { icall_safehandle_dangerous_add_ref(nullptr, &success); }),
               "AddRef on a null receiver throws NullReferenceException");
        Expect(ThrowsNullReference([&] { icall_safehandle_dangerous_release(nullptr); }),
               "Release on a null receiver throws NullReferenceException");
        Expect(ThrowsNullReference([&] { icall_safehandle_set_handle_as_invalid(nullptr); }),
               "SetHandleAsInvalid on a null receiver throws NullReferenceException");
    }

    // --- 8. AddRef/Release balance keeps the handle usable (no early close) ---
    {
        il2cpp::Il2CppObject* h = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("h8"));
        int32_t success = 0;
        icall_safehandle_dangerous_add_ref(h, &success);   // 2
        icall_safehandle_dangerous_add_ref(h, &success);   // 3
        icall_safehandle_dangerous_release(h);             // 2
        icall_safehandle_dangerous_release(h);             // 1
        icall_safehandle_dangerous_add_ref(h, &success);   // 2 -- still open
        Expect(success == 1, "a balanced AddRef/Release pair keeps the handle open");
        icall_safehandle_dangerous_release(h);             // 1
        icall_safehandle_dangerous_release(h);             // 0 -> released
        Expect(ThrowsObjectDisposed([&] { icall_safehandle_dangerous_add_ref(h, &success); }),
               "the handle closes only when the count reaches zero");
    }

    // --- 9. ObjectDisposedException carries the full net48 inheritance chain ---
    {
        il2cpp::Il2CppObject* h = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("h9"));
        icall_safehandle_dangerous_release(h); // release the construction reference -> closed
        int32_t success = 0;
        const std::string chain = ObjectDisposedChainOf([&] { icall_safehandle_dangerous_add_ref(h, &success); });
        Expect(!chain.empty(), "a closed handle throws ObjectDisposedException");
        Expect(il2cpp_exception_matches("System.ObjectDisposedException", chain.c_str()),
               "the chain matches a catch of ObjectDisposedException itself");
        Expect(il2cpp_exception_matches("System.InvalidOperationException", chain.c_str()),
               "the chain matches a catch of InvalidOperationException (net48 base)");
        Expect(il2cpp_exception_matches("System.SystemException", chain.c_str()),
               "the chain matches a catch of SystemException (net48 base)");
        Expect(il2cpp_exception_matches("System.Exception", chain.c_str()),
               "the chain matches a catch of Exception (net48 root)");
    }

    // --- 10. SetHandleAsInvalid leaves the ref count usable (net48: only the
    //         closed bit is set) -- a following Release is balanced ---
    {
        il2cpp::Il2CppObject* h = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("h10"));
        icall_safehandle_set_handle_as_invalid(h); // closed bit, ref count unchanged (1)
        Expect(!ThrowsObjectDisposed([&] { icall_safehandle_dangerous_release(h); }),
               "Release after SetHandleAsInvalid decrements without throwing (count was 1)");
        // the count is now zero and the handle closed: a further Release is unbalanced
        Expect(ThrowsObjectDisposed([&] { icall_safehandle_dangerous_release(h); }),
               "a Release past the SetHandleAsInvalid zero count throws");
    }

    if (g_failures != 0)
    {
        std::printf("%d safehandle assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime safehandle tests passed\n");
    return 0;
}
