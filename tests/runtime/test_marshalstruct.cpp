//------------------------------------------------------------------------------
// test_marshalstruct.cpp -- D-MAR-1: Marshal.SizeOf / OffsetOf /
// PtrToStructure / StructureToPtr for the REGISTERED struct family
// (System.Drawing.Point/Rectangle/System.Windows.Forms.Message -- the native
// layout registry, il2cpp_pinvoke_struct.h).
//
// The e2e surface (marshaldeep) reaches SizeOf/OffsetOf through the
// transpiler; this unit test pins the RUNTIME semantics directly:
//
//   1. Named SizeOf (typeof(X) form): the type-name STRING resolves through
//      the registry (Rect=16, Point=8, Msg=48 x64 / 28 x86); an unregistered
//      name -> NotSupportedException; null -> ArgumentNullException.
//   2. Per-struct SizeOf<T>() forms: rect/point/msg constants (the MSG size
//      stays runtime-computed -- pointer-width dependent).
//   3. Named OffsetOf (typeof(X),string): net48 FIELD names (case-sensitive,
//      the exact declared names) -- Rectangle x/y/width/height at 0/4/8/12,
//      Point x/y at 0/4; an unknown field -> ArgumentException; an
//      unregistered type -> NotSupportedException.
//   4. Per-struct OffsetOf<T>(string) forms.
//   5. PtrToStructure/StructureToPtr byte semantics: rect/point/msg load
//      reads a native buffer into the managed struct VALUE, store writes it
//      back -- byte-exact round trip (the blittable-registry contract).
//   6. Null semantics: load(nullptr) / store(nullptr) -> ArgumentNullException
//      (net48 Marshal.PtrToStructure/StructureToPtr on IntPtr.Zero).
//
// Plain executable + minimal assertion harness (test_object.cpp idiom).
//------------------------------------------------------------------------------

#include "il2cpp_marshal_ext.h"
#include "il2cpp_pinvoke_struct.h"
#include "il2cpp_string.h"

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

const char* kArgumentNull =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,"
    "System.SystemException,System.Exception";
const char* kNotSupported =
    "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception";
const char* kArgument =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";

il2cpp::Il2CppString* Str(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_cs(ascii);
}

constexpr int32_t MsgNativeSize()
{
    const int32_t P = static_cast<int32_t>(sizeof(void*));
    return ((4 * P + 12) + P - 1) & ~(P - 1); // AlignUp(4P+12, P)
}

