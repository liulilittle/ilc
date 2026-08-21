//------------------------------------------------------------------------------
// test_object.cpp -- runtime object-model unit tests.
//
// Coverage (normal + boundary + error paths):
//   1. il2cpp_object_new returns a non-null object whose klass header points at
//      the requested descriptor.
//   2. The freshly allocated object is zero-initialized: every payload byte
//      beyond the klass header is 0 (default .NET field value semantics).
//   3. il2cpp_class_is_ref_field matches the reference-field bitmap at slot
//      granularity (slot = offset / sizeof(void*)): the two reference slots and
//      the intermediate value slot are each resolved correctly.
//   4. The type registry round-trips: a registered descriptor is found by its
//      full type name; an unregistered name resolves to null.
//   5. Registry defensive paths: a null name and a null descriptor are handled
//      without crashing.
//
// Fixture note (spec correction, see task-3-report.md):
//   The task brief's literal fixture (instanceSize=16, probe offsets 0/4/8)
//   is only consistent with 32-bit pointers: with 8-byte pointers (all three
//   target compilers are x64) offset 4 and offset 0 collapse onto the same
//   slot 0, so the expected true/false/true pattern cannot hold. This fixture
//   ports the SAME intent -- a ref/value/ref mixed object, bitmap 0b101, slots
//   0 and 2 are references -- onto the real 64-bit slot grid: slot i lives at
//   byte offset i*8, so the probes are 0 (ref), 8 (value) and 16 (ref), and the
//   instance grows to 24 bytes to hold the third slot. The static_assert below
//   pins the 8-byte-pointer assumption so a future 32-bit build fails loudly
//   instead of silently producing wrong probe offsets.
//
// This is a plain executable with a minimal assertion harness -- no third-party
// framework. Any failed Expect() prints FAIL and bumps the failure counter; the
// process exits non-zero if any check failed, so the CMake test target and CI
// see a red signal on regression.
//------------------------------------------------------------------------------

#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

// The fixture's probe offsets and slot computations below are valid only when a
// pointer occupies exactly 8 bytes. All supported build targets (MSVC x64,
// MinGW x64, WSL GCC 7.5 x64) satisfy this; a hypothetical 32-bit build must
// fail here rather than exercise wrong offsets.
static_assert(sizeof(void*) == 8, "object-model test fixture assumes 8-byte pointers");

// Number of failed assertions accumulated by Expect(). main() returns non-zero
// when this counter is not zero, which is what the test runner and CI observe.
int g_failures = 0;

// Expect -- evaluate a single invariant. On failure, name the violated check
// so a regression is diagnosable from the printed line alone.
void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Test fixture: a mock descriptor describing a 24-byte object whose payload is
// three 8-byte pointer slots. Bitmap 0b101 marks slot 0 (offset 0) and slot 2
// (offset 16) as GC references; slot 1 (offset 8) is a plain value field --
// the classic mixed ref/value/reference field pattern the collector must scan
// exactly.
il2cpp::Il2CppClass g_mockClass = {
    "Test.MockObject", // name
    24,                // instanceSize: header pointer + 3 payload slots (24B)
    0b101u,            // refFieldBitmap: slot0 and slot2 are GC references
    2,                 // refFieldCount: two reference-typed fields
    nullptr,           // baseClass: standalone fixture, no parent
    nullptr,           // vtable: no virtual methods in this fixture
    0,                 // vtableSize
    nullptr,           // interfaces: no implemented interfaces
    0,                 // interfaceCount
};

} // namespace

//------------------------------------------------------------------------------
// main -- run every object-model invariant and report the outcome.
//------------------------------------------------------------------------------
int main()
{
    // --- 1. Allocation: non-null + klass header + payload zero-initialized ---
    il2cpp::Il2CppObject* obj = il2cpp::il2cpp_object_new(&g_mockClass);
    Expect(obj != nullptr, "il2cpp_object_new returns a non-null object");
    Expect(obj->klass == &g_mockClass, "object klass header points at the requested descriptor");

    // Zero-init check: every byte in the payload region (after the klass
    // header, i.e. [sizeof(Il2CppObject), instanceSize)) must be 0. The klass
    // pointer itself is intentionally non-zero, so the payload region is the
    // meaningful "fields are default-initialized" evidence.
    const unsigned char* bytes = static_cast<const unsigned char*>(static_cast<const void*>(obj));
    bool payloadZero = true;
    for (size_t i = sizeof(il2cpp::Il2CppObject); i < static_cast<size_t>(g_mockClass.instanceSize); ++i)
    {
        if (bytes[i] != 0)
        {
            payloadZero = false;
            break;
        }
    }
    Expect(payloadZero, "all object payload bytes are zero-initialized");

    // --- 2. Reference-field bitmap: slot-granularity queries (8-byte slots) ---
    Expect(il2cpp::il2cpp_class_is_ref_field(&g_mockClass, 0)  == true,  "offset 0  (slot 0) is a GC reference field");
    Expect(il2cpp::il2cpp_class_is_ref_field(&g_mockClass, 8)  == false, "offset 8  (slot 1) is a plain value field");
    Expect(il2cpp::il2cpp_class_is_ref_field(&g_mockClass, 16) == true,  "offset 16 (slot 2) is a GC reference field");

    // Boundary: offset 24 is slot 3, outside the 24-byte object and outside the
    // marked bitmap bits -- must be false, never a false positive.
    Expect(il2cpp::il2cpp_class_is_ref_field(&g_mockClass, 24) == false, "offset 24 (slot 3) is not a marked reference field");

    // --- 3. Type registry: register then resolve by name ---
    il2cpp::il2cpp_class_register(&g_mockClass);
    il2cpp::Il2CppClass* found = il2cpp::il2cpp_class_by_name("Test.MockObject");
    Expect(found == &g_mockClass, "registered descriptor is found by full type name");
    Expect(il2cpp::il2cpp_class_by_name("No.Such.Type") == nullptr, "unregistered type name resolves to null");

    // --- 4. Registry defensive paths: null name / null descriptor ---
    il2cpp::il2cpp_class_register(nullptr);
    Expect(il2cpp::il2cpp_class_by_name(nullptr) == nullptr, "null lookup name resolves to null");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d object-model assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime object-model tests passed\n");
    return 0;
}
