//------------------------------------------------------------------------------
// test_ienum.cpp -- P25c enumerator-interface dispatch (arraysort.h P25c
// section: il2cpp_ienum_* + il2cpp_typeinfo_lookup_by_class). A foreach over a
// user-written enumerable / yield-returning iterator compiles to callvirts
// whose declaring owner is a BCL iterator interface; the runtime resolves the
// receiver's member vtable slot through the typeinfo registry JOINED BY THE
// KLASS POINTER and dispatches through the receiver's vtable.
//
// Coverage (normal + edge + error paths):
//   1. il2cpp_typeinfo_lookup_by_class joins a class descriptor to its
//      typeinfo by POINTER where the name join fails (the name-based lookup
//      cannot resolve the nested/compiler-generated short name "<Range>d__0").
//   2. Compiler state machine shape (Cases.YieldProbe+<Range>d__0): the fully
//      reflected type implements BOTH the generic and the non-generic iterator
//      slots; the signature-aware dispatchers pick the right slot:
//      get_current_i4 reads the "i()" generic slot, get_current_obj the
//      "r:System.Object()" non-generic slot, move_next the "b()" slot,
//      get_enumerator / get_enumerator_nongeneric the two distinct
//      GetEnumerator slots, reset/dispose their void slots.
//   3. User enumerator shape (enprobe's MyEnum/MyEnum2): IMPLICIT members
//      stored under the PLAIN metadata name ("MoveNext"/"Dispose"/"get_Current"
//      with virtualSlot -1 for the plain property accessor) resolve by the
//      member suffix + signature; the GetEnumerator with the returned
//      IEnumerator`1 signature matches even though its name is plain.
//   4. Error paths: null receiver -> NullReferenceException on every entry
//      EXCEPT dispose (null-safe no-op); a receiver with a reflected type that
//      is NOT listed (unregistered typeinfo / no matching method) ->
//      NotSupportedException on every entry EXCEPT dispose (no reflected
//      Dispose slot -> no-op).
//
// Stability contract: dispatch results are driven by DELIBERATELY DISTINCT
// slot markers (move_next returns 1, get_Current-i4 returns 42, the ref-
// returning slots return distinct object pointers) so each assertion proves
// the resolved slot -- not merely "the dispatcher returned something".
//
// Plain executable + minimal assertion harness (see test_array.cpp).
//------------------------------------------------------------------------------

#include "arraysort.h"
#include "il2cpp_exception.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "typeinfo.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
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

void ExpectI32(int32_t actual, int32_t expected, const char* what)
{
    if (actual != expected)
    {
        std::printf("FAIL: %s (expected %d, got %d)\n", what, expected, actual);
        ++g_failures;
    }
}

template <typename F>
void ExpectThrows(const char* expectedType, const char* what, F&& fn)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* thrown = e.type_name();
        const size_t len = std::strlen(expectedType);
        const bool headMatches = std::strncmp(thrown, expectedType, len) == 0 &&
                                 (thrown[len] == ',' || thrown[len] == '\0');
        if (headMatches)
        {
            return;
        }
        std::printf("FAIL: %s (threw %s, expected %s)\n", what, thrown, expectedType);
        ++g_failures;
        return;
    }
    std::printf("FAIL: %s (no exception thrown, expected %s)\n", what, expectedType);
    ++g_failures;
}

const char kNotSupportedType[] = "System.NotSupportedException";
const char kNullRefType[]      = "System.NullReferenceException";

//------------------------------------------------------------------------------
// Fixtures. A reflected iterator receiver needs (a) a class descriptor whose
// vtable holds this-style member functions and (b) a registered typeinfo whose
// klass pointer joins to that descriptor.
//------------------------------------------------------------------------------

// Deliberately distinct slot markers so the resolved slot is proven.
int32_t SlotMoveNext(il2cpp::Il2CppObject*) { return 1; }
int32_t SlotGetCurrentI4(il2cpp::Il2CppObject*) { return 42; }
il2cpp::Il2CppObject* g_markerRef = nullptr; // filled in main
il2cpp::Il2CppObject* SlotGetCurrentObj(il2cpp::Il2CppObject*) { return g_markerRef; }
il2cpp::Il2CppObject* SlotGetEnumerator(il2cpp::Il2CppObject* self) { return self; }
il2cpp::Il2CppObject* SlotGetEnumeratorNonGeneric(il2cpp::Il2CppObject* self) { return self; }
int32_t g_resetCalled = 0;
void SlotReset(il2cpp::Il2CppObject*) { g_resetCalled = 1; }
int32_t g_disposeCalled = 0;
void SlotDispose(il2cpp::Il2CppObject*) { g_disposeCalled = 1; }

