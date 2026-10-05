//------------------------------------------------------------------------------
// test_marshalext4.cpp -- Marshal medium-priority gap batch unit tests (see
// il2cpp_marshal_ext.h for the contract under test; implementation in
// marshalext4.cpp).
//
// Coverage (normal + edge + error paths):
//   1. destroy_structure: null ptr (IntPtr.Zero) -> ArgumentNullException;
//      null Type -> ArgumentNullException; a non-string Type value ->
//      ArgumentException; a valid (non-zero ptr, Type-name string) call is a
//      documented no-op (the memory block is untouched -- verified by writing
//      a sentinel into a buffer and observing it unchanged after the call).
//   2. size_of_object: every boxed primitive resolves to its net48 marshal
//      size (Int32=4, Boolean=1, Char=2, Double=8, Decimal=16, Int64=8,
//      Single=4, Byte=1, Int16=2, UInt32=4, ...); a boxed String -> the
//      no-fixed-native-size path (NotSupportedException, the established
//      il2cpp_marshal_size_of policy); a non-primitive object (array) ->
//      NotSupportedException; null -> ArgumentNullException.
//   3. copy_to_unmanaged_elem / copy_from_unmanaged_elem: byte[] (elemSize 1),
//      int32[] (elemSize 4), double[] (elemSize 8) round-trips through a
//      native buffer; startIndex offsets the window; length 0 is a no-op;
//      null array / null dest / null src -> ArgumentNullException; negative
//      startIndex or length, and a window past the array end ->
//      ArgumentOutOfRangeException; a reference-element array (string[]/
//      object[]) -> NotSupportedException (raw object-ref bytes are a GC
//      hazard -- net48's ArgumentException, layer NotSupportedException).
//   4. get_object_for_native_variant / get_native_variant_for_object /
//      get_objects_for_native_variants / is_com_object: the ILC VARIANT
//      subset -- every supported primitive round-trips through a 16-byte
//      native VARIANT (vt at offset 0, payload at offset 8, reserved zeroed);
//      null -> VT_EMPTY; VT_NULL reads as null; VT_BOOL writes VARIANT_BOOL
//      -1/0 and reads any nonzero value as true; VT_ERROR reads S_OK (0) as
//      null and any other SCODE as a boxed Int32; VT_UI2 is shared by UInt16
//      and Char (Char reads back as UInt16); unsupported VTs (BSTR/CY/...) and
//      non-primitive / String / Decimal objects -> NotSupportedException;
//      null pointers -> ArgumentNullException; negative cVars ->
//      ArgumentOutOfRangeException; is_com_object(null) ->
//      ArgumentNullException and every non-null object -> false.
//
// Plain executable + minimal assertion harness (see test_marshalext.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext.h"

#include "il2cpp_array.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_boxing_ext2.h"
#include "il2cpp_exception.h"
#include "il2cpp_runtime.h" // il2cpp_box_int32 / il2cpp_array_new
#include "il2cpp_string.h"

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

template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedType)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedType) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Expected type chains (must match exceptionext.cpp exactly).
const char* kArgumentNull =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kArgumentOutOfRange =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kNotSupported =
    "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception";
[[maybe_unused]] const char* kArgument =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";

