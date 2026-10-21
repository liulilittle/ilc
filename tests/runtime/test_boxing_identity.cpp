//------------------------------------------------------------------------------
// test_boxing_identity.cpp -- ONE DESCRIPTOR PER .NET PRIMITIVE.
//
// The transpiler emits `is`/`as` on a primitive as
//
//     il2cpp_is_instance(obj, il2cpp_class_by_name("<full name>"))
//
// (CodeGenerator.BoxClassDescriptorCpp -- box-only primitives resolve by name)
// while il2cpp_is_instance is a POINTER compare against obj->klass. The box
// icalls stamp the object with their own descriptor. Both sides therefore
// must agree on the SAME Il2CppClass object; a second descriptor carrying the
// same .NET name makes `boxed is <primitive>` answer False even though the
// object is that primitive (the objectops `box_long_is=False` regression:
// metadata.cpp System_Int64_class was registered before boxingext2.cpp's
// g_longBoxClass, so class_by_name returned the metadata descriptor while
// icall_box_long stamped the box descriptor).
//
// ORDER MATTERS: this test registers the builtin System.* descriptors FIRST,
// exactly like generated code (il2cpp_runtime_init ->
// il2cpp_ensure_builtin_system_classes before Main). test_boxingext2.cpp
// exercises the opposite order (a box icall first, lazily registering the box
// classes before the builtins); both orders must produce identical identity.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
#include "il2cpp_runtime.h"

#include <cstdint>
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

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // Generated startup order: builtins before any managed code runs.
    il2cpp_ensure_builtin_system_classes();

    const int64_t int64Max = INT64_MAX;
    const uint64_t uint64Max = UINT64_MAX;

    struct Probe
    {
        Il2CppObject* boxed;
        const char* name;
        Il2CppClass* canonical; // the transpiler's direct descriptor, or null
    };
    const Probe probes[] = {
        { il2cpp_box_int32(nullptr, 42), "System.Int32", &System_Int32_class },
        { icall_box_long(42), "System.Int64", &System_Int64_class },
        { icall_box_short(-1), "System.Int16", nullptr },
        { icall_box_byte(255), "System.Byte", nullptr },
        { icall_box_sbyte(-1), "System.SByte", nullptr },
        { icall_box_ushort(65535), "System.UInt16", nullptr },
        { icall_box_uint(-1), "System.UInt32", &System_UInt32_class },
        { icall_box_ulong(uint64Max), "System.UInt64", nullptr },
        { icall_box_float(1.5), "System.Single", &System_Single_class },
        { icall_box_double(3.25), "System.Double", &System_Double_class },
        { icall_box_bool(true), "System.Boolean", &System_Boolean_class },
        { icall_box_char('A'), "System.Char", &System_Char_class },
    };

    for (const Probe& p : probes)
    {
        Il2CppClass* byName = il2cpp_class_by_name(p.name);
        Expect(byName != nullptr, "primitive box class is registered by its .NET name");
        Expect(byName == p.boxed->klass,
               "class_by_name(<primitive>) IS the box's klass (one descriptor per type)");
        Expect(il2cpp_is_instance(p.boxed, byName),
               "generated `boxed is <primitive>` passes (by-name pointer identity)");
        Expect(il2cpp_is_instance(p.boxed, &System_Object_class),
               "boxed primitive IS-A System.Object");
        if (p.canonical != nullptr)
        {
            Expect(p.boxed->klass == p.canonical,
                   "box stamps the canonical global descriptor (transpiler symbol identity)");
            Expect(byName == p.canonical,
                   "class_by_name resolves to the canonical global descriptor");
        }
    }

    // Cross-type directions the e2e case asserts (must stay False).
    Expect(!il2cpp_is_instance(icall_box_long(42), il2cpp_class_by_name("System.Int32")),
           "boxed long is NOT a System.Int32 (by-name cross-type)");
    Expect(!il2cpp_is_instance(il2cpp_box_int32(nullptr, 42), il2cpp_class_by_name("System.Int64")),
           "boxed int32 is NOT a System.Int64 (by-name cross-type)");
    Expect(!il2cpp_is_instance(icall_box_double(3.25), il2cpp_class_by_name("System.Single")),
           "boxed double is NOT a System.Single (by-name cross-type)");
    Expect(!il2cpp_is_instance(icall_box_float(1.5), il2cpp_class_by_name("System.Double")),
           "boxed Single is NOT a System.Double (by-name cross-type)");
    Expect(!il2cpp_is_instance(icall_box_uint(-1), il2cpp_class_by_name("System.Int32")),
           "boxed uint is NOT a System.Int32 (by-name cross-type)");
    Expect(!il2cpp_is_instance(icall_box_short(-1), il2cpp_class_by_name("System.Int32")),
           "boxed short is NOT a System.Int32 (by-name cross-type)");
    Expect(!il2cpp_is_instance(icall_box_ulong(uint64Max), il2cpp_class_by_name("System.Int64")),
           "boxed ulong is NOT a System.Int64 (by-name cross-type)");

    // The box payload slots still line up with the unbox icalls after the
    // descriptor unification (long/single are the two that were duplicated).
    Expect(icall_unbox_long(icall_box_long(int64Max)) == int64Max,
           "boxed long INT64_MAX round-trips");
    Expect(icall_unbox_float(icall_box_float(1.5)) == 1.5,
           "boxed float 1.5 round-trips");

    if (g_failures != 0)
    {
        std::printf("%d boxing-identity assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime boxing-identity tests passed\n");
    return 0;
}