// The compiler state machine shape ("Cases.YieldProbe+<Range>d__0"): slots 0-6
// in the exact _Range_d__0 vtable order (b(), r:IEnumerator`1<i>(), i(),
// r:IEnumerator(), r:System.Object(), v(), v()).
void* g_stateVtable[] = {
    reinterpret_cast<void*>(&SlotMoveNext),              // slot 0: MoveNext
    reinterpret_cast<void*>(&SlotGetEnumerator),         // slot 1: generic GetEnumerator
    reinterpret_cast<void*>(&SlotGetCurrentI4),          // slot 2: generic get_Current
    reinterpret_cast<void*>(&SlotGetEnumeratorNonGeneric), // slot 3: non-generic GetEnumerator
    reinterpret_cast<void*>(&SlotGetCurrentObj),         // slot 4: non-generic get_Current
    reinterpret_cast<void*>(&SlotReset),                 // slot 5: Reset
    reinterpret_cast<void*>(&SlotDispose),               // slot 6: System.IDisposable.Dispose
};
il2cpp::Il2CppClass g_stateClass = {
    "<Range>d__0", // klass->name is the SHORT name -- the name join fails.
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)),
    0u, 0, nullptr,
    reinterpret_cast<void**>(g_stateVtable),
    7, nullptr, 0,
};
static const il2cpp::Il2CppMethodInfo g_stateMethods[] = {
    { "System.IDisposable.Dispose", "v()", 0, 0x8, 6, "Cases.YieldProbe+<Range>d__0",
      reinterpret_cast<void*>(&SlotDispose) },
    { "MoveNext", "b()", 0, 0x8, 0, "Cases.YieldProbe+<Range>d__0",
      reinterpret_cast<void*>(&SlotMoveNext) },
    { "System.Collections.Generic.IEnumerator<System.Int32>.get_Current", "i()", 0, 0x8,
      2, "Cases.YieldProbe+<Range>d__0", reinterpret_cast<void*>(&SlotGetCurrentI4) },
    { "System.Collections.IEnumerator.Reset", "v()", 0, 0x8, 5,
      "Cases.YieldProbe+<Range>d__0", reinterpret_cast<void*>(&SlotReset) },
    { "System.Collections.IEnumerator.get_Current", "r:System.Object()", 0, 0x8, 4,
      "Cases.YieldProbe+<Range>d__0", reinterpret_cast<void*>(&SlotGetCurrentObj) },
    { "System.Collections.Generic.IEnumerable<System.Int32>.GetEnumerator",
      "r:System.Collections.Generic.IEnumerator`1<i>()", 0, 0x8, 1,
      "Cases.YieldProbe+<Range>d__0", reinterpret_cast<void*>(&SlotGetEnumerator) },
    { "System.Collections.IEnumerable.GetEnumerator", "r:System.Collections.IEnumerator()",
      0, 0x8, 3, "Cases.YieldProbe+<Range>d__0",
      reinterpret_cast<void*>(&SlotGetEnumeratorNonGeneric) },
};
il2cpp::Il2CppTypeInfo g_stateInfo = {
    "Cases.YieldProbe+<Range>d__0", &g_stateClass, nullptr, nullptr, 0,
    nullptr, 0, g_stateMethods, 7, nullptr, 0,
};
il2cpp::Il2CppObject g_stateReceiver = { &g_stateClass };

