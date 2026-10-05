// mylib.cpp -- native library for the pinvokedelegate case: functions that
// RECEIVE a managed delegate (marshaled to a native fn pointer by the P-D2 #4
// bridge) and CALL it back. Exported with C linkage so the transpiled import
// declaration binds.
#include <cstdint>

extern "C" {

// 1-arg callback: fn(x).
int32_t call_with_delegate(int32_t (*fn)(int32_t), int32_t x)
{
    return fn(x);
}

// 2-arg callback: fn(a, b).
int32_t call_with_delegate2(int32_t (*fn)(int32_t, int32_t), int32_t a, int32_t b)
{
    return fn(a, b);
}

} // extern "C"
