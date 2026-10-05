//------------------------------------------------------------------------------
// test_pinvokestruct.cpp -- P/Invoke struct marshalling unit tests
// (WinForms stage-1 gap #2: MSG/RECT/POINT by-value / by-ref).
//
// Coverage (normal paths):
//   1. Size oracle: POINT=8, RECT=16, MSG=48 (x64) / 28 (x86).
//   2. Field layout oracle: canonical Win32 offsets for POINT/RECT/MSG.
//   3. RECT by-value round trip: managed struct -> native buffer -> managed
//      (il2cpp_pinvoke_struct_to_native / from_native preserve all 4 int32s).
//   4. GetWindowRect-form simulation: a "native" function fills a RECT
//      buffer ({10,20,310,220}); from_native marshals it back into the
//      managed struct, which reads left/top/right/bottom.
//   5. POINT round trip.
//   6. MSG: size + pt offset are pointer-width-correct; a full-size value
//      round trips byte-exact.
//   7. kind_by_name: registered names map to kinds; unknown/null map to -1.
//   8. Windows-only cross-validation: the registry agrees with the real
//      Win32 layout (windows.h sizeof/offsetof for RECT/POINT/MSG).
//   8b. Windows-only exclusion oracle: WNDCLASS/DEVMODE are by-ADDRESS
//      passthrough structs, NOT registry candidates -- cross-checks the
//      empirical Win32 facts (WNDCLASSW=72 with lpfnWndProc@8 pointer,
//      DEVMODEW=220 with a union at 76) and that kind_by_name never
//      resolves the by-address carrier.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke_struct.h"

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

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

// Power-of-two alignment helper mirroring the runtime's layout math.
int32_t AlignUp(int32_t value, int32_t alignment)
{
    return (value + alignment - 1) & ~(alignment - 1);
}

int32_t PointerSize()
{
    return static_cast<int32_t>(sizeof(void*));
}

// Expected MSG pt field offset (4P+4 -- the POINT only needs 4-byte
// alignment, so no pad follows time; verified against windows.h: 36 on x64).
int32_t ExpectedMsgPtOffset()
{
    return 4 * PointerSize() + 4;
}

// Expected MSG native size (AlignUp(4P+12, P) -- 48 x64 / 28 x86).
int32_t ExpectedMsgSize()
{
    return AlignUp(4 * PointerSize() + 12, PointerSize());
}

} // namespace

