// mylib.cpp -- native library for the delegategfp case: a callback function
// that CALLS a managed delegate marshaled via GetFunctionPointerForDelegate.
#include <cstdint>

extern "C" {

// 1-arg callback: fn(x).
int32_t call_callback(int32_t (*fn)(int32_t), int32_t x)
{
    return fn(x);
}

} // extern "C"