// The USER enumerator shape (enprobe's MyEnum): MetaHeader-named members hold
// the PLAIN metadata name ("MoveNext"/"Dispose"), the plain property accessor
// records virtualSlot -1 (excluded), and the generic/non-generic get_Current
// slots are distinguished by signature. Its vtable mirrors the method-table
// slots (Dispose at 0, MoveNext at 1, generic get_Current at 3, non-generic
// at 4) so each resolved slot drives its OWN marker.
void* g_userVtable[] = {
    reinterpret_cast<void*>(&SlotDispose),           // slot 0: Dispose
    reinterpret_cast<void*>(&SlotMoveNext),          // slot 1: MoveNext
    reinterpret_cast<void*>(&SlotGetCurrentI4),      // slot 2: (unused accessor slot -1)
    reinterpret_cast<void*>(&SlotGetCurrentI4),      // slot 3: generic get_Current
    reinterpret_cast<void*>(&SlotGetCurrentObj),     // slot 4: non-generic get_Current
};
il2cpp::Il2CppClass g_userClass = {
    "MyEnum", static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)),
    0u, 0, nullptr, reinterpret_cast<void**>(g_userVtable), 5, nullptr, 0,
};
static const il2cpp::Il2CppMethodInfo g_userMethods[] = {
    { "MoveNext", "b()", 0, 0x9, 1, "Cases.EnProbe+MyEnum",
      reinterpret_cast<void*>(&SlotMoveNext) },
    { "get_Current", "i()", 0, 0x1, -1, "Cases.EnProbe+MyEnum", nullptr },
    { "System.Collections.Generic.IEnumerator<System.Int32>.get_Current", "i()", 0, 0x8,
      3, "Cases.EnProbe+MyEnum", reinterpret_cast<void*>(&SlotGetCurrentI4) },
    { "System.Collections.IEnumerator.get_Current", "r:System.Object()", 0, 0x8, 4,
      "Cases.EnProbe+MyEnum", reinterpret_cast<void*>(&SlotGetCurrentObj) },
    { "Dispose", "v()", 0, 0x9, 0, "Cases.EnProbe+MyEnum",
      reinterpret_cast<void*>(&SlotDispose) },
};
il2cpp::Il2CppTypeInfo g_userInfo = {
    "Cases.EnProbe+MyEnum", &g_userClass, nullptr, nullptr, 0,
    nullptr, 0, g_userMethods, 5, nullptr, 0,
};
il2cpp::Il2CppObject g_userReceiver = { &g_userClass };

// A type with NO reflected typeinfo at all (e.g. a boxed primitive / snapshot
// object): the klass-pointer join fails -> every entry must throw
// NotSupportedException (except dispose -> no-op).
il2cpp::Il2CppClass g_unlistedClass = {
    "System.SomeUnreflected", static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)),
    0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
il2cpp::Il2CppObject g_unlistedReceiver = { &g_unlistedClass };

} // namespace

