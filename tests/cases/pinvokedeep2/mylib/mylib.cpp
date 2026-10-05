// mylib.cpp -- native library for the pinvokedeep2 case: callback bridge +
// native struct-layout authority. Exported with C linkage so the transpiled
// import declaration binds.
//
// Layout notes (x64 Windows, g++ AND MSVC agree):
//   NativePoint { int32 x, y; }                       -> 8 bytes, y@4
//   NativeRect  { int32 x, y, width, height; }        -> 16 bytes, height@12
//   NativeMixed { int32 a; int64 b; int16 c; }        -> 24 bytes, b@8, c@16
// The registered managed family (System.Drawing.Point/Rectangle) uses the
// same layout, so the native constants must equal Marshal.SizeOf/OffsetOf.

#include <cstdint>

extern "C" {

struct NativePoint { int32_t x, y; };
struct NativeRect  { int32_t x, y, width, height; };
struct NativeMixed { int32_t a; int64_t b; int16_t c; };

int32_t native_point_size(void)      { return static_cast<int32_t>(sizeof(NativePoint)); }
int32_t native_rect_size(void)       { return static_cast<int32_t>(sizeof(NativeRect)); }
int32_t native_mixed_size(void)      { return static_cast<int32_t>(sizeof(NativeMixed)); }
int32_t native_point_off_y(void)     { return static_cast<int32_t>(reinterpret_cast<size_t>(&((NativePoint*)0)->y)); }
int32_t native_rect_off_height(void) { return static_cast<int32_t>(reinterpret_cast<size_t>(&((NativeRect*)0)->height)); }
int32_t native_mixed_off_b(void)     { return static_cast<int32_t>(reinterpret_cast<size_t>(&((NativeMixed*)0)->b)); }

// 1-arg callback: fn(x).
int32_t call_callback(int32_t (*fn)(int32_t), int32_t x)
{
    return fn(x);
}

// Two invocations summed: fn(a) + fn(b).
int32_t call_callback_twice(int32_t (*fn)(int32_t), int32_t a, int32_t b)
{
    return fn(a) + fn(b);
}

// 2-arg callback: fn(a, b).
int32_t call_callback2(int32_t (*fn)(int32_t, int32_t), int32_t a, int32_t b)
{
    return fn(a, b);
}

// Struct-offset route: the managed side passes a Marshal.OffsetOf value,
// the native side pushes it through the callback: fn(off) + 1.
int32_t struct_field_callback(int32_t (*fn)(int32_t), int32_t off)
{
    return fn(off) + 1;
}

} // extern "C"