int main()
{
    const int32_t P = PointerSize();

    // --- 1. Size oracle ---
    Expect(il2cpp::il2cpp_pinvoke_struct_size(il2cpp::kPinvokeStructPoint) == 8,
           "POINT native size is 8 bytes");
    Expect(il2cpp::il2cpp_pinvoke_struct_size(il2cpp::kPinvokeStructRect) == 16,
           "RECT native size is 16 bytes");
    Expect(il2cpp::il2cpp_pinvoke_struct_size(il2cpp::kPinvokeStructMsg) == ExpectedMsgSize(),
           "MSG native size is 48 (x64) / 28 (x86)");

    // --- 2. Field layout oracle ---
    {
        const il2cpp::Il2CppPinvokeStructField* pt = il2cpp::il2cpp_pinvoke_struct_field_layout(il2cpp::kPinvokeStructPoint);
        Expect(il2cpp::il2cpp_pinvoke_struct_field_count(il2cpp::kPinvokeStructPoint) == 2, "POINT has 2 fields");
        Expect(pt[0].offset == 0 && pt[0].size == 4, "POINT.x at offset 0 size 4");
        Expect(pt[1].offset == 4 && pt[1].size == 4, "POINT.y at offset 4 size 4");

        const il2cpp::Il2CppPinvokeStructField* rc = il2cpp::il2cpp_pinvoke_struct_field_layout(il2cpp::kPinvokeStructRect);
        Expect(il2cpp::il2cpp_pinvoke_struct_field_count(il2cpp::kPinvokeStructRect) == 4, "RECT has 4 fields");
        Expect(rc[0].offset == 0 && rc[0].size == 4, "RECT.left at offset 0 size 4");
        Expect(rc[1].offset == 4 && rc[1].size == 4, "RECT.top at offset 4 size 4");
        Expect(rc[2].offset == 8 && rc[2].size == 4, "RECT.right at offset 8 size 4");
        Expect(rc[3].offset == 12 && rc[3].size == 4, "RECT.bottom at offset 12 size 4");

        const il2cpp::Il2CppPinvokeStructField* msg = il2cpp::il2cpp_pinvoke_struct_field_layout(il2cpp::kPinvokeStructMsg);
        Expect(il2cpp::il2cpp_pinvoke_struct_field_count(il2cpp::kPinvokeStructMsg) == 6, "MSG has 6 fields");
        Expect(msg[0].offset == 0 && msg[0].size == P, "MSG.hwnd at offset 0, pointer-width");
        Expect(msg[1].offset == P && msg[1].size == 4, "MSG.message at offset P, size 4");
        Expect(msg[2].offset == 2 * P && msg[2].size == P, "MSG.wParam at offset 2P, pointer-width");
        Expect(msg[3].offset == 3 * P && msg[3].size == P, "MSG.lParam at offset 3P, pointer-width");
        Expect(msg[4].offset == 4 * P && msg[4].size == 4, "MSG.time at offset 4P, size 4");
        Expect(msg[5].offset == ExpectedMsgPtOffset() && msg[5].size == 8, "MSG.pt at offset 4P+4, size 8");
    }

    // --- 3. RECT by-value round trip ---
    {
        struct ManagedRect { int32_t left, top, right, bottom; };
        ManagedRect src = { 1, 2, 3, 4 };
        ManagedRect dst = { 0, 0, 0, 0 };
        int32_t native[4] = { 0, 0, 0, 0 };

        il2cpp::il2cpp_pinvoke_struct_to_native(il2cpp::kPinvokeStructRect, &src, native);
        Expect(native[0] == 1 && native[1] == 2 && native[2] == 3 && native[3] == 4,
               "RECT to_native copies the four int32 fields in order");

        native[1] = 99; // simulate the native callee writing top
        il2cpp::il2cpp_pinvoke_struct_from_native(il2cpp::kPinvokeStructRect, native, &dst);
        Expect(dst.left == 1 && dst.top == 99 && dst.right == 3 && dst.bottom == 4,
               "RECT from_native copies native bytes back into the managed struct");
    }

    // --- 4. GetWindowRect-form simulation ---
    {
        struct ManagedRect { int32_t left, top, right, bottom; };
        int32_t nativeRect[4] = { 0, 0, 0, 0 };
        // The "native" callee: GetWindowRect fills a RECT {left=10, top=20,
        // right=310, bottom=220}.
        nativeRect[0] = 10;
        nativeRect[1] = 20;
        nativeRect[2] = 310;
        nativeRect[3] = 220;

        ManagedRect managed = { 0, 0, 0, 0 };
        il2cpp::il2cpp_pinvoke_struct_from_native(il2cpp::kPinvokeStructRect, nativeRect, &managed);
        Expect(managed.left == 10 && managed.top == 20 && managed.right == 310 && managed.bottom == 220,
               "GetWindowRect simulation: native-filled RECT marshals into the managed struct");
        Expect(managed.right - managed.left == 300 && managed.bottom - managed.top == 200,
               "GetWindowRect simulation: width/height derive from the marshalled RECT");
    }

    // --- 5. POINT round trip ---
    {
        il2cpp::Il2CppPinvokePoint src = { 7, -3 };
        il2cpp::Il2CppPinvokePoint dst = { 0, 0 };
        il2cpp::Il2CppPinvokePoint native = { 0, 0 };
        il2cpp::il2cpp_pinvoke_struct_to_native(il2cpp::kPinvokeStructPoint, &src, &native);
        Expect(native.x == 7 && native.y == -3, "POINT to_native copies x/y");
        native.x = 42;
        il2cpp::il2cpp_pinvoke_struct_from_native(il2cpp::kPinvokeStructPoint, &native, &dst);
        Expect(dst.x == 42 && dst.y == -3, "POINT from_native copies native bytes back");
    }

    // --- 6. MSG: full-size value round trips byte-exact ---
    {
        const int32_t size = il2cpp::il2cpp_pinvoke_struct_size(il2cpp::kPinvokeStructMsg);
        unsigned char* buf = new unsigned char[static_cast<size_t>(size)];
        unsigned char* round = new unsigned char[static_cast<size_t>(size)];
        for (int32_t i = 0; i < size; ++i) buf[i] = static_cast<unsigned char>(i + 1);

        il2cpp::il2cpp_pinvoke_struct_from_native(il2cpp::kPinvokeStructMsg, buf, round);
        Expect(std::memcmp(buf, round, static_cast<size_t>(size)) == 0,
               "MSG native -> managed -> (bytes) is byte-exact");

        unsigned char* back = new unsigned char[static_cast<size_t>(size)];
        il2cpp::il2cpp_pinvoke_struct_to_native(il2cpp::kPinvokeStructMsg, round, back);
        Expect(std::memcmp(buf, back, static_cast<size_t>(size)) == 0,
               "MSG to_native(managed) reproduces the source native bytes");

        delete[] back;
        delete[] round;
        delete[] buf;
    }

    // --- 7. kind_by_name lookup ---
    {
        Expect(il2cpp::il2cpp_pinvoke_struct_kind_by_name("System.Drawing.Point") == il2cpp::kPinvokeStructPoint,
               "name 'System.Drawing.Point' resolves to kPinvokeStructPoint");
        Expect(il2cpp::il2cpp_pinvoke_struct_kind_by_name("System.Drawing.Rectangle") == il2cpp::kPinvokeStructRect,
               "name 'System.Drawing.Rectangle' resolves to kPinvokeStructRect");
        Expect(il2cpp::il2cpp_pinvoke_struct_kind_by_name("System.Windows.Forms.Message") == il2cpp::kPinvokeStructMsg,
               "name 'System.Windows.Forms.Message' resolves to kPinvokeStructMsg");
        Expect(il2cpp::il2cpp_pinvoke_struct_kind_by_name("System.Drawing.Widget") == -1,
               "unknown name resolves to -1 (never a default kind)");
        Expect(il2cpp::il2cpp_pinvoke_struct_kind_by_name(nullptr) == -1,
               "null name resolves to -1");
    }

    // --- 8. Windows: cross-validation against the real Win32 layout ---
#if defined(_WIN32)
    Expect(sizeof(POINT) == 8, "windows.h POINT is 8 bytes");
    Expect(sizeof(RECT) == 16, "windows.h RECT is 16 bytes");
    Expect(offsetof(RECT, left) == 0 && offsetof(RECT, top) == 4
           && offsetof(RECT, right) == 8 && offsetof(RECT, bottom) == 12,
           "windows.h RECT field offsets match the registry");
    Expect(sizeof(MSG) == static_cast<size_t>(ExpectedMsgSize()),
           "windows.h MSG size matches the registry (48 x64 / 28 x86)");
    Expect(offsetof(MSG, hwnd) == 0 && offsetof(MSG, message) == static_cast<size_t>(P),
           "windows.h MSG hwnd/message offsets match the registry");
    Expect(offsetof(MSG, wParam) == static_cast<size_t>(2 * P)
           && offsetof(MSG, lParam) == static_cast<size_t>(3 * P),
           "windows.h MSG wParam/lParam offsets match the registry");
    Expect(offsetof(MSG, time) == static_cast<size_t>(4 * P)
           && offsetof(MSG, pt) == static_cast<size_t>(ExpectedMsgPtOffset()),
           "windows.h MSG time/pt offsets match the registry");

    // --- 8b. Windows: exclusion cross-check (WNDCLASS/DEVMODE are NOT in
    // the registry -- they are by-ADDRESS passthrough structs, not blittable
    // by-value marshalling shapes). These asserts document the empirical
    // Win32 facts behind the header's exclusion rationale, so a future
    // change that tries to register them has a hard oracle to prove the
    // layout is NOT a simple sequential registry candidate:
    //   * WNDCLASSW carries a WNDPROC function pointer (lpfnWndProc@8) --
    //     a callback, not a marshalled field (pinvokecb.cpp bridges it).
    //   * DEVMODEW is non-blittable: sizeof=220 and the dmPosition union
    //     overlaps the dmOrientation/dmPaperSize scalar group at offset 76.
    Expect(sizeof(WNDCLASSW) == 72, "windows.h WNDCLASSW is 72 bytes (x64)");
    Expect(offsetof(WNDCLASSW, lpfnWndProc) == 8,
           "windows.h WNDCLASSW.lpfnWndProc is a pointer at offset 8");
    Expect(sizeof(DEVMODEW) == 220, "windows.h DEVMODEW is 220 bytes (x64)");
    Expect(offsetof(DEVMODEW, dmOrientation) == 76
           && offsetof(DEVMODEW, dmPosition) == 76,
           "windows.h DEVMODEW union: dmOrientation/dmPosition share offset 76");
    // The registry's managed-name lookup must NEVER resolve these
    // by-address structs (they have no registered kind -- resolving them
    // would emit a memcpy marshall of a non-blittable layout).
    Expect(il2cpp::il2cpp_pinvoke_struct_kind_by_name("System.Windows.Forms.CreateParams") == -1,
           "by-address-only WNDCLASS carrier resolves to -1");
#endif

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d pinvokestruct assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pinvokestruct tests passed\n");
    return 0;
}