// ipaudit3: Marshal.SizeOf(Message) reports the net48 MANAGED layout
// (marshalext3.cpp MessageNet48Size: 40 x64 / 20 x86), NOT the native tagMSG.
constexpr int32_t MsgNet48Size()
{
    const int32_t P = static_cast<int32_t>(sizeof(void*));
    const int32_t wParam = (P + 4 + (P - 1)) & ~(P - 1);
    return wParam + 3 * P;
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 1. named SizeOf (typeof(X) form) ---
    Expect(il2cpp_marshal_size_of_struct_named(Str("System.Drawing.Rectangle")) == 16,
           "SizeOf(typeof(Rectangle)) is 16");
    Expect(il2cpp_marshal_size_of_struct_named(Str("System.Drawing.Point")) == 8,
           "SizeOf(typeof(Point)) is 8");
    Expect(il2cpp_marshal_size_of_struct_named(Str("System.Windows.Forms.Message")) == MsgNet48Size(),
           "SizeOf(typeof(Message)) is the net48 managed size (40 x64 / 20 x86; ipaudit3)");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_size_of_struct_named(nullptr); }, kArgumentNull),
           "SizeOf((string)null) -> ArgumentNullException");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_size_of_struct_named(Str("System.Drawing.Widget")); }, kNotSupported),
           "SizeOf(typeof(unregistered)) -> NotSupportedException");

    // --- 2. per-struct SizeOf<T>() forms ---
    Expect(il2cpp_marshal_size_of_struct_rect() == 16, "SizeOf<Rectangle>() is 16");
    Expect(il2cpp_marshal_size_of_struct_point() == 8, "SizeOf<Point>() is 8");
    Expect(il2cpp_marshal_size_of_struct_msg() == MsgNet48Size(),
           "SizeOf<Message>() is the net48 managed size (40 x64 / 20 x86)");

    // --- 3. named OffsetOf (typeof(X),string) ---
    Expect(il2cpp_marshal_offset_of_struct_named(Str("System.Drawing.Rectangle"), Str("x")) == 0,
           "OffsetOf(typeof(Rectangle), X) is 0");
    Expect(il2cpp_marshal_offset_of_struct_named(Str("System.Drawing.Rectangle"), Str("y")) == 4,
           "OffsetOf(typeof(Rectangle), Y) is 4");
    Expect(il2cpp_marshal_offset_of_struct_named(Str("System.Drawing.Rectangle"), Str("width")) == 8,
           "OffsetOf(typeof(Rectangle), Width) is 8");
    Expect(il2cpp_marshal_offset_of_struct_named(Str("System.Drawing.Rectangle"), Str("height")) == 12,
           "OffsetOf(typeof(Rectangle), Height) is 12");
    Expect(il2cpp_marshal_offset_of_struct_named(Str("System.Drawing.Point"), Str("y")) == 4,
           "OffsetOf(typeof(Point), Y) is 4");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of_struct_named(Str("System.Drawing.Rectangle"), Str("Left")); }, kArgument),
           "OffsetOf(typeof(Rectangle), Left) -> ArgumentException (net48: property, not a field)");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of_struct_named(Str("System.Drawing.Widget"), Str("X")); }, kNotSupported),
           "OffsetOf(typeof(unregistered)) -> NotSupportedException");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of_struct_named(Str("System.Drawing.Rectangle"), nullptr); }, kArgumentNull),
           "OffsetOf(typeof(Rectangle), null field) -> ArgumentNullException");

    // --- 4. per-struct OffsetOf<T>(string) ---
    Expect(il2cpp_marshal_offset_of_struct_rect(Str("height")) == 12, "OffsetOf<Rectangle>(\"Height\") is 12");
    Expect(il2cpp_marshal_offset_of_struct_point(Str("x")) == 0, "OffsetOf<Point>(\"X\") is 0");
    Expect(il2cpp_marshal_offset_of_struct_msg(Str("hwnd")) == 0, "OffsetOf<Message>(\"hwnd\") is 0");
    Expect(il2cpp_marshal_offset_of_struct_msg(Str("message")) == static_cast<int32_t>(sizeof(void*)),
           "OffsetOf<Message>(\"message\") is pointer-size");
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_offset_of_struct_point(Str("Z")); }, kArgument),
           "OffsetOf<Point>(\"Z\") -> ArgumentException");

    // --- 5. PtrToStructure/StructureToPtr byte round-trip (blittable) ---
    {
        int32_t native[4] = { 10, 20, 30, 40 }; // a native RECT buffer
        const il2cpp::Il2CppPinvokeRect r = il2cpp_marshal_struct_rect_load(native);
        Expect(r.left == 10 && r.top == 20 && r.right == 30 && r.bottom == 40,
               "PtrToStructure<Rectangle> reads the 16 native bytes into the managed value");
        int32_t back[4] = { 0, 0, 0, 0 };
        il2cpp_marshal_struct_rect_store(back, &r);
        Expect(std::memcmp(back, native, sizeof(native)) == 0,
               "StructureToPtr<Rectangle> writes the managed value back byte-exact");
    }
    {
        int32_t native[2] = { 7, -3 }; // a native POINT buffer
        const il2cpp::Il2CppPinvokePoint p = il2cpp_marshal_struct_point_load(native);
        Expect(p.x == 7 && p.y == -3, "PtrToStructure<Point> reads the 8 native bytes");
        int32_t back[2] = { 0, 0 };
        il2cpp_marshal_struct_point_store(back, &p);
        Expect(std::memcmp(back, native, sizeof(native)) == 0, "StructureToPtr<Point> writes back byte-exact");
    }
    {
        uint8_t native[MsgNativeSize()] = {};
        native[0] = 0x11;
        native[1] = 0x22;
        const il2cpp::Il2CppPinvokeMsg m = il2cpp_marshal_struct_msg_load(native);
        Expect(reinterpret_cast<uintptr_t>(m.hwnd) == 0x2211u,
               "PtrToStructure<Message> reads hwnd from the native buffer");
        uint8_t back[MsgNativeSize()] = {};
        il2cpp_marshal_struct_msg_store(back, &m);
        Expect(std::memcmp(back, native, sizeof(native)) == 0,
               "StructureToPtr<Message> writes back byte-exact");
    }

    // --- 6. null semantics (net48 IntPtr.Zero) ---
    Expect(ThrowsDotNet([&] { (void)il2cpp_marshal_struct_rect_load(nullptr); }, kArgumentNull),
           "PtrToStructure<Rectangle>(IntPtr.Zero) -> ArgumentNullException");
    {
        const il2cpp::Il2CppPinvokePoint p = { 1, 2 };
        Expect(ThrowsDotNet([&] { il2cpp_marshal_struct_point_store(nullptr, &p); }, kArgumentNull),
               "StructureToPtr<Point>(IntPtr.Zero) -> ArgumentNullException");
    }

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("%d FAILURE(S)\n", g_failures);
    return 1;
}