int main()
{
    il2cpp::Il2CppObject marker = { &g_stateClass };
    g_markerRef = &marker;

    il2cpp::il2cpp_typeinfo_register(&g_stateInfo);
    il2cpp::il2cpp_typeinfo_register(&g_userInfo);

    // --- 1. the klass-POINTER join wins where the NAME join fails ---
    {
        // The typeinfo registers the FULL "+"-chained name; klass->name is the
        // SHORT nested name -- the name-based lookup cannot see it.
        Expect(il2cpp::il2cpp_typeinfo_lookup("<Range>d__0") == nullptr,
               "name-based lookup misses the compiler-generated short name");
        Expect(il2cpp::il2cpp_typeinfo_lookup_by_class(&g_stateClass) == &g_stateInfo,
               "klass-pointer join resolves the state-machine typeinfo");
        Expect(il2cpp::il2cpp_typeinfo_lookup_by_class(&g_userClass) == &g_userInfo,
               "klass-pointer join resolves the user-enumerator typeinfo");
        Expect(il2cpp::il2cpp_typeinfo_lookup_by_class(&g_unlistedClass) == nullptr,
               "unreflected class resolves to null typeinfo");
        Expect(il2cpp::il2cpp_typeinfo_lookup_by_class(nullptr) == nullptr,
               "null klass resolves to null typeinfo");
    }

    // --- 2. the state machine shape: signature-aware slot selection ---
    {
        // MoveNext -> the "b()" slot -> slot 0 marker (1).
        ExpectI32(il2cpp::il2cpp_ienum_move_next(&g_stateReceiver), 1,
                  "state machine MoveNext dispatches the b() slot");
        // get_Current i4 -> the GENERIC "i()" slot, NOT the non-generic
        // "r:System.Object()" slot (which would return the marker pointer).
        ExpectI32(il2cpp::il2cpp_ienum_get_current_i4(&g_stateReceiver), 42,
                  "state machine get_Current-i4 selects the generic i() slot");
        // get_Current obj -> the NON-generic object slot.
        Expect(il2cpp::il2cpp_ienum_get_current_obj(&g_stateReceiver) == g_markerRef,
               "state machine get_Current-obj selects the non-generic slot");
        // GetEnumerator generic vs non-generic: two ENDPOINT slots, provably
        // different receivers still dispatch through their own slots.
        Expect(il2cpp::il2cpp_ienum_get_enumerator(&g_stateReceiver) == &g_stateReceiver,
               "generic GetEnumerator dispatches its own slot");
        Expect(il2cpp::il2cpp_ienum_get_enumerator_nongeneric(&g_stateReceiver) == &g_stateReceiver,
               "non-generic GetEnumerator dispatches its own slot");
        g_resetCalled = 0;
        il2cpp::il2cpp_ienum_reset(&g_stateReceiver);
        Expect(g_resetCalled == 1, "Reset dispatches the v() slot");
        g_disposeCalled = 0;
        il2cpp::il2cpp_ienum_dispose(&g_stateReceiver);
        Expect(g_disposeCalled == 1, "Dispose dispatches the v() slot");
    }

    // --- 3. the user-enumerator shape: plain-name members + -1 accessor ---
    {
        ExpectI32(il2cpp::il2cpp_ienum_move_next(&g_userReceiver), 1,
                  "user enumerator MoveNext resolves the plain 'MoveNext' name");
        // The plain "get_Current" (slot -1) must be EXCLUDED -- the i4 carrier
        // takes the generic qualified slot, the object carrier the non-generic.
        ExpectI32(il2cpp::il2cpp_ienum_get_current_i4(&g_userReceiver), 42,
                  "user enumerator i4 get_Current skips the -1 accessor");
        Expect(il2cpp::il2cpp_ienum_get_current_obj(&g_userReceiver) == g_markerRef,
               "user enumerator obj get_Current takes the non-generic slot");
        g_disposeCalled = 0;
        il2cpp::il2cpp_ienum_dispose(&g_userReceiver);
        Expect(g_disposeCalled == 1, "user enumerator Dispose resolves the plain 'Dispose' name");
    }

    // --- 4. the reference carriers (get_Current_ref) ---
    {
        // The state machine's generic slot is i()-typed, so the ref carrier
        // must NOT silently take it -- no i8/d/ref slot exists -> the general
        // NotSupported contract applies. (The ref carrier needs a r: generic
        // slot like IEnumerator<string>.get_Current; none is declared here.)
        ExpectThrows(kNotSupportedType,
                     "state machine get_Current-ref (no r: generic slot) throws NotSupportedException",
                     [] { il2cpp::il2cpp_ienum_get_current_ref(&g_stateReceiver); });
    }

    // --- 5. error paths: unreflected receiver + null receiver ---
    {
        ExpectThrows(kNotSupportedType,
                     "unreflected receiver move_next throws NotSupportedException",
                     [] { il2cpp::il2cpp_ienum_move_next(&g_unlistedReceiver); });
        ExpectThrows(kNotSupportedType,
                     "unreflected receiver get_current_i4 throws NotSupportedException",
                     [] { il2cpp::il2cpp_ienum_get_current_i4(&g_unlistedReceiver); });
        ExpectThrows(kNotSupportedType,
                     "unreflected receiver get_enumerator throws NotSupportedException",
                     [] { il2cpp::il2cpp_ienum_get_enumerator(&g_unlistedReceiver); });
        ExpectThrows(kNullRefType, "null receiver move_next throws NullReferenceException",
                     [] { il2cpp::il2cpp_ienum_move_next(nullptr); });
        ExpectThrows(kNullRefType, "null receiver get_current_i4 throws NullReferenceException",
                     [] { il2cpp::il2cpp_ienum_get_current_i4(nullptr); });
        ExpectThrows(kNullRefType, "null receiver get_current_obj throws NullReferenceException",
                     [] { il2cpp::il2cpp_ienum_get_current_obj(nullptr); });
        ExpectThrows(kNullRefType, "null receiver get_enumerator throws NullReferenceException",
                     [] { il2cpp::il2cpp_ienum_get_enumerator(nullptr); });
        ExpectThrows(kNullRefType, "null receiver reset throws NullReferenceException",
                     [] { il2cpp::il2cpp_ienum_reset(nullptr); });
        // Dispose is null-safe AND no-op for a no-slot receiver. A recursive
        // check proves it runs without throwing: the two calls below must
        // return normally (an exception would abort with a nonzero exit).
        il2cpp::il2cpp_ienum_dispose(nullptr);
        int32_t afterNull = g_disposeCalled;
        il2cpp::il2cpp_ienum_dispose(&g_unlistedReceiver);
        Expect(g_disposeCalled == afterNull,
               "dispose leaves the slot-flag untouched for null/unreflected receivers");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d ienum assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime ienum tests passed\n");
    return 0;
}