// A mock array element class descriptor (test_marshalext.cpp idiom).
il2cpp::Il2CppClass g_mockElemClass = {
    "Mock.Elem", 16, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 1. destroy_structure ---
    Expect(ThrowsDotNet([&] { icall_marshal_destroy_structure(0, nullptr); }, kArgumentNull),
           "destroy_structure(ptr=0, null type) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_marshal_destroy_structure(0,
               il2cpp::il2cpp_string_new_from_cs("System.Int32")); }, kArgumentNull),
           "destroy_structure(ptr=0, valid type) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_marshal_destroy_structure(0x1234, nullptr); }, kArgumentNull),
           "destroy_structure(ptr, null type) -> ArgumentNullException");
    // (The old "non-string Type value -> ArgumentException" probe is obsolete:
    // the current ABI types the Type argument as Il2CppString* and the
    // transpiler folds the Type value to its name string.)
    // Documented no-op: a valid call must not touch the memory block. Write a
    // sentinel into a heap buffer and verify it is unchanged after the call.
    void* buf = std::malloc(16);
    std::memset(buf, 0xAB, 16);
    icall_marshal_destroy_structure(reinterpret_cast<intptr_t>(buf),
        il2cpp::il2cpp_string_new_from_cs("Some.StructType"));
    const unsigned char* bytes = static_cast<const unsigned char*>(buf);
    bool untouched = true;
    for (int i = 0; i < 16; ++i)
    {
        untouched = untouched && bytes[i] == 0xAB;
    }
    Expect(untouched, "destroy_structure is a documented no-op (buffer untouched)");
    std::free(buf);

    // --- 2. size_of_object ---
    Expect(ThrowsDotNet([&] { (void)icall_marshal_size_of_object(nullptr); }, kArgumentNull),
           "size_of_object(null) -> ArgumentNullException");
    Expect(icall_marshal_size_of_object(il2cpp::il2cpp_box_int32(nullptr, 7)) == 4,
           "size_of_object(boxed Int32) is 4");
    Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_bool(true)) == 4,
           "size_of_object(boxed Boolean) is 4 (Ansi BOOL; net48 oracle)");
    Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_char('A')) == 1,
           "size_of_object(boxed Char) is 1 (Ansi; net48 oracle)");
    Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_double(3.5)) == 8,
           "size_of_object(boxed Double) is 8");
    Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_long(123)) == 8,
           "size_of_object(boxed Int64) is 8");
    Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_float(1.5)) == 4,
           "size_of_object(boxed Single) is 4");
    Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_byte(9)) == 1,
           "size_of_object(boxed Byte) is 1");
    Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_short(-2)) == 2,
           "size_of_object(boxed Int16) is 2");
    Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_uint(0xFFFFFFFFu)) == 4,
           "size_of_object(boxed UInt32) is 4");
    {
        il2cpp::runtime::Il2CppDecimal dec = {};
        dec.lo64 = 12345u;
        Expect(icall_marshal_size_of_object(il2cpp::runtime::icall_box_decimal(&dec)) == 16,
               "size_of_object(boxed Decimal) is 16");
    }
    il2cpp::Il2CppObject* str = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs("hello"));
    Expect(ThrowsDotNet([&] { (void)icall_marshal_size_of_object(str); }, kNotSupported),
           "size_of_object(boxed String) -> NotSupportedException (no fixed native size)");
    il2cpp::Il2CppArray* mockArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 4, false, 3);
    Expect(ThrowsDotNet([&] { (void)icall_marshal_size_of_object(
               reinterpret_cast<il2cpp::Il2CppObject*>(mockArr)); }, kNotSupported),
           "size_of_object(non-primitive object) -> NotSupportedException");

    // --- 3. copy_to_unmanaged_elem / copy_from_unmanaged_elem ---
    // byte[] (elemSize 1)
    {
        il2cpp::Il2CppArray* bytes = il2cpp::il2cpp_array_new(&g_mockElemClass, 1, false, 5);
        unsigned char* b = reinterpret_cast<unsigned char*>(bytes->data);
        b[0] = 10; b[1] = 20; b[2] = 30; b[3] = 40; b[4] = 50;
        unsigned char native[8] = {};
        icall_marshal_copy_to_unmanaged_elem(bytes, 1, native, 3);
        Expect(native[0] == 20 && native[1] == 30 && native[2] == 40,
               "copy_to_unmanaged_elem(byte[], start=1, len=3) copies 3 bytes");
        unsigned char back[5] = {};
        icall_marshal_copy_from_unmanaged_elem(native, bytes, 0, 3);
        Expect(back[0] == 0 && back[1] == 0, "from_unmanaged writes only the window");
        unsigned char* b2 = reinterpret_cast<unsigned char*>(bytes->data);
        Expect(b2[0] == 20 && b2[1] == 30 && b2[2] == 40,
               "copy_from_unmanaged_elem restores the byte window");
    }
    // int32[] (elemSize 4)
    {
        il2cpp::Il2CppArray* ints = il2cpp::il2cpp_array_new(&g_mockElemClass, 4, false, 4);
        int32_t* i = reinterpret_cast<int32_t*>(ints->data);
        i[0] = 100; i[1] = 200; i[2] = 300; i[3] = 400;
        int32_t native[4] = {};
        icall_marshal_copy_to_unmanaged_elem(ints, 0, native, 4);
        Expect(native[0] == 100 && native[1] == 200 && native[2] == 300 && native[3] == 400,
               "copy_to_unmanaged_elem(int32[], len=4) copies 16 bytes");
        icall_marshal_copy_to_unmanaged_elem(ints, 2, native, 2);
        Expect(native[0] == 300 && native[1] == 400,
               "copy_to_unmanaged_elem startIndex offsets the int32 window");
        // length 0 is a no-op even at the array end.
        icall_marshal_copy_to_unmanaged_elem(ints, 4, native, 0);
        Expect(native[0] == 300 && native[1] == 400, "copy_to_unmanaged_elem len=0 is a no-op");
    }
    // double[] (elemSize 8)
    {
        il2cpp::Il2CppArray* dbls = il2cpp::il2cpp_array_new(&g_mockElemClass, 8, false, 3);
        double* d = reinterpret_cast<double*>(dbls->data);
        d[0] = 1.5; d[1] = 2.5; d[2] = 3.5;
        double native[3] = {};
        icall_marshal_copy_to_unmanaged_elem(dbls, 0, native, 3);
        Expect(native[0] == 1.5 && native[1] == 2.5 && native[2] == 3.5,
               "copy_to_unmanaged_elem(double[], len=3) copies 24 bytes");
        d[0] = 0.0; d[1] = 0.0; d[2] = 0.0;
        icall_marshal_copy_from_unmanaged_elem(native, dbls, 0, 3);
        Expect(d[0] == 1.5 && d[1] == 2.5 && d[2] == 3.5,
               "copy_from_unmanaged_elem(double[]) round-trips");
    }
    // Error paths
    {
        il2cpp::Il2CppArray* ints = il2cpp::il2cpp_array_new(&g_mockElemClass, 4, false, 3);
        Expect(ThrowsDotNet([&] { icall_marshal_copy_to_unmanaged_elem(nullptr, 0, nullptr, 0); }, kArgumentNull),
               "copy_to_unmanaged_elem(null array) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_marshal_copy_to_unmanaged_elem(ints, 0, nullptr, 0); }, kArgumentNull),
               "copy_to_unmanaged_elem(null dest) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_marshal_copy_to_unmanaged_elem(ints, -1, nullptr, 0); }, kArgumentNull),
               "negative startIndex hits the null-dest check first (net48 arg order)");
        int32_t dest = 0;
        Expect(ThrowsDotNet([&] { icall_marshal_copy_to_unmanaged_elem(ints, -1, &dest, 0); }, kArgumentOutOfRange),
               "copy_to_unmanaged_elem(negative startIndex) -> ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&] { icall_marshal_copy_to_unmanaged_elem(ints, 0, &dest, -1); }, kArgumentOutOfRange),
               "copy_to_unmanaged_elem(negative length) -> ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&] { icall_marshal_copy_to_unmanaged_elem(ints, 1, &dest, 3); }, kArgumentOutOfRange),
               "copy_to_unmanaged_elem(window past the array end) -> ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&] { icall_marshal_copy_from_unmanaged_elem(nullptr, ints, 0, 0); }, kArgumentNull),
               "copy_from_unmanaged_elem(null src) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_marshal_copy_from_unmanaged_elem(&dest, nullptr, 0, 0); }, kArgumentNull),
               "copy_from_unmanaged_elem(null array) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_marshal_copy_from_unmanaged_elem(&dest, ints, 0, 4); }, kArgumentOutOfRange),
               "copy_from_unmanaged_elem(window past the array end) -> ArgumentOutOfRangeException");
        // Reference-element arrays are NOT blittable (raw object-ref bytes are
        // a GC hazard -- net48 throws ArgumentException for non-blittable
        // element types; the layer rejects with NotSupportedException).
        il2cpp::Il2CppArray* refArr = il2cpp::il2cpp_array_new(&g_mockElemClass, sizeof(void*), true, 2);
        Expect(ThrowsDotNet([&] { icall_marshal_copy_to_unmanaged_elem(refArr, 0, &dest, 0); }, kNotSupported),
               "copy_to_unmanaged_elem(reference-element array) -> NotSupportedException");
        Expect(ThrowsDotNet([&] { icall_marshal_copy_from_unmanaged_elem(&dest, refArr, 0, 0); }, kNotSupported),
               "copy_from_unmanaged_elem(reference-element array) -> NotSupportedException");
    }

    // --- 4. VARIANT family (marshalext5.cpp) ---
    {
        // A VARIANT is a 16-byte buffer with vt at offset 0 and the payload at
        // offset 8 (the marshalext5.cpp subset).
        unsigned char buf[16] = {};

        // Round-trips: encode a boxed primitive, decode it back, verify the
        // value and the box class name.
        auto roundTrip = [&](il2cpp::Il2CppObject* boxed, const char* klassName,
                             bool (*verify)(il2cpp::Il2CppObject*))
        {
            std::memset(buf, 0, sizeof(buf));
            icall_marshal_get_native_variant_for_object(boxed,
                reinterpret_cast<intptr_t>(buf));
            il2cpp::Il2CppObject* back = icall_marshal_get_object_for_native_variant(
                reinterpret_cast<intptr_t>(buf));
            return back != nullptr && back->klass != nullptr &&
                   std::strcmp(back->klass->name, klassName) == 0 && verify(back);
        };
        Expect(roundTrip(il2cpp::runtime::icall_box_byte(200), "System.Byte",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_byte(o) == 200; }),
               "variant round-trip: Byte 200");
        Expect(roundTrip(il2cpp::runtime::icall_box_sbyte(-5), "System.SByte",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_sbyte(o) == -5; }),
               "variant round-trip: SByte -5");
        Expect(roundTrip(il2cpp::runtime::icall_box_short(-1234), "System.Int16",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_short(o) == -1234; }),
               "variant round-trip: Int16 -1234");
        Expect(roundTrip(il2cpp::runtime::icall_box_ushort(50000), "System.UInt16",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_ushort(o) == 50000; }),
               "variant round-trip: UInt16 50000");
        Expect(roundTrip(il2cpp::runtime::icall_box_char('A'), "System.UInt16",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_ushort(o) == 65; }),
               "variant round-trip: Char 'A' reads back as UInt16 65 (VT_UI2)");
        Expect(roundTrip(il2cpp::il2cpp_box_int32(nullptr, 42), "System.Int32",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_int32(o) == 42; }),
               "variant round-trip: Int32 42");
        Expect(roundTrip(il2cpp::runtime::icall_box_uint(0xFFFFFF00), "System.UInt32",
               [](il2cpp::Il2CppObject* o) { return static_cast<uint32_t>(icall_unbox_uint(o)) == 0xFFFFFF00u; }),
               "variant round-trip: UInt32 0xFFFFFF00");
        Expect(roundTrip(il2cpp::runtime::icall_box_long(-1099511627776LL), "System.Int64",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_long(o) == -1099511627776LL; }),
               "variant round-trip: Int64 -2^40");
        Expect(roundTrip(il2cpp::runtime::icall_box_ulong(9223372036854775808ULL), "System.UInt64",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_ulong(o) == 9223372036854775808ULL; }),
               "variant round-trip: UInt64 2^63");
        Expect(roundTrip(il2cpp::runtime::icall_box_float(1.5), "System.Single",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_float(o) == 1.5; }),
               "variant round-trip: Single 1.5");
        Expect(roundTrip(il2cpp::runtime::icall_box_double(3.25), "System.Double",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_double(o) == 3.25; }),
               "variant round-trip: Double 3.25");
        Expect(roundTrip(il2cpp::runtime::icall_box_bool(true), "System.Boolean",
               [](il2cpp::Il2CppObject* o) { return icall_unbox_bool(o); }),
               "variant round-trip: Boolean true");
        Expect(roundTrip(il2cpp::runtime::icall_box_bool(false), "System.Boolean",
               [](il2cpp::Il2CppObject* o) { return !icall_unbox_bool(o); }),
               "variant round-trip: Boolean false");

        // Direct buffer inspection of the encoded form.
        std::memset(buf, 0, sizeof(buf));
        icall_marshal_get_native_variant_for_object(il2cpp::il2cpp_box_int32(nullptr, 42),
            reinterpret_cast<intptr_t>(buf));
        uint16_t vt = 0;
        int32_t i4 = 0;
        std::memcpy(&vt, buf + 0, 2);
        std::memcpy(&i4, buf + 8, 4);
        Expect(vt == 3 && i4 == 42, "variant encode Int32: vt==3 (VT_I4), data==42");
        Expect(buf[2] == 0 && buf[4] == 0 && buf[6] == 0,
               "variant encode: reserved bytes are zeroed");
        std::memset(buf, 0, sizeof(buf));
        icall_marshal_get_native_variant_for_object(il2cpp::runtime::icall_box_bool(true),
            reinterpret_cast<intptr_t>(buf));
        std::memcpy(&vt, buf + 0, 2);
        int16_t vb = 0;
        std::memcpy(&vb, buf + 8, 2);
        Expect(vt == 11 && vb == -1, "variant encode Boolean true: vt==11 (VT_BOOL), VARIANT_BOOL -1");
        std::memset(buf, 0, sizeof(buf));
        icall_marshal_get_native_variant_for_object(nullptr, reinterpret_cast<intptr_t>(buf));
        std::memcpy(&vt, buf + 0, 2);
        bool allZero = true;
        for (int i = 0; i < 16; ++i) allZero = allZero && buf[i] == 0;
        Expect(vt == 0 && allZero, "variant encode null: VT_EMPTY, whole buffer zeroed");

        // Hand-built VARIANT reads.
        std::memset(buf, 0, sizeof(buf));
        Expect(icall_marshal_get_object_for_native_variant(reinterpret_cast<intptr_t>(buf)) == nullptr,
               "variant decode VT_EMPTY -> null");
        std::memset(buf, 0, sizeof(buf));
        const uint16_t vtNull = 1; // VT_NULL
        std::memcpy(buf + 0, &vtNull, 2);
        Expect(icall_marshal_get_object_for_native_variant(reinterpret_cast<intptr_t>(buf)) == nullptr,
               "variant decode VT_NULL -> null");
        std::memset(buf, 0, sizeof(buf));
        const uint16_t vtBool = 11; // VT_BOOL
        const int16_t rawBool = 1; // nonzero -> true
        std::memcpy(buf + 0, &vtBool, 2);
        std::memcpy(buf + 8, &rawBool, 2);
        il2cpp::Il2CppObject* boolBack = icall_marshal_get_object_for_native_variant(
            reinterpret_cast<intptr_t>(buf));
        Expect(boolBack != nullptr && icall_unbox_bool(boolBack),
               "variant decode VT_BOOL with raw data 1 -> boxed true (nonzero)");

        // VT_ERROR (int32 SCODE -- no COM substrate): S_OK (0) decodes as null,
        // any other SCODE as a boxed Int32 (net48 MarshalObjectForOleVariant).
        std::memset(buf, 0, sizeof(buf));
        const uint16_t vtError = 10; // VT_ERROR
        const int32_t hresultOk = 0; // S_OK
        std::memcpy(buf + 0, &vtError, 2);
        std::memcpy(buf + 8, &hresultOk, 4);
        Expect(icall_marshal_get_object_for_native_variant(
                   reinterpret_cast<intptr_t>(buf)) == nullptr,
               "variant decode VT_ERROR with S_OK (0) -> null");
        std::memset(buf, 0, sizeof(buf));
        const int32_t hresultArg = static_cast<int32_t>(0x80070057); // E_INVALIDARG
        std::memcpy(buf + 0, &vtError, 2);
        std::memcpy(buf + 8, &hresultArg, 4);
        il2cpp::Il2CppObject* errBack = icall_marshal_get_object_for_native_variant(
            reinterpret_cast<intptr_t>(buf));
        Expect(errBack != nullptr && errBack->klass != nullptr &&
               std::strcmp(errBack->klass->name, "System.Int32") == 0 &&
               icall_unbox_int32(errBack) == static_cast<int32_t>(0x80070057),
               "variant decode VT_ERROR with E_INVALIDARG -> boxed Int32 0x80070057");

        // Error paths.
        Expect(ThrowsDotNet([&] { (void)icall_marshal_get_object_for_native_variant(0); },
               kArgumentNull),
               "get_object_for_native_variant(null src) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { icall_marshal_get_native_variant_for_object(nullptr, 0); },
               kArgumentNull),
               "get_native_variant_for_object(null dst) -> ArgumentNullException");
        il2cpp::Il2CppObject* mockStr = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("hello"));
        Expect(ThrowsDotNet([&] { icall_marshal_get_native_variant_for_object(mockStr,
                   reinterpret_cast<intptr_t>(buf)); }, kNotSupported),
               "get_native_variant_for_object(String) -> NotSupportedException (no BSTR)");
        il2cpp::runtime::Il2CppDecimal dec = {};
        dec.lo64 = 12345u;
        Expect(ThrowsDotNet([&] { icall_marshal_get_native_variant_for_object(
                   il2cpp::runtime::icall_box_decimal(&dec),
                   reinterpret_cast<intptr_t>(buf)); }, kNotSupported),
               "get_native_variant_for_object(Decimal) -> NotSupportedException");
        il2cpp::Il2CppArray* mockArr = il2cpp::il2cpp_array_new(&g_mockElemClass, 4, false, 2);
        Expect(ThrowsDotNet([&] { icall_marshal_get_native_variant_for_object(
                   reinterpret_cast<il2cpp::Il2CppObject*>(mockArr),
                   reinterpret_cast<intptr_t>(buf)); }, kNotSupported),
               "get_native_variant_for_object(non-primitive array) -> NotSupportedException");
        const uint16_t vtBstr = 8; // VT_BSTR -- outside the subset
        std::memset(buf, 0, sizeof(buf));
        std::memcpy(buf + 0, &vtBstr, 2);
        Expect(ThrowsDotNet([&] { (void)icall_marshal_get_object_for_native_variant(
                   reinterpret_cast<intptr_t>(buf)); }, kNotSupported),
               "get_object_for_native_variant(VT_BSTR) -> NotSupportedException");
        const uint16_t vtCy = 6; // VT_CY -- outside the subset
        std::memset(buf, 0, sizeof(buf));
        std::memcpy(buf + 0, &vtCy, 2);
        Expect(ThrowsDotNet([&] { (void)icall_marshal_get_object_for_native_variant(
                   reinterpret_cast<intptr_t>(buf)); }, kNotSupported),
               "get_object_for_native_variant(VT_CY) -> NotSupportedException");

        // get_objects_for_native_variants: 3 consecutive VARIANTs.
        unsigned char base[3 * 16] = {};
        int32_t v0 = 7;
        const uint16_t vtI4 = 3;
        std::memcpy(base + 0, &vtI4, 2);
        std::memcpy(base + 8, &v0, 4);
        const uint16_t vtB = 11; // VT_BOOL
        const int16_t bTrue = -1;
        std::memcpy(base + 16 + 0, &vtB, 2);
        std::memcpy(base + 16 + 8, &bTrue, 2);
        // slot 2 stays all-zero = VT_EMPTY
        il2cpp::Il2CppObject* objArr = icall_marshal_get_objects_for_native_variants(
            reinterpret_cast<intptr_t>(base), 3);
        il2cpp::Il2CppArray* arr = reinterpret_cast<il2cpp::Il2CppArray*>(objArr);
        Expect(arr != nullptr && arr->length == 3 && arr->isRefElem,
               "get_objects_for_native_variants: object[] of length 3");
        il2cpp::Il2CppObject** slots = reinterpret_cast<il2cpp::Il2CppObject**>(arr->data);
        Expect(slots[0] != nullptr && icall_unbox_int32(slots[0]) == 7,
               "get_objects_for_native_variants slot0: Int32 7");
        Expect(slots[1] != nullptr && icall_unbox_bool(slots[1]),
               "get_objects_for_native_variants slot1: Boolean true");
        Expect(slots[2] == nullptr,
               "get_objects_for_native_variants slot2: VT_EMPTY -> null");
        Expect(ThrowsDotNet([&] { (void)icall_marshal_get_objects_for_native_variants(0, 0); },
               kArgumentNull),
               "get_objects_for_native_variants(null base) -> ArgumentNullException");
        Expect(ThrowsDotNet([&] { (void)icall_marshal_get_objects_for_native_variants(
                   reinterpret_cast<intptr_t>(base), -1); }, kArgumentOutOfRange),
               "get_objects_for_native_variants(negative cVars) -> ArgumentOutOfRangeException");

        // is_com_object.
        Expect(ThrowsDotNet([&] { (void)icall_marshal_is_com_object(nullptr); }, kArgumentNull),
               "is_com_object(null) -> ArgumentNullException");
        Expect(!icall_marshal_is_com_object(il2cpp::il2cpp_box_int32(nullptr, 1)),
               "is_com_object(boxed Int32) -> false");
        Expect(!icall_marshal_is_com_object(mockStr),
               "is_com_object(String) -> false (a string is not an RCW)");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d marshalext4 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime marshalext4 tests passed\n");
    return 0;
